# HyTranslateService

A local-first, privacy-preserving multilingual translation service — with a built-in Web UI and an **MCP** interface, running the Hunyuan translation model (Hy-MT2) on your own machine.

---

[中文](./README.md) | English

## Why this project

Tencent open-sourced [Hunyuan Translation Hy-MT2](https://github.com/Tencent-Hunyuan/Hy-MT2), a family of high-quality multilingual translation models, and [sdcb/HyMT2Sharp](https://github.com/sdcb/HyMT2Sharp) provides a **pure C#, cross-platform (CPU / GPU)** inference kernel that loads and runs Hy-MT2 GGUF models directly on .NET.

Both are closer to a library and low-level API than to something you can just use. This project wraps HyMT2Sharp into a **ready-to-use local translation service**:

- **Web UI**: translate text or files with a few clicks;
- **MCP (Model Context Protocol) interface**: connects to Claude, Cursor, VS Code, or any other MCP-capable client, turning "high-quality local translation" into a capability of those tools;
- **Local-first and privacy-preserving**: data never leaves the machine, no API key, no network access required after the model has been downloaded once.

---

## Design decisions and trade-offs

The sections below describe decisions that affect how you use the service.

This project is developed and maintained by **Guipu Studio (归朴工作室)**, hosted at [github.com/am1235/HyTranslateService](https://github.com/am1235/HyTranslateService).

### 1. Origin: writing an MCP server by hand, but not only for AI

> The project started as an exercise: I wanted to build a **standard MCP server** end to end — tool registration, argument injection, progress notifications, client cancellation — and understand that path rather than just run an SDK sample. (The current implementation is stateless Streamable HTTP: the protocol no longer requires an `initialize` handshake and the server creates no session; an `initialize` from an older client is still answered by the SDK, but no session is created.)

Serving only AI clients was not enough, so the same backend is split by protocol family:

- **MCP peers with HTTP**: MCP is a protocol entry point alongside the WebUI and HTTP clients, not an adapter bolted onto REST afterwards;
- **REST + SignalR reuse the same backend**: the WebUI and HTTP clients submit over REST and receive live progress over SignalR, on top of the same `TranslationJobManager` / `ModelHost` / `ModelRegistry` that the MCP tools depend on;
- The job model (cancellable, reports progress, holds exclusive resources) is defined in `TranslationJobManager`, and every entry point calls its `TryStart` directly; the WebUI, HTTP clients and AI clients all get exactly the same translation capabilities and constraints.

### 2. Trade-off: local-first means one job at a time

The most important limitation of this project is that **only one job runs at a time**. This is a deliberate choice for local deployment:

- **Resources are finite**. The model stays resident in RAM (or VRAM), and a local machine has far less of both than a cloud server. Instead of building scheduling, preemption and queuing, the rule is simply "one machine, one model, one job".
- **A model instance is a single-threaded consumer**. The underlying model holds a KV cache and is not thread-safe; feeding one set of weights from several jobs in parallel is neither safe nor useful.
- **The backend therefore enforces serialization with a "non-blocking exclusive gate"** (see `Translation/ModelHost.cs`):
  - `ModelHost` holds the model resource with a `SemaphoreSlim(1,1)`;
  - a new request tries the gate through `TryAcquire` (a single `Wait(0)`) and is **told "busy" immediately if it fails**: no queuing, no waiting, and the server does not retry automatically;
  - only **one** model is ever resident in memory. Switching models (including switching the engine / KV cache policy) means unloading the old one before loading the new one, which rules out concurrency as well.
- **The outside sees the same "reject when busy" behavior**: whether you come in through the WebUI, REST, SignalR or MCP, a new job is rejected immediately while one is running — as HTTP `409`, a `SignalR error` event, or an error raised at the MCP layer. All entry points share one `TranslationJobManager`, so the behavior is consistent by construction.
- **The gate is returned with the job, and the model can be unloaded automatically**: `using (lease)` / `ModelLease.Dispose()` returns the gate when the job ends; the outer `finally` only records the job in `_lastFinished`, clears `_active`, and disposes the previous job's cancellation source. If the model stays idle longer than the configured number of minutes (`ModelIdleUnloadMinutes`) with no new requests, it is unloaded and its VRAM / RAM is released.

`TranslationJobManager` keeps just one object, the "current job". There is no queue and no job history.

For a "local, single-GPU, privacy-first" scenario, this buys a minimal concurrency model, lower resident memory use, and the same "busy" semantics at every entry point.

### 3. Resuming after a refresh: disconnect stops events, the server runs to a terminal state

Refreshing the page loses progress, and translating again wastes local compute. Reliability lives on the server; the complexity of resuming lives in the frontend:

- **A disconnect only stops the event stream; the job is not aborted**. Whether you refresh, close the tab, or lose the connection, the background translation job keeps running on the machine to a **terminal state** (completed / cancelled / failed). The browser observes the job; it does not decide whether the job continues.
- **The frontend reconnects**. On page load, `resumeCheck()` (defined in `app.js`, called by `HyTransApp.start()`) probes `/api/translation/current?brief=1`, then dispatches by job "kind" (`Kind` is exposed in both snapshots and takes the values `inline` / `file` / `mcp` / `mcp-file`):
  - **busy + file** → the large-file dialog takes over with `resume()` and shows progress;
  - **busy + inline** → the main window polls the full `current` snapshot once per second;
  - **busy + mcp** → polls `brief=1` only (lightweight, no full snapshot), because an MCP client should not be held up by long polling;
  - **idle with stashed state** → a confirmation dialog asks whether to restore the previous translation (stored in `sessionStorage` as `hytrans.job.v1`, containing `jobId` / `kind` / `sourceName`; `inline` also holds the full source text `source`, `file` does not; for `inline`, `jobId` is filled in from the first streaming event). When fetching a finished result, `file` goes through `HyBigFileModal.recover()` and `inline` through `recoverInlineResult()`.
- Two preconditions:
  - **There is a race between the terminal state and clearing the current job**: after the server reports idle, fetching the translation through `GET /api/translation/file-result?job=` must check `saved.jobId === state.jobId`; `state.resumeKind` is also used to tell whether the job was started by MCP (MCP results take a different retrieval path).
  - **No per-segment events during loading**: while the model loads, the only signal is the snapshot's `note` field reporting that loading is in progress, so a polling client **can only rely on `note`** to tell "loading" from "stuck" — the `loading` event and the snapshot `note` are both kept deliberately and must not be removed.

The backend keeps no long-lived connection or session state; jobs run statelessly to completion and resumption is left to polling and client-side stashing.

### 4. Whitespace fidelity: line breaks and indentation never reach the model

Translation models change extra spaces, indentation, blank lines and line breaks. That hardly matters for ordinary prose, but it breaks the layout of code blocks, tables, poetry and aligned text. This project enforces a verifiable invariant (see `Translation/TextSegmenter.cs`):

> **Σ(segment Prefix + Text + Separator) === the original text, character for character.** Reassembling the leading whitespace, body text and trailing whitespace of every segment must reproduce the original exactly.

Formatting is separated from semantics:

- **Split line by line; segments never cross a line**. A line break is a natural boundary, so the model never sees half a line.
- **Structural whitespace is not sent to the model**. Line breaks, indentation, tabs and runs of spaces are stripped into `Prefix` / `Separator` at the two ends of each segment and restored verbatim after translation.
- **Only a "single space" is translated with the text**: it joins words (as in English); all other structural whitespace is restored by deterministic rules.
- **In the event stream, `Separator` is carried only by `segment_done`**; the frontend must keep it and append it, or formatting is lost. The full snapshot also carries `segments[].separator` back to the frontend, which is what resuming after a refresh uses to rebuild the original.

The translated text therefore keeps the same layout as the source: blank lines, indentation, Markdown code blocks and list structure all survive. Split/restore rules guarantee formatting; the model handles only semantics.

---

What these four sections have in common: the uncertainty of translation quality is left to the model, while everything that deterministic rules can settle — how capabilities are exposed, scheduling, resumption and format restoration — is handled by rules.

---

## Features

- **38 target languages**, with automatic source language detection;
- **Local inference**: built on HyMT2Sharp in pure C#; CPU works out of the box, GPU uses Vulkan on Windows / Linux and Metal on macOS. The default `auto` probes for a GPU and falls back to CPU if unavailable; an explicit `gpu` reports an error when no GPU is available;
- **Automatic segmentation of long text**: long input is split into segments and translated in sequence, with no manual work;
- **Streaming progress and mid-job cancellation**: progress is pushed over SignalR, and a translation can be stopped partway through;
- **File translation**: translates local plain-text files (`.txt` / `.md` / `.srt` / `.csv` / `.json` / `.log`, etc.), preserving paragraph and line-break structure;
- **Automatic model download**: selecting an official model that has not been downloaded pulls it from Hugging Face automatically (switching to a mirror when access is restricted);
- **MCP server**: a standard Streamable HTTP implementation with 6 tools that any MCP client can connect to directly;
- **Glossary / style control**: a glossary and target style can be specified to keep proper nouns consistent and match the desired tone.

---

## Quick start

### 1. Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Operating system: Windows / Linux / macOS
- At least one usable inference backend: CPU (built in, no extra dependency); GPU: Vulkan runtime / driver on Windows / Linux, system Metal on macOS; `auto` falls back to CPU when no GPU is detected

### 2. Build and run

```bash
# Clone the repository
git clone https://github.com/am1235/HyTranslateService.git
cd HyTranslateService

# Run (default port 8086)
dotnet run --project HyTranslateService/HyTranslateService.csproj -c Release
```

Then open **<http://localhost:8086>** in a browser.

### 3. Download a model

Model files are roughly 0.43 GB to 1.78 GB and are needed on first use, but there is no separate download step: after starting the service, pick a model from the "Translation model" dropdown in the Web UI. Anything not yet downloaded is pulled from Hugging Face automatically (switching to a mirror when access is restricted), and is ready to use once the download finishes.

Model files live in `./models/` under the **current working directory (CWD)** (created automatically at startup if missing; override with `--models-dir` / `HYTRANSLATE_MODELS_DIR`). `wwwroot` and the language packs are resolved against the web host's ContentRoot / WebRoot (the CWD by default), so start the service with the application project directory as the working directory.

---

## Usage

> By protocol family there are only **two entry points**: HTTP (WebUI and any HTTP client, including REST and SignalR) and MCP.

### Option 1: Web UI

Open <http://localhost:8086>, paste the source text → choose a target language → click translate. File upload, glossary and style settings are supported.

### Option 2: Connect other clients over MCP

The service exposes a standard Streamable HTTP endpoint at **`http://localhost:8086/mcp`**. Any MCP-capable client can connect directly.

**The 6 tools:**

| Tool                           | Description                                                        |
| ------------------------------ | ------------------------------------------------------------------ |
| `translate_text`             | Translate a piece of text (auto-segmented, streaming progress, cancellable) |
| `translate_file`             | Translate a text file on the service's machine                       |
| `list_translation_models`    | List available models (including whether each is resident in memory)  |
| `list_target_languages`      | List supported target languages                                      |
| `get_current_translation`    | Query the current job's progress / result                             |
| `cancel_current_translation` | Stop the running translation job                                     |

> **Note:** Only one translation job runs at a time. If a job is already running, a new request reports an error immediately with details of the current job; query or cancel it before retrying.

**Client configuration snippets** (pick one, add it to the corresponding config file, and replace `url` with your service address):

- **Claude / Cursor** (`claude_desktop_config.json` / `~/.cursor/mcp.json`):
  ```json
  {
    "mcpServers": {
      "hytranslate": {
        "type": "streamableHttp",
        "url": "http://localhost:8086/mcp"
      }
    }
  }
  ```
- **VS Code** (in `settings.json`):
  ```json
  {
    "mcp": {
      "servers": {
        "hytranslate": {
          "type": "http",
          "url": "http://localhost:8086/mcp"
        }
      }
    }
  }
  ```

Once connected, you can ask Claude / Cursor / VS Code in natural language to "translate this...", and they call the service's MCP tools to do the translation locally.

### Option 3: HTTP API (REST + real-time SignalR)

The service is a standard HTTP server exposing **two complementary interfaces that are meant to be used together**:

- **REST**: request/response operations — submit / query / cancel / fetch results — plus one-shot synchronous translation;
- **SignalR** (`/signalr/translation`): **real-time push** of the translation process — per-segment progress and incremental output.

Both share the same backend jobs (`TranslationJobManager`), and only one job runs at a time. Two typical flows:

**Inline text (streaming)**

1. The client calls `Translate(request)` over SignalR; the server pushes the `JobEvent` stream only to the **calling** connection: `preparing → planned → loading → started → segment / delta / segment_done → done / cancelled / error` (`started` is pushed as a hardcoded string in `TranslationJob.cs`; `JobEventTypes` has no matching constant);
2. If SignalR is unavailable, it falls back automatically to `POST /api/translation` (one-shot, no streaming progress);
3. On refresh or disconnect the event stream stops, but **the background job keeps running** — poll `GET /api/translation/current?brief=1` to resume seamlessly; stop it with `POST /api/translation/cancel` (the job does not have to be started from the current page).

**File translation (large text)**

1. `POST /api/translation/file` (`multipart/form-data`) returns a `jobId` immediately; the file part is identified by the presence of `filename`, the field name is conventionally `file` (the server does not validate `name`), and translation arguments are passed as form fields (for example `targetLanguage`);
2. Poll `GET /api/translation/current?brief=1` for progress (always use `brief=1` for large files — it is O(1) and lightweight);
3. After the job finishes (completed / cancelled), download the translation with `GET /api/translation/file-result?job=`; call `POST /api/translation/cancel` at any point to stop it.

#### REST endpoints (base `http://localhost:8086`)

| Method & path                            | Description                                                             |
| ---------------------------------------- | ----------------------------------------------------------------------- |
| `GET /api/translation/languages`        | List the 38 target languages (code / English and Chinese names / native name) |
| `POST /api/translation`                 | One-shot translation: waits for the whole text and returns the translation plus statistics; 409 when busy (includes `currentJobId`) |
| `POST /api/translation/file`            | Upload a file for translation, returns a `jobId` immediately              |
| `GET /api/translation/file-result?job=` | Download the translation of a completed job (text attachment)             |
| `GET /api/translation/current?brief=1`  | Query current job progress; returns `{ "idle": true }` when idle          |
| `POST /api/translation/cancel`          | Stop the current job (completed segments are kept)                        |
| `GET /api/system/info`                  | Service capabilities and defaults (engines, KV policies, models directory, MCP endpoint, etc.) |
| `GET /api/language/list`                | UI language pack list (not the same as "target languages")                |
| `GET /api/models`                       | Model list (5 official + local extras, with downloaded / resident flags)   |
| `POST /api/models/refresh`              | Rescan the models directory                                               |
| `POST /api/models/download`             | Start downloading a specified official model (mirror on restricted access) |
| `GET /api/models/download`              | Query download progress (poll `progress`)                                 |
| `POST /api/models/download/cancel`      | Cancel a download                                                         |

`POST /api/translation` request body example:

```json
{
  "text": "Hello, world!",
  "targetLanguage": "zh",
  "model": "auto",
  "engine": "auto",
  "kvCache": "memory"
}
```

#### SignalR real-time channel (`/signalr/translation`)

- **Client → server**: `Translate(request)` (start a streaming translation, same arguments as `POST /api/translation`), `Cancel()` (stop the current job);
- **Server → client**: pushed only to the calling connection, event types in order `preparing → planned → loading → started → segment / delta / segment_done → done / cancelled / error` (`started` is pushed as a hardcoded string in `TranslationJob.cs`; `JobEventTypes` has no matching constant).

> **Note:** SignalR's `Translate` only streams progress for **inline text**; file translation uses the REST flow above (submit → poll → download). Only one job runs at a time, and REST returns 409 when busy. A client disconnect does not abort the background job; stopping it requires an explicit `Cancel`. Size limits: 128 KB for inline text and 32 MB for file uploads by default (adjust with `--max-text-bytes` / `--max-file-bytes` or the matching `HYTRANSLATE_*` environment variables; validation happens at the API layer only).

---

## Models

The official catalog contains 5 quantized Hunyuan Hy-MT2 1.8B variants. Approximate sizes come from `ApproximateSizeBytes` in `Translation/ModelCatalog.cs` and are displayed by `FormatSize` using 1024-based units; the Notes column comes from `ModelCatalog.Description` as selection guidance (the decoding speed figure for 1.25Bit is a claim from the upstream model card). No benchmarks were run in this repository, so these figures are for reference only.

| Model                    | Quantization | Approx. size | Notes                                                        |
| ------------------------ | ------------ | ------------ | ------------------------------------------------------------ |
| Hy-MT2-1.8B-1.25Bit    | 1.25Bit      | ~0.43 GB     | Fastest; upstream model card claims ~74% faster decoding, with slight quality loss |
| Hy-MT2-1.8B-2Bit       | Q2_0C        | ~0.56 GB     | Balance of speed and quality                                 |
| **Hy-MT2-1.8B-Q4_K_M** | Q4_K_M       | ~1.06 GB     | **Speed/quality balance, default recommendation**             |
| Hy-MT2-1.8B-Q6_K       | Q6_K         | ~1.37 GB     | Higher precision; size and quality between Q4_K_M and Q8_0    |
| Hy-MT2-1.8B-Q8_0       | Q8_0         | ~1.78 GB     | Highest quality, largest size                                 |

> The recommendation is computed at runtime: `RecommendedId()` picks the highest-quality variant among downloaded models; only when none has been downloaded does it fall back to Q4_K_M from the official catalog.

---

## Configuration

The server **does not read or write any configuration file**. Arguments have exactly two sources: the command line and `HYTRANSLATE_*` environment variables (environment variables are applied first, then the command line takes precedence). For the full list, see `--help`. Commonly used arguments:

| Argument | Default | Description |
| --- | --- | --- |
| `--host` / `--port` | `127.0.0.1` / `8086` | Listen address; remember the service has no authentication before changing it to `0.0.0.0`. Only when host/port are **not** explicitly specified does it honor `ASPNETCORE_URLS` and the address injected from `launchSettings.json` by `dotnet run` or an IDE (that file has no effect when running the published output directly, and `mcpEndpoint` in `/api/system/info` is still computed from `--port`) |
| `--engine` | `auto` | `auto` probes for a GPU and falls back to CPU; `gpu` forces GPU and reports an error if unavailable; `cpu` is CPU-only |
| `--kv-cache` | `memory` | `memory` keeps the buffer after a job ends; `none` releases it immediately, without affecting the translation |
| `--models-dir` | `models` | Resolved relative to the current working directory |
| `--model-idle-unload` | `5` | Minutes of idle time before unloading the model; `0` disables it |
| `--segment-chars` | `480` | Maximum characters per segment |
| `--max-text-bytes` | `131072` | Inline text limit (128 KB); exceeding it is an error — split the text or use file upload |
| `--max-file-bytes` | `33554432` | File upload limit (32 MB) |
| `--no-file-tools` | enabled unless passed | Disables the MCP `translate_file` call; the tool is still listed in `tools/list` and calls return `isError`. WebUI file upload is unaffected |
| `--browser` | `false` | Open a browser automatically after startup |

Environment variables share the `HYTRANSLATE_` prefix; the key names are defined by `AppOptionsParser.ApplyEnv`: `MODELS_DIR` / `HOST` / `PORT` / `THREADS` / `ENGINE` / `KV_CACHE` / `PRELOAD` / `MODEL_IDLE_UNLOAD` / `DEFAULT_MODEL` / `DEFAULT_TARGET` / `DEFAULT_SOURCE` / `SEGMENT_CHARS` / `MAX_TEXT_BYTES` / `MAX_FILE_BYTES` / `LOG_LEVEL`. `--no-file-tools` and `--browser` have no matching environment variable and can only be set on the command line; model downloads can use a separate `HYTRANSLATE_HTTP_PROXY` proxy.

---

## Directory layout

```
HyTranslateService/
├── HyTranslateService/        # ASP.NET Core service: WebUI + REST + SignalR + MCP
│   ├── wwwroot/              # Frontend (HTML / JS / CSS / UI language packs)
│   ├── Translation/          # Translation jobs, model host, model catalog, downloader
│   ├── Tools/                # MCP tool implementations
│   └── models/               # Downloaded GGUF models (created in the CWD at runtime, not a fixed repo path)
├── HyMT2Sharp/               # Inference kernel (vendored source, not NuGet, referenced as a project)
└── HyTranslateService.slnx   # Solution
```

> `HyMT2Sharp` is included in the repository as source (vendored 2.0.0) and compiled through a ProjectReference rather than installed from NuGet, so the whole project builds from a single solution.

---

## Acknowledgments

- Thanks to **[sdcb/HyMT2Sharp](https://github.com/sdcb/HyMT2Sharp)**: this project directly depends on its pure C#, cross-platform Hy-MT2 inference kernel.

---

## License

This project uses the **Apache License 2.0**. See the [`LICENSE`](./LICENSE) file in the repository root.

- The source code in this repository (ASP.NET Core service, WebUI, MCP tools, etc.) is released under Apache-2.0, **Copyright 2026 归朴工作室 (Guipu Studio)**;
- The inference kernel **[sdcb/HyMT2Sharp](https://github.com/sdcb/HyMT2Sharp)** (vendored 2.0.0) is also Apache-2.0 (Copyright sdcb). This repository includes it **as source** under `HyMT2Sharp/`; its license text is in [`HyMT2Sharp/LICENSE`](./HyMT2Sharp/LICENSE), and the local modification made here (the 1 GiB VRAM heap threshold for `CoherentDeviceLocal` in the Vulkan backend) is recorded in [`HyMT2Sharp/NOTICE`](./HyMT2Sharp/NOTICE);
- The Hunyuan translation model **[Tencent-Hunyuan/Hy-MT2](https://github.com/Tencent-Hunyuan/Hy-MT2)** is open-sourced by Tencent Hunyuan, also under Apache-2.0. Follow its repository license when using the model.

> Because `HyMT2Sharp/` is third-party source shipped with this repository (including our local patch), keep its `LICENSE` and `NOTICE` files and do not fold its copyright into this repository's Apache-2.0 notice.

> **Note:** Make sure your use of this project complies with the Hunyuan model license and related policies.

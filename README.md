# HyTranslateService

本地优先、隐私安全的多语种翻译服务 —— 自带 Web 界面与 **MCP** 接口，让混元翻译模型（Hy-MT2）跑在你自己的机器上。

---

中文 | [English](./README.en.md)

## 为什么做这个项目

腾讯开源的 [混元翻译 Hy-MT2](https://github.com/Tencent-Hunyuan/Hy-MT2) 是一组高质量的多语种翻译模型，而 [sdcb/HyMT2Sharp](https://github.com/sdcb/HyMT2Sharp) 则提供了**纯 C#、跨平台（CPU / GPU）** 的推理内核，可以直接在 .NET 上加载并运行 Hy-MT2 的 GGUF 模型。

但它们本身更偏向“库”和“底层调用”。为了让更多人能真正用起来，本项目把 HyMT2Sharp 包装成一个**开箱即用的本地翻译服务**：

- **带 Web 界面**，点点就能翻译文本或文件；
- **暴露 MCP（Model Context Protocol）接口**，可以接入 Claude、Cursor、VS Code 等任意支持 MCP 的客户端，把“本地高质量翻译”变成这些工具的一项能力；
- **本地优先、隐私安全**：数据不出本机，无需 API Key，无需联网（首次下载模型后即可完全离线使用）。

---

## 设计理念与取舍

下面几节写的是几个会影响使用方式的取舍。

本项目由 **归朴工作室** 开发与维护，代码托管于 [github.com/am1235/HyTranslateService](https://github.com/am1235/HyTranslateService)。

### 1. 缘起：想亲手写一个 MCP 服务，但不只服务 AI

> 这个项目的种子，其实是一个「练手」的念头——我想从头把一套**标准 MCP 服务**写通：工具注册、参数注入、进度推送、客户端取消，把这条链路摸清楚，而不是只跑通 SDK 的示例。（当前实现为无状态 Streamable HTTP：协议层不再强制 `initialize` 握手，服务端不建会话；旧客户端发来的 `initialize` 仍会被 SDK 应答，但握完不建会话。）

只给 AI 客户端用不够，于是把同一套后端按协议族分流：

- **MCP 与 HTTP 平级**：MCP 是与 WebUI、HTTP 客户端并列的协议入口，不是后加到 REST 上的适配器；
- **REST + SignalR 复用同一套后端**：WebUI 与 HTTP 客户端通过 REST 提交、SignalR 接收实时进度，底层是 MCP 工具所依赖的 `TranslationJobManager` / `ModelHost` / `ModelRegistry`；
- 任务模型（可取消、可推进度、独占资源）定义在 `TranslationJobManager`，各入口都直接调它的 `TryStart`；WebUI、HTTP 客户端与 AI 客户端拿到的翻译能力与约束完全一致。

### 2. 取舍：本地优先，所以「同一时刻只跑一个任务」

本项目最重要的限制是「同一时刻只跑一个任务」，这是针对本地部署做的主动选择：

- **资源有限是前提**。模型常驻在内存（或显存）里，本地机器的显存 / 内存远没有云服务器宽裕；省掉多任务调度、抢占与排队，直接约定「一台机器、一个模型、一个任务」。
- **模型实例本身就是单线程消费者**。底层模型持有 KV 缓存、且非线程安全，多个任务并行喂同一份权重既不安全也无收益。
- **因此后端用一把「非阻塞独占闸门」来强制串行**（见 `Translation/ModelHost.cs`）：
  - `ModelHost` 内部用 `SemaphoreSlim(1,1)` 把模型资源锁住；
  - 新请求通过 `TryAcquire`（一次 `Wait(0)`）尝试拿闸门，**拿不到立刻被告知「忙」**：不排队、不等待，服务端也不会自动重试；
  - 内存中始终只常驻**一个**模型，要换模型（含切换引擎 / KV 缓存策略）就得先把旧的卸载再加载，进一步决定了无法并发。
- **对外的表现是一致的「忙则拒绝」**：无论你从 WebUI、REST、SignalR 还是 MCP 进来，只要已有任务在跑，新任务都会被立即拒绝——分别对应 HTTP `409`、`SignalR error` 事件、以及 MCP 层抛出的错误。所有入口共用同一个 `TranslationJobManager`，所以行为天然一致。
- **闸门随任务归还，模型可自动卸载**：任务结束时由 `using (lease)` / `ModelLease.Dispose()` 归还闸门；外层 `finally` 只负责把任务记入 `_lastFinished`、清空 `_active`、释放上一任务的取消源。空闲超过设定分钟数（`ModelIdleUnloadMinutes`）且无新请求时，常驻模型会被自动卸载，释放显存 / 内存。

`TranslationJobManager` 只维护「当前任务」这一个对象，没有队列，也没有任务历史。

针对「本地、单卡、隐私优先」场景，这么做换来极简的并发模型、更低的内存常驻，以及各入口一致的「忙」语义。

### 3. 刷新接续：断开只停推送，服务端跑到终态

刷新页面会丢进度，重新翻译又浪费本地算力。可靠性放在服务端，接续的复杂度放在前端：

- **断开只停事件推送，任务不中止**。无论刷新页面、关掉标签页，还是网络抖动掉线，后台翻译任务都会在本机**继续跑到终态**（完成 / 取消 / 失败）。浏览器只负责观察，不决定任务是否继续。
- **前端负责「续上」**。页面加载时 `resumeCheck()`（定义在 `app.js`，由 `HyTransApp.start()` 触发）先向 `/api/translation/current?brief=1` 探活，再按任务的「种类」分流接续（种类 `Kind` 随两个快照透出，取值 `inline` / `file` / `mcp` / `mcp-file`）：
  - **忙 + file** → 大文件弹窗 `resume()` 接续显示进度；
  - **忙 + inline** → 主窗口每秒轮询全量 `current`；
  - **忙 + mcp** → 只轮询 `brief=1`（轻量，不拉全量），因为 MCP 客户端不该被长轮询拖住；
  - **空闲但有暂存** → 弹确认框，让用户决定是否把上次译文恢复出来（暂存于 `sessionStorage` 的 `hytrans.job.v1`，含 `jobId` / `kind` / `sourceName`，`inline` 还含原文全文 `source`、`file` 不含；其中 `inline` 的 `jobId` 由首个流式事件补上）。补下载时 `file` 走 `HyBigFileModal.recover()`，`inline` 走 `recoverInlineResult()`。
- 两处前提：
  - **终态与「当前任务清空」之间有竞态**：空闲后用 `GET /api/translation/file-result?job=` 补取译文时，必须校验 `saved.jobId === state.jobId`；并用 `state.resumeKind` 判断任务是否由 MCP 发起（MCP 的译文取回路径不同）。
  - **加载期没有逐段事件**：模型加载期间只会通过快照的 `note` 字段报「正在加载」，所以轮询端**只能靠 `note`** 知道卡在加载——`loading` 事件与快照 `note` 都刻意保留，不能删。

后端不维护长连接或会话状态，任务无状态跑到结束，接续交给轮询与暂存。

### 4. 空白符保真：换行 / 缩进不进模型

翻译模型会改掉多余的空格、缩进、空行和换行。普通段落影响不大，代码块、表格、诗歌、对齐文本的排版却会因此错乱。这里定了一条可验证的硬不变式（见 `Translation/TextSegmenter.cs`）：

> **Σ(片段 Prefix + Text + Separator) === 原文（逐字符相等）。** 把所有片段的前导空白、正文、尾部空白拼回去，必须和原文一字不差。

实现上把「格式」从「语义」里剥离开来：

- **逐行拆分，片段绝不跨行**：换行本身就是天然边界，模型不会看到半行句子；
- **结构性空白不送模型**：换行、缩进、Tab、连续空格一律剥离，归入每段两端的 `Prefix` / `Separator`，翻译完成后原样回填；
- **只有「单个空格」随文翻译**：用于连接词与词（如英文的空格），其余结构性空白全部交给确定性规则还原；
- **事件流里 `Separator` 仅由 `segment_done` 携带**，前端必须原样保留并拼回，否则就会丢格式；全量快照的 `segments[].separator` 同样带回前端，刷新接续靠它还原原文。

译文排版因此与原文一致：空行、缩进、Markdown 代码块与列表结构都保留。格式由切分/回填规则保证，模型只处理语义。

---

这四节的共同做法是：翻译质量的不确定性留给模型，能用确定性规则解决的暴露方式、调度、接续与格式还原则收口到规则里。

---

## 特性

- **38 种目标语言**，支持自动识别源语言；
- **本地推理**：基于 HyMT2Sharp，纯 C# 实现；CPU 自带，GPU 在 Windows / Linux 走 Vulkan、macOS 走 Metal。默认 `auto` 自动探测、不可用时回落 CPU，显式 `gpu` 不可用即报错；
- **长文本自动拆段**：超长文本会被自动切成片段依次翻译，无需手动处理；
- **流式进度 + 中途取消**：通过 SignalR 实时回传进度，翻译到一半也能随时停；
- **文件翻译**：直接翻译本地纯文本文件（`.txt` / `.md` / `.srt` / `.csv` / `.json` / `.log` 等），保留段落与换行结构；
- **模型自动下载**：选择未下载的官方模型即可自动从 Hugging Face 拉取（受限时自动切换镜像）；
- **MCP 服务**：标准 Streamable HTTP 实现，6 个工具，任意 MCP 客户端可直连；
- **术语表 / 风格控制**：可指定术语表与目标风格，提升专有名词一致性和语气契合度。

---

## 快速开始

### 1. 环境要求

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- 操作系统：Windows / Linux / macOS
- 至少一种可用的推理后端：CPU（自带，无需额外依赖）；GPU：Windows / Linux 需 Vulkan 运行时 / 驱动，macOS 用系统自带 Metal；`auto` 探测不到 GPU 时自动回落 CPU

### 2. 构建与运行

```bash
# 克隆仓库
git clone https://github.com/am1235/HyTranslateService.git
cd HyTranslateService

# 运行（默认端口 8086）
dotnet run --project HyTranslateService/HyTranslateService.csproj -c Release
```

启动后打开浏览器访问：**<http://localhost:8086>**

### 3. 下载模型

模型文件约 0.43 GB ~ 1.78 GB，首次使用需获取，但不用单独下载：运行后在 Web 界面的「翻译模型」下拉里选一个，未下载的会由服务自动从 Hugging Face 拉取（访问受限时切换镜像），完成后即可翻译。

模型文件保存在**当前工作目录（CWD）**下的 `./models/`（不存在则启动时自动创建，可用 `--models-dir` / `HYTRANSLATE_MODELS_DIR` 覆盖）。`wwwroot` 与语言包按 Web 宿主的 ContentRoot / WebRoot 解析（默认即 CWD），请以应用工程目录为工作目录启动。

---

## 使用方式

> 按协议族算，对外只有**两个入口**：HTTP（WebUI 与任意 HTTP 客户端，含 REST 与 SignalR）和 MCP。

### 方式一：Web 界面

打开 <http://localhost:8086> ，粘贴原文 → 选择目标语言 → 点击翻译即可。支持文件上传、术语表、风格设置等。

### 方式二：通过 MCP 接入其他客户端

服务在 **`http://localhost:8086/mcp`** 暴露标准 Streamable HTTP 端点。任何支持 MCP 的客户端都可直接连接。

**提供的 6 个工具：**

| 工具                           | 说明                      |
| ---------------------------- | ----------------------- |
| `translate_text`             | 翻译一段文本（自动拆段、流式进度、可中途取消） |
| `translate_file`             | 翻译本机上的一个文本文件            |
| `list_translation_models`    | 列出可用模型（含是否已常驻内存）        |
| `list_target_languages`      | 列出支持的目标语言               |
| `get_current_translation`    | 查询当前任务进度 / 结果           |
| `cancel_current_translation` | 终止正在运行的翻译任务             |

> ⚠️ 同一时刻仅执行一个翻译任务。若已有任务在跑，新请求会直接报错并提示当前任务，可先查询或取消再重试。

**客户端配置片段**（选一种放入对应配置文件，将 `url` 改成你的服务地址）：

- **Claude / Cursor**（配置文件：`claude_desktop_config.json` / `~/.cursor/mcp.json`）：
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
- **VS Code**（放入 `settings.json`）：
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

接入后，即可在 Claude / Cursor / VS Code 中直接用自然语言让它"翻译这段……"，由它们调用本服务的 MCP 工具完成本地翻译。

### 方式三：HTTP 接口（REST + SignalR 实时通讯）

服务本身是一个标准 HTTP 服务，对外同时提供**两套互补的接口，需要配合使用**：

- **REST**：负责"提交 / 查询 / 取消 / 取结果"这类请求—响应操作，以及一步到位的同步翻译；
- **SignalR**（`/signalr/translation`）：负责**实时推送**翻译过程——逐段进度与增量译文。

两者共用同一套后端任务（`TranslationJobManager`），同一时刻只跑一个任务。典型的两种配合方式：

**① 内联文本（随写随译）**

1. 客户端通过 SignalR 调用 `Translate(request)`，服务端把 `JobEvent` 事件流只推给**发起方**连接：`preparing → planned → loading → started → segment / delta / segment_done → done / cancelled / error`（`started` 由 `TranslationJob.cs` 硬编码推送，`JobEventTypes` 无对应常量）；
2. 若 SignalR 不可用，自动退化为 `POST /api/translation`（一步到位，无流式进度）；
3. 页面刷新或掉线时，事件推送会断、但**后台任务不会停**——此时用 `GET /api/translation/current?brief=1` 轮询进度即可无缝接续；停止用 `POST /api/translation/cancel`（不要求任务由本页发起）。

**② 文件翻译（大文本）**

1. `POST /api/translation/file`（`multipart/form-data`）立即返回 `jobId`；文件段按是否带 `filename` 识别，字段名约定为 `file`（服务端不校验 `name`），翻译参数作为表单字段传入（如 `targetLanguage`）；
2. 轮询 `GET /api/translation/current?brief=1` 看进度（大文件请务必用 `brief=1`，O(1) 轻量）；
3. 结束（完成 / 终止）后 `GET /api/translation/file-result?job=` 下载译文；中途可 `POST /api/translation/cancel` 停止。

#### REST 端点一览（基址 `http://localhost:8086`）

| 方法 & 路径                                 | 说明                                            |
| --------------------------------------- | --------------------------------------------- |
| `GET /api/translation/languages`        | 列出 38 种目标语言（code / 中英文名 / 母语名）                |
| `POST /api/translation`                 | 一步到位翻译：等待整篇完成返回译文与统计；忙时 409（含 `currentJobId`） |
| `POST /api/translation/file`            | 上传文件翻译，立即返回 `jobId`                           |
| `GET /api/translation/file-result?job=` | 下载已完成任务的译文（文本附件）                              |
| `GET /api/translation/current?brief=1`  | 查询当前任务进度；空闲返回 `{ "idle": true }`              |
| `POST /api/translation/cancel`          | 停止当前任务（已译部分保留）                                |
| `GET /api/system/info`                  | 服务能力与默认值（引擎、KV 策略、模型目录、MCP 端点等）              |
| `GET /api/language/list`                | 界面语言包清单（与「目标语言」不是一回事）                        |
| `GET /api/models`                       | 模型清单（官方 5 个 + 本地额外，含是否已下载 / 常驻）               |
| `POST /api/models/refresh`              | 重新扫描模型目录                                       |
| `POST /api/models/download`             | 触发指定官方模型下载（受限自动走镜像）                           |
| `GET /api/models/download`              | 查询下载进度（轮询 `progress`）                         |
| `POST /api/models/download/cancel`      | 取消下载                                          |

`POST /api/translation` 请求体示例：

```json
{
  "text": "Hello, world!",
  "targetLanguage": "zh",
  "model": "auto",
  "engine": "auto",
  "kvCache": "memory"
}
```

#### SignalR 实时通道（`/signalr/translation`）

- **客户端 → 服务端**：`Translate(request)`（发起流式翻译，参数同 `POST /api/translation`）、`Cancel()`（停止当前任务）；
- **服务端 → 客户端**：只推给发起方连接，事件类型依次为 `preparing → planned → loading → started → segment / delta / segment_done → done / cancelled / error`（`started` 由 `TranslationJob.cs` 硬编码推送，`JobEventTypes` 无对应常量）。

> 注意：SignalR 的 `Translate` 只负责把**内联文本**的进度实时推回；文件翻译走的是上面的 REST 流程（提交 → 轮询 → 下载）。同一时刻只跑一个任务，忙时 REST 返回 409；客户端断开不会中止后台任务，停止需显式调用 `Cancel`。大小上限：内联文本默认 128 KB，文件上传默认 32 MB（可用 `--max-text-bytes` / `--max-file-bytes` 或对应 `HYTRANSLATE_*` 环境变量调整；校验只在接口层）。

---

## 模型说明

官方目录固定 5 个混元 Hy-MT2 1.8B 量化版本。近似大小取自 `Translation/ModelCatalog.cs` 的 `ApproximateSizeBytes`，由 `FormatSize` 按 1024 进位显示；特点列取自 `ModelCatalog.Description` 的选购参考文案（其中 1.25Bit 的解码提速数据为上游模型卡宣称），本仓库未做基准测试，仅供参考。

| 模型                     | 量化      | 近似大小     | 特点                            |
| ---------------------- | ------- | -------- | ----------------------------- |
| Hy-MT2-1.8B-1.25Bit    | 1.25Bit | ~0.43 GB | 最快；上游模型卡称 decode 速度约提升 74%，质量略有损失 |
| Hy-MT2-1.8B-2Bit       | Q2_0C   | ~0.56 GB | 速度与质量的折中选择                    |
| **Hy-MT2-1.8B-Q4_K_M** | Q4_K_M  | ~1.06 GB | **速度质量平衡，默认推荐**                |
| Hy-MT2-1.8B-Q6_K       | Q6_K    | ~1.37 GB | 更高精度，体积与质量介于 Q4_K_M 与 Q8_0 之间 |
| Hy-MT2-1.8B-Q8_0       | Q8_0    | ~1.78 GB | 最高质量，体积最大                     |

> 「推荐」在运行期是动态的：`RecommendedId()` 取已下载模型中质量最高的一档；一个都没下载时才回退到官方目录的 Q4_K_M。

---

## 配置

服务端**不读写任何配置文件**，参数只有两个入口：命令行与 `HYTRANSLATE_*` 环境变量（环境变量先应用，命令行优先）。完整清单以 `--help` 为准，常用参数：

| 参数 | 默认值 | 说明 |
| --- | --- | --- |
| `--host` / `--port` | `127.0.0.1` / `8086` | 监听地址；改 `0.0.0.0` 前先想清楚服务没有认证。仅当**未**显式指定 host/port 时才尊重 `ASPNETCORE_URLS` 与 `dotnet run` / IDE 由 `launchSettings.json` 注入的地址（直接运行发布产物时该文件不生效，且 `/api/system/info` 的 `mcpEndpoint` 仍按 `--port` 计算） |
| `--engine` | `auto` | `auto` 探测 GPU、失败回落 CPU；`gpu` 强制 GPU、不可用直接报错；`cpu` 纯 CPU |
| `--kv-cache` | `memory` | `memory` 任务结束保留缓冲；`none` 结束即释放，不影响译文 |
| `--models-dir` | `models` | 相对当前工作目录解析 |
| `--model-idle-unload` | `5` | 空闲多少分钟卸载模型，`0` 禁用 |
| `--segment-chars` | `480` | 单段最大字符数 |
| `--max-text-bytes` | `131072` | 内联文本上限（128 KB）；超限会报错，需拆分或改用文件上传 |
| `--max-file-bytes` | `33554432` | 文件上传上限（32 MB） |
| `--no-file-tools` | 不带即启用 | 停用 MCP 的 `translate_file`；工具仍在 `tools/list`，调用返回 `isError`。不影响 WebUI 文件上传 |
| `--browser` | `false` | 启动后自动打开浏览器 |

环境变量统一前缀 `HYTRANSLATE_`，键名以 `AppOptionsParser.ApplyEnv` 为准：`MODELS_DIR` / `HOST` / `PORT` / `THREADS` / `ENGINE` / `KV_CACHE` / `PRELOAD` / `MODEL_IDLE_UNLOAD` / `DEFAULT_MODEL` / `DEFAULT_TARGET` / `DEFAULT_SOURCE` / `SEGMENT_CHARS` / `MAX_TEXT_BYTES` / `MAX_FILE_BYTES` / `LOG_LEVEL`。`--no-file-tools` 与 `--browser` 没有对应环境变量，只能命令行指定；模型下载代理可另设 `HYTRANSLATE_HTTP_PROXY`。

---

## 目录结构

```
HyTranslateService/
├── HyTranslateService/        # ASP.NET Core 服务：WebUI + REST + SignalR + MCP
│   ├── wwwroot/              # 前端（HTML / JS / CSS / 多语言包）
│   ├── Translation/          # 翻译任务、模型宿主、模型目录、下载器
│   ├── Tools/                # MCP 工具实现
│   └── models/               # 下载的 GGUF 模型（运行时在 CWD 自动创建，非仓库固定路径）
├── HyMT2Sharp/               # 推理内核（vendored 源码，非 NuGet，直接作为工程引用）
└── HyTranslateService.slnx   # 解决方案
```

> 说明：`HyMT2Sharp` 以源码形式内置在仓库中（vendored 2.0.0），通过工程引用（ProjectReference）参与编译，而非从 NuGet 安装，因此整个项目以单一解决方案即可构建。

---

## 致谢


- 感谢 **[sdcb/HyMT2Sharp](https://github.com/sdcb/HyMT2Sharp)**：本项目直接依赖其纯 C#、跨平台的 Hy-MT2 推理内核。

---

## 许可证

本项目采用 **Apache License 2.0**。详见仓库根目录的 [`LICENSE`](./LICENSE) 文件。

- 本仓库的源代码（ASP.NET Core 服务、WebUI、MCP 工具等）在 Apache-2.0 协议下发布，**Copyright 2026 归朴工作室**；
- 推理内核 **[sdcb/HyMT2Sharp](https://github.com/sdcb/HyMT2Sharp)**（vendored 2.0.0）同样以 Apache-2.0 开源（Copyright sdcb）。本仓库以**源码形式**将其内置在 `HyMT2Sharp/` 目录，其许可证文本见 [`HyMT2Sharp/LICENSE`](./HyMT2Sharp/LICENSE)，本仓库对其所作的本地修改（Vulkan 后端 `CoherentDeviceLocal` 的 1 GiB 显存门槛）记录在 [`HyMT2Sharp/NOTICE`](./HyMT2Sharp/NOTICE)；
- 混元翻译模型 **[Tencent-Hunyuan/Hy-MT2](https://github.com/Tencent-Hunyuan/Hy-MT2)** 由腾讯混元开源，本身同样以 Apache-2.0 发布，使用模型时请遵循其仓库协议。

> 因 `HyMT2Sharp/` 是随仓库发布的第三方源码（含本仓库的本地补丁），请保留其中的 `LICENSE` 与 `NOTICE`，不要将其版权并入本仓库的 Apache-2.0 声明。

> 提示：使用本项目翻译时，请确保你的使用方式符合混元模型的许可与相关政策。

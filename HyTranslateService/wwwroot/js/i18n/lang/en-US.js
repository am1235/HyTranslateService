/* @i18n-lang {"code":"en-US","nativeName":"English","englishName":"English","order":3} */
/**
 * English language pack (en-US)
 *
 * Self-describing: the @i18n-lang header on the first line declares this pack's
 * code, display names and ordering. The server scans this directory at startup
 * and builds /api/language/list from those headers; the front-end then loads the
 * packs dynamically — so adding a language means dropping a file in here.
 * Usage: sets window.I18N_LANGS['en-US']; i18n-config.js merges it into the
 *        Layui i18n message table (keeping Layui's built-in strings intact).
 */
(function () {
    var pack = {
        langNames: {
            'zh': 'Chinese', 'en': 'English', 'fr': 'French', 'pt': 'Portuguese', 'es': 'Spanish',
            'ja': 'Japanese', 'tr': 'Turkish', 'ru': 'Russian', 'ar': 'Arabic', 'ko': 'Korean',
            'th': 'Thai', 'it': 'Italian', 'de': 'German', 'vi': 'Vietnamese', 'ms': 'Malay',
            'id': 'Indonesian', 'tl': 'Filipino', 'hi': 'Hindi', 'zh-Hant': 'Traditional Chinese', 'pl': 'Polish',
            'cs': 'Czech', 'nl': 'Dutch', 'km': 'Khmer', 'my': 'Burmese', 'fa': 'Persian',
            'gu': 'Gujarati', 'ur': 'Urdu', 'te': 'Telugu', 'mr': 'Marathi', 'he': 'Hebrew',
            'bn': 'Bengali', 'ta': 'Tamil', 'uk': 'Ukrainian', 'bo': 'Tibetan', 'kk': 'Kazakh',
            'mn': 'Mongolian', 'ug': 'Uyghur', 'yue': 'Cantonese'
        },
        app: {
            name: 'HyTrans',
            slogan: 'Local Hy-MT2 translation · CPU / GPU inference · Fully offline',
            copy: 'Copy',
            confirm: 'OK',
            connecting: 'Connecting…',
            connected: 'Connected',
            disconnected: 'Disconnected'
        },
        header: {
            mcp: 'MCP setup',
            api: 'WebAPI guide',
            themeMode: 'Light / Dark',
            home: 'Project Homepage'
        },
        input: {
            title: 'Source text',
            placeholder: 'Paste or type the content to translate, or drag a text file into this area.\n\nLong texts are automatically split into segments by paragraph and sentence, then streamed back as they are translated.',
            charCount: '{count} chars'
        },
        option: {
            title: 'Translation settings',
            sourceLang: 'Source language',
            targetLang: 'Target language',
            model: 'Model',
            engine: 'Inference engine',
            engineHint: 'auto = use a GPU when available and fall back to CPU if it fails to load; GPU = require a graphics card and error out instead of degrading.',
            kvCache: 'KV cache',
            kvCacheHint: 'Affects memory use and warm-up speed only; the translation is unchanged.',
            autoDetect: 'Auto detect',
            loaded: 'Loaded',
            reloadModels: 'Rescan models',
            downloaded: '(Downloaded)',
            modelHint: 'Selecting an undownloaded model downloads it automatically from Hugging Face (falls back to the hf-mirror.com mirror if access is restricted).',
            advanced: 'Advanced options',
            resetAdvanced: 'Restore defaults',
            segmentChars: 'Segment size (max chars)',
            segmentCharsHint: 'Range 80 – {max}. Smaller saves memory, larger keeps more context.',
            maxTokensHint: '0 = let the model decide; higher yields longer output but takes more time.',
            promptStyleHint: 'Different prompt templates noticeably change tone and term coverage.',
            promptLanguageHint: 'Language of the prompt; keep English for non-Chinese models.',
            temperatureHint: 'Higher = more creative, lower = more deterministic; translation prefers low.',
            topPHint: 'Nucleus sampling: draw only from the top-p probability mass to control diversity.',
            topKHint: 'Restrict to the top-K most likely tokens; 0 disables this limit.',
            repetitionPenaltyHint: '>1 reduces repetition, <1 encourages it; too high feels stiff.',
            backgroundHint: 'Add domain / scenario context so the model understands the whole passage.',
            targetStyleHint: 'Pick multiple and customize; styles combine.',
            maxTokens: 'Max tokens per segment',
            promptStyle: 'Prompt template',
            promptLanguage: 'Prompt language',
            temperature: 'Temperature',
            topP: 'Nucleus sampling top_p',
            topK: 'top_k',
            repetitionPenalty: 'Repetition penalty',
            glossary: 'Glossary',
            glossaryHint: 'One "source=target" per line; greatly improves terminology consistency.',
            glossaryPlaceholder: 'Hunyuan=混元\nMCP=Model Context Protocol',
            background: 'Background / document context',
            backgroundPlaceholder: 'This is a technical document about product XX, written for engineers…',
            targetStyle: 'Target style (used with the style template)',
            targetStylePlaceholder: 'e.g. colloquial, formal written, concise technical',
            targetStyleCustomPlaceholder: 'Custom style…',
            add: 'Add'
        },
        action: {
            translate: 'Translate',
            stop: 'Stop',
            clear: 'Clear text',
            bigFile: 'File translation',
            swap: 'Swap languages',
            copyResult: 'Copy translation',
            download: 'Save translation .txt'
        },
        output: {
            title: 'Translation',
            ready: 'Ready',
            placeholder: 'The translation appears here, word by word, in real time',
            placeholderSub: 'Auto segmentation · streaming output · stop anytime',
            translating: 'Translating…',
            segments: '{done} / {total} segments',
            elapsed: 'elapsed {time}',
            eta: 'remaining {time}',
            speed: '{value} chars/s'
        },
        status: {
            idle: 'Idle',
            preparing: 'Preparing',
            loading: 'Loading model',
            running: 'Translating',
            completed: 'Completed',
            cancelled: 'Stopped',
            failed: 'Failed'
        },
        style: {
            short: 'Short instruction (default)',
            official: 'Official default template',
            terminology: 'Terminology template',
            style: 'Style template',
            colloquial: 'Colloquial',
            formal: 'Formal written',
            techdoc: 'Concise technical',
            delimiters: 'Keep delimiters (subtitles/code)',
            background: 'Background-information template'
        },
        engine: {
            auto: 'Auto (use GPU if available)',
            gpu: 'GPU',
            cpu: 'CPU'
        },
        kvCache: {
            none: 'Do not cache (lowest memory)',
            memory: 'Keep for next (faster)'
        },
        promptLang: {
            en: 'English',
            zh: '中文'
        },
        msg: {
            noText: 'Please enter or choose something to translate first.',
            noModel: 'No model downloaded yet: pick one in the "Translation model" dropdown to download it automatically (requires access to Hugging Face or its mirror).',
            started: 'Translation started',
            cancelRequested: 'Stopping the translation…',
            cancelled: 'The current translation has been stopped',
            textTooLong: 'The text exceeds the per-request size limit; please split it into smaller batches.',
            failed: 'Translation failed: {message}',
            loadFailed: 'Failed to load: {message}',
            confirmClear: 'Clear both the source text and the translation?',
            confirmResetAdvanced: 'Restore the advanced options to their defaults?',
            advancedReset: 'Advanced options restored to defaults',
            cleared: 'Cleared',
            swapNeedsSource: 'Cannot swap while the source language is set to auto. Please pick a source language first.',
            nothingToCopy: 'There is nothing to copy.',
            copied: 'Copied to clipboard',
            copyFailed: 'Copy failed, please select the text manually.',
            nothingToDownload: 'There is nothing to download.',
            modelsRefreshed: 'Model list updated: {count} model(s).',
            langSwitched: 'Language switched',
            fileTooLarge: 'The dropped file is too large to load directly into the editor.',
            fileReadFailed: 'Failed to read the file: {message}',
            busyTitle: 'Model busy',
            busyConfirm: 'The model is busy, so this translation did not start. The current task {jobId} is still running — stop it and start this translation?',
            busyKept: 'The current task was kept; this translation did not start.',
            resumed: 'An ongoing translation task was detected; progress display has been resumed',
            resumeAskInline: 'The previous translation task has finished. Restore its source text and translation?',
            resumeAskFile: 'The previous file translation has finished. Download the translated text ({name})?',
            resumeGone: 'The previous result is no longer on the server (only the most recent task is kept).',
            resumeRestored: 'Source text and translation of the previous task restored',
            resumeMcpDone: 'The resumed translation task has finished; its result was delivered via the original entry (MCP).'
        },
        model: {
            downloadTitle: 'Download model',
            downloadDone: '{name} downloaded and ready to use.',
            downloadFailed: 'Model download failed: {message}',
            downloadBusy: 'Another model download is already in progress; please wait or cancel it.',
            downloading: 'Downloaded {size} ({percent})',
            probing: 'Checking download source (huggingface.co / hf-mirror.com)…',
            preparing: 'Preparing download…',
            downloadOk: 'Download completed!',
            cancelled: 'Download cancelled.',
            cancelDownload: 'Cancel download',
            notDownloaded: 'Model "{name}" is not fully downloaded yet; please wait for the download to finish.'
        },
        job: {
            title: 'Current task',
            refresh: 'Refresh current task',
            empty: 'No translation is currently running',
            stop: 'Stop',
            busy: 'Server busy (current task {jobId})',
            hint: 'Only one task runs at a time (only one model is kept in memory): new translations are rejected while the current task is running — stop the current task first.'
        },
        mcp: {
            title: 'Connect this service to an AI client',
            endpoint: 'MCP endpoint (Streamable HTTP)',
            tools: 'Available tools',
            toolList: 'List every translation model found under ./models',
            toolLanguages: 'List the 38 supported target languages',
            toolTranslateText: 'Translate text: auto segmentation + streaming progress + cancellable',
            toolTranslateFile: 'Translate a text file on this machine',
            toolCurrent: 'Query the current task status / stop the current task',
            footer: 'These tools share the exact same translation core as the WebUI.',
            clients: 'Client configuration (Claude / VS Code / Cursor)',
            clientsHint: 'Copy the snippet below and paste it into the config file for your client. Claude and Cursor use the exact same format — only the file location differs.',
            clientCommon: 'Claude / Cursor (generic mcpServers format)',
            clientVscode: 'VS Code (settings.json format)',
            clientClaudePath: 'Put it in claude_desktop_config.json',
            clientVscodePath: 'Put it under mcp.servers in VS Code settings',
            clientCursorPath: 'Put it in ~/.cursor/mcp.json',
            faq: 'FAQ & Troubleshooting',
            faqTargetLangTitle: 'Use camelCase for parameter names',
            faqTargetLang: 'Tool parameters match the C# parameter names and use camelCase (e.g. targetLanguage). Do NOT use snake_case (target_language), otherwise the server reports a missing required parameter.',
            faqSessionTitle: 'No Session-Id in handshake is normal',
            faqSession: 'This service is stateless: the initialize handshake response does not include an Mcp-Session-Id header. This is expected behavior — no special handling is required and the client can call tools normally.'
        },
        api: {
            title: 'WebAPI & Realtime Channel Guide',
            intro: 'Service address (all paths below are relative to it):',
            rest: 'WebAPI (REST / HTTP)',
            rowSystem: 'System info: version, MCP endpoint, realtime channel path, etc.',
            rowModels: 'List translation models found under ./models (add refresh parameter)',
            rowModelsRefresh: 'Rescan the model directory (new gguf files need no restart)',
            rowLanguages: 'List the 38 supported target languages',
            rowTranslate: 'Synchronous translate: waits for the whole text, returns the {translation} field once',
            rowFileUpload: 'Large file upload translate: multipart upload (file field), returns jobId; poll current for progress',
            rowFileResult: 'Download the translation of a finished task (last finished task kept; 409 while running)',
            rowCurrent: 'Query the current task snapshot (progress + translated text + entry kind); returns {idle:true} when idle',
            rowCancel: 'Stop the current task (translated parts are kept)',
            busy: 'Only one task runs at a time: when busy, translation returns 409 (busy, currentJobId). Query or stop the current task first, then resubmit.',
            exampleRequest: 'Translate request body example (POST /api/translation; text and targetLanguage are required, the rest optional)',
            hub: 'Realtime channel (SignalR)',
            hubPath: 'Hub path (negotiate first to establish the connection)',
            hubServer: 'Server methods: Translate(request) to start, Cancel() to stop the current translation',
            hubEvents: 'Client registers the JobEvent stream: preparing / planned / loading / started / segment / delta / segment_done / done / cancelled / error (pushed only to the initiator)',
            hubSeparator: 'To rebuild the full text you must use the separator carried by segment_done: line breaks, indentation and tabs never enter the model — they are restored on reassembly.',
            footer: 'WebAPI, the realtime channel and MCP tools share the same translation core and concurrency gate.'
        },
        bigfile: {
            title: 'File translation',
            pickHint: 'Pick a local text file to translate: all settings are taken from the main window (languages, model and advanced options, current values). Long texts are segmented and translated sequentially with no segment-count cap.',
            pick: 'Choose file',
            start: 'Start translation',
            stop: 'Stop translation',
            uploading: 'Uploading, please wait…',
            noFile: 'Please choose a file first.',
            tooLarge: 'The file is too large to upload.',
            busy: 'Model busy, this translation did not start: {message}',
            failed: 'Translation failed: {message}',
            progressDone: '{done} / {total} segments',
            elapsed: 'elapsed {time}',
            eta: 'about {time} left',
            finishing: 'Finishing…',
            autoSaving: 'Translation finished, saving to your device…',
            saved: 'Saved to your device: {file}',
            partialNote: '(translation was stopped; the translated part has been saved)',
            stopping: 'Stopping the translation; the translated part will be saved automatically…',
            stopUnavailable: 'There is no task to stop.',
            downloadFailed: 'Failed to fetch the translation result: {message}',
            resumed: 'Resumed the ongoing file translation task.'
        }
    };

    window.I18N_LANGS = window.I18N_LANGS || {};
    window.I18N_LANGS['en-US'] = pack;
})();

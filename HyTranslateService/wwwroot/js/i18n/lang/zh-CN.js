/* @i18n-lang {"code":"zh-CN","nativeName":"简体中文","englishName":"Simplified Chinese","order":1} */
/**
 * 简体中文语言包（zh-CN）
 *
 * 自描述：首行的 @i18n-lang 头声明自己的代码、显示名与排序。
 *         服务启动时扫描本目录、读取该头生成 /api/language/list，
 *         前端再按清单动态装载语言包——所以「新增一门语言」= 往本目录放一个文件。
 * 用法：执行后把文案挂到 window.I18N_LANGS['zh-CN']，
 *       由 i18n-config.js 合并进 Layui 的 i18n 消息表（保留 Layui 内置文案）。
 * 新增语言：复制本文件 → 翻译所有 value → 改文件名、@i18n-lang 头与下面的 key。
 */
(function () {
    var pack = {
        langNames: {
            'zh': '中文', 'en': '英语', 'fr': '法语', 'pt': '葡萄牙语', 'es': '西班牙语',
            'ja': '日语', 'tr': '土耳其语', 'ru': '俄语', 'ar': '阿拉伯语', 'ko': '韩语',
            'th': '泰语', 'it': '意大利语', 'de': '德语', 'vi': '越南语', 'ms': '马来语',
            'id': '印尼语', 'tl': '菲律宾语', 'hi': '印地语', 'zh-Hant': '繁体中文', 'pl': '波兰语',
            'cs': '捷克语', 'nl': '荷兰语', 'km': '高棉语', 'my': '缅甸语', 'fa': '波斯语',
            'gu': '古吉拉特语', 'ur': '乌尔都语', 'te': '泰卢固语', 'mr': '马拉地语', 'he': '希伯来语',
            'bn': '孟加拉语', 'ta': '泰米尔语', 'uk': '乌克兰语', 'bo': '藏语', 'kk': '哈萨克语',
            'mn': '蒙古语', 'ug': '维吾尔语', 'yue': '粤语'
        },
        app: {
            name: 'HyTrans',
            slogan: '本地 Hy-MT2 翻译 · CPU / GPU 推理 · 全程离线',
            copy: '复制',
            confirm: '确定',
            connecting: '连接中…',
            connected: '已连接',
            disconnected: '未连接'
        },
        header: {
            mcp: 'MCP 接入',
            api: 'WebAPI 说明',
            themeMode: '明暗模式',
            home: '项目主页'
        },
        input: {
            title: '原文',
            placeholder: '在此粘贴或输入要翻译的内容，也可将文本文件拖入本区域。\n\n长文本会自动按段落与句子拆分成多个片段依次翻译，并实时流式显示结果。',
            charCount: '{count} 字符'
        },
        option: {
            title: '翻译设置',
            sourceLang: '源语言',
            targetLang: '目标语言',
            model: '翻译模型',
            engine: '推理引擎',
            engineHint: 'auto = 有可用 GPU 就用，加载失败自动回落 CPU；GPU = 强制显卡，不可用时报错不降级。',
            kvCache: 'KV 缓存',
            kvCacheHint: '只影响内存与预热速度，不改变译文。',
            autoDetect: '自动识别',
            loaded: '已加载',
            reloadModels: '重新扫描模型',
            downloaded: '（已下载）',
            modelHint: '选择未下载的模型会自动从 Hugging Face 下载（访问受限时自动切换 hf-mirror.com 镜像站）。',
            advanced: '高级选项',
            resetAdvanced: '恢复默认值',
            segmentChars: '分段字数（单段最大字符）',
            segmentCharsHint: '范围 80 ~ {max}，越小越省内存，越大上下文越完整。',
            maxTokensHint: '0 = 由模型自行决定；越大单次生成越长，耗时也越多。',
            promptStyleHint: '不同的提示词模板会明显影响译文风格与术语命中率。',
            promptLanguageHint: '提示词用哪种语言书写；非中文大模型建议保持 English。',
            temperatureHint: '越高越发散有创意，越低越确定稳定；翻译通常取低值。',
            topPHint: '核采样：只从累计概率达 top_p 的候选词中采样，控制多样性。',
            topKHint: '只考虑概率最高的前 K 个词，0 = 关闭该限制。',
            repetitionPenaltyHint: '>1 抑制重复，<1 鼓励重复；过大易使语句生硬。',
            backgroundHint: '补充领域 / 场景信息，帮助模型理解全文语境。',
            targetStyleHint: '可多选并自定义，多种风格会叠加生效。',
            maxTokens: '单段最大生成 token',
            promptStyle: '提示词模板',
            promptLanguage: '提示词语言',
            temperature: '温度',
            topP: '核采样 top_p',
            topK: 'top_k',
            repetitionPenalty: '重复惩罚',
            glossary: '术语表',
            glossaryHint: '每行一条「原文=译文」，可显著提升专有名词一致性。',
            glossaryPlaceholder: 'Hunyuan=混元\nMCP=模型上下文协议',
            background: '背景信息 / 文档上下文',
            backgroundPlaceholder: '这是一份关于 XX 产品的技术文档，读者是工程师……',
            targetStyle: '目标风格（配合「风格模板」使用）',
            targetStylePlaceholder: '例如：口语化、正式书面、简洁技术文档',
            targetStyleCustomPlaceholder: '自定义风格…',
            add: '添加'
        },
        action: {
            translate: '开始翻译',
            stop: '终止翻译',
            clear: '清空文本',
            bigFile: '文件翻译',
            swap: '交换语言',
            copyResult: '复制译文',
            download: '保存译文 .txt'
        },
        output: {
            title: '译文',
            ready: '就绪',
            placeholder: '翻译结果会在此实时逐字显示',
            placeholderSub: '长文本自动拆段 · 流式输出 · 可随时终止',
            translating: '正在翻译…',
            segments: '{done} / {total} 段',
            elapsed: '已用 {time}',
            eta: '剩余 {time}',
            speed: '{value} 字/秒'
        },
        status: {
            idle: '待翻译',
            preparing: '准备中',
            loading: '加载模型',
            running: '翻译中',
            completed: '已完成',
            cancelled: '已终止',
            failed: '失败'
        },
        style: {
            short: '简洁指令（默认）',
            official: '官方默认模板',
            terminology: '术语模板',
            style: '风格模板',
            colloquial: '口语化',
            formal: '正式书面',
            techdoc: '简洁技术文档',
            delimiters: '分隔符保持（字幕/代码）',
            background: '背景信息模板'
        },
        engine: {
            auto: '自动（有 GPU 就用）',
            gpu: 'GPU（显卡）',
            cpu: 'CPU'
        },
        kvCache: {
            none: '不缓存（最省内存）',
            memory: '保留上次（更快）'
        },
        promptLang: {
            en: 'English',
            zh: '中文'
        },
        msg: {
            noText: '请先输入或选择要翻译的内容。',
            noModel: '尚未下载任何翻译模型：在「翻译模型」下拉中选择一个即可自动下载（需要能访问 Hugging Face 或其镜像站）。',
            started: '翻译已开始',
            cancelRequested: '正在停止翻译…',
            cancelled: '已终止当前翻译任务',
            textTooLong: '文本超过单次大小上限，请拆分后分批翻译。',
            failed: '翻译失败：{message}',
            loadFailed: '加载失败：{message}',
            confirmClear: '确定要清空原文与译文吗？',
            confirmResetAdvanced: '确定要把高级选项恢复为默认值吗？',
            advancedReset: '高级选项已恢复默认',
            cleared: '已清空',
            swapNeedsSource: '源语言为「自动识别」时无法交换，请先指定源语言。',
            nothingToCopy: '没有可复制的内容。',
            copied: '已复制到剪贴板',
            copyFailed: '复制失败，请手动选择文本后复制。',
            nothingToDownload: '没有可下载的内容。',
            modelsRefreshed: '模型列表已更新，共 {count} 个模型。',
            langSwitched: '界面语言已切换',
            fileTooLarge: '拖入的文件过大，无法直接载入编辑框。',
            fileReadFailed: '文件读取失败：{message}',
            busyTitle: '模型正忙',
            busyConfirm: '模型正忙，本次翻译没有开始。当前任务 {jobId} 正在翻译，是否先停止它、再开始本次翻译？',
            busyKept: '已保留当前任务，本次翻译未开始。',
            resumed: '检测到进行中的翻译任务，已接续显示进度',
            resumeAskInline: '检测到上次翻译任务已完成，是否恢复其原文与译文？',
            resumeAskFile: '检测到上次文件翻译已完成，是否下载译文（{name}）？',
            resumeGone: '上次任务的结果已不在服务端（只保留最近一次任务）。',
            resumeRestored: '已恢复上次任务的原文与译文',
            resumeMcpDone: '接续的翻译任务已结束，结果经原入口（MCP）交付。'
        },
        model: {
            downloadTitle: '下载翻译模型',
            downloadDone: '{name} 下载完成，已可在「翻译模型」中选用。',
            downloadFailed: '模型下载失败：{message}',
            downloadBusy: '已有模型下载任务在进行，请先完成或取消。',
            downloading: '已下载 {size}（{percent}）',
            probing: '正在检测下载源（huggingface.co / hf-mirror.com）…',
            preparing: '准备下载…',
            downloadOk: '下载完成！',
            cancelled: '下载已取消。',
            cancelDownload: '取消下载',
            notDownloaded: '模型「{name}」尚未下载完成，请等待下载结束。'
        },
        job: {
            title: '当前任务',
            refresh: '刷新当前任务',
            empty: '当前没有正在执行的翻译任务',
            stop: '停止',
            busy: '服务端正忙（当前任务 {jobId}）',
            hint: '同一时刻只执行一个任务（内存中只载入一个模型）：当前任务执行期间新的翻译会被拒绝，请先停止当前任务。'
        },
        mcp: {
            title: '把本服务接入 AI 客户端',
            endpoint: 'MCP 端点（Streamable HTTP）',
            tools: '可用工具',
            toolList: '列出 ./models 中扫描到的全部翻译模型',
            toolLanguages: '列出 38 种受支持的翻译目标语言',
            toolTranslateText: '翻译文本：自动拆段 + 进度流式输出 + 可随时取消',
            toolTranslateFile: '翻译本机上的文本文件',
            toolCurrent: '查询当前任务状态 / 停止当前任务',
            footer: '上述工具与 WebUI 共用同一套翻译核心，行为完全一致。',
            clients: '客户端配置（Claude / VS Code / Cursor）',
            clientsHint: '复制下面的配置片段，按各客户端说明放入配置文件即可。Claude 与 Cursor 写法完全相同，仅文件位置不同。',
            clientCommon: 'Claude / Cursor（通用 mcpServers 写法）',
            clientVscode: 'VS Code（settings.json 写法）',
            clientClaudePath: '放入配置文件 claude_desktop_config.json',
            clientVscodePath: '放入 VS Code 设置 mcp.servers',
            clientCursorPath: '放入配置文件 ~/.cursor/mcp.json',
            faq: '常见问题与排障',
            faqTargetLangTitle: '参数名用驼峰式',
            faqTargetLang: '工具参数名与 C# 参数名一致，采用驼峰式（如 targetLanguage）；请勿写成下划线式（target_language），否则服务端会判定缺少必填参数而报错。',
            faqSessionTitle: '握手不返回 Session-Id 属正常',
            faqSession: '本服务为无状态实现，initialize 握手的响应头不含 Mcp-Session-Id，属正常表现，客户端无需特殊处理即可正常调用。'
        },
        api: {
            title: 'WebAPI 与实时通道说明',
            intro: '服务地址（下述所有路径均相对此地址）：',
            rest: 'WebAPI（REST / HTTP）',
            rowSystem: '系统信息：版本、MCP 端点、实时通道路径等',
            rowModels: '列出 ./models 扫描到的翻译模型（加 refresh 参数）',
            rowModelsRefresh: '重新扫描模型目录（放入新 gguf 后无需重启）',
            rowLanguages: '列出 38 种受支持的翻译目标语言',
            rowTranslate: '同步翻译：提交后等整篇完成，一次性返回 {translation} 字段',
            rowFileUpload: '大文本文件上传翻译：multipart 上传（file 字段），返回 jobId 后用 current 轮询进度',
            rowFileResult: '下载已完成任务的译文（只保留最近一次结束任务；进行中返回 409）',
            rowCurrent: '查询当前任务快照（进度 + 已译内容 + kind 入口种类）；空闲时返回 {idle:true}',
            rowCancel: '停止当前任务（已译部分保留）',
            busy: '同一时刻只有一个任务：忙时翻译返回 409（busy、currentJobId），可先查询或停止当前任务后重新提交。',
            exampleRequest: '翻译请求体示例（POST /api/translation，text 与 targetLanguage 必填，其余可选）',
            hub: '实时通道（SignalR）',
            hubPath: 'Hub 路径（先 negotiate 建立连接）',
            hubServer: '服务端方法：Translate(request) 发起翻译、Cancel() 停止当前翻译',
            hubEvents: '客户端注册 JobEvent 事件流：preparing / planned / loading / started / segment / delta / segment_done / done / cancelled / error（只推送给发起方）',
            hubSeparator: '拼接全文必须使用 segment_done 事件携带的 separator：换行、缩进、Tab 不进模型，靠回填保真。',
            footer: 'WebAPI、实时通道与 MCP 工具共用同一翻译核心与并发闸门，行为完全一致。'
        },
        bigfile: {
            title: '文件翻译',
            pickHint: '选择本地文本文件上传翻译：翻译设置与主窗口完全一致（语言、模型、高级选项均取当前值）。长文本自动分段依次翻译，段数不限。',
            pick: '选择文件',
            start: '开始翻译',
            stop: '终止翻译',
            uploading: '正在上传，请稍候…',
            noFile: '请先选择要翻译的文件。',
            tooLarge: '文件过大，无法上传。',
            busy: '模型正忙，本次翻译没有开始：{message}',
            failed: '翻译失败：{message}',
            progressDone: '{done} / {total} 段',
            elapsed: '已用 {time}',
            eta: '预计剩余 {time}',
            finishing: '正在收尾…',
            autoSaving: '翻译完成，正在保存到本地…',
            saved: '已保存到本地：{file}',
            partialNote: '（翻译被终止，保存的是已译部分）',
            stopping: '正在停止翻译，停止后将自动保存已译部分…',
            stopUnavailable: '当前没有可停止的任务。',
            downloadFailed: '获取翻译结果失败：{message}',
            resumed: '已接续进行中的文件翻译任务。'
        }
    };

    window.I18N_LANGS = window.I18N_LANGS || {};
    window.I18N_LANGS['zh-CN'] = pack;
})();

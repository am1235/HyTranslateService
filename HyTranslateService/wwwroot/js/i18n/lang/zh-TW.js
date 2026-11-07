/* @i18n-lang {"code":"zh-TW","nativeName":"繁體中文","englishName":"Traditional Chinese","order":2} */
/**
 * 繁體中文語言包（zh-TW）
 *
 * 自描述：首行的 @i18n-lang 頭宣告自己的代碼、顯示名與排序。
 *         服務啟動時掃描本目錄、讀取該頭生成 /api/language/list，
 *         前端再按清單動態裝載語言包——所以「新增一門語言」= 往本目錄放一個檔案。
 * 用法：執行後把文案掛到 window.I18N_LANGS['zh-TW']，
 *       由 i18n-config.js 合併進 Layui 的 i18n 訊息表（保留 Layui 內建文案）。
 * 新增語言：複製本檔案 → 翻譯所有 value → 改檔名、@i18n-lang 頭與下面的 key。
 */
(function () {
    var pack = {
        langNames: {
            'zh': '中文', 'en': '英語', 'fr': '法語', 'pt': '葡萄牙語', 'es': '西班牙語',
            'ja': '日語', 'tr': '土耳其語', 'ru': '俄語', 'ar': '阿拉伯語', 'ko': '韓語',
            'th': '泰語', 'it': '義大利語', 'de': '德語', 'vi': '越南語', 'ms': '馬來語',
            'id': '印尼語', 'tl': '菲律賓語', 'hi': '印地語', 'zh-Hant': '繁體中文', 'pl': '波蘭語',
            'cs': '捷克語', 'nl': '荷蘭語', 'km': '高棉語', 'my': '緬甸語', 'fa': '波斯語',
            'gu': '古吉拉特語', 'ur': '烏爾都語', 'te': '泰盧固語', 'mr': '馬拉地語', 'he': '希伯來語',
            'bn': '孟加拉語', 'ta': '泰米爾語', 'uk': '烏克蘭語', 'bo': '藏語', 'kk': '哈薩克語',
            'mn': '蒙古語', 'ug': '維吾爾語', 'yue': '粵語'
        },
        app: {
            name: 'HyTrans',
            slogan: '本機 Hy-MT2 翻譯 · CPU / GPU 推論 · 全程離線',
            copy: '複製',
            confirm: '確定',
            connecting: '連線中…',
            connected: '已連線',
            disconnected: '未連線'
        },
        header: {
            mcp: 'MCP 接入',
            api: 'WebAPI 說明',
            themeMode: '明暗模式',
            home: '專案主頁'
        },
        input: {
            title: '原文',
            placeholder: '在此貼上或輸入要翻譯的內容，也可將文字檔拖入本區域。\n\n長文字會自動依段落與句子拆分成多個片段依序翻譯，並即時串流顯示結果。',
            charCount: '{count} 字元'
        },
        option: {
            title: '翻譯設定',
            sourceLang: '來源語言',
            targetLang: '目標語言',
            model: '翻譯模型',
            engine: '推理引擎',
            engineHint: 'auto = 有可用 GPU 就用，載入失敗自動回落 CPU；GPU = 強制顯卡，不可用時報錯不降級。',
            kvCache: 'KV 快取',
            kvCacheHint: '只影響記憶體與預熱速度，不改變譯文。',
            autoDetect: '自動辨識',
            loaded: '已載入',
            reloadModels: '重新掃描模型',
            downloaded: '（已下載）',
            modelHint: '選擇未下載的模型會自動從 Hugging Face 下載（存取受限時自動切換 hf-mirror.com 鏡像站）。',
            advanced: '進階選項',
            resetAdvanced: '恢復預設值',
            segmentChars: '分段字數（單段最大字元）',
            segmentCharsHint: '範圍 80 ~ {max}，越小越省記憶體，越大上下文越完整。',
            maxTokensHint: '0 = 由模型自行決定；越大單次生成越長，耗時也越多。',
            promptStyleHint: '不同的提示詞範本會明顯影響譯文風格與術語命中率。',
            promptLanguageHint: '提示詞用哪種語言書寫；非中文大模型建議保持 English。',
            temperatureHint: '越高越發散有創意，越低越確定穩定；翻譯通常取低值。',
            topPHint: '核取樣：只從累計機率達 top_p 的候選詞中取樣，控制多樣性。',
            topKHint: '只考慮機率最高的前 K 個詞，0 = 關閉該限制。',
            repetitionPenaltyHint: '>1 抑制重複，<1 鼓勵重複；過大易使語句生硬。',
            backgroundHint: '補充領域 / 場景資訊，幫助模型理解全文語境。',
            targetStyleHint: '可多選並自訂，多種風格會疊加生效。',
            maxTokens: '單段最大生成 token',
            promptStyle: '提示詞範本',
            promptLanguage: '提示詞語言',
            temperature: '溫度',
            topP: '核取樣 top_p',
            topK: 'top_k',
            repetitionPenalty: '重複懲罰',
            glossary: '術語表',
            glossaryHint: '每行一條「原文=譯文」，可明顯提升專有名詞一致性。',
            glossaryPlaceholder: 'Hunyuan=混元\nMCP=模型上下文協定',
            background: '背景資訊 / 文件上下文',
            backgroundPlaceholder: '這是一份關於 XX 產品的技術文件，讀者是工程師……',
            targetStyle: '目標風格（搭配「風格範本」使用）',
            targetStylePlaceholder: '例如：口語化、正式書面、精簡技術文件',
            targetStyleCustomPlaceholder: '自訂風格…',
            add: '新增'
        },
        action: {
            translate: '開始翻譯',
            stop: '終止翻譯',
            clear: '清空文字',
            bigFile: '檔案翻譯',
            swap: '交換語言',
            copyResult: '複製譯文',
            download: '儲存譯文 .txt'
        },
        output: {
            title: '譯文',
            ready: '就緒',
            placeholder: '翻譯結果會在此即時逐字顯示',
            placeholderSub: '長文字自動拆段 · 串流輸出 · 可隨時終止',
            translating: '正在翻譯…',
            segments: '{done} / {total} 段',
            elapsed: '已用 {time}',
            eta: '剩餘 {time}',
            speed: '{value} 字/秒'
        },
        status: {
            idle: '待翻譯',
            preparing: '準備中',
            loading: '載入模型',
            running: '翻譯中',
            completed: '已完成',
            cancelled: '已終止',
            failed: '失敗'
        },
        style: {
            short: '精簡指令（預設）',
            official: '官方預設範本',
            terminology: '術語範本',
            style: '風格範本',
            colloquial: '口語化',
            formal: '正式書面',
            techdoc: '精簡技術文件',
            delimiters: '分隔符保持（字幕/程式碼）',
            background: '背景資訊範本'
        },
        engine: {
            auto: '自動（有 GPU 就用）',
            gpu: 'GPU（顯卡）',
            cpu: 'CPU'
        },
        kvCache: {
            none: '不快取（最省記憶體）',
            memory: '保留上次（更快）'
        },
        promptLang: {
            en: 'English',
            zh: '中文'
        },
        msg: {
            noText: '請先輸入或選擇要翻譯的內容。',
            noModel: '尚未下載任何翻譯模型：在「翻譯模型」下拉中選擇一個即可自動下載（需要能存取 Hugging Face 或其鏡像站）。',
            started: '翻譯已開始',
            cancelRequested: '正在停止翻譯…',
            cancelled: '已終止當前翻譯任務',
            textTooLong: '文字超過單次大小上限，請拆分後分批翻譯。',
            failed: '翻譯失敗：{message}',
            loadFailed: '載入失敗：{message}',
            confirmClear: '確定要清空原文與譯文嗎？',
            confirmResetAdvanced: '確定要將進階選項恢復為預設值嗎？',
            advancedReset: '進階選項已恢復預設',
            cleared: '已清空',
            swapNeedsSource: '來源語言為「自動辨識」時無法交換，請先指定來源語言。',
            nothingToCopy: '沒有可複製的內容。',
            copied: '已複製到剪貼簿',
            copyFailed: '複製失敗，請手動選取文字後複製。',
            nothingToDownload: '沒有可下載的內容。',
            modelsRefreshed: '模型清單已更新，共 {count} 個模型。',
            langSwitched: '介面語言已切換',
            fileTooLarge: '拖入的檔案過大，無法直接載入編輯框。',
            fileReadFailed: '檔案讀取失敗：{message}',
            busyTitle: '模型忙碌',
            busyConfirm: '模型忙碌，本次翻譯未開始。目前任務 {jobId} 正在翻譯，是否先停止它，再開始本次翻譯？',
            busyKept: '已保留目前任務，本次翻譯未開始。',
            resumed: '偵測到進行中的翻譯任務，已接續顯示進度',
            resumeAskInline: '偵測到上次翻譯任務已完成，是否還原其原文與譯文？',
            resumeAskFile: '偵測到上次檔案翻譯已完成，是否下載譯文（{name}）？',
            resumeGone: '上次任務的結果已不在伺服端（只保留最近一次任務）。',
            resumeRestored: '已還原上次任務的原文與譯文',
            resumeMcpDone: '接續的翻譯任務已結束，結果經原入口（MCP）交付。'
        },
        model: {
            downloadTitle: '下載翻譯模型',
            downloadDone: '{name} 下載完成，已可在「翻譯模型」中選用。',
            downloadFailed: '模型下載失敗：{message}',
            downloadBusy: '已有模型下載任務在進行，請先完成或取消。',
            downloading: '已下載 {size}（{percent}）',
            probing: '正在檢測下載來源（huggingface.co / hf-mirror.com）…',
            preparing: '準備下載…',
            downloadOk: '下載完成！',
            cancelled: '下載已取消。',
            cancelDownload: '取消下載',
            notDownloaded: '模型「{name}」尚未下載完成，請等待下載結束。'
        },
        job: {
            title: '目前任務',
            refresh: '重新整理目前任務',
            empty: '目前沒有正在執行的翻譯任務',
            stop: '停止',
            busy: '伺服器忙碌（目前任務 {jobId}）',
            hint: '同一時間只執行一個任務（記憶體中只載入一個模型）：目前任務執行期間新的翻譯會被拒絕，請先停止目前任務。'
        },
        mcp: {
            title: '把本服務接入 AI 用戶端',
            endpoint: 'MCP 端點（Streamable HTTP）',
            tools: '可用工具',
            toolList: '列出 ./models 中掃描到的全部翻譯模型',
            toolLanguages: '列出 38 種支援的翻譯目標語言',
            toolTranslateText: '翻譯文字：自動拆段 + 進度串流輸出 + 可隨時取消',
            toolTranslateFile: '翻譯本機上的文字檔',
            toolCurrent: '查詢目前任務狀態 / 停止目前任務',
            footer: '上述工具與 WebUI 共用同一套翻譯核心，行為完全一致。',
            clients: '用戶端設定（Claude / VS Code / Cursor）',
            clientsHint: '複製下面的設定片段，依各用戶端說明放入設定檔即可。Claude 與 Cursor 寫法完全相同，僅檔案位置不同。',
            clientCommon: 'Claude / Cursor（通用 mcpServers 寫法）',
            clientVscode: 'VS Code（settings.json 寫法）',
            clientClaudePath: '放入設定檔 claude_desktop_config.json',
            clientVscodePath: '放入 VS Code 設定 mcp.servers',
            clientCursorPath: '放入設定檔 ~/.cursor/mcp.json',
            faq: '常見問題與排障',
            faqTargetLangTitle: '參數名用駝峰式',
            faqTargetLang: '工具參數名與 C# 參數名一致，採駝峰式（如 targetLanguage）；請勿寫成底線式（target_language），否則服務端會判定缺少必填參數而報錯。',
            faqSessionTitle: '握手不回傳 Session-Id 屬正常',
            faqSession: '本服務為無狀態實作，initialize 握手的回應標頭不含 Mcp-Session-Id，屬正常表現，用戶端無需特殊處理即可正常呼叫。'
        },
        api: {
            title: 'WebAPI 與即時通道說明',
            intro: '服務位址（下述所有路徑均相對此位址）：',
            rest: 'WebAPI（REST / HTTP）',
            rowSystem: '系統資訊：版本、MCP 端點、即時通道路徑等',
            rowModels: '列出 ./models 掃描到的翻譯模型（加 refresh 參數）',
            rowModelsRefresh: '重新掃描模型目錄（放入新 gguf 後無需重啟）',
            rowLanguages: '列出 38 種支援的翻譯目標語言',
            rowTranslate: '同步翻譯：送出後等整篇完成，一次回傳 {translation} 欄位',
            rowFileUpload: '大文本檔案上傳翻譯：multipart 上傳（file 欄位），回傳 jobId 後用 current 輪詢進度',
            rowFileResult: '下載已完成任務的譯文（只保留最近一次結束任務；進行中回傳 409）',
            rowCurrent: '查詢目前任務快照（進度 + 已譯內容 + kind 入口種類）；空閒時回傳 {idle:true}',
            rowCancel: '停止目前任務（已譯部分保留）',
            busy: '同一時間只有一個任務：忙碌時翻譯回傳 409（busy、currentJobId），可先查詢或停止目前任務後重新送出。',
            exampleRequest: '翻譯請求體範例（POST /api/translation，text 與 targetLanguage 必填，其餘可選）',
            hub: '即時通道（SignalR）',
            hubPath: 'Hub 路徑（先 negotiate 建立連線）',
            hubServer: '伺服器端方法：Translate(request) 發起翻譯、Cancel() 停止目前翻譯',
            hubEvents: '用戶端註冊 JobEvent 事件流：preparing / planned / loading / started / segment / delta / segment_done / done / cancelled / error（只推送給發起方）',
            hubSeparator: '拼接全文必須使用 segment_done 事件攜帶的 separator：換行、縮排、Tab 不進模型，靠回填保真。',
            footer: 'WebAPI、即時通道與 MCP 工具共用同一翻譯核心與並發閘門，行為完全一致。'
        },
        bigfile: {
            title: '檔案翻譯',
            pickHint: '選擇本地文字檔上傳翻譯：翻譯設定與主視窗完全一致（語言、模型、進階選項均取目前值）。長文字自動分段依次翻譯，段數不限。',
            pick: '選擇檔案',
            start: '開始翻譯',
            stop: '終止翻譯',
            uploading: '正在上傳，請稍候…',
            noFile: '請先選擇要翻譯的檔案。',
            tooLarge: '檔案過大，無法上傳。',
            busy: '模型忙碌，本次翻譯沒有開始：{message}',
            failed: '翻譯失敗：{message}',
            progressDone: '{done} / {total} 段',
            elapsed: '已用 {time}',
            eta: '預計剩餘 {time}',
            finishing: '正在收尾…',
            autoSaving: '翻譯完成，正在儲存到本機…',
            saved: '已儲存到本機：{file}',
            partialNote: '（翻譯被終止，儲存的是已譯部分）',
            stopping: '正在停止翻譯，停止後將自動儲存已譯部分…',
            stopUnavailable: '目前沒有可停止的任務。',
            downloadFailed: '取得翻譯結果失敗：{message}',
            resumed: '已接續進行中的檔案翻譯任務。'
        }
    };

    window.I18N_LANGS = window.I18N_LANGS || {};
    window.I18N_LANGS['zh-TW'] = pack;
})();

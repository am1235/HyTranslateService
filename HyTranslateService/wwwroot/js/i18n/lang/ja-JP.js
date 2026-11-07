/* @i18n-lang {"code":"ja-JP","nativeName":"日本語","englishName":"Japanese","order":4} */
/**
 * 日本語 language pack (ja-JP)
 *
 * 自描述：首行 @i18n-lang 头声明代码、母语名、英文名与排序；
 *         服务启动时扫描本目录并据此生成 /api/language/list，前端动态装载。
 * 用法：挂到 window.I18N_LANGS['ja-JP']，由 i18n-config.js 合并进 Layui i18n 消息表。
 *
 * 新增语言：复制本文件 → 翻译所有 value → 改文件名、@i18n-lang 头与下面的 key。
 */
(function () {
    var pack = {
        langNames: {
            'zh': '中国語', 'en': '英語', 'fr': 'フランス語', 'pt': 'ポルトガル語', 'es': 'スペイン語',
            'ja': '日本語', 'tr': 'トルコ語', 'ru': 'ロシア語', 'ar': 'アラビア語', 'ko': '韓国語',
            'th': 'タイ語', 'it': 'イタリア語', 'de': 'ドイツ語', 'vi': 'ベトナム語', 'ms': 'マレー語',
            'id': 'インドネシア語', 'tl': 'フィリピン語', 'hi': 'ヒンディー語', 'zh-Hant': '中国語（繁体）', 'pl': 'ポーランド語',
            'cs': 'チェコ語', 'nl': 'オランダ語', 'km': 'クメール語', 'my': 'ビルマ語', 'fa': 'ペルシア語',
            'gu': 'グジャラート語', 'ur': 'ウルドゥー語', 'te': 'テルグ語', 'mr': 'マラーティー語', 'he': 'ヘブライ語',
            'bn': 'ベンガル語', 'ta': 'タミル語', 'uk': 'ウクライナ語', 'bo': 'チベット語', 'kk': 'カザフ語',
            'mn': 'モンゴル語', 'ug': 'ウイグル語', 'yue': '広東語'
        },
        app: {
            name: 'HyTrans',
            slogan: 'ローカル Hy-MT2 翻訳 · CPU / GPU 推論 · 完全オフライン',
            copy: 'コピー',
            confirm: 'OK',
            connecting: '接続中…',
            connected: '接続済み',
            disconnected: '未接続'
        },
        header: {
            mcp: 'MCP 連携',
            api: 'WebAPI 解説',
            themeMode: '表示モード',
            home: 'プロジェクトページ'
        },
        input: {
            title: '原文',
            placeholder: 'ここに翻訳したい内容を貼り付けるか入力してください。テキストファイルをこの領域にドラッグすることもできます。\n\n長文は段落・文単位で自動分割され、順次翻訳しながら結果をリアルタイムで表示します。',
            charCount: '{count} 文字'
        },
        option: {
            title: '翻訳設定',
            sourceLang: '原文の言語',
            targetLang: '訳文の言語',
            model: '翻訳モデル',
            engine: '推論エンジン',
            engineHint: 'auto = 利用可能な GPU を使用し、読み込みに失敗したら CPU に戻します。GPU = グラフィックボードを強制し、使えない場合はエラーにします。',
            kvCache: 'KV キャッシュ',
            kvCacheHint: 'メモリ使用量と再開の速さだけに影響し、訳文は変わりません。',
            autoDetect: '自動判定',
            loaded: '読み込み済み',
            reloadModels: 'モデルを再スキャン',
            downloaded: '（ダウンロード済み）',
            modelHint: '未ダウンロードのモデルを選ぶと、Hugging Face から自動的にダウンロードされます（アクセス制限時は hf-mirror.com ミラーに自動切替）。',
            advanced: '詳細オプション',
            resetAdvanced: '初期値に戻す',
            segmentChars: '分割文字数（1 セグメントの最大文字数）',
            segmentCharsHint: '範囲 80 ～ {max}。小さいほど省メモリ、大きいほど文脈を保持します。',
            maxTokensHint: '0 = let the model decide; higher yields longer output but takes more time.',
            promptStyleHint: 'Different prompt templates noticeably change tone and term coverage.',
            promptLanguageHint: 'Language of the prompt; keep English for non-Chinese models.',
            temperatureHint: 'Higher = more creative, lower = more deterministic; translation prefers low.',
            topPHint: 'Nucleus sampling: draw only from the top-p probability mass to control diversity.',
            topKHint: 'Restrict to the top-K most likely tokens; 0 disables this limit.',
            repetitionPenaltyHint: '>1 reduces repetition, <1 encourages it; too high feels stiff.',
            backgroundHint: 'Add domain / scenario context so the model understands the whole passage.',
            targetStyleHint: 'Pick multiple and customize; styles combine.',
            maxTokens: '1 セグメントの最大生成トークン',
            promptStyle: 'プロンプトテンプレート',
            promptLanguage: 'プロンプトの言語',
            temperature: '温度',
            topP: '核サンプリング top_p',
            topK: 'top_k',
            repetitionPenalty: '繰り返しペナルティ',
            glossary: '用語集',
            glossaryHint: '1 行に「原文=訳文」を 1 件。固有名詞の一貫性が大きく向上します。',
            glossaryPlaceholder: 'Hunyuan=混元\nMCP=モデルコンテキストプロトコル',
            background: '背景情報 / 文書コンテキスト',
            backgroundPlaceholder: 'これは XX 製品に関する技術文書で、読者はエンジニアです……',
            targetStyle: '訳文のスタイル（「スタイルテンプレート」と併用）',
            targetStylePlaceholder: '例：口語体、正式な書き言葉、簡潔な技術文書',
            targetStyleCustomPlaceholder: 'Custom style…',
            add: 'Add'
        },
        action: {
            translate: '翻訳を開始',
            stop: '翻訳を中止',
            clear: 'テキストをクリア',
            bigFile: 'ファイル翻訳',
            swap: '言語を入れ替え',
            copyResult: '訳文をコピー',
            download: '訳文を .txt で保存'
        },
        output: {
            title: '訳文',
            ready: '準備完了',
            placeholder: '翻訳結果がここにリアルタイムで表示されます',
            placeholderSub: '長文は自動分割 · ストリーミング出力 · いつでも中止可能',
            translating: '翻訳中…',
            segments: '{done} / {total} セグメント',
            elapsed: '経過 {time}',
            eta: '残り {time}',
            speed: '{value} 文字/秒'
        },
        status: {
            idle: '未翻訳',
            preparing: '準備中',
            loading: 'モデル読み込み中',
            running: '翻訳中',
            completed: '完了',
            cancelled: '中止',
            failed: '失敗'
        },
        style: {
            short: '簡潔な指示（既定）',
            official: '公式デフォルトテンプレート',
            terminology: '用語テンプレート',
            style: 'スタイルテンプレート',
            colloquial: 'Colloquial',
            formal: 'Formal written',
            techdoc: 'Concise technical',
            delimiters: '区切り文字を保持（字幕/コード）',
            background: '背景情報テンプレート'
        },
        engine: {
            auto: '自動（GPU があれば使用）',
            gpu: 'GPU',
            cpu: 'CPU'
        },
        kvCache: {
            none: 'キャッシュしない（省メモリ）',
            memory: '前回を保持（高速）'
        },
        promptLang: {
            en: 'English',
            zh: '中文'
        },
        msg: {
            noText: '翻訳する内容を入力または選択してください。',
            noModel: 'モデルがまだダウンロードされていません。「翻訳モデル」ドロップダウンで選ぶと自動的にダウンロードされます（Hugging Face またはそのミラーへの接続が必要です）。',
            started: '翻訳を開始しました',
            cancelRequested: '翻訳を中止しています…',
            cancelled: '現在の翻訳を中止しました',
            textTooLong: 'テキストが1回のサイズ上限を超えています。分割して翻訳してください。',
            failed: '翻訳に失敗しました：{message}',
            loadFailed: '読み込みに失敗しました：{message}',
            confirmClear: '原文と訳文をクリアしてもよろしいですか？',
            confirmResetAdvanced: '詳細オプションを初期値に戻しますか？',
            advancedReset: '詳細オプションを初期値に戻しました',
            cleared: 'クリアしました',
            swapNeedsSource: '原文の言語が「自動判定」のときは入れ替えできません。先に原文の言語を指定してください。',
            nothingToCopy: 'コピーできる内容がありません。',
            copied: 'クリップボードにコピーしました',
            copyFailed: 'コピーに失敗しました。テキストを手動で選択してコピーしてください。',
            nothingToDownload: 'ダウンロードできる内容がありません。',
            modelsRefreshed: 'モデル一覧を更新しました。全 {count} 件です。',
            langSwitched: '表示言語を切り替えました',
            fileTooLarge: 'ドロップされたファイルが大きすぎて、エディタに直接読み込めません。',
            fileReadFailed: 'ファイルの読み込みに失敗しました：{message}',
            busyTitle: 'モデル使用中',
            busyConfirm: 'モデルが使用中のため、今回の翻訳は開始されませんでした。現在のタスク {jobId} が翻訳中です。先に停止してから開始しますか？',
            busyKept: '現在のタスクを維持しました。今回の翻訳は開始されていません。',
            resumed: '実行中の翻訳タスクを検出しました。進捗表示を引き継ぎました',
            resumeAskInline: '前回の翻訳タスクが完了しています。原文と訳文を復元しますか？',
            resumeAskFile: '前回のファイル翻訳が完了しています。訳文（{name}）をダウンロードしますか？',
            resumeGone: '前回のタスクの結果はサーバーに残っていません（最新の1件のみ保持）。',
            resumeRestored: '前回のタスクの原文と訳文を復元しました',
            resumeMcpDone: '引き継いだ翻訳タスクは終了しました。結果は元の入口（MCP）経由で渡されます。'
        },
        model: {
            downloadTitle: 'モデルをダウンロード',
            downloadDone: '{name} のダウンロードが完了し、「翻訳モデル」で選択できます。',
            downloadFailed: 'モデルのダウンロードに失敗しました：{message}',
            downloadBusy: '他のモデルのダウンロードが進行中です。完了またはキャンセルしてください。',
            downloading: 'ダウンロード済み {size}（{percent}）',
            probing: 'ダウンロード元を確認中（huggingface.co / hf-mirror.com）…',
            preparing: 'ダウンロードを準備中…',
            downloadOk: 'ダウンロード完了！',
            cancelled: 'ダウンロードをキャンセルしました。',
            cancelDownload: 'ダウンロードをキャンセル',
            notDownloaded: 'モデル「{name}」はまだダウンロード完了していません。完了までお待ちください。'
        },
        job: {
            title: '現在のタスク',
            refresh: '現在のタスクを更新',
            empty: '現在実行中の翻訳タスクはありません',
            stop: '停止',
            busy: 'サーバー使用中（現在のタスク {jobId}）',
            hint: '同時に実行できるタスクは 1 つだけです（メモリに常駐するモデルも 1 つ）。現在のタスクの実行中は新しい翻訳が拒否されるため、先に現在のタスクを停止してください。'
        },
        mcp: {
            title: 'このサービスを AI クライアントに接続する',
            endpoint: 'MCP エンドポイント（Streamable HTTP）',
            tools: '利用可能なツール',
            toolList: './models で検出されたすべての翻訳モデルを一覧表示',
            toolLanguages: '対応する 38 種類の翻訳先言語を一覧表示',
            toolTranslateText: 'テキスト翻訳：自動分割＋進捗ストリーミング＋いつでもキャンセル可能',
            toolTranslateFile: 'ローカル上のテキストファイルを翻訳',
            toolCurrent: '現在のタスクの状態照会 / 現在のタスクの停止',
            footer: '上記のツールは WebUI と同じ翻訳コアを共有しており、動作は完全に一致します。',
            clients: 'クライアント設定（Claude / VS Code / Cursor）',
            clientsHint: '下の設定スニペットをコピーし、各クライアントの設定ファイルに貼り付けてください。Claude と Cursor は書式が同じで、異なるのはファイルの場所だけです。',
            clientCommon: 'Claude / Cursor（共通の mcpServers 書式）',
            clientVscode: 'VS Code（settings.json 書式）',
            clientClaudePath: '設定ファイル claude_desktop_config.json に追加',
            clientVscodePath: 'VS Code 設定の mcp.servers に追加',
            clientCursorPath: '設定ファイル ~/.cursor/mcp.json に追加',
            faq: 'よくある質問とトラブルシューティング',
            faqTargetLangTitle: 'パラメータ名はキャメルケース',
            faqTargetLang: 'ツールのパラメータは C# のパラメータ名と一致し、キャメルケース（例: targetLanguage）です。スネークケース（target_language）は使わないでください。そうするとサーバーは必須パラメータ不足と報告します。',
            faqSessionTitle: 'ハンドシェイクで Session-Id が返らなくても正常',
            faqSession: '本サービスはステートレス実装です。initialize ハンドシェイクの応答ヘッダーに Mcp-Session-Id は含まれません。これは正常な動作であり、特別な処理は不要で、クライアントは通常通りツールを呼び出せます。'
        },
        api: {
            title: 'WebAPI とリアルタイム通道の説明',
            intro: 'サービスのアドレス（以下のパスはすべてこのアドレス基準）：',
            rest: 'WebAPI（REST / HTTP）',
            rowSystem: 'システム情報：バージョン、MCP エンドポイント、リアルタイム通道パスなど',
            rowModels: './models で検出された翻訳モデルを一覧表示（refresh パラメータ付き）',
            rowModelsRefresh: 'モデルディレクトリを再スキャン（新しい gguf は再起動不要）',
            rowLanguages: '対応する 38 種類の翻訳先言語を一覧表示',
            rowTranslate: '同期翻訳：全文の完了を待って、{translation} フィールドを一度に返す',
            rowFileUpload: '大容量テキストファイルのアップロード翻訳：multipart アップロード（file フィールド）、jobId を返し、current で進捗を取得',
            rowFileResult: '完了タスクの訳文をダウンロード（直近の終了タスクのみ保持。実行中は 409）',
            rowCurrent: '現在のタスクのスナップショット照会（進捗＋訳文＋kind 種別）；アイドル時は {idle:true}',
            rowCancel: '現在のタスクを停止（訳済み部分は保持）',
            busy: '同時に実行できるタスクは 1 つだけです：使用中の場合は 409（busy、currentJobId）を返すので、現在のタスクを照会・停止してから再送信してください。',
            exampleRequest: '翻訳リクエスト本文の例（POST /api/translation。text と targetLanguage は必須、他は任意）',
            hub: 'リアルタイム通道（SignalR）',
            hubPath: 'Hub パス（先に negotiate で接続を確立）',
            hubServer: 'サーバーメソッド：Translate(request) で翻訳開始、Cancel() で現在の翻訳を停止',
            hubEvents: 'クライアントは JobEvent イベントストリームを登録：preparing / planned / loading / started / segment / delta / segment_done / done / cancelled / error（発起元にのみ送信）',
            hubSeparator: '全文を組み立てる際は segment_done イベントの separator を必ず使用：改行・インデント・タブはモデルに入らず、復元時に埋め戻されます。',
            footer: 'WebAPI・リアルタイム通道・MCP ツールは同一の翻訳コアと同時実行ゲートを共有しています。'
        },
        bigfile: {
            title: 'ファイル翻訳',
            pickHint: 'ローカルのテキストファイルを選んで翻訳：翻訳設定はメイン画面と完全に同じ（言語・モデル・詳細設定はすべて現在の値を使用）。長文は自動分割して順番に翻訳され、セグメント数の上限はありません。',
            pick: 'ファイルを選択',
            start: '翻訳を開始',
            stop: '翻訳を中止',
            uploading: 'アップロード中です。お待ちください…',
            noFile: '翻訳するファイルを先に選択してください。',
            tooLarge: 'ファイルが大きすぎてアップロードできません。',
            busy: 'モデルが使用中のため、翻訳は開始されませんでした：{message}',
            failed: '翻訳に失敗しました：{message}',
            progressDone: '{done} / {total} セグメント',
            elapsed: '経過 {time}',
            eta: '残り約 {time}',
            finishing: '仕上げ中…',
            autoSaving: '翻訳が完了しました。ローカルに保存しています…',
            saved: 'ローカルに保存しました：{file}',
            partialNote: '（翻訳は中止されました。訳済み部分を保存します）',
            stopping: '翻訳を停止しています。停止後、訳済み部分を自動保存します…',
            stopUnavailable: '停止できるタスクはありません。',
            downloadFailed: '翻訳結果の取得に失敗しました：{message}',
            resumed: '実行中のファイル翻訳タスクを引き継ぎました。'
        }
    };

    window.I18N_LANGS = window.I18N_LANGS || {};
    window.I18N_LANGS['ja-JP'] = pack;
})();

/* @i18n-lang {"code":"ko-KR","nativeName":"한국어","englishName":"Korean","order":5} */
/**
 * 한국어 language pack (ko-KR)
 *
 * 自描述：首行 @i18n-lang 头声明代码、母语名、英文名与排序；
 *         服务启动时扫描本目录并据此生成 /api/language/list，前端动态装载。
 * 用法：挂到 window.I18N_LANGS['ko-KR']，由 i18n-config.js 合并进 Layui i18n 消息表。
 *
 * 新增语言：复制本文件 → 翻译所有 value → 改文件名、@i18n-lang 头与下面的 key。
 */
(function () {
    var pack = {
        langNames: {
            'zh': '중국어', 'en': '영어', 'fr': '프랑스어', 'pt': '포르투갈어', 'es': '스페인어',
            'ja': '일본어', 'tr': '터키어', 'ru': '러시아어', 'ar': '아랍어', 'ko': '한국어',
            'th': '태국어', 'it': '이탈리아어', 'de': '독일어', 'vi': '베트남어', 'ms': '말레이어',
            'id': '인도네시아어', 'tl': '필리핀어', 'hi': '힌디어', 'zh-Hant': '중국어(번체)', 'pl': '폴란드어',
            'cs': '체코어', 'nl': '네덜란드어', 'km': '크메르어', 'my': '미얀마어', 'fa': '페르시아어',
            'gu': '구자라트어', 'ur': '우르두어', 'te': '텔루구어', 'mr': '마라티어', 'he': '히브리어',
            'bn': '벵골어', 'ta': '타밀어', 'uk': '우크라이나어', 'bo': '티베트어', 'kk': '카자흐어',
            'mn': '몽골어', 'ug': '위구르어', 'yue': '광둥어'
        },
        app: {
            name: 'HyTrans',
            slogan: '로컬 Hy-MT2 번역 · CPU / GPU 추론 · 완전 오프라인',
            copy: '복사',
            confirm: '확인',
            connecting: '연결 중…',
            connected: '연결됨',
            disconnected: '연결 끊김'
        },
        header: {
            mcp: 'MCP 연동',
            api: 'WebAPI 안내',
            themeMode: '라이트/다크',
            home: '프로젝트 홈페이지'
        },
        input: {
            title: '원문',
            placeholder: '번역할 내용을 여기에 붙여넣거나 입력하세요. 텍스트 파일을 이 영역으로 끌어다 놓을 수도 있습니다.\n\n긴 텍스트는 문단과 문장 단위로 자동 분할되어 순차적으로 번역되며, 결과가 실시간으로 표시됩니다.',
            charCount: '{count} 자'
        },
        option: {
            title: '번역 설정',
            sourceLang: '원문 언어',
            targetLang: '번역 언어',
            model: '번역 모델',
            engine: '추론 엔진',
            engineHint: 'auto = 사용 가능한 GPU를 사용하고 로드에 실패하면 CPU로 되돌립니다. GPU = 그래픽카드를 강제하며 사용할 수 없으면 오류를 냅니다.',
            kvCache: 'KV 캐시',
            kvCacheHint: '메모리 사용량과 예열 속도에만 영향을 주며 번역문은 바뀌지 않습니다.',
            autoDetect: '자동 감지',
            loaded: '로드됨',
            reloadModels: '모델 다시 검색',
            downloaded: '（다운로드됨）',
            modelHint: '다운로드하지 않은 모델을 선택하면 Hugging Face에서 자동으로 다운로드합니다(접근 제한 시 hf-mirror.com 미러로 자동 전환).',
            advanced: '고급 옵션',
            resetAdvanced: '기본값으로 복원',
            segmentChars: '분할 글자 수(세그먼트 최대 글자 수)',
            segmentCharsHint: '범위 80 ~ {max}. 작을수록 메모리를 아끼고, 클수록 문맥을 더 많이 유지합니다.',
            maxTokensHint: '0 = let the model decide; higher yields longer output but takes more time.',
            promptStyleHint: 'Different prompt templates noticeably change tone and term coverage.',
            promptLanguageHint: 'Language of the prompt; keep English for non-Chinese models.',
            temperatureHint: 'Higher = more creative, lower = more deterministic; translation prefers low.',
            topPHint: 'Nucleus sampling: draw only from the top-p probability mass to control diversity.',
            topKHint: 'Restrict to the top-K most likely tokens; 0 disables this limit.',
            repetitionPenaltyHint: '>1 reduces repetition, <1 encourages it; too high feels stiff.',
            backgroundHint: 'Add domain / scenario context so the model understands the whole passage.',
            targetStyleHint: 'Pick multiple and customize; styles combine.',
            maxTokens: '세그먼트당 최대 생성 토큰',
            promptStyle: '프롬프트 템플릿',
            promptLanguage: '프롬프트 언어',
            temperature: '온도',
            topP: '핵 샘플링 top_p',
            topK: 'top_k',
            repetitionPenalty: '반복 페널티',
            glossary: '용어집',
            glossaryHint: '한 줄에 「원문=번역」 하나씩. 고유명사 일관성이 크게 향상됩니다.',
            glossaryPlaceholder: 'Hunyuan=混元\nMCP=모델 컨텍스트 프로토콜',
            background: '배경 정보 / 문서 컨텍스트',
            backgroundPlaceholder: '이 문서는 XX 제품에 관한 기술 문서이며, 독자는 엔지니어입니다……',
            targetStyle: '번역문 스타일(「스타일 템플릿」과 함께 사용)',
            targetStylePlaceholder: '예: 구어체, 격식 있는 문어체, 간결한 기술 문서',
            targetStyleCustomPlaceholder: 'Custom style…',
            add: 'Add'
        },
        action: {
            translate: '번역 시작',
            stop: '번역 중단',
            clear: '텍스트 지우기',
            bigFile: '파일 번역',
            swap: '언어 교체',
            copyResult: '번역문 복사',
            download: '번역문 .txt 저장'
        },
        output: {
            title: '번역문',
            ready: '준비됨',
            placeholder: '번역 결과가 여기에 실시간으로 표시됩니다',
            placeholderSub: '긴 텍스트 자동 분할 · 스트리밍 출력 · 언제든지 중단 가능',
            translating: '번역 중…',
            segments: '{done} / {total} 세그먼트',
            elapsed: '경과 {time}',
            eta: '남은 {time}',
            speed: '{value} 자/초'
        },
        status: {
            idle: '대기',
            preparing: '준비 중',
            loading: '모델 로드 중',
            running: '번역 중',
            completed: '완료',
            cancelled: '중단됨',
            failed: '실패'
        },
        style: {
            short: '간결 지시(기본)',
            official: '공식 기본 템플릿',
            terminology: '용어 템플릿',
            style: '스타일 템플릿',
            colloquial: 'Colloquial',
            formal: 'Formal written',
            techdoc: 'Concise technical',
            delimiters: '구분자 유지(자막/코드)',
            background: '배경 정보 템플릿'
        },
        engine: {
            auto: '자동(GPU 사용 가능 시)',
            gpu: 'GPU',
            cpu: 'CPU'
        },
        kvCache: {
            none: '캐시 안 함(메모리 절약)',
            memory: '이전 유지(더 빠름)'
        },
        promptLang: {
            en: 'English',
            zh: '中文'
        },
        msg: {
            noText: '먼저 번역할 내용을 입력하거나 선택하세요.',
            noModel: '다운로드된 모델이 없습니다. 「번역 모델」 드롭다운에서 하나를 선택하면 자동으로 다운로드합니다(Hugging Face 또는 미러 접근 필요).',
            started: '번역을 시작했습니다',
            cancelRequested: '번역을 중단하는 중…',
            cancelled: '현재 번역 작업을 중단했습니다',
            textTooLong: '텍스트가 요청당 크기 한도를 초과했습니다. 나누어 번역해 주세요.',
            failed: '번역 실패: {message}',
            loadFailed: '불러오기 실패: {message}',
            confirmClear: '원문과 번역문을 모두 지우시겠습니까?',
            confirmResetAdvanced: '고급 옵션을 기본값으로 복원하시겠습니까?',
            advancedReset: '고급 옵션이 기본값으로 복원되었습니다',
            cleared: '지웠습니다',
            swapNeedsSource: '원문 언어가 「자동 감지」일 때는 교체할 수 없습니다. 먼저 원문 언어를 지정하세요.',
            nothingToCopy: '복사할 내용이 없습니다.',
            copied: '클립보드에 복사했습니다',
            copyFailed: '복사 실패. 텍스트를 직접 선택해 복사하세요.',
            nothingToDownload: '다운로드할 내용이 없습니다.',
            modelsRefreshed: '모델 목록을 갱신했습니다. 총 {count}개입니다.',
            langSwitched: '인터페이스 언어를 전환했습니다',
            fileTooLarge: '드롭한 파일이 너무 커서 편집기로 직접 불러올 수 없습니다.',
            fileReadFailed: '파일 읽기 실패: {message}',
            busyTitle: '모델 사용 중',
            busyConfirm: '모델이 사용 중이라 이번 번역은 시작되지 않았습니다. 현재 작업 {jobId}이(가) 번역 중입니다. 먼저 중지하고 시작할까요?',
            busyKept: '현재 작업을 유지했습니다. 이번 번역은 시작되지 않았습니다.',
            resumed: '진행 중인 번역 작업이 감지되어 진행률 표시를 이어받았습니다',
            resumeAskInline: '이전 번역 작업이 완료되었습니다. 원문과 번역문을 복원할까요?',
            resumeAskFile: '이전 파일 번역이 완료되었습니다. 번역문({name})을 다운로드할까요?',
            resumeGone: '이전 작업의 결과가 서버에 없습니다(최근 작업 1건만 보관됩니다).',
            resumeRestored: '이전 작업의 원문과 번역문을 복원했습니다',
            resumeMcpDone: '이어받은 번역 작업이 종료되었습니다. 결과는 원래 경로(MCP)로 전달됩니다.'
        },
        model: {
            downloadTitle: '모델 다운로드',
            downloadDone: '{name} 다운로드가 완료되어 「번역 모델」에서 선택할 수 있습니다.',
            downloadFailed: '모델 다운로드 실패: {message}',
            downloadBusy: '이미 진행 중인 모델 다운로드가 있습니다. 완료하거나 취소해 주세요.',
            downloading: '다운로드됨 {size} ({percent})',
            probing: '다운로드 소스 확인 중(huggingface.co / hf-mirror.com)…',
            preparing: '다운로드 준비 중…',
            downloadOk: '다운로드 완료!',
            cancelled: '다운로드가 취소되었습니다.',
            cancelDownload: '다운로드 취소',
            notDownloaded: '모델 "{name}"의 다운로드가 아직 완료되지 않았습니다. 완료될 때까지 기다려 주세요.'
        },
        job: {
            title: '현재 작업',
            refresh: '현재 작업 새로고침',
            empty: '현재 실행 중인 번역 작업이 없습니다',
            stop: '중지',
            busy: '서버 사용 중 (현재 작업 {jobId})',
            hint: '동시에 실행되는 작업은 하나뿐입니다(메모리에 상주하는 모델도 하나). 현재 작업 실행 중에는 새 번역이 거부되므로 현재 작업을 먼저 중지하세요.'
        },
        mcp: {
            title: '이 서비스를 AI 클라이언트에 연결',
            endpoint: 'MCP 엔드포인트(Streamable HTTP)',
            tools: '사용 가능한 도구',
            toolList: './models 에서 검색된 모든 번역 모델 나열',
            toolLanguages: '지원되는 38개 번역 대상 언어 나열',
            toolTranslateText: '텍스트 번역: 자동 분할 + 진행률 스트리밍 + 언제든 취소 가능',
            toolTranslateFile: '로컬의 텍스트 파일 번역',
            toolCurrent: '현재 작업 상태 조회 / 현재 작업 중지',
            footer: '위 도구들은 WebUI와 동일한 번역 코어를 공유하며 동작이 완전히 일치합니다.',
            clients: '클라이언트 설정(Claude / VS Code / Cursor)',
            clientsHint: '아래 설정 조각을 복사해 각 클라이언트의 설정 파일에 넣으세요. Claude 와 Cursor 는 형식이 동일하며 파일 위치만 다릅니다.',
            clientCommon: 'Claude / Cursor(공통 mcpServers 형식)',
            clientVscode: 'VS Code(settings.json 형식)',
            clientClaudePath: '설정 파일 claude_desktop_config.json 에 추가',
            clientVscodePath: 'VS Code 설정의 mcp.servers 에 추가',
            clientCursorPath: '설정 파일 ~/.cursor/mcp.json 에 추가',
            faq: '자주 묻는 질문 및 문제 해결',
            faqTargetLangTitle: '매개변수 이름은 카멜케이스',
            faqTargetLang: '도구 매개변수는 C# 매개변수 이름과 일치하며 카멜케이스(예: targetLanguage)입니다. 스네이크케이스(target_language)를 사용하지 마세요. 그러면 서버가 필수 매개변수 누락이라고 보고합니다.',
            faqSessionTitle: '핸드셰이크에서 Session-Id 가 안 돌아와도 정상',
            faqSession: '이 서비스는 상태 비저장(stateless) 구현입니다. initialize 핸드셰이크 응답 헤더에는 Mcp-Session-Id 가 포함되지 않습니다. 이는 정상적인 동작이며 별도 처리 없이 도구를 정상 호출할 수 있습니다.'
        },
        api: {
            title: 'WebAPI 및 실시간 채널 안내',
            intro: '서비스 주소(아래 모든 경로는 이 주소 기준):',
            rest: 'WebAPI (REST / HTTP)',
            rowSystem: '시스템 정보: 버전, MCP 엔드포인트, 실시간 채널 경로 등',
            rowModels: './models 에서 검색된 번역 모델 나열(refresh 파라미터 추가)',
            rowModelsRefresh: '모델 디렉터리 재스캔(새 gguf 추가 시 재시작 불필요)',
            rowLanguages: '지원되는 38개 번역 대상 언어 나열',
            rowTranslate: '동기 번역: 전체 완료를 기다린 후 {translation} 필드를 한 번에 반환',
            rowFileUpload: '대용량 텍스트 파일 업로드 번역: multipart 업로드(file 필드), jobId 반환 후 current 로 진행률 조회',
            rowFileResult: '완료된 작업의 번역문 다운로드(마지막 종료 작업만 보관. 실행 중이면 409)',
            rowCurrent: '현재 작업 스냅샷 조회(진행률 + 번역된 내용 + kind 종류); 유휴 시 {idle:true}',
            rowCancel: '현재 작업 중지(번역된 부분은 유지)',
            busy: '동시에 실행되는 작업은 하나뿐입니다: 사용 중이면 409(busy, currentJobId)를 반환하므로 현재 작업을 조회·중지한 후 다시 제출하세요.',
            exampleRequest: '번역 요청 본문 예시(POST /api/translation, text 와 targetLanguage 는 필수, 나머지는 선택)',
            hub: '실시간 채널(SignalR)',
            hubPath: 'Hub 경로(먼저 negotiate 로 연결 수립)',
            hubServer: '서버 메서드: Translate(request) 로 번역 시작, Cancel() 로 현재 번역 중지',
            hubEvents: '클라이언트는 JobEvent 이벤트 스트림 등록: preparing / planned / loading / started / segment / delta / segment_done / done / cancelled / error(요청자에게만 전송)',
            hubSeparator: '전체 텍스트를 조립할 때는 segment_done 이벤트의 separator 를 반드시 사용: 줄바꿈·들여쓰기·탭은 모델에 들어가지 않으며 복원 시 채워 넣습니다.',
            footer: 'WebAPI, 실시간 채널, MCP 도구는 동일한 번역 코어와 동시성 게이트를 공유합니다.'
        },
        bigfile: {
            title: '파일 번역',
            pickHint: '로컬 텍스트 파일을 선택해 번역하세요: 번역 설정은 메인 화면과 완전히 동일합니다(언어·모델·고급 옵션 모두 현재 값 사용). 긴 텍스트는 자동 분할되어 순서대로 번역되며 세그먼트 수 제한이 없습니다.',
            pick: '파일 선택',
            start: '번역 시작',
            stop: '번역 중지',
            uploading: '업로드 중입니다. 잠시만 기다려 주세요…',
            noFile: '먼저 번역할 파일을 선택하세요.',
            tooLarge: '파일이 너무 커서 업로드할 수 없습니다.',
            busy: '모델이 사용 중이어서 번역이 시작되지 않았습니다: {message}',
            failed: '번역 실패: {message}',
            progressDone: '{done} / {total} 세그먼트',
            elapsed: '경과 {time}',
            eta: '약 {time} 남음',
            finishing: '마무리 중…',
            autoSaving: '번역이 완료되었습니다. 로컬에 저장하는 중…',
            saved: '로컬에 저장했습니다: {file}',
            partialNote: '(번역이 중지되었습니다. 번역된 부분을 저장합니다)',
            stopping: '번역을 중지하는 중입니다. 중지 후 번역된 부분이 자동 저장됩니다…',
            stopUnavailable: '중지할 수 있는 작업이 없습니다.',
            downloadFailed: '번역 결과를 가져오지 못했습니다: {message}',
            resumed: '진행 중인 파일 번역 작업을 이어받았습니다.'
        }
    };

    window.I18N_LANGS = window.I18N_LANGS || {};
    window.I18N_LANGS['ko-KR'] = pack;
})();

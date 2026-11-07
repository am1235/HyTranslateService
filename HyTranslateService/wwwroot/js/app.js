/* ═══════════════════════════════════════════════════════════════
   HyTrans WebUI —— 页面逻辑

   架构：
     · 后端：Controllers 提供 WebAPI（/api/**），SignalR 推送流式翻译事件
     · 前端：Layui 组件 + 原生 DOM，国际化走 Layui 自带 i18n（_t）
     · 设置：界面语言与面板偏好存在本浏览器（HySettings / localStorage），
             服务端不下发初始配置，也不保存任何界面状态

   实时通道（SignalR Hub，/signalr/translation）只做一件事——流式翻译：
     · Translate(request) → 服务端推回 JobEvent（preparing/segment/delta/…/done）
     · Cancel()           → 立即停止当前任务

   任务控制走 WebAPI（SignalR 不可用时的兜底）：
     · POST /api/translation/cancel  → 停止当前任务
     · POST /api/translation         → 一步到位翻译（无流式进度）

   系统只维护一个「当前任务」：同一时刻只跑一个任务（内存中只常驻一个模型）。
   已有任务在跑时新的翻译会被拒绝（模型正忙），需先停止当前任务再重试。

   本文件只定义 window.HyTransApp，不自行启动：
   由 boot.js 在「语言清单 + 语言包」就绪后调用 start()。
   ═══════════════════════════════════════════════════════════════ */
(function () {
    'use strict';

    /** SignalR 端点（与后端 TranslationHubRoutes.Path 保持一致） */
    var SIGNALR_PATH = '/signalr/translation';

    /** 拼 WebAPI 地址（页面位于站点根路径，'./api/...' 可兼容子路径部署） */
    function api(path) {
        return './api/' + path;
    }

    /**
     * 会话暂存键（sessionStorage，随标签页存活、刷新不丢）：
     * 记录最近一次提交的翻译任务 { jobId, kind, sourceName, source? }，
     * 刷新后凭它接续进行中的任务 / 恢复已结束任务的结果（与 bigfile-modal.js 共用同一键）。
     */
    var SESSION_KEY = 'hytrans.job.v1';

    function readSessionJob() {
        try {
            var raw = sessionStorage.getItem(SESSION_KEY);
            return raw ? JSON.parse(raw) : null;
        } catch (e) {
            return null;
        }
    }

    function writeSessionJob(entry) {
        try { sessionStorage.setItem(SESSION_KEY, JSON.stringify(entry)); } catch (e) { /* 隐私模式等：忽略 */ }
    }

    function clearSessionJob() {
        try { sessionStorage.removeItem(SESSION_KEY); } catch (e) { /* 忽略 */ }
    }

    /**
     * 应用入口：由 boot.js 在语言清单与语言包就绪后调用，
     * 保证首屏渲染就使用正确语言，不会先闪一下中文。
     */
    window.HyTransApp = { start: main, defaults: null };

    function main() {
        layui.use(['form', 'element', 'layer', 'dropdown', 'slider'], function () {
            init(layui.$, layui.form, layui.element, layui.layer, layui.dropdown, layui.slider);
        });
    }

    /** 页面逻辑主体 */
    function init($, form, element, layer, dropdown, slider) {
        // ────────────────────────────────────────────────────────
        // 状态
        // ────────────────────────────────────────────────────────
        var state = {
            info: null,
            defaults: {},
            models: [],
            download: null,          // 当前模型下载状态（/api/models 的 download 字段）
            languages: [],
            sampling: { temperature: 0.7, topP: 0.6, topK: 20, repetitionPenalty: 1.05 },
            jobId: null,            // 当前任务的 ID（来自流式事件 / 快照）
            running: false,
            segments: [],
            doneCount: 0,
            totalCount: 0,
            startedAt: 0,
            timer: null,
            connection: null,
            fileName: '',           // 最近一次拖入的文件名（用作请求来源名与下载默认文件名）
            streaming: false,
            resumeTimer: null,      // 刷新接续的轮询定时器（进行中任务的秒级轮询）
            resumeSettled: false,   // 接续任务是否已收尾（防止终态与 idle 双路触发）
            resumeKind: null        // 正在接续的任务种类（inline / mcp / mcp-file）
        };

        /** 译文容器（已取消「双语对照」，只有译文一个视图） */
        var OUTPUT_ID = 'output';

        /** 分段 index → { row, target } */
        var nodeMap = {};

        /** 采样滑块是否已渲染：折叠面板展开后才渲染，且只渲染一次 */
        var slidersDrawn = false;

        // ────────────────────────────────────────────────────────
        // DOM 引用
        // ────────────────────────────────────────────────────────
        var dom = {
            source: $('#source'),
            inputMeta: $('#inputMeta'),
            outputMeta: $('#outputMeta'),
            statSegments: $('#statSegments'),
            statElapsed: $('#statElapsed'),
            statSpeed: $('#statSpeed'),
            statEta: $('#statEta'),
            statModel: $('#statModel'),
            footMeta: $('#footMeta'),
            streamNote: $('#streamNote'),
            streamNoteText: $('#streamNoteText'),
            statusPill: $('#signalrStatus'),
            statusText: $('#signalrStatusText'),
            languageLabel: $('#languageLabel'),
            sourceDrop: $('#sourceDrop'),
            btnTheme: $('#btnTheme'),
            themeIcon: $('#themeIcon'),
            segmentCharsHint: $('#segmentCharsHint'),
            btnTranslate: $('#btnTranslate'),
            btnStop: $('#btnStop')
        };

        // ────────────────────────────────────────────────────────
        // 启动
        // ────────────────────────────────────────────────────────
        window.I18nInit();                  // 语言清单与语言包已由 boot.js 准备就绪
        window.I18nOnChange(onLanguageChanged);

        applyTheme();                       // 尽早应用皮肤，避免首屏闪烁
        bindEvents();
        bindOptionPersistence();
        renderSamplingLabels();
        renderLanguagePicker();
        renderSignalRStatus('connecting');
        renderStats();
        connectSignalR();
        loadMeta();
        loadModels();
        loadLanguages();
        resumeCheck();                      // 页面可能在任务进行中/结束后刷新：探测并接续显示（含终止按钮可用性）
        initTargetStyle();                  // 目标风格多选组件（口语化/正式书面/简洁技术文档 + 自定义）

        /**
         * 目标风格多选组件：口语化 / 正式书面 / 简洁技术文档（预设，可勾选）+ 自定义（自由输入）。
         * 选择结果以逗号分隔写入隐藏域 #targetStyle，buildRequest 直接读取，无需改动后端契约。
         */
        function initTargetStyle() {
            var ms = document.getElementById('targetStyleMs');
            if (!ms) return;
            var trigger = ms.querySelector('.hy-ms-trigger');
            var panel = ms.querySelector('.hy-ms-panel');
            var placeholder = ms.querySelector('.hy-ms-placeholder');
            var hidden = document.getElementById('targetStyle');
            var customInput = ms.querySelector('.hy-ms-custom-input');
            var customAdd = ms.querySelector('.hy-ms-custom-add');
            var checkboxes = ms.querySelectorAll('.hy-ms-opt input[type="checkbox"]');

            // 初始选择来自本浏览器记住的偏好（逗号拼接的串，'' = 未选择）
            var savedStyle = String(HySettings.options().targetStyle || '');
            var selected = savedStyle
                ? savedStyle.split(',').filter(function (v) { return !!v; })
                : [];

            function syncHidden() {
                hidden.value = selected.join(',');
                HySettings.setOption('targetStyle', hidden.value);   // 任何勾选/删除/自定义都写回本浏览器
            }

            function render() {
                // 清掉旧 chips（保留 placeholder 与展开箭头），按当前选择重建
                Array.prototype.forEach.call(ms.querySelectorAll('.hy-ms-chip'), function (c) { c.remove(); });
                if (!selected.length) {
                    placeholder.style.display = '';
                } else {
                    placeholder.style.display = 'none';
                    selected.forEach(function (val) {
                        var chip = document.createElement('span');
                        chip.className = 'hy-ms-chip';
                        chip.appendChild(document.createTextNode(val));
                        var x = document.createElement('i');
                        x.className = 'hy-ms-remove layui-icon layui-icon-close';
                        x.setAttribute('data-val', val);
                        chip.appendChild(x);
                        trigger.insertBefore(chip, placeholder);
                    });
                }
                Array.prototype.forEach.call(checkboxes, function (cb) {
                    cb.checked = selected.indexOf(cb.value) !== -1;
                });
                syncHidden();
            }

            function openPanel() { panel.hidden = false; }
            function closePanel() { panel.hidden = true; }

            trigger.addEventListener('click', function (e) {
                if (e.target.closest('.hy-ms-remove')) return;   // 点删除不触发展开/收起
                if (panel.hidden) openPanel(); else closePanel();
            });

            Array.prototype.forEach.call(checkboxes, function (cb) {
                cb.addEventListener('change', function () {
                    if (cb.checked) {
                        if (selected.indexOf(cb.value) === -1) selected.push(cb.value);
                    } else {
                        selected = selected.filter(function (v) { return v !== cb.value; });
                    }
                    render();
                });
            });

            customAdd.addEventListener('click', function () {
                var v = (customInput.value || '').trim();
                if (!v) return;
                if (selected.indexOf(v) === -1) selected.push(v);
                customInput.value = '';
                render();
            });
            customInput.addEventListener('keydown', function (e) {
                if (e.keyCode === 13) { e.preventDefault(); customAdd.click(); }
            });

            // 面板内点击删除
            ms.addEventListener('click', function (e) {
                var rm = e.target.closest('.hy-ms-remove');
                if (rm) {
                    var v = rm.getAttribute('data-val');
                    selected = selected.filter(function (x) { return x !== v; });
                    render();
                }
            });

            // 点击组件外部收起
            document.addEventListener('click', function (e) {
                if (!ms.contains(e.target)) closePanel();
            });

            render();

            // 「恢复默认值」按钮的入口：按当前浏览器偏好（重置后为默认形态）重绘组件
            state.applyTargetStyle = function () {
                var saved = String(HySettings.options().targetStyle || '');
                selected = saved
                    ? saved.split(',').filter(function (v) { return !!v; })
                    : [];
                render();
            };
        }

        // ════════════════════════════════════════════════════════
        // SignalR：只做流式翻译（Translate / Cancel）
        // ════════════════════════════════════════════════════════
        function connectSignalR() {
            if (typeof signalR === 'undefined') {
                renderSignalRStatus('offline');
                console.error('SignalR 客户端未加载：lib/signalr/dist/browser/signalr.min.js');
                return;
            }

            var conn = new signalR.HubConnectionBuilder()
                .withUrl(SIGNALR_PATH)
                .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
                .configureLogging(signalR.LogLevel.Warning)
                .build();

        // SignalR 的 JS 客户端按「方法名小写」索引处理器：只按服务端实际发送的名称注册一次。
        // 同时注册 'JobEvent' 与 'jobEvent' 会落到同一个桶，导致每条事件被投递两次
        //（done 变 2 次、进度计数翻倍）。即使服务端按 camelCase 发送，小写后也会映射到同一桶，无需重复。
        onHub(conn, 'JobEvent', onJobEvent);

            conn.onreconnecting(function () { renderSignalRStatus('connecting'); });
            conn.onreconnected(function () {
                renderSignalRStatus('online');
            });
            conn.onclose(function () { renderSignalRStatus('offline'); });

            conn.start()
                .then(function () { renderSignalRStatus('online'); })
                .catch(function (err) {
                    console.error('SignalR 连接失败', err);
                    renderSignalRStatus('offline');
                });

            state.connection = conn;
        }

        /** 按服务端方法名注册一次。SignalR 的 JS 客户端本身会把方法名小写后索引，
         *  多写一份 camelCase 只会让同一事件被投递两次，因此这里只注册一次。 */
        function onHub(conn, name, handler) {
            conn.on(name, handler);
        }

        function invoke(method, arg) {
            if (!state.connection) return null;
            var p = typeof arg === 'undefined' ? state.connection.invoke(method) : state.connection.invoke(method, arg);
            if (p && p.catch) p.catch(function (err) { console.error(method + ' 调用失败', err); });
            return p;
        }

        /** 实时通道是否可用（不可用时翻译退化为一步到位的 REST 调用） */
        function connected() {
            return !!(state.connection
                && typeof signalR !== 'undefined'
                && state.connection.state === signalR.HubConnectionState.Connected);
        }

        function renderSignalRStatus(status) {
            var pill = dom.statusPill;
            pill.removeClass('is-online is-offline is-pending');
            if (status === 'online') {
                pill.addClass('is-online');
                setText(dom.statusText, _t('app.connected'));
            } else if (status === 'offline') {
                pill.addClass('is-offline');
                setText(dom.statusText, _t('app.disconnected'));
            } else {
                pill.addClass('is-pending');
                setText(dom.statusText, _t('app.connecting'));
            }
        }

        // ── SignalR 服务端消息：流式翻译事件 ────────────────────

        function onJobEvent(evt) {
            if (!evt) return;
            if (evt.jobId) {
                state.jobId = evt.jobId;   // 记录当前任务 ID（首个事件即带）
                // 会话暂存里本页刚提交、还没拿到 jobId 的任务：补上真实 ID
                var saved = readSessionJob();
                if (saved && saved.kind === 'inline' && !saved.jobId) {
                    saved.jobId = evt.jobId;
                    writeSessionJob(saved);
                }
            }

            switch (evt.type) {
                case 'preparing':
                    applyJobStatus('Preparing');
                    if (evt.message) setText(dom.streamNoteText, evt.message);
                    break;

                case 'planned':
                    state.totalCount = evt.total || 0;
                    break;

                case 'loading':
                    applyJobStatus('LoadingModel');
                    setText(dom.streamNoteText, evt.message || _t('status.loading'));
                    break;

                case 'started':
                    applyJobStatus('Running');
                    // 模型已就绪，加载说明完成使命：恢复为「正在翻译…」。
                    // （服务端 started 时已把快照 note 清空；此处同步清掉
                    //   loading 事件留下的「正在加载模型 …」陈旧文案）
                    setText(dom.streamNoteText, _t('output.translating'));
                    break;

                case 'segment':
                    upsertSegment(evt.index, evt.source);
                    if (evt.total) state.totalCount = evt.total;
                    break;

                case 'delta':
                    // 只重写「当前流式段」节点（O(段长)），不重建全文、不刷统计——
                    // 统计与滚动由 250ms 定时器统一处理，事件路径必须保持廉价
                    upsertSegment(evt.index, null);
                    var liveSeg = state.segments[evt.index];
                    if (liveSeg) {
                        liveSeg.translation = evt.text || '';
                        ensureOutputNodes();
                        outNodes.live.textContent = evt.text || '';
                    }
                    break;

                case 'segment_done':
                    upsertSegment(evt.index, evt.source);
                    var doneSeg = state.segments[evt.index];
                    if (doneSeg) {
                        doneSeg.separator = evt.separator || '';   // 片段后的换行/空行/空格，拼全文时原样回填
                        doneSeg.translation = evt.translation || '';
                        doneSeg.done = true;
                        ensureOutputNodes();
                        appendDoneText((evt.translation || '') + (evt.separator || ''));
                        outNodes.live.textContent = '';            // 该段已定稿，流式节点让位给下一段
                    }
                    state.doneCount = evt.completed || state.doneCount + 1;
                    if (evt.total) state.totalCount = evt.total;
                    break;

                case 'done':
                    applyJobStatus('Completed');
                    ensureOutputNodes();
                    scrollOutputToEnd();
                    finishBusy();
                    break;

                case 'cancelled':
                    applyJobStatus('Cancelled');
                    finishBusy();
                    layer.msg(_t('msg.cancelled'), { icon: 1, time: 2000 });
                    break;

                case 'error':
                    applyJobStatus('Failed');
                    finishBusy();
                    layer.msg(_t('msg.failed', { message: evt.message || '' }), { icon: 2, time: 4000 });
                    syncStopAvailability();     // 拒绝原因若是「模型正忙」，得让用户还能停掉那只任务
                    break;
            }
        }

        // 语言变更的回调见下方「语言切换后的重绘」一节（onLanguageChanged）。
        // 服务端不再广播界面语言：语言是每个浏览器自己的偏好。

        // ════════════════════════════════════════════════════════
        // 元信息加载
        // ════════════════════════════════════════════════════════
        function loadMeta() {
            $.getJSON(api('system/info'))
                .done(function (info) {
                    state.info = info;
                    state.defaults = info.defaults || {};
                    window.HyTransApp.defaults = state.defaults;
                    applyAdvancedToForm();
                    renderLanguageLabel();
                    window.I18nApply();
                })
                .fail(function (xhr) {
                    console.error('读取服务信息失败', xhr);
                    layer.msg(_t('msg.loadFailed', { message: apiError(xhr) }), { icon: 2, time: 4000 });
                });
        }

        /**
         * 按「本浏览器存过的偏好 + 后端默认值」重绘高级选项表单。
         * loadMeta（首屏）与「恢复默认值」按钮共用：恢复默认即先清偏好再调本函数。
         */
        function applyAdvancedToForm() {
            var d = state.defaults || {};
            var saved = HySettings.options();

            state.sampling = {
                temperature: pick(saved.temperature, d.temperature, 0.7),
                topP: pick(saved.topP, d.topP, 0.6),
                topK: pick(saved.topK, d.topK, 20),
                repetitionPenalty: pick(saved.repetitionPenalty, d.repetitionPenalty, 1.05)
            };

            $('#segmentChars').val(pick(saved.segmentChars, d.segmentChars, 480));
            $('#maxTokens').val(pick(saved.maxTokens, 0, 0));
            if (saved.promptLanguage) $('#promptLanguage').val(saved.promptLanguage);

            // 分段字数提示里的 {max} 占位符来自后端默认上限
            dom.segmentCharsHint.attr('data-i18n-params', JSON.stringify({ max: d.maxSegmentChars }));
            dom.segmentCharsHint.attr('data-i18n', 'option.segmentCharsHint');

            renderPromptStyles(state.info ? (state.info.promptStyles || []) : []);
            renderSamplingLabels();
            renderSliders(true);        // force：滑块可能已画过，重置后必须强制按新值重画
            renderInference();
        }

        /**
         * 推理引擎与 KV 缓存下拉。
         *
         * 选项清单来自后端（/api/system/info 的 engines、kvCachePolicies），前端不硬编码 ——
         * 后端将来支持新策略时，这里自动多一项；文案由 i18n 的 engine.* / kvCache.* 提供。
         * 两者都绑定在模型加载时：换值等同换模型，服务端会先卸载再加载。
         */
        function renderInference() {
            var s = state.info || {};
            var engines = (s.engines && s.engines.length) ? s.engines : ['auto', 'gpu', 'cpu'];
            var policies = (s.kvCachePolicies && s.kvCachePolicies.length) ? s.kvCachePolicies : ['none', 'memory'];
            var d = state.defaults || {};
            var saved = HySettings.options();

            keepSelectValue('#engine', function () {
                $('#engine').html(engines.map(function (id) {
                    return '<option value="' + escapeAttr(id) + '">' + escapeHtml(_t('engine.' + id)) + '</option>';
                }).join(''));
            });
            keepSelectValue('#kvCache', function () {
                $('#kvCache').html(policies.map(function (id) {
                    return '<option value="' + escapeAttr(id) + '">' + escapeHtml(_t('kvCache.' + id)) + '</option>';
                }).join(''));
            });

            // 选中项：本浏览器记住的值 → 后端默认 → 清单首项
            var engine = engines.indexOf(saved.engine) !== -1 ? saved.engine : (d.engine || engines[0]);
            if (engine) $('#engine').val(engine);
            var kvCache = policies.indexOf(saved.kvCache) !== -1 ? saved.kvCache : (d.kvCache || policies[0]);
            if (kvCache) $('#kvCache').val(kvCache);

            form.render('select');
        }

        function loadModels() {
            $.getJSON(api('models'))
                .done(function (res) {
                    state.models = res.models || [];
                    state.download = res.download || null;
                    renderModels();
                    if (!hasDownloadedModel()) {
                        layer.msg(_t('msg.noModel'), { icon: 0, time: 5000 });
                    }
                    // 打开页面时已有下载在进行（本标签页发的，或上一个标签页发的）：恢复进度弹窗
                    if (isDownloadActive(state.download)) {
                        showDownloadProgress(state.download.modelId);
                    }
                })
                .fail(function (xhr) {
                    console.error('读取模型列表失败', xhr);
                });
        }

        /** 本地是否已有至少一个模型（决定「无模型」提示与翻译预检） */
        function hasDownloadedModel() {
            return state.models.some(function (m) { return m.downloaded; });
        }

        /** 按 ID 找清单条目（含未下载的官方模型） */
        function findModel(id) {
            if (!id) return null;
            for (var i = 0; i < state.models.length; i++) {
                if (state.models[i].id === id) return state.models[i];
            }
            return null;
        }

        /** 模型显示名（找不到时退回 ID） */
        function modelName(id) {
            var m = findModel(id);
            return m ? m.name : (id || '');
        }

        function loadLanguages() {
            $.getJSON(api('translation/languages'))
                .done(function (list) {
                    state.languages = list || [];
                    renderLanguages();
                })
                .fail(function (xhr) {
                    console.error('读取语言列表失败', xhr);
                });
        }

        // ════════════════════════════════════════════════════════
        // 下拉/滑块渲染
        // ════════════════════════════════════════════════════════
        function keepSelectValue(selector, render) {
            var previous = $(selector).val();
            render();
            if (previous !== null && previous !== undefined && previous !== '') $(selector).val(previous);
            form.render('select');
        }

        function renderModels() {
            keepSelectValue('#model', function () {
                var options = state.models.map(function (m) {
                    var label = m.name;
                    if (m.sizeText) label += ' · ' + m.sizeText;
                    if (m.recommended) label += ' ★';
                    if (m.downloaded) {
                        label += ' ' + _t('option.downloaded');
                        if (m.loaded) label += ' [' + _t('option.loaded') + ']';
                    }
                    return '<option value="' + escapeAttr(m.id) + '">' + escapeHtml(label) + '</option>';
                });
                $('#model').html(options.join(''));
            });

            // 选中项：本浏览器记住且已下载 → 后端推荐（★）→ 首个已下载 → 首项
            var saved = HySettings.options().model;
            var wanted = (saved && state.models.some(function (m) { return m.id === saved && m.downloaded; }))
                ? saved
                : '';
            if (!wanted) {
                var rec = state.models.filter(function (m) { return m.recommended; })[0]
                       || state.models.filter(function (m) { return m.downloaded; })[0];
                wanted = rec ? rec.id : (state.models[0] ? state.models[0].id : '');
            }
            if (wanted) $('#model').val(wanted);
            form.render('select');
        }

        // ════════════════════════════════════════════════════════
        // 模型自动下载：选中未下载的官方模型即开始下载（后端探测
        // huggingface.co，受限自动切换 hf-mirror.com 镜像站）
        // ════════════════════════════════════════════════════════
        var downloadTimer = null;       // 下载状态轮询定时器
        var downloadLayerIndex = null;  // 下载进度弹窗索引

        /** 下载是否正在进行（探测源 / 下载中） */
        function isDownloadActive(d) {
            return !!d && (d.status === 'probing' || d.status === 'downloading');
        }

        /**
         * 选中了未下载的模型：请求后端开始下载（后台执行，关页面也继续），
         * 弹窗展示进度；失败或取消时把下拉回滚到之前的有效选择。
         */
        function startModelDownload(id) {
            if (isDownloadActive(state.download)) {
                layer.msg(_t('model.downloadBusy'), { icon: 0 });
                renderModels();
                return;
            }
            $.ajax({
                url: api('models/download'),
                type: 'POST',
                contentType: 'application/json',
                data: JSON.stringify({ id: id }),
                dataType: 'json'
            })
                .done(function (res) {
                    state.download = res.download || null;
                    if (res.started === false) {
                        // 模型已在本地（并发下载刚完成）：直接刷新清单并选中
                        HySettings.setOption('model', id);
                        loadModels();
                        return;
                    }
                    showDownloadProgress(id);
                })
                .fail(function (xhr) {
                    if (xhr && xhr.status === 409) {
                        // 已有下载在进行（另一标签页/上次选择）：转为跟踪那个下载
                        $.getJSON(api('models/download')).done(function (res) {
                            state.download = res.download || null;
                            if (isDownloadActive(state.download)) {
                                showDownloadProgress(state.download.modelId);
                                return;
                            }
                            renderModels();
                        });
                        return;
                    }
                    renderModels();
                    layer.msg(_t('model.downloadFailed', { message: apiError(xhr) }), { icon: 2, time: 5000 });
                });
        }

        /** 打开下载进度弹窗并开始轮询后端状态 */
        function showDownloadProgress(id) {
            if (downloadLayerIndex) {
                layer.close(downloadLayerIndex);
                downloadLayerIndex = null;
            }
            var html =
                '<div style="padding:16px 16px 6px;">' +
                    '<div style="margin-bottom:10px;font-weight:600;" id="dlName"></div>' +
                    '<div class="layui-progress" lay-filter="dlProgress">' +
                        '<div class="layui-progress-bar" lay-percent="0%"></div>' +
                    '</div>' +
                    '<div id="dlText" style="margin:10px 0 12px;color:#666;font-size:12px;min-height:18px;"></div>' +
                    '<div style="text-align:right;">' +
                        '<button type="button" class="layui-btn layui-btn-primary layui-btn-sm" id="dlCancel"></button>' +
                    '</div>' +
                '</div>';
            downloadLayerIndex = layer.open({
                type: 1,
                title: _t('model.downloadTitle'),
                area: '420px',
                content: html,
                success: function () {
                    $('#dlName').text(modelName(id));
                    $('#dlText').text(_t('model.preparing'));
                    $('#dlCancel').text(_t('model.cancelDownload')).on('click', function () {
                        $.post(api('models/download/cancel'));
                    });
                    element.render('progress', 'dlProgress');
                    updateDownloadText(state.download || {});
                },
                end: function () {
                    // 关闭弹窗只停跟踪，不影响后台下载；重新打开页面或再次选择该模型可继续看进度
                    stopDownloadPolling();
                    downloadLayerIndex = null;
                }
            });
            startDownloadPolling(id);
        }

        /** 轮询下载状态（900ms 一拍，终态即收尾） */
        function startDownloadPolling(id) {
            stopDownloadPolling();
            downloadTimer = setInterval(function () {
                $.getJSON(api('models/download'))
                    .done(function (res) {
                        var d = res.download || {};
                        state.download = d;
                        updateDownloadText(d);
                        if (d.status === 'completed') {
                            finishDownload(id, 'completed');
                        } else if (d.status === 'failed' || d.status === 'cancelled') {
                            finishDownload(id, d.status, d.error);
                        }
                    })
                    .fail(function () { /* 网络抖动：下一轮再试 */ });
            }, 900);
        }

        function stopDownloadPolling() {
            if (downloadTimer) {
                clearInterval(downloadTimer);
                downloadTimer = null;
            }
        }

        /** 刷新进度弹窗里的文案与进度条 */
        function updateDownloadText(d) {
            var text = $('#dlText');
            if (!text.length) return;
            if (d.status === 'probing') {
                setProgressBar(0);
                text.text(_t('model.probing'));
                return;
            }
            var pct = (typeof d.progress === 'number') ? Math.round(d.progress * 100) : 0;
            setProgressBar(pct);
            var size = formatBytes(d.receivedBytes || 0);
            if (d.totalBytes) size += ' / ' + formatBytes(d.totalBytes);
            var line = _t('model.downloading', { size: size, percent: pct + '%' });
            if (d.sourceHost) line += ' · ' + String(d.sourceHost).replace(/^https?:\/\//, '');
            text.text(line);
        }

        /** 同步进度条（纯 DOM：更新宽度与 lay-percent；未开 showpercent 时条内无文字，百分比只在下方文案显示一处） */
        function setProgressBar(pct) {
            var p = Math.max(0, Math.min(100, pct));
            var bar = $('#dlProgress .layui-progress-bar');
            if (bar.length) bar.attr('lay-percent', p + '%').css('width', p + '%');
        }

        /**
         * 下载终态收尾：弹窗内先给出文字提示（完成/取消/失败）并停留片刻，
         * 再关闭弹窗；成功则记住选择并刷新清单，失败/取消则回滚下拉。
         */
        function finishDownload(id, status, error) {
            stopDownloadPolling();
            var ok = status === 'completed';
            var afterClose = function () {
                if (ok) {
                    HySettings.setOption('model', id);
                    loadModels();
                    layer.msg(_t('model.downloadDone', { name: modelName(id) }), { icon: 1, time: 3000 });
                } else {
                    renderModels();
                }
            };
            if (downloadLayerIndex) {
                setProgressBar(ok ? 100 : 0);
                $('#dlText').text(ok
                    ? _t('model.downloadOk')
                    : (status === 'cancelled'
                        ? _t('model.cancelled')
                        : _t('model.downloadFailed', { message: error || '' })));
                setTimeout(function () {
                    if (downloadLayerIndex) {
                        layer.close(downloadLayerIndex);
                        downloadLayerIndex = null;
                    }
                    afterClose();
                }, ok ? 1500 : 2500);
            } else {
                // 弹窗已被用户手动关闭：静默收尾
                afterClose();
            }
        }

        /** 字节数格式化（下载进度展示） */
        function formatBytes(bytes) {
            if (!bytes) return '0B';
            var units = ['B', 'KB', 'MB', 'GB', 'TB'];
            var value = bytes;
            var unit = 0;
            while (value >= 1024 && unit < units.length - 1) {
                value /= 1024;
                unit++;
            }
            return (unit <= 1 ? Math.round(value) : value.toFixed(1)) + units[unit];
        }

        function renderLanguages() {
            keepSelectValue('#targetLang', function () {
                $('#targetLang').html(state.languages.map(function (l) {
                    return '<option value="' + escapeAttr(l.code) + '">' + escapeHtml(languageLabel(l)) + '</option>';
                }).join(''));
            });

            keepSelectValue('#sourceLang', function () {
                var options = ['<option value="">' + escapeHtml(_t('option.autoDetect')) + '</option>'];
                state.languages.forEach(function (l) {
                    options.push('<option value="' + escapeAttr(l.code) + '">' + escapeHtml(languageLabel(l)) + '</option>');
                });
                $('#sourceLang').html(options.join(''));
            });

            // 选中项：本浏览器记住的选择 → 后端默认（后端可能写作 Chinese / zh / 中文）
            var saved = HySettings.options();

            var target = matchLanguage(saved.targetLanguage || state.defaults.targetLanguage);
            if (target) $('#targetLang').val(target.code);

            // sourceLanguage 用 null 表示「用户没选过」；'' 表示用户就是要「自动识别」
            var savedSource = saved.sourceLanguage;
            var wantSource = (savedSource === null || savedSource === undefined)
                ? state.defaults.sourceLanguage
                : savedSource;
            var source = matchLanguage(wantSource);
            if (source) $('#sourceLang').val(source.code);

            form.render('select');
        }

        /**
         * 取某翻译语言在「当前界面语言」下的名称。
         * 优先用当前语言包里的 langNames 映射；没有则回落到该语言自己的母语名（autonym），
         * 保证任何界面语言下下拉项都能正确、可读地展示，而非写死的中/英配对。
         * @param {string} code 语言代码（如 zh、en、ja）
         * @returns {string|null} 当前界面语言下的名称，找不到返回 null
         */
        function uiLanguageName(code) {
            var packs = window.I18N_LANGS || {};
            var pack = packs[window.I18nCurrent()];
            if (pack && pack.langNames && pack.langNames[code]) return pack.langNames[code];
            return null;
        }

        /**
         * 翻译语言下拉项的展示文本：以当前界面语言名称为主，母语名（autonym）作补充说明。
         * 例如界面为英文时显示「Chinese（中文）」，界面为中文时显示「中文」。
         * @param {object} language 语言描述对象（含 code / nativeName / englishName）
         * @returns {string} 下拉项文本
         */
        function languageLabel(language) {
            var code = language.code || '';
            var native = language.nativeName || language.englishName || code;
            var uiName = uiLanguageName(code);
            var primary = uiName || native;
            var secondary = (uiName && uiName !== native) ? native : null;
            return secondary ? (primary + '（' + secondary + '）') : primary;
        }

        function matchLanguage(value) {
            if (!value) return null;
            var want = String(value).trim().toLowerCase();
            for (var i = 0; i < state.languages.length; i++) {
                var l = state.languages[i];
                if (String(l.code || '').toLowerCase() === want) return l;
                if (String(l.englishName || '').toLowerCase() === want) return l;
                if (String(l.chineseName || '').toLowerCase() === want) return l;
            }
            return null;
        }

        function renderPromptStyles(styles) {
            keepSelectValue('#promptStyle', function () {
                $('#promptStyle').html(styles.map(function (id) {
                    return '<option value="' + escapeAttr(id) + '">' + escapeHtml(_t('style.' + id)) + '</option>';
                }).join(''));
            });

            var saved = HySettings.options().promptStyle;
            if (saved && styles.indexOf(saved) !== -1) $('#promptStyle').val(saved);
            form.render('select');
        }

        /**
         * 渲染或同步 4 个采样滑块。
         *
         * 为什么不能一开始就渲染：高级选项默认折叠，折叠时容器 display:none、宽度为 0，
         * 而 layui 滑块是按容器宽度计算刻度与滑块位置的 —— 此时渲染会画出一条 0 宽度的
         * 滑块，展开后就错位了。所以只在面板真正展开后渲染。
         *
         * 用面板自己的展开状态（layui 用 .layui-show 标记）判断，而不是"点过展开按钮"这种
         * 事件痕迹 —— 否则面板若一开始就是展开的，滑块就永远渲染不出来。
         *
         * 已渲染过就不再重建（layui 每次 render 都会重建容器内容，重建会把用户拖过的
         * 位置重置掉）；只有元信息晚到时（force）才重刷一次，把保存的采样值补上。
         */
        function renderSliders(force) {
            if (slidersDrawn && !force) return;
            if (!advancedExpanded()) return;
            slidersDrawn = true;

            var s = state.sampling;

            slider.render({
                elem: '#temperatureSlider', min: 0, max: 1.5, step: 0.05, value: s.temperature,
                change: function (v) {
                    state.sampling.temperature = round(v, 2);
                    setText($('#temperatureVal'), state.sampling.temperature.toFixed(2));
                    HySettings.setOption('temperature', state.sampling.temperature);
                }
            });

            slider.render({
                elem: '#topPSlider', min: 0.1, max: 1, step: 0.05, value: s.topP,
                change: function (v) {
                    state.sampling.topP = round(v, 2);
                    setText($('#topPVal'), state.sampling.topP.toFixed(2));
                    HySettings.setOption('topP', state.sampling.topP);
                }
            });

            slider.render({
                elem: '#topKSlider', min: 0, max: 100, step: 1, value: s.topK,
                change: function (v) {
                    state.sampling.topK = Math.round(v);
                    setText($('#topKVal'), String(state.sampling.topK));
                    HySettings.setOption('topK', state.sampling.topK);
                }
            });

            slider.render({
                elem: '#repetitionPenaltySlider', min: 1, max: 1.5, step: 0.01, value: s.repetitionPenalty,
                change: function (v) {
                    state.sampling.repetitionPenalty = round(v, 2);
                    setText($('#repetitionPenaltyVal'), state.sampling.repetitionPenalty.toFixed(2));
                    HySettings.setOption('repetitionPenalty', state.sampling.repetitionPenalty);
                }
            });
        }

        /** 高级选项面板是否已展开（layui 的折叠面板用 .layui-show 标记展开态） */
        function advancedExpanded() {
            var item = document.querySelector('.layui-colla-item');
            return !!item && item.classList.contains('layui-show');
        }

        function renderSamplingLabels() {
            setText($('#temperatureVal'), Number(state.sampling.temperature).toFixed(2));
            setText($('#topPVal'), Number(state.sampling.topP).toFixed(2));
            setText($('#topKVal'), String(state.sampling.topK));
            setText($('#repetitionPenaltyVal'), Number(state.sampling.repetitionPenalty).toFixed(2));
        }

        function renderLanguageLabel() {
            var code = window.I18nCurrent();
            var info = window.I18nCurrentInfo ? window.I18nCurrentInfo() : null;
            setText(dom.languageLabel, info ? (info.nativeName || info.code) : code);
        }

        // ── 皮肤主题（亮 / 暗） ────────────────────────────────
        function applyTheme() {
            var theme = HySettings.get('theme', 'light');
            document.documentElement.setAttribute('data-theme', theme === 'dark' ? 'dark' : 'light');
            if (dom.themeIcon && dom.themeIcon.length) {
                dom.themeIcon.removeClass('layui-icon-moon layui-icon-light');
                dom.themeIcon.addClass(theme === 'dark' ? 'layui-icon-light' : 'layui-icon-moon');
            }
        }

        function toggleTheme() {
            var cur = document.documentElement.getAttribute('data-theme') === 'dark' ? 'dark' : 'light';
            HySettings.set('theme', cur === 'dark' ? 'light' : 'dark');
            applyTheme();
        }

        // ── 流式光标 ───────────────────────────────────────────
        function setStreaming(on) {
            state.streaming = !!on;
            var c = document.getElementById(OUTPUT_ID);
            if (c) c.classList.toggle('hy-streaming', state.streaming);
        }

        /**
         * 语言下拉：选项来自引擎登记的目录 ——
         * 也就是服务端启动时扫描 wwwroot/js/i18n/lang/ 目录得到的结果，
         * 前端不硬编码任何语言；往那个目录里放一个语言包，这里就会多一项。
         */
        function renderLanguagePicker() {
            var list = window.I18nLanguages ? window.I18nLanguages() : [];
            dropdown.render({
                elem: '#btnLanguage',
                data: list.map(function (l) {
                    return { title: l.nativeName || l.code, code: l.code };
                }),
                click: function (item) {
                    window.I18nSwitch(item.code);   // 选择即写入本浏览器
                    layer.msg(_t('msg.langSwitched'), { icon: 1, time: 1200 });
                }
            });
            renderLanguageLabel();
        }

        // ════════════════════════════════════════════════════════
        // 语言切换后的重绘
        // ════════════════════════════════════════════════════════
        function onLanguageChanged() {
            document.title = _t('app.name');
            renderLanguageLabel();
            renderSamplingLabels();
            if (state.info) {
                renderPromptStyles(state.info.promptStyles || []);
                renderLanguages();
                renderInference();
            }
            renderModels();
            renderStats();
            if (!state.segments.length) renderPlaceholders();
        }

        // ════════════════════════════════════════════════════════
        // 翻译
        // ════════════════════════════════════════════════════════
        function buildRequest(text) {
            return {
                text: text,
                fileName: state.fileName || null,
                sourceLanguage: $('#sourceLang').val() || null,
                targetLanguage: $('#targetLang').val() || null,
                model: $('#model').val() || null,
                engine: $('#engine').val() || null,
                kvCache: $('#kvCache').val() || null,
                segmentChars: intOrNull($('#segmentChars').val()),
                maxTokensPerSegment: intOrNull($('#maxTokens').val()),
                temperature: state.sampling.temperature,
                topP: state.sampling.topP,
                topK: state.sampling.topK,
                repetitionPenalty: state.sampling.repetitionPenalty,
                promptStyle: $('#promptStyle').val() || null,
                promptLanguage: $('#promptLanguage').val() || null,
                glossary: $('#glossary').val() || null,
                background: $('#background').val() || null,
                targetStyle: $('#targetStyle').val() || null
            };
        }

        /**
         * 开始翻译。同一时刻只跑一个任务：
         * 先查询当前是否已有任务在跑，有则引导「停止当前 → 重试」，避免无谓的失败。
         */
        function startTranslate() {
            var text = currentSourceText();
            if (!text.trim()) {
                layer.msg(_t('msg.noText'), { icon: 0 });
                return;
            }
            // 客户端预检（按 UTF-8 字节算，与接口层 MaxTextBytes 同口径）；
            // 超限由服务端返回精确错误，这里只是提前拦截「注定被拒绝」的请求
            var maxBytes = state.defaults && state.defaults.maxTextBytes;
            var textBytes = new TextEncoder().encode(text).length;
            if (maxBytes && textBytes > maxBytes) {
                layer.alert(_t('msg.textTooLong'), { icon: 2 });
                return;
            }
            if (!hasDownloadedModel()) {
                layer.msg(_t('msg.noModel'), { icon: 2, time: 4000 });
                return;
            }
            var chosenModel = findModel($('#model').val());
            if (chosenModel && !chosenModel.downloaded) {
                // 选了还没下完的模型：等下载收尾再翻（下载进度见弹窗/再次选择该模型）
                layer.msg(_t('model.notDownloaded', { name: chosenModel.name }), { icon: 0, time: 4000 });
                return;
            }
            beginTranslate(text);
        }

        function beginTranslate(text) {
            resetOutput();
            setBusy(true);
            applyJobStatus('Preparing');
            state.segments = [];
            state.doneCount = 0;
            state.totalCount = 0;
            state.startedAt = Date.now();
            startTimer();
            // 会话暂存：刷新后凭它接续本任务 / 恢复结果（jobId 由首个流式事件补上）
            writeSessionJob({ jobId: null, kind: 'inline', sourceName: state.fileName || '', source: text });

            if (connected()) {
                // 流式翻译：服务端把 JobEvent 推回本连接（首个即 preparing，终态为 done/cancelled/error）。
                // 失败必须可见：消息超限（SignalR 会直接断开连接）等情况若吞掉错误，
                // 界面就表现为「点了翻译没反应」（2026-09-30 踩坑）。
                // 不自动重提：连接断开时后台任务可能仍在跑，重提只会撞上「模型正忙」。
                var pending = state.connection.invoke('Translate', buildRequest(text));
                if (pending && pending.then) {
                    pending.catch(function (err) {
                        console.error('Translate 调用失败', err);
                        var message = (err && err.message) ? String(err.message) : String(err || '');
                        failJob(message || _t('msg.loadFailed', { message: 'SignalR' }));
                    });
                }
            } else {
                // 实时通道不可用：退化为一步到位的 REST 翻译（无流式进度）
                syncTranslate(text);
            }
        }

        /** SignalR 不可用时的兜底：POST /api/translation，等待整篇结果 */
        function syncTranslate(text) {
            $.ajax({
                url: api('translation'),
                type: 'POST',
                contentType: 'application/json; charset=UTF-8',
                dataType: 'json',
                data: JSON.stringify(buildRequest(text))
            })
                .done(function (res) {
                    state.jobId = res.jobId;
                    state.totalCount = res.totalSegments || 0;
                    state.doneCount = res.completedSegments || state.totalCount;
                    setText($('#statModel'), formatModel(res.model, res.modelQuantization));
                    setText(dom.footMeta, (res.sourceLanguage || _t('option.autoDetect')) + ' → ' + res.targetLanguage);
                    showPlainResult(res.translation || '');
                    applyJobStatus(res.status);
                    finishBusy();
                    layer.msg(_t('msg.started'), { icon: 1, time: 900 });
                })
                .fail(function (xhr) {
                    var info = errorInfo(xhr);
                    // 409 = 模型正忙（同一时刻只跑一个任务）：直接提示，不自动重试
                    if (xhr.status === 409) {
                        failJob(info.error || apiError(xhr));
                        return;
                    }
                    failJob(info.error || apiError(xhr));
                });
        }

        function stopTranslate() {
            cancelCurrent();
        }

        function failJob(message) {
            applyJobStatus('Failed');
            finishBusy();
            layer.msg(_t('msg.failed', { message: message || '' }), { icon: 2, time: 4000 });
            syncStopAvailability();             // 409「忙则拒绝」后，占用模型的任务必须仍可从界面停止
        }

        /**
         * 查询服务端是否确有任务在跑；有则保持「终止翻译」按钮可用。
         *
         * 为什么需要：按钮默认只在「本页发起的任务」运行期间可用（setBusy 控制），
         * 但占用模型的可能是别的客户端、或本页刷新前留下的任务——本页没有它的
         * 事件流，收到的只是「忙则拒绝」错误。若按钮保持禁用，用户将面对
         * 「点了终止没反应、模型却被一直占用」的死局（真实踩过）。
         * Cancel / cancel 停的是「当前任务」，不要求任务必须由本页发起。
         */
        function syncStopAvailability() {
            $.getJSON(api('translation/current?brief=1'))
                .done(function (snap) {
                    if (snap && !snap.idle) dom.btnStop.attr('disabled', null);
                });
        }

        // ════════════════════════════════════════════════════════
        // 刷新接续：页面装入后检查是否有进行中的翻译任务
        //
        // 任务在服务端后台执行，客户端断开（刷新）只停止事件推送、不终止任务；
        // 因此刷新后凭 GET current 快照即可无损接上。种类分派：
        //   · file     → 打开大文件弹窗，复用其「轮询进度 → 终态自动下载」链路
        //   · inline   → 主窗口接续：轮询全量快照恢复原文+已译文本与进度
        //   · mcp(-file) → 主窗口只显示进度（结果经 MCP 入口交付，不接管编辑区）
        // 空闲时再查会话暂存：上一次任务若已结束且结果仍在服务端（只保留最近一次），
        // 以确认制恢复（不静默覆盖编辑区内容）。
        // ════════════════════════════════════════════════════════

        function resumeCheck() {
            $.getJSON(api('translation/current?brief=1'))
                .done(function (snap) {
                    if (snap && !snap.idle) {
                        resumeActive(snap);
                        return;
                    }
                    recoverFinished();
                })
                .fail(function () { /* 服务不可达：无任务可接续 */ });
        }

        /** 有任务正在执行：接续显示 */
        function resumeActive(snap) {
            var kind = snap.kind || 'inline';

            // 文件任务：交给大文件弹窗（它有自己的进度条与自动下载收尾）
            if (kind === 'file' && window.HyBigFileModal && window.HyBigFileModal.resume) {
                window.HyBigFileModal.resume(snap.jobId, snap.sourceName || '');
                return;
            }

            // inline / mcp / mcp-file：主窗口接续
            state.jobId = snap.jobId;
            state.doneCount = snap.completedSegments || 0;
            state.totalCount = snap.totalSegments || 0;
            state.startedAt = Date.now() - (snap.elapsedMs || 0);   // 已用时间从快照回推
            state.resumeSettled = false;
            state.resumeKind = kind;
            setBusy(true);
            applyJobStatus(snap.status);
            showNote(snap.note || _t('output.translating'));
            setStreaming(true);
            startTimer();
            layer.msg(_t('msg.resumed'), { icon: 0, time: 2500 });

            // inline 轮询全量快照（每秒一次可接受）：顺带恢复原文与已译文本；
            // mcp(-file) 只轮询轻量 brief：任务结果经 MCP 交付，界面不接管编辑区
            startResumePoll(kind === 'inline');
        }

        function startResumePoll(withText) {
            var sourceRestored = !withText;     // inline：首个全量快照里恢复原文
            if (state.resumeTimer) clearInterval(state.resumeTimer);
            state.resumeTimer = setInterval(function () {
                var url = withText ? api('translation/current') : api('translation/current?brief=1');
                $.getJSON(url).done(function (cur) {
                    if (!cur || cur.idle) {
                        finishResume(null);     // 终态竞态：任务已清出「当前任务」位
                        return;
                    }
                    state.doneCount = cur.completedSegments || 0;
                    state.totalCount = cur.totalSegments || 0;
                    state.startedAt = Date.now() - (cur.elapsedMs || 0);
                    if (cur.note) setText(dom.streamNoteText, cur.note);
                    if (withText) {
                        if (!sourceRestored) {
                            sourceRestored = true;
                            restoreSourceFromSnapshot(cur);
                        }
                        if (typeof cur.translation === 'string') setOutputText(cur.translation);
                    }
                    renderStats();
                    if (cur.isFinished || /Completed|Cancelled|Failed/i.test(cur.status || '')) {
                        finishResume(cur);
                    }
                });
                // 请求失败（网络抖动）不中断：下个周期继续轮询
            }, 1000);
        }

        /** 接续任务收尾（终态快照 / idle 双路触发，只执行一次） */
        function finishResume(cur) {
            if (state.resumeSettled) return;
            state.resumeSettled = true;
            if (state.resumeTimer) {
                clearInterval(state.resumeTimer);
                state.resumeTimer = null;
            }
            var status = cur ? String(cur.status || '') : 'Completed';
            if (cur && typeof cur.translation === 'string') setOutputText(cur.translation);
            setStreaming(false);
            setBusy(false);
            stopTimer();
            applyJobStatus(status);
            renderStats();

            if (status === 'Failed') {
                layer.msg(_t('msg.failed', { message: (cur && cur.error) || '' }), { icon: 2, time: 4000 });
                return;
            }

            if (state.resumeKind === 'mcp' || state.resumeKind === 'mcp-file') {
                layer.msg(_t('msg.resumeMcpDone'), { icon: 1, time: 3000 });
                return;
            }
            if (cur) return;    // 终态快照已带全文（inline 轮询路径），无需补救

            // idle 兜底：终态与「_active 置空」之间有竞态，用暂存 jobId 经 file-result 补最终译文。
            // 只信「与接续任务同一只」的暂存（否则会把上一次任务的旧结果错当本次结果）
            var saved = readSessionJob();
            if (state.resumeKind === 'inline' && saved && saved.jobId && saved.jobId === state.jobId) {
                fetch('./api/translation/file-result?job=' + encodeURIComponent(saved.jobId))
                    .then(function (r) { return r.ok ? r.text() : null; })
                    .then(function (text) { if (text !== null) setOutputText(text); })
                    .catch(function () { /* 结果已不在服务端：保留界面已有内容 */ });
            }
        }

        /** 从全量快照的分段还原原文（不变式：Σ(Prefix+Source+Separator) === 原文逐字符） */
        function restoreSourceFromSnapshot(snap) {
            var segs = snap && snap.segments;
            if (!segs || !segs.length) return;
            var out = [];
            segs.forEach(function (s) {
                out.push(s.prefix || '');
                out.push(s.source || '');
                out.push(s.separator || '');
            });
            dom.source.val(out.join(''));
            state.fileName = snap.sourceName || '';
            updateInputMeta();
        }

        /** 接续模式专用：整体替换译文区文本（每秒一次，O(全文)，inline 全文较小可接受） */
        function setOutputText(text) {
            if (!text) return;
            ensureOutputNodes();
            outNodes.done.textContent = text;
            outNodes.live.textContent = '';
            scrollOutputToEnd();
        }

        /** 空闲但暂存里有已结束的任务：确认制恢复（不静默覆盖编辑区） */
        function recoverFinished() {
            var saved = readSessionJob();
            if (!saved || !saved.jobId) return;

            if (saved.kind === 'inline') {
                var current = dom.source.val() || '';
                if (current.trim()) return;     // 编辑区已有内容：不打扰
                layer.confirm(_t('msg.resumeAskInline'), { icon: 3, title: _t('app.confirm') }, function (index) {
                    layer.close(index);
                    recoverInlineResult(saved);
                }, function () { clearSessionJob(); });
                return;
            }

            if (saved.kind === 'file' && window.HyBigFileModal && window.HyBigFileModal.recover) {
                layer.confirm(_t('msg.resumeAskFile', { name: saved.sourceName || saved.jobId }), { icon: 3, title: _t('app.confirm') }, function (index) {
                    layer.close(index);
                    window.HyBigFileModal.recover(saved.jobId, saved.sourceName || '');
                }, function () { clearSessionJob(); });
            }
            // mcp / mcp-file：结果经 MCP 入口交付，WebUI 不主动恢复
        }

        /** 空窗补救：内联任务刚结束就刷新 → 凭暂存 jobId 从 file-result 取回译文 */
        function recoverInlineResult(saved) {
            fetch('./api/translation/file-result?job=' + encodeURIComponent(saved.jobId))
                .then(function (r) {
                    if (!r.ok) return null;
                    var status = r.headers.get('X-Translation-Status') || 'Completed';
                    return r.text().then(function (t) { return { status: status, text: t }; });
                })
                .then(function (res) {
                    if (!res) {
                        clearSessionJob();
                        layer.msg(_t('msg.resumeGone'), { icon: 0, time: 2500 });
                        return;
                    }
                    dom.source.val(saved.source || '');
                    state.fileName = saved.sourceName || '';
                    updateInputMeta();
                    setOutputText(res.text);
                    applyJobStatus(res.status);
                    clearSessionJob();
                    layer.msg(_t('msg.resumeRestored'), { icon: 1, time: 2000 });
                })
                .catch(function () { clearSessionJob(); });
        }

        function setBusy(busy) {
            state.running = busy;
            dom.btnTranslate.attr('disabled', busy ? true : null);
            if (busy) dom.btnTranslate.addClass('layui-btn-disabled');
            else dom.btnTranslate.removeClass('layui-btn-disabled');
            dom.btnStop.attr('disabled', busy ? null : true);
            dom.streamNote.attr('hidden', busy ? null : true);
            if (busy) setText(dom.streamNoteText, _t('output.translating'));
        }

        /** 显示进度提示条并给出说明文案 */
        function showNote(text) {
            setText(dom.streamNoteText, text);
            dom.streamNote.attr('hidden', null);
        }

        function finishBusy() {
            setBusy(false);
            stopTimer();
            renderStats();
        }

        /**
         * 停止当前翻译（已译部分保留，可继续查看）。
         * 当前任务面板已移除：翻译状态见译文区元信息（#outputMeta），
         * 「终止翻译」按钮经 SignalR Cancel / REST cancel 直接停止。
         *
         * 兜底：REST 路径没有事件流、SignalR 路径也可能碰上「无任务可停」
         * （按钮可用的瞬间任务自己结束了）——停一次后主动对齐服务端状态，
         * 空闲就把「正在请求终止…」的提示收掉，别让它一直挂着。
         */
        function cancelCurrent() {
            // 点击即给一条明确反馈：终止请求已发出（服务端真正停下来的
            // 确认提示由 cancelled 事件 / settle 兜底负责）
            layer.msg(_t('msg.cancelRequested'), { icon: 0, time: 2000 });
            showNote(_t('msg.cancelRequested'));
            var settle = function () {
                setTimeout(function () {
                    $.getJSON(api('translation/current?brief=1')).done(function (snap) {
                        if (!snap || snap.idle) finishBusy();
                    });
                }, 600);
            };
            if (connected()) {
                var p = invoke('Cancel');
                if (p && p.then) p.then(settle, settle);
            } else {
                $.post(api('translation/cancel')).always(settle);
            }
        }

        // ════════════════════════════════════════════════════════
        // 输出渲染（增量式）
        //
        // 译文 DOM 只有两个 span：<done> 已完成段（只追加、永不改写）与
        // <live> 当前流式段。delta 事件只重写 <live>（成本 O(段长)），
        // segment_done 把定稿文本一次性追加进 <done>（追加 O(1)）——
        // 每条事件的处理成本与全文长度无关，1MB+ 的大文档不会拖垮主线程。
        // （旧实现每条 delta 都「重拼整篇全文 + .text() 整体替换 DOM」，
        //   复杂度 O(全文×事件数)，正是大文件卡死的元凶，已移除。）
        // ════════════════════════════════════════════════════════
        var outNodes = { pre: null, done: null, live: null };

        function ensureOutputNodes() {
            var container = document.getElementById(OUTPUT_ID);
            if (!container) return;
            var ph = container.querySelector('.hy-placeholder');
            if (ph) ph.remove();
            var pre = container.querySelector('.hy-trans');
            if (!pre) {
                pre = document.createElement('pre');
                pre.className = 'hy-trans';
                container.appendChild(pre);
            }
            if (pre !== outNodes.pre || !pre.querySelector('.hy-trans-done')) {
                pre.textContent = '';
                outNodes.done = document.createElement('span');
                outNodes.done.className = 'hy-trans-done';
                outNodes.live = document.createElement('span');
                outNodes.live.className = 'hy-trans-live';
                pre.appendChild(outNodes.done);
                pre.appendChild(outNodes.live);
                outNodes.pre = pre;
            }
            // 流式光标挂在容器上（CSS 由 .hy-output.hy-streaming .hy-caret 控制）
            if (!container.querySelector('.hy-caret')) {
                var caret = document.createElement('span');
                caret.className = 'hy-caret';
                container.appendChild(caret);
            }
        }

        /** 已完成段：追加一个文本节点（不改写既有内容，成本与全文长度无关） */
        function appendDoneText(text) {
            if (!text) return;
            outNodes.done.appendChild(document.createTextNode(text));
        }

        function resetOutput() {
            state.segments = [];
            state.doneCount = 0;
            state.totalCount = 0;
            state.jobId = null;
            outNodes = { pre: null, done: null, live: null };
            setText(dom.footMeta, '');
            setText($('#statModel'), '');
            setText(dom.outputMeta, _t('output.ready'));
            element.progress('hyProgress', '0%');
            renderStats();
            renderPlaceholders();
            setStreaming(false);
        }

        function renderPlaceholders() {
            var html = '<div class="hy-placeholder">' +
                '<div class="hy-ph-icon"><i class="layui-icon layui-icon-form"></i></div>' +
                '<p>' + escapeHtml(_t('output.placeholder')) + '</p>' +
                '<p class="hy-ph-sub">' + escapeHtml(_t('output.placeholderSub')) + '</p>' +
                '</div>';
            $('#' + OUTPUT_ID).html(html);
            outNodes = { pre: null, done: null, live: null };
        }

        /** 直接展示整段译文（REST 兜底路径，无流式分段） */
        function showPlainResult(text) {
            if (!text) { renderPlaceholders(); setStreaming(false); return; }
            ensureOutputNodes();
            appendDoneText(text);
            outNodes.live.textContent = '';
            setStreaming(false);
        }

        function upsertSegment(index, source) {
            if (index === null || index === undefined) return;
            var seg = state.segments[index];
            if (!seg) {
                state.segments[index] = {
                    index: index,
                    source: source || '',
                    translation: '',
                    separator: '',
                    done: false
                };
            } else if (source && !seg.source) {
                seg.source = source;    // 译文视图不再显示原文，仅保留数据
            }
        }

        function scrollOutputToEnd() {
            var container = document.getElementById(OUTPUT_ID);
            if (!container || container.offsetParent === null) return;
            // 用户手动上翻时不打扰
            var atBottom = container.scrollHeight - container.scrollTop - container.clientHeight < 120;
            if (atBottom) container.scrollTop = container.scrollHeight;
        }

        function fullTranslation() {
            var out = [];
            state.segments.forEach(function (seg) {
                if (!seg || !seg.done) return;
                out.push(seg.translation);
                if (seg.separator) out.push(seg.separator);
            });
            return out.join('');
        }

        // ── 状态与统计 ─────────────────────────────────────────

        function applyJobStatus(status) {
            var key = statusKey(status);
            setText(dom.outputMeta, _t('status.' + key));
        }

        function statusKey(status) {
            var s = String(status || '').toLowerCase();
            if (s.indexOf('prepar') === 0) return 'preparing';
            if (s.indexOf('loading') === 0) return 'loading';
            if (s.indexOf('running') === 0) return 'running';
            if (s.indexOf('completed') === 0) return 'completed';
            if (s.indexOf('cancel') === 0) return 'cancelled';
            if (s.indexOf('failed') === 0) return 'failed';
            return 'idle';
        }

        function renderStats() {
            var total = state.totalCount || state.segments.length || 0;
            var done = state.doneCount || countDone();
            setText(dom.statSegments, _t('output.segments', { done: done, total: total }));

            var elapsed = state.startedAt ? (Date.now() - state.startedAt) / 1000 : 0;
            setText(dom.statElapsed, _t('output.elapsed', { time: formatHms(elapsed) }));
            setText(dom.statSpeed, _t('output.speed', { value: elapsed > 0.3 ? Math.round(currentChars() / elapsed) : 0 }));

            if (done > 0 && total > done) {
                setText(dom.statEta, _t('output.eta', { time: formatHms(elapsed / done * (total - done)) }));
            } else {
                setText(dom.statEta, '');
            }

            var percent = total > 0 ? Math.min(100, Math.round(done / total * 100)) : 0;
            element.progress('hyProgress', percent + '%');

            // 流式期间由定时器顺带把滚动钉在底部（每条事件都滚会拖慢大文档）
            if (state.streaming) scrollOutputToEnd();
        }

        /** 秒 → H:mm:ss */
        function formatHms(sec) {
            sec = Math.round(Math.max(0, sec));
            var h = sec / 3600 | 0, m = (sec / 60 | 0) % 60, s = sec % 60;
            return (h ? h + ':' : '') + ('0' + m).slice(-2) + ':' + ('0' + s).slice(-2);
        }

        function countDone() {
            var n = 0;
            state.segments.forEach(function (s) { if (s && s.done) n++; });
            return n;
        }

        function currentChars() {
            var n = 0;
            state.segments.forEach(function (s) { if (s) n += (s.translation || '').length; });
            return n;
        }

        function startTimer() {
            stopTimer();
            state.timer = setInterval(renderStats, 250);
        }

        function stopTimer() {
            if (state.timer) {
                clearInterval(state.timer);
                state.timer = null;
            }
        }

        // ════════════════════════════════════════════════════════
        // 事件绑定
        // ════════════════════════════════════════════════════════
        function bindEvents() {
            dom.source.on('input', function () {
                state.fileName = '';        // 手动编辑后不再沿用拖入的文件名（来源名/下载命名）
                updateInputMeta();
            });
            dom.btnTranslate.on('click', startTranslate);
            dom.btnStop.on('click', stopTranslate);

            $('#btnClear').on('click', function () {
                layer.confirm(_t('msg.confirmClear'), { icon: 3, title: _t('app.confirm') }, function (index) {
                    layer.close(index);
                    dom.source.val('');
                    state.fileName = '';
                    clearSessionJob();      // 编辑区已清空：上次任务的会话暂存一并作废
                    updateInputMeta();
                    resetOutput();
                    layer.msg(_t('msg.cleared'), { icon: 1, time: 1000 });
                });
            });

            $('#btnSwap').on('click', function () {
                var source = $('#sourceLang').val();
                var target = $('#targetLang').val();
                if (!source) {
                    layer.msg(_t('msg.swapNeedsSource'), { icon: 0, time: 2600 });
                    return;
                }
                $('#sourceLang').val(target);
                $('#targetLang').val(source);
                form.render('select');
                persistLanguageChoice();
            });

            $('#btnRefreshModels').on('click', function () {
                $.ajax({ url: api('models/refresh'), type: 'POST', dataType: 'json' })
                    .done(function (res) {
                        loadModels();
                        layer.msg(_t('msg.modelsRefreshed', { count: res.count }), { icon: 1 });
                    })
                    .fail(function (xhr) {
                        layer.msg(_t('msg.loadFailed', { message: apiError(xhr) }), { icon: 2 });
                    });
            });

            // 高级选项：恢复默认值（确认后清掉本浏览器记住的高级偏好，回到后端默认）
            $('#btnResetAdvanced').on('click', function (e) {
                e.stopPropagation();    // 按钮在折叠面板标题里，别触发展开/收起
                layer.confirm(_t('msg.confirmResetAdvanced'), { icon: 3, title: _t('app.confirm') }, function (index) {
                    layer.close(index);
                    resetAdvancedOptions();
                });
            });

            $('#btnCopy').on('click', copyResult);
            $('#btnDownload').on('click', downloadResult);

            // 弹窗内的复制按钮（MCP 弹窗的 DOM 与逻辑见 mcp-modal.js，复制行为复用此委托）
            $(document).on('click', '[data-copy]', function () {
                var target = $($(this).attr('data-copy'));
                copyText(target.text() || '');
            });

            // 高级选项折叠面板展开后再渲染滑块（折叠时容器宽度为 0）
            element.on('collapse(hyAdvanced)', function (data) {
                if (!data || !data.show) return;
                renderSliders();
            });

            // 文件拖拽：文本文件直接提取文本填入输入框（超出上限拒绝加载）
            ['dragenter', 'dragover'].forEach(function (type) {
                dom.sourceDrop.on(type, function (e) {
                    e.preventDefault();
                    e.stopPropagation();
                    dom.sourceDrop.addClass('is-drag');
                });
            });
            ['dragleave', 'drop'].forEach(function (type) {
                dom.sourceDrop.on(type, function (e) {
                    e.preventDefault();
                    e.stopPropagation();
                    dom.sourceDrop.removeClass('is-drag');
                });
            });
            dom.sourceDrop.on('drop', function (e) {
                var files = e.originalEvent.dataTransfer && e.originalEvent.dataTransfer.files;
                if (files && files.length) readFile(files[0]);
            });

            // 皮肤主题切换
            dom.btnTheme.on('click', toggleTheme);

            // 输入框变化时同步字符数
            updateInputMeta();
        }

        /**
         * 面板偏好的持久化：任何改动都写进本浏览器（localStorage），刷新后仍然生效。
         * 服务端只提供「默认值」——用户动过的项以浏览器里的记录为准。
         */
        function bindOptionPersistence() {
            form.on('select(sourceLang)', persistLanguageChoice);
            form.on('select(targetLang)', persistLanguageChoice);

            form.on('select(model)', function (data) {
                var id = data.value || '';
                var model = findModel(id);
                if (model && !model.downloaded) {
                    // 选了尚未下载的模型：自动下载（进度弹窗跟踪，失败/取消自动回滚选择）
                    startModelDownload(id);
                    return;
                }
                HySettings.setOption('model', id);
            });
            form.on('select(engine)', function (data) {
                HySettings.setOption('engine', data.value || '');
            });
            form.on('select(kvCache)', function (data) {
                HySettings.setOption('kvCache', data.value || '');
            });
            form.on('select(promptStyle)', function (data) {
                HySettings.setOption('promptStyle', data.value || '');
            });
            form.on('select(promptLanguage)', function (data) {
                HySettings.setOption('promptLanguage', data.value || 'zh');
            });

            $('#segmentChars').on('change', function () {
                HySettings.setOption('segmentChars', intOrNull(this.value));
            });
            $('#maxTokens').on('change', function () {
                HySettings.setOption('maxTokens', intOrNull(this.value) || 0);
            });
        }

        /** 「恢复默认值」覆盖的高级选项键（只动高级区，不碰翻译设置里的模型/语向） */
        var ADVANCED_OPTION_KEYS = ['segmentChars', 'maxTokens', 'promptStyle', 'promptLanguage',
            'temperature', 'topP', 'topK', 'repetitionPenalty', 'targetStyle'];

        /**
         * 高级选项恢复默认：把偏好写回默认形态（null/'' = 未选择 → 用后端默认），再重绘表单。
         * 术语表 / 背景信息不持久化（仅随请求提交），无需清理。
         */
        function resetAdvancedOptions() {
            var partial = {};
            ADVANCED_OPTION_KEYS.forEach(function (key) {
                partial[key] = HySettings.DEFAULTS.options[key];
            });
            HySettings.patchOptions(partial);

            // 提示词模板是「有值就保留」的下拉，先清空当前选择，apply 时才会回到首项
            $('#promptStyle').val('');
            applyAdvancedToForm();
            if (state.applyTargetStyle) state.applyTargetStyle();   // 目标风格多选组件按默认形态重绘
            layer.msg(_t('msg.advancedReset'), { icon: 1, time: 1500 });
        }

        /** 记住源语言 / 目标语言的选择（'' 表示「自动识别」这一明确选择） */
        function persistLanguageChoice() {
            HySettings.patchOptions({
                sourceLanguage: $('#sourceLang').val() || '',
                targetLanguage: $('#targetLang').val() || ''
            });
        }

        /**
         * 拖入文本文件：直接提取文本填入输入框；超出上限弹提示框拒绝加载。
         * 文件名记入 state（用作来源名与下载默认文件名）。
         */
        function readFile(file) {
            var maxBytes = state.defaults && state.defaults.maxTextBytes;
            if (maxBytes && file.size > maxBytes) {
                layer.alert(_t('msg.fileTooLarge'), { icon: 2 });
                return;
            }
            var fr = new FileReader();
            fr.onload = function () {
                state.fileName = file.name;
                dom.source.val(fr.result);
                updateInputMeta();
                layer.msg(file.name, { icon: 1, time: 1200 });
            };
            fr.onerror = function () {
                layer.msg(_t('msg.fileReadFailed', { message: file.name }), { icon: 2 });
            };
            fr.readAsText(file, 'utf-8');
        }

        /** 当前待翻译的源内容：文本框即唯一来源 */
        function currentSourceText() {
            return dom.source.val() || '';
        }

        function updateInputMeta() {
            var length = (dom.source.val() || '').length;
            setText(dom.inputMeta, _t('input.charCount', { count: length }));
        }

        // ── 复制 / 下载 ────────────────────────────────────────

        function copyResult() {
            var text = fullTranslation();
            if (!text) {
                layer.msg(_t('msg.nothingToCopy'), { icon: 0 });
                return;
            }
            copyText(text);
        }

        function copyText(text) {
            if (!text) {
                layer.msg(_t('msg.nothingToCopy'), { icon: 0 });
                return;
            }
            if (navigator.clipboard && navigator.clipboard.writeText) {
                navigator.clipboard.writeText(text)
                    .then(function () { layer.msg(_t('msg.copied'), { icon: 1, time: 1200 }); })
                    .catch(function () { fallbackCopy(text); });
            } else {
                fallbackCopy(text);
            }
        }

        function fallbackCopy(text) {
            try {
                var ta = document.createElement('textarea');
                ta.value = text;
                ta.style.position = 'fixed';
                ta.style.opacity = '0';
                document.body.appendChild(ta);
                ta.select();
                var ok = document.execCommand('copy');
                document.body.removeChild(ta);
                layer.msg(_t(ok ? 'msg.copied' : 'msg.copyFailed'), { icon: ok ? 1 : 2, time: 1600 });
            } catch (e) {
                layer.msg(_t('msg.copyFailed'), { icon: 2 });
            }
        }

        function downloadResult() {
            // 把译文区当前文本保存为本地 .txt（浏览器端下载）
            var text = fullTranslation();
            if (!text) {
                layer.msg(_t('msg.nothingToDownload'), { icon: 0 });
                return;
            }
            var name = (state.fileName ? state.fileName.replace(/\.[^.]+$/, '') : 'translation') + '.txt';
            var blob = new Blob(['\ufeff' + text], { type: 'text/plain;charset=utf-8' });
            var url = URL.createObjectURL(blob);
            var a = document.createElement('a');
            a.href = url;
            a.download = name;
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
            setTimeout(function () { URL.revokeObjectURL(url); }, 1000);
        }

        // ── 小工具 ─────────────────────────────────────────────

        function setText(target, value) {
            if (!target) return;
            if (target.jquery) target.text(value === null || value === undefined ? '' : String(value));
            else target.textContent = value === null || value === undefined ? '' : String(value);
        }

        function intOrNull(value) {
            var n = parseInt(value, 10);
            return isNaN(n) ? null : n;
        }

        /** 依次取「浏览器里存过的值 → 后端默认值 → 兜底值」，空值（null/''/undefined）视为未设置 */
        function pick() {
            for (var i = 0; i < arguments.length; i++) {
                var value = arguments[i];
                if (value !== null && value !== undefined && value !== '') return value;
            }
            return null;
        }

        function round(value, digits) {
            var f = Math.pow(10, digits);
            return Math.round(Number(value) * f) / f;
        }

        function formatModel(id, quant) {
            if (!id) return '';
            return quant ? (id + ' · ' + quant) : id;
        }

        function formatSize(bytes) {
            var units = ['B', 'KB', 'MB', 'GB'];
            var value = Number(bytes) || 0;
            var unit = 0;
            while (value >= 1024 && unit < units.length - 1) {
                value /= 1024;
                unit++;
            }
            return (unit <= 1 ? value.toFixed(0) : value.toFixed(2)) + ' ' + units[unit];
        }

        function escapeHtml(text) {
            return String(text === null || text === undefined ? '' : text)
                .replace(/&/g, '&amp;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;')
                .replace(/"/g, '&quot;')
                .replace(/'/g, '&#39;');
        }

        function escapeAttr(text) {
            return escapeHtml(text);
        }

        /** 解析后端错误体（如 { error, busy, currentJobId, hint }） */
        function errorInfo(xhr) {
            if (!xhr) return {};
            try {
                var body = JSON.parse(xhr.responseText);
                return (body && typeof body === 'object') ? body : {};
            } catch (e) {
                return {};
            }
        }

        function apiError(xhr) {
            if (!xhr) return '';
            var info = errorInfo(xhr);
            return info.error || info.title || info.detail || xhr.responseText || xhr.statusText || '';
        }
    }
})();

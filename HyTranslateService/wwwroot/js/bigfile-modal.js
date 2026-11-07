/* ═══════════════════════════════════════════════════════════════
   文件翻译弹窗（独立脚本）

   与 mcp-modal.js / api-modal.js 同一套模式：DOM 与交互逻辑抽离到本脚本，
   页面只保留一个脚本引用（./js/bigfile-modal.js），index.html 不写弹窗结构。

   功能：
     · 选择本地文本文件，上传到 POST /api/translation/file；
     · 翻译参数完全跟随主窗口当前设置（目标语言、源语言、模型、分段字数、
       采样参数、提示词模板/语言、术语表、背景信息、目标风格）；
     · 轮询 GET /api/translation/current 展示进度、已用时间与预计剩余时间；
     · 「终止翻译」调用 POST /api/translation/cancel（已译部分保留）；
     · 翻译结束（完成 / 终止）自动从 GET /api/translation/file-result 下载译文。

   文案走 window._t（i18n-config.js 提供），静态结构带 data-i18n 由引擎统一翻译。
   ═══════════════════════════════════════════════════════════════ */
(function () {
    'use strict';

    // 文件大小上限从服务端元信息（window.HyTransApp.defaults.maxFileBytes）取，不在前端写死
    function getMaxFileBytes() {
        var d = window.HyTransApp && window.HyTransApp.defaults;
        return d && d.maxFileBytes ? d.maxFileBytes : 0;
    }

    var POLL_INTERVAL_MS = 1000;

    /** 构建弹窗 DOM 并挂到 body */
    function buildModal() {
        var div = document.createElement('div');
        div.className = 'hy-modal-mask';
        div.id = 'bigFileModal';
        div.hidden = true;
        div.innerHTML =
            '<div class="hy-modal">' +
                '<div class="hy-modal-head">' +
                    '<h2 data-i18n="bigfile.title">文件翻译</h2>' +
                    '<button type="button" class="layui-btn layui-btn-primary layui-btn-sm hy-modal-close" id="btnCloseBigFile">' +
                        '<i class="layui-icon layui-icon-close"></i>' +
                    '</button>' +
                '</div>' +
                '<div class="hy-modal-body">' +
                    '<p class="hy-hint" data-i18n="bigfile.pickHint"></p>' +
                    '<div class="hy-kv">' +
                        '<input type="file" id="bigFileInput" hidden accept=".txt,.md,.srt,.csv,.json,.log,.html,.htm,.xml,.yaml,.yml,.ini,.tsv">' +
                        '<code id="bigFileName" class="hy-bigfile-name"></code>' +
                        '<button type="button" class="layui-btn layui-btn-primary layui-btn-xs" id="btnBigFilePick" data-i18n="bigfile.pick">选择文件</button>' +
                    '</div>' +
                    '<div class="hy-bigfile-progress" id="bigFileProgressArea" hidden>' +
                        '<div class="layui-progress layui-progress-big">' +
                            '<div class="layui-progress-bar" id="bigFileBar" style="width:0%"></div>' +
                        '</div>' +
                        '<p class="hy-bigfile-stat" id="bigFileStat"></p>' +
                    '</div>' +
                    '<p class="hy-bigfile-msg" id="bigFileMsg"></p>' +
                '</div>' +
                '<div class="hy-bigfile-foot">' +
                    '<button type="button" class="layui-btn layui-btn-normal" id="btnBigFileStart" data-i18n="bigfile.start">开始翻译</button>' +
                    '<button type="button" class="layui-btn layui-btn-danger" id="btnBigFileStop" disabled data-i18n="bigfile.stop">终止翻译</button>' +
                '</div>' +
            '</div>';
        document.body.appendChild(div);
        return div;
    }

    var modal = buildModal();
    var els = {
        input: modal.querySelector('#bigFileInput'),
        name: modal.querySelector('#bigFileName'),
        progressArea: modal.querySelector('#bigFileProgressArea'),
        bar: modal.querySelector('#bigFileBar'),
        stat: modal.querySelector('#bigFileStat'),
        msg: modal.querySelector('#bigFileMsg'),
        start: modal.querySelector('#btnBigFileStart'),
        stop: modal.querySelector('#btnBigFileStop'),
        pick: modal.querySelector('#btnBigFilePick'),
    };

    var file = null;            // 用户选中的 File
    var jobId = null;           // 进行中 / 最近一次提交的任务
    var pollTimer = null;
    var finished = false;       // 当前 jobId 是否已走完「下载 / 报错」收尾

    // ──────────────────────────────────────────────────────────────
    // 工具
    // ──────────────────────────────────────────────────────────────

    /** 会话暂存键（与 app.js 约定一致）：刷新后凭它接续显示 / 补下载 */
    var SESSION_KEY = 'hytrans.job.v1';

    function saveSession(entry) {
        try { sessionStorage.setItem(SESSION_KEY, JSON.stringify(entry)); } catch (e) { /* 隐私模式等：忽略 */ }
    }

    function clearSession() {
        try { sessionStorage.removeItem(SESSION_KEY); } catch (e) { /* 忽略 */ }
    }

    function formatSize(bytes) {
        var units = ['B', 'KB', 'MB', 'GB'];
        var value = Number(bytes) || 0;
        var unit = 0;
        while (value >= 1024 && unit < units.length - 1) { value /= 1024; unit++; }
        return (unit === 0 ? value : value.toFixed(1)) + ' ' + units[unit];
    }

    function formatDuration(ms) {
        var sec = Math.round(Math.max(0, ms / 1000));
        var h = sec / 3600 | 0, m = (sec / 60 | 0) % 60, s = sec % 60;
        return (h ? h + ':' : '') + ('0' + m).slice(-2) + ':' + ('0' + s).slice(-2);
    }

    function setMsg(text) { els.msg.textContent = text || ''; }

    function setRunning(running) {
        els.start.disabled = running;
        els.stop.disabled = !running;
        els.pick.disabled = running;
        els.input.disabled = running;
    }

    function showProgress(snapshot) {
        els.progressArea.hidden = false;
        var done = snapshot.completedSegments || 0;
        var total = snapshot.totalSegments || 0;
        var pct = total > 0 ? Math.min(100, Math.round(done / total * 100)) : 0;
        els.bar.style.width = pct + '%';

        var parts = [pct + '%', window._t('bigfile.progressDone', { done: done, total: total })];
        if (snapshot.elapsedMs > 0) parts.push(window._t('bigfile.elapsed', { time: formatDuration(snapshot.elapsedMs) }));
        if (done > 0 && total > done) parts.push(window._t('bigfile.eta', { time: formatDuration(snapshot.elapsedMs / done * (total - done)) }));
        else if (total > 0 && done >= total) parts.push(window._t('bigfile.finishing'));
        if (snapshot.note) parts.push(snapshot.note);
        els.stat.textContent = parts.join(' · ');
    }

    /**
     * 采集主窗口当前的翻译设置：弹窗不再有自己的语言/参数选择，
     * 文件翻译完全沿用主窗口的全部配置（含高级选项与采样参数）。
     * 采样参数读滑块旁的数值文本（#temperatureVal 等，始终与当前状态同步）。
     */
    function collectMainOptions(form) {
        function appendField(name, value) {
            var v = String(value === undefined || value === null ? '' : value).trim();
            if (v !== '' && v !== 'NaN') form.append(name, v);   // 空值/无效数值不发送，后端按「未指定」处理
        }
        function val(sel) {
            var el = document.querySelector(sel);
            return el ? el.value : '';
        }
        function text(sel) {
            var el = document.querySelector(sel);
            return el ? el.textContent : '';
        }

        appendField('targetLanguage', val('#targetLang') || 'zh');
        appendField('sourceLanguage', val('#sourceLang'));
        appendField('model', val('#model'));
        appendField('engine', val('#engine'));
        appendField('kvCache', val('#kvCache'));
        appendField('segmentChars', val('#segmentChars'));
        appendField('maxTokens', val('#maxTokens'));
        appendField('temperature', parseFloat(text('#temperatureVal')));
        appendField('topP', parseFloat(text('#topPVal')));
        appendField('topK', parseInt(text('#topKVal'), 10));
        appendField('repetitionPenalty', parseFloat(text('#repetitionPenaltyVal')));
        appendField('promptStyle', val('#promptStyle'));
        appendField('promptLanguage', val('#promptLanguage'));
        appendField('glossary', val('#glossary'));
        appendField('background', val('#background'));
        appendField('targetStyle', val('#targetStyle'));   // 主窗口多选风格的逗号拼接结果（隐藏域）
    }

    // ──────────────────────────────────────────────────────────────
    // 上传与轮询
    // ──────────────────────────────────────────────────────────────

    function startTranslate() {
        if (!file) {
            setMsg(window._t('bigfile.noFile'));
            return;
        }
        var maxFileBytes = getMaxFileBytes();
        if (maxFileBytes && file.size > maxFileBytes) {
            setMsg(window._t('bigfile.tooLarge'));
            return;
        }

        setMsg(window._t('bigfile.uploading'));
        els.start.disabled = true;

        var form = new FormData();
        form.append('file', file, file.name);
        collectMainOptions(form);

        fetch('./api/translation/file', { method: 'POST', body: form })
            .then(function (r) {
                return r.json().then(function (body) { return { ok: r.ok, status: r.status, body: body }; });
            })
            .then(function (res) {
                if (!res.ok) {
                    // 409 = 模型正忙；400 = 请求无效（文件过大 / 内容为空 / 语言不识别）
                    var err = res.body && res.body.error ? res.body.error : 'HTTP ' + res.status;
                    setMsg(res.status === 409 ? window._t('bigfile.busy', { message: err }) : window._t('bigfile.failed', { message: err }));
                    els.start.disabled = false;
                    return;
                }
                jobId = res.body.jobId;
                finished = false;
                // 会话暂存：刷新后凭 jobId 接续进度 / 重新下载（app.js 与本脚本共用同一键）
                saveSession({ jobId: jobId, kind: 'file', sourceName: file.name });
                setMsg('');
                setRunning(true);
                showProgress(res.body);
                schedulePoll();
            })
            .catch(function (e) {
                setMsg(window._t('bigfile.failed', { message: e.message || e }));
                els.start.disabled = false;
            });
    }

    function schedulePoll() {
        if (pollTimer) clearTimeout(pollTimer);
        pollTimer = setTimeout(pollOnce, POLL_INTERVAL_MS);
    }

    function pollOnce() {
        if (!jobId) return;
        // brief=1：轻量进度快照（无分段明细与全文）——大文件任务全量快照的轮询开销不可接受
        fetch('./api/translation/current?brief=1')
            .then(function (r) { return r.json(); })
            .then(function (data) {
                var snapshot = data && !data.idle ? data : null;

                if (snapshot && snapshot.jobId === jobId) {
                    showProgress(snapshot);
                    if (snapshot.isFinished || /Completed|Cancelled|Failed/i.test(snapshot.status || '')) {
                        finalize(snapshot);
                        return;
                    }
                    schedulePoll();
                    return;
                }

                if (snapshot) {
                    // 当前任务被别的请求占用（理论上不可能：同时只跑一个任务）——按继续等待处理
                    schedulePoll();
                    return;
                }

                // 服务端已空闲：任务已结束并被清出「当前任务」位 → 尝试取结果
                finalize(null);
            })
            .catch(function () { schedulePoll(); });
    }

    /** 收尾：成功 / 终止 → 下载译文；失败 → 报错。只执行一次 */
    function finalize(snapshot) {
        if (finished) return;
        finished = true;
        if (pollTimer) { clearTimeout(pollTimer); pollTimer = null; }
        setRunning(false);

        var status = snapshot ? String(snapshot.status || '') : 'Completed';
        if (status === 'Failed') {
            setMsg(window._t('bigfile.failed', { message: snapshot.error || 'unknown' }));
            jobId = null;
            clearSession();
            return;
        }

        setMsg(window._t('bigfile.autoSaving'));
        fetch('./api/translation/file-result?job=' + encodeURIComponent(jobId))
            .then(function (r) {
                if (!r.ok) {
                    return r.json().catch(function () { return {}; }).then(function (body) {
                        throw new Error(body && body.error ? body.error : 'HTTP ' + r.status);
                    });
                }
                var dispo = r.headers.get('Content-Disposition') || '';
                var m = dispo.match(/filename\*=UTF-8''([^;]+)/i);
                var name = m ? decodeURIComponent(m[1]) : 'translation.txt';
                return r.blob().then(function (blob) { return { blob: blob, name: name }; });
            })
            .then(function (result) {
                var url = URL.createObjectURL(result.blob);
                var a = document.createElement('a');
                a.href = url;
                a.download = result.name;
                document.body.appendChild(a);
                a.click();
                document.body.removeChild(a);
                setTimeout(function () { URL.revokeObjectURL(url); }, 1000);
                setMsg(window._t('bigfile.saved', { file: result.name }) +
                    (status === 'Cancelled' ? ' ' + window._t('bigfile.partialNote') : ''));
                jobId = null;
                clearSession();     // 译文已下载到本地，暂存使命完成
            })
            .catch(function (e) {
                setMsg(window._t('bigfile.downloadFailed', { message: e.message || e }));
                jobId = null;
            });
    }

    function stopTranslate() {
        if (!jobId) return;
        fetch('./api/translation/cancel', { method: 'POST' })
            .then(function (r) { return r.json(); })
            .then(function (body) {
                setMsg(body && body.cancelled
                    ? window._t('bigfile.stopping')
                    : window._t('bigfile.stopUnavailable'));
            })
            .catch(function () { setMsg(window._t('bigfile.stopUnavailable')); });
    }

    // ──────────────────────────────────────────────────────────────
    // 事件绑定
    // ──────────────────────────────────────────────────────────────

    els.pick.addEventListener('click', function () { els.input.click(); });
    els.input.addEventListener('change', function () {
        file = els.input.files && els.input.files[0] ? els.input.files[0] : null;
        els.name.textContent = file
            ? file.name + '（' + formatSize(file.size) + '）'
            : '';
        var maxFileBytes = getMaxFileBytes();
        if (file && maxFileBytes && file.size > maxFileBytes) {
            setMsg(window._t('bigfile.tooLarge'));
        } else {
            setMsg('');
        }
    });

    els.start.addEventListener('click', startTranslate);
    els.stop.addEventListener('click', stopTranslate);

    function open() {
        modal.hidden = false;
    }

    function close() { modal.hidden = true; }

    /**
     * 接续进行中的文件翻译任务（页面刷新后由 app.js 调用）：
     * 不重新上传，直接沿用服务端仍在执行的任务进入轮询-下载链路。
     */
    function resume(jobIdExisting, name) {
        if (!jobIdExisting) return;
        jobId = jobIdExisting;
        finished = false;
        els.name.textContent = name || '';
        els.progressArea.hidden = false;
        open();
        setRunning(true);
        setMsg(window._t('bigfile.resumed'));
        pollOnce();
    }

    /**
     * 补下载已结束任务的译文（刷新时任务已结束、暂存里还有 jobId）：
     * 打开弹窗直接走「下载译文」收尾；结果不在了（只保留最近一次）由 finalize 报错。
     */
    function recover(jobIdExisting, name) {
        if (!jobIdExisting) return;
        jobId = jobIdExisting;
        finished = false;
        els.name.textContent = name || '';
        open();
        setRunning(false);
        finalize(null);
    }

    var btnBigFile = document.getElementById('btnBigFile');
    if (btnBigFile) btnBigFile.addEventListener('click', open);

    modal.addEventListener('click', function (e) {
        // 点遮罩（modal 本身）或关闭按钮时关闭；翻译任务继续在服务端执行
        if (e.target === modal || e.target.closest('#btnCloseBigFile')) close();
    });

    document.addEventListener('keydown', function (e) {
        if (e.keyCode === 27) close();   // Esc
    });

    // 暴露给需要的地方：open/close 常规开关；resume/recover 供刷新接续（app.js 调用）
    window.HyBigFileModal = { open: open, close: close, resume: resume, recover: recover };
})();

/* ═══════════════════════════════════════════════════════════════
   WebAPI 说明弹窗（独立脚本）

   与 mcp-modal.js 同一套模式：DOM 与「打开 / 关闭」逻辑抽离到本脚本，
   页面只保留一个脚本引用（./js/api-modal.js），index.html 不写弹窗结构。

   行为：
     · 点击顶栏 #btnApi 打开弹窗，展示 WebAPI（REST）与实时通道（SignalR）使用说明；
     · 服务地址取 location.origin（本服务与页面同源部署）；
     · 点击遮罩 / 关闭按钮 / 按 Esc 关闭；
     · 弹窗内的「复制」按钮复用 app.js 里对 [data-copy] 的全局委托。

   文案走 Layui i18n（data-i18n 属性）：本脚本只负责构建 DOM 与填充动态内容，
   文本翻译由 i18n 引擎在 boot.js 启动时统一处理。
   ═══════════════════════════════════════════════════════════════ */
(function () {
    'use strict';

    /** 弹窗内的接口清单（路径为静态契约，与后端 Controller 路由一致） */
    var REST_ENDPOINTS = [
        { method: 'GET',    path: '/api/system/info',              key: 'api.rowSystem' },
        { method: 'GET',    path: '/api/models?refresh=true',      key: 'api.rowModels' },
        { method: 'POST',   path: '/api/models/refresh',           key: 'api.rowModelsRefresh' },
        { method: 'GET',    path: '/api/translation/languages',    key: 'api.rowLanguages' },
        { method: 'POST',   path: '/api/translation',              key: 'api.rowTranslate' },
        { method: 'POST',   path: '/api/translation/file',         key: 'api.rowFileUpload' },
        { method: 'GET',    path: '/api/translation/file-result?job=…', key: 'api.rowFileResult' },
        { method: 'GET',    path: '/api/translation/current',      key: 'api.rowCurrent' },
        { method: 'POST',   path: '/api/translation/cancel',       key: 'api.rowCancel' }
    ];

    /** 构建弹窗 DOM 并挂到 body */
    function buildModal() {
        var rowsHtml = REST_ENDPOINTS.map(function (ep) {
            return '<tr>' +
                '<td><span class="hy-method is-' + ep.method.toLowerCase() + '">' + ep.method + '</span></td>' +
                '<td><code>' + ep.path + '</code></td>' +
                '<td data-i18n="' + ep.key + '"></td>' +
            '</tr>';
        }).join('');

        var div = document.createElement('div');
        div.className = 'hy-modal-mask';
        div.id = 'apiModal';
        div.hidden = true;
        div.innerHTML =
            '<div class="hy-modal">' +
                '<div class="hy-modal-head">' +
                    '<h2 data-i18n="api.title">WebAPI 与实时通道说明</h2>' +
                    '<button type="button" class="layui-btn layui-btn-primary layui-btn-sm hy-modal-close" id="btnCloseApi">' +
                        '<i class="layui-icon layui-icon-close"></i>' +
                    '</button>' +
                '</div>' +
                '<div class="hy-modal-body">' +
                    '<div class="hy-kv">' +
                        '<code id="apiBase">—</code>' +
                        '<button type="button" class="layui-btn layui-btn-primary layui-btn-xs" data-copy="#apiBase" data-i18n="app.copy">复制</button>' +
                    '</div>' +
                    '<p class="hy-hint" data-i18n="api.intro"></p>' +
                    '<h3 data-i18n="api.rest">WebAPI（REST / HTTP）</h3>' +
                    '<table class="hy-api-table"><tbody>' + rowsHtml + '</tbody></table>' +
                    '<p class="hy-hint" data-i18n="api.busy"></p>' +
                    '<h3 data-i18n="api.exampleRequest">翻译请求体示例（POST /api/translation）</h3>' +
                    '<pre id="apiExample"></pre>' +
                    '<button type="button" class="layui-btn layui-btn-primary layui-btn-xs" data-copy="#apiExample" data-i18n="app.copy">复制</button>' +
                    '<h3 data-i18n="api.hub">实时通道（SignalR）</h3>' +
                    '<div class="hy-kv">' +
                        '<code id="apiHubPath">—</code>' +
                        '<button type="button" class="layui-btn layui-btn-primary layui-btn-xs" data-copy="#apiHubPath" data-i18n="app.copy">复制</button>' +
                    '</div>' +
                    '<ul class="hy-endpoint-notes">' +
                        '<li data-i18n="api.hubServer"></li>' +
                        '<li data-i18n="api.hubEvents"></li>' +
                        '<li data-i18n="api.hubSeparator"></li>' +
                    '</ul>' +
                    '<p class="hy-hint" data-i18n="api.footer"></p>' +
                '</div>' +
            '</div>';
        document.body.appendChild(div);
        return div;
    }

    var modal = buildModal();

    /** 填充服务地址与 Hub 路径（同源部署，无需请求） */
    function renderEndpoints() {
        var base = location.origin;
        var baseEl = modal.querySelector('#apiBase');
        var hubEl = modal.querySelector('#apiHubPath');
        var exampleEl = modal.querySelector('#apiExample');
        if (baseEl) baseEl.textContent = base;
        if (hubEl) hubEl.textContent = base + '/signalr/translation';
        if (exampleEl) exampleEl.textContent = JSON.stringify({
            text: 'Hello, world!',
            targetLanguage: 'zh',
            sourceLanguage: '',
            model: 'auto',
            glossary: 'Hunyuan=混元'
        }, null, 2);
    }

    function open() {
        renderEndpoints();
        modal.hidden = false;
    }

    function close() {
        modal.hidden = true;
    }

    // —— 事件绑定 ——
    var btnApi = document.getElementById('btnApi');
    if (btnApi) btnApi.addEventListener('click', open);

    modal.addEventListener('click', function (e) {
        // 点遮罩（modal 本身）或关闭按钮时关闭
        if (e.target === modal || e.target.closest('#btnCloseApi')) close();
    });

    document.addEventListener('keydown', function (e) {
        if (e.keyCode === 27) close();   // Esc
    });

    // 暴露给需要的地方（如有）
    window.HyApiModal = { open: open, close: close };
})();

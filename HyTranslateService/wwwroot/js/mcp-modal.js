/* ═══════════════════════════════════════════════════════════════
   MCP 接入弹窗（独立脚本）

   把弹窗的 DOM 与「打开 / 关闭 / 内容填充」逻辑从 index.html 抽离到这里，
   让页面只保留一个脚本引用（./js/mcp-modal.js），不在 index.html 里写弹窗结构。

   行为：
     · 点击顶栏 #btnMcp 打开弹窗，并填充 MCP 端点与客户端配置片段；
     · 端点取自 GET /api/system/info 的 mcpEndpoint（取不到时回退到 location.origin + '/mcp'）；
     · 点击遮罩 / 关闭按钮 / 按 Esc 关闭；
     · 弹窗内的「复制」按钮复用 app.js 里对 [data-copy] 的全局委托。

   文案走 Layui i18n（data-i18n 属性）：本脚本只负责构建 DOM 与填充动态内容，
   文本翻译由 i18n 引擎在 boot.js 启动时统一处理（弹窗已在 body 中，会被一并扫描）。
   ═══════════════════════════════════════════════════════════════ */
(function () {
    'use strict';

    /** 构建弹窗 DOM 并挂到 body（index.html 不再内联这段结构） */
    function buildModal() {
        var div = document.createElement('div');
        div.className = 'hy-modal-mask';
        div.id = 'mcpModal';
        div.hidden = true;
        div.innerHTML =
            '<div class="hy-modal">' +
                '<div class="hy-modal-head">' +
                    '<h2 data-i18n="mcp.title">把本服务接入 AI 客户端</h2>' +
                    '<button type="button" class="layui-btn layui-btn-primary layui-btn-sm hy-modal-close" id="btnCloseMcp">' +
                        '<i class="layui-icon layui-icon-close"></i>' +
                    '</button>' +
                '</div>' +
                '<div class="hy-modal-body">' +
                    '<h3 data-i18n="mcp.endpoint">MCP 端点（Streamable HTTP）</h3>' +
                    '<div class="hy-kv">' +
                        '<code id="mcpEndpoint">—</code>' +
                        '<button type="button" class="layui-btn layui-btn-primary layui-btn-xs" data-copy="#mcpEndpoint" data-i18n="app.copy">复制</button>' +
                    '</div>' +
                    '<h3 data-i18n="mcp.tools">可用工具</h3>' +
                    '<ul class="hy-tool-list">' +
                        '<li><code>list_translation_models</code><span data-i18n="mcp.toolList"></span></li>' +
                        '<li><code>list_target_languages</code><span data-i18n="mcp.toolLanguages"></span></li>' +
                        '<li><code>translate_text</code><span data-i18n="mcp.toolTranslateText"></span></li>' +
                        '<li><code>translate_file</code><span data-i18n="mcp.toolTranslateFile"></span></li>' +
                        '<li><code>get_current_translation</code> / <code>cancel_current_translation</code><span data-i18n="mcp.toolCurrent"></span></li>' +
                    '</ul>' +
                    '<p class="hy-hint" data-i18n="mcp.footer"></p>' +
                    '<h3 data-i18n="mcp.clients">客户端配置（Claude / VS Code / Cursor）</h3>' +
                    '<p class="hy-hint" data-i18n="mcp.clientsHint">复制下面的配置片段，按各客户端说明放入配置文件即可。Claude 与 Cursor 写法完全相同，仅文件位置不同。</p>' +
                    '<p class="hy-subhead" data-i18n="mcp.clientCommon">Claude / Cursor（通用 mcpServers 写法）</p>' +
                    '<pre id="mcpSnippetCommon"></pre>' +
                    '<button type="button" class="layui-btn layui-btn-primary layui-btn-xs" data-copy="#mcpSnippetCommon" data-i18n="app.copy">复制</button>' +
                    '<ul class="hy-loc-list">' +
                        '<li><b>Claude</b><span data-i18n="mcp.clientClaudePath"></span></li>' +
                        '<li><b>Cursor</b><span data-i18n="mcp.clientCursorPath"></span></li>' +
                    '</ul>' +
                    '<p class="hy-subhead" data-i18n="mcp.clientVscode">VS Code（settings.json 写法）</p>' +
                    '<pre id="mcpSnippetVscode"></pre>' +
                    '<button type="button" class="layui-btn layui-btn-primary layui-btn-xs" data-copy="#mcpSnippetVscode" data-i18n="app.copy">复制</button>' +
                    '<ul class="hy-loc-list">' +
                        '<li><b>VS Code</b><span data-i18n="mcp.clientVscodePath"></span></li>' +
                    '</ul>' +
                    '<h3 data-i18n="mcp.faq">常见问题与排障</h3>' +
                    '<ul class="hy-faq">' +
                        '<li><b data-i18n="mcp.faqTargetLangTitle"></b><span data-i18n="mcp.faqTargetLang"></span></li>' +
                        '<li><b data-i18n="mcp.faqSessionTitle"></b><span data-i18n="mcp.faqSession"></span></li>' +
                    '</ul>' +
                '</div>' +
            '</div>';
        document.body.appendChild(div);
        return div;
    }

    var modal = buildModal();
    var endpoint = null;          // 缓存端点，避免每次打开都重新拉取
    var fetching = false;

    /** 用端点信息填充弹窗内的端点与配置片段 */
    function renderSnippet() {
        var ep = endpoint || (location.origin + '/mcp');
        var code = modal.querySelector('#mcpEndpoint');
        if (code) code.textContent = ep;

        // Claude / Cursor 写法相同：顶层 mcpServers 键 + type=streamableHttp（两者共用一份片段）。
        // VS Code 使用 mcp.servers 包装 + type=http（其 settings.json 的约定），单独一份。
        var common = JSON.stringify({
            mcpServers: {
                hytranslate: { type: 'streamableHttp', url: ep }
            }
        }, null, 2);
        var vscode = JSON.stringify({
            mcp: {
                servers: {
                    hytranslate: { type: 'http', url: ep }
                }
            }
        }, null, 2);

        setText('#mcpSnippetCommon', common);
        setText('#mcpSnippetVscode', vscode);
    }

    /** 按选择器设置元素文本（元素不存在时静默跳过，避免影响其他客户端） */
    function setText(sel, text) {
        var el = modal.querySelector(sel);
        if (el) el.textContent = text;
    }

    /** 打开弹窗：首次打开时拉取端点信息 */
    function open() {
        if (!endpoint && !fetching) {
            fetching = true;
            fetch('./api/system/info')
                .then(function (r) { return r.json(); })
                .then(function (info) {
                    endpoint = (info && info.mcpEndpoint) || (location.origin + '/mcp');
                })
                .catch(function () {
                    endpoint = location.origin + '/mcp';
                })
                .then(function () {
                    fetching = false;
                    renderSnippet();
                });
        } else {
            renderSnippet();
        }
        modal.hidden = false;
    }

    function close() {
        modal.hidden = true;
    }

    // —— 事件绑定 ——
    var btnMcp = document.getElementById('btnMcp');
    if (btnMcp) btnMcp.addEventListener('click', open);

    modal.addEventListener('click', function (e) {
        // 点遮罩（modal 本身）或关闭按钮时关闭
        if (e.target === modal || e.target.closest('#btnCloseMcp')) close();
    });

    document.addEventListener('keydown', function (e) {
        if (e.keyCode === 27) close();   // Esc
    });

    // 暴露给需要的地方（如有）
    window.HyMcpModal = { open: open, close: close };
})();

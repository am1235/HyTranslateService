/**
 * 启动编排（WebUI 入口）
 *
 * 存在的原因：可选界面语言由服务端扫描 wwwroot/js/i18n/lang/ 目录得出，
 * 前端必须先拿到清单、动态加载对应语言包，之后才能初始化 i18n 与页面逻辑。
 * 因此把「取清单 → 装语言包 → 启动应用」这段时序单独放在这里。
 *
 * 必须在 app.js 之后引入（app.js 只定义 window.HyTransApp，不自行启动）。
 */
(function () {
    'use strict';

    var LIST_URL = './api/language/list';

    /** 语言包就绪后再启动应用（app.js 已定义 HyTransApp.start） */
    function boot(languages) {
        if (languages && languages.length) {
            window.I18nSetLanguages(languages);
            window.I18nLoadPacks(languages, function () {
                window.HyTransApp.start();
            });
            return;
        }

        // 后端不可用 / 清单为空：仍按浏览器语言尽力启动，界面回退为 HTML 里的原文
        window.I18nSetLanguages([]);
        window.HyTransApp.start();
    }

    function fetchLanguages(done) {
        if (!window.fetch) {
            done(null);
            return;
        }

        window.fetch(LIST_URL, { headers: { Accept: 'application/json' } })
            .then(function (response) { return response.ok ? response.json() : null; })
            .then(function (data) {
                if (!data) { done(null); return; }
                // 接口返回 { directory, count, languages: [...] }
                done(data.languages || (Array.isArray(data) ? data : null));
            })
            .catch(function (err) {
                if (window.console && console.warn) {
                    console.warn('读取界面语言清单失败，使用降级方案', err);
                }
                done(null);
            });
    }

    function run() {
        if (!window.HyTransApp || typeof window.HyTransApp.start !== 'function') {
            if (window.console && console.error) console.error('HyTransApp 未就绪，app.js 是否已加载？');
            return;
        }
        fetchLanguages(boot);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', run);
    } else {
        run();
    }
})();

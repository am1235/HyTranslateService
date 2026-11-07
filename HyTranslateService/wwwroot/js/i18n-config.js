/**
 * Layui 国际化引擎（本项目公共配置）
 *
 * 必须在 layui.js 之前引入。
 * 引擎底座仍是 Layui 自带 i18n（layui.i18n.$t / layui.i18n.set），本文件在其上补三件事：
 *   ① 在 layui.js 加载前把当前语言写入 window.LAYUI_GLOBAL.i18n.locale，
 *      让 Layui 自身组件一开始就用对语种；
 *   ② 语言包就绪后，把业务文案深合并进 layui.i18n 的消息表（保留 Layui 内置文案）；
 *   ③ 提供业务侧 API：_t()、data-i18n 自动翻译、语言切换 / 目录 / 动态加载。
 *
 * 界面语言的真相源在**浏览器**：
 *   · 可选语言 = 服务启动时扫描 wwwroot/js/i18n/lang/ 得到的清单（GET /api/language/list）
 *   · 语言包 = 由 I18nLoadPacks() 按清单动态注入 <script>，页面里不再硬编码语言列表
 *   · 用户选择 = 存进 localStorage（HySettings），服务端不参与
 *
 * 语言包约定：文件位于 /js/i18n/lang/<code>.js，执行后挂载 window.I18N_LANGS['<code>']，
 *            并在首行用自描述头声明自己的显示名与外链地址来源：
 *            /* @i18n-lang {"code":"ja-JP","nativeName":"日本語","englishName":"Japanese","order":4} *\/
 */
(function () {
    'use strict';

    /** 最终兜底语言（清单为空且浏览器语言也无法匹配时使用） */
    var FALLBACK_LANG = 'zh-CN';

    /** 界面语言 → Layui 内置 locale 标识（Layui 用 en 表示英文） */
    var LOCALE_MAP = {
        'en-US': 'en'
    };

    /** 可选语言清单（由 I18nSetLanguages 从后端目录扫描结果填充） */
    var catalog = [];

    /** 运行时当前语言 */
    var current = FALLBACK_LANG;

    /** 语言变更监听器 */
    var listeners = [];

    // ────────────────────────────────────────────────────────────────
    // 基础工具
    // ────────────────────────────────────────────────────────────────

    function allPacks() {
        return window.I18N_LANGS || {};
    }

    function pickNavigator() {
        if (typeof navigator === 'undefined') return [];
        if (navigator.languages && navigator.languages.length) return navigator.languages;
        var single = navigator.language || navigator.userLanguage;
        return single ? [single] : [];
    }

    /**
     * 中文繁简的常见写法（必须先于「主子标签匹配」判断，
     * 否则 zh-Hant / zh-HK 会被清单里排在前面的 zh-CN 抢走）。
     * 依据脚本子标签（hans/hant）与地区子标签（cn/tw/hk/mo/sg）判定。
     */
    function zhAlias(value) {
        var low = String(value).replace(/_/g, '-').toLowerCase();
        var parts = low.split('-');
        if (low !== 'chinese' && parts[0] !== 'zh' && parts[0] !== 'cn') return '';

        if (parts.indexOf('hant') !== -1) return 'zh-TW';
        if (parts.indexOf('hans') !== -1) return 'zh-CN';
        if (parts.indexOf('tw') !== -1 || parts.indexOf('hk') !== -1 || parts.indexOf('mo') !== -1) return 'zh-TW';
        return 'zh-CN';
    }

    /** 清单中的第一个语言（作为默认值） */
    function firstCatalogCode() {
        return catalog.length ? catalog[0].code : FALLBACK_LANG;
    }

    /** 在清单里按代码精确查找 */
    function byCode(code) {
        if (!code) return null;
        for (var i = 0; i < catalog.length; i++) {
            if (catalog[i].code.toLowerCase() === String(code).toLowerCase()) return catalog[i];
        }
        return null;
    }

    /**
     * 归一化语言标识：把 en / ja / 日本語 / zh-Hant 之类的写法映射到清单中的标准 code。
     * 清单尚未加载时原样返回（引擎要先于清单运行），稍后由 I18nInit 按清单校正。
     */
    function normalize(lang) {
        if (typeof lang !== 'string') return firstCatalogCode();
        var value = lang.trim();
        if (!value) return firstCatalogCode();

        if (catalog.length) {
            var hit = byCode(value);
            if (hit) return hit.code;

            // 母语名 / 英文名
            for (var i = 0; i < catalog.length; i++) {
                var item = catalog[i];
                if (item.nativeName && item.nativeName.toLowerCase() === value.toLowerCase()) return item.code;
                if (item.englishName && item.englishName.toLowerCase() === value.toLowerCase()) return item.code;
            }

            // 中文繁简别名（在主子标签匹配之前）
            var alias = zhAlias(value);
            if (alias && byCode(alias)) return byCode(alias).code;

            // 主子标签匹配：zh-CN / ja-JP / ko-KR …
            var primary = value.split(/[-_]/)[0].toLowerCase();
            for (var j = 0; j < catalog.length; j++) {
                if (catalog[j].code.split('-')[0].toLowerCase() === primary) return catalog[j].code;
            }

            return firstCatalogCode();
        }

        return value;
    }

    /** 界面语言 → Layui locale */
    function localeKey(lang) {
        return LOCALE_MAP[lang] || lang;
    }

    /**
     * Layui 的 $t 返回的字符串会被 HTML 转义（' → &#39;），
     * 这里把数字实体还原，便于赋给 textContent / value。
     */
    function decodeEntities(text) {
        if (typeof text !== 'string' || text.indexOf('&#') === -1) return text;
        return text.replace(/&#(\d+);/g, function (match, code) {
            return String.fromCharCode(parseInt(code, 10));
        });
    }

    /** 用 {name} 占位符做插值（与 Layui $t 的写法保持一致） */
    function interpolate(text, params) {
        if (!params || typeof text !== 'string' || text.indexOf('{') === -1) return text;
        return text.replace(/\{(\w+)\}/g, function (match, key) {
            return params[key] !== undefined ? params[key] : match;
        });
    }

    /** 读取当前语言包里的原始字符串（不依赖 layui，供 layui 就绪前使用） */
    function lookupPack(key) {
        var node = allPacks()[current];
        if (!node || !key) return key;
        var parts = String(key).split('.');
        for (var i = 0; i < parts.length; i++) {
            if (node && typeof node === 'object' && parts[i] in node) node = node[parts[i]];
            else return key;
        }
        return typeof node === 'string' ? node : key;
    }

    /** 深合并：把 source 合进 target（已存在的其它键保留） */
    function deepMerge(target, source) {
        if (!source || typeof source !== 'object') return target;
        for (var key in source) {
            if (!Object.prototype.hasOwnProperty.call(source, key)) continue;
            var value = source[key];
            if (value && typeof value === 'object' && !Array.isArray(value)) {
                if (!target[key] || typeof target[key] !== 'object') target[key] = {};
                deepMerge(target[key], value);
            } else {
                target[key] = value;
            }
        }
        return target;
    }

    /** 把业务语言包合并进 Layui i18n 的消息表，并切换 locale（保留 Layui 内置文案） */
    function syncToLayui() {
        var lang = current;
        var key = localeKey(lang);
        var pack = allPacks()[lang];
        if (!pack) return;

        var layuiReady = typeof window.layui !== 'undefined' && window.layui && window.layui.i18n;
        var container = null;

        if (layuiReady && window.layui.i18n.config && window.layui.i18n.config.messages) {
            // 模块已加载：直接改 Layui 的消息表，Layui 内置文案得以保留
            container = window.layui.i18n.config.messages;
        } else {
            // 模块尚未加载：准备好 LAYUI_GLOBAL.i18n.messages（仅在缺失时创建，
            // 以免覆盖 layui.js 内部合并的默认文案）
            window.LAYUI_GLOBAL = window.LAYUI_GLOBAL || {};
            window.LAYUI_GLOBAL.i18n = window.LAYUI_GLOBAL.i18n || {};
            if (!window.LAYUI_GLOBAL.i18n.messages) window.LAYUI_GLOBAL.i18n.messages = {};
            container = window.LAYUI_GLOBAL.i18n.messages;
        }

        deepMerge(container[key] || (container[key] = {}), pack);
        // 同时登记业务语言标识本身，便于 $t(key, params, { locale: 'en-US' }) 这类显式调用
        if (key !== lang) deepMerge(container[lang] || (container[lang] = {}), pack);

        if (layuiReady && typeof window.layui.i18n.set === 'function') {
            // 切换 locale 并清空 $t 的结果缓存
            window.layui.i18n.set({ locale: key });
        } else {
            window.LAYUI_GLOBAL.i18n.locale = key;
        }
    }

    // ────────────────────────────────────────────────────────────────
    // 翻译 API
    // ────────────────────────────────────────────────────────────────

    /**
     * 取翻译文案。
     * 优先走 Layui 自带 i18n；未命中则回退到本地语言包，最后回退 key 本身。
     * @param {string} key 点号分隔的 key，如 'action.translate'
     * @param {object} [params] {name} 占位符参数
     */
    window._t = function (key, params) {
        if (!key) return '';

        if (window.layui && window.layui.i18n && typeof window.layui.i18n.$t === 'function') {
            var translated = window.layui.i18n.$t(key, params);
            if (typeof translated === 'string' && translated !== key) {
                return decodeEntities(translated);
            }
        }

        var raw = lookupPack(key);
        return typeof raw === 'string' ? interpolate(raw, params) : key;
    };

    /** _t 的别名，语义更明确 */
    window.I18nText = window._t;

    // ────────────────────────────────────────────────────────────────
    // DOM 自动翻译（data-i18n 系列属性）
    // ────────────────────────────────────────────────────────────────

    /**
     * 支持的属性：
     *   data-i18n             → textContent
     *   data-i18n-placeholder → placeholder
     *   data-i18n-title       → title
     *   data-i18n-value       → value
     *   data-i18n-html        → innerHTML
     * 另外支持 data-i18n-params='{"max":6000}' 传入占位符参数。
     */
    var I18N_ATTRS = [
        { attr: 'data-i18n', target: 'textContent' },
        { attr: 'data-i18n-placeholder', target: 'placeholder' },
        { attr: 'data-i18n-title', target: 'title' },
        { attr: 'data-i18n-value', target: 'value' },
        { attr: 'data-i18n-html', target: 'innerHTML' }
    ];

    function parseParams(node) {
        var raw = node.getAttribute('data-i18n-params');
        if (!raw) return null;
        try {
            return JSON.parse(raw);
        } catch (e) {
            return null;
        }
    }

    function applyDataI18n(root) {
        root = root || document;

        for (var i = 0; i < I18N_ATTRS.length; i++) {
            var def = I18N_ATTRS[i];
            var nodes = root.querySelectorAll('[' + def.attr + ']');
            for (var j = 0; j < nodes.length; j++) {
                var node = nodes[j];
                var key = node.getAttribute(def.attr);
                if (!key) continue;

                var value = window._t(key, parseParams(node));
                // 没配翻译时保留 HTML 里的原始文字，避免界面出现裸 key
                if (value === key && def.target === 'textContent') continue;

                node[def.target] = value;
            }
        }
    }

    window.I18nApply = applyDataI18n;

    /** 同步 <html lang="...">，利于无障碍与浏览器翻译提示 */
    function applyDocumentLang() {
        if (document && document.documentElement) document.documentElement.lang = current;
    }

    /** 触发 Layui 组件重渲染（form 的开关/下拉、element 的标签页等） */
    function rerenderLayuiComponents() {
        if (!window.layui || typeof window.layui.use !== 'function') return;
        try {
            window.layui.use(['form', 'element'], function (form, element) {
                if (form && typeof form.render === 'function') form.render();
                if (element && typeof element.render === 'function') element.render();
            });
        } catch (e) {
            // 忽略：Layui 尚未就绪时由 I18nInit 的延迟回调再补一次
        }
    }

    // ────────────────────────────────────────────────────────────────
    // 语言清单与语言包加载
    // ────────────────────────────────────────────────────────────────

    /**
     * 登记可选语言清单（来自 GET /api/language/list，该清单由服务端扫描
     * wwwroot/js/i18n/lang/ 目录得到 —— 目录即真相源，前端不写死任何一种语言）。
     * @param {Array} list [{ code, nativeName, englishName, url, order }]
     * @returns {number} 登记后的语言数量
     */
    window.I18nSetLanguages = function (list) {
        catalog = [];

        if (list && list.length) {
            for (var i = 0; i < list.length; i++) {
                var item = list[i];
                if (!item) continue;
                var code = item.code || item.Code;
                if (!code) continue;
                catalog.push({
                    code: code,
                    nativeName: item.nativeName || item.NativeName || code,
                    englishName: item.englishName || item.EnglishName || code,
                    url: item.url || item.Url || ('./js/i18n/lang/' + code + '.js'),
                    order: item.order || 0
                });
            }
        }

        return catalog.length;
    };

    /**
     * 按清单动态注入语言包 <script> 并等待全部就绪。
     * 加载失败的条目会从清单里剔除，避免下拉里出现「选了没反应」的语言。
     * @param {Array} [list] 语言清单；省略则使用已登记的清单
     * @param {function} [done] 全部就绪（或失败）后回调，参数为成功加载的语言代码数组
     */
    window.I18nLoadPacks = function (list, done) {
        var targets = (list && list.length ? list : catalog).filter(function (x) {
            return x && x.code && x.url;
        });

        if (!targets.length) {
            if (done) done([]);
            return;
        }

        var pending = targets.length;
        var loaded = [];

        function settle(item, ok) {
            if (ok && allPacks()[item.code]) loaded.push(item.code);

            if (--pending === 0) {
                // 剔除没有成功装载语言包的条目（如文件被删/改名）
                catalog = catalog.filter(function (x) { return !!allPacks()[x.code]; });
                if (done) done(loaded);
            }
        }

        for (var i = 0; i < targets.length; i++) {
            (function (item) {
                var script = document.createElement('script');
                script.src = item.url;
                script.async = false;   // 保持按顺序执行，行为可预期
                script.charset = 'utf-8';
                script.onload = function () { settle(item, true); };
                script.onerror = function () { settle(item, false); };
                document.head.appendChild(script);
            })(targets[i]);
        }
    };

    /** 可选语言清单（副本） */
    window.I18nLanguages = function () {
        return catalog.map(function (x) {
            return {
                code: x.code,
                nativeName: x.nativeName,
                englishName: x.englishName,
                url: x.url,
                order: x.order
            };
        });
    };

    /** 受支持的界面语言标识列表 */
    window.I18nSupported = function () {
        if (catalog.length) return catalog.map(function (x) { return x.code; });
        return Object.keys(allPacks());
    };

    /** 当前语言的元信息（找不到返回 null） */
    window.I18nCurrentInfo = function () {
        return byCode(current);
    };

    /**
     * 推断初始语言：浏览器里存过的选择 → 浏览器语言 → 清单第一项。
     */
    window.I18nPickInitial = function () {
        var saved = (window.HySettings && window.HySettings.get('language', '')) || '';
        if (saved) {
            var matched = normalize(saved);
            if (matched) return matched;
        }

        var navs = pickNavigator();
        for (var i = 0; i < navs.length; i++) {
            if (!navs[i]) continue;
            var guess = normalize(navs[i]);
            if (guess) return guess;
        }

        return firstCatalogCode();
    };

    // ────────────────────────────────────────────────────────────────
    // 语言切换
    // ────────────────────────────────────────────────────────────────

    /** 通知业务侧（重新渲染动态内容） */
    function notifyListeners(lang) {
        for (var i = 0; i < listeners.length; i++) {
            try {
                listeners[i](lang);
            } catch (e) {
                if (window.console && console.warn) console.warn('i18n listener error', e);
            }
        }
    }

    /**
     * 切换界面语言（即时生效，无需刷新页面）。
     * @param {string} lang    目标语言标识
     * @param {object} [opts]  { persist: false 时不写入浏览器存储 }
     * @returns {string} 实际生效的语言标识
     */
    window.I18nSwitch = function (lang, opts) {
        opts = opts || {};
        var next = normalize(lang);
        var changed = next !== current;

        current = next;
        syncToLayui();

        if (typeof document !== 'undefined' && document.querySelectorAll) {
            applyDataI18n(document);
            applyDocumentLang();
        }
        rerenderLayuiComponents();

        if (changed) notifyListeners(current);

        // 用户选择保存在浏览器里（服务端不参与）
        if (opts.persist !== false && window.HySettings) {
            window.HySettings.set('language', current);
        }
        return current;
    };

    /**
     * 初始化 i18n。语言包与清单就绪后调用一次。
     * @param {string} [lang] 指定初始语言；省略则按「存储 → 浏览器语言 → 清单首项」推断
     */
    window.I18nInit = function (lang) {
        current = normalize(lang || window.I18nPickInitial());
        syncToLayui();
        applyDataI18n(document);
        applyDocumentLang();

        // Layui 组件的模板往往在 DOMContentLoaded 之后才渲染，再补一次
        if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', function () {
                setTimeout(function () {
                    applyDataI18n(document);
                    rerenderLayuiComponents();
                }, 0);
            });
        }

        return current;
    };

    /** 当前语言标识 */
    window.I18nCurrent = function () { return current; };

    /** 注册语言变更回调（用于重绘动态生成的内容） */
    window.I18nOnChange = function (fn) {
        if (typeof fn === 'function') listeners.push(fn);
    };

    // ────────────────────────────────────────────────────────────────
    // 同浏览器多标签页同步：另一个标签页改了语言，本页跟着切
    // （只跨标签页，不跨浏览器 —— 语言本就是「本机偏好」）
    // ────────────────────────────────────────────────────────────────
    if (window.addEventListener && window.HySettings) {
        window.addEventListener('storage', function (event) {
            if (!event || event.key !== window.HySettings.STORAGE_KEY) return;
            try {
                var parsed = event.newValue ? JSON.parse(event.newValue) : null;
                var next = parsed && parsed.language;
                if (next && next !== current) window.I18nSwitch(next, { persist: false });
            } catch (e) {
                // 忽略解析失败
            }
        });
    }

    // ────────────────────────────────────────────────────────────────
    // 预置 Layui 全局 locale
    // 注意：这里只写 locale，不写 messages —— 否则 layui.js 合并时会清掉内置文案
    // ────────────────────────────────────────────────────────────────
    current = window.I18nPickInitial();
    window.LAYUI_GLOBAL = window.LAYUI_GLOBAL || {};
    window.LAYUI_GLOBAL.i18n = window.LAYUI_GLOBAL.i18n || {};
    if (!window.LAYUI_GLOBAL.i18n.locale) {
        window.LAYUI_GLOBAL.i18n.locale = localeKey(current);
    }
})();

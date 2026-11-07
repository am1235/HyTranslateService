/**
 * HySettings —— WebUI 的浏览器侧设置存储
 *
 * 设计约定：
 *   · 前端的一切可记忆状态（界面语言、翻译面板偏好）都只存在本浏览器的
 *     localStorage 里，服务端不保存、不读取、不提供默认语言。
 *   · 这里只负责「存/取/合并」，具体默认数值由后端 /api/system/info 提供
 *     （那份数值来自命令行参数），前端用 null / '' 表示「沿用后端默认」。
 *   · localStorage 不可用时（隐私模式、iframe 沙箱）自动退化为内存存储，
 *     功能不中断，只是刷新后不保留。
 *
 * 必须在 i18n-config.js 与 app.js 之前引入。
 */
(function () {
    'use strict';

    var STORAGE_KEY = 'hytrans.settings.v1';

    /** 设置的完整形态（结构即契约） */
    var DEFAULTS = {
        /** 界面语言标识；空串表示「尚未选择」，由引擎按浏览器语言推断 */
        language: '',
        /** 翻译面板偏好；null / '' 表示沿用后端默认值 */
        options: {
            model: '',
            /** 推理引擎 auto/gpu/cpu；'' = 沿用后端默认 */
            engine: '',
            /** KV 缓存策略 none/memory；'' = 沿用后端默认 */
            kvCache: '',
            /** null = 尚未选择（沿用后端默认）；'' = 用户明确选了「自动识别」 */
            sourceLanguage: null,
            targetLanguage: '',
            segmentChars: null,
            maxTokens: 0,
            promptStyle: '',
            promptLanguage: 'zh',
            temperature: null,
            topP: null,
            topK: null,
            repetitionPenalty: null,
            /** 目标风格（多选 + 自定义），逗号拼接存于隐藏域 #targetStyle；'' = 未选择 */
            targetStyle: ''
        }
    };

    // ────────────────────────────────────────────────────────────────
    // 存储介质：优先 localStorage，不可用则退化为进程内内存
    // ────────────────────────────────────────────────────────────────
    var memory = null;

    var persistent = (function () {
        try {
            var probe = '__hy_probe__';
            window.localStorage.setItem(probe, '1');
            window.localStorage.removeItem(probe);
            return true;
        } catch (e) {
            return false;
        }
    })();

    function isPlainObject(value) {
        return !!value && typeof value === 'object' && !Array.isArray(value);
    }

    function clone(value) {
        if (Array.isArray(value)) return value.map(clone);
        if (isPlainObject(value)) {
            var copy = {};
            for (var k in value) {
                if (Object.prototype.hasOwnProperty.call(value, k)) copy[k] = clone(value[k]);
            }
            return copy;
        }
        return value;
    }

    /** 深合并 source → target（source 中的 null 会覆盖，用于「重置为后端默认」） */
    function merge(target, source) {
        if (!isPlainObject(source)) return target;
        for (var k in source) {
            if (!Object.prototype.hasOwnProperty.call(source, k)) continue;
            var value = source[k];
            if (isPlainObject(value)) {
                if (!isPlainObject(target[k])) target[k] = {};
                merge(target[k], value);
            } else if (value !== undefined) {
                target[k] = value;
            }
        }
        return target;
    }

    function loadRaw() {
        if (!persistent) return memory ? clone(memory) : {};
        try {
            var text = window.localStorage.getItem(STORAGE_KEY);
            if (!text) return {};
            var parsed = JSON.parse(text);
            return isPlainObject(parsed) ? parsed : {};
        } catch (e) {
            return {};
        }
    }

    function saveRaw(raw) {
        if (!persistent) {
            memory = clone(raw);
            return true;
        }
        try {
            window.localStorage.setItem(STORAGE_KEY, JSON.stringify(raw));
            return true;
        } catch (e) {
            // 配额超限等：不影响本次会话运行，只是没持久化
            return false;
        }
    }

    /** 读取完整设置（默认形态 + 已保存值，深合并后返回深拷贝） */
    function read() {
        var result = merge(clone(DEFAULTS), loadRaw());
        if (!isPlainObject(result.options)) result.options = clone(DEFAULTS.options);
        return result;
    }

    var HySettings = {
        /** localStorage 键名（跨标签页同步时用来比对） */
        STORAGE_KEY: STORAGE_KEY,

        /** 设置的默认形态 */
        DEFAULTS: DEFAULTS,

        /** 是否真的能持久化（false 表示已退化为内存存储） */
        persistent: persistent,

        /** 完整设置 */
        read: read,

        /** 顶层取值 */
        get: function (key, fallback) {
            var value = read()[key];
            return (value === undefined || value === null || value === '') ? fallback : value;
        },

        /** 顶层写值 */
        set: function (key, value) {
            var raw = loadRaw();
            raw[key] = value;
            saveRaw(raw);
            return read();
        },

        /** 读取翻译面板偏好 */
        options: function () {
            return read().options;
        },

        /** 批量写入翻译面板偏好 */
        patchOptions: function (partial) {
            var raw = loadRaw();
            if (!isPlainObject(raw.options)) raw.options = {};
            merge(raw.options, partial);
            saveRaw(raw);
            return read().options;
        },

        /** 写入单个翻译面板偏好 */
        setOption: function (name, value) {
            var partial = {};
            partial[name] = value;
            return HySettings.patchOptions(partial);
        },

        /** 清空全部设置（界面语言与面板偏好一起回到默认） */
        clear: function () {
            if (!persistent) {
                memory = null;
                return;
            }
            try {
                window.localStorage.removeItem(STORAGE_KEY);
            } catch (e) {
                // 忽略
            }
        }
    };

    window.HySettings = HySettings;
})();

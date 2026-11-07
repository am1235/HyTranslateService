/**
 * Layui 全局配置
 *
 * 说明：本项目使用 layui-v2.13.9 的完整打包版（lib/layui/layui.js），
 *      内置模块（form / element / layer / laytpl / dropdown / slider / util / i18n …）
 *      均已内联，layui.use() 不会再发起额外请求。
 *
 * base 仅用于解析「自定义扩展模块」（/js/lay-module/ 下的模块）。
 * 新增扩展：把文件放进 wwwroot/js/lay-module/，然后在 .extend({}) 里登记模块名。
 */
window.rootPath = (function (src) {
    src = document.scripts[document.scripts.length - 1].src;
    return src.substring(0, src.lastIndexOf("/") + 1);
})();

layui.config({
    base: "./js/lay-module/",
    version: "2.13.9"
}).extend({
    // 目前无需额外扩展模块；如需引入，请在此登记，例如：
    // echarts: "echarts/echarts",
});

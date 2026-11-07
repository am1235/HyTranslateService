using Microsoft.AspNetCore.Mvc;

namespace HyTranslateService.Controllers;

/// <summary>
/// 页面控制器：把 wwwroot/page/index.html 原样作为首页返回。
/// </summary>
[Route("/")]
public sealed class HomeController : ControllerBase
{
    /// <summary>Web 根目录文件提供器，用于按相对路径读取 wwwroot 下的页面文件。</summary>
    private readonly IWebHostEnvironment _env;

    /// <summary>日志记录器。</summary>
    private readonly ILogger<HomeController> _logger;

    /// <summary>构造函数，注入 Web 环境与日志。</summary>
    /// <param name="env">Web 宿主环境，用于定位 wwwroot 下的首页文件。</param>
    /// <param name="logger">日志记录器，用于记录页面缺失等异常情况。</param>
    public HomeController(IWebHostEnvironment env, ILogger<HomeController> logger)
    {
        _env = env;
        _logger = logger;
    }

    /// <summary>首页（WebUI）。</summary>
    /// <returns>200 返回 WebUI 首页 HTML；页面文件缺失时返回 404 与提示文本。</returns>
    /// <response code="200">返回 WebUI 首页 HTML（text/html; charset=utf-8）。</response>
    /// <response code="404">wwwroot/page/index.html 缺失。</response>
    /// <remarks>
    /// Sample request:
    ///
    ///     GET /
    ///
    /// Sample response:
    ///     text/html（page/index.html 的完整内容）
    /// </remarks>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Index()
    {
        var file = _env.WebRootFileProvider.GetFileInfo("page/index.html");
        if (!file.Exists)
        {
            string full = Path.Combine(_env.WebRootPath ?? "wwwroot", "page", "index.html");
            _logger.LogError("WebUI 页面缺失：{Path}", full);
            return NotFound("WebUI 页面缺失，请确认 wwwroot/page/index.html 存在。");
        }

        using var stream = file.CreateReadStream();
        using var reader = new StreamReader(stream);
        string html = await reader.ReadToEndAsync();

        _logger.LogDebug("WebUI 首页已响应（page/index.html，{Bytes} 字节，来源 {RemoteIp}）",
            html.Length, HttpContext.Connection.RemoteIpAddress);
        return Content(html, "text/html; charset=utf-8");
    }
}

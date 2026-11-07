using HyTranslateService.Services;
using Microsoft.AspNetCore.Mvc;

namespace HyTranslateService.Controllers;

/// <summary>
/// 界面语言控制器：把启动时扫描到的可选界面语言告诉前端。
/// </summary>
[Route("/api/[Controller]")]
[ApiController]
public sealed class LanguageController : ControllerBase
{
    /// <summary>启动时扫描到的界面语言目录，语言包由此动态加载。</summary>
    private readonly LanguageCatalog _catalog;

    /// <summary>构造函数，注入界面语言目录。</summary>
    /// <param name="catalog">界面语言目录，由启动时的语言包扫描填充。</param>
    public LanguageController(LanguageCatalog catalog) => _catalog = catalog;

    /// <summary>列出可选的界面语言（含语言包地址，供前端动态加载）。</summary>
    /// <returns>语言包目录、数量与语言清单（code/nativeName/englishName/displayName/url/order）。</returns>
    /// <response code="200">返回界面语言清单。</response>
    /// <remarks>
    /// Sample request:
    ///
    ///     GET /api/language/list
    ///
    /// Sample response:
    ///     {
    ///        "directory": "js/i18n/lang",
    ///        "count": 2,
    ///        "languages": [
    ///           {
    ///              "code": "en",
    ///              "nativeName": "English",
    ///              "englishName": "English",
    ///              "displayName": "English",
    ///              "url": "/js/i18n/lang/en.json",
    ///              "order": 1
    ///           }
    ///        ]
    ///     }
    /// </remarks>
    [HttpGet("list")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult List() => Ok(new
    {
        directory = LanguageCatalog.RelativeDirectory,
        count = _catalog.All.Count,
        languages = _catalog.All.Select(x => new
        {
            code = x.Code,
            nativeName = x.NativeName,
            englishName = x.EnglishName,
            displayName = x.DisplayName,
            url = x.Url,
            order = x.Order,
        }),
    });
}

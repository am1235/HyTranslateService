using HyTranslateService.SignalRHub;
using HyTranslateService.Translation;
using Microsoft.AspNetCore.Mvc;

namespace HyTranslateService.Controllers;

/// <summary>
/// 系统信息控制器：向前端提供一次性的元信息（默认参数、提示词模板清单、接入端点等）。
/// </summary>
[Route("/api/[Controller]")]
[ApiController]
public sealed class SystemController : ControllerBase
{
    /// <summary>应用配置：大小上限、默认语言与采样参数等命令行/环境变量可覆盖的选项。</summary>
    private readonly AppOptions _options;

    /// <summary>模型注册表：提供模型目录与推荐模型。</summary>
    private readonly ModelRegistry _registry;

    /// <summary>模型宿主：用于报告当前已加载的模型。</summary>
    private readonly ModelHost _host;

    /// <summary>翻译任务管理器：用于报告忙闲状态与当前任务。</summary>
    private readonly TranslationJobManager _jobs;

    /// <summary>构造函数，注入配置、模型注册表、模型宿主与任务管理器。</summary>
    /// <param name="options">应用配置，提供默认参数与各类大小上限。</param>
    /// <param name="registry">模型注册表，提供模型清单与推荐模型。</param>
    /// <param name="host">模型宿主，提供已加载模型列表。</param>
    /// <param name="jobs">翻译任务管理器，提供忙闲状态与当前任务 ID。</param>
    public SystemController(
        AppOptions options,
        ModelRegistry registry,
        ModelHost host,
        TranslationJobManager jobs)
    {
        _options = options;
        _registry = registry;
        _host = host;
        _jobs = jobs;
    }

    /// <summary>服务元信息与默认翻译参数。</summary>
    /// <returns>服务名/版本/启动时间、模型与任务状态、各类接入端点，以及 defaults 默认参数、promptStyles 提示词模板清单、engines 推理引擎与 kvCachePolicies 缓存策略清单。</returns>
    /// <response code="200">返回服务元信息。</response>
    /// <remarks>
    /// Sample request:
    ///
    ///     GET /api/system/info
    ///
    /// Sample response:
    ///     {
    ///        "server": "HyTranslateService",
    ///        "version": "1.0.0",
    ///        "status": "running",
    ///        "modelCount": 1,
    ///        "busy": false,
    ///        "mcpEndpoint": "/mcp",
    ///        "signalrPath": "/signalr/translation",
    ///        "defaults": {
    ///           "model": "hymt2-7b-q4",
    ///           "targetLanguage": "zh",
    ///           "segmentChars": 1200,
    ///           "temperature": 0.6,
    ///           "engine": "auto",
    ///           "kvCache": "memory"
    ///        },
    ///        "promptStyles": [ "short", "official", "terminology", "style", "delimiters", "background" ],
    ///        "engines": [ "auto", "gpu", "cpu" ],
    ///        "kvCachePolicies": [ "none", "memory" ]
    ///     }
    /// </remarks>
    [HttpGet("info")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Info() => Ok(new
    {
        server = HyTranslateServices.ServerName,
        version = HyTranslateServices.ServerVersion,
        status = "running",
        startedAt = StartupTime,
        modelsDirectory = _registry.ModelsDirectory,
        modelCount = _registry.Models.Count,
        loadedModels = _host.LoadedModelIds,
        busy = _jobs.IsBusy,
        currentJobId = _jobs.CurrentJobId,
        mcpEndpoint = _options.McpEndpoint,
        signalrPath = TranslationHubRoutes.Path,
        webApiBase = "/api",
        fileToolsEnabled = _options.EnableFileTools,
        defaults = new
        {
            model = _registry.Recommended?.Id,
            targetLanguage = _options.DefaultTargetLanguage,
            sourceLanguage = _options.DefaultSourceLanguage,
            segmentChars = _options.DefaultSegmentChars,
            maxSegmentChars = _options.MaxSegmentChars,
            maxTextBytes = _options.MaxTextBytes,
            maxFileBytes = _options.MaxFileUploadBytes,
            temperature = SamplingOptions.Recommended.Temperature,
            topP = SamplingOptions.Recommended.TopP,
            topK = SamplingOptions.Recommended.TopK,
            repetitionPenalty = SamplingOptions.Recommended.RepetitionPenalty,
            promptStyle = StyleId(PromptStyle.Short),
            promptLanguage = "en",
            engine = _options.Engine,
            kvCache = _options.KvCache,
        },
        promptStyles = Enum.GetValues<PromptStyle>().Select(StyleId),
        engines = InferenceEngines.All,
        kvCachePolicies = KvCachePolicies.All,
    });

    /// <summary>
    /// 提示词模板的对外标识（与前端 i18n 的 style.* 键一一对应）。
    /// </summary>
    /// <param name="style">内部提示词模板枚举值。</param>
    /// <returns>小写形式的对外标识；WithBackground 统一返回 background，保证与 ParseStyle 可往返。</returns>
    internal static string StyleId(PromptStyle style) => style switch
    {
        PromptStyle.WithBackground => "background",
        _ => style.ToString().ToLowerInvariant(),
    };

    /// <summary>服务启动时间，类型初始化时取值一次，进程存续期间不变。</summary>
    private static readonly DateTimeOffset StartupTime = DateTimeOffset.Now;
}

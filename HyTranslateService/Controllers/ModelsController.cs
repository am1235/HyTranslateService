using HyTranslateService.Translation;
using Microsoft.AspNetCore.Mvc;

namespace HyTranslateService.Controllers;

/// <summary>
/// 模型下载请求体。
/// </summary>
/// <param name="Id">要下载的官方模型 ID 或文件名（见 <see cref="ModelCatalog.All"/>）。</param>
public sealed record ModelDownloadRequest(string Id);

/// <summary>
/// 翻译模型控制器：下拉清单 = 官方发布的 5 个模型（<see cref="ModelCatalog"/>）+ 本地 models 目录里扫描到的额外模型，
/// 已下载的条目带 downloaded 标记；未下载的官方模型可一键自动下载（huggingface.co 受限时自动走 hf-mirror.com 镜像站）。
/// </summary>
[Route("/api/[Controller]")]
[ApiController]
public sealed class ModelsController : ControllerBase
{
    /// <summary>模型注册表：./models 目录的扫描结果、模型清单与推荐模型。</summary>
    private readonly ModelRegistry _registry;

    /// <summary>模型宿主：维护已加载的模型实例，用于标注清单中的 loaded 状态。</summary>
    private readonly ModelHost _host;

    /// <summary>模型下载器：官方模型的自动下载（含镜像站切换）与进度查询。</summary>
    private readonly ModelDownloader _downloader;

    /// <summary>日志记录器。</summary>
    private readonly ILogger<ModelsController> _logger;

    /// <summary>构造函数，注入模型注册表、模型宿主、下载器与日志。</summary>
    /// <param name="registry">模型注册表，提供模型清单与重新扫描。</param>
    /// <param name="host">模型宿主，用于查询各模型的加载状态。</param>
    /// <param name="downloader">模型下载器，提供自动下载与下载状态。</param>
    /// <param name="logger">日志记录器。</param>
    public ModelsController(ModelRegistry registry, ModelHost host, ModelDownloader downloader, ILogger<ModelsController> logger)
    {
        _registry = registry;
        _host = host;
        _downloader = downloader;
        _logger = logger;
    }

    /// <summary>列出可选翻译模型：官方目录 5 个 + 本地额外模型，已下载的带标记。</summary>
    /// <param name="refresh">为 true 时先重新扫描模型目录。</param>
    /// <returns>模型目录、合并后的模型总数、已下载数量、推荐模型 ID、模型清单（含 downloaded/approximate/repo 等）与当前下载状态。</returns>
    /// <response code="200">返回模型清单。</response>
    /// <remarks>
    /// Sample request:
    ///
    ///     GET /api/models?refresh=true
    ///
    /// Sample response:
    ///     {
    ///        "directory": "D:\\app\\models",
    ///        "count": 5,
    ///        "downloadedCount": 1,
    ///        "recommended": "Hy-MT2-1.8B-Q4_K_M",
    ///        "models": [
    ///           {
    ///              "id": "Hy-MT2-1.8B-1.25Bit",
    ///              "name": "Hy-MT2 1.8B · 1.25Bit",
    ///              "fileName": "Hy-MT2-1.8B-1.25Bit.gguf",
    ///              "quantization": "1.25Bit",
    ///              "sizeBytes": 461860800,
    ///              "sizeText": "~440MB",
    ///              "approximate": true,
    ///              "downloaded": true,
    ///              "loaded": false,
    ///              "recommended": false,
    ///              "repo": "tencent/Hy-MT2-1.8B-1.25Bit-GGUF",
    ///              "description": "最快，decode 速度约提升 74%，质量略有损失"
    ///           }
    ///        ],
    ///        "download": {
    ///           "status": "idle",
    ///           "modelId": null,
    ///           "fileName": null,
    ///           "receivedBytes": 0,
    ///           "totalBytes": null,
    ///           "sourceHost": null,
    ///           "error": null,
    ///           "progress": null
    ///        }
    ///     }
    /// </remarks>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Get(bool? refresh = null)
    {
        var local = refresh == true ? _registry.Refresh() : _registry.Models;
        var merged = MergeCatalog(local);
        string recommended = RecommendedId(merged);
        int downloaded = merged.Count(m => m.Downloaded);

        return Ok(new
        {
            directory = _registry.ModelsDirectory,
            count = merged.Count,
            downloadedCount = downloaded,
            recommended,
            models = merged.Select(m => new
            {
                id = m.Id,
                name = m.Name,
                fileName = m.FileName,
                quantization = m.Quantization,
                sizeBytes = m.SizeBytes,
                sizeText = m.SizeText,
                approximate = m.Approximate,
                downloaded = m.Downloaded,
                loaded = _host.IsLoaded(m.Id),
                recommended = m.Id == recommended,
                repo = m.Repo,
                description = m.Description,
            }),
            download = Project(_downloader.Current),
        });
    }

    /// <summary>重新扫描模型目录（放入新的 gguf 后无需重启）。</summary>
    /// <returns>重新扫描后的模型目录、已下载数量、推荐模型 ID 与已下载模型 ID 清单。</returns>
    /// <response code="200">返回最新模型清单。</response>
    /// <remarks>
    /// Sample request:
    ///
    ///     POST /api/models/refresh
    ///
    /// Sample response:
    ///     {
    ///        "directory": "D:\\app\\models",
    ///        "count": 2,
    ///        "recommended": "Hy-MT2-1.8B-Q4_K_M",
    ///        "models": [ "Hy-MT2-1.8B-1.25Bit", "Hy-MT2-1.8B-Q4_K_M" ]
    ///     }
    /// </remarks>
    [HttpPost("refresh")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Refresh()
    {
        var local = _registry.Refresh();
        var merged = MergeCatalog(local);
        int downloaded = merged.Count(m => m.Downloaded);
        _logger.LogInformation("模型目录已重新扫描（{Directory}）：已下载 {Count} 个", _registry.ModelsDirectory, downloaded);
        return Ok(new
        {
            directory = _registry.ModelsDirectory,
            count = downloaded,
            recommended = RecommendedId(merged),
            models = merged.Where(m => m.Downloaded).Select(m => m.Id),
        });
    }

    /// <summary>开始下载指定官方模型；已在本地则直接返回完成态（幂等），下载在后台执行。</summary>
    /// <param name="request">下载请求，含模型 ID。</param>
    /// <returns>启动后的下载状态快照；已在本地时 started 为 false。</returns>
    /// <response code="200">下载已启动（或模型已在本地）。</response>
    /// <response code="400">模型 ID 不在官方目录中。</response>
    /// <response code="409">已有下载在进行，或目标模型正被常驻占用。</response>
    /// <remarks>
    /// Sample request:
    ///
    ///     POST /api/models/download
    ///     { "id": "Hy-MT2-1.8B-Q4_K_M" }
    ///
    /// Sample response:
    ///     {
    ///        "started": true,
    ///        "download": { "status": "probing", "modelId": "Hy-MT2-1.8B-Q4_K_M", "progress": null }
    ///     }
    /// </remarks>
    [HttpPost("download")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public IActionResult Download([FromBody] ModelDownloadRequest request)
    {
        try
        {
            var state = _downloader.StartDownload(request.Id);
            return Ok(new
            {
                started = state.Status != ModelDownloadStatus.Completed,
                download = Project(state),
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    /// <summary>查询当前模型下载状态（前端轮询进度用）。</summary>
    /// <returns>当前下载状态快照；无任务时为 idle。</returns>
    /// <response code="200">返回下载状态。</response>
    /// <remarks>
    /// Sample request:
    ///
    ///     GET /api/models/download
    ///
    /// Sample response:
    ///     {
    ///        "download": {
    ///           "status": "downloading",
    ///           "modelId": "Hy-MT2-1.8B-Q4_K_M",
    ///           "fileName": "Hy-MT2-1.8B-Q4_K_M.gguf",
    ///           "receivedBytes": 524288000,
    ///           "totalBytes": 1133080448,
    ///           "sourceHost": "https://hf-mirror.com",
    ///           "error": null,
    ///           "progress": 0.4627
    ///        }
    ///     }
    /// </remarks>
    [HttpGet("download")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult DownloadStatus()
    {
        return Ok(new { download = Project(_downloader.Current) });
    }

    /// <summary>取消当前模型下载（无进行中的下载为空操作），并删除未完成的临时文件。</summary>
    /// <returns>取消后的下载状态快照。</returns>
    /// <response code="200">返回取消后的下载状态。</response>
    /// <remarks>
    /// Sample request:
    ///
    ///     POST /api/models/download/cancel
    ///
    /// Sample response:
    ///     {
    ///        "download": { "status": "cancelled", "modelId": "Hy-MT2-1.8B-Q4_K_M", "error": "下载已被取消。" }
    ///     }
    /// </remarks>
    [HttpPost("download/cancel")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult CancelDownload()
    {
        _downloader.Cancel();
        return Ok(new { download = Project(_downloader.Current) });
    }

    #region 私有方法

    /// <summary>
    /// 合并官方目录与本地扫描结果：目录条目按质量从低到高在前，本地额外模型追加在后。
    /// </summary>
    /// <param name="local">本地 models 目录扫描到的模型。</param>
    /// <returns>合并后的下拉条目。</returns>
    private static List<MergedModel> MergeCatalog(IReadOnlyList<TranslationModelInfo> local)
    {
        var result = new List<MergedModel>();
        foreach (var official in ModelCatalog.All)
        {
            var hit = local.FirstOrDefault(m => string.Equals(m.Id, official.Id, StringComparison.OrdinalIgnoreCase));
            result.Add(hit is not null
                ? new MergedModel(
                    official.Id,
                    official.DisplayName,
                    official.FileName,
                    official.Quantization,
                    hit.SizeBytes,
                    hit.SizeText,
                    Approximate: false,
                    Downloaded: true,
                    Repo: official.Repo,
                    Description: official.Description)
                : new MergedModel(
                    official.Id,
                    official.DisplayName,
                    official.FileName,
                    official.Quantization,
                    official.ApproximateSizeBytes,
                    "~" + TranslationModelInfo.FormatSize(official.ApproximateSizeBytes),
                    Approximate: true,
                    Downloaded: false,
                    Repo: official.Repo,
                    Description: official.Description));
        }

        foreach (var extra in local.Where(m => ModelCatalog.Find(m.Id) is null)
                                   .OrderByDescending(m => m.QualityRank)
                                   .ThenByDescending(m => m.SizeBytes))
        {
            string name = extra.Quantization is "unknown" || extra.Id.Contains(extra.Quantization, StringComparison.OrdinalIgnoreCase)
                ? extra.Id
                : extra.Id + " · " + extra.Quantization;
            result.Add(new MergedModel(
                extra.Id,
                name,
                extra.FileName,
                extra.Quantization,
                extra.SizeBytes,
                extra.SizeText,
                Approximate: false,
                Downloaded: true,
                Repo: null,
                Description: string.Empty));
        }

        return result;
    }

    /// <summary>
    /// 推荐模型 ID：已下载中质量最高的；一个都没下载时回退官方默认（Q4_K_M），引导用户优先下载。
    /// </summary>
    /// <param name="merged">合并后的下拉条目。</param>
    /// <returns>推荐模型 ID。</returns>
    private static string RecommendedId(List<MergedModel> merged)
    {
        var best = merged.Where(m => m.Downloaded)
            .OrderByDescending(m => ModelRegistry.GuessQuantization(m.FileName).Rank)
            .ThenByDescending(m => m.SizeBytes)
            .FirstOrDefault();
        return best?.Id ?? ModelCatalog.Default.Id;
    }

    /// <summary>把下载状态投影为前端约定的小写字符串状态（枚举不走字符串序列化器，保持线上格式稳定）。</summary>
    /// <param name="state">下载状态快照。</param>
    /// <returns>匿名对象（camelCase 由 ApiJson 统一处理）。</returns>
    private static object Project(ModelDownloadState state) => new
    {
        status = StatusText(state.Status),
        modelId = state.ModelId,
        fileName = state.FileName,
        receivedBytes = state.ReceivedBytes,
        totalBytes = state.TotalBytes,
        sourceHost = state.SourceHost,
        error = state.Error,
        progress = state.Progress,
    };

    /// <summary>下载状态枚举的对外文本（与前端轮询逻辑逐字对应）。</summary>
    /// <param name="status">下载状态枚举值。</param>
    /// <returns>小写状态字符串。</returns>
    private static string StatusText(ModelDownloadStatus status) => status switch
    {
        ModelDownloadStatus.Probing => "probing",
        ModelDownloadStatus.Downloading => "downloading",
        ModelDownloadStatus.Completed => "completed",
        ModelDownloadStatus.Failed => "failed",
        ModelDownloadStatus.Cancelled => "cancelled",
        _ => "idle",
    };

    /// <summary>
    /// 合并后的下拉条目（不可变投影，供 JSON 序列化与推荐判定共用）。
    /// </summary>
    /// <param name="Id">模型 ID。</param>
    /// <param name="Name">下拉显示名。</param>
    /// <param name="FileName">文件名。</param>
    /// <param name="Quantization">量化标识。</param>
    /// <param name="SizeBytes">文件大小（已下载为实际值，未下载为近似值）。</param>
    /// <param name="SizeText">人类可读大小（近似值带 ~ 前缀）。</param>
    /// <param name="Approximate">大小是否为近似值。</param>
    /// <param name="Downloaded">是否已下载到本地。</param>
    /// <param name="Repo">官方仓库标识；本地额外模型为 null。</param>
    /// <param name="Description">一句话特点说明。</param>
    private sealed record MergedModel(
        string Id,
        string Name,
        string FileName,
        string Quantization,
        long SizeBytes,
        string SizeText,
        bool Approximate,
        bool Downloaded,
        string? Repo,
        string Description);

    #endregion
}

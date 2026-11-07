using System.Diagnostics;
using System.Net;

namespace HyTranslateService.Translation;

/// <summary>
/// 模型下载状态机的取值。
/// </summary>
public enum ModelDownloadStatus
{
    /// <summary>空闲：当前没有下载任务。</summary>
    Idle,

    /// <summary>正在探测 huggingface.co 可达性，以决定走官方站还是镜像站。</summary>
    Probing,

    /// <summary>正在下载模型文件。</summary>
    Downloading,

    /// <summary>下载已完成（终态）。</summary>
    Completed,

    /// <summary>下载失败（终态）。</summary>
    Failed,

    /// <summary>下载被用户取消（终态）。</summary>
    Cancelled,
}

/// <summary>
/// 模型下载状态快照：供 WebUI 轮询展示进度（只读，不可变）。
/// </summary>
public sealed record ModelDownloadState
{
    /// <summary>当前状态。</summary>
    public ModelDownloadStatus Status { get; init; }

    /// <summary>正在下载的模型 ID（官方目录条目）。</summary>
    public string? ModelId { get; init; }

    /// <summary>下载目标文件名。</summary>
    public string? FileName { get; init; }

    /// <summary>已下载字节数。</summary>
    public long ReceivedBytes { get; init; }

    /// <summary>总字节数（取响应头 Content-Length，可能为 null 表示未知）。</summary>
    public long? TotalBytes { get; init; }

    /// <summary>实际使用的下载源主机（huggingface.co 或 hf-mirror.com）。</summary>
    public string? SourceHost { get; init; }

    /// <summary>失败或取消时的错误说明。</summary>
    public string? Error { get; init; }

    /// <summary>下载进度 0~1；总大小未知时为 null。</summary>
    public double? Progress => TotalBytes is > 0
        ? Math.Min(1.0, (double)ReceivedBytes / TotalBytes.Value)
        : null;
}

/// <summary>
/// 官方模型下载器：把 Hugging Face 上的 GGUF 权重下载到本地 models 目录。
/// 下载前探测 huggingface.co 是否可达，不可达（如跨境网络受限）时自动改用镜像站 hf-mirror.com；
/// 即使探测通过，下载过程中失败也会自动重试另一个源。同一时刻只允许一个下载任务。
/// </summary>
public sealed class ModelDownloader
{
    /// <summary>Hugging Face 官方站。</summary>
    private const string HuggingFaceHost = "https://huggingface.co";

    /// <summary>Hugging Face 镜像站（官方站不可达时使用）。</summary>
    private const string MirrorHost = "https://hf-mirror.com";

    /// <summary>探测官方站可达性的超时时间（秒）：受限网络下连接会被挂起，不宜等太久。</summary>
    private const int ProbeTimeoutSeconds = 8;

    /// <summary>进度上报的最小字节间隔，避免高频锁竞争。</summary>
    private const long ProgressReportBytes = 256 * 1024;

    /// <summary>下载用的 HttpClient：不设总超时（大文件流式下载靠取消令牌终止）。</summary>
    private static readonly Lazy<HttpClient> SharedClient = new(CreateHttpClient);

    /// <summary>模型注册表：提供模型目录与下载完成后的重新扫描。</summary>
    private readonly ModelRegistry _registry;

    /// <summary>模型宿主：下载前确认目标模型未被常驻占用，避免覆盖正在使用的文件。</summary>
    private readonly ModelHost _host;

    /// <summary>日志记录器。</summary>
    private readonly ILogger<ModelDownloader> _logger;

    /// <summary>启动与取消的互斥门：保证同一时刻只有一个下载任务，且取消不会错过刚开始的任务。</summary>
    private readonly object _startGate = new();

    /// <summary>状态读写锁：保护 <see cref="_current"/> 与下载循环的进度上报。</summary>
    private readonly object _stateGate = new();

    /// <summary>当前下载状态；终态会保留到最后一次查询。</summary>
    private ModelDownloadState _current = new() { Status = ModelDownloadStatus.Idle };

    /// <summary>当前下载任务的取消源；无任务时为 null。</summary>
    private CancellationTokenSource? _cts;

    /// <summary>创建下载器。</summary>
    /// <param name="registry">模型注册表，提供模型目录与下载完成后的重新扫描。</param>
    /// <param name="host">模型宿主，用于确认目标模型未被常驻占用。</param>
    /// <param name="logger">日志记录器。</param>
    public ModelDownloader(ModelRegistry registry, ModelHost host, ILogger<ModelDownloader> logger)
    {
        _registry = registry;
        _host = host;
        _logger = logger;
        CleanStalePartFiles();
    }

    /// <summary>当前下载状态快照（线程安全）。</summary>
    public ModelDownloadState Current
    {
        get
        {
            lock (_stateGate) return _current;
        }
    }

    /// <summary>是否有下载正在进行（探测或下载中）。</summary>
    public bool IsBusy
    {
        get
        {
            ModelDownloadStatus status = Current.Status;
            return status is ModelDownloadStatus.Probing or ModelDownloadStatus.Downloading;
        }
    }

    #region 公共方法

    /// <summary>
    /// 开始下载指定官方模型。已在本地目录中的模型直接返回完成态（幂等）。
    /// 下载在后台执行，本方法立即返回；进度通过 <see cref="Current"/> 轮询。
    /// </summary>
    /// <param name="modelId">官方模型 ID 或文件名（见 <see cref="ModelCatalog.All"/>）。</param>
    /// <returns>启动时的状态快照（Probing 或 Completed）。</returns>
    /// <exception cref="ArgumentException">模型 ID 不在官方目录中。</exception>
    /// <exception cref="InvalidOperationException">已有下载在进行，或目标模型正被常驻占用。</exception>
    public ModelDownloadState StartDownload(string modelId)
    {
        var official = ModelCatalog.Find(modelId)
            ?? throw new ArgumentException(
                $"未知的官方模型 \"{modelId}\"。可下载的模型：" +
                string.Join(", ", ModelCatalog.All.Select(m => m.Id)) + "。");

        if (_registry.Models.Any(m => string.Equals(m.Id, official.Id, StringComparison.OrdinalIgnoreCase)))
        {
            return new ModelDownloadState
            {
                Status = ModelDownloadStatus.Completed,
                ModelId = official.Id,
                FileName = official.FileName,
                ReceivedBytes = official.ApproximateSizeBytes,
                TotalBytes = official.ApproximateSizeBytes,
            };
        }

        if (_host.IsLoaded(official.Id))
            throw new InvalidOperationException(
                $"模型 {official.Id} 正在内存中使用，无法覆盖下载；请先停止当前翻译任务或等待其结束。");

        lock (_startGate)
        {
            if (IsBusy)
                throw new InvalidOperationException(
                    $"已有下载任务在进行（{Current.ModelId}），请先等待其完成或取消。");

            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            CancellationToken ct = _cts.Token;

            Report(new ModelDownloadState
            {
                Status = ModelDownloadStatus.Probing,
                ModelId = official.Id,
                FileName = official.FileName,
            });

            // 后台执行：不随 HTTP 请求生命周期终止，浏览器关掉也继续下载
            _ = Task.Run(() => DownloadAsync(official, ct), CancellationToken.None);

            return Current;
        }
    }

    /// <summary>取消当前下载（无进行中的下载时为空操作）。取消后会删除未完成的 .part 临时文件。</summary>
    public void Cancel()
    {
        lock (_startGate)
        {
            _cts?.Cancel();
        }
    }

    #endregion

    #region 私有方法

    /// <summary>
    /// 下载主流程：探测源站 → 依次尝试官方站/镜像站 → 落盘改名 → 刷新注册表。
    /// 所有异常都在此收敛为 Failed 终态，绝不让后台任务裸抛。
    /// </summary>
    /// <param name="official">官方模型目录条目。</param>
    /// <param name="ct">取消令牌。</param>
    private async Task DownloadAsync(OfficialModelInfo official, CancellationToken ct)
    {
        string tempPath = Path.Combine(_registry.ModelsDirectory, official.FileName + ".part");
        string finalPath = Path.Combine(_registry.ModelsDirectory, official.FileName);

        try
        {
            bool hfReachable = await ProbeAsync(official.Repo, HuggingFaceHost, ct);
            _logger.LogInformation(
                "模型下载源探测：huggingface.co {Result}，本次使用 {Host}",
                hfReachable ? "可达" : "不可达（自动切换镜像站）",
                hfReachable ? HuggingFaceHost : MirrorHost);

            // 首选源按探测结果定；失败后用另一个源重试一次，兼顾「探测通过但下载被拦」的情况
            string[] hosts = hfReachable ? [HuggingFaceHost, MirrorHost] : [MirrorHost, HuggingFaceHost];
            if (!hfReachable && !await ProbeAsync(official.Repo, MirrorHost, ct))
                throw new InvalidOperationException(
                    "huggingface.co 与镜像站 hf-mirror.com 均不可访问，请检查网络或代理设置后重试。");

            string usedHost = hosts[0];
            Exception? lastError = null;
            foreach (string host in hosts)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    await DownloadFromAsync(host, official, tempPath, ct);
                    usedHost = host;
                    lastError = null;
                    break;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    _logger.LogWarning(ex, "从 {Host} 下载 {File} 失败，尝试其他源", host, official.FileName);
                }
            }

            if (lastError is not null)
                throw new InvalidOperationException($"模型下载失败：{lastError.Message}", lastError);

            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(_registry.ModelsDirectory);
            File.Move(tempPath, finalPath, overwrite: true);
            _registry.Refresh();

            Report(new ModelDownloadState
            {
                Status = ModelDownloadStatus.Completed,
                ModelId = official.Id,
                FileName = official.FileName,
                ReceivedBytes = official.ApproximateSizeBytes,
                TotalBytes = official.ApproximateSizeBytes,
                SourceHost = usedHost,
            });
            _logger.LogInformation("模型 {Id} 下载完成（来源 {Host}）：{File}", official.Id, usedHost, official.FileName);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            DeleteQuietly(tempPath);
            Report(new ModelDownloadState
            {
                Status = ModelDownloadStatus.Cancelled,
                ModelId = official.Id,
                FileName = official.FileName,
                Error = "下载已被取消。",
            });
            _logger.LogInformation("模型 {Id} 下载已取消", official.Id);
        }
        catch (Exception ex)
        {
            DeleteQuietly(tempPath);
            Report(new ModelDownloadState
            {
                Status = ModelDownloadStatus.Failed,
                ModelId = official.Id,
                FileName = official.FileName,
                Error = ex.Message,
            });
            _logger.LogError(ex, "模型 {Id} 下载失败", official.Id);
        }
    }

    /// <summary>
    /// 从指定主机流式下载单个模型文件到 .part 临时文件，并按节流策略上报进度。
    /// </summary>
    /// <param name="host">下载源主机（含协议，如 https://hf-mirror.com）。</param>
    /// <param name="official">官方模型目录条目。</param>
    /// <param name="tempPath">.part 临时文件路径。</param>
    /// <param name="ct">取消令牌。</param>
    private async Task DownloadFromAsync(string host, OfficialModelInfo official, string tempPath, CancellationToken ct)
    {
        string url = $"{host}/{official.Repo}/resolve/main/{official.FileName}";
        using var response = await SharedClient.Value.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        long? total = response.Content.Headers.ContentLength;

        Directory.CreateDirectory(_registry.ModelsDirectory);
        await using var network = await response.Content.ReadAsStreamAsync(ct);
        await using var file = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);

        var buffer = new byte[81920];
        long received = 0;
        long lastReported = -1;
        var throttle = Stopwatch.StartNew();
        int read;
        while ((read = await network.ReadAsync(buffer, ct)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read), ct);
            received += read;
            if (received - lastReported >= ProgressReportBytes || throttle.ElapsedMilliseconds >= 250)
            {
                lastReported = received;
                throttle.Restart();
                Report(new ModelDownloadState
                {
                    Status = ModelDownloadStatus.Downloading,
                    ModelId = official.Id,
                    FileName = official.FileName,
                    ReceivedBytes = received,
                    TotalBytes = total,
                    SourceHost = host,
                });
            }
        }

        await file.FlushAsync(ct);
    }

    /// <summary>
    /// 探测指定主机的 Hugging Face API 是否可达（超时或异常视为不可达）。
    /// </summary>
    /// <param name="repo">用于探测的仓库标识（取待下载模型的仓库）。</param>
    /// <param name="host">待探测主机（含协议）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>可达返回 true。</returns>
    private static async Task<bool> ProbeAsync(string repo, string host, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(ProbeTimeoutSeconds));
        try
        {
            using var response = await SharedClient.Value.GetAsync(
                $"{host}/api/models/{repo}", HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>更新当前状态快照（加锁替换，读取方无锁读到旧值也安全）。</summary>
    /// <param name="state">新的状态快照。</param>
    private void Report(ModelDownloadState state)
    {
        lock (_stateGate) _current = state;
    }

    /// <summary>删除文件，忽略一切异常（清理临时文件的最佳努力）。</summary>
    /// <param name="path">待删除的文件路径。</param>
    private static void DeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // 清理失败不影响主流程：残留的 .part 不会被打包进模型清单
        }
    }

    /// <summary>
    /// 启动时清理上一次进程遗留的 .part 临时文件（未完成的下载残留，不可能是有效模型）。
    /// </summary>
    private void CleanStalePartFiles()
    {
        try
        {
            foreach (string part in Directory.EnumerateFiles(_registry.ModelsDirectory, "*.part"))
            {
                DeleteQuietly(part);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "清理遗留 .part 临时文件失败（可忽略）");
        }
    }

    /// <summary>
    /// 创建全局共享的 HttpClient：尊重 HYTRANSLATE_HTTP_PROXY / HTTPS_PROXY / HTTP_PROXY 环境变量，
    /// 未设置时用系统默认代理；不设总超时（大文件流式下载靠取消令牌终止）。
    /// </summary>
    /// <returns>配置好的 HttpClient 实例。</returns>
    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(15),
        };

        string? proxyUrl = Environment.GetEnvironmentVariable("HYTRANSLATE_HTTP_PROXY")
            ?? Environment.GetEnvironmentVariable("HTTPS_PROXY")
            ?? Environment.GetEnvironmentVariable("HTTP_PROXY");
        if (!string.IsNullOrWhiteSpace(proxyUrl))
        {
            handler.Proxy = new WebProxy(proxyUrl);
            handler.UseProxy = true;
        }

        var client = new HttpClient(handler);
        client.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
        return client;
    }

    #endregion
}

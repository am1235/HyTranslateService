namespace HyTranslateService.Translation;

/// <summary>
/// 翻译任务管理器。WebUI、WebAPI 与 MCP 工具共用。
/// 并发语义为「忙则拒绝」：同一时刻只跑一个任务，只维护「当前任务」这一个对象——
/// 没有队列、没有历史；已有任务在跑时新任务立即抛 <see cref="ModelBusyException"/>。
/// </summary>
public sealed class TranslationJobManager : IDisposable
{
    #region 字段

    /// <summary>应用配置：默认语言、分段大小等归一化默认值的来源。</summary>
    private readonly AppOptions _options;

    /// <summary>模型注册表：把宽松的模型描述解析成具体模型。</summary>
    private readonly ModelRegistry _registry;

    /// <summary>模型宿主：负责模型独占闸门与加载状态上报。</summary>
    private readonly ModelHost _modelHost;

    /// <summary>翻译引擎：流式产出片段翻译事件。</summary>
    private readonly TranslationEngine _engine;

    /// <summary>日志器。</summary>
    private readonly ILogger<TranslationJobManager> _logger;

    /// <summary>任务 ID 自增计数（十六进制短 ID 的可读前缀）。</summary>
    private int _jobCounter;

    /// <summary>当前独占模型的唯一任务（null = 空闲，可以受理新任务）。</summary>
    private volatile TranslationJob? _active;

    /// <summary>
    /// 最近一次结束的任务（内存保留，供下载接口取译文；新任务开始前不清除）。
    /// 翻译结果按用户约定**永不写磁盘**——这里就是唯一的「事后可取」位置。
    /// </summary>
    private volatile TranslationJob? _lastFinished;

    #endregion

    #region 公共属性

    /// <summary>当前正在执行的任务（占用模型者）；null 表示空闲。</summary>
    public TranslationJob? CurrentJob => _active;

    /// <summary>当前正在执行的任务 ID；null 表示空闲。</summary>
    public string? CurrentJobId => _active?.Id;

    /// <summary>当前是否已有任务在跑（此时新任务会被拒绝）。</summary>
    public bool IsBusy => _active is not null;

    #endregion

    /// <summary>创建任务管理器；依赖均来自 DI 单例。</summary>
    /// <param name="options">应用配置。</param>
    /// <param name="registry">模型注册表。</param>
    /// <param name="modelHost">模型宿主（独占闸门）。</param>
    /// <param name="engine">翻译引擎。</param>
    /// <param name="logger">日志器。</param>
    public TranslationJobManager(
        AppOptions options,
        ModelRegistry registry,
        ModelHost modelHost,
        TranslationEngine engine,
        ILogger<TranslationJobManager> logger)
    {
        _options = options;
        _registry = registry;
        _modelHost = modelHost;
        _engine = engine;
        _logger = logger;
    }

    #region 私有方法

    /// <summary>把「忙」拼成一句可直接展示、且带下一步动作的话。</summary>
    /// <param name="reason">ModelHost 给出的忙原因；可为 null。</param>
    /// <returns>拼好的完整提示语。</returns>
    private string DescribeBusy(string? reason)
    {
        var parts = new List<string> { reason ?? "模型正忙，无法开始新的翻译任务。" };

        var active = _active;
        if (active is not null)
        {
            var snapshot = active.Snapshot();
            parts.Add($"当前正在执行的任务：{snapshot.JobId}"
                + $"（状态 {snapshot.Status}，模型 {snapshot.Model}，"
                + $"进度 {snapshot.CompletedSegments}/{snapshot.TotalSegments} 段）。");
        }

        parts.Add("请先查询当前任务状态；若确实要开始新的翻译，请先停止它，然后再提交。");
        return string.Join(" ", parts);
    }

    /// <summary>把 ModelHost 的状态说明转成任务事件（卸载旧模型 / 加载新模型）。</summary>
    /// <param name="job">要上报的目标任务。</param>
    /// <param name="status">模型宿主状态。</param>
    private static void ReportModelStatus(TranslationJob job, ModelHost.ModelStatus status)
        => job.RaiseModelLoading(status.Message);

    /// <summary>任务执行体：加载模型、驱动引擎逐段翻译并结算终态；后台运行，异常都落在任务自身状态上。</summary>
    /// <param name="job">已受理的任务。</param>
    /// <param name="lease">已获取的模型独占租约；结束时归还。</param>
    /// <param name="segments">分段计划。</param>
    private async Task RunAsync(TranslationJob job, ModelLease lease, IReadOnlyList<TextSegment> segments)
    {
        try
        {
            using (lease)
            {
                var request = job.Request;

                // 受理后立刻被终止：不必白白加载一遍模型
                if (job.CancellationToken.IsCancellationRequested)
                {
                    job.MarkCancelled();
                    return;
                }

                // 已独占模型：必要时先卸载旧模型再加载（状态通过任务事件上报）
                var model = lease.EnsureLoaded(status => ReportModelStatus(job, status));

                bool completed = false;
                try
                {
                    job.RaiseRunning();

                    await foreach (var evt in _engine
                        .TranslateAsync(model, request, segments, job.CancellationToken)
                        .ConfigureAwait(false))
                    {
                        job.ApplyEngineEvent(evt);
                    }

                    completed = true;
                }
                finally
                {
                    // 请求收尾：不缓存策略必须释放 KV 缓冲（否则选项形同虚设）；
                    // 失败 / 终止的任务也一律收尾，避免把写了一半的缓存留给下一个任务。
                    // 收尾失败不能让已经成功的任务变成失败，只记日志。
                    if (KvCachePolicies.ShouldEndRequest(request.KvCache, completed))
                    {
                        try
                        {
                            model.EndRequest();
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug(ex, "任务 {JobId} 的 KV 缓存收尾失败（忽略）", job.Id);
                        }
                    }
                }

                var finished = job.Snapshot();
                double seconds = finished.ElapsedMs / 1000.0;
                double charSpeed = seconds > 0 ? finished.SourceChars / seconds : 0;
                double tokenSpeed = seconds > 0 ? finished.GeneratedTokens / seconds : 0;
                string backend = _modelHost.ResidentBackendName ?? "cpu";
                _logger.LogInformation(
                    "任务 {JobId} 完成：{Done}/{Total} 段，原文 {Chars} 字符，生成 {Tokens} token；" +
                    "翻译耗时 {Elapsed:0.0} 秒；速度 {CharSpeed:0} 字符/秒（{TokenSpeed:0} token/秒）；" +
                    "模型 {Model}（{Quant}），引擎 {Engine}→后端 {Backend}，KV {Kv}",
                    job.Id, finished.CompletedSegments, finished.TotalSegments, finished.TotalChars, finished.GeneratedTokens,
                    finished.ElapsedMs / 1000, charSpeed, tokenSpeed,
                    finished.Model, finished.ModelQuantization, finished.Engine, backend, finished.KvCache);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("任务 {JobId} 已终止", job.Id);
            job.MarkCancelled();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "任务 {JobId} 失败", job.Id);
            job.Fail(ex.Message);
        }
        finally
        {
            // 释放「唯一执行者」身份；模型闸门已随 using 结束归还。
            // 结束的任务记入 _lastFinished（内存），供 GET /api/translation/file-result 下载译文。
            if (ReferenceEquals(_active, job))
            {
                // 先捕获并替换旧任务，再释放旧的——绝不可在置空前释放刚完成的新任务
                var prev = _lastFinished;
                _lastFinished = job;
                _active = null;
                prev?.Dispose();
            }
        }
    }

    #endregion

    #region 公共方法

    /// <summary>
    /// 创建并启动一个翻译任务，使其成为「当前任务」。
    /// 不再以异常表达「忙 / 参数无效」，而是返回中性 <see cref="JobStartResult"/>，
    /// 由调用方映射成各自的对外契约（HTTP 409 / SignalR error 事件 / McpException），
    /// 从而消除各入口重复的 try/catch 逻辑。语义与原 <c>Start</c> 一致。
    /// </summary>
    /// <param name="rawRequest">宽松原始请求（字段可选，解析时归一化）。</param>
    /// <returns>受理结果：已启动 / 忙 / 参数无效。</returns>
    public JobStartResult TryStart(TranslationRequest rawRequest)
    {
        ResolvedTranslationRequest resolved;
        try
        {
            resolved = ResolvedTranslationRequest.Resolve(rawRequest, _registry, _options);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // 参数无效 / 无法归一化：与原先 try/catch 范围一致（其余异常仍向上传播）
            return JobStartResult.ForInvalid(ex);
        }

        // 分段是纯 CPU 计算且很快，同步完成后任务一创建即带准确总段数；
        // 不设段数上限——逐段串行翻译成本随文本量线性增长，输入大小已由各入口收口，内存天然有界。
        var segments = TextSegmenter.Segment(resolved.Text, resolved.SegmentChars);

        // ★ 忙则拒绝：非阻塞独占模型，拿不到立刻拒绝
        ModelLease? lease = _modelHost.TryAcquire(resolved.Model.Id, resolved.Engine, resolved.KvCache, out string? busyReason);
        if (lease is null)
            return JobStartResult.ForBusy(new ModelBusyException(DescribeBusy(busyReason), _active?.Id));

        string id = $"job-{Interlocked.Increment(ref _jobCounter):x4}-{Guid.NewGuid().ToString("N")[..6]}";
        var job = new TranslationJob(id, resolved, _logger);
        job.InitializeSegments(segments);

        _active = job;

        // 事件先入历史，晚到的订阅者靠 SubscribeAsync 回放
        job.RaisePreparing();
        job.RaisePlanned();

        _ = Task.Run(() => RunAsync(job, lease, segments), CancellationToken.None);
        return JobStartResult.ForStarted(job);
    }

    /// <summary>当前任务的快照；空闲时为 null。</summary>
    /// <returns>当前任务的全量快照；空闲为 null。</returns>
    public TranslationJobSnapshot? GetCurrentSnapshot() => _active?.Snapshot();

    /// <summary>
    /// 当前任务的轻量进度快照（O(1)，不含分段明细与全文）；空闲时为 null。
    /// 供大文件任务的高频轮询使用——全量 Snapshot 对数十万段的任务每次都要复制全部状态并拼接全文。
    /// 刻意不回落到 _lastFinished：调用方（终止结算等）依赖「空闲 → null」这一语义。
    /// </summary>
    /// <returns>轻量进度快照；空闲为 null。</returns>
    public TranslationProgressSnapshot? GetCurrentProgress() => _active?.ProgressSnapshot();

    /// <summary>停止当前任务（已译部分保留）。空闲或已结束时返回 false。</summary>
    /// <returns>true 表示已发出取消；空闲或已结束时为 false。</returns>
    public bool CancelCurrent() => _active?.Cancel() ?? false;

    /// <summary>
    /// 取某次已完成任务的快照（仅匹配最近一次结束的任务，纯内存）。不匹配返回 null。
    /// </summary>
    /// <param name="jobId">要查询的任务 ID；null / 空白直接视为不匹配。</param>
    /// <returns>匹配的任务全量快照；不匹配为 null。</returns>
    public TranslationJobSnapshot? TryGetFinishedSnapshot(string? jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId)) return null;
        var finished = _lastFinished;
        return finished is not null && finished.Id == jobId ? finished.Snapshot() : null;
    }

    /// <summary>释放当前任务与最近结束任务的资源；管理器停机时由容器调用。</summary>
    public void Dispose()
    {
        _active?.Dispose();
        _active = null;
        _lastFinished?.Dispose();
        _lastFinished = null;
    }

    #endregion
}

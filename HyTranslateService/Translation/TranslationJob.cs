using System.Text;
using System.Threading.Channels;

namespace HyTranslateService.Translation;

/// <summary>任务状态。</summary>
public enum TranslationJobStatus
{
    /// <summary>已受理，正在准备模型（加载 / 换模型）。</summary>
    Preparing,

    /// <summary>正在加载 / 切换模型，尚未开始翻译。</summary>
    LoadingModel,
    /// <summary>正在逐段翻译。</summary>
    Running,
    /// <summary>全部片段翻译完成。</summary>
    Completed,
    /// <summary>被用户主动终止。</summary>
    Cancelled,
    /// <summary>执行出错，未产出完整译文。</summary>
    Failed,
}

/// <summary>推送给订阅者 / SSE / MCP 进度的任务事件（可直接序列化）。</summary>
public sealed record JobEvent
{
    /// <summary>事件类型：preparing / planned / loading / started / segment / delta / segment_done / done / cancelled / error。</summary>
    public required string Type { get; init; }

    /// <summary>所属任务 ID。</summary>
    public string? JobId { get; init; }

    /// <summary>片段序号（从 0 起）。</summary>
    public int? Index { get; init; }

    /// <summary>已完成片段数。</summary>
    public int? Completed { get; init; }

    /// <summary>总片段数。</summary>
    public int? Total { get; init; }

    /// <summary>增量 / 展示文本（delta 事件为含 Prefix 的展示文本）。</summary>
    public string? Text { get; init; }

    /// <summary>片段原文（segment / segment_done 事件携带）。</summary>
    public string? Source { get; init; }

    /// <summary>片段译文（segment_done / done / cancelled 事件携带）。</summary>
    public string? Translation { get; init; }

    /// <summary>
    /// 该片段之后需要原样回填的空白（换行 / 空行 / 空格 / Tab）。
    /// 客户端按「译文 + Separator」逐段拼接即可还原原文排版；仅 segment_done 事件携带。
    /// </summary>
    public string? Separator { get; init; }

    /// <summary>人类可读的说明（准备中 / 出错原因等）。</summary>
    public string? Message { get; init; }

    /// <summary>任务总耗时（毫秒，仅 done 事件携带）。</summary>
    public double? ElapsedMs { get; init; }
}

/// <summary>单个片段的状态。</summary>
public sealed class SegmentState
{
    /// <summary>片段序号（从 0 起）。</summary>
    public required int Index { get; init; }

    /// <summary>片段原文（不含 Prefix / Separator）。</summary>
    public required string Source { get; init; }

    /// <summary>片段之前的空白（行首缩进 / 文首空行），原样保留，不送入模型。</summary>
    public required string Prefix { get; init; }

    /// <summary>片段之后的空白（换行 / 空行 / 空格），原样保留，不送入模型。</summary>
    public required string Separator { get; init; }

    /// <summary>片段在原文中的起始偏移（字符位置）。</summary>
    public required int StartOffset { get; init; }

    /// <summary>译文累积缓冲；写入需 lock，读取走 <see cref="TranslationText"/>。</summary>
    public readonly StringBuilder Translation = new();

    /// <summary>模型是否已开始输出该片段。</summary>
    public bool Started { get; set; }

    /// <summary>该片段是否已完成。</summary>
    public bool Done { get; set; }

    /// <summary>当前译文快照；加锁读取，避免与流式写入互踩。</summary>
    public string TranslationText
    {
        get { lock (Translation) return Translation.ToString(); }
    }
}

/// <summary>片段快照（用于序列化返回）。</summary>
/// <param name="Index">片段序号（从 0 起）。</param>
/// <param name="Source">片段原文（不含空白）。</param>
/// <param name="Translation">当前译文（含 Prefix 的展示文本）。</param>
/// <param name="Prefix">片段之前的空白（行首缩进 / 文首空行）。</param>
/// <param name="Separator">片段之后的空白（换行 / 空行 / 空格）。</param>
/// <param name="Done">该片段是否已完成。</param>
public sealed record SegmentSnapshot(int Index, string Source, string Translation, string Prefix, string Separator, bool Done);

/// <summary>任务快照。</summary>
public sealed record TranslationJobSnapshot
{
    /// <summary>任务 ID。</summary>
    public required string JobId { get; init; }

    /// <summary>任务状态（<see cref="TranslationJobStatus"/> 枚举名）。</summary>
    public required string Status { get; init; }

    /// <summary>模型 ID。</summary>
    public required string Model { get; init; }

    /// <summary>模型量化等级；未指定时为 null。</summary>
    public string? ModelQuantization { get; init; }

    /// <summary>本次任务使用的推理引擎标识（<see cref="InferenceEngines"/>）。</summary>
    public required string Engine { get; init; }

    /// <summary>本次任务使用的 KV 缓存策略标识（<see cref="KvCachePolicies"/>）。</summary>
    public required string KvCache { get; init; }

    /// <summary>目标语言代码。</summary>
    public required string TargetLanguage { get; init; }

    /// <summary>源语言代码；自动识别时为 null。</summary>
    public string? SourceLanguage { get; init; }

    /// <summary>来源名称（文件名等，仅用于展示）。</summary>
    public string? SourceName { get; init; }

    /// <summary>入口种类（inline / file / mcp / mcp-file）：WebUI 刷新后据此分派接续显示。</summary>
    public required string Kind { get; init; }

    /// <summary>总片段数。</summary>
    public required int TotalSegments { get; init; }

    /// <summary>已完成片段数。</summary>
    public required int CompletedSegments { get; init; }

    /// <summary>原文总字符数（含空白）。</summary>
    public required int TotalChars { get; init; }

    /// <summary>送入模型的原文长度（不含空白）。</summary>
    public required int SourceChars { get; init; }

    /// <summary>本次翻译生成的 token 总数（所有片段之和）。</summary>
    public required int GeneratedTokens { get; init; }

    /// <summary>已拼接的全文译文（未完成时为已完成部分）。</summary>
    public required string Translation { get; init; }

    /// <summary>各片段快照。</summary>
    public required IReadOnlyList<SegmentSnapshot> Segments { get; init; }

    /// <summary>失败原因；未失败时为 null。</summary>
    public string? Error { get; init; }

    /// <summary>当前正在做什么的说明（准备模型 / 卸载模型 / 加载模型；开始翻译后为 null）。</summary>
    public string? Note { get; init; }

    /// <summary>任务创建时间。</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>开始翻译时间；尚未开始时为 null。</summary>
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>结束时间；未结束时为 null。</summary>
    public DateTimeOffset? FinishedAt { get; init; }

    /// <summary>耗时（毫秒，未结束时按当前时刻累计）。</summary>
    public required double ElapsedMs { get; init; }

    /// <summary>任务是否已进入终态。</summary>
    public required bool IsFinished { get; init; }
}

/// <summary>
/// 轻量进度快照（无分段明细、无译文内容）：供大文件任务的高频轮询（?brief=1）使用。
/// 字段与 TranslationJobSnapshot 同名子集，前端可无差别读取进度字段。
/// </summary>
public sealed record TranslationProgressSnapshot
{
    /// <summary>任务 ID。</summary>
    public required string JobId { get; init; }

    /// <summary>任务状态（<see cref="TranslationJobStatus"/> 枚举名）。</summary>
    public required string Status { get; init; }

    /// <summary>模型 ID。</summary>
    public required string Model { get; init; }

    /// <summary>本次任务使用的推理引擎标识（<see cref="InferenceEngines"/>）。</summary>
    public required string Engine { get; init; }

    /// <summary>本次任务使用的 KV 缓存策略标识（<see cref="KvCachePolicies"/>）。</summary>
    public required string KvCache { get; init; }

    /// <summary>目标语言代码。</summary>
    public required string TargetLanguage { get; init; }

    /// <summary>来源名称（文件名等，仅用于展示）。</summary>
    public string? SourceName { get; init; }

    /// <summary>入口种类（inline / file / mcp / mcp-file）：WebUI 刷新后据此分派接续显示。</summary>
    public required string Kind { get; init; }

    /// <summary>总片段数。</summary>
    public required int TotalSegments { get; init; }

    /// <summary>已完成片段数。</summary>
    public required int CompletedSegments { get; init; }

    /// <summary>耗时（毫秒，未结束时按当前时刻累计）。</summary>
    public required double ElapsedMs { get; init; }

    /// <summary>失败原因；未失败时为 null。</summary>
    public string? Error { get; init; }

    /// <summary>当前正在做什么的说明（准备模型 / 卸载模型 / 加载模型；开始翻译后为 null）。</summary>
    public string? Note { get; init; }

    /// <summary>任务是否已进入终态。</summary>
    public required bool IsFinished { get; init; }
}

/// <summary>
/// 一次翻译任务。持有一份事件历史（供晚到的订阅者回放）与一组订阅通道（供实时推送）。
/// </summary>
public sealed class TranslationJob
{
    #region 字段

    /// <summary>保护事件历史 / 订阅表 / 片段状态等可变状态的锁。</summary>
    private readonly object _sync = new();

    /// <summary>事件历史（按上限裁剪），供晚到的订阅者回放。</summary>
    private readonly List<JobEvent> _history = [];

    /// <summary>实时订阅通道；任务进入终态时统一 Complete 并清空。</summary>
    private readonly List<Channel<JobEvent>> _subscribers = [];

    /// <summary>片段状态表；下标即片段序号，初始化后不再增删。</summary>
    private readonly List<SegmentState> _segments = [];

    /// <summary>已完成片段计数；O(1) 维护，避免快照时全表扫描。</summary>
    private int _completedCount;

    /// <summary>已生成 token 累计；O(1) 维护，供完成统计与日志使用。</summary>
    private int _generatedTokens;

    /// <summary>任务级取消源；Dispose 后不可再取消。</summary>
    private readonly CancellationTokenSource _cts = new();

    /// <summary>日志器，由管理器注入。</summary>
    private readonly ILogger _logger;

    /// <summary>最近的「准备 / 卸载 / 加载模型」说明（开始翻译后清空）。</summary>
    private string? _note;

    /// <summary>事件历史上限：长文档任务事件可无限增长，而片段快照才是权威状态，历史只保留最近一段。</summary>
    private const int MaxHistoryEvents = 2000;

    #endregion

    #region 公共属性

    /// <summary>任务唯一 ID。</summary>
    public string Id { get; }

    /// <summary>归一化后的翻译请求。</summary>
    public ResolvedTranslationRequest Request { get; }

    /// <summary>任务状态。</summary>
    public TranslationJobStatus Status { get; private set; } = TranslationJobStatus.Preparing;

    /// <summary>失败原因；未失败时为 null。</summary>
    public string? Error { get; private set; }

    /// <summary>任务创建时间。</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>开始翻译时间；尚未开始时为 null。</summary>
    public DateTimeOffset? StartedAt { get; private set; }

    /// <summary>结束时间；未结束时为 null。</summary>
    public DateTimeOffset? FinishedAt { get; private set; }

    /// <summary>任务取消令牌；终止任务即触发。</summary>
    public CancellationToken CancellationToken => _cts.Token;

    /// <summary>任务是否已进入终态（完成 / 取消 / 失败）。</summary>
    public bool IsFinished => Status is TranslationJobStatus.Completed or TranslationJobStatus.Cancelled or TranslationJobStatus.Failed;

    #endregion

    /// <summary>创建任务；仅由 <see cref="TranslationJobManager"/> 在受理时调用。</summary>
    /// <param name="id">任务唯一 ID，由管理器生成。</param>
    /// <param name="request">已归一化的翻译请求。</param>
    /// <param name="logger">日志器。</param>
    internal TranslationJob(string id, ResolvedTranslationRequest request, ILogger logger)
    {
        Id = id;
        Request = request;
        _logger = logger;
        CreatedAt = DateTimeOffset.Now;
    }

    #region 私有方法

    /// <summary>
    /// 记录最近的「模型准备」说明，供快照与轮询接口展示。
    /// </summary>
    /// <param name="message">说明文本；null 表示清空。</param>
    private void SetNote(string? message)
    {
        lock (_sync) _note = message;
    }

    /// <summary>按序号取片段状态；越界返回 null。</summary>
    /// <param name="index">片段序号。</param>
    /// <returns>对应片段状态；越界为 null。</returns>
    private SegmentState? Index(int index)
    {
        lock (_sync)
        {
            return index >= 0 && index < _segments.Count ? _segments[index] : null;
        }
    }

    /// <summary>按「译文 + Separator」拼接已完成片段，还原原文排版。</summary>
    /// <returns>已拼接的全文译文；未完成片段跳过。</returns>
    private string BuildTranslation()
    {
        var sb = new StringBuilder();
        lock (_sync)
        {
            foreach (var s in _segments)
            {
                if (!s.Done) continue;
                sb.Append(s.TranslationText);
                sb.Append(s.Separator);
            }
        }
        return sb.ToString();
    }

    /// <summary>更新任务状态；进入 Running 时记录开始时间。</summary>
    /// <param name="status">目标状态。</param>
    internal void MarkStatus(TranslationJobStatus status)
    {
        lock (_sync)
        {
            Status = status;
        }
        if (status == TranslationJobStatus.Running) StartedAt = DateTimeOffset.Now;
    }

    /// <summary>用分段计划初始化片段状态表（受理时调用一次，重复调用会清空已有进度）。</summary>
    /// <param name="segments">分段计划（含原文与前后空白）。</param>
    internal void InitializeSegments(IReadOnlyList<TextSegment> segments)
    {
        lock (_sync)
        {
            _segments.Clear();
            foreach (var s in segments)
            {
                _segments.Add(new SegmentState
                {
                    Index = s.Index,
                    Source = s.Text,
                    Prefix = s.Prefix,
                    Separator = s.Separator,
                    StartOffset = s.StartOffset,
                });
            }
        }
    }

    /// <summary>事件写入历史并推给所有订阅者；终态事件会落 FinishedAt 并关闭全部订阅通道。</summary>
    /// <param name="evt">要发布的事件。</param>
    internal void Publish(JobEvent evt)
    {
        List<Channel<JobEvent>> targets;
        bool isTerminal = evt.Type is JobEventTypes.Done or JobEventTypes.Error or JobEventTypes.Cancelled;

        lock (_sync)
        {
            _history.Add(evt);
            // 只保留最近的事件，避免长文档任务无限增长（片段快照才是权威状态）
            if (_history.Count > MaxHistoryEvents)
                _history.RemoveRange(0, _history.Count - MaxHistoryEvents);

            if (isTerminal) FinishedAt ??= DateTimeOffset.Now;
            targets = _subscribers.ToList();
        }

        foreach (var channel in targets)
        {
            channel.Writer.TryWrite(evt);
        }

        if (isTerminal)
        {
            lock (_sync)
            {
                foreach (var channel in _subscribers) channel.Writer.TryComplete();
                _subscribers.Clear();
            }
        }
    }

    /// <summary>把引擎事件转换为任务事件：更新片段状态并发布给订阅者。</summary>
    /// <param name="evt">引擎事件。</param>
    internal void ApplyEngineEvent(TranslationEvent evt)
    {
        switch (evt)
        {
            case TranslationEvent.Planned planned:
                Publish(new JobEvent
                {
                    Type = JobEventTypes.Planned,
                    JobId = Id,
                    Total = planned.TotalSegments,
                    Message = $"已拆分为 {planned.TotalSegments} 段",
                });
                break;

            case TranslationEvent.SegmentStarted started:
                Index(started.Index)?.Let(s => s.Started = true);
                Publish(new JobEvent
                {
                    Type = JobEventTypes.Segment,
                    JobId = Id,
                    Index = started.Index,
                    Total = started.Total,
                    Source = started.Source,
                });
                break;

            case TranslationEvent.Delta delta:
                string? deltaShown = null;
                Index(delta.Index)?.Let(s =>
                {
                    // 译文展示文本 = Prefix（行首缩进/空行）+ 模型输出
                    deltaShown = s.Prefix + delta.Text;
                    lock (s.Translation)
                    {
                        s.Translation.Clear();
                        s.Translation.Append(deltaShown);
                    }
                });
                Publish(new JobEvent
                {
                    Type = JobEventTypes.Delta,
                    JobId = Id,
                    Index = delta.Index,
                    Total = _segments.Count,
                    Text = deltaShown,
                });
                break;

            case TranslationEvent.SegmentCompleted completed:
                string? doneShown = null;
                string? doneSeparator = null;
                Index(completed.Index)?.Let(s =>
                {
                    // 译文展示文本 = Prefix（行首缩进/空行）+ 模型输出
                    doneShown = s.Prefix + completed.Translation;
                    doneSeparator = s.Separator;
                    lock (s.Translation)
                    {
                        s.Translation.Clear();
                        s.Translation.Append(doneShown);
                    }
                    s.Done = true;
                    // O(1) 完成计数：全表扫描在数十万段的任务上是 O(n²)（每完成一段扫全部段）
                    _completedCount++;
                    _generatedTokens += completed.GeneratedTokens;
                });
                Publish(new JobEvent
                {
                    Type = JobEventTypes.SegmentDone,
                    JobId = Id,
                    Index = completed.Index,
                    Completed = _completedCount,
                    Total = _segments.Count,
                    Source = completed.Source,
                    Translation = doneShown,
                    Separator = doneSeparator,
                });
                break;

            case TranslationEvent.Completed done:
                MarkStatus(TranslationJobStatus.Completed);
                Publish(new JobEvent
                {
                    Type = JobEventTypes.Done,
                    JobId = Id,
                    Completed = done.SegmentCount,
                    Total = done.SegmentCount,
                    Translation = BuildTranslation(),
                    ElapsedMs = done.ElapsedSeconds * 1000,
                });
                break;
        }
    }

    /// <summary>标记任务失败并发布 error 事件。</summary>
    /// <param name="message">对用户展示的失败原因。</param>
    internal void Fail(string message)
    {
        Error = message;
        MarkStatus(TranslationJobStatus.Failed);
        Publish(new JobEvent { Type = JobEventTypes.Error, JobId = Id, Message = message });
    }

    /// <summary>标记任务已终止并发布 cancelled 事件（携带已译部分）。</summary>
    internal void MarkCancelled()
    {
        MarkStatus(TranslationJobStatus.Cancelled);
        Publish(new JobEvent
        {
            Type = JobEventTypes.Cancelled,
            JobId = Id,
            Message = "翻译已终止",
            Translation = BuildTranslation(),
        });
    }

    /// <summary>模型相关说明：正在卸载旧模型 / 加载新模型。</summary>
    /// <param name="message">对订阅者展示的说明文本。</param>
    internal void RaiseModelLoading(string message)
    {
        MarkStatus(TranslationJobStatus.LoadingModel);
        SetNote(message);
        Publish(new JobEvent
        {
            Type = JobEventTypes.Loading,
            JobId = Id,
            Message = message,
        });
    }

    /// <summary>任务已受理（已独占模型），正在准备模型。</summary>
    internal void RaisePreparing()
    {
        MarkStatus(TranslationJobStatus.Preparing);
        SetNote("任务已受理，正在准备模型…");
        Publish(new JobEvent
        {
            Type = JobEventTypes.Preparing,
            JobId = Id,
            Message = _note,
        });
    }

    /// <summary>已生成分段计划（此时还不知道模型是否已就绪）。</summary>
    internal void RaisePlanned() => ApplyEngineEvent(new TranslationEvent.Planned(_segments.Count, Request.Text.Length));

    /// <summary>进入翻译执行阶段并清空准备说明。</summary>
    internal void RaiseRunning()
    {
        MarkStatus(TranslationJobStatus.Running);
        SetNote(null);
        Publish(new JobEvent { Type = "started", JobId = Id, Total = _segments.Count, Message = "开始翻译" });
    }

    /// <summary>取消并释放取消源；任务对象废弃时由管理器统一调用。</summary>
    internal void Dispose()
    {
        try
        {
            _cts.Cancel();
        }
        catch
        {
        }
        _cts.Dispose();
    }

    #endregion

    #region 公共方法

    /// <summary>放弃该任务。</summary>
    /// <returns>true 表示已发出取消；任务已结束或取消源已释放时为 false。</returns>
    public bool Cancel()
    {
        if (IsFinished) return false;
        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
        return true;
    }

    /// <summary>
    /// 轻量进度快照：O(1) 取计数，不含分段明细与全文。
    /// 全量 <see cref="Snapshot"/> 对数十万段的任务每次都要复制全部状态并拼接全文，
    /// 高频轮询会把服务拖垮——轮询方请走本方法（GET /api/translation/current?brief=1）。
    /// </summary>
    /// <returns>轻量进度快照。</returns>
    public TranslationProgressSnapshot ProgressSnapshot()
    {
        lock (_sync)
        {
            DateTimeOffset? finished = FinishedAt;
            double elapsed = ((finished ?? DateTimeOffset.Now) - (StartedAt ?? CreatedAt)).TotalMilliseconds;
            return new TranslationProgressSnapshot
            {
                JobId = Id,
                Status = Status.ToString(),
                Model = Request.Model.Id,
                Engine = Request.Engine,
                KvCache = Request.KvCache,
                TargetLanguage = Request.Target.Code(),
                SourceName = Request.SourceName,
                Kind = Request.Kind,
                TotalSegments = _segments.Count,
                CompletedSegments = _completedCount,
                ElapsedMs = Math.Max(0, elapsed),
                Error = Error,
                Note = _note,
                IsFinished = IsFinished,
            };
        }
    }

    /// <summary>
    /// 订阅任务事件流：先回放历史，再持续接收实时事件，直到任务进入终态。
    /// </summary>
    /// <returns>事件流：先回放历史，再实时推送，终态后自然结束。</returns>
    public async IAsyncEnumerable<JobEvent> SubscribeAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateUnbounded<JobEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

        List<JobEvent> replay;
        bool finished;
        lock (_sync)
        {
            replay = _history.ToList();
            // 以“历史里是否已出现终态事件”为准，避免与 Publish 产生竞态而漏掉终态
            finished = replay.Count > 0 && replay[^1].Type is JobEventTypes.Done or JobEventTypes.Error or JobEventTypes.Cancelled;
            if (!finished) _subscribers.Add(channel);
        }

        foreach (var evt in replay)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return evt;
        }

        if (finished)
        {
            yield break;
        }

        try
        {
            while (await channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (channel.Reader.TryRead(out var evt))
                {
                    yield return evt;
                }
            }
        }
        finally
        {
            lock (_sync)
            {
                _subscribers.Remove(channel);
            }
        }
    }

    /// <summary>生成当前全量快照（含分段明细与全文译文）。</summary>
    /// <returns>任务全量快照。</returns>
    public TranslationJobSnapshot Snapshot()
    {
        List<SegmentSnapshot> segments;
        List<SegmentState> states;
        TranslationJobStatus status;
        string? note;
        lock (_sync)
        {
            states = _segments.ToList();
            status = Status;
            note = _note;
        }

        segments = states
            .Select(s => new SegmentSnapshot(s.Index, s.Source, s.TranslationText, s.Prefix, s.Separator, s.Done))
            .ToList();

        string translation = BuildTranslation();
        int totalChars = states.Sum(s => s.Source.Length + s.Prefix.Length + s.Separator.Length);
        DateTimeOffset? finished = FinishedAt;
        double elapsed = ((finished ?? DateTimeOffset.Now) - (StartedAt ?? CreatedAt)).TotalMilliseconds;

        return new TranslationJobSnapshot
        {
            JobId = Id,
            Status = status.ToString(),
            Model = Request.Model.Id,
            ModelQuantization = Request.Model.Quantization,
            Engine = Request.Engine,
            KvCache = Request.KvCache,
            TargetLanguage = Request.Target.Code(),
            SourceLanguage = Request.Source?.Code(),
            SourceName = Request.SourceName,
            Kind = Request.Kind,
            TotalSegments = states.Count,
            CompletedSegments = status == TranslationJobStatus.Completed ? states.Count : states.Count(s => s.Done),
            TotalChars = totalChars,
            SourceChars = Request.Text.Length,
            GeneratedTokens = _generatedTokens,
            Translation = translation,
            Segments = segments,
            Error = Error,
            Note = note,
            CreatedAt = CreatedAt,
            StartedAt = StartedAt,
            FinishedAt = finished,
            ElapsedMs = Math.Max(0, elapsed),
            IsFinished = IsFinished,
        };
    }

    #endregion
}

internal static class FunctionalExtensions
{
    /// <summary>可空对象非空时执行副作用。</summary>
    /// <typeparam name="T">元素类型。</typeparam>
    /// <param name="value">可能为 null 的对象。</param>
    /// <param name="action">非空时执行的动作。</param>
    public static void Let<T>(this T? value, Action<T> action) where T : class
    {
        if (value is not null) action(value);
    }
}

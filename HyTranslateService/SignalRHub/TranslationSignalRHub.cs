using System.Text;
using HyTranslateService.Controllers;
using HyTranslateService.Translation;
using Microsoft.AspNetCore.SignalR;

namespace HyTranslateService.SignalRHub;

/// <summary>
/// 翻译实时通道：流式翻译 + 流式进度 + 可取消。
/// 任务状态查询走 WebAPI（GET /api/translation/current）；忙时 Translate 推回 error 事件。
/// </summary>
public sealed class TranslationSignalRHub : Hub<ITranslationSignalRHub>
{
    #region 字段

    /// <summary>任务管理器：负责任务受理、事件订阅与停止。</summary>
    private readonly TranslationJobManager _jobs;

    /// <summary>应用选项：内联文本入口的大小闸取 MaxTextBytes。</summary>
    private readonly AppOptions _options;

    /// <summary>连接与任务受理的日志器。</summary>
    private readonly ILogger<TranslationSignalRHub> _logger;

    #endregion

    /// <summary>构造函数：注入实时通道所需的任务、配置与日志设施。</summary>
    /// <param name="jobs">任务管理器，负责任务受理与生命周期。</param>
    /// <param name="options">应用选项，提供内联文本大小上限。</param>
    /// <param name="logger">日志器。</param>
    public TranslationSignalRHub(TranslationJobManager jobs, AppOptions options, ILogger<TranslationSignalRHub> logger)
    {
        _jobs = jobs;
        _options = options;
        _logger = logger;
    }

    #region 公共方法

    /// <summary>新连接建立：记录连接日志后交回基类（不维护服务端订阅状态，事件只推给发起方）。</summary>
    public override async Task OnConnectedAsync()
    {
        _logger.LogDebug("SignalR 客户端已连接：{ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync().ConfigureAwait(false);
    }

    /// <summary>连接断开：仅记录日志后交回基类；正在推送的任务继续在后台执行，不因断开而终止。</summary>
    /// <param name="exception">断开原因；客户端正常关闭为 null。</param>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogDebug("SignalR 客户端已断开：{ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception).ConfigureAwait(false);
    }

    /// <summary>
    /// 发起流式翻译：把过程与结果作为 <see cref="JobEvent"/> 流推回给调用方，
    /// 直到任务完成 / 被取消 / 出错。模型正忙时推回一条 error 事件，不创建任务。
    /// </summary>
    /// <param name="body">翻译请求参数；文本为空或超过 MaxTextBytes 时推回 error 事件。</param>
    public async Task Translate(TranslationApiRequest body)
    {
        if (body is null || string.IsNullOrWhiteSpace(body.Text))
        {
            _logger.LogWarning("流式翻译被拒绝（连接 {ConnectionId}）：待翻译内容为空", Context.ConnectionId);
            await Clients.Caller.JobEvent(new JobEvent { Type = JobEventTypes.Error, Message = "待翻译内容不能为空。" })
                .ConfigureAwait(false);
            return;
        }

        // 内联文本入口的大小闸（接口层收口）：核心层不校验大小；文件入口的大小上限由 AppOptions 定义
        int textBytes = Encoding.UTF8.GetByteCount(body.Text);
        if (textBytes > _options.MaxTextBytes)
        {
            _logger.LogWarning("流式翻译被拒绝（连接 {ConnectionId}）：文本 {Actual} 字节超过上限 {Limit}",
                Context.ConnectionId, textBytes, _options.MaxTextBytes);
            await Clients.Caller.JobEvent(new JobEvent
            {
                Type = JobEventTypes.Error,
                Message = $"文本过大（{textBytes / 1024.0:F1} KB），单次上限为 {_options.MaxTextBytes / 1024} KB，请拆分后分批翻译。",
            }).ConfigureAwait(false);
            return;
        }

        var outcome = _jobs.TryStart(body.ToTranslationRequest() with { Kind = TranslationJobKinds.Inline });
        if (outcome.Busy is not null)
        {
            _logger.LogWarning("流式翻译被拒绝（连接 {ConnectionId}，模型正忙）：{Reason}", Context.ConnectionId, outcome.Busy.Message);
            await Clients.Caller.JobEvent(new JobEvent { Type = JobEventTypes.Error, Message = outcome.Busy.Message })
                .ConfigureAwait(false);
            return;
        }
        if (outcome.Invalid is not null)
        {
            _logger.LogWarning("流式翻译请求无效（连接 {ConnectionId}）：{Reason}", Context.ConnectionId, outcome.Invalid.Message);
            await Clients.Caller.JobEvent(new JobEvent { Type = JobEventTypes.Error, Message = outcome.Invalid.Message })
                .ConfigureAwait(false);
            return;
        }
        var job = outcome.Job!;

        var planned = job.Snapshot();
        _logger.LogInformation("流式翻译已受理 {JobId}（连接 {ConnectionId}）：{Chars} 字符 → {Target}，{Segments} 段（模型 {Model}）",
            job.Id, Context.ConnectionId, body.Text.Length, planned.TargetLanguage, planned.TotalSegments, planned.Model);

        try
        {
            // 纯事件流：首个即 preparing（历史回放），随后流式增量，终态 done / cancelled / error。
            // 客户端断开时只结束推送，不自动终止任务（停止需显式调用 Cancel 或 REST cancel）。
            await foreach (var evt in job.SubscribeAsync(Context.ConnectionAborted).ConfigureAwait(false))
            {
                await Clients.Caller.JobEvent(evt).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // 客户端已断开：结束推送，任务继续在后台执行直至完成
            _logger.LogDebug("订阅任务的客户端已断开，停止推送事件");
        }
    }

    /// <summary>停止当前任务（已译部分保留）。</summary>
    public Task Cancel()
    {
        bool cancelled = _jobs.CancelCurrent();
        if (cancelled) _logger.LogInformation("收到停止请求（SignalR，连接 {ConnectionId}）：已请求终止当前任务 {JobId}",
            Context.ConnectionId, _jobs.CurrentJobId ?? "(已结束)");
        else _logger.LogInformation("收到停止请求（SignalR，连接 {ConnectionId}）：当前没有可停止的任务", Context.ConnectionId);
        return Task.CompletedTask;
    }

    #endregion
}

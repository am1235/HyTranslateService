using HyTranslateService.Translation;

namespace HyTranslateService.SignalRHub;

/// <summary>
/// SignalR 服务端 → 客户端的消息契约（Strongly-typed Hub）。
/// 实时通道只推送发起翻译那一方的事件流；任务状态查询走 WebAPI。
/// </summary>
public interface ITranslationSignalRHub
{
    /// <summary>
    /// 客户端接收翻译过程的流式事件：
    /// preparing / planned / loading / started / segment / delta / segment_done / done / cancelled / error。
    /// </summary>
    /// <remarks>服务端只推送给发起翻译的连接；首个事件即 preparing（含历史回放），终态为 done / cancelled / error。</remarks>
    /// <param name="evt">任务事件对象，含类型、序号与载荷。</param>
    /// <returns>表示异步操作的任务。</returns>
    [SignalRMethod(summary: "客户端接收翻译流式事件",
        description: "任务产生事件后由服务器推送给发起方客户端；首个事件为 preparing（历史回放），终态为 done / cancelled / error",
        autoDiscover: AutoDiscover.Params)]
    Task JobEvent([SignalRParam(description: "任务事件对象，含类型、序号与载荷")] JobEvent evt);
}

/// <summary>SignalR 端点路径。</summary>
public static class TranslationHubRoutes
{
    /// <summary>翻译通道的 SignalR 端点路径。</summary>
    public const string Path = "/signalr/translation";
}

namespace HyTranslateService.Translation;

/// <summary>
/// 翻译事件类型标识，与 <c>JobEvent.Type</c> 字段一一对应。
/// 集中定义以避免各入口（控制器 / SignalR / MCP / 任务管理器）散落硬编码魔法字符串——
/// 任一处拼错都会静默断流，常量化后拼写错误会在编译期暴露。
/// </summary>
public static class JobEventTypes
{
    /// <summary>任务已受理，正在准备模型（首个事件）。</summary>
    public const string Preparing = "preparing";

    /// <summary>分段计划已生成（含总段数）。</summary>
    public const string Planned = "planned";

    /// <summary>正在加载模型。</summary>
    public const string Loading = "loading";

    /// <summary>开始翻译某一段。</summary>
    public const string Segment = "segment";

    /// <summary>某一段的流式增量。</summary>
    public const string Delta = "delta";

    /// <summary>某一段翻译完成。</summary>
    public const string SegmentDone = "segment_done";

    /// <summary>整篇翻译完成。</summary>
    public const string Done = "done";

    /// <summary>任务被取消。</summary>
    public const string Cancelled = "cancelled";

    /// <summary>出错 / 请求被拒（如空文本、模型正忙、参数无效）。</summary>
    public const string Error = "error";
}

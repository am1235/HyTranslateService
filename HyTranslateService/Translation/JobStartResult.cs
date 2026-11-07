namespace HyTranslateService.Translation;

/// <summary>
/// <see cref="TranslationJobManager.TryStart"/> 的中性结果。
/// 把「模型正忙 / 参数无效 / 已受理」三种语义从各入口的响应形态
/// （HTTP 409 / SignalR error 事件 / McpException）中抽离，由各调用方自行映射回自己的对外契约。
/// 行为保持：与原先 try/catch <see cref="ModelBusyException"/> / <see cref="ArgumentException"/> / <see cref="InvalidOperationException"/> 完全一致——三类分支一一对应。
/// </summary>
public sealed class JobStartResult
{
    #region 公共属性

    /// <summary>已受理的任务（仅当 <see cref="IsStarted"/> 为 true）。</summary>
    public TranslationJob? Job { get; }

    /// <summary>模型正忙时的拒绝原因（仅当 <see cref="Busy"/> 非 null）。</summary>
    public ModelBusyException? Busy { get; }

    /// <summary>参数无效 / 无法归一化时的异常（仅当 <see cref="Invalid"/> 非 null）。</summary>
    public Exception? Invalid { get; }

    /// <summary>是否成功受理（<see cref="Busy"/> 与 <see cref="Invalid"/> 均为 null）。</summary>
    public bool IsStarted => Job is not null;

    #endregion

    /// <summary>私有构造：只能通过三个工厂方法创建，保证三态互斥。</summary>
    /// <param name="job">已受理的任务；未受理时为 null。</param>
    /// <param name="busy">模型正忙的拒绝原因；不适用时为 null。</param>
    /// <param name="invalid">参数无效的异常；不适用时为 null。</param>
    private JobStartResult(TranslationJob? job, ModelBusyException? busy, Exception? invalid)
    {
        Job = job;
        Busy = busy;
        Invalid = invalid;
    }

    #region 公共方法

    /// <summary>构造「已受理」结果。</summary>
    /// <param name="job">已受理的翻译任务。</param>
    /// <returns>IsStarted 为 true 的结果。</returns>
    public static JobStartResult ForStarted(TranslationJob job) => new(job, null, null);

    /// <summary>构造「模型正忙」结果。</summary>
    /// <param name="ex">捕获到的 <see cref="ModelBusyException"/>，携带占用信息。</param>
    /// <returns>Busy 非 null 的结果。</returns>
    public static JobStartResult ForBusy(ModelBusyException ex) => new(null, ex, null);

    /// <summary>构造「参数无效」结果。</summary>
    /// <param name="ex">参数校验 / 归一化抛出的异常。</param>
    /// <returns>Invalid 非 null 的结果。</returns>
    public static JobStartResult ForInvalid(Exception ex) => new(null, null, ex);

    #endregion
}

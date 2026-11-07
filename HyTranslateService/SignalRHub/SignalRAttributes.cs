namespace HyTranslateService.SignalRHub;

/// <summary>客户端方法的自动发现策略。</summary>
public enum AutoDiscover
{
    /// <summary>不自动发现，客户端需手动注册监听。</summary>
    None = 0,

    /// <summary>按方法参数自动发现并生成客户端注册代码。</summary>
    Params = 1,
}

/// <summary>
/// 标记客户端接口方法为 SignalR 推送方法，并携带供文档与代码生成使用的描述信息。
/// 各属性必须与方法的 XML 注释语义一致——一处修改、两处同步。
/// </summary>
/// <param name="summary">方法一句话摘要，与方法级 <c>&lt;summary&gt;</c> 语义一致。</param>
/// <param name="description">推送触发时机的完整描述，与方法级 <c>&lt;remarks&gt;</c> 语义一致。</param>
/// <param name="autoDiscover">客户端方法自动发现策略。</param>
[AttributeUsage(AttributeTargets.Method)]
public sealed class SignalRMethodAttribute(
    string summary,
    string description,
    AutoDiscover autoDiscover = AutoDiscover.Params) : Attribute
{
    /// <summary>方法一句话摘要。</summary>
    public string Summary { get; } = summary;

    /// <summary>推送触发时机的完整描述。</summary>
    public string Description { get; } = description;

    /// <summary>客户端方法自动发现策略。</summary>
    public AutoDiscover AutoDiscover { get; } = autoDiscover;
}

/// <summary>描述推送方法参数的客户端语义，与该参数的 <c>&lt;param&gt;</c> 注释保持一致。</summary>
/// <param name="description">参数说明。</param>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class SignalRParamAttribute(string description) : Attribute
{
    /// <summary>参数说明。</summary>
    public string Description { get; } = description;
}

/// <summary>标记方法、参数或返回值不参与客户端自动发现（生成侧隐藏）。</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Parameter | AttributeTargets.ReturnValue)]
public sealed class SignalRHiddenAttribute : Attribute;

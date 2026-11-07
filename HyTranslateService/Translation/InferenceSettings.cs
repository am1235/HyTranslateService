using System.Runtime.InteropServices;
using Sdcb.HyMT2Sharp.Backends;
using Sdcb.HyMT2Sharp.Model;

namespace HyTranslateService.Translation;

/// <summary>
/// 推理引擎选择：把界面上的「自动 / GPU / CPU」映射到内核的后端工厂（<see cref="BackendFactory"/>）。
/// 引擎在模型加载时绑定，换引擎等同换模型——必须先卸载当前模型再加载。
/// </summary>
public static class InferenceEngines
{
    #region 常量

    /// <summary>自动：由内核按平台探测可用 GPU（macOS 先 Metal、再 Vulkan），都不可用时回落 CPU。</summary>
    public const string Auto = "auto";

    /// <summary>强制 GPU：不可用时直接报错，不静默回落到 CPU（否则用户以为在用显卡）。</summary>
    public const string Gpu = "gpu";

    /// <summary>强制 CPU：纯 SIMD 内核，行为与 1.x 版本一致。</summary>
    public const string Cpu = "cpu";

    #endregion

    #region 公共属性

    /// <summary>全部可选引擎标识（顺序即界面下拉顺序）。</summary>
    public static IReadOnlyList<string> All { get; } = [Auto, Gpu, Cpu];

    #endregion

    #region 公共方法

    /// <summary>把宽松的引擎描述归一化为标准标识。</summary>
    /// <param name="value">原始描述，允许大小写与首尾空白。</param>
    /// <param name="fallback">无法识别（含空值）时采用的默认标识。</param>
    /// <returns>归一化后的引擎标识（<see cref="Auto"/>/<see cref="Gpu"/>/<see cref="Cpu"/> 之一）。</returns>
    public static string Normalize(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        string v = value.Trim().ToLowerInvariant();
        return v switch
        {
            Auto => Auto,
            Gpu or "vulkan" or "metal" => Gpu,
            Cpu => Cpu,
            _ => fallback,
        };
    }

    /// <summary>
    /// 按引擎标识创建内核计算后端。
    /// </summary>
    /// <param name="engine">归一化后的引擎标识。</param>
    /// <returns>GPU 后端实例；CPU（含自动回落 CPU）时为 null。实例归模型所有，由模型释放。</returns>
    /// <exception cref="InvalidOperationException">强制 GPU 但后端无法创建（无驱动 / 无 Vulkan 运行时）时抛出。</exception>
    public static IComputeBackend? CreateBackend(string engine)
    {
        string key = engine switch
        {
            Cpu => "cpu",
            Gpu => RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "metal" : "vulkan",
            _ => "auto",
        };

        try
        {
            return BackendFactory.Create(key);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // 自动模式内部已做过探测，走到这里说明是显式 GPU 请求：直接失败比静默降级更诚实
            throw new InvalidOperationException(
                $"GPU 推理引擎不可用（{key}）：{ex.Message}。请改用 CPU 引擎，或确认显卡驱动与 Vulkan/Metal 运行时已就绪。", ex);
        }
    }

    #endregion
}

/// <summary>
/// KV 缓存策略：控制**一次任务结束后**模型内部 KV 缓冲的去留。
/// 分段之间引擎仍会重置缓存（保证各段互不污染），因此它不改变译文，只影响内存占用与下次加载的预热成本。
/// </summary>
public static class KvCachePolicies
{
    #region 常量

    /// <summary>不缓存：任务结束即释放 KV 缓冲，内存占用最低，下次任务需要重新分配。</summary>
    public const string None = "none";

    /// <summary>保留上次：任务结束保留缓冲供下一个任务复用，换模型前一直占用。</summary>
    public const string Memory = "memory";

    #endregion

    #region 公共属性

    /// <summary>全部可选策略标识（顺序即界面下拉顺序）。</summary>
    public static IReadOnlyList<string> All { get; } = [None, Memory];

    #endregion

    #region 公共方法

    /// <summary>把宽松的策略描述归一化为标准标识。</summary>
    /// <param name="value">原始描述，允许大小写与首尾空白。</param>
    /// <param name="fallback">无法识别（含空值）时采用的默认标识。</param>
    /// <returns>归一化后的策略标识（<see cref="None"/>/<see cref="Memory"/> 之一）。</returns>
    public static string Normalize(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        string v = value.Trim().ToLowerInvariant();
        return v switch
        {
            None or "off" => None,
            Memory or "on" => Memory,
            _ => fallback,
        };
    }

    /// <summary>按策略标识构造内核的缓存配置。</summary>
    /// <param name="policy">归一化后的策略标识。</param>
    /// <returns>交给 <c>HunyuanDenseModel</c> 的缓存配置。</returns>
    public static KvCacheConfig ToConfig(string policy)
        => policy == None ? KvCacheConfig.None : KvCacheConfig.Memory;

    /// <summary>把策略标识转成可直接展示的中文说明。</summary>
    /// <param name="policy">归一化后的策略标识。</param>
    /// <returns>展示用文本，如「保留上次」。</returns>
    public static string Describe(string policy) => policy == None ? "不缓存" : "保留上次";

    /// <summary>
    /// 任务结束时是否要调用内核的 <c>EndRequest()</c>。
    /// 不缓存策略必须调用（否则缓冲不释放，选项形同虚设）；异常结束的任务也一律收尾，
    /// 避免把写了一半的缓存留给下一个任务。
    /// </summary>
    /// <param name="policy">归一化后的策略标识。</param>
    /// <param name="completed">任务是否正常完成（非失败、非终止）。</param>
    /// <returns>需要调用 <c>EndRequest()</c> 时为 true。</returns>
    public static bool ShouldEndRequest(string policy, bool completed)
        => policy == None || !completed;

    #endregion
}

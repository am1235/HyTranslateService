using HyTranslateService.Translation;

namespace HyTranslateService;

/// <summary>
/// 服务运行期配置。每项都有内置默认值，只能通过命令行参数或 HYTRANSLATE_* 环境变量覆盖
/// ——服务端不读写任何配置文件。界面语言由前端保存在浏览器；内存中只载入一个模型（见 ModelHost）。
/// </summary>
public sealed class AppOptions
{
    /// <summary>模型目录（默认相对当前工作目录的 ./models）。</summary>
    public string ModelsDirectory { get; set; } = "models";

    /// <summary>Web/HTTP 监听地址。</summary>
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>Web/HTTP 监听端口。</summary>
    public int Port { get; set; } = 8086;

    /// <summary>推理线程数，0 = 按 CPU 拓扑自动绑定物理核心。</summary>
    public int Threads { get; set; }

    /// <summary>
    /// 默认推理引擎：auto（有可用 GPU 就用，加载失败自动回落 CPU）/ gpu（强制显卡，不可用即报错）/ cpu（强制纯 CPU）。
    /// 默认 auto：GPU 已实测可用（打补丁后见 README），享受加速的同时无 GPU 的机器自动回落。
    /// </summary>
    public string Engine { get; set; } = InferenceEngines.Auto;

    /// <summary>
    /// 默认 KV 缓存策略：memory（任务结束后保留缓冲，默认）/ none（结束即释放）。
    /// 只影响内存占用与下次任务的预热成本，不改变译文。
    /// </summary>
    public string KvCache { get; set; } = KvCachePolicies.Memory;

    /// <summary>启动时预加载的模型 ID（可选）。</summary>
    public string? PreloadModel { get; set; }

    /// <summary>
    /// 模型空闲自动卸载分钟数，默认 5：超过该时长没有新的翻译请求且无任务在跑，
    /// 就卸载常驻模型释放内存，下一个请求再按需重新加载。0 表示禁用自动卸载。
    /// </summary>
    public int ModelIdleUnloadMinutes { get; set; } = 5;

    /// <summary>未显式指定模型时使用的默认模型 ID（可选，缺省为推荐模型）。</summary>
    public string? DefaultModel { get; set; }

    /// <summary>默认目标语言。</summary>
    public string DefaultTargetLanguage { get; set; } = "Chinese";

    /// <summary>默认源语言；为空表示自动识别（由模型自行判断）。</summary>
    public string? DefaultSourceLanguage { get; set; }

    /// <summary>默认单段最大字符数。</summary>
    public int DefaultSegmentChars { get; set; } = 480;

    /// <summary>单段允许的最大字符数上限（防止用户填入超大值撑爆上下文）。</summary>
    public int MaxSegmentChars { get; set; } = 6000;

    /// <summary>
    /// 单次内联文本翻译允许的最大文本大小（UTF-8 字节数），默认 128KB。
    /// 内联文本大小的唯一权威定义，运行时只通过 --max-text-bytes / HYTRANSLATE_MAX_TEXT_BYTES 覆盖。
    /// </summary>
    public int MaxTextBytes { get; set; } = 128 * 1024;

    /// <summary>
    /// 单文件上传允许的最大大小（UTF-8 文本字节数），默认 32MB。
    /// WebUI 大文件上传与 MCP 本地文件翻译共用此上限；上传大小的唯一权威定义，
    /// 运行时只通过 --max-file-bytes / HYTRANSLATE_MAX_FILE_BYTES 覆盖。
    /// </summary>
    public long MaxFileUploadBytes { get; set; } = 32L * 1024 * 1024;

    /// <summary>是否启用基于本地文件路径的翻译工具（MCP）。</summary>
    public bool EnableFileTools { get; set; } = true;

    /// <summary>Web 启动后是否自动打开浏览器。</summary>
    public bool OpenBrowser { get; set; } = false;

    /// <summary>日志级别（Trace/Debug/Information/Warning/Error）。</summary>
    public string LogLevel { get; set; } = "Information";

    /// <summary>实际生效的 Web 访问地址（未显式指定 host/port 时可能来自 launchSettings / ASPNETCORE_URLS）。</summary>
    public string WebUrl => $"http://{Host}:{Port}";

    /// <summary>MCP（Streamable HTTP）端点地址。</summary>
    public string McpEndpoint => $"{WebUrl}/mcp";

    /// <summary>是否由用户显式指定了 --host / --port（否则尊重 ASPNETCORE_URLS / launchSettings）。</summary>
    public bool AddressExplicitlySet { get; set; }
}

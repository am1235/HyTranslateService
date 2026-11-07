using System.Text;
using HyTranslateService.Translation;

namespace HyTranslateService;

/// <summary>
/// 轻量命令行/环境变量解析器。
/// 支持 --key value、--key=value、--flag 以及 HYTRANSLATE_KEY 环境变量。
/// 所有运行期参数都只来自这两处，服务端不读取也不写入任何配置文件。
/// </summary>
public static class AppOptionsParser
{
    /// <summary>帮助文本：--help 输出的唯一来源，也是全部命令行参数的权威说明。</summary>
    private const string Help = """
        HyTranslateService —— 基于 Hy-MT2 (HyMT2Sharp) 的本地翻译服务（WebUI + MCP）

        用法:
          HyTranslateService [选项]

        服务同时提供：
          · WebUI 界面        http://<host>:<port>/
          · WebAPI（控制器）  http://<host>:<port>/api/**
          · SignalR 实时通道  http://<host>:<port>/signalr/translation
          · MCP 端点          http://<host>:<port>/mcp  (Streamable HTTP)

        常用选项:
          --models-dir <路径>      模型目录，默认 ./models（启动时自动扫描其中的 *.gguf）
          --host <地址>            监听地址，默认 127.0.0.1
          --port <端口>            监听端口，默认 8086
          --threads <数量>         推理线程数，默认 0（按 CPU 拓扑绑定物理核心）
          --engine <引擎>          推理引擎：auto / gpu / cpu，默认 auto（有 GPU 就用，失败回落 CPU）
          --kv-cache <策略>        KV 缓存策略：memory（结束保留，默认）/ none（结束释放）
          --preload <模型ID>       启动时预加载指定模型
          --model-idle-unload <分>  模型空闲自动卸载分钟数，默认 5（超时无请求即卸载释放内存；0 = 禁用）
          --default-model <模型ID> 未指定模型时使用的默认模型
          --default-target <语言>  默认目标语言，默认 Chinese
          --default-source <语言>  默认源语言，留空表示自动识别
          --segment-chars <n>      默认单段最大字符数，默认 480
          --max-text-bytes <字节>   单次内联文本翻译的字节上限，默认 131072（128KB）
          --max-file-bytes <字节>   单文件上传（WebUI / MCP）的字节上限，默认 33554432（32MB）
          --no-file-tools          禁用按本地文件路径翻译的 MCP 工具
          --browser                启动后自动打开浏览器
          --log-level <级别>       Trace/Debug/Information/Warning/Error
          -h, --help               显示本帮助

        说明:
          服务端不读写任何配置文件，以上参数即为唯一配置入口。
          固定规则（不可配置）：内存中同一时刻只常驻一个模型，且只执行一个翻译任务。
          请求另一个模型时会先卸载当前模型、再加载新的；
          已有任务在跑时，新任务会被直接拒绝（HTTP 409「模型正忙」，不排队）——
          先用当前任务接口（GET /api/translation/current，或 MCP 的 get_current_translation）查询，
          需要时用 POST /api/translation/cancel（或 MCP 的 cancel_current_translation）停止，然后再提交。
          界面语言不在后端配置：它只由用户在 WebUI 上选择，并保存在该浏览器中；
          可选语言在启动时扫描 ./wwwroot/js/i18n/lang/ 目录自动得到。

        示例:
          HyTranslateService --port 8086
          HyTranslateService --models-dir D:\models --default-target Japanese
        """;

    #region 私有方法

    /// <summary>把 HYTRANSLATE_* 环境变量叠加到配置上；只覆盖实际存在的键，其余保留默认值。</summary>
    /// <param name="options">待填充的配置对象</param>
    private static void ApplyEnv(AppOptions options)
    {
        string? Get(string name) => Environment.GetEnvironmentVariable("HYTRANSLATE_" + name);

        options.ModelsDirectory = Get("MODELS_DIR") ?? options.ModelsDirectory;
        if (!string.IsNullOrWhiteSpace(Get("HOST")))
        {
            options.Host = Get("HOST")!;
            options.AddressExplicitlySet = true;
        }
        if (int.TryParse(Get("PORT"), out int port))
        {
            options.Port = port;
            options.AddressExplicitlySet = true;
        }
        if (int.TryParse(Get("THREADS"), out int threads)) options.Threads = threads;
        options.Engine = InferenceEngines.Normalize(Get("ENGINE"), options.Engine);
        options.KvCache = KvCachePolicies.Normalize(Get("KV_CACHE"), options.KvCache);
        options.PreloadModel = Get("PRELOAD") ?? options.PreloadModel;
        if (int.TryParse(Get("MODEL_IDLE_UNLOAD"), out int idleMinutes))
            options.ModelIdleUnloadMinutes = Math.Max(0, idleMinutes);
        options.DefaultModel = Get("DEFAULT_MODEL") ?? options.DefaultModel;
        options.DefaultTargetLanguage = Get("DEFAULT_TARGET") ?? options.DefaultTargetLanguage;
        options.DefaultSourceLanguage = Get("DEFAULT_SOURCE") ?? options.DefaultSourceLanguage;
        if (int.TryParse(Get("SEGMENT_CHARS"), out int segChars)) options.DefaultSegmentChars = Math.Max(80, segChars);
        if (long.TryParse(Get("MAX_TEXT_BYTES"), out long maxText)) options.MaxTextBytes = Math.Max(1024, (int)maxText);
        if (long.TryParse(Get("MAX_FILE_BYTES"), out long maxFile)) options.MaxFileUploadBytes = Math.Max(1024L * 1024, maxFile);
        options.LogLevel = Get("LOG_LEVEL") ?? options.LogLevel;
    }

    /// <summary>取出选项值：优先 --key=value 内联值，否则取紧随其后的非 -- 开头参数。</summary>
    /// <param name="key">参数名，仅用于缺值时的错误提示</param>
    /// <param name="inlineValue">等号内联值；null 表示未内联</param>
    /// <param name="args">完整参数数组</param>
    /// <param name="index">当前下标；吞掉后续取值时原地前移</param>
    /// <returns>选项值</returns>
    private static string Take(string key, string? inlineValue, string[] args, ref int index)
    {
        if (inlineValue is not null) return inlineValue;
        if (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            index++;
            return args[index];
        }
        throw new ArgumentException($"参数 --{key} 缺少取值。");
    }

    /// <summary>取出整型选项值。</summary>
    /// <param name="key">参数名，仅用于错误提示</param>
    /// <param name="inlineValue">等号内联值；null 表示未内联</param>
    /// <param name="args">完整参数数组</param>
    /// <param name="index">当前下标；吞掉后续取值时原地前移</param>
    /// <param name="fallback">回退缺省值；当前实现缺值或非法时直接抛错，不使用该值</param>
    /// <returns>解析出的整数</returns>
    private static int TakeInt(string key, string? inlineValue, string[] args, ref int index, int fallback)
    {
        string value = Take(key, inlineValue, args, ref index);
        if (int.TryParse(value, out int parsed)) return parsed;
        throw new ArgumentException($"参数 --{key} 需要整数，实际为 \"{value}\"。");
    }

    /// <summary>取出长整型选项值。</summary>
    /// <param name="key">参数名，仅用于错误提示</param>
    /// <param name="inlineValue">等号内联值；null 表示未内联</param>
    /// <param name="args">完整参数数组</param>
    /// <param name="index">当前下标；吞掉后续取值时原地前移</param>
    /// <param name="fallback">回退缺省值；当前实现缺值或非法时直接抛错，不使用该值</param>
    /// <returns>解析出的长整数</returns>
    private static long TakeLong(string key, string? inlineValue, string[] args, ref int index, long fallback)
    {
        string value = Take(key, inlineValue, args, ref index);
        if (long.TryParse(value, out long parsed)) return parsed;
        throw new ArgumentException($"参数 --{key} 需要整数，实际为 \"{value}\"。");
    }

    /// <summary>宽松解析布尔开关值。</summary>
    /// <param name="value">待解析文本，接受 1/0、true/false、yes/no、on/off、y/n 等写法</param>
    /// <param name="fallback">空白或无法识别时的回退值</param>
    /// <returns>解析结果</returns>
    private static bool ParseBool(string value, bool fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        return value.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "on" or "y" => true,
            "0" or "false" or "no" or "off" or "n" => false,
            _ => fallback,
        };
    }

    #endregion

    #region 公共方法

    /// <summary>解析启动参数得到运行期配置；同名选项以命令行为准，环境变量仅作基线。</summary>
    /// <param name="args">命令行参数；支持 --key value、--key=value、开关与 -h/--help，位置参数一律忽略</param>
    /// <returns>解析结果：配置对象、是否要求显示帮助与帮助文本</returns>
    public static ParseResult Parse(string[] args)
    {
        var options = new AppOptions();
        bool showHelp = false;

        // 先应用环境变量，再应用命令行（命令行优先）
        ApplyEnv(options);

        for (int i = 0; i < args.Length; i++)
        {
            string raw = args[i];
            if (raw.Length == 0) continue;

            string key = raw;
            string? inlineValue = null;
            if (raw.StartsWith("--", StringComparison.Ordinal))
            {
                key = raw[2..];
                int eq = key.IndexOf('=');
                if (eq >= 0)
                {
                    inlineValue = key[(eq + 1)..];
                    key = key[..eq];
                }
            }
            else if (raw is "-h" or "-?")
            {
                showHelp = true;
                continue;
            }
            else
            {
                continue; // 忽略位置参数
            }

            switch (key.ToLowerInvariant())
            {
                case "help":
                    showHelp = true;
                    break;
                case "models-dir":
                case "models":
                    options.ModelsDirectory = Take(key, inlineValue, args, ref i);
                    break;
                case "host":
                case "listen":
                    options.Host = Take(key, inlineValue, args, ref i);
                    options.AddressExplicitlySet = true;
                    break;
                case "port":
                    options.Port = TakeInt(key, inlineValue, args, ref i, options.Port);
                    options.AddressExplicitlySet = true;
                    break;
                case "threads":
                    options.Threads = TakeInt(key, inlineValue, args, ref i, options.Threads);
                    break;
                case "engine":
                    options.Engine = InferenceEngines.Normalize(Take(key, inlineValue, args, ref i), options.Engine);
                    break;
                case "kv-cache":
                    options.KvCache = KvCachePolicies.Normalize(Take(key, inlineValue, args, ref i), options.KvCache);
                    break;
                case "preload":
                    options.PreloadModel = Take(key, inlineValue, args, ref i);
                    break;
                case "model-idle-unload":
                    // 负值视为 0（禁用自动卸载）
                    options.ModelIdleUnloadMinutes = Math.Max(0, TakeInt(key, inlineValue, args, ref i, options.ModelIdleUnloadMinutes));
                    break;
                case "default-model":
                    options.DefaultModel = Take(key, inlineValue, args, ref i);
                    break;
                case "default-target":
                case "target":
                    options.DefaultTargetLanguage = Take(key, inlineValue, args, ref i);
                    break;
                case "default-source":
                case "source":
                    options.DefaultSourceLanguage = Take(key, inlineValue, args, ref i);
                    break;
                case "segment-chars":
                    options.DefaultSegmentChars = Math.Max(80, TakeInt(key, inlineValue, args, ref i, options.DefaultSegmentChars));
                    break;
                case "max-text-bytes":
                    options.MaxTextBytes = Math.Max(1024, TakeInt(key, inlineValue, args, ref i, options.MaxTextBytes));
                    break;
                case "max-file-bytes":
                    options.MaxFileUploadBytes = Math.Max(1024L * 1024, TakeLong(key, inlineValue, args, ref i, options.MaxFileUploadBytes));
                    break;
                case "file-tools":
                    options.EnableFileTools = ParseBool(Take(key, inlineValue, args, ref i), true);
                    break;
                case "no-file-tools":
                    options.EnableFileTools = false;
                    break;
                case "browser":
                    options.OpenBrowser = true;
                    break;
                case "log-level":
                    options.LogLevel = Take(key, inlineValue, args, ref i);
                    break;
                default:
                    // 未知参数忽略，避免 IDE 附加参数导致启动失败
                    break;
            }
        }

        return new ParseResult(options, showHelp, Help);
    }

    /// <summary>构造用于输出到控制台的启动信息。</summary>
    /// <param name="options">启动配置，未拿到实际监听地址时回退其 WebUrl</param>
    /// <param name="resolvedModelsDir">已解析出的模型目录路径</param>
    /// <param name="modelCount">扫描到的可用模型数量</param>
    /// <param name="actualUrls">Kestrel 实际监听地址（分号分隔）；null 时回退 options.WebUrl</param>
    /// <returns>多行启动信息文本，供控制台直接输出</returns>
    public static string DescribeStartup(AppOptions options, string resolvedModelsDir, int modelCount, string? actualUrls = null)
    {
        string baseUrl = FirstHttpUrl(actualUrls) ?? options.WebUrl;
        var sb = new StringBuilder();
        sb.AppendLine("  HyTranslateService · 本地 Hy-MT2 翻译服务");
        sb.AppendLine($"  模型目录   : {resolvedModelsDir}");
        sb.AppendLine($"  可用模型   : {modelCount} 个");
        sb.AppendLine($"  WebUI      : {baseUrl}/");
        sb.AppendLine($"  WebAPI     : {baseUrl}/api");
        sb.AppendLine($"  SignalR    : {baseUrl}/signalr/translation");
        sb.AppendLine($"  MCP 端点   : {baseUrl}/mcp  (Streamable HTTP)");
        return sb.ToString();
    }

    /// <summary>从 Kestrel 实际监听地址里挑一个可用于访问的 HTTP 地址。</summary>
    /// <param name="urls">分号分隔的监听地址列表；null 或空白视为无</param>
    /// <returns>第一个 http:// 地址（去除尾部斜杠）；无 http 地址时回退第一个地址；无任何地址返回 null</returns>
    public static string? FirstHttpUrl(string? urls)
    {
        if (string.IsNullOrWhiteSpace(urls)) return null;
        foreach (string raw in urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) return raw.TrimEnd('/');
        }
        return urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault()?.TrimEnd('/');
    }

    #endregion

    /// <summary>Parse 的返回载体：解析出的配置与帮助请求。</summary>
    /// <param name="Options">解析得到的运行期配置</param>
    /// <param name="ShowHelp">为 true 时程序只打印 HelpText 并退出，不启动服务</param>
    /// <param name="HelpText">帮助文本，内容来自内置常量</param>
    public sealed record ParseResult(AppOptions Options, bool ShowHelp, string HelpText);
}

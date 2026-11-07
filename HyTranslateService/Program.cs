using System.Diagnostics;
using HyTranslateService;
using HyTranslateService.Services;
using HyTranslateService.SignalRHub;
using HyTranslateService.Translation;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

var parsed = AppOptionsParser.Parse(args);
if (parsed.ShowHelp)
{
    Console.WriteLine(parsed.HelpText);
    return 0;
}

AppOptions options = parsed.Options;

try
{
    return await RunWebAsync(options);
}
catch (Exception ex)
{
    Console.Error.WriteLine("启动失败：" + ex.Message);
    if (ex.InnerException is not null) Console.Error.WriteLine("  " + ex.InnerException.Message);
    return 1;
}

/// <summary>Web 服务主流程：WebUI + WebAPI(Controllers) + SignalR + Streamable HTTP MCP。</summary>
/// <param name="options">已解析的命令行 / 环境变量运行参数。</param>
/// <returns>进程退出码：0 为启动成功，非 0 为启动失败。</returns>
static async Task<int> RunWebAsync(AppOptions options)
{
    var builder = WebApplication.CreateBuilder();

    builder.Logging.ClearProviders();
    builder.Logging.AddSimpleConsole(o =>
    {
        o.SingleLine = true;
        o.TimestampFormat = "HH:mm:ss ";
    });
    builder.Logging.SetMinimumLevel(ParseLogLevel(options.LogLevel));
    builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
    builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Information);
    builder.Logging.AddFilter("Microsoft.AspNetCore.SignalR", LogLevel.Warning);

    builder.WebHost.ConfigureKestrel(kestrel =>
    {
        // 上传大小上限随 AppOptions.MaxFileUploadBytes 走（留 8MB 余量给 multipart 封装开销），
        // 真正的拦截在控制器里按 _options.MaxFileUploadBytes 精确判定。
        kestrel.Limits.MaxRequestBodySize = options.MaxFileUploadBytes + 8L * 1024 * 1024;
        kestrel.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(10);
    });

    // ── 核心服务 ────────────────────────────────────────────────
    builder.Services.AddHyTranslateCore(options);
    builder.Services.AddHttpContextAccessor();

    // ── WebAPI：控制器 + 统一 camelCase JSON ────────────────────
    builder.Services.AddControllers().AddJsonOptions(o => ApiJson.Configure(o.JsonSerializerOptions));
    builder.Services.ConfigureHttpJsonOptions(o => ApiJson.Configure(o.SerializerOptions));

    // ── SignalR：实时任务进度 ───────────────────────────────────
    builder.Services
        .AddSignalR(o =>
        {
            o.EnableDetailedErrors = true;
            o.KeepAliveInterval = TimeSpan.FromSeconds(15);
            o.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
            // 上限需容纳 MaxTextBytes 文本 JSON 转义的最坏膨胀 + 术语表等大字段
            o.MaximumReceiveMessageSize = 4 * 1024 * 1024;
            // 必须 > 1：Translate 占用调用直到任务结束，默认值 1 会把 Cancel 排队到任务之后，终止即失效
            o.MaximumParallelInvocationsPerClient = 2;
        })
        .AddJsonProtocol(o => ApiJson.Configure(o.PayloadSerializerOptions));

    // ── MCP：Streamable HTTP ────────────────────────────────────
    builder.Services
        .AddMcpServer(o =>
        {
            o.ServerInfo = new Implementation { Name = HyTranslateServices.ServerName, Version = HyTranslateServices.ServerVersion };
            o.ServerInstructions = HyTranslateServices.ServerInstructions;
        })
        .WithHttpTransport()
        .WithTools<HyTranslateService.Tools.TranslationTools>();

    var app = builder.Build();

    var registry = app.Services.GetRequiredService<ModelRegistry>();
    var languages = app.Services.GetRequiredService<LanguageCatalog>();

    // 若服务位于反向代理之后，保留原始 Host（用于拼 MCP 端点地址）
    app.UseForwardedHeaders(new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    });

    // ── 静态文件（wwwroot）：css / js / images / lib / page ─────
    var mimeTypes = new FileExtensionContentTypeProvider();
    mimeTypes.Mappings[".json"] = "application/json; charset=utf-8";
    mimeTypes.Mappings[".woff2"] = "font/woff2";

    app.UseStaticFiles(new StaticFileOptions
    {
        ContentTypeProvider = mimeTypes,
        OnPrepareResponse = ctx =>
        {
            var headers = ctx.Context.Response.Headers;
            string path = ctx.Context.Request.Path.Value ?? string.Empty;

            // 第三方库可长缓存；自有 css/js/html 走协商缓存，避免改完看不到效果
            bool isLibrary = path.StartsWith("/lib/", StringComparison.OrdinalIgnoreCase);
            headers[HeaderNames.CacheControl] = isLibrary
                ? "public,max-age=604800"
                : "no-cache,must-revalidate";
        },
    });

    // API 访问日志：GET 属查询记 Debug（--log-level debug 才显示），POST 有副作用记 Information；
    // 受理/停止/忙拒绝等更细的语义日志在各控制器与 Hub 中。
    app.Use(async (context, next) =>
    {
        // 上传翻译：body 是不可回卷流。这里启用缓冲并整体预读回卷，
        // 保证控制器能稳定读到完整 multipart。
        if (context.Request.Path.StartsWithSegments("/api/translation/file") &&
            HttpMethods.IsPost(context.Request.Method))
        {
            context.Request.EnableBuffering();
            using var pre = new MemoryStream();
            await context.Request.Body.CopyToAsync(pre);
            context.Request.Body.Position = 0;
        }
        Stopwatch sw = Stopwatch.StartNew();
        try
        {
            await next();
        }
        finally
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                ILogger logger = context.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("HyTranslateService.Api");
                LogLevel level = HttpMethods.IsGet(context.Request.Method) ? LogLevel.Debug : LogLevel.Information;
                logger.Log(level, "API {Method} {Path}{Query} -> {StatusCode} ({ElapsedMs:0} ms)",
                    context.Request.Method,
                    context.Request.Path.Value,
                    context.Request.QueryString.HasValue ? context.Request.QueryString.Value : string.Empty,
                    context.Response.StatusCode,
                    sw.Elapsed.TotalMilliseconds);
            }
        }
    });

    // ── 路由与端点 ──────────────────────────────────────────────
    app.MapControllers();
    app.MapHub<TranslationSignalRHub>(TranslationHubRoutes.Path);
    app.MapMcp("/mcp");

    // 未显式指定 --host/--port 时，尊重 launchSettings / ASPNETCORE_URLS
    string? envUrls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
    bool useConfiguredUrls = !options.AddressExplicitlySet && !string.IsNullOrWhiteSpace(envUrls);

    app.Lifetime.ApplicationStarted.Register(() =>
    {
        string urls = string.Join("  ", app.Urls);
        Console.WriteLine(AppOptionsParser.DescribeStartup(options, registry.ModelsDirectory, registry.Models.Count, urls));
        Console.WriteLine($"  界面语言   : {languages.All.Count} 种（扫描自 {LanguageCatalog.RelativeDirectory}）");
        Console.WriteLine("  按 Ctrl+C 退出。");
        if (options.OpenBrowser) TryOpenBrowser(AppOptionsParser.FirstHttpUrl(urls) ?? options.WebUrl);
    });

    // 预加载放到后台，避免拖慢端口监听（结果由 ModelHost 内部日志体现）
    if (!string.IsNullOrWhiteSpace(options.PreloadModel))
    {
        var host = app.Services.GetRequiredService<ModelHost>();
        _ = Task.Run(() => host.TryPreload(options.PreloadModel));
    }

    if (useConfiguredUrls)
    {
        await app.RunAsync();
    }
    else
    {
        await app.RunAsync(options.WebUrl);
    }
    return 0;
}

/// <summary>把命令行 / 环境变量传入的日志级别字符串归一化为 <see cref="LogLevel"/>。</summary>
/// <param name="value">日志级别文本（如 debug / info / warn），可为空。</param>
/// <returns>对应的 <see cref="LogLevel"/>；无法识别回退 Information。</returns>
static LogLevel ParseLogLevel(string value) => value.Trim().ToLowerInvariant() switch
{
    "trace" => LogLevel.Trace,
    "debug" => LogLevel.Debug,
    "information" or "info" => LogLevel.Information,
    "warning" or "warn" => LogLevel.Warning,
    "error" => LogLevel.Error,
    "critical" => LogLevel.Critical,
    "none" => LogLevel.None,
    _ => LogLevel.Information,
};

/// <summary>在用户默认浏览器中打开指定 URL（按平台调用对应命令）。</summary>
/// <param name="url">待打开的地址。</param>
static void TryOpenBrowser(string url)
{
    try
    {
        if (OperatingSystem.IsWindows())
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        else if (OperatingSystem.IsMacOS())
            Process.Start("open", url);
        else
            Process.Start("xdg-open", url);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine("自动打开浏览器失败（可手动访问 " + url + "）：" + ex.Message);
    }
}

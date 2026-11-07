using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using HyTranslateService.Translation;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace HyTranslateService.Tools;

/// <summary>
/// 面向 AI 客户端的 MCP 翻译工具集。与 WebUI 共用同一套模型注册表、模型宿主与任务管理器。
/// 同一时刻只跑一个任务：忙时新翻译被立即拒绝，先 get_current_translation 查看、
/// 必要时 cancel_current_translation 停止，再重新提交。
/// </summary>
[McpServerToolType]
public sealed class TranslationTools
{
    #region 字段

    /// <summary>进度消息最大字符数：超过即尾部截断，避免 MCP 进度通知过大。</summary>
    private const int ProgressMessageMaxChars = 320;

    /// <summary>应用选项：大小闸（MaxTextBytes/MaxFileUploadBytes）与文件工具开关。</summary>
    private readonly AppOptions _options;

    /// <summary>模型注册表：提供模型清单与推荐模型。</summary>
    private readonly ModelRegistry _registry;

    /// <summary>模型宿主：查询常驻模型与加载状态。</summary>
    private readonly ModelHost _modelHost;

    /// <summary>任务管理器：受理翻译任务、查询与停止。</summary>
    private readonly TranslationJobManager _jobs;

    /// <summary>工具层日志器。</summary>
    private readonly ILogger<TranslationTools> _logger;

    #endregion

    /// <summary>构造函数：注入与 WebUI 共用的同一套模型与任务设施。</summary>
    /// <param name="options">应用选项。</param>
    /// <param name="registry">模型注册表。</param>
    /// <param name="modelHost">模型宿主。</param>
    /// <param name="jobs">任务管理器。</param>
    /// <param name="logger">日志器。</param>
    public TranslationTools(
        AppOptions options,
        ModelRegistry registry,
        ModelHost modelHost,
        TranslationJobManager jobs,
        ILogger<TranslationTools> logger)
    {
        _options = options;
        _registry = registry;
        _modelHost = modelHost;
        _jobs = jobs;
        _logger = logger;
    }

    #region 私有方法

    /// <summary>受理任务并订阅事件直至终态，把任务事件映射为 MCP 进度通知；返回完整译文。</summary>
    /// <param name="request">已组装的翻译请求（Kind 为 mcp 或 mcp-file）。</param>
    /// <param name="progress">由 MCP SDK 注入的进度上报器。</param>
    /// <param name="cancellationToken">客户端取消令牌；取消时立即终止本地推理。</param>
    /// <returns>完整译文；停止时为已完成部分并加前缀说明。</returns>
    private async Task<string> RunToCompletionAsync(
        TranslationRequest request,
        IProgress<ProgressNotificationValue> progress,
        CancellationToken cancellationToken)
    {
        var outcome = _jobs.TryStart(request);
        if (outcome.Busy is not null)
        {
            _logger.LogWarning("MCP 翻译被拒绝（模型正忙）：{Reason}", outcome.Busy.Message);
            throw new McpException(BusyMessage(outcome.Busy));
        }
        if (outcome.Invalid is not null)
        {
            _logger.LogWarning("MCP 翻译请求无效：{Reason}", outcome.Invalid.Message);
            throw new McpException(outcome.Invalid.Message);
        }
        var job = outcome.Job!;

        var planned = job.Snapshot();
        _logger.LogInformation("MCP 翻译已受理 {JobId}：{Chars} 字符 → {Target}，{Segments} 段（模型 {Model}，引擎 {Engine}，KV {KvCache}，来源 {SourceName}）",
            job.Id, planned.SourceChars, planned.TargetLanguage, planned.TotalSegments, planned.Model, planned.Engine, planned.KvCache, planned.SourceName ?? "(内联文本)");

        // MCP 客户端取消 → 立即终止本地推理
        using var registration = cancellationToken.Register(() =>
        {
            _logger.LogInformation("收到客户端取消，停止任务 {JobId}", job.Id);
            job.Cancel();
        });

        int total = 0;
        int done = 0;
        var stopwatch = Stopwatch.StartNew();
        long lastReportMs = -1000;

        void Report(bool force, string? message = null)
        {
            long now = stopwatch.ElapsedMilliseconds;
            if (!force && now - lastReportMs < 250) return;
            lastReportMs = now;
            progress.Report(new ProgressNotificationValue
            {
                Progress = done,
                Total = total > 0 ? total : null,
                Message = message,
            });
        }

        Report(true, "任务已创建，正在准备…");

        string translation = string.Empty;
        string finalStatus = "unknown";
        string? error = null;

        try
        {
            await foreach (var evt in job.SubscribeAsync(cancellationToken).ConfigureAwait(false))
            {
                switch (evt.Type)
                {
                    case JobEventTypes.Preparing:
                        // 任务已受理，正在准备模型（内存中只常驻一个模型）
                        if (!string.IsNullOrEmpty(evt.Message)) Report(true, evt.Message);
                        break;

                    case JobEventTypes.Planned:
                        total = evt.Total ?? 0;
                        Report(true, $"已拆分为 {total} 段，准备翻译…");
                        break;

                    case JobEventTypes.Loading:
                        Report(true, evt.Message);
                        break;

                    case JobEventTypes.Segment:
                        Report(true, $"正在翻译第 {(evt.Index ?? 0) + 1}/{evt.Total ?? total} 段…");
                        break;

                    case JobEventTypes.Delta:
                        Report(false, BuildDeltaMessage(evt));
                        break;

                    case JobEventTypes.SegmentDone:
                        done = evt.Completed ?? done;
                        Report(true, $"[{done}/{evt.Total ?? total}] 已完成：{Trim(evt.Translation ?? string.Empty, ProgressMessageMaxChars)}");
                        break;

                    case JobEventTypes.Done:
                        translation = evt.Translation ?? string.Empty;
                        done = evt.Completed ?? done;
                        total = evt.Total ?? total;
                        finalStatus = "completed";
                        Report(true, $"翻译完成，共 {total} 段，用时 {stopwatch.Elapsed.TotalSeconds:0.0}s");
                        break;

                    case JobEventTypes.Cancelled:
                        translation = evt.Translation ?? string.Empty;
                        finalStatus = JobEventTypes.Cancelled;
                        break;

                    case JobEventTypes.Error:
                        finalStatus = JobEventTypes.Error;
                        error = evt.Message;
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            job.Cancel();
            throw;
        }

        if (finalStatus == "unknown")
        {
            // 订阅提前结束（例如历史上已有终态事件），以任务快照为准
            var snapshot = job.Snapshot();
            translation = snapshot.Translation;
            finalStatus = snapshot.Status.ToLowerInvariant();
            error = snapshot.Error;
        }

        switch (finalStatus)
        {
            case "completed":
                _logger.LogInformation("MCP 翻译完成 {JobId}：{Done}/{Total} 段，耗时 {Elapsed:0.0} 秒", job.Id, done, total, stopwatch.Elapsed.TotalSeconds);
                break;
            case JobEventTypes.Cancelled:
                _logger.LogInformation("MCP 翻译已停止 {JobId}：完成 {Done}/{Total} 段", job.Id, done, total);
                break;
            case "failed" or JobEventTypes.Error:
                _logger.LogWarning("MCP 翻译失败 {JobId}：{Error}", job.Id, error ?? "未知错误");
                break;
        }

        return finalStatus switch
        {
            "completed" => translation,
            JobEventTypes.Cancelled => $"[翻译已停止，以下为已完成部分]\n\n{translation}",
            "failed" or JobEventTypes.Error => throw new McpException("翻译失败：" + (error ?? "未知错误")),
            _ => string.IsNullOrEmpty(translation) ? "(未产生翻译结果)" : translation,
        };
    }

    /// <summary>「模型正忙」的统一对外说明：原因 + 下一步动作。</summary>
    private static string BusyMessage(ModelBusyException ex)
    {
        return $"""
            模型正忙，本次翻译未开始。
            {ex.Message}

            下一步：
              1) get_current_translation 查看当前任务的进度与译文，确认是否需要保留已译内容；
              2) cancel_current_translation 停止当前任务（已译部分仍可查询）；
              3) 重新调用本工具开始新的翻译。
            """;
    }

    private static string BuildDeltaMessage(JobEvent evt)
    {
        string text = evt.Text ?? string.Empty;
        string tail = text.Length <= ProgressMessageMaxChars ? text : "…" + text[^ProgressMessageMaxChars..];
        return $"第 {(evt.Index ?? 0) + 1}/{evt.Total ?? 0} 段译文中：{tail}";
    }

    private static string Trim(string value, int max)
        => value.Length <= max ? value : value[..max] + "…";

    #endregion

    #region 公共方法

    /// <summary>MCP 工具：列出本地已下载的 Hy-MT2 模型及加载状态，并附官方发布的全部模型目录。</summary>
    /// <param name="refresh">true 时重新扫描模型目录（新增权重后使用），默认 false 用缓存清单。</param>
    /// <returns>模型表格文本（已下载模型表 + 官方目录含未下载项与下载方式 + 使用提示）。</returns>
    [McpServerTool(Name = "list_translation_models", ReadOnly = true, Idempotent = true,
        Title = "列出可用翻译模型")]
    [Description("列出本地 models 目录中已下载的 Hy-MT2 翻译模型（含 ID、量化、体积、是否已加载、是否为推荐模型），以及官方发布的全部模型目录（未下载项可经 WebUI「翻译模型」下拉自动下载，huggingface.co 受限时自动走 hf-mirror.com 镜像站）。翻译时用 model 参数传入这里的 ID。")]
    public string ListTranslationModels([Description("是否重新扫描模型目录，默认 false")] bool refresh = false)
    {
        var models = refresh ? _registry.Refresh() : _registry.Models;
        var recommended = _registry.Recommended;

        var sb = new StringBuilder();
        sb.AppendLine($"模型目录：{_registry.ModelsDirectory}");
        sb.AppendLine($"已下载模型：{models.Count} 个" + (recommended is null ? "" : $"，推荐：{recommended.Id}"));
        sb.AppendLine($"常驻内存：{_modelHost.ResidentModelId ?? "(当前无)"}（系统规定同一时刻只载入 1 个模型）");
        if (_modelHost.ResidentModelId is not null)
            sb.AppendLine($"常驻配置：引擎 {_modelHost.ResidentEngine}（实际后端 {_modelHost.ResidentBackendName}）· KV 缓存 {_modelHost.ResidentKvCache}");
        sb.AppendLine();

        if (models.Count > 0)
        {
            sb.AppendLine("序号 | 模型 ID | 量化 | 体积 | 状态 | 说明");
            int index = 1;
            foreach (var m in models)
            {
                var flags = new List<string>();
                if (_modelHost.IsLoaded(m.Id)) flags.Add("已加载");
                if (recommended is not null && m.Id == recommended.Id) flags.Add("推荐");
                sb.Append(index++).Append(" | ")
                  .Append(m.Id).Append(" | ")
                  .Append(m.Quantization).Append(" | ")
                  .Append(m.SizeText).Append(" | ")
                  .Append(flags.Count == 0 ? "未加载" : string.Join("/", flags)).Append(" | ")
                  .AppendLine(m.FileName);
            }

            sb.AppendLine();
        }

        sb.AppendLine("官方发布的模型目录（未下载项：在 WebUI「翻译模型」下拉中选择即自动下载）：");
        foreach (var official in ModelCatalog.All)
        {
            var hit = models.FirstOrDefault(m => string.Equals(m.Id, official.Id, StringComparison.OrdinalIgnoreCase));
            string size = hit is not null
                ? hit.SizeText
                : "~" + TranslationModelInfo.FormatSize(official.ApproximateSizeBytes);
            sb.Append("  · ").Append(official.Id).Append(" | ")
              .Append(official.Quantization).Append(" | ")
              .Append(size).Append(" | ")
              .Append(hit is not null ? "已下载" : "未下载").Append(" | ")
              .AppendLine(official.Description);
        }

        var extras = models.Where(m => ModelCatalog.Find(m.Id) is null).ToList();
        if (extras.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("本地额外模型（用户自行放入，不在官方目录中）：");
            foreach (var m in extras)
            {
                sb.Append("  · ").Append(m.Id).Append(" | ")
                  .Append(m.Quantization).Append(" | ")
                  .AppendLine(m.SizeText);
            }
        }

        sb.AppendLine();
        sb.AppendLine("提示：model 参数可传模型 ID、文件名、序号或 auto（自动选择推荐模型）。");
        sb.AppendLine("      engine 可选 auto/gpu/cpu；kvCache 可选 memory/none。两者在模型加载时生效：");
        sb.AppendLine("      与常驻模型不同时会先卸载再加载（需要一点加载时间），同一模型的译文不受影响。");
        sb.AppendLine("      内存中同一时刻只常驻 1 个模型：要换模型时会先卸载当前模型再加载，需要一点加载时间。");
        sb.AppendLine("      同一时刻只允许 1 个翻译任务：当前已有任务在跑时新任务会被**立即拒绝**（不会排队），");
        sb.AppendLine("      此时先用 get_current_translation 查看当前任务，需要的话用 cancel_current_translation 停止，然后再重试。");
        return sb.ToString();
    }

    /// <summary>MCP 工具：列出全部可翻译目标语言（代码/英文名/中文名）。</summary>
    /// <returns>语言清单表格文本。</returns>
    [McpServerTool(Name = "list_target_languages", ReadOnly = true, Idempotent = true,
        Title = "列出可翻译的目标语言")]
    [Description("列出 Hy-MT2 支持的全部目标语言（代码、英文名、中文名）。targetLanguage 参数可传代码（zh/en/...）、英文名或中文名。")]
    public string ListTargetLanguages()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Hy-MT2 支持 {HyMT2Languages.All.Count} 种目标语言：");
        sb.AppendLine();
        sb.AppendLine("代码 | 英文名 | 中文名");
        foreach (var d in HyMT2Languages.All)
            sb.Append(d.Code).Append(" | ").Append(d.EnglishName).Append(" | ").AppendLine(d.ChineseName);
        sb.AppendLine();
        sb.AppendLine("提示：sourceLanguage 留空即自动识别；提示词语言可用 promptLanguage 设为 zh（中文提示词）。");
        return sb.ToString();
    }

    /// <summary>MCP 工具：翻译文本；长文本自动拆段、流式上报进度且可中途取消。忙时直接失败并提示当前任务。</summary>
    /// <param name="text">要翻译的原文，可包含换行与 Markdown；超限改用 translate_file。</param>
    /// <param name="targetLanguage">目标语言：代码、英文名或中文名。</param>
    /// <param name="progress">由 MCP SDK 注入的进度上报器。</param>
    /// <param name="sourceLanguage">源语言；null 自动识别。</param>
    /// <param name="model">模型 ID/文件名/序号/auto；null 取推荐模型。</param>
    /// <param name="engine">推理引擎：auto / gpu / cpu；null 用服务端默认。</param>
    /// <param name="kvCache">KV 缓存策略：memory / none；null 用服务端默认。</param>
    /// <param name="segmentChars">单段最大字符数；null 用默认 480。</param>
    /// <param name="maxTokens">单段最大生成 token 数；null 自动推断。</param>
    /// <param name="temperature">采样温度，0 为贪心；null 用官方推荐 0.7。</param>
    /// <param name="topP">核采样阈值；null 默认 0.6。</param>
    /// <param name="topK">top-k 采样；null 默认 20。</param>
    /// <param name="repetitionPenalty">重复惩罚；null 默认 1.05。</param>
    /// <param name="promptStyle">提示词模板；null 用 short。</param>
    /// <param name="promptLanguage">提示词语言；null 用 en。</param>
    /// <param name="glossary">术语表（每行「原文=译文」）；null 无术语约束。</param>
    /// <param name="background">背景信息/文档上下文；null 无。</param>
    /// <param name="targetStyle">目标风格描述；null 不附加风格要求。</param>
    /// <returns>完整译文（保留原文段落与换行结构）；停止时为已完成部分。</returns>
    [McpServerTool(Name = "translate_text", ReadOnly = true, Idempotent = false, OpenWorld = false,
        Title = "翻译文本（流式进度 + 可取消）")]
    [Description(
        "把文本翻译成目标语言。会自动把长文本拆分为多个片段依次翻译（无需手动切分），" +
        "并在执行过程中持续上报进度（含译文增量），因此客户端可以在中途取消该次调用以立即终止翻译。" +
        "返回完整的译文（已保留原文的段落与换行结构）。" +
        "注意：同一时刻只执行一个翻译任务，若当前已有任务在跑，本工具会直接失败并告知当前任务，" +
        "此时应先 get_current_translation 查看，必要时 cancel_current_translation 停止，再重试。")]
    public async Task<string> TranslateText(
        [Description("要翻译的原文，可包含换行与 Markdown；超长文本会自动拆段翻译")] string text,
        [Description("目标语言：代码(zh/en/ja/...)、英文名(Chinese/Japanese/...)或中文名(中文/日语/...)，详见 list_target_languages")] string targetLanguage,
        // 以下两个参数由 MCP SDK 注入：progress 用于流式上报进度，cancellationToken 用于响应客户端取消
        IProgress<ProgressNotificationValue> progress,
        CancellationToken cancellationToken,
        [Description("源语言；留空表示自动识别，取值规则同 targetLanguage")] string? sourceLanguage = null,
        [Description("翻译模型：模型 ID / 文件名 / 序号 / auto（默认取推荐模型），详见 list_translation_models")] string? model = null,
        [Description("推理引擎：auto（有可用 GPU 就用，失败回落 CPU）/ gpu（强制显卡，不可用即失败）/ cpu（纯 CPU）；留空用服务端默认（默认 auto）")] string? engine = null,
        [Description("KV 缓存策略：memory（任务结束保留缓冲，默认）/ none（结束即释放，最省内存）；只影响内存与预热速度，不改变译文")] string? kvCache = null,
        [Description("单段最大字符数，默认 480；越大上下文越完整但单段耗时更长")] int? segmentChars = null,
        [Description("单段最大生成 token 数，留空自动推断")] int? maxTokens = null,
        [Description("采样温度，0 表示贪心（确定性）；默认使用官方推荐 0.7")] float? temperature = null,
        [Description("核采样阈值 topP，默认 0.6")] float? topP = null,
        [Description("top-k 采样 topK，默认 20")] int? topK = null,
        [Description("重复惩罚，默认 1.05")] float? repetitionPenalty = null,
        [Description("提示词模板：short(默认)/official/terminology/style/delimiters/background")] string? promptStyle = null,
        [Description("提示词语言：en(默认) 或 zh")] string? promptLanguage = null,
        [Description("术语表，每行一条「原文=译文」，可显著提升专有名词一致性")] string? glossary = null,
        [Description("背景信息/文档上下文，帮助模型把握语气与领域")] string? background = null,
        [Description("目标风格描述，如「口语化」「正式书面」「简洁技术文档」")] string? targetStyle = null)
    {
        ArgumentNullException.ThrowIfNull(progress);

        // 内联文本入口的大小闸（接口层收口）：核心层不校验大小。大文本请用 translate_file。
        int textBytes = Encoding.UTF8.GetByteCount(text);
        if (textBytes > _options.MaxTextBytes)
            throw new McpException(
                $"文本过大（{textBytes / 1024.0:F1} KB），单次上限为 {_options.MaxTextBytes / 1024} KB；" +
                "请改用 translate_file 翻译本地文件，或拆分后分批翻译。");

        var request = new TranslationRequest
        {
            Text = text,
            Kind = TranslationJobKinds.Mcp,
            TargetLanguage = targetLanguage,
            SourceLanguage = sourceLanguage,
            Model = model,
            Engine = engine,
            KvCache = kvCache,
            SegmentChars = segmentChars,
            MaxTokensPerSegment = maxTokens,
            Temperature = temperature,
            TopP = topP,
            TopK = topK,
            RepetitionPenalty = repetitionPenalty,
            PromptStyle = promptStyle,
            PromptLanguage = promptLanguage,
            Glossary = glossary,
            Background = background,
            TargetStyle = targetStyle,
        };

        return await RunToCompletionAsync(request, progress, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>MCP 工具：读取本机文本文件并翻译其内容；支持自动拆段、流式进度与中途取消。受 --no-file-tools 开关控制。</summary>
    /// <param name="filePath">本地文本文件的绝对路径；不存在/超限/读取失败时抛 McpException。</param>
    /// <param name="targetLanguage">目标语言：代码、英文名或中文名。</param>
    /// <param name="progress">由 MCP SDK 注入的进度上报器。</param>
    /// <param name="sourceLanguage">源语言；null 自动识别。</param>
    /// <param name="model">模型 ID/序号/auto；null 取推荐模型。</param>
    /// <param name="engine">推理引擎：auto / gpu / cpu；null 用服务端默认。</param>
    /// <param name="kvCache">KV 缓存策略：memory / none；null 用服务端默认。</param>
    /// <param name="segmentChars">单段最大字符数；null 用默认 480。</param>
    /// <param name="maxTokens">单段最大生成 token 数；null 自动推断。</param>
    /// <param name="temperature">采样温度，0 为贪心；null 用官方推荐 0.7。</param>
    /// <param name="topP">核采样阈值；null 默认 0.6。</param>
    /// <param name="topK">top-k 采样；null 默认 20。</param>
    /// <param name="repetitionPenalty">重复惩罚；null 默认 1.05。</param>
    /// <param name="promptStyle">提示词模板；null 用 short。</param>
    /// <param name="promptLanguage">提示词语言；null 用 en。</param>
    /// <param name="glossary">术语表（每行「原文=译文」）；null 无术语约束。</param>
    /// <param name="background">背景信息/文档上下文；null 无。</param>
    /// <param name="targetStyle">目标风格描述；null 不附加风格要求。</param>
    /// <returns>完整译文；停止时为已完成部分并加前缀说明；空文件返回提示文本。</returns>
    [McpServerTool(Name = "translate_file", ReadOnly = true, Idempotent = false, OpenWorld = false,
        Title = "翻译本地文本文件")]
    [Description(
        "读取本机上的一个文本文件（.txt/.md/.srt/.csv/.json/.log 等纯文本），翻译其内容并返回译文。" +
        "同样支持长文本自动拆段、流式进度与中途取消。")]
    public async Task<string> TranslateFile(
        [Description("本地文本文件的路径，建议用绝对路径（相对路径按服务进程的工作目录解析）")] string filePath,
        [Description("目标语言：代码、英文名或中文名")] string targetLanguage,
        // 以下两个参数由 MCP SDK 注入
        IProgress<ProgressNotificationValue> progress,
        CancellationToken cancellationToken,
        [Description("源语言；留空自动识别")] string? sourceLanguage = null,
        [Description("翻译模型 ID / 序号 / auto")] string? model = null,
        [Description("推理引擎：auto / gpu / cpu；留空用服务端默认（默认 auto）")] string? engine = null,
        [Description("KV 缓存策略：memory（默认）/ none（最省内存）；不改变译文")] string? kvCache = null,
        [Description("单段最大字符数，默认 480")] int? segmentChars = null,
        [Description("单段最大生成 token 数，留空自动推断")] int? maxTokens = null,
        [Description("采样温度，0 表示贪心（确定性）；默认使用官方推荐 0.7")] float? temperature = null,
        [Description("核采样阈值 topP，默认 0.6")] float? topP = null,
        [Description("top-k 采样 topK，默认 20")] int? topK = null,
        [Description("重复惩罚，默认 1.05")] float? repetitionPenalty = null,
        [Description("提示词模板：short/official/terminology/style/delimiters/background")] string? promptStyle = null,
        [Description("提示词语言：en 或 zh")] string? promptLanguage = null,
        [Description("术语表，每行「原文=译文」")] string? glossary = null,
        [Description("背景信息/文档上下文")] string? background = null,
        [Description("目标风格描述")] string? targetStyle = null)
    {
        ArgumentNullException.ThrowIfNull(progress);

        if (!_options.EnableFileTools)
            throw new McpException("本服务已禁用按文件路径翻译（启动参数 --no-file-tools）。");

        if (string.IsNullOrWhiteSpace(filePath))
            throw new McpException("filePath 不能为空。");

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(filePath);
        }
        catch (Exception ex)
        {
            throw new McpException($"路径无效：{ex.Message}");
        }

        if (!File.Exists(fullPath))
            throw new McpException($"文件不存在：{fullPath}");

        var info = new FileInfo(fullPath);
        if (info.Length > _options.MaxFileUploadBytes)
            throw new McpException($"文件过大（{info.Length / 1024 / 1024} MB），上限 {_options.MaxFileUploadBytes / 1024 / 1024} MB。");

        string content;
        try
        {
            content = await File.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new McpException($"读取文件失败：{ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(content))
            return "(文件内容为空，无需翻译)";

        var request = new TranslationRequest
        {
            Text = content,
            Kind = TranslationJobKinds.McpFile,
            TargetLanguage = targetLanguage,
            SourceLanguage = sourceLanguage,
            Model = model,
            Engine = engine,
            KvCache = kvCache,
            SegmentChars = segmentChars,
            MaxTokensPerSegment = maxTokens,
            Temperature = temperature,
            TopP = topP,
            TopK = topK,
            RepetitionPenalty = repetitionPenalty,
            PromptStyle = promptStyle,
            PromptLanguage = promptLanguage,
            Glossary = glossary,
            Background = background,
            TargetStyle = targetStyle,
            SourceName = info.Name,
        };

        return await RunToCompletionAsync(request, progress, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>MCP 工具：查询当前翻译任务的状态、进度与已译内容（系统只维护一个当前任务）。</summary>
    /// <param name="includeSegments">true 时附带逐段原文/译文对照，默认 false。</param>
    /// <returns>任务状态文本；无任务时返回提示。</returns>
    [McpServerTool(Name = "get_current_translation", ReadOnly = true, Idempotent = true,
        Title = "查询当前翻译任务")]
    [Description("查询当前正在执行的翻译任务的状态、进度与已译内容。同一时刻只执行一个任务；没有任务时返回提示。")]
    public string GetCurrentTranslation(
        [Description("包含已完成的逐段对照内容，默认 false")] bool includeSegments = false)
    {
        var snapshot = _jobs.GetCurrentSnapshot();
        if (snapshot is null) return "当前没有正在执行的翻译任务。";

        var sb = new StringBuilder();
        sb.AppendLine($"任务     : {snapshot.JobId}");
        sb.AppendLine($"状态     : {snapshot.Status}{(snapshot.IsFinished ? "（已结束）" : "")}");
        if (!string.IsNullOrEmpty(snapshot.Note)) sb.AppendLine($"说明     : {snapshot.Note}");
        sb.AppendLine($"模型     : {snapshot.Model} ({snapshot.ModelQuantization})");
        sb.AppendLine($"推理     : 引擎 {snapshot.Engine} · KV 缓存 {snapshot.KvCache}");
        sb.AppendLine($"语言     : {(snapshot.SourceLanguage ?? "auto")} -> {snapshot.TargetLanguage}");
        sb.AppendLine($"进度     : {snapshot.CompletedSegments}/{snapshot.TotalSegments} 段，用时 {snapshot.ElapsedMs / 1000:0.0}s");
        if (!string.IsNullOrEmpty(snapshot.Error)) sb.AppendLine($"错误     : {snapshot.Error}");
        sb.AppendLine();
        sb.AppendLine("── 译文 ──");
        sb.AppendLine(string.IsNullOrEmpty(snapshot.Translation) ? "(暂无内容)" : snapshot.Translation);

        if (includeSegments && snapshot.Segments.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("── 逐段对照 ──");
            foreach (var seg in snapshot.Segments)
            {
                sb.Append('[').Append(seg.Index + 1).Append(seg.Done ? "]" : "·进行中]").AppendLine();
                sb.Append("原文: ").AppendLine(Trim(seg.Source, 200));
                sb.Append("译文: ").AppendLine(Trim(seg.Translation, 200));
            }
        }

        return sb.ToString();
    }

    /// <summary>MCP 工具：立即停止当前翻译任务，推理在下一个 token 处停下（已译部分仍可查询）。</summary>
    /// <returns>停止结果说明文本。</returns>
    [McpServerTool(Name = "cancel_current_translation", ReadOnly = false, Destructive = true, Idempotent = true,
        Title = "停止当前翻译任务")]
    [Description("立即停止当前正在执行的翻译任务，正在进行的推理会在下一个 token 处停下。已翻译的部分内容仍可用 get_current_translation 获取。")]
    public string CancelCurrentTranslation()
    {
        var snapshot = _jobs.GetCurrentSnapshot();
        if (snapshot is null) return "当前没有正在执行的翻译任务。";
        if (snapshot.IsFinished) return $"当前任务 {snapshot.JobId} 已结束（{snapshot.Status}），无需停止。";

        bool ok = _jobs.CancelCurrent();
        if (ok) _logger.LogInformation("收到停止请求（MCP）：已请求终止当前任务 {JobId}", snapshot.JobId);
        else _logger.LogInformation("收到停止请求（MCP）：任务 {JobId} 无法停止", snapshot.JobId);
        return ok
            ? $"已请求停止当前任务 {snapshot.JobId}，推理将在下一个 token 处停止。已翻译部分仍可用 get_current_translation 获取。"
            : $"当前任务 {snapshot.JobId} 无法停止。";
    }

    #endregion
}

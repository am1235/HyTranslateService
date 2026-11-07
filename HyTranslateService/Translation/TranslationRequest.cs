namespace HyTranslateService.Translation;

/// <summary>任务入口种类（随快照透出，供 WebUI 刷新后分派接续显示）。</summary>
public static class TranslationJobKinds
{
    /// <summary>WebUI / REST 内联文本。</summary>
    public const string Inline = "inline";

    /// <summary>WebUI 大文本文件上传（结束后经 file-result 下载）。</summary>
    public const string File = "file";

    /// <summary>MCP 内联文本工具。</summary>
    public const string Mcp = "mcp";

    /// <summary>MCP 本地文件工具。</summary>
    public const string McpFile = "mcp-file";
}

/// <summary>翻译请求（来自 MCP 工具或 WebUI，字段均为可选/宽松字符串，解析时统一归一化）。</summary>
public sealed record TranslationRequest
{
    /// <summary>待翻译的原文。</summary>
    public required string Text { get; init; }

    /// <summary>目标语言：代码 / 英文名 / 中文名 / 枚举名均可。</summary>
    public string? TargetLanguage { get; init; }

    /// <summary>源语言；留空表示由模型自行判断。</summary>
    public string? SourceLanguage { get; init; }

    /// <summary>模型：ID / 文件名 / 路径 / 序号 / auto。</summary>
    public string? Model { get; init; }

    /// <summary>单段最大字符数。</summary>
    public int? SegmentChars { get; init; }

    /// <summary>单段最大生成 token 数。</summary>
    public int? MaxTokensPerSegment { get; init; }

    /// <summary>采样温度；null 用推荐值。</summary>
    public float? Temperature { get; init; }

    /// <summary>核采样阈值（0.01~1）；null 用推荐值。</summary>
    public float? TopP { get; init; }

    /// <summary>Top-K 采样候选数；null 用推荐值。</summary>
    public int? TopK { get; init; }

    /// <summary>重复惩罚；null 用推荐值。</summary>
    public float? RepetitionPenalty { get; init; }

    /// <summary>随机种子；null 用推荐值。</summary>
    public int? Seed { get; init; }

    /// <summary>提示词风格（short/official/terminology/style/delimiters/background）。</summary>
    public string? PromptStyle { get; init; }

    /// <summary>提示词语言（en/zh）。</summary>
    public string? PromptLanguage { get; init; }

    /// <summary>术语表，每行 <c>原文=译文</c>。</summary>
    public string? Glossary { get; init; }

    /// <summary>背景信息 / 文档上下文。</summary>
    public string? Background { get; init; }

    /// <summary>目标风格描述（如「口语化」「正式书面」）。</summary>
    public string? TargetStyle { get; init; }

    /// <summary>推理引擎（auto/gpu/cpu）；null 用服务端默认。</summary>
    public string? Engine { get; init; }

    /// <summary>KV 缓存策略（none/memory）；null 用服务端默认。</summary>
    public string? KvCache { get; init; }

    /// <summary>来源名称（文件名等，仅用于展示）。</summary>
    public string? SourceName { get; init; }

    /// <summary>入口种类（<see cref="TranslationJobKinds"/>）；空值按 inline 处理。</summary>
    public string? Kind { get; init; }

    /// <summary>是否丢弃输出中的多余空白（默认开启）。</summary>
    public bool TrimOutput { get; init; } = true;
}

/// <summary>已归一化的翻译请求。</summary>
public sealed record ResolvedTranslationRequest
{
    #region 公共属性

    /// <summary>待翻译的原文（非空，入口已校验）。</summary>
    public required string Text { get; init; }

    /// <summary>目标语言；显式传参但无法识别时会在归一化时报错，不会静默回退。</summary>
    public required HyMT2Language Target { get; init; }

    /// <summary>源语言；null 表示由模型自动识别，或与目标语言一致（按自动处理）。</summary>
    public HyMT2Language? Source { get; init; }

    /// <summary>解析后的目标模型。</summary>
    public required TranslationModelInfo Model { get; init; }

    /// <summary>单段最大字符数（已按配置上下限收敛）。</summary>
    public required int SegmentChars { get; init; }

    /// <summary>采样参数（各项已收敛到合法区间）。</summary>
    public required SamplingOptions Sampling { get; init; }

    /// <summary>提示词风格。</summary>
    public required PromptStyle Style { get; init; }

    /// <summary>提示词语言。</summary>
    public required PromptLanguage PromptLanguage { get; init; }

    /// <summary>术语表，每行 <c>原文=译文</c>；未提供时为 null。</summary>
    public string? Glossary { get; init; }

    /// <summary>背景信息 / 文档上下文；未提供时为 null。</summary>
    public string? Background { get; init; }

    /// <summary>目标风格描述（如「口语化」「正式书面」）；未提供时为 null。</summary>
    public string? TargetStyle { get; init; }

    /// <summary>推理引擎标识（<see cref="InferenceEngines"/>）；已按服务端默认补齐。</summary>
    public required string Engine { get; init; }

    /// <summary>KV 缓存策略标识（<see cref="KvCachePolicies"/>）；已按服务端默认补齐。</summary>
    public required string KvCache { get; init; }

    /// <summary>来源名称（文件名等，仅用于展示）。</summary>
    public string? SourceName { get; init; }

    /// <summary>入口种类（<see cref="TranslationJobKinds"/>）。</summary>
    public string Kind { get; init; } = TranslationJobKinds.Inline;

    /// <summary>是否丢弃输出中的多余空白（默认开启）。</summary>
    public bool TrimOutput { get; init; } = true;

    #endregion

    /// <summary>入口种类归一化：空值与未知值都按 inline 处理。</summary>
    /// <param name="kind">原始入口种类。</param>
    /// <returns>归一化后的入口种类常量。</returns>
    private static string NormalizeKind(string? kind)
    {
        if (string.IsNullOrWhiteSpace(kind)) return TranslationJobKinds.Inline;
        var v = kind.Trim().ToLowerInvariant();
        return v is TranslationJobKinds.Inline or TranslationJobKinds.File
            or TranslationJobKinds.Mcp or TranslationJobKinds.McpFile
            ? v : TranslationJobKinds.Inline;
    }

    #region 公共方法

    /// <summary>把宽松请求归一化为强类型请求。</summary>
    /// <param name="raw">宽松原始请求。</param>
    /// <param name="registry">模型注册表，用于解析模型描述。</param>
    /// <param name="options">应用配置，提供各项默认值。</param>
    /// <returns>归一化后的强类型请求。</returns>
    public static ResolvedTranslationRequest Resolve(TranslationRequest raw, ModelRegistry registry, AppOptions options)
    {
        if (string.IsNullOrWhiteSpace(raw.Text))
            throw new ArgumentException("待翻译文本不能为空。");

        // 注：文本大小上限不在核心层校验——由各接口入口收口（见 AppOptions）。

        // 目标语言：显式传了但无法识别时必须报错，避免静默回退到默认语言造成误译
        HyMT2Language target;
        if (string.IsNullOrWhiteSpace(raw.TargetLanguage))
        {
            target = HyMT2Languages.TryParse(options.DefaultTargetLanguage, out var def)
                ? def
                : HyMT2Language.Chinese;
        }
        else if (!HyMT2Languages.TryParse(raw.TargetLanguage, out target))
        {
            throw new ArgumentException(
                $"无法识别目标语言 \"{raw.TargetLanguage}\"。可用取值：" + HyMT2Languages.CompactList() +
                "，也可使用中文名（如 中文 / 繁体中文）。");
        }

        // 源语言：未显式指定时回退到服务端默认源语言（约定：前端 null/空 = 没选过，用后端默认）。
        // 默认源语言为空时行为与以往一致——仍由模型自动识别。
        string? rawSource = raw.SourceLanguage;
        if (string.IsNullOrWhiteSpace(rawSource) && !string.IsNullOrWhiteSpace(options.DefaultSourceLanguage))
            rawSource = options.DefaultSourceLanguage;

        HyMT2Language? source = null;
        if (!string.IsNullOrWhiteSpace(rawSource) &&
            !rawSource.Equals("auto", StringComparison.OrdinalIgnoreCase) &&
            !rawSource.Equals("自动", StringComparison.OrdinalIgnoreCase))
        {
            if (!HyMT2Languages.TryParse(rawSource, out var parsedSource))
            {
                throw new ArgumentException(
                    $"无法识别源语言 \"{rawSource}\"。可用取值：" + HyMT2Languages.CompactList() +
                    "，或留空表示自动识别。");
            }
            if (parsedSource != target) source = parsedSource; // 源与目标一致时按自动处理
        }

        var model = registry.Resolve(raw.Model);

        // 引擎与缓存策略：显式传了但无法识别时报错，避免「以为在用 GPU 实际在跑 CPU」
        string engine = ResolveEngine(raw.Engine, options.Engine);
        string kvCache = ResolveKvCache(raw.KvCache, options.KvCache);

        int segmentChars = Math.Clamp(
            raw.SegmentChars ?? options.DefaultSegmentChars,
            80,
            options.MaxSegmentChars);

        var sampling = new SamplingOptions
        {
            Temperature = raw.Temperature ?? SamplingOptions.Recommended.Temperature,
            TopP = Math.Clamp(raw.TopP ?? SamplingOptions.Recommended.TopP, 0.01f, 1f),
            TopK = raw.TopK ?? SamplingOptions.Recommended.TopK,
            RepetitionPenalty = Math.Clamp(raw.RepetitionPenalty ?? SamplingOptions.Recommended.RepetitionPenalty, 0.5f, 3f),
            Seed = raw.Seed ?? -1,
            MaxTokens = raw.MaxTokensPerSegment ?? 0,
        };

        return new ResolvedTranslationRequest
        {
            Text = raw.Text,
            Target = target,
            Source = source,
            Model = model,
            SegmentChars = segmentChars,
            Sampling = sampling,
            Style = ParseStyle(raw.PromptStyle),
            PromptLanguage = ParsePromptLanguage(raw.PromptLanguage),
            Engine = engine,
            KvCache = kvCache,
            Glossary = raw.Glossary,
            Background = raw.Background,
            TargetStyle = raw.TargetStyle,
            SourceName = raw.SourceName,
            Kind = NormalizeKind(raw.Kind),
            TrimOutput = raw.TrimOutput,
        };
    }

    /// <summary>归一化推理引擎；显式传了未知值时抛错（不静默改用默认引擎）。</summary>
    /// <param name="value">请求中的引擎描述；空白表示沿用服务端默认。</param>
    /// <param name="fallback">服务端默认引擎（来自 <see cref="AppOptions.Engine"/>）。</param>
    /// <returns>归一化后的引擎标识。</returns>
    /// <exception cref="ArgumentException">描述非空且无法识别时抛出。</exception>
    private static string ResolveEngine(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        string normalized = InferenceEngines.Normalize(value, string.Empty);
        if (normalized.Length == 0)
        {
            throw new ArgumentException(
                $"无法识别推理引擎 \"{value}\"。可用取值：{string.Join(" / ", InferenceEngines.All)}" +
                "（auto = 有可用 GPU 就用，gpu = 强制显卡，cpu = 强制 CPU）。");
        }
        return normalized;
    }

    /// <summary>归一化 KV 缓存策略；显式传了未知值时抛错。</summary>
    /// <param name="value">请求中的策略描述；空白表示沿用服务端默认。</param>
    /// <param name="fallback">服务端默认策略（来自 <see cref="AppOptions.KvCache"/>）。</param>
    /// <returns>归一化后的策略标识。</returns>
    /// <exception cref="ArgumentException">描述非空且无法识别时抛出。</exception>
    private static string ResolveKvCache(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        string normalized = KvCachePolicies.Normalize(value, string.Empty);
        if (normalized.Length == 0)
        {
            throw new ArgumentException(
                $"无法识别 KV 缓存策略 \"{value}\"。可用取值：{string.Join(" / ", KvCachePolicies.All)}" +
                "（none = 任务结束即释放，memory = 保留缓冲供下次复用）。");
        }
        return normalized;
    }

    /// <summary>解析提示词风格（中英文名、常见写法均可）。</summary>
    /// <param name="value">风格描述；空白或未知值回退到 <see cref="PromptStyle.Short"/>。</param>
    /// <returns>解析出的提示词风格。</returns>
    public static PromptStyle ParseStyle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return PromptStyle.Short;
        string v = value.Trim().ToLowerInvariant();
        return v switch
        {
            "short" or "简洁" or "简洁指令" or "default" => PromptStyle.Short,
            "official" or "官方" or "官方默认" or "text" => PromptStyle.Official,
            "terminology" or "术语" or "glossary" => PromptStyle.Terminology,
            "style" or "风格" => PromptStyle.Style,
            "delimiters" or "分隔符" or "subtitle" or "字幕" => PromptStyle.Delimiters,
            "background" or "withbackground" or "背景" or "背景信息" or "context" => PromptStyle.WithBackground,
            _ => PromptStyle.Short,
        };
    }

    /// <summary>解析提示词语言；未知值回退到英文。</summary>
    /// <param name="value">语言描述；空白按英文处理。</param>
    /// <returns>解析出的提示词语言。</returns>
    public static PromptLanguage ParsePromptLanguage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return PromptLanguage.English;
        string v = value.Trim().ToLowerInvariant();
        return v switch
        {
            "zh" or "cn" or "chinese" or "中文" or "zh-cn" => PromptLanguage.Chinese,
            _ => PromptLanguage.English,
        };
    }

    #endregion
}

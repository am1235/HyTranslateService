using System.Text;

namespace HyTranslateService.Translation;

/// <summary>
/// 提示词风格，对应 Hy-MT2 官方模型卡给出的多种任务模板。
/// </summary>
public enum PromptStyle
{
    /// <summary>简洁指令（HyMT2Sharp CLI 使用的形式），默认。</summary>
    Short,

    /// <summary>官方「默认翻译」模板。</summary>
    Official,

    /// <summary>官方「术语」模板，配合术语表使用。</summary>
    Terminology,

    /// <summary>官方「风格」模板，配合目标风格使用。</summary>
    Style,

    /// <summary>官方「分隔符」模板，适合字幕 / 代码 / 结构化文本。</summary>
    Delimiters,

    /// <summary>官方「结构化数据 2」模板，配合背景信息使用。</summary>
    WithBackground,
}

/// <summary>提示词语言：英文提示词用英文语言名，中文提示词用中文语言名。</summary>
public enum PromptLanguage
{
    /// <summary>英文提示词。</summary>
    English,

    /// <summary>中文提示词。</summary>
    Chinese,
}

/// <summary>
/// 构造 Hy-MT2 翻译提示词。
/// 模板取自 https://huggingface.co/tencent/Hy-MT2-1.8B 模型卡；语言名必须使用完整名称。
/// </summary>
public static class PromptBuilder
{
    /// <summary>按风格与语言拼接指令句（不含术语块 / 背景块 / 正文）。</summary>
    /// <param name="style">提示词风格。</param>
    /// <param name="language">提示词语言。</param>
    /// <param name="targetName">目标语言的完整名称。</param>
    /// <param name="sourceName">源语言完整名称；未指定源语言时为 null。</param>
    /// <param name="hasBackground">是否携带背景信息。</param>
    /// <param name="hasStyle">是否指定目标风格。</param>
    /// <param name="targetStyle">目标风格描述，可为 null。</param>
    /// <returns>对应模板的指令句文本。</returns>
    private static string BuildInstruction(
        PromptStyle style,
        PromptLanguage language,
        string targetName,
        string? sourceName,
        bool hasBackground,
        bool hasStyle,
        string? targetStyle)
    {
        bool en = language == PromptLanguage.English;
        string fromInline = sourceName is null ? string.Empty : (en ? $" {sourceName}" : $"{sourceName}");

        switch (style)
        {
            case PromptStyle.Official:
                return en
                    ? $"Translate the following{fromInline} text into {targetName}. Note that you should only output the translated result without any additional explanation:"
                    : $"将以下{fromInline}文本翻译成{targetName}，注意只需要输出翻译后的结果，不要额外解释：";

            case PromptStyle.Terminology:
                return en
                    ? $"Translate the following{fromInline} text into {targetName}. Note that you must ONLY output the translated result without any additional explanation:"
                    : $"将以下{fromInline}文本翻译成{targetName}，注意必须只输出翻译后的结果，不要额外解释：";

            case PromptStyle.Style:
                return en
                    ? $"Please translate the following{fromInline} text into {targetName}. Note that the translation style must strictly conform to [{targetStyle ?? "the original"}]:"
                    : $"请将以下{fromInline}文本翻译成{targetName}。注意翻译的风格要严格符合【{targetStyle ?? "原文"}】：";

            case PromptStyle.Delimiters:
                return en
                    ? $"Please accurately translate the following{fromInline} text into {targetName}.\n" +
                      "You must retain the exact same number of delimiters in the translation. Strictly do not omit, escape, or translate these symbols, and pay close attention to their placement."
                    : $"请准确地将以下{fromInline}文本翻译成{targetName}。\n" +
                      "必须保持译文中分隔符的数量完全一致，严格不得省略、转义或翻译这些符号，并注意它们的位置。";

            case PromptStyle.WithBackground:
                return en
                    ? $"Please translate the following{fromInline} text into {targetName}, taking the provided background information into consideration."
                    : $"请将以下{fromInline}文本翻译成{targetName}，并参考提供的背景信息。";

            case PromptStyle.Short:
            default:
                {
                    string instruction = en
                        ? $"Translate the following{fromInline} segment into {targetName}, without additional explanation："
                        : $"将以下{fromInline}文本翻译成{targetName}，注意只需要输出翻译后的结果，不要额外解释：";
                    if (hasStyle && !string.IsNullOrWhiteSpace(targetStyle))
                        instruction += en ? $" The translation style must strictly conform to [{targetStyle}]." : $"翻译风格需严格符合【{targetStyle}】。";
                    if (hasBackground)
                        instruction += en ? " Take the provided background information into consideration." : "请结合提供的背景信息进行翻译。";
                    return instruction;
                }
        }
    }

    #region 公共方法

    /// <summary>解析术语表文本，忽略空行与 # 注释行。</summary>
    /// <param name="glossary">术语表文本，每行 <c>原文=译文</c> 或 <c>原文=&gt;译文</c>；null/空白返回空表。</param>
    /// <returns>术语对列表。</returns>
    public static IReadOnlyList<(string From, string To)> ParseGlossary(string? glossary)
    {
        if (string.IsNullOrWhiteSpace(glossary)) return [];
        var pairs = new List<(string, string)>();
        foreach (string rawLine in glossary.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            int sep = line.IndexOf("=>", StringComparison.Ordinal);
            if (sep < 0) sep = line.IndexOf('=');
            if (sep <= 0) continue;
            string from = line[..sep].Trim().Trim('"', '\'');
            string to = line[(sep + (line[sep] == '=' && sep + 1 < line.Length && line[sep + 1] == '>' ? 2 : 1))..].Trim().Trim('"', '\'');
            if (from.Length == 0 || to.Length == 0) continue;
            pairs.Add((from, to));
        }
        return pairs;
    }

    /// <summary>
    /// 用 Hy-MT2 的对话模板包裹用户消息，生成最终送入分词器的文本。
    /// 必须调用：模型依赖 &lt;|hy_begin_of_sentence|&gt;&lt;|hy_User|&gt;…&lt;|hy_Assistant|&gt; 这段结构，
    /// 直接送裸文本会导致模型立即输出 EOG 或产生无关内容。
    /// </summary>
    /// <param name="userMessage">已拼好的完整翻译指令（含术语块、正文等）。</param>
    /// <returns>套用对话模板后可直接送入分词器的文本。</returns>
    public static string RenderChat(string userMessage)
    {
        var messages = new List<Sdcb.HyMT2Sharp.Model.ChatMessage>
        {
            new("user", userMessage),
        };
        return Sdcb.HyMT2Sharp.Model.ChatTemplate.RenderHunyuanDense(messages);
    }

    /// <summary>
    /// 构造单段翻译提示词。等价于 <see cref="Compile"/> 后 <see cref="PromptTemplate.Render"/>，
    /// 保留它仅为向后兼容；循环内请改用 <see cref="Compile"/> + <see cref="PromptTemplate.Render"/> 以省去重复解析。
    /// </summary>
    /// <param name="text">待翻译正文。</param>
    /// <param name="target">目标语言。</param>
    /// <param name="source">源语言；null 表示由模型自动判断。</param>
    /// <param name="style">提示词风格。</param>
    /// <param name="language">提示词语言。</param>
    /// <param name="glossary">术语表文本，可为 null。</param>
    /// <param name="background">背景信息，可为 null。</param>
    /// <param name="targetStyle">目标风格描述，可为 null。</param>
    /// <returns>完整翻译提示词。</returns>
    public static string Build(
        string text,
        HyMT2Language target,
        HyMT2Language? source,
        PromptStyle style,
        PromptLanguage language,
        string? glossary = null,
        string? background = null,
        string? targetStyle = null)
        => Compile(target, source, style, language, glossary, background, targetStyle).Render(text);

    /// <summary>
    /// 把与正文无关的部分（术语块、背景块、指令句等）一次性拼成可复用模板。
    /// 分段循环中每段只替换正文，避免重复解析术语表与重建指令造成的 O(段数 × 术语行数) 浪费。
    /// </summary>
    /// <param name="target">目标语言。</param>
    /// <param name="source">源语言；null 表示由模型自动判断。</param>
    /// <param name="style">提示词风格。</param>
    /// <param name="language">提示词语言。</param>
    /// <param name="glossary">术语表文本，可为 null。</param>
    /// <param name="background">背景信息，可为 null。</param>
    /// <param name="targetStyle">目标风格描述，可为 null。</param>
    /// <returns>可在分段循环中复用的提示词模板。</returns>
    public static PromptTemplate Compile(
        HyMT2Language target,
        HyMT2Language? source,
        PromptStyle style,
        PromptLanguage language,
        string? glossary = null,
        string? background = null,
        string? targetStyle = null)
    {
        string targetName = language == PromptLanguage.English ? target.EnglishName() : target.ChineseName();
        string? sourceName = source is null
            ? null
            : language == PromptLanguage.English ? source.Value.EnglishName() : source.Value.ChineseName();

        var glossaryPairs = ParseGlossary(glossary);
        bool hasBackground = !string.IsNullOrWhiteSpace(background);
        bool hasStyle = !string.IsNullOrWhiteSpace(targetStyle);

        var sb = new StringBuilder();

        // 统一使用 \n：训练数据与分词器都以 \n 为准，且跨平台结果一致
        void Line(string text = "") => sb.Append(text).Append('\n');

        // 术语块（任意风格下都会前置，官方「术语」模板的固定句式）
        if (glossaryPairs.Count > 0)
        {
            Line(language == PromptLanguage.English ? "Reference the following translations:" : "参考以下翻译：");
            foreach (var (from, to) in glossaryPairs)
                Line("`" + from + (language == PromptLanguage.English ? "` translates to `" : "` 翻译为 `") + to + "`");
            Line();
        }

        // 背景块（WithBackground 风格会在源文前单独成块，避免与源文混淆）
        if (hasBackground && style != PromptStyle.WithBackground)
        {
            Line(language == PromptLanguage.English ? "[Background Information]" : "[背景信息]");
            Line("`" + background!.Trim() + "`");
            Line();
        }

        Line(BuildInstruction(style, language, targetName, sourceName, hasBackground, hasStyle, targetStyle));

        if (style == PromptStyle.WithBackground && hasBackground)
        {
            Line();
            Line(language == PromptLanguage.English ? "[Background Information]" : "[背景信息]");
            Line("`" + background!.Trim() + "`");
            Line();
            Line(language == PromptLanguage.English ? "[Source Text]" : "[源文本]");
        }

        Line();
        // 模板到此为止：正文在 Render 时追加（不带回车，与原文 Build 行为一致）
        return new PromptTemplate(sb.ToString());
    }

    #endregion

    /// <summary>
    /// 一次翻译请求共享的提示词模板。由 <see cref="Compile"/> 生成，<see cref="Render"/> 仅替换正文。
    /// </summary>
    public sealed class PromptTemplate
    {
        /// <summary>模板头部（指令句、术语块、背景块等与正文无关的部分），以换行结尾。</summary>
        private readonly string _header;

        /// <summary>创建模板。</summary>
        /// <param name="header">模板头部文本。</param>
        internal PromptTemplate(string header) => _header = header;

        /// <summary>把共享模板与正文拼成完整提示词。</summary>
        /// <param name="text">待翻译正文。</param>
        /// <returns>完整提示词文本。</returns>
        public string Render(string text) => _header + text;
    }
}

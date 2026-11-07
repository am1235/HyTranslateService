using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace HyTranslateService.Services;

/// <summary>
/// 一个可选界面语言的元信息。全部来自语言包文件自身的自描述头，
/// 而不是在代码里维护清单。
/// </summary>
/// <param name="Code">语言标识，取自自描述头（缺省用文件名），如 zh-CN。</param>
/// <param name="NativeName">母语名称，如「日本語」。</param>
/// <param name="EnglishName">英文名称，如 Japanese。</param>
/// <param name="Order">排序权重，越小越靠前。</param>
/// <param name="FileName">语言包文件名，如 ja-JP.js。</param>
public sealed record UiLanguageInfo(string Code, string NativeName, string EnglishName, int Order, string FileName)
{
    /// <summary>前端动态注入 &lt;script&gt; 时使用的相对 URL。</summary>
    public string Url => $"{LanguageCatalog.UrlPrefix}{FileName}";

    /// <summary>展示名，例如「日本語 (ja-JP)」。</summary>
    public string DisplayName => $"{NativeName} ({Code})";
}

/// <summary>
/// 界面语言目录：启动时扫描 <c>wwwroot/js/i18n/lang/*.js</c>，把目录内容作为唯一真相源。
///
/// 语言包通过首行的自描述头声明自己是谁：
/// <code>/* @i18n-lang {"code":"ja-JP","nativeName":"日本語","englishName":"Japanese","order":4} */</code>
/// 因此「新增一门语言」= 往目录里放一个语言包文件，无需改动任何 C# 或前端代码。
///
/// 注意：这是「界面语言」（UI locale），与翻译的「目标语言」（<see cref="HyMT2Language"/>，38 种）
/// 是完全无关的两个概念。
/// </summary>
public sealed class LanguageCatalog
{
    #region 字段与常量

    /// <summary>语言包目录（相对站点根），前端据此拼接 &lt;script&gt; 地址。</summary>
    public const string UrlPrefix = "./js/i18n/lang/";

    /// <summary>语言包目录（相对 wwwroot 的物理路径）。</summary>
    public const string RelativeDirectory = "js/i18n/lang";

    /// <summary>自描述头：@i18n-lang 后面跟一个单行 JSON 对象。</summary>
    private static readonly Regex MetaPattern = new(@"@i18n-lang\s*(\{.*\})", RegexOptions.Compiled);

    /// <summary>自描述头 JSON 反序列化选项：字段名不区分大小写，容忍语言包手写差异。</summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>扫描/解析失败的告警日志器。</summary>
    private readonly ILogger<LanguageCatalog> _logger;

    #endregion

    #region 公共属性

    /// <summary>wwwroot/js/i18n/lang 的物理绝对路径。</summary>
    public string Directory { get; }

    /// <summary>扫描到的语言清单（按 Order、Code 排序）。</summary>
    public IReadOnlyList<UiLanguageInfo> All { get; private set; }

    #endregion

    /// <summary>构造时完成目录解析与语言清单扫描；清单在服务生命周期内不变。</summary>
    /// <param name="env">Web 宿主环境，用于定位 wwwroot 物理路径。</param>
    /// <param name="logger">日志器。</param>
    public LanguageCatalog(IWebHostEnvironment env, ILogger<LanguageCatalog> logger)
    {
        _logger = logger;
        Directory = ResolveDirectory(env);
        All = Scan();
        _logger.LogInformation("界面语言目录 {Dir}：扫描到 {Count} 种语言（{Codes}）",
            Directory, All.Count, string.Join(", ", All.Select(x => x.Code)));
    }

    #region 私有方法

    /// <summary>扫描语言包目录并按自描述头构建清单；目录缺失或读取失败返回空清单（界面回退 HTML 原文）。</summary>
    private IReadOnlyList<UiLanguageInfo> Scan()
    {
        var list = new List<UiLanguageInfo>();

        if (!System.IO.Directory.Exists(Directory))
        {
            _logger.LogWarning("语言包目录不存在：{Dir}（界面将回退到 HTML 中的原文）", Directory);
            return list;
        }

        string[] files;
        try
        {
            files = System.IO.Directory.GetFiles(Directory, "*.js", SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取语言包目录失败：{Dir}", Directory);
            return list;
        }

        foreach (string file in files.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            string name = Path.GetFileName(file);

            // 跳过 _ 前缀（草稿/禁用）与隐藏文件
            if (name.StartsWith('_') || name.StartsWith('.')) continue;

            var info = TryReadMeta(file, name);
            if (info is not null) list.Add(info);
        }

        return list
            .OrderBy(x => x.Order)
            .ThenBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>读取单个语言包的自描述头；缺头按文件名兜底，解析失败跳过该文件。</summary>
    private UiLanguageInfo? TryReadMeta(string file, string fileName)
    {
        string fallbackCode = Path.GetFileNameWithoutExtension(fileName);

        try
        {
            string text = File.ReadAllText(file);
            Match match = MetaPattern.Match(text);
            if (match.Success)
            {
                var meta = JsonSerializer.Deserialize<LangMeta>(match.Groups[1].Value, JsonOptions);
                if (meta is not null && !string.IsNullOrWhiteSpace(meta.Code))
                {
                    return new UiLanguageInfo(
                        meta.Code.Trim(),
                        string.IsNullOrWhiteSpace(meta.NativeName) ? meta.Code.Trim() : meta.NativeName.Trim(),
                        string.IsNullOrWhiteSpace(meta.EnglishName) ? meta.Code.Trim() : meta.EnglishName.Trim(),
                        meta.Order,
                        fileName);
                }
            }

            // 没有自描述头：用文件名兜底，保证「放进去就能用」
            _logger.LogWarning("语言包 {File} 缺少 @i18n-lang 自描述头，已按文件名推断为 {Code}", fileName, fallbackCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "解析语言包 {File} 失败，已跳过", fileName);
            return null;
        }

        return new UiLanguageInfo(fallbackCode, fallbackCode, fallbackCode, 1000, fileName);
    }

    /// <summary>解析语言包目录物理路径；WebRootPath 不可用时回退到 ContentRootPath/wwwroot。</summary>
    private static string ResolveDirectory(IWebHostEnvironment env)
    {
        string root = !string.IsNullOrWhiteSpace(env.WebRootPath)
            ? env.WebRootPath
            : Path.Combine(env.ContentRootPath, "wwwroot");

        return Path.Combine(root, "js", "i18n", "lang");
    }

    #endregion

    #region 公共方法

    /// <summary>把任意写法（en、ja、日本語…）归一化为清单里的语言标识；无法匹配返回 null。</summary>
    /// <param name="value">用户或浏览器上报的语言写法；null/空白直接视为无法匹配。</param>
    /// <returns>清单中的语言标识（如 zh-CN）；无法匹配返回 null。</returns>
    public string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        string v = value.Trim();

        foreach (var item in All)
        {
            if (string.Equals(item.Code, v, StringComparison.OrdinalIgnoreCase)) return item.Code;
        }

        foreach (var item in All)
        {
            if (string.Equals(item.NativeName, v, StringComparison.OrdinalIgnoreCase)) return item.Code;
            if (string.Equals(item.EnglishName, v, StringComparison.OrdinalIgnoreCase)) return item.Code;
        }

        // 繁简判定必须放在「主语言子标签匹配」之前，否则 zh-Hant / zh-HK 会被清单里
        // 排在前面的 zh-CN 抢走。
        string low = v.Replace('_', '-').ToLowerInvariant();
        if (low == "chinese" || low == "zh" || low == "cn" || low.StartsWith("zh-", StringComparison.Ordinal))
        {
            string[] parts = low.Split('-');
            string alias =
                parts.Contains("hant") ? "zh-TW"
                : parts.Contains("hans") ? "zh-CN"
                : (parts.Contains("tw") || parts.Contains("hk") || parts.Contains("mo")) ? "zh-TW"
                : "zh-CN";

            foreach (var item in All)
            {
                if (string.Equals(item.Code, alias, StringComparison.OrdinalIgnoreCase)) return item.Code;
            }
        }

        // 浏览器常只报 ja、ko 这类主语言简写，最后按主子标签兜底匹配。
        string primary = v.Split(new[] { '-', '_' }, 2)[0];
        if (primary.Length > 0)
        {
            foreach (var item in All)
            {
                if (string.Equals(item.Code.Split('-')[0], primary, StringComparison.OrdinalIgnoreCase))
                    return item.Code;
            }
        }

        return null;
    }

    #endregion

    #region 嵌套类型

    /// <summary>语言包首行自描述头的结构。</summary>
    private sealed class LangMeta
    {
        /// <summary>语言标识；缺失时按文件名兜底。</summary>
        public string Code { get; set; } = "";

        /// <summary>母语名称；缺失时回退为语言标识。</summary>
        public string NativeName { get; set; } = "";

        /// <summary>英文名称；缺失时回退为语言标识。</summary>
        public string EnglishName { get; set; } = "";

        /// <summary>排序权重，越小越靠前；缺失默认 500。</summary>
        [JsonPropertyName("order")]
        public int Order { get; set; } = 500;
    }

    #endregion
}

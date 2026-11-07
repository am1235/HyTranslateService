using System.Text;

namespace HyTranslateService;

/// <summary>
/// Hy-MT2 官方支持的翻译语言（38 项，含中文变体）。
/// 官方清单见 https://huggingface.co/tencent/Hy-MT2-1.8B 。
/// 提示词中必须使用完整语言名（英文提示词用英文名，中文提示词用中文名），不能使用 zh/en 之类的缩写。
/// </summary>
public enum HyMT2Language
{
    /// <summary>中文（zh）</summary>
    Chinese,

    /// <summary>英语（en）</summary>
    English,

    /// <summary>法语（fr）</summary>
    French,

    /// <summary>葡萄牙语（pt）</summary>
    Portuguese,

    /// <summary>西班牙语（es）</summary>
    Spanish,

    /// <summary>日语（ja）</summary>
    Japanese,

    /// <summary>土耳其语（tr）</summary>
    Turkish,

    /// <summary>俄语（ru）</summary>
    Russian,

    /// <summary>阿拉伯语（ar）</summary>
    Arabic,

    /// <summary>韩语（ko）</summary>
    Korean,

    /// <summary>泰语（th）</summary>
    Thai,

    /// <summary>意大利语（it）</summary>
    Italian,

    /// <summary>德语（de）</summary>
    German,

    /// <summary>越南语（vi）</summary>
    Vietnamese,

    /// <summary>马来语（ms）</summary>
    Malay,

    /// <summary>印尼语（id）</summary>
    Indonesian,

    /// <summary>菲律宾语（tl）</summary>
    Filipino,

    /// <summary>印地语（hi）</summary>
    Hindi,

    /// <summary>繁体中文（zh-Hant）</summary>
    TraditionalChinese,

    /// <summary>波兰语（pl）</summary>
    Polish,

    /// <summary>捷克语（cs）</summary>
    Czech,

    /// <summary>荷兰语（nl）</summary>
    Dutch,

    /// <summary>高棉语（km）</summary>
    Khmer,

    /// <summary>缅甸语（my）</summary>
    Burmese,

    /// <summary>波斯语（fa）</summary>
    Persian,

    /// <summary>古吉拉特语（gu）</summary>
    Gujarati,

    /// <summary>乌尔都语（ur）</summary>
    Urdu,

    /// <summary>泰卢固语（te）</summary>
    Telugu,

    /// <summary>马拉地语（mr）</summary>
    Marathi,

    /// <summary>希伯来语（he）</summary>
    Hebrew,

    /// <summary>孟加拉语（bn）</summary>
    Bengali,

    /// <summary>泰米尔语（ta）</summary>
    Tamil,

    /// <summary>乌克兰语（uk）</summary>
    Ukrainian,

    /// <summary>藏语（bo）</summary>
    Tibetan,

    /// <summary>哈萨克语（kk）</summary>
    Kazakh,

    /// <summary>蒙古语（mn）</summary>
    Mongolian,

    /// <summary>维吾尔语（ug）</summary>
    Uyghur,

    /// <summary>粤语（yue）</summary>
    Cantonese,
}

/// <summary>语言的展示信息：缩写代码 / 英文名 / 中文名 / 母语名（autonym）。</summary>
/// <param name="Language">对应的枚举值，同时是稳定 ID 的来源</param>
/// <param name="Code">语言缩写代码，如 zh、en</param>
/// <param name="EnglishName">英文语言名，英文提示词必须使用英文名</param>
/// <param name="ChineseName">中文语言名，中文提示词必须使用中文名</param>
/// <param name="NativeName">该语言自己的母语名（autonym），如 日本語、한국어、Français，用于界面按当前语言展示</param>
public sealed record LanguageDescriptor(HyMT2Language Language, string Code, string EnglishName, string ChineseName, string NativeName)
{
    /// <summary>枚举名，作为稳定 ID。</summary>
    public string Id => Language.ToString();
}

/// <summary>语言表与解析工具。</summary>
public static class HyMT2Languages
{
    /// <summary>全部受支持语言（按官方表格顺序），是别名解析与展示信息的数据源。</summary>
    public static readonly IReadOnlyList<LanguageDescriptor> All = new LanguageDescriptor[]
    {
        new(HyMT2Language.Chinese,            "zh",      "Chinese",             "中文",       "中文"),
        new(HyMT2Language.English,            "en",      "English",             "英语",       "English"),
        new(HyMT2Language.French,             "fr",      "French",              "法语",       "Français"),
        new(HyMT2Language.Portuguese,         "pt",      "Portuguese",          "葡萄牙语",   "Português"),
        new(HyMT2Language.Spanish,            "es",      "Spanish",             "西班牙语",   "Español"),
        new(HyMT2Language.Japanese,           "ja",      "Japanese",            "日语",       "日本語"),
        new(HyMT2Language.Turkish,            "tr",      "Turkish",             "土耳其语",   "Türkçe"),
        new(HyMT2Language.Russian,            "ru",      "Russian",             "俄语",       "Русский"),
        new(HyMT2Language.Arabic,             "ar",      "Arabic",              "阿拉伯语",   "العربية"),
        new(HyMT2Language.Korean,             "ko",      "Korean",              "韩语",       "한국어"),
        new(HyMT2Language.Thai,               "th",      "Thai",                "泰语",       "ไทย"),
        new(HyMT2Language.Italian,            "it",      "Italian",             "意大利语",   "Italiano"),
        new(HyMT2Language.German,             "de",      "German",              "德语",       "Deutsch"),
        new(HyMT2Language.Vietnamese,         "vi",      "Vietnamese",          "越南语",     "Tiếng Việt"),
        new(HyMT2Language.Malay,              "ms",      "Malay",               "马来语",     "Bahasa Melayu"),
        new(HyMT2Language.Indonesian,         "id",      "Indonesian",          "印尼语",     "Bahasa Indonesia"),
        new(HyMT2Language.Filipino,           "tl",      "Filipino",            "菲律宾语",   "Filipino"),
        new(HyMT2Language.Hindi,              "hi",      "Hindi",               "印地语",     "हिन्दी"),
        new(HyMT2Language.TraditionalChinese, "zh-Hant", "Traditional Chinese", "繁体中文",   "繁體中文"),
        new(HyMT2Language.Polish,             "pl",      "Polish",              "波兰语",     "Polski"),
        new(HyMT2Language.Czech,              "cs",      "Czech",               "捷克语",     "Čeština"),
        new(HyMT2Language.Dutch,              "nl",      "Dutch",               "荷兰语",     "Nederlands"),
        new(HyMT2Language.Khmer,              "km",      "Khmer",               "高棉语",     "ខ្មែរ"),
        new(HyMT2Language.Burmese,            "my",      "Burmese",             "缅甸语",     "မြန်မာ"),
        new(HyMT2Language.Persian,            "fa",      "Persian",             "波斯语",     "فارسی"),
        new(HyMT2Language.Gujarati,           "gu",      "Gujarati",            "古吉拉特语", "ગુજરાતી"),
        new(HyMT2Language.Urdu,               "ur",      "Urdu",                "乌尔都语",   "اردو"),
        new(HyMT2Language.Telugu,             "te",      "Telugu",              "泰卢固语",   "తెలుగు"),
        new(HyMT2Language.Marathi,            "mr",      "Marathi",             "马拉地语",   "मराठी"),
        new(HyMT2Language.Hebrew,             "he",      "Hebrew",              "希伯来语",   "עברית"),
        new(HyMT2Language.Bengali,            "bn",      "Bengali",             "孟加拉语",   "বাংলা"),
        new(HyMT2Language.Tamil,              "ta",      "Tamil",               "泰米尔语",   "தமிழ்"),
        new(HyMT2Language.Ukrainian,          "uk",      "Ukrainian",           "乌克兰语",   "Українська"),
        new(HyMT2Language.Tibetan,            "bo",      "Tibetan",             "藏语",       "བོད་སྐད"),
        new(HyMT2Language.Kazakh,             "kk",      "Kazakh",              "哈萨克语",   "Қазақша"),
        new(HyMT2Language.Mongolian,          "mn",      "Mongolian",           "蒙古语",     "Монгол"),
        new(HyMT2Language.Uyghur,             "ug",      "Uyghur",              "维吾尔语",   "ئۇيغۇرچە"),
        new(HyMT2Language.Cantonese,          "yue",     "Cantonese",           "粤语",       "粵語"),
    };

    /// <summary>枚举 → 展示信息查找表，Describe 的后端；未收录枚举值由 Describe 兜底。</summary>
    private static readonly Dictionary<HyMT2Language, LanguageDescriptor> ByEnum = All.ToDictionary(d => d.Language);

    /// <summary>别名总表（键忽略大小写），TryParse 的唯一数据源。</summary>
    private static readonly Dictionary<string, HyMT2Language> Aliases = BuildAliases();

    /// <summary>构造别名表：自动收录每种语言的枚举名、代码、英文名、中文名与去空格英文名，另补手工常见别名。</summary>
    /// <returns>别名 → 枚举值的映射表</returns>
    private static Dictionary<string, HyMT2Language> BuildAliases()
    {
        var map = new Dictionary<string, HyMT2Language>(StringComparer.OrdinalIgnoreCase);
        void Add(string? key, HyMT2Language lang)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            map[key.Trim()] = lang;
        }

        foreach (var d in All)
        {
            Add(d.Id, d.Language);
            Add(d.Code, d.Language);
            Add(d.EnglishName, d.Language);
            Add(d.ChineseName, d.Language);
            Add(d.EnglishName.Replace(" ", ""), d.Language);
        }

        // 常见别名
        Add("zh-cn", HyMT2Language.Chinese);
        Add("zh-hans", HyMT2Language.Chinese);
        Add("简中", HyMT2Language.Chinese);
        Add("简体中文", HyMT2Language.Chinese);
        Add("zh-tw", HyMT2Language.TraditionalChinese);
        Add("zh-hk", HyMT2Language.TraditionalChinese);
        Add("繁中", HyMT2Language.TraditionalChinese);
        Add("farsi", HyMT2Language.Persian);
        Add("tagalog", HyMT2Language.Filipino);
        Add("bahasa", HyMT2Language.Indonesian);
        Add("deutsch", HyMT2Language.German);
        Add("français", HyMT2Language.French);
        Add("español", HyMT2Language.Spanish);
        Add("日本語", HyMT2Language.Japanese);
        Add("한국어", HyMT2Language.Korean);
        Add("русский", HyMT2Language.Russian);
        return map;
    }

    #region 公共方法

    /// <summary>按枚举取展示信息。</summary>
    /// <param name="language">语言枚举值</param>
    /// <returns>对应展示信息；未收录的枚举值兜底返回中文项</returns>
    public static LanguageDescriptor Describe(this HyMT2Language language)
        => ByEnum.TryGetValue(language, out var d) ? d : ByEnum[HyMT2Language.Chinese];

    /// <summary>英文名（用于英文提示词）。</summary>
    /// <param name="language">语言枚举值</param>
    /// <returns>英文语言名，如 Japanese</returns>
    public static string EnglishName(this HyMT2Language language) => language.Describe().EnglishName;

    /// <summary>中文名（用于中文提示词）。</summary>
    /// <param name="language">语言枚举值</param>
    /// <returns>中文语言名，如 日语</returns>
    public static string ChineseName(this HyMT2Language language) => language.Describe().ChineseName;

    /// <summary>母语名（autonym，用于界面按当前语言展示，如 日本語、한국어）。</summary>
    /// <param name="language">语言枚举值</param>
    /// <returns>该语言的母语名，如 日本語</returns>
    public static string NativeName(this HyMT2Language language) => language.Describe().NativeName;

    /// <summary>语言缩写代码。</summary>
    /// <param name="language">语言枚举值</param>
    /// <returns>缩写代码，如 ja</returns>
    public static string Code(this HyMT2Language language) => language.Describe().Code;

    /// <summary>
    /// 宽松解析语言：接受枚举名、zh/en 代码、英文名、中文名、常见别名，大小写与空格不敏感。
    /// </summary>
    /// <param name="value">待解析文本；null 或空白直接失败</param>
    /// <param name="language">成功时输出对应枚举值，失败时输出 Chinese</param>
    /// <returns>是否解析成功</returns>
    public static bool TryParse(string? value, out HyMT2Language language)
    {
        language = HyMT2Language.Chinese;
        if (string.IsNullOrWhiteSpace(value)) return false;
        return Aliases.TryGetValue(value.Trim(), out language);
    }

    /// <summary>用于错误提示与工具描述的紧凑清单。</summary>
    /// <returns>形如 "zh=Chinese, en=English, …" 的单行语言清单</returns>
    public static string CompactList()
    {
        var sb = new StringBuilder();
        foreach (var d in All)
        {
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(d.Code).Append('=').Append(d.EnglishName);
        }
        return sb.ToString();
    }

    #endregion
}

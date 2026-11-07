using HyTranslateService.Translation;

namespace HyTranslateService.Controllers;

/// <summary>
/// 前端提交的翻译请求（字段均为可选/宽松写法，由后端统一归一化）。
/// </summary>
public sealed class TranslationApiRequest
{
    /// <summary>待翻译文本。</summary>
    public string? Text { get; set; }

    /// <summary>来源文件名（前端读取文本文件后附带，仅用于展示）。</summary>
    public string? FileName { get; set; }

    /// <summary>目标语言：代码(zh/en/ja/…)、英文名(Chinese/…)或中文名(中文/…)。</summary>
    public string? TargetLanguage { get; set; }

    /// <summary>源语言；留空表示自动识别。</summary>
    public string? SourceLanguage { get; set; }

    /// <summary>翻译模型：ID / 文件名 / 序号 / auto。</summary>
    public string? Model { get; set; }

    /// <summary>单段最大字符数。</summary>
    public int? SegmentChars { get; set; }

    /// <summary>单段最大生成 token 数（0 或留空 = 自动）。</summary>
    public int? MaxTokensPerSegment { get; set; }

    /// <summary>采样温度；留空 = 用模型默认值。越高越随机。</summary>
    public float? Temperature { get; set; }

    /// <summary>核采样概率阈值；留空 = 用模型默认值。</summary>
    public float? TopP { get; set; }

    /// <summary>Top-K 采样候选数；留空 = 用模型默认值。</summary>
    public int? TopK { get; set; }

    /// <summary>重复惩罚系数；留空 = 用模型默认值。</summary>
    public float? RepetitionPenalty { get; set; }

    /// <summary>随机种子；留空 = 非确定性采样，同文本多次翻译结果可能不同。</summary>
    public int? Seed { get; set; }

    /// <summary>提示词模板：short/official/terminology/style/delimiters/background。</summary>
    public string? PromptStyle { get; set; }

    /// <summary>提示词语言：en / zh。</summary>
    public string? PromptLanguage { get; set; }

    /// <summary>术语表，每行「原文=译文」。</summary>
    public string? Glossary { get; set; }

    /// <summary>背景信息 / 文档上下文。</summary>
    public string? Background { get; set; }

    /// <summary>目标风格描述（如「口语化」「正式书面」）。</summary>
    public string? TargetStyle { get; set; }

    /// <summary>推理引擎：auto（有可用 GPU 就用）/ gpu（强制显卡）/ cpu（强制 CPU）；留空用服务端默认。</summary>
    public string? Engine { get; set; }

    /// <summary>KV 缓存策略：none（任务结束即释放）/ memory（保留缓冲供复用）；留空用服务端默认。</summary>
    public string? KvCache { get; set; }

    /// <summary>转换为内部翻译请求（字段一一映射，Text 为 null 时落为空字符串）。</summary>
    /// <returns>内部翻译请求对象，供任务管理器受理。</returns>
    public TranslationRequest ToTranslationRequest() => new()
    {
        Text = Text ?? string.Empty,
        TargetLanguage = TargetLanguage,
        SourceLanguage = SourceLanguage,
        Model = Model,
        SegmentChars = SegmentChars,
        MaxTokensPerSegment = MaxTokensPerSegment,
        Temperature = Temperature,
        TopP = TopP,
        TopK = TopK,
        RepetitionPenalty = RepetitionPenalty,
        Seed = Seed,
        PromptStyle = PromptStyle,
        PromptLanguage = PromptLanguage,
        Glossary = Glossary,
        Background = Background,
        TargetStyle = TargetStyle,
        Engine = Engine,
        KvCache = KvCache,
        SourceName = FileName,
    };
}

namespace HyTranslateService.Translation;

/// <summary>翻译过程中产出的流式事件。</summary>
public abstract record TranslationEvent
{
    #region 嵌套类型

    /// <summary>分段计划已生成。</summary>
    /// <param name="TotalSegments">片段总数。</param>
    /// <param name="TotalChars">原文总字符数。</param>
    public sealed record Planned(int TotalSegments, int TotalChars) : TranslationEvent;

    /// <summary>开始翻译某一段。</summary>
    /// <param name="Index">片段序号（从 0 起）。</param>
    /// <param name="Total">片段总数。</param>
    /// <param name="Source">本段原文。</param>
    public sealed record SegmentStarted(int Index, int Total, string Source) : TranslationEvent;

    /// <summary>
    /// 某一段的流式增量。为简化与稳健（BPE 合并可能导致已输出字符变化），
    /// <see cref="Text"/> 是该片段「到目前为止的完整可见译文」快照，消费方直接替换即可。
    /// </summary>
    /// <param name="Index">片段序号（从 0 起）。</param>
    /// <param name="Text">该片段当前的完整可见译文快照。</param>
    public sealed record Delta(int Index, string Text) : TranslationEvent;

    /// <summary>某一段翻译完成。</summary>
    /// <param name="Index">片段序号（从 0 起）。</param>
    /// <param name="Source">本段原文。</param>
    /// <param name="Translation">本段最终译文。</param>
    /// <param name="GeneratedTokens">本段生成的 token 数。</param>
    public sealed record SegmentCompleted(int Index, string Source, string Translation, int GeneratedTokens) : TranslationEvent;

    /// <summary>整篇翻译完成。</summary>
    /// <param name="SegmentCount">完成的片段数。</param>
    /// <param name="CharacterCount">原文总字符数。</param>
    /// <param name="ElapsedSeconds">总耗时（秒）。</param>
    /// <param name="GeneratedTokens">本次翻译生成的 token 总数（所有片段之和）。</param>
    public sealed record Completed(int SegmentCount, int CharacterCount, double ElapsedSeconds, int GeneratedTokens) : TranslationEvent;

    #endregion
}

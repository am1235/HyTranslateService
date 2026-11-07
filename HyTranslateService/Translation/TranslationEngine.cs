using System.Runtime.CompilerServices;
using Sdcb.HyMT2Sharp.Model;

namespace HyTranslateService.Translation;

/// <summary>
/// 翻译引擎：负责把「片段列表」逐段送进 Hy-MT2 推理，并以流式事件的形式产出结果。
///
/// 关键点：
/// - 每段开始前 <c>ResetCache()</c>，避免上一段的 KV cache 干扰；
/// - 逐 token 检查取消令牌，因此可以做到「翻译过程中随时终止」；
/// - 检测退化重复（同一小段连续重复 3 次）并提前收尾，避免 1.8B 模型陷入复读。
/// </summary>
public sealed class TranslationEngine
{
    /// <summary>日志器。</summary>
    private readonly ILogger<TranslationEngine> _logger;

    /// <summary>流式 delta 事件的最小发送间隔（毫秒）。</summary>
    private const int DeltaThrottleMs = 120;

    /// <summary>创建翻译引擎。</summary>
    /// <param name="logger">日志器。</param>
    public TranslationEngine(ILogger<TranslationEngine> logger) => _logger = logger;

    /// <summary>执行翻译，流式产出事件。</summary>
    /// <param name="model">已加载的推理模型实例。</param>
    /// <param name="request">已解析的翻译请求（含采样参数、提示词配置）。</param>
    /// <param name="segments">待翻译片段列表。</param>
    /// <returns>按片段推进顺序产出的流式事件序列；每段以 SegmentStarted 开始、SegmentCompleted 结束，末尾为 Completed。</returns>
    public async IAsyncEnumerable<TranslationEvent> TranslateAsync(
        HunyuanDenseModel model,
        ResolvedTranslationRequest request,
        IReadOnlyList<TextSegment> segments,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var sampler = new Sampler(
            request.Sampling,
            model.Config.VocabSize > 0 ? model.Config.VocabSize : model.Tokenizer.VocabSize);

        int contextLimit = model.Config.ContextLength > 0 ? model.Config.ContextLength : 4096;
        int completed = 0;
        int totalTokens = 0;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // 提示词与正文无关的部分（术语表、背景块、指令句）只算一次，分段时仅替换正文
        var promptTemplate = PromptBuilder.Compile(
            request.Target,
            request.Source,
            request.Style,
            request.PromptLanguage,
            request.Glossary,
            request.Background,
            request.TargetStyle);

        foreach (var segment in segments)
        {
            cancellationToken.ThrowIfCancellationRequested();

            yield return new TranslationEvent.SegmentStarted(segment.Index, segments.Count, segment.Text);

            // 零内容片段（全文只有空白时的占位）：没有可翻译的东西，直接原样完成。
            // 其 Prefix / Separator 由任务层原样回填，因此全文纯空白也能逐字符还原。
            if (segment.Text.Length == 0)
            {
                yield return new TranslationEvent.SegmentCompleted(segment.Index, string.Empty, string.Empty, 0);
                completed++;
                continue;
            }

            string prompt = promptTemplate.Render(segment.Text);

            // 必须套用 Hy-MT2 的对话模板，否则模型不会进入「翻译」状态
            string rendered = PromptBuilder.RenderChat(prompt);
            _logger.LogDebug("片段 {Index} 最终提示词：{Prompt}", segment.Index, rendered);

            int[] promptIds = model.Tokenizer.Encode(rendered);
            int maxTokens = request.Sampling.ResolveMaxTokens(segment.Text.Length, contextLimit);

            if (promptIds.Length + maxTokens > contextLimit)
            {
                maxTokens = contextLimit - promptIds.Length - 8;
                if (maxTokens < 16)
                {
                    _logger.LogWarning(
                        "片段 {Index} 的提示词长度 {Prompt} 已接近上下文上限 {Ctx}，跳过该片段",
                        segment.Index, promptIds.Length, contextLimit);
                    yield return new TranslationEvent.SegmentCompleted(segment.Index, segment.Text, string.Empty, 0);
                    completed++;
                    continue;
                }
            }

            model.ResetCache();
            var alignment = model.AlignPrompt(promptIds);
            float[] logits = model.Forward(alignment.Suffix);
            await Task.Yield();

            var generated = new List<int>(Math.Min(maxTokens, 512));
            string visible = string.Empty;
            int tokensThisSegment = 0;

            // delta 节流：按时间窗口合并流式事件（逐 token 全量文本是 O(段长²) 的事件量）。
            // 段末 SegmentCompleted 始终携带最终全量文本，节流不影响正确性。
            var throttle = System.Diagnostics.Stopwatch.StartNew();

            for (int step = 0; step < maxTokens; step++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int token = sampler.Pick(logits, generated);
                if (model.Tokenizer.IsStop(token)) break;

                generated.Add(token);
                tokensThisSegment++;

                string current = model.Tokenizer.DecodeVisible(generated);
                if (!string.Equals(current, visible, StringComparison.Ordinal))
                {
                    visible = current;
                    if (throttle.ElapsedMilliseconds >= DeltaThrottleMs)
                    {
                        throttle.Restart();
                        yield return new TranslationEvent.Delta(segment.Index, visible);
                    }
                }

                if (IsDegenerate(generated))
                {
                    _logger.LogDebug("片段 {Index} 在第 {Step} 步检测到重复输出，提前结束该段", segment.Index, step);
                    break;
                }

                logits = model.Forward([token]);
                await Task.Yield();
            }

            string result = request.TrimOutput ? visible.Trim() : visible;
            completed++;
            totalTokens += tokensThisSegment;

            yield return new TranslationEvent.SegmentCompleted(segment.Index, segment.Text, result, tokensThisSegment);
        }

        stopwatch.Stop();
        yield return new TranslationEvent.Completed(completed, request.Text.Length, stopwatch.Elapsed.TotalSeconds, totalTokens);
    }

    /// <summary>检测退化重复：末尾 3 组相同周期（周期 1~6）视为复读。</summary>
    /// <param name="tokens">本段已生成的 token 序列。</param>
    /// <returns>检测到复读为 true，调用方应提前结束该段。</returns>
    private static bool IsDegenerate(List<int> tokens)
    {
        int n = tokens.Count;
        if (n < 12) return false;

        for (int period = 1; period <= 6; period++)
        {
            int need = period * 3;
            if (n < need) continue;
            bool same = true;
            for (int i = 0; i < period && same; i++)
            {
                int a = tokens[n - need + i];
                if (a != tokens[n - need + period + i] || a != tokens[n - need + 2 * period + i])
                    same = false;
            }
            if (same) return true;
        }
        return false;
    }
}

namespace HyTranslateService.Translation;

/// <summary>
/// 采样参数。默认值取自 Hy-MT2 1.8B 官方推荐
/// （模型卡写作 temperature 0.7 / top_p 0.6 / top_k 20 / repetition_penalty 1.05，
/// 对应本类的 Temperature / TopP / TopK / RepetitionPenalty）。
/// </summary>
public sealed record SamplingOptions
{
    /// <summary>温度。&lt;= 0 表示贪心解码（确定性输出）。</summary>
    public float Temperature { get; init; } = 0.7f;

    /// <summary>核采样阈值。</summary>
    public float TopP { get; init; } = 0.6f;

    /// <summary>Top-K。&lt;= 0 时使用内部候选上限。</summary>
    public int TopK { get; init; } = 20;

    /// <summary>重复惩罚。</summary>
    public float RepetitionPenalty { get; init; } = 1.05f;

    /// <summary>随机种子，&lt; 0 表示随机。</summary>
    public int Seed { get; init; } = -1;

    /// <summary>单个片段的最大生成 token 数，0 表示按片段长度自动推断。</summary>
    public int MaxTokens { get; init; }

    /// <summary>是否贪心解码。</summary>
    public bool IsGreedy => Temperature <= 0f;

    /// <summary>官方推荐参数。</summary>
    public static SamplingOptions Recommended { get; } = new();

    /// <summary>根据片段字符数推断所需的 token 上限。</summary>
    /// <param name="segmentChars">片段字符数。</param>
    /// <param name="contextLimit">模型上下文长度（token 数）。</param>
    /// <returns>max tokens；已为上下文预算留出提示词与安全余量。</returns>
    public int ResolveMaxTokens(int segmentChars, int contextLimit)
    {
        int wanted = MaxTokens > 0
            ? MaxTokens
            : Math.Clamp(segmentChars * 2 + 96, 128, 4096);
        return Math.Max(16, Math.Min(wanted, Math.Max(16, contextLimit - 512)));
    }
}

/// <summary>
/// 词表级采样器。为保证性能，候选数组在构造时一次性分配，选 top-k 时采用有界插入选择（O(vocab) 而非排序）。
/// </summary>
public sealed class Sampler
{
    #region 字段

    /// <summary>未启用 top-k 时的候选上限；候选数组按容量在构造时一次性分配，避免逐步扩容。</summary>
    private const int CandidateCapWithoutTopK = 256;

    /// <summary>采样参数。</summary>
    private readonly SamplingOptions _options;

    /// <summary>随机源；指定种子时用独立 Random，否则共享进程级实例。</summary>
    private readonly Random _rng;

    /// <summary>候选 token 下标（按 logit 降序）。</summary>
    private readonly int[] _candidateIndex;

    /// <summary>候选 token 的 logit 值，与 <see cref="_candidateIndex"/> 平行。</summary>
    private readonly float[] _candidateLogit;

    /// <summary>候选 token 归一化后的采样概率，与 <see cref="_candidateIndex"/> 平行。</summary>
    private readonly double[] _candidateProb;

    /// <summary>按概率降序的候选槽位下标，避免移动候选数组本身。</summary>
    private readonly int[] _order;

    #endregion

    /// <summary>按词表大小一次性分配候选数组。</summary>
    /// <param name="options">采样参数。</param>
    /// <param name="vocabSize">词表大小。</param>
    /// <param name="rng">随机源；null 时按 options.Seed 决定（&gt;= 0 用固定种子，否则共享随机源）。</param>
    public Sampler(SamplingOptions options, int vocabSize, Random? rng = null)
    {
        _options = options;
        _rng = rng ?? (options.Seed >= 0 ? new Random(options.Seed) : Random.Shared);
        int k = options.TopK > 0 ? Math.Min(options.TopK, vocabSize) : Math.Min(CandidateCapWithoutTopK, vocabSize);
        _candidateIndex = new int[k];
        _candidateLogit = new float[k];
        _candidateProb = new double[k];
        _order = new int[k];
    }

    #region 私有方法

    /// <summary>用有界插入法选出 logit 最大的 k 个候选，返回候选个数。</summary>
    /// <param name="logits">全词表 logits，不做修改。</param>
    /// <returns>入选候选个数（不超过容量）。</returns>
    private int SelectTopCandidates(float[] logits)
    {
        int capacity = _candidateIndex.Length;
        int count = 0;

        for (int i = 0; i < logits.Length; i++)
        {
            float value = logits[i];
            if (float.IsNaN(value) || float.IsNegativeInfinity(value)) continue;

            if (count == capacity && value <= _candidateLogit[count - 1]) continue;

            int insertAt = count < capacity ? count : capacity - 1;
            while (insertAt > 0 && _candidateLogit[insertAt - 1] < value)
            {
                if (insertAt < capacity)
                {
                    _candidateLogit[insertAt] = _candidateLogit[insertAt - 1];
                    _candidateIndex[insertAt] = _candidateIndex[insertAt - 1];
                }
                insertAt--;
            }

            _candidateLogit[insertAt] = value;
            _candidateIndex[insertAt] = i;
            if (count < capacity) count++;
        }

        return count;
    }

    /// <summary>应用重复惩罚到已生成 token 的 logits 上。</summary>
    /// <param name="logits">全词表 logits，会被就地修改。</param>
    /// <param name="generated">本段已生成的 token 序列。</param>
    private void ApplyRepetitionPenalty(float[] logits, IReadOnlyList<int> generated)
    {
        float penalty = _options.RepetitionPenalty;
        if (penalty <= 1f || generated.Count == 0) return;

        // 只回看最近 64 个 token，既省时间也避免长文本被过度压制
        int from = Math.Max(0, generated.Count - 64);
        for (int i = from; i < generated.Count; i++)
        {
            int id = generated[i];
            if ((uint)id >= (uint)logits.Length) continue;
            float v = logits[id];
            logits[id] = v > 0 ? v / penalty : v * penalty;
        }
    }

    #endregion

    /// <summary>从 logits 中采样下一个 token（会就地修改 logits 以应用重复惩罚）。</summary>
    /// <param name="logits">全词表 logits，会被就地修改以应用重复惩罚。</param>
    /// <param name="generated">本段已生成的 token 序列，用于重复惩罚。</param>
    /// <returns>采样得到的下一个 token id。</returns>
    public int Pick(float[] logits, IReadOnlyList<int> generated)
    {
        ApplyRepetitionPenalty(logits, generated);

        if (_options.IsGreedy)
        {
            int best = 0;
            for (int i = 1; i < logits.Length; i++)
                if (logits[i] > logits[best]) best = i;
            return best;
        }

        int count = SelectTopCandidates(logits);

        float temperature = Math.Max(0.01f, _options.Temperature);
        double max = double.NegativeInfinity;
        for (int i = 0; i < count; i++)
        {
            _candidateProb[i] = _candidateLogit[i] / temperature;
            if (_candidateProb[i] > max) max = _candidateProb[i];
        }

        double sum = 0;
        for (int i = 0; i < count; i++)
        {
            double p = Math.Exp(_candidateProb[i] - max);
            _candidateProb[i] = p;
            sum += p;
        }
        if (sum <= 0) return _candidateIndex[0];
        for (int i = 0; i < count; i++) _candidateProb[i] /= sum;

        // 按概率降序（插入排序，count <= 256）
        for (int i = 0; i < count; i++) _order[i] = i;
        for (int i = 1; i < count; i++)
        {
            int key = _order[i];
            int j = i - 1;
            while (j >= 0 && _candidateProb[_order[j]] < _candidateProb[key])
            {
                _order[j + 1] = _order[j];
                j--;
            }
            _order[j + 1] = key;
        }

        // 核采样截断
        int keep = count;
        if (_options.TopP is > 0f and < 1f)
        {
            double cumulative = 0;
            for (int i = 0; i < count; i++)
            {
                cumulative += _candidateProb[_order[i]];
                if (cumulative >= _options.TopP)
                {
                    keep = i + 1;
                    break;
                }
            }
            if (keep < 1) keep = 1;
        }

        double total = 0;
        for (int i = 0; i < keep; i++) total += _candidateProb[_order[i]];
        if (total <= 0) return _candidateIndex[_order[0]];

        double target = _rng.NextDouble() * total;
        double acc = 0;
        for (int i = 0; i < keep; i++)
        {
            acc += _candidateProb[_order[i]];
            if (acc >= target) return _candidateIndex[_order[i]];
        }
        return _candidateIndex[_order[keep - 1]];
    }
}

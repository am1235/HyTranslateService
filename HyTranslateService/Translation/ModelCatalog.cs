namespace HyTranslateService.Translation;

/// <summary>
/// 一个官方发布的 Hy-MT2 GGUF 模型条目（Hugging Face tencent 名下，1.8B 稠密架构，HyMT2Sharp 可直接加载）。
/// </summary>
public sealed record OfficialModelInfo
{
    /// <summary>稳定 ID：文件名去掉扩展名（如 Hy-MT2-1.8B-Q4_K_M）。</summary>
    public required string Id { get; init; }

    /// <summary>下拉显示名（如 Hy-MT2 1.8B · Q4_K_M）。</summary>
    public required string DisplayName { get; init; }

    /// <summary>仓库内的文件名（如 Hy-MT2-1.8B-Q4_K_M.gguf），也是下载到 models 目录后的本地文件名。</summary>
    public required string FileName { get; init; }

    /// <summary>Hugging Face 仓库标识（如 tencent/Hy-MT2-1.8B-GGUF）。</summary>
    public required string Repo { get; init; }

    /// <summary>量化标识（如 Q4_K_M / Q2_0C / 1.25Bit）。</summary>
    public required string Quantization { get; init; }

    /// <summary>文件大小（字节，取自 Hugging Face 仓库信息；未下载时作为体积预估展示）。</summary>
    public long ApproximateSizeBytes { get; init; }

    /// <summary>一句话特点说明（展示给用户的选购参考）。</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>质量排序权重（与 <see cref="ModelRegistry.GuessQuantization"/> 保持一致，越大质量越高）。</summary>
    public int QualityRank { get; init; }
}

/// <summary>
/// 官方模型目录：固定 5 个 Hy-MT2 1.8B 量化版本（与 README 模型表一致），
/// 与本地 models 目录扫描结果合并后构成前端「翻译模型」下拉；未下载条目可由 <see cref="ModelDownloader"/> 自动下载。
/// </summary>
public static class ModelCatalog
{
    /// <summary>官方模型清单，按质量从低到高排列（与本地扫描结果的默认排序一致）。</summary>
    public static IReadOnlyList<OfficialModelInfo> All { get; } =
    [
        new OfficialModelInfo
        {
            Id = "Hy-MT2-1.8B-1.25Bit",
            DisplayName = "Hy-MT2 1.8B · 1.25Bit",
            FileName = "Hy-MT2-1.8B-1.25Bit.gguf",
            Repo = "tencent/Hy-MT2-1.8B-1.25Bit-GGUF",
            Quantization = "1.25Bit",
            ApproximateSizeBytes = 461_860_800,
            Description = "最快，decode 速度约提升 74%，质量略有损失",
            QualityRank = 10,
        },
        new OfficialModelInfo
        {
            Id = "Hy-MT2-1.8B-2Bit",
            DisplayName = "Hy-MT2 1.8B · 2Bit",
            FileName = "Hy-MT2-1.8B-2Bit.gguf",
            Repo = "tencent/Hy-MT2-1.8B-2Bit-GGUF",
            Quantization = "Q2_0C",
            ApproximateSizeBytes = 600_534_880,
            Description = "速度与质量的折中选择",
            QualityRank = 20,
        },
        new OfficialModelInfo
        {
            Id = "Hy-MT2-1.8B-Q4_K_M",
            DisplayName = "Hy-MT2 1.8B · Q4_K_M",
            FileName = "Hy-MT2-1.8B-Q4_K_M.gguf",
            Repo = "tencent/Hy-MT2-1.8B-GGUF",
            Quantization = "Q4_K_M",
            ApproximateSizeBytes = 1_133_080_448,
            Description = "速度质量平衡，默认推荐",
            QualityRank = 45,
        },
        new OfficialModelInfo
        {
            Id = "Hy-MT2-1.8B-Q6_K",
            DisplayName = "Hy-MT2 1.8B · Q6_K",
            FileName = "Hy-MT2-1.8B-Q6_K.gguf",
            Repo = "tencent/Hy-MT2-1.8B-GGUF",
            Quantization = "Q6_K",
            ApproximateSizeBytes = 1_474_785_120,
            Description = "更高精度，体积与质量介于 Q4_K_M 与 Q8_0 之间",
            QualityRank = 65,
        },
        new OfficialModelInfo
        {
            Id = "Hy-MT2-1.8B-Q8_0",
            DisplayName = "Hy-MT2 1.8B · Q8_0",
            FileName = "Hy-MT2-1.8B-Q8_0.gguf",
            Repo = "tencent/Hy-MT2-1.8B-GGUF",
            Quantization = "Q8_0",
            ApproximateSizeBytes = 1_908_528_192,
            Description = "最高质量，体积最大",
            QualityRank = 80,
        },
    ];

    /// <summary>默认推荐的官方模型（Q4_K_M，速度质量平衡）。</summary>
    public static OfficialModelInfo Default => All.First(m => m.Quantization == "Q4_K_M");

    /// <summary>
    /// 按模型 ID 或文件名在官方目录中查找（大小写不敏感）。
    /// </summary>
    /// <param name="reference">模型 ID 或文件名；null/空白返回 null。</param>
    /// <returns>命中的目录条目；未命中返回 null。</returns>
    public static OfficialModelInfo? Find(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return null;
        string want = reference.Trim();
        return All.FirstOrDefault(m =>
            string.Equals(m.Id, want, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(m.FileName, want, StringComparison.OrdinalIgnoreCase));
    }
}

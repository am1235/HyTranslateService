using System.Text.RegularExpressions;

namespace HyTranslateService.Translation;

/// <summary>
/// 一个可用翻译模型的元信息（由 ./models 目录扫描得到）。
/// </summary>
public sealed record TranslationModelInfo
{
    /// <summary>稳定 ID：文件名去掉扩展名（如 Hy-MT2-1.8B-Q4_K_M）。</summary>
    public required string Id { get; init; }

    /// <summary>显示名。</summary>
    public required string DisplayName { get; init; }

    /// <summary>文件名。</summary>
    public required string FileName { get; init; }

    /// <summary>绝对路径。</summary>
    public required string FullPath { get; init; }

    /// <summary>文件大小（字节）。</summary>
    public long SizeBytes { get; init; }

    /// <summary>量化标识（从文件名推断，如 Q4_K_M / Q2_0C / 1.25Bit）。</summary>
    public string Quantization { get; init; } = "unknown";

    /// <summary>文件最后写入时间。</summary>
    public DateTime LastWriteTimeUtc { get; init; }

    /// <summary>量化质量/体积排序权重，数值越大质量越高（用于挑默认模型）。</summary>
    public int QualityRank { get; init; }

    /// <summary>人类可读的大小。</summary>
    public string SizeText => FormatSize(SizeBytes);

    /// <summary>把字节数格式化为人类可读的大小（B/KB/MB/GB/TB）。</summary>
    /// <param name="bytes">文件大小（字节）。</param>
    /// <returns>人类可读的大小文本，如 "4.5MB"；1KB 以内保留整数字节。</returns>
    internal static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit <= 1 ? $"{value:0}{units[unit]}" : $"{value:0.##}{units[unit]}";
    }
}

/// <summary>
/// 模型注册表：启动时扫描模型目录，把目录下所有 *.gguf 作为可选的翻译模型暴露出去。
/// </summary>
public sealed class ModelRegistry
{
    #region 字段

    /// <summary>可识别的量化标识与质量排序权重（按 token 匹配文件名，顺序不敏感）。</summary>
    private static readonly (string Token, string Quant, int Rank)[] KnownQuants =
    [
        ("1.25", "1.25Bit", 10),
        ("1.58", "1.58Bit", 12),
        ("STQ1_0", "STQ1_0", 10),
        ("Q2_0C", "Q2_0C", 20),
        ("2BIT", "Q2_0C", 20),
        ("Q2_K", "Q2_K", 22),
        ("Q3_K", "Q3_K", 30),
        ("Q4_0", "Q4_0", 38),
        ("Q4_K_M", "Q4_K_M", 45),
        ("Q4_K_S", "Q4_K_S", 42),
        ("Q4_K", "Q4_K", 44),
        ("Q5_K", "Q5_K", 55),
        ("Q6_K", "Q6_K", 65),
        ("Q8_0", "Q8_0", 80),
        ("BF16", "BF16", 100),
        ("F16", "F16", 98),
        ("F32", "F32", 110),
    ];

    /// <summary>应用配置：模型目录、默认模型等。</summary>
    private readonly AppOptions _options;

    /// <summary>日志器。</summary>
    private readonly ILogger<ModelRegistry> _logger;

    /// <summary>扫描互斥锁：目录枚举较慢，只允许一个 Refresh 并发执行。</summary>
    private readonly SemaphoreSlim _scanLock = new(1, 1);

    /// <summary>当前扫描结果；读取用 lock 保护，避免与 Refresh 的整表替换竞争。</summary>
    private readonly List<TranslationModelInfo> _models = [];

    /// <summary>实际生效的模型目录（绝对路径）。</summary>
    private string _modelsDirectory = string.Empty;

    #endregion

    /// <summary>实际生效的模型目录（绝对路径）。</summary>
    public string ModelsDirectory => _modelsDirectory;

    /// <summary>当前已扫描到的模型（按推荐优先级排序）。</summary>
    public IReadOnlyList<TranslationModelInfo> Models
    {
        get
        {
            lock (_models) return _models.ToArray();
        }
    }

    /// <summary>推荐模型（质量优先，其次体积适中）。</summary>
    public TranslationModelInfo? Recommended
    {
        get
        {
            var list = Models;
            if (list.Count == 0) return null;
            if (!string.IsNullOrWhiteSpace(_options.DefaultModel) && TryResolve(_options.DefaultModel, out var pinned)) return pinned;
            return list
                .OrderByDescending(m => m.QualityRank)
                .ThenByDescending(m => m.SizeBytes)
                .First();
        }
    }

    /// <summary>创建注册表并完成首次模型扫描。</summary>
    /// <param name="options">应用配置，ModelsDirectory 指定模型目录。</param>
    /// <param name="logger">日志器。</param>
    public ModelRegistry(AppOptions options, ILogger<ModelRegistry> logger)
    {
        _options = options;
        _logger = logger;
        _modelsDirectory = ResolveModelsDirectory(options.ModelsDirectory);
        Refresh();
    }

    #region 私有方法

    /// <summary>把模型文件信息转成 <see cref="TranslationModelInfo"/>。</summary>
    /// <param name="fi">gguf 文件信息。</param>
    /// <returns>模型元信息，ID 取文件名去扩展名。</returns>
    private static TranslationModelInfo CreateInfo(FileInfo fi)
    {
        string id = Path.GetFileNameWithoutExtension(fi.Name);
        var (quant, rank) = GuessQuantization(fi.Name);
        return new TranslationModelInfo
        {
            Id = id,
            DisplayName = id,
            FileName = fi.Name,
            FullPath = fi.FullName,
            SizeBytes = fi.Length,
            Quantization = quant,
            QualityRank = rank,
            LastWriteTimeUtc = fi.LastWriteTimeUtc,
        };
    }

    /// <summary>
    /// 定位模型目录：显式配置（<c>--models-dir</c> / <c>MODELS_DIR</c>，绝对路径或相对当前工作目录）优先；
    /// 未配置时用运行目录下的 <c>models</c> 子目录。目录不存在时自动创建（幂等）。
    /// </summary>
    /// <param name="configured">配置的模型目录，可为空。</param>
    /// <returns>实际使用的模型目录绝对路径。</returns>
    private static string ResolveModelsDirectory(string configured)
    {
        string dir = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Directory.GetCurrentDirectory(), "models")
            : Path.GetFullPath(configured, Directory.GetCurrentDirectory());
        Directory.CreateDirectory(dir);
        return dir;
    }

    #endregion

    #region 公共方法

    /// <summary>重新扫描模型目录。</summary>
    /// <returns>本次扫描发现的模型（已按推荐优先级排序）。</returns>
    public IReadOnlyList<TranslationModelInfo> Refresh()
    {
        _scanLock.Wait();
        try
        {
            var found = new List<TranslationModelInfo>();
            if (Directory.Exists(_modelsDirectory))
            {
                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(_modelsDirectory, "*.gguf", SearchOption.AllDirectories);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "枚举模型目录失败: {Dir}", _modelsDirectory);
                    files = [];
                }

                foreach (string file in files)
                {
                    try
                    {
                        var fi = new FileInfo(file);
                        if (!fi.Exists || fi.Length == 0) continue;
                        found.Add(CreateInfo(fi));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "读取模型文件信息失败: {File}", file);
                    }
                }
            }

            found = found
                .OrderByDescending(m => m.QualityRank)
                .ThenByDescending(m => m.SizeBytes)
                .ThenBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            lock (_models)
            {
                _models.Clear();
                _models.AddRange(found);
            }

            _logger.LogInformation("模型扫描完成：{Dir} 下发现 {Count} 个模型", _modelsDirectory, found.Count);
            if (found.Count > 0)
            {
                foreach (var m in found)
                    _logger.LogInformation("  · {Id}  {Quant}  {Size}  {File}", m.Id, m.Quantization, m.SizeText, m.FileName);
            }
            else
            {
                _logger.LogWarning("模型目录中没有可用的 *.gguf 文件，请把 Hy-MT2 GGUF 权重放入 {Dir}", _modelsDirectory);
            }
            return found;
        }
        finally
        {
            _scanLock.Release();
        }
    }

    /// <summary>从文件名推断量化标识与质量权重；先查已知表，再回退正则匹配，仍不行记 unknown。</summary>
    /// <param name="fileName">模型文件名。</param>
    /// <returns>量化标识与质量排序权重。</returns>
    internal static (string Quantization, int Rank) GuessQuantization(string fileName)
    {
        string upper = fileName.ToUpperInvariant();
        foreach (var (token, quant, rank) in KnownQuants)
        {
            if (upper.Contains(token.ToUpperInvariant(), StringComparison.Ordinal))
                return (quant, rank);
        }
        var m = Regex.Match(upper, @"Q\d+(_[A-Z0-9]+)*");
        return m.Success ? (m.Value, 40) : ("unknown", 25);
    }

    /// <summary>
    /// 解析模型引用。支持：ID、文件名、绝对/相对路径、序号（1 起）、
    /// "auto"/"default"/空（取推荐模型）。
    /// </summary>
    /// <param name="reference">模型引用；null/空白视为取推荐模型。</param>
    /// <param name="model">解析成功时的模型元信息；失败时为 null。</param>
    /// <returns>解析成功为 true。</returns>
    public bool TryResolve(string? reference, out TranslationModelInfo model)
    {
        model = null!;
        var list = Models;
        if (list.Count == 0) return false;

        if (string.IsNullOrWhiteSpace(reference) ||
            reference.Equals("auto", StringComparison.OrdinalIgnoreCase) ||
            reference.Equals("default", StringComparison.OrdinalIgnoreCase) ||
            reference.Equals("recommended", StringComparison.OrdinalIgnoreCase))
        {
            var rec = Recommended;
            if (rec is null) return false;
            model = rec;
            return true;
        }

        string want = reference.Trim();

        // 先试序号：数字引用优先按序号解释，避免与同名 ID 冲突时歧义
        if (int.TryParse(want, out int index) && index >= 1 && index <= list.Count)
        {
            model = list[index - 1];
            return true;
        }

        // 含路径分隔符或 .gguf 后缀的引用按路径匹配
        if (want.Contains(Path.DirectorySeparatorChar) || want.Contains(Path.AltDirectorySeparatorChar) || want.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                string full = Path.GetFullPath(want);
                var byPath = list.FirstOrDefault(m => string.Equals(m.FullPath, full, StringComparison.OrdinalIgnoreCase));
                if (byPath is not null)
                {
                    model = byPath;
                    return true;
                }
            }
            catch
            {
                // 忽略非法路径，继续按 ID 匹配
            }
        }

        var exact = list.FirstOrDefault(m => string.Equals(m.Id, want, StringComparison.OrdinalIgnoreCase))
                 ?? list.FirstOrDefault(m => string.Equals(m.FileName, want, StringComparison.OrdinalIgnoreCase))
                 ?? list.FirstOrDefault(m => string.Equals(m.DisplayName, want, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            model = exact;
            return true;
        }

        var partial = list.Where(m =>
            m.Id.Contains(want, StringComparison.OrdinalIgnoreCase) ||
            m.Quantization.Contains(want, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (partial.Length >= 1)
        {
            model = partial[0];
            return true;
        }

        return false;
    }

    /// <summary>解析模型；失败时抛出异常并附上可用清单。</summary>
    /// <param name="reference">模型引用，语义同 <see cref="TryResolve"/>。</param>
    /// <returns>解析出的模型元信息。</returns>
    public TranslationModelInfo Resolve(string? reference)
    {
        if (TryResolve(reference, out var m)) return m;

        // 命中官方目录但尚未下载：给出可操作的引导，而不是笼统的「无法识别」
        if (ModelCatalog.Find(reference) is { } official)
            throw new ArgumentException(
                $"模型 \"{official.Id}\" 尚未下载。请在 WebUI「翻译模型」下拉中选择该模型（会自动从 Hugging Face 下载），" +
                $"或手动把 {official.FileName} 放入模型目录 {_modelsDirectory}。");

        var list = Models;
        if (list.Count == 0)
            throw new InvalidOperationException(
                $"模型目录 {_modelsDirectory} 中还没有已下载的模型。请在 WebUI「翻译模型」下拉中选择一个官方模型（会自动下载），或手动把 GGUF 权重放入该目录。");
        throw new ArgumentException(
            $"无法识别模型 \"{reference}\"。可用模型：" + string.Join(", ", list.Select(x => x.Id)) + "（也可使用 auto 取推荐模型）。");
    }

    #endregion
}

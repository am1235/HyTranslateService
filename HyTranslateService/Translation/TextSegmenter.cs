using System.Collections.Generic;
using System.Text;

namespace HyTranslateService.Translation;

/// <summary>
/// 一个待翻译片段。Prefix 是片段之前的空白，Separator 是之后的空白——
/// 两者不送模型、原样回填，拼接即可逐字符还原原文排版。
/// </summary>
/// <param name="Index">片段序号（从 0 递增）。</param>
/// <param name="Text">送入模型翻译的正文。</param>
/// <param name="Separator">片段之后的空白（换行 / 行尾缩进），原样回填。</param>
public sealed record TextSegment(int Index, string Text, string Separator)
{
    /// <summary>片段在原文中的起始偏移。</summary>
    public int StartOffset { get; init; }

    /// <summary>片段之前的空白（行首缩进 / 文首空行），原样保留，不送入模型。</summary>
    public string Prefix { get; init; } = string.Empty;
}

/// <summary>
/// 长文本自动拆段器（排版保真优先）：
/// 1. 逐行拆分，片段绝不跨行；换行 / 缩进 / 空行不送模型，归入 Prefix / Separator 回填；
/// 2. 行内按句子边界聚合到接近 <c>maxChars</c>；仅单个空格可随文翻译；
/// 3. 仍超长时按逗号/空格软切，最后硬切（不切断代理对）。
/// 不变式：Σ(前缀ᵢ + 文本ᵢ + 分隔ᵢ) === 原文（逐字符相等）。
/// </summary>
public static class TextSegmenter
{
    #region 字段

    /// <summary>句末终结符（硬边界）：其后必断句，空白归 Separator。</summary>
    private static readonly HashSet<char> HardTerminators = new(['。', '．', '！', '？', '；', '…', '!', '?', ';', '⁇', '⁈', '⁉']);

    /// <summary>子句级标点（软边界）：仅超长硬切时优先选作切点。</summary>
    private static readonly HashSet<char> SoftTerminators = new(['，', '、', ',', '：', ':', '—', '–']);

    /// <summary>终结符后可吸收进句读的右引号 / 右括号（避免引号与句子被切开）。</summary>
    private static readonly HashSet<char> TrailingClosers = new(['"', '\'', '”', '’', '」', '』', '）', ')', '》', '】', ']', '}', '〉', '›', '»']);

    #endregion

    #region 私有方法

    /// <summary>
    /// 把单行内容拆为不超过 maxChars 的片段。
    /// 只有单个空格可以作为段内空白随文翻译；其余空白（连续空格 / Tab）一律切开、
    /// 原样挂到片段的 Separator 上，保证译文拼接时逐字符还原。
    /// </summary>
    /// <param name="line">行内容（不含换行符）。</param>
    /// <param name="maxChars">单片段最大字符数。</param>
    /// <returns>片段列表，Separator 携带片段后的结构性空白。</returns>
    private static List<Piece> SplitLine(string line, int maxChars)
    {
        var pieces = new List<Piece>();

        var units = SplitUnits(line);
        var current = new StringBuilder();
        int currentOffset = 0;
        string pendingSep = string.Empty;   // 当前累积片段之后挂着的空白（尚未落定）

        void Flush()
        {
            if (current.Length == 0) return;
            pieces.Add(new Piece(current.ToString(), pendingSep, currentOffset));
            current.Clear();
            pendingSep = string.Empty;
        }

        foreach (var unit in units)
        {
            if (unit.Text.Length > maxChars)
            {
                Flush();
                int relative = 0;
                var hards = HardSplit(unit.Text, maxChars);
                for (int h = 0; h < hards.Count; h++)
                {
                    // 硬切的最后一块接上该单元原本的行内空白
                    string sep = h == hards.Count - 1 ? unit.Separator : hards[h].Separator;
                    pieces.Add(new Piece(hards[h].Text, sep, unit.Offset + relative));
                    relative += hards[h].Text.Length + hards[h].Separator.Length;
                }
                continue;
            }

            if (current.Length == 0)
            {
                currentOffset = unit.Offset;
                current.Append(unit.Text);
                pendingSep = unit.Separator;
                continue;
            }

            bool mergeable = pendingSep == " " && current.Length + 1 + unit.Text.Length <= maxChars;
            if (mergeable)
            {
                current.Append(' ').Append(unit.Text);
                pendingSep = unit.Separator;
            }
            else
            {
                Flush();
                currentOffset = unit.Offset;
                current.Append(unit.Text);
                pendingSep = unit.Separator;
            }
        }

        Flush();
        return pieces;
    }

    /// <summary>
    /// 把行内容切成「句子 + 其后的空白」单元（单行内没有换行符）。
    /// 只有单个空格视为随文空白（留在单元文本内）；连续空格 / Tab 等结构性空白
    /// 一律在此切开、归入 Separator，保证译文拼接时逐字符还原。
    /// </summary>
    /// <param name="block">行内容（不含换行符）。</param>
    /// <returns>单元列表，按出现顺序排列。</returns>
    private static List<Unit> SplitUnits(string block)
    {
        var units = new List<Unit>();
        int i = 0;
        int length = block.Length;

        while (i < length)
        {
            int start = i;
            int j = start;
            int contentEnd = start;   // 内容与分隔的分界点
            bool boundary = false;

            while (j < length)
            {
                char c = block[j];
                if (IsHardTerminator(c))
                {
                    j++;
                    // 吸收连续的终结符与右引号/右括号
                    while (j < length && (IsHardTerminator(block[j]) || IsTrailingCloser(block[j]))) j++;
                    contentEnd = j;
                    boundary = true;   // 句读后必断，其后的空白（无论形状）归 Separator
                    break;
                }
                if (char.IsWhiteSpace(c))
                {
                    int k = j;
                    while (k < length && char.IsWhiteSpace(block[k])) k++;
                    bool soft = k - j == 1 && c == ' ';   // 恰好单个空格：随文空白，不切
                    if (soft)
                    {
                        j = k;
                        continue;
                    }
                    contentEnd = j;    // 空白串之前是内容
                    j = k;             // j 越过整个空白串，串本身归 Separator
                    boundary = true;
                    break;
                }
                j++;
            }

            if (j == start) j = Math.Min(start + 1, length);
            if (!boundary)
            {
                // 扫到行尾仍无边界：内容回退掉结尾空白，空白归 Separator
                contentEnd = j;
                while (contentEnd > start && char.IsWhiteSpace(block[contentEnd - 1])) contentEnd--;
            }

            string content = block[start..contentEnd];
            int k2 = boundary ? j : contentEnd;
            while (k2 < length && char.IsWhiteSpace(block[k2])) k2++;
            string separator = block[contentEnd..k2];

            units.Add(new Unit(content, separator, start));
            i = k2;
        }

        return units;
    }

    /// <summary>对超长单元做软切分，仍不行则硬切；切在空白串上时空白改挂 Separator。</summary>
    /// <param name="text">超长单元文本。</param>
    /// <param name="maxChars">单片段最大字符数。</param>
    /// <returns>切分结果，各片段长度均不超过 maxChars（代理对不会被切断）。</returns>
    private static List<Piece> HardSplit(string text, int maxChars)
    {
        var result = new List<Piece>();
        int start = 0;
        while (start < text.Length)
        {
            int remaining = text.Length - start;
            if (remaining <= maxChars)
            {
                result.Add(new Piece(text[start..], string.Empty, start));
                break;
            }

            int windowEnd = start + maxChars;
            int cut = -1;

            // 优先在句末/子句标点或空白处切
            for (int p = windowEnd; p > start + maxChars / 3; p--)
            {
                char c = text[p - 1];
                if (IsSoftTerminator(c) || char.IsWhiteSpace(c))
                {
                    cut = p;
                    break;
                }
            }

            if (cut <= start) cut = windowEnd;

            // 落在空白串上时：空白不进片段文本，原样挂到 Separator
            int pieceEnd = cut;
            if (char.IsWhiteSpace(text[cut - 1]))
            {
                int runStart = cut;
                while (runStart > start + 1 && char.IsWhiteSpace(text[runStart - 1])) runStart--;
                pieceEnd = runStart;
            }

            // 吸收切点右侧的整段空白，保证下一段不以空白开头
            int sepEnd = cut;
            while (sepEnd < text.Length && char.IsWhiteSpace(text[sepEnd])) sepEnd++;
            string sep = text[pieceEnd..sepEnd];

            // 不要切断代理对（空白不是低代理项，只需考虑无分隔的场景）
            if (sep.Length == 0 && pieceEnd > start && char.IsHighSurrogate(text[pieceEnd - 1]))
            {
                pieceEnd--;
                if (pieceEnd <= start) pieceEnd = Math.Min(start + 2, text.Length);
            }

            result.Add(new Piece(text[start..pieceEnd], sep, start));
            start = sepEnd;
        }
        return result;
    }

    /// <summary>字符是否为句末终结符（硬边界）。</summary>
    private static bool IsHardTerminator(char c) => HardTerminators.Contains(c);

    /// <summary>字符是否为子句级标点（软边界）。</summary>
    private static bool IsSoftTerminator(char c) => SoftTerminators.Contains(c);

    /// <summary>字符是否为句读后可吸收的右引号 / 右括号。</summary>
    private static bool IsTrailingCloser(char c) => TrailingClosers.Contains(c);

    #endregion

    /// <summary>把文本拆成待翻译片段。</summary>
    /// <param name="text">原文。</param>
    /// <param name="maxChars">单片段最大字符数（建议 300~800）。</param>
    /// <returns>片段列表；拼接所有片段（含 Prefix / Separator）可逐字符还原原文。</returns>
    public static IReadOnlyList<TextSegment> Segment(string text, int maxChars)
    {
        ArgumentNullException.ThrowIfNull(text);
        maxChars = Math.Clamp(maxChars, 80, 20000);

        var segments = new List<TextSegment>();
        int index = 0;

        // 自上一个「内容字符」以来累积的空白（上一行的换行 + 中间的空行整行）。
        // 出现下一行内容时结算进上一片段的 Separator；全文尚无内容时并入下一片段的 Prefix。
        string between = string.Empty;
        int lastPieceIdx = -1;   // 上一内容行最后一个片段在 segments 中的下标（Separator 之后仍可追加）

        int i = 0;
        int length = text.Length;
        while (i < length)
        {
            int lineStart = i;
            int nl = text.IndexOf('\n', i);
            int lineEnd = nl < 0 ? length : nl;
            i = nl < 0 ? length : nl + 1;

            // 拆出行首缩进 / 行内容 / 行尾空白（\r 计入行尾空白，从而原样保留 CRLF）
            int lead = 0;
            while (lineStart + lead < lineEnd && char.IsWhiteSpace(text[lineStart + lead])) lead++;
            int trail = 0;
            while (lineEnd - 1 - trail >= lineStart + lead && char.IsWhiteSpace(text[lineEnd - 1 - trail])) trail++;
            int contentStart = lineStart + lead;
            int contentEnd = lineEnd - trail;

            if (contentStart >= contentEnd)
            {
                // 空行 / 纯空白行：整行原样保留，等待挂到相邻片段上
                between += text[lineStart..lineEnd];
                if (nl >= 0) between += "\n";
                continue;
            }

            string leading = text[lineStart..contentStart];
            string prefix;
            if (lastPieceIdx >= 0)
            {
                // 与上一行内容之间隔着的空白（换行 + 空行）归上一片段的 Separator
                var last = segments[lastPieceIdx];
                segments[lastPieceIdx] = last with { Separator = last.Separator + between };
                prefix = leading;
            }
            else
            {
                // 全文第一段内容：文首空白（缩进 / 空行）归本片段的 Prefix
                prefix = between + leading;
            }
            between = string.Empty;

            string content = text[contentStart..contentEnd];
            string trailing = text[contentEnd..lineEnd];
            string eol = nl >= 0 ? "\n" : string.Empty;

            var pieces = SplitLine(content, maxChars);
            for (int p = 0; p < pieces.Count; p++)
            {
                var piece = pieces[p];
                bool isLast = p == pieces.Count - 1;
                var segment = new TextSegment(index++, piece.Text, isLast ? trailing + eol : piece.Separator)
                {
                    StartOffset = contentStart + piece.Offset,
                    Prefix = p == 0 ? prefix : string.Empty,
                };
                segments.Add(segment);
                if (isLast) lastPieceIdx = segments.Count - 1;
            }
        }

        // 结尾残留的空白（最后几行空行）挂到最后一个片段
        if (lastPieceIdx >= 0 && between.Length > 0)
        {
            var last = segments[lastPieceIdx];
            segments[lastPieceIdx] = last with { Separator = last.Separator + between };
        }

        // 全文只有空白（没有任何内容行）：作为一个「零内容」片段原样回填
        if (segments.Count == 0 && text.Length > 0)
        {
            segments.Add(new TextSegment(0, string.Empty, string.Empty)
            {
                StartOffset = 0,
                Prefix = text,
            });
        }

        return segments;
    }

    /// <summary>行内切分产物：片段文本 + 其后的空白 + 在行内的起始偏移。</summary>
    /// <param name="Text">片段文本。</param>
    /// <param name="Separator">片段之后挂着的空白。</param>
    /// <param name="Offset">片段在行内的起始偏移。</param>
    private readonly record struct Piece(string Text, string Separator, int Offset);

    /// <summary>句子级单元：内容 + 其后的空白 + 在行内的起始偏移。</summary>
    /// <param name="Text">单元内容（含随文的单个空格）。</param>
    /// <param name="Separator">单元之后挂着的结构性空白。</param>
    /// <param name="Offset">单元在行内的起始偏移。</param>
    private readonly record struct Unit(string Text, string Separator, int Offset);
}

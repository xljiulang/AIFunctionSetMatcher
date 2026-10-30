using System.Globalization;
using System.Buffers;
using System.Text;

namespace AIFunctionSetMatcher.Internal;

/// <summary>
/// 计算函数名称和描述的词汇相似度。
/// </summary>
internal static class TextSimilarity
{
    /// <summary>
    /// 将函数名称转换为 token 集合。
    /// </summary>
    /// <param name="value">函数名称。</param>
    /// <param name="interner">可选的组内 token 驻留器。</param>
    /// <param name="arena">接收有序 token 的 Schema 组 Arena。</param>
    /// <returns>名称 token 集合。</returns>
    public static TokenSet TokenizeName(
        string value,
        TokenInterner? interner,
        TokenArena arena)
    {
        return Tokenize(value, includeCjkBigrams: false, interner, arena);
    }

    /// <summary>
    /// 使用预计算 token 计算两个函数名称的词汇相似度。
    /// </summary>
    /// <param name="leftTokens">左侧名称 token。</param>
    /// <param name="rightTokens">右侧名称 token。</param>
    /// <returns>Jaccard 或 containment 中的较大值。</returns>
    public static double ComputeNameSimilarity(
        TokenSet leftTokens,
        TokenSet rightTokens)
    {
        var intersection = leftTokens.CountIntersection(rightTokens);
        return Math.Max(
            ComputeJaccard(leftTokens.Count, rightTokens.Count, intersection),
            ComputeContainment(leftTokens.Count, rightTokens.Count, intersection));
    }

    /// <summary>
    /// 计算规范化描述 token 集合的稳定语义指纹。
    /// </summary>
    /// <param name="value">要计算指纹的函数描述。</param>
    /// <param name="workspace">可复用的描述指纹工作区。</param>
    /// <returns>描述语义指纹。</returns>
    public static DescriptionFingerprint ComputeDescriptionFingerprint(
        string value,
        DescriptionFingerprintWorkspace workspace)
    {
        var tokens = new TokenSetBuilder(tokenInterner: null);
        try
        {
            AddNormalizedTokens(
                value,
                includeCjkBigrams: true,
                ref tokens);
            workspace.Reset();
            workspace.Writer.WriteStartArray();
            foreach (var token in tokens.GetSortedTokens())
            {
                workspace.Writer.WriteStringValue(token);
            }

            workspace.Writer.WriteEndArray();
            return workspace.ComputeFingerprint();
        }
        finally
        {
            tokens.Dispose();
        }
    }

    /// <summary>
    /// 根据集合大小计算 Jaccard 相似度。
    /// </summary>
    /// <param name="leftCount">左侧 token 数量。</param>
    /// <param name="rightCount">右侧 token 数量。</param>
    /// <param name="intersection">交集数量。</param>
    /// <returns>Jaccard 相似度。</returns>
    private static double ComputeJaccard(
        int leftCount,
        int rightCount,
        int intersection)
    {
        var union = leftCount + rightCount - intersection;
        return union == 0 ? 0 : (double)intersection / union;
    }

    /// <summary>
    /// 根据集合大小计算 containment 相似度。
    /// </summary>
    /// <param name="leftCount">左侧 token 数量。</param>
    /// <param name="rightCount">右侧 token 数量。</param>
    /// <param name="intersection">交集数量。</param>
    /// <returns>containment 相似度。</returns>
    private static double ComputeContainment(
        int leftCount,
        int rightCount,
        int intersection)
    {
        var smallerCount = Math.Min(leftCount, rightCount);
        return smallerCount == 0 ? 0 : (double)intersection / smallerCount;
    }

    /// <summary>
    /// 将文本转换为大小写无关的 token 集合。
    /// </summary>
    /// <param name="value">要分词的文本。</param>
    /// <param name="includeCjkBigrams">是否将中文连续文本切分为双字 token。</param>
    /// <param name="interner">可选的组内 token 驻留器。</param>
    /// <param name="arena">接收有序 token 的 Schema 组 Arena。</param>
    /// <returns>分词结果。</returns>
    private static TokenSet Tokenize(
        string value,
        bool includeCjkBigrams,
        TokenInterner? interner,
        TokenArena arena)
    {
        var tokens = new TokenSetBuilder(interner);
        try
        {
            AddNormalizedTokens(value, includeCjkBigrams, ref tokens);
            return tokens.Build(arena);
        }
        finally
        {
            tokens.Dispose();
        }
    }

    /// <summary>
    /// 将规范化文本中的标识符 token 添加到集合构建器。
    /// </summary>
    /// <param name="value">要分词的文本。</param>
    /// <param name="includeCjkBigrams">是否将中文连续文本切分为双字 token。</param>
    /// <param name="tokens">接收 token 的集合构建器。</param>
    private static void AddNormalizedTokens(
        string value,
        bool includeCjkBigrams,
        ref TokenSetBuilder tokens)
    {
        var normalized = RequiresNormalization(value)
            ? value.Normalize(NormalizationForm.FormKC)
            : value;
        var text = normalized.AsSpan();
        var wordStart = -1;
        var position = 0;

        foreach (var rune in normalized.EnumerateRunes())
        {
            if (Rune.IsLetterOrDigit(rune))
            {
                if (wordStart < 0)
                {
                    wordStart = position;
                }
            }
            else if (wordStart >= 0)
            {
                FlushWord(
                    text.Slice(wordStart, position - wordStart),
                    includeCjkBigrams,
                    ref tokens);
                wordStart = -1;
            }

            position += rune.Utf16SequenceLength;
        }

        if (wordStart >= 0)
        {
            FlushWord(
                text.Slice(wordStart, position - wordStart),
                includeCjkBigrams,
                ref tokens);
        }
    }

    /// <summary>
    /// 判断文本是否需要进行 NFKC 规范化。
    /// </summary>
    /// <param name="value">要检查的文本。</param>
    /// <returns>包含非 ASCII 字符时返回 <see langword="true"/>。</returns>
    private static bool RequiresNormalization(string value)
    {
        foreach (var character in value)
        {
            if (character > 0x7F)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 将单个连续字母数字片段添加到 token 集合。
    /// </summary>
    /// <param name="word">连续字母数字片段。</param>
    /// <param name="includeCjkBigrams">是否生成中文双字片段。</param>
    /// <param name="tokens">接收 token 的集合构建器。</param>
    private static void FlushWord(
        ReadOnlySpan<char> word,
        bool includeCjkBigrams,
        ref TokenSetBuilder tokens)
    {
        if (word.IsEmpty)
        {
            return;
        }

        if (includeCjkBigrams && ContainsOnlyCjk(word))
        {
            AddCjkBigrams(word, ref tokens);
            return;
        }

        AddIdentifierParts(word, ref tokens);
    }

    /// <summary>
    /// 判断连续文本是否全部由常用 CJK 统一表意文字组成。
    /// </summary>
    /// <param name="word">连续字母数字片段。</param>
    /// <returns>全部字符属于 CJK 统一表意文字时返回 <see langword="true"/>。</returns>
    private static bool ContainsOnlyCjk(ReadOnlySpan<char> word)
    {
        foreach (var rune in word.EnumerateRunes())
        {
            if (!IsCjk(rune))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 将连续中文文本切分为双字 token；单字文本保留单字 token。
    /// </summary>
    /// <param name="word">全部由常用 CJK 字符组成的文本。</param>
    /// <param name="tokens">接收 token 的集合构建器。</param>
    private static void AddCjkBigrams(
        ReadOnlySpan<char> word,
        ref TokenSetBuilder tokens)
    {
        if (word.Length == 1)
        {
            tokens.Add(word);
            return;
        }

        Span<char> bigram = stackalloc char[2];
        for (var index = 0; index < word.Length - 1; index++)
        {
            bigram[0] = word[index];
            bigram[1] = word[index + 1];
            tokens.Add(bigram);
        }
    }

    /// <summary>
    /// 将连续字母数字片段按 camelCase、PascalCase 和数字边界拆分。
    /// </summary>
    /// <param name="word">要拆分的连续字母数字片段。</param>
    /// <param name="tokens">接收拆分结果的集合构建器。</param>
    private static void AddIdentifierParts(
        ReadOnlySpan<char> word,
        ref TokenSetBuilder tokens)
    {
        var start = 0;
        for (var index = 1; index < word.Length; index++)
        {
            var previous = word[index - 1];
            var current = word[index];
            var nextIsLower = index + 1 < word.Length &&
                char.IsLower(word[index + 1]);
            var isBoundary =
                (char.IsLower(previous) && char.IsUpper(current)) ||
                (char.IsLetter(previous) && char.IsDigit(current)) ||
                (char.IsDigit(previous) && char.IsLetter(current)) ||
                (char.IsUpper(previous) && char.IsUpper(current) && nextIsLower);
            if (!isBoundary)
            {
                continue;
            }

            AddToken(word[start..index], ref tokens);
            start = index;
        }

        AddToken(word[start..], ref tokens);
    }

    /// <summary>
    /// 以小写形式将非空 token 添加到集合。
    /// </summary>
    /// <param name="value">要添加的 token 文本。</param>
    /// <param name="tokens">接收 token 的集合构建器。</param>
    private static void AddToken(
        ReadOnlySpan<char> value,
        ref TokenSetBuilder tokens)
    {
        if (value.IsEmpty)
        {
            return;
        }

        char[]? rented = null;
        var lowercase = value.Length <= 256
            ? stackalloc char[value.Length]
            : rented = ArrayPool<char>.Shared.Rent(value.Length);
        try
        {
            if (TryLowerAscii(value, lowercase))
            {
                tokens.Add(lowercase);
                return;
            }
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<char>.Shared.Return(rented);
            }
        }

        var normalized = value.ToString().ToLowerInvariant();
        tokens.Add(normalized);
    }

    /// <summary>
    /// 将纯 ASCII token 无分配地转换为小写形式。
    /// </summary>
    /// <param name="value">要转换的 token。</param>
    /// <param name="lowercase">接收小写字符的目标跨度。</param>
    /// <returns>token 全部为 ASCII 时返回 <see langword="true"/>。</returns>
    private static bool TryLowerAscii(
        ReadOnlySpan<char> value,
        Span<char> lowercase)
    {
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character > 0x7F)
            {
                return false;
            }

            lowercase[index] = char.ToLowerInvariant(character);
        }

        return true;
    }

    /// <summary>
    /// 判断 Unicode 标量是否属于常用 CJK 统一表意文字范围。
    /// </summary>
    /// <param name="rune">要判断的 Unicode 标量。</param>
    /// <returns>属于 CJK 统一表意文字时返回 <see langword="true"/>。</returns>
    private static bool IsCjk(Rune rune)
    {
        var category = Rune.GetUnicodeCategory(rune);
        return category == UnicodeCategory.OtherLetter &&
            rune.Value is >= 0x3400 and <= 0x9FFF;
    }
}

using System.Globalization;
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
    /// <returns>名称 token 集合。</returns>
    public static HashSet<string> TokenizeName(string value)
    {
        return Tokenize(value, includeCjkBigrams: false);
    }

    /// <summary>
    /// 将函数描述转换为 token 集合。
    /// </summary>
    /// <param name="value">函数描述。</param>
    /// <returns>描述 token 集合。</returns>
    public static HashSet<string> TokenizeDescription(string value)
    {
        return Tokenize(value, includeCjkBigrams: true);
    }

    /// <summary>
    /// 使用预计算 token 计算两个函数名称的词汇相似度。
    /// </summary>
    /// <param name="leftTokens">左侧名称 token。</param>
    /// <param name="rightTokens">右侧名称 token。</param>
    /// <returns>Jaccard 或 containment 中的较大值。</returns>
    public static double ComputeNameSimilarity(
        HashSet<string> leftTokens,
        HashSet<string> rightTokens)
    {
        var intersection = CountIntersection(leftTokens, rightTokens);
        return Math.Max(
            ComputeJaccard(leftTokens.Count, rightTokens.Count, intersection),
            ComputeContainment(leftTokens.Count, rightTokens.Count, intersection));
    }

    /// <summary>
    /// 使用预计算 token 计算两个函数描述的 Jaccard 相似度。
    /// </summary>
    /// <param name="leftTokens">左侧描述 token。</param>
    /// <param name="rightTokens">右侧描述 token。</param>
    /// <returns>位于 0 到 1 之间的 Jaccard 相似度。</returns>
    public static double ComputeDescriptionJaccard(
        HashSet<string> leftTokens,
        HashSet<string> rightTokens)
    {
        var intersection = CountIntersection(leftTokens, rightTokens);
        return ComputeJaccard(leftTokens.Count, rightTokens.Count, intersection);
    }

    /// <summary>
    /// 计算两个 token 集合的交集数量。
    /// </summary>
    /// <param name="left">左侧 token 集合。</param>
    /// <param name="right">右侧 token 集合。</param>
    /// <returns>两个集合的交集数量。</returns>
    private static int CountIntersection(HashSet<string> left, HashSet<string> right)
    {
        return left.Count(right.Contains);
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
    /// <returns>分词结果。</returns>
    private static HashSet<string> Tokenize(
        string value,
        bool includeCjkBigrams)
    {
        var normalized = value.Normalize(NormalizationForm.FormKC);
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        var word = new StringBuilder();

        void FlushWord()
        {
            if (word.Length == 0)
            {
                return;
            }

            var wordText = word.ToString();
            if (includeCjkBigrams && wordText.EnumerateRunes().All(IsCjk))
            {
                var runes = wordText.EnumerateRunes().Select(rune => rune.ToString()).ToArray();
                if (runes.Length == 1)
                {
                    tokens.Add(runes[0].ToLowerInvariant());
                }
                else
                {
                    for (var index = 0; index < runes.Length - 1; index++)
                    {
                        tokens.Add((runes[index] + runes[index + 1]).ToLowerInvariant());
                    }
                }
            }
            else
            {
                AddIdentifierParts(wordText, tokens);
            }

            word.Clear();
        }

        foreach (var rune in normalized.EnumerateRunes())
        {
            if (Rune.IsLetterOrDigit(rune))
            {
                word.Append(rune);
            }
            else
            {
                FlushWord();
            }
        }

        FlushWord();
        return tokens;
    }

    /// <summary>
    /// 按 camelCase、PascalCase 和数字边界拆分标识符。
    /// </summary>
    /// <param name="value">要拆分的标识符。</param>
    /// <param name="tokens">接收拆分结果的 token 集合。</param>
    private static void AddIdentifierParts(string value, HashSet<string> tokens)
    {
        var start = 0;
        for (var index = 1; index < value.Length; index++)
        {
            var previous = value[index - 1];
            var current = value[index];
            var nextIsLower = index + 1 < value.Length &&
                char.IsLower(value[index + 1]);
            var isBoundary =
                (char.IsLower(previous) && char.IsUpper(current)) ||
                (char.IsLetter(previous) && char.IsDigit(current)) ||
                (char.IsDigit(previous) && char.IsLetter(current)) ||
                (char.IsUpper(previous) && char.IsUpper(current) && nextIsLower);
            if (!isBoundary)
            {
                continue;
            }

            AddToken(value[start..index], tokens);
            start = index;
        }

        AddToken(value[start..], tokens);
    }

    /// <summary>
    /// 以小写形式将非空 token 添加到集合。
    /// </summary>
    /// <param name="value">要添加的 token。</param>
    /// <param name="tokens">接收 token 的集合。</param>
    private static void AddToken(string value, HashSet<string> tokens)
    {
        if (value.Length > 0)
        {
            tokens.Add(value.ToLowerInvariant());
        }
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

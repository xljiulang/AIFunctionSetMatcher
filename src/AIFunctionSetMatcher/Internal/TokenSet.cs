namespace AIFunctionSetMatcher.Internal;

/// <summary>
/// 保存按序号排序的不可变文本 token 集合。
/// </summary>
internal sealed class TokenSet
{
    private readonly string[] tokens;

    /// <summary>
    /// 获取不包含任何 token 的共享空集合。
    /// </summary>
    public static TokenSet Empty { get; } = new([]);

    /// <summary>
    /// 初始化排序后的文本 token 集合。
    /// </summary>
    /// <param name="sortedTokens">已去重并按序号排序的 token 数组。</param>
    internal TokenSet(string[] sortedTokens)
    {
        tokens = sortedTokens;
    }

    /// <summary>
    /// 获取 token 数量。
    /// </summary>
    public int Count => tokens.Length;

    /// <summary>
    /// 获取按序号排序的 token 只读跨度。
    /// </summary>
    public ReadOnlySpan<string> Tokens => tokens;

    /// <summary>
    /// 使用双指针计算当前集合与另一个集合的交集数量。
    /// </summary>
    /// <param name="other">另一个排序 token 集合。</param>
    /// <returns>两个集合共有的 token 数量。</returns>
    public int CountIntersection(TokenSet other)
    {
        var leftIndex = 0;
        var rightIndex = 0;
        var intersection = 0;
        while (leftIndex < tokens.Length && rightIndex < other.tokens.Length)
        {
            var comparison = string.CompareOrdinal(
                tokens[leftIndex],
                other.tokens[rightIndex]);
            if (comparison == 0)
            {
                intersection++;
                leftIndex++;
                rightIndex++;
            }
            else if (comparison < 0)
            {
                leftIndex++;
            }
            else
            {
                rightIndex++;
            }
        }

        return intersection;
    }
}

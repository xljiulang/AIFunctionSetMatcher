namespace AIFunctionSetMatcher.Internal;

/// <summary>
/// 保存按序号排序的不可变文本 token 集合。
/// </summary>
internal readonly struct TokenSet
{
    private readonly TokenArena? arena;
    private readonly int offset;
    private readonly int count;

    /// <summary>
    /// 初始化 Arena 中的文本 token slice。
    /// </summary>
    /// <param name="arena">保存 token 的冻结 Arena。</param>
    /// <param name="offset">slice 起始偏移。</param>
    /// <param name="count">slice token 数量。</param>
    internal TokenSet(TokenArena arena, int offset, int count)
    {
        this.arena = arena;
        this.offset = offset;
        this.count = count;
    }

    /// <summary>
    /// 获取不包含任何 token 的空集合。
    /// </summary>
    public static TokenSet Empty => default;

    /// <summary>
    /// 获取 token 数量。
    /// </summary>
    public int Count => count;

    /// <summary>
    /// 获取按序号排序的 token 只读跨度。
    /// </summary>
    public ReadOnlySpan<string> Tokens
    {
        get
        {
            return arena is null
                ? []
                : arena.GetSpan(offset, count);
        }
    }

    /// <summary>
    /// 使用双指针计算当前集合与另一个集合的交集数量。
    /// </summary>
    /// <param name="other">另一个排序 token 集合。</param>
    /// <returns>两个集合共有的 token 数量。</returns>
    public int CountIntersection(TokenSet other)
    {
        var leftTokens = Tokens;
        var rightTokens = other.Tokens;
        var leftIndex = 0;
        var rightIndex = 0;
        var intersection = 0;
        while (leftIndex < leftTokens.Length && rightIndex < rightTokens.Length)
        {
            var comparison = string.CompareOrdinal(
                leftTokens[leftIndex],
                rightTokens[rightIndex]);
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

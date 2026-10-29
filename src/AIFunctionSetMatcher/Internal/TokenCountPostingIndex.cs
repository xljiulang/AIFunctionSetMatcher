namespace AIFunctionSetMatcher.Internal;

/// <summary>
/// 使用连续数组保存 token 数量桶到声明下标的索引。
/// </summary>
internal sealed class TokenCountPostingIndex
{
    private readonly IReadOnlyDictionary<int, int> buckets;
    private readonly int[] offsets;
    private readonly int[] postings;

    /// <summary>
    /// 初始化紧凑 token 数量桶索引。
    /// </summary>
    /// <param name="buckets">token 数量到 slot 的映射。</param>
    /// <param name="offsets">每个 slot 及其结束位置的 posting 偏移。</param>
    /// <param name="postings">按 slot 连续保存的声明下标。</param>
    private TokenCountPostingIndex(
        IReadOnlyDictionary<int, int> buckets,
        int[] offsets,
        int[] postings)
    {
        this.buckets = buckets;
        this.offsets = offsets;
        this.postings = postings;
    }

    /// <summary>
    /// 获取不包含数量桶的共享空索引。
    /// </summary>
    public static TokenCountPostingIndex Empty { get; } = new(
        new Dictionary<int, int>(),
        [0],
        []);

    /// <summary>
    /// 获取 token 数量到 slot 的只读映射。
    /// </summary>
    public IReadOnlyDictionary<int, int> Buckets => buckets;

    /// <summary>
    /// 获取指定数量桶的声明下标。
    /// </summary>
    /// <param name="slot">数量桶 slot。</param>
    /// <returns>该 token 数量桶的声明下标跨度。</returns>
    public ReadOnlySpan<int> GetPostings(int slot)
    {
        return postings.AsSpan(
            offsets[slot],
            offsets[slot + 1] - offsets[slot]);
    }

    /// <summary>
    /// 根据匹配档案构建 token 数量桶索引。
    /// </summary>
    /// <param name="profiles">分组内匹配档案。</param>
    /// <param name="useDescriptionTokens">索引描述 token 数量时为 <see langword="true"/>。</param>
    /// <returns>紧凑 token 数量桶索引。</returns>
    public static TokenCountPostingIndex Create(
        IReadOnlyList<DeclarationMatchProfile> profiles,
        bool useDescriptionTokens)
    {
        var hasProfiles = false;
        foreach (var profile in profiles)
        {
            if (profile.HasDescription == useDescriptionTokens)
            {
                hasProfiles = true;
                break;
            }
        }

        if (!hasProfiles)
        {
            return Empty;
        }

        var buckets = new Dictionary<int, int>();
        var counts = new List<int>();
        for (var index = 0; index < profiles.Count; index++)
        {
            var profile = profiles[index];
            if (profile.HasDescription != useDescriptionTokens)
            {
                continue;
            }

            var tokenCount = useDescriptionTokens
                ? profile.DescriptionTokens.Count
                : profile.NameTokens.Count;
            if (!buckets.TryGetValue(tokenCount, out var slot))
            {
                slot = buckets.Count;
                buckets.Add(tokenCount, slot);
                counts.Add(0);
            }

            counts[slot]++;
        }

        var offsets = new int[buckets.Count + 1];
        var cursors = new int[buckets.Count];
        for (var slot = 0; slot < counts.Count; slot++)
        {
            offsets[slot + 1] = offsets[slot] + counts[slot];
            cursors[slot] = offsets[slot];
        }

        var postings = new int[offsets[^1]];
        for (var profileIndex = 0; profileIndex < profiles.Count; profileIndex++)
        {
            var profile = profiles[profileIndex];
            if (profile.HasDescription != useDescriptionTokens)
            {
                continue;
            }

            var tokenCount = useDescriptionTokens
                ? profile.DescriptionTokens.Count
                : profile.NameTokens.Count;
            var slot = buckets[tokenCount];
            postings[cursors[slot]++] = profileIndex;
        }

        return new TokenCountPostingIndex(buckets, offsets, postings);
    }
}

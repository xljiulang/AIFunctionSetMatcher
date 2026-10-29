namespace AIFunctionSetMatcher.Internal;

/// <summary>
/// 使用连续数组保存 token 到声明下标的倒排索引。
/// </summary>
internal sealed class CompactPostingIndex
{
    private readonly IReadOnlyDictionary<string, int> tokenSlots;
    private readonly int[] offsets;
    private readonly int[] postings;

    /// <summary>
    /// 初始化紧凑 token posting 索引。
    /// </summary>
    /// <param name="tokenSlots">token 到 slot 的映射。</param>
    /// <param name="offsets">每个 slot 及其结束位置的 posting 偏移。</param>
    /// <param name="postings">按 slot 连续保存的声明下标。</param>
    private CompactPostingIndex(
        IReadOnlyDictionary<string, int> tokenSlots,
        int[] offsets,
        int[] postings)
    {
        this.tokenSlots = tokenSlots;
        this.offsets = offsets;
        this.postings = postings;
    }

    /// <summary>
    /// 获取不包含 posting 的共享空索引。
    /// </summary>
    public static CompactPostingIndex Empty { get; } = new(
        new Dictionary<string, int>(StringComparer.Ordinal),
        [0],
        []);

    /// <summary>
    /// 获取 token 的 posting 数量。
    /// </summary>
    /// <param name="token">要查询的 token。</param>
    /// <returns>token 对应的 posting 数量；不存在时返回零。</returns>
    public int GetPostingCount(string token)
    {
        if (!tokenSlots.TryGetValue(token, out var slot))
        {
            return 0;
        }

        return offsets[slot + 1] - offsets[slot];
    }

    /// <summary>
    /// 获取 token 对应的 posting 只读跨度。
    /// </summary>
    /// <param name="token">要查询的 token。</param>
    /// <returns>不存在时返回空跨度。</returns>
    public ReadOnlySpan<int> GetPostings(string token)
    {
        if (!tokenSlots.TryGetValue(token, out var slot))
        {
            return [];
        }

        return postings.AsSpan(
            offsets[slot],
            offsets[slot + 1] - offsets[slot]);
    }

    /// <summary>
    /// 根据匹配档案构建紧凑 token posting 索引。
    /// </summary>
    /// <param name="profiles">分组内匹配档案。</param>
    /// <param name="useDescriptionTokens">索引描述 token 时为 <see langword="true"/>。</param>
    /// <returns>紧凑 posting 索引。</returns>
    public static CompactPostingIndex Create(
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

        var tokenSlots = new Dictionary<string, int>(StringComparer.Ordinal);
        var counts = new List<int>();
        for (var profileIndex = 0; profileIndex < profiles.Count; profileIndex++)
        {
            var profile = profiles[profileIndex];
            if (profile.HasDescription != useDescriptionTokens)
            {
                continue;
            }

            var tokens = useDescriptionTokens
                ? profile.DescriptionTokens
                : profile.NameTokens;
            foreach (var token in tokens.Tokens)
            {
                if (!tokenSlots.TryGetValue(token, out var slot))
                {
                    slot = tokenSlots.Count;
                    tokenSlots.Add(token, slot);
                    counts.Add(0);
                }

                counts[slot]++;
            }
        }

        var offsets = new int[tokenSlots.Count + 1];
        var cursors = new int[tokenSlots.Count];
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

            var tokens = useDescriptionTokens
                ? profile.DescriptionTokens
                : profile.NameTokens;
            foreach (var token in tokens.Tokens)
            {
                var slot = tokenSlots[token];
                postings[cursors[slot]++] = profileIndex;
            }
        }

        return new CompactPostingIndex(tokenSlots, offsets, postings);
    }
}

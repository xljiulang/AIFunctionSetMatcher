namespace AIFunctionSetMatcher.Internal;

/// <summary>
/// 保存单个 Schema 分组作为右侧时使用的 token 倒排索引。
/// </summary>
internal sealed class SchemaGroupQueryIndex
{
    private SchemaGroupQueryIndex(
        IReadOnlyDictionary<string, int[]> descriptionPostings,
        IReadOnlyDictionary<int, int[]> descriptionsByTokenCount,
        IReadOnlyDictionary<string, int[]> namePostings,
        IReadOnlyDictionary<int, int[]> namesByTokenCount)
    {
        DescriptionPostings = descriptionPostings;
        DescriptionsByTokenCount = descriptionsByTokenCount;
        NamePostings = namePostings;
        NamesByTokenCount = namesByTokenCount;
    }

    /// <summary>
    /// 获取描述 token 到分组内声明下标的倒排表。
    /// </summary>
    public IReadOnlyDictionary<string, int[]> DescriptionPostings { get; }

    /// <summary>
    /// 获取描述 token 数量到分组内声明下标的索引。
    /// </summary>
    public IReadOnlyDictionary<int, int[]> DescriptionsByTokenCount { get; }

    /// <summary>
    /// 获取无描述声明的名称 token 倒排表。
    /// </summary>
    public IReadOnlyDictionary<string, int[]> NamePostings { get; }

    /// <summary>
    /// 获取无描述声明的名称 token 数量索引。
    /// </summary>
    public IReadOnlyDictionary<int, int[]> NamesByTokenCount { get; }

    /// <summary>
    /// 根据分组匹配档案创建右侧查询索引。
    /// </summary>
    /// <param name="profiles">分组内完整匹配档案。</param>
    /// <returns>不可变右侧查询索引。</returns>
    public static SchemaGroupQueryIndex Create(
        IReadOnlyList<DeclarationMatchProfile> profiles)
    {
        var descriptionCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var descriptionTokenCounts = new Dictionary<int, int>();
        var nameCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var nameTokenCounts = new Dictionary<int, int>();

        for (var index = 0; index < profiles.Count; index++)
        {
            var profile = profiles[index];
            if (profile.HasDescription)
            {
                AddProfile(
                    profile.DescriptionTokens,
                    descriptionCounts,
                    descriptionTokenCounts);
            }
            else
            {
                AddProfile(
                    profile.NameTokens,
                    nameCounts,
                    nameTokenCounts);
            }
        }

        var descriptionPostings = CreatePostingArrays(descriptionCounts);
        var descriptionsByTokenCount = CreateTokenCountArrays(descriptionTokenCounts);
        var namePostings = CreatePostingArrays(nameCounts);
        var namesByTokenCount = CreateTokenCountArrays(nameTokenCounts);
        var descriptionOffsets = new Dictionary<string, int>(StringComparer.Ordinal);
        var descriptionCountOffsets = new Dictionary<int, int>();
        var nameOffsets = new Dictionary<string, int>(StringComparer.Ordinal);
        var nameCountOffsets = new Dictionary<int, int>();

        for (var profileIndex = 0; profileIndex < profiles.Count; profileIndex++)
        {
            var profile = profiles[profileIndex];
            if (profile.HasDescription)
            {
                AddProfileIndex(
                    profileIndex,
                    profile.DescriptionTokens,
                    descriptionPostings,
                    descriptionOffsets,
                    descriptionsByTokenCount,
                    descriptionCountOffsets);
            }
            else
            {
                AddProfileIndex(
                    profileIndex,
                    profile.NameTokens,
                    namePostings,
                    nameOffsets,
                    namesByTokenCount,
                    nameCountOffsets);
            }
        }

        return new SchemaGroupQueryIndex(
            descriptionPostings,
            descriptionsByTokenCount,
            namePostings,
            namesByTokenCount);
    }

    /// <summary>
    /// 统计单个档案的 token posting 数量和 token 数量桶。
    /// </summary>
    /// <param name="tokens">要索引的 token 集合。</param>
    /// <param name="postingCounts">token posting 数量。</param>
    /// <param name="tokenCounts">token 数量桶计数。</param>
    private static void AddProfile(
        TokenSet tokens,
        Dictionary<string, int> postingCounts,
        Dictionary<int, int> tokenCounts)
    {
        foreach (var token in tokens.Tokens)
        {
            postingCounts[token] = postingCounts.GetValueOrDefault(token) + 1;
        }

        tokenCounts[tokens.Count] = tokenCounts.GetValueOrDefault(tokens.Count) + 1;
    }

    /// <summary>
    /// 将单个档案下标写入最终 posting 和 token 数量数组。
    /// </summary>
    /// <param name="profileIndex">分组内档案下标。</param>
    /// <param name="tokens">要索引的 token 集合。</param>
    /// <param name="postings">最终 token posting 数组。</param>
    /// <param name="postingOffsets">token posting 当前写入位置。</param>
    /// <param name="indexesByTokenCount">最终 token 数量数组。</param>
    /// <param name="tokenCountOffsets">token 数量数组当前写入位置。</param>
    private static void AddProfileIndex(
        int profileIndex,
        TokenSet tokens,
        Dictionary<string, int[]> postings,
        Dictionary<string, int> postingOffsets,
        Dictionary<int, int[]> indexesByTokenCount,
        Dictionary<int, int> tokenCountOffsets)
    {
        foreach (var token in tokens.Tokens)
        {
            var offset = postingOffsets.GetValueOrDefault(token);
            postings[token][offset] = profileIndex;
            postingOffsets[token] = offset + 1;
        }

        var countOffset = tokenCountOffsets.GetValueOrDefault(tokens.Count);
        indexesByTokenCount[tokens.Count][countOffset] = profileIndex;
        tokenCountOffsets[tokens.Count] = countOffset + 1;
    }

    /// <summary>
    /// 根据 posting 数量创建最终字符串倒排数组。
    /// </summary>
    /// <param name="postingCounts">token posting 数量。</param>
    /// <returns>已分配未填充的 posting 数组字典。</returns>
    private static Dictionary<string, int[]> CreatePostingArrays(
        Dictionary<string, int> postingCounts)
    {
        var result = new Dictionary<string, int[]>(
            postingCounts.Count,
            StringComparer.Ordinal);
        foreach (var (token, count) in postingCounts)
        {
            result.Add(token, new int[count]);
        }

        return result;
    }

    /// <summary>
    /// 根据数量桶计数创建最终 token 数量数组。
    /// </summary>
    /// <param name="tokenCounts">token 数量桶计数。</param>
    /// <returns>已分配未填充的数量索引字典。</returns>
    private static Dictionary<int, int[]> CreateTokenCountArrays(
        Dictionary<int, int> tokenCounts)
    {
        var result = new Dictionary<int, int[]>(tokenCounts.Count);
        foreach (var (tokenCount, count) in tokenCounts)
        {
            result.Add(tokenCount, new int[count]);
        }

        return result;
    }
}

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
        var descriptionPostings = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var descriptionsByTokenCount = new Dictionary<int, List<int>>();
        var namePostings = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var namesByTokenCount = new Dictionary<int, List<int>>();

        for (var index = 0; index < profiles.Count; index++)
        {
            var profile = profiles[index];
            if (profile.HasDescription)
            {
                AddProfile(
                    index,
                    profile.DescriptionTokens,
                    descriptionPostings,
                    descriptionsByTokenCount);
            }
            else
            {
                AddProfile(
                    index,
                    profile.NameTokens,
                    namePostings,
                    namesByTokenCount);
            }
        }

        return new SchemaGroupQueryIndex(
            FreezePostings(descriptionPostings),
            FreezeTokenCounts(descriptionsByTokenCount),
            FreezePostings(namePostings),
            FreezeTokenCounts(namesByTokenCount));
    }

    /// <summary>
    /// 将单个档案的 token 添加到倒排表和 token 数量索引。
    /// </summary>
    /// <param name="profileIndex">分组内档案下标。</param>
    /// <param name="tokens">要索引的 token 集合。</param>
    /// <param name="postings">token 倒排表。</param>
    /// <param name="indexesByTokenCount">token 数量索引。</param>
    private static void AddProfile(
        int profileIndex,
        TokenSet tokens,
        Dictionary<string, List<int>> postings,
        Dictionary<int, List<int>> indexesByTokenCount)
    {
        foreach (var token in tokens.Tokens)
        {
            if (!postings.TryGetValue(token, out var indexes))
            {
                indexes = [];
                postings.Add(token, indexes);
            }

            indexes.Add(profileIndex);
        }

        if (!indexesByTokenCount.TryGetValue(tokens.Count, out var countIndexes))
        {
            countIndexes = [];
            indexesByTokenCount.Add(tokens.Count, countIndexes);
        }

        countIndexes.Add(profileIndex);
    }

    /// <summary>
    /// 将字符串倒排表冻结为数组。
    /// </summary>
    /// <param name="postings">待冻结倒排表。</param>
    /// <returns>值为数组的只读倒排字典。</returns>
    private static IReadOnlyDictionary<string, int[]> FreezePostings(
        Dictionary<string, List<int>> postings)
    {
        var result = new Dictionary<string, int[]>(
            postings.Count,
            StringComparer.Ordinal);
        foreach (var (token, indexes) in postings)
        {
            result.Add(token, indexes.ToArray());
        }

        return result;
    }

    /// <summary>
    /// 将 token 数量索引冻结为数组。
    /// </summary>
    /// <param name="indexesByTokenCount">待冻结 token 数量索引。</param>
    /// <returns>值为数组的只读数量索引。</returns>
    private static IReadOnlyDictionary<int, int[]> FreezeTokenCounts(
        Dictionary<int, List<int>> indexesByTokenCount)
    {
        var result = new Dictionary<int, int[]>(indexesByTokenCount.Count);
        foreach (var (tokenCount, indexes) in indexesByTokenCount)
        {
            result.Add(tokenCount, indexes.ToArray());
        }

        return result;
    }
}

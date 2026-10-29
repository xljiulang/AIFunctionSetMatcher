namespace AIFunctionSetMatcher.Internal;

/// <summary>
/// 保存单个 Schema 分组作为右侧时使用的紧凑 token 倒排索引。
/// </summary>
internal sealed class SchemaGroupQueryIndex
{
    private SchemaGroupQueryIndex(
        CompactPostingIndex descriptionPostings,
        TokenCountPostingIndex descriptionsByTokenCount,
        CompactPostingIndex namePostings,
        TokenCountPostingIndex namesByTokenCount)
    {
        DescriptionPostings = descriptionPostings;
        DescriptionsByTokenCount = descriptionsByTokenCount;
        NamePostings = namePostings;
        NamesByTokenCount = namesByTokenCount;
    }

    /// <summary>
    /// 获取描述 token 的紧凑倒排索引。
    /// </summary>
    public CompactPostingIndex DescriptionPostings { get; }

    /// <summary>
    /// 获取描述 token 数量桶索引。
    /// </summary>
    public TokenCountPostingIndex DescriptionsByTokenCount { get; }

    /// <summary>
    /// 获取无描述声明的名称 token 紧凑倒排索引。
    /// </summary>
    public CompactPostingIndex NamePostings { get; }

    /// <summary>
    /// 获取无描述声明的名称 token 数量桶索引。
    /// </summary>
    public TokenCountPostingIndex NamesByTokenCount { get; }

    /// <summary>
    /// 根据分组匹配档案创建紧凑右侧查询索引。
    /// </summary>
    /// <param name="profiles">分组内完整匹配档案。</param>
    /// <returns>不可变紧凑右侧查询索引。</returns>
    public static SchemaGroupQueryIndex Create(
        IReadOnlyList<DeclarationMatchProfile> profiles)
    {
        return new SchemaGroupQueryIndex(
            CompactPostingIndex.Create(profiles, useDescriptionTokens: true),
            TokenCountPostingIndex.Create(profiles, useDescriptionTokens: true),
            CompactPostingIndex.Create(profiles, useDescriptionTokens: false),
            TokenCountPostingIndex.Create(profiles, useDescriptionTokens: false));
    }
}

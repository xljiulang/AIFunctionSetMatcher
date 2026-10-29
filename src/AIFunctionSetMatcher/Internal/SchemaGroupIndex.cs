namespace AIFunctionSetMatcher.Internal;

/// <summary>
/// 表示单个 Schema 语义指纹下的声明分组，并按需构建完整匹配档案。
/// </summary>
internal sealed class SchemaGroupIndex
{
    private readonly DeclarationSchemaEntry[] entries;
    private readonly int[] globalIndexes;
    private readonly Lazy<DeclarationMatchProfile[]> profiles;
    private readonly Lazy<SchemaGroupQueryIndex> queryIndex;

    /// <summary>
    /// 初始化 Schema 声明分组。
    /// </summary>
    /// <param name="entries">拥有该 Schema 指纹的全部轻量条目。</param>
    /// <param name="globalIndexes">分组内条目在原始集合中的全局下标。</param>
    public SchemaGroupIndex(
        DeclarationSchemaEntry[] entries,
        int[] globalIndexes)
    {
        this.entries = entries;
        this.globalIndexes = globalIndexes;
        profiles = new Lazy<DeclarationMatchProfile[]>(
            CreateProfiles,
            LazyThreadSafetyMode.ExecutionAndPublication);
        queryIndex = new Lazy<SchemaGroupQueryIndex>(
            () => SchemaGroupQueryIndex.Create(Profiles),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>
    /// 获取分组内声明数量。
    /// </summary>
    public int Count => globalIndexes.Length;

    /// <summary>
    /// 获取分组内全局声明下标。
    /// </summary>
    public IReadOnlyList<int> GlobalIndexes => globalIndexes;

    /// <summary>
    /// 获取线程安全惰性构建的完整匹配档案。
    /// </summary>
    public IReadOnlyList<DeclarationMatchProfile> Profiles => profiles.Value;

    /// <summary>
    /// 获取线程安全惰性构建的右侧查询索引。
    /// </summary>
    public SchemaGroupQueryIndex QueryIndex => queryIndex.Value;

    /// <summary>
    /// 为分组内声明构建完整匹配档案。
    /// </summary>
    /// <returns>与全局下标顺序一致的匹配档案数组。</returns>
    private DeclarationMatchProfile[] CreateProfiles()
    {
        var result = new DeclarationMatchProfile[globalIndexes.Length];
        for (var index = 0; index < globalIndexes.Length; index++)
        {
            result[index] = DeclarationMatchProfile.Create(
                entries[globalIndexes[index]].Declaration);
        }

        return result;
    }
}

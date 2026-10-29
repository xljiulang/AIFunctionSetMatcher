namespace AIFunctionSetMatcher.Internal;

/// <summary>
/// 表示单个 Schema 语义指纹下的声明分组，并按需构建完整匹配档案。
/// </summary>
internal sealed class SchemaGroupIndex
{
    private readonly DeclarationSchemaEntry[] entries;
    private readonly int[] globalIndexes;
    private DeclarationMatchProfile[]? profiles;
    private SchemaGroupQueryIndex? queryIndex;

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
    public IReadOnlyList<DeclarationMatchProfile> Profiles
    {
        get
        {
            var current = Volatile.Read(ref profiles);
            if (current is not null)
            {
                return current;
            }

            lock (globalIndexes)
            {
                current = profiles;
                if (current is null)
                {
                    current = CreateProfiles();
                    Volatile.Write(ref profiles, current);
                }

                return current;
            }
        }
    }

    /// <summary>
    /// 获取线程安全惰性构建的右侧查询索引。
    /// </summary>
    public SchemaGroupQueryIndex QueryIndex
    {
        get
        {
            var current = Volatile.Read(ref queryIndex);
            if (current is not null)
            {
                return current;
            }

            lock (globalIndexes)
            {
                current = queryIndex;
                if (current is null)
                {
                    current = SchemaGroupQueryIndex.Create(Profiles);
                    Volatile.Write(ref queryIndex, current);
                }

                return current;
            }
        }
    }

    /// <summary>
    /// 为分组内声明构建完整匹配档案。
    /// </summary>
    /// <returns>与全局下标顺序一致的匹配档案数组。</returns>
    private DeclarationMatchProfile[] CreateProfiles()
    {
        var result = new DeclarationMatchProfile[globalIndexes.Length];
        var interner = globalIndexes.Length >= 16
            ? new TokenInterner()
            : null;
        var arena = new TokenArena();
        try
        {
            for (var index = 0; index < globalIndexes.Length; index++)
            {
                var entry = entries[globalIndexes[index]];
                result[index] = DeclarationMatchProfile.Create(
                    entry.Declaration,
                    entry.Fingerprint,
                    entry.SchemaByteCount,
                    arena,
                    interner);
            }

            arena.Seal();
            return result;
        }
        catch
        {
            arena.Dispose();
            throw;
        }
    }
}

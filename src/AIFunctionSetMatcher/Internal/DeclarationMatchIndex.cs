using Microsoft.Extensions.AI;

namespace AIFunctionSetMatcher.Internal;

/// <summary>
/// 保存函数声明的轻量 Schema 条目和按需构建的 Schema 分组索引。
/// </summary>
internal sealed class DeclarationMatchIndex
{
    private readonly AIFunctionDeclaration[] declarations;
    private readonly DeclarationSchemaEntry[] entries;

    /// <summary>
    /// 初始化声明匹配索引。
    /// </summary>
    /// <param name="declarations">与全局下标一致的声明数组。</param>
    /// <param name="entries">与全局下标一致的轻量 Schema 条目。</param>
    /// <param name="groupsByFingerprint">Schema 指纹到分组索引的映射。</param>
    /// <param name="maximumSchemaByteCount">单个 Schema 最大 UTF-8 字节数。</param>
    private DeclarationMatchIndex(
        AIFunctionDeclaration[] declarations,
        DeclarationSchemaEntry[] entries,
        IReadOnlyDictionary<SchemaFingerprint, SchemaGroupIndex> groupsByFingerprint,
        int maximumSchemaByteCount)
    {
        this.declarations = declarations;
        this.entries = entries;
        GroupsByFingerprint = groupsByFingerprint;
        MaximumSchemaByteCount = maximumSchemaByteCount;
    }

    /// <summary>
    /// 获取声明数量。
    /// </summary>
    public int Count => declarations.Length;

    /// <summary>
    /// 获取 Schema 语义指纹到惰性分组索引的映射。
    /// </summary>
    public IReadOnlyDictionary<SchemaFingerprint, SchemaGroupIndex> GroupsByFingerprint { get; }

    /// <summary>
    /// 获取当前索引中单个 Schema 的最大 UTF-8 字节数。
    /// </summary>
    public int MaximumSchemaByteCount { get; }

    /// <summary>
    /// 获取指定全局下标的原始函数声明。
    /// </summary>
    /// <param name="index">全局声明下标。</param>
    /// <returns>原始函数声明。</returns>
    public AIFunctionDeclaration GetDeclaration(int index)
    {
        return declarations[index];
    }

    /// <summary>
    /// 获取指定全局下标的轻量 Schema 条目。
    /// </summary>
    /// <param name="index">全局声明下标。</param>
    /// <returns>轻量 Schema 条目。</returns>
    public DeclarationSchemaEntry GetEntry(int index)
    {
        return entries[index];
    }

    /// <summary>
    /// 为声明集合构建轻量 Schema 索引和惰性分组。
    /// </summary>
    /// <param name="declarations">要建立索引的函数声明。</param>
    /// <returns>不可变声明匹配索引。</returns>
    public static DeclarationMatchIndex Create(
        IReadOnlyList<AIFunctionDeclaration> declarations)
    {
        var declarationArray = declarations as AIFunctionDeclaration[] ??
            declarations.ToArray();
        var entries = new DeclarationSchemaEntry[declarationArray.Length];
        var groups = new Dictionary<SchemaFingerprint, List<int>>();
        var maximumSchemaByteCount = 0;
        using var signatureWorkspace = new SchemaSignatureWorkspace();
        for (var index = 0; index < declarationArray.Length; index++)
        {
            var declaration = declarationArray[index];
            var signature = SchemaSignature.Compute(
                declaration,
                signatureWorkspace);
            entries[index] = new DeclarationSchemaEntry(
                declaration,
                signature.Fingerprint,
                signature.Utf8ByteCount);
            maximumSchemaByteCount = Math.Max(
                maximumSchemaByteCount,
                signature.Utf8ByteCount);
            if (!groups.TryGetValue(signature.Fingerprint, out var indexes))
            {
                indexes = [];
                groups.Add(signature.Fingerprint, indexes);
            }

            indexes.Add(index);
        }

        var groupIndexes =
            new Dictionary<SchemaFingerprint, SchemaGroupIndex>(groups.Count);
        foreach (var (fingerprint, indexes) in groups)
        {
            groupIndexes.Add(
                fingerprint,
                new SchemaGroupIndex(entries, indexes.ToArray()));
        }

        return new DeclarationMatchIndex(
            declarationArray,
            entries,
            groupIndexes,
            maximumSchemaByteCount);
    }
}

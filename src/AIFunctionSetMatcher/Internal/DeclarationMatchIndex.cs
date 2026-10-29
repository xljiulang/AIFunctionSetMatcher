using Microsoft.Extensions.AI;

namespace AIFunctionSetMatcher.Internal;

/// <summary>
/// 保存一组成员不可变的函数声明匹配档案和 Schema 分组。
/// </summary>
internal sealed class DeclarationMatchIndex
{
    private DeclarationMatchIndex(
        IReadOnlyList<DeclarationMatchProfile> profiles,
        IReadOnlyDictionary<SchemaFingerprint, List<int>> groupsByFingerprint,
        int maximumSchemaByteCount)
    {
        Profiles = profiles;
        GroupsByFingerprint = groupsByFingerprint;
        MaximumSchemaByteCount = maximumSchemaByteCount;
    }

    /// <summary>
    /// 获取与声明顺序一致的匹配档案。
    /// </summary>
    public IReadOnlyList<DeclarationMatchProfile> Profiles { get; }

    /// <summary>
    /// 获取 Schema 语义指纹到声明下标的只读分组。
    /// </summary>
    public IReadOnlyDictionary<SchemaFingerprint, List<int>> GroupsByFingerprint { get; }

    /// <summary>
    /// 获取当前索引中单个 Schema 的最大 UTF-8 字节数。
    /// </summary>
    public int MaximumSchemaByteCount { get; }

    /// <summary>
    /// 为声明集合构建匹配索引。
    /// </summary>
    /// <param name="declarations">要建立索引的函数声明。</param>
    /// <returns>不可变匹配索引。</returns>
    public static DeclarationMatchIndex Create(
        IReadOnlyList<AIFunctionDeclaration> declarations)
    {
        var profiles = new DeclarationMatchProfile[declarations.Count];
        var groups = new Dictionary<SchemaFingerprint, List<int>>();
        var maximumSchemaByteCount = 0;
        for (var index = 0; index < declarations.Count; index++)
        {
            var profile = DeclarationMatchProfile.Create(declarations[index]);
            profiles[index] = profile;
            maximumSchemaByteCount = Math.Max(
                maximumSchemaByteCount,
                profile.SchemaByteCount);
            if (!groups.TryGetValue(profile.SchemaFingerprint, out var indexes))
            {
                indexes = [];
                groups.Add(profile.SchemaFingerprint, indexes);
            }

            indexes.Add(index);
        }

        return new DeclarationMatchIndex(
            profiles,
            groups,
            maximumSchemaByteCount);
    }
}

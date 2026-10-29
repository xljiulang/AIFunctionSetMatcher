using Microsoft.Extensions.AI;

namespace AIFunctionSetMatcher.Internal;

/// <summary>
/// 保存一组成员不可变的函数声明匹配档案和 Schema 分组。
/// </summary>
internal sealed class DeclarationMatchIndex
{
    private DeclarationMatchIndex(
        IReadOnlyList<DeclarationMatchProfile> profiles,
        IReadOnlyDictionary<string, IReadOnlyList<int>> groupsBySchemaSignature)
    {
        Profiles = profiles;
        GroupsBySchemaSignature = groupsBySchemaSignature;
    }

    /// <summary>
    /// 获取与声明顺序一致的匹配档案。
    /// </summary>
    public IReadOnlyList<DeclarationMatchProfile> Profiles { get; }

    /// <summary>
    /// 获取 Schema 语义签名到声明下标的只读分组。
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<int>> GroupsBySchemaSignature { get; }

    /// <summary>
    /// 为声明集合构建匹配索引。
    /// </summary>
    /// <param name="declarations">要建立索引的函数声明。</param>
    /// <returns>不可变匹配索引。</returns>
    public static DeclarationMatchIndex Create(
        IReadOnlyList<AIFunctionDeclaration> declarations)
    {
        var profiles = new DeclarationMatchProfile[declarations.Count];
        var groups = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (var index = 0; index < declarations.Count; index++)
        {
            var profile = DeclarationMatchProfile.Create(declarations[index]);
            profiles[index] = profile;
            if (!groups.TryGetValue(profile.SchemaSignature, out var indexes))
            {
                indexes = [];
                groups.Add(profile.SchemaSignature, indexes);
            }

            indexes.Add(index);
        }

        var readOnlyGroups =
            new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal);
        foreach (var (signature, indexes) in groups)
        {
            readOnlyGroups.Add(signature, indexes.ToArray());
        }

        return new DeclarationMatchIndex(profiles, readOnlyGroups);
    }
}

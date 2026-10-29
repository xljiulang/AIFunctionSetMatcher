using Microsoft.Extensions.AI;

namespace AIFunctionSetMatcher.Internal;

/// <summary>
/// 保存单个函数声明在一次匹配调用中复用的预计算数据。
/// </summary>
internal sealed class DeclarationMatchProfile
{
    private DeclarationMatchProfile(
        AIFunctionDeclaration declaration,
        SchemaFingerprint schemaFingerprint,
        int schemaByteCount,
        TokenSet nameTokens,
        TokenSet descriptionTokens,
        bool hasDescription)
    {
        Declaration = declaration;
        SchemaFingerprint = schemaFingerprint;
        SchemaByteCount = schemaByteCount;
        NameTokens = nameTokens;
        DescriptionTokens = descriptionTokens;
        HasDescription = hasDescription;
    }

    /// <summary>
    /// 获取原始函数声明。
    /// </summary>
    public AIFunctionDeclaration Declaration { get; }

    /// <summary>
    /// 获取规范化后的 Schema 语义指纹。
    /// </summary>
    public SchemaFingerprint SchemaFingerprint { get; }

    /// <summary>
    /// 获取输入 Schema 原始文本的 UTF-8 字节数。
    /// </summary>
    public int SchemaByteCount { get; }

    /// <summary>
    /// 获取预计算的名称 token 集合。
    /// </summary>
    public TokenSet NameTokens { get; }

    /// <summary>
    /// 获取预计算的描述 token 集合。
    /// </summary>
    public TokenSet DescriptionTokens { get; }

    /// <summary>
    /// 获取描述是否存在且包含非空白字符。
    /// </summary>
    public bool HasDescription { get; }

    /// <summary>
    /// 为函数声明创建预计算匹配档案。
    /// </summary>
    /// <param name="declaration">要建立档案的函数声明。</param>
    /// <param name="schemaFingerprint">已计算的 Schema 语义指纹。</param>
    /// <param name="schemaByteCount">已计算的 Schema UTF-8 字节数。</param>
    /// <param name="interner">可选的组内 token 驻留器。</param>
    /// <param name="arena">接收名称和描述 token 的 Schema 组 Arena。</param>
    /// <returns>新的匹配档案。</returns>
    public static DeclarationMatchProfile Create(
        AIFunctionDeclaration declaration,
        SchemaFingerprint schemaFingerprint,
        int schemaByteCount,
        TokenArena arena,
        TokenInterner? interner = null)
    {
        var name = declaration.Name;
        var description = declaration.Description;
        var hasDescription = !string.IsNullOrWhiteSpace(description);
        return new DeclarationMatchProfile(
            declaration,
            schemaFingerprint,
            schemaByteCount,
            TextSimilarity.TokenizeName(name, interner, arena),
            hasDescription
                ? TextSimilarity.TokenizeDescription(description, interner, arena)
                : TokenSet.Empty,
            hasDescription);
    }

    /// <summary>
    /// 计算 Schema 签名并为函数声明创建匹配档案。
    /// </summary>
    /// <param name="declaration">要建立档案的函数声明。</param>
    /// <param name="interner">可选的组内 token 驻留器。</param>
    /// <returns>新的匹配档案。</returns>
    public static DeclarationMatchProfile Create(
        AIFunctionDeclaration declaration,
        TokenInterner? interner = null)
    {
        var schemaSignature = SchemaSignature.Compute(declaration);
        var arena = new TokenArena();
        try
        {
            var profile = Create(
                declaration,
                schemaSignature.Fingerprint,
                schemaSignature.Utf8ByteCount,
                arena,
                interner);
            arena.Seal();
            return profile;
        }
        catch
        {
            arena.Dispose();
            throw;
        }
    }
}

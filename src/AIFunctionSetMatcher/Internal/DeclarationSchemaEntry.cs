using Microsoft.Extensions.AI;

namespace AIFunctionSetMatcher.Internal;

/// <summary>
/// 保存函数声明的轻量 Schema 条目。
/// </summary>
/// <param name="Declaration">原始函数声明。</param>
/// <param name="Fingerprint">Schema 语义指纹。</param>
/// <param name="DescriptionFingerprint">描述语义指纹。</param>
/// <param name="SchemaByteCount">Schema 原始 UTF-8 字节数。</param>
internal readonly record struct DeclarationSchemaEntry(
    AIFunctionDeclaration Declaration,
    SchemaFingerprint Fingerprint,
    DescriptionFingerprint DescriptionFingerprint,
    int SchemaByteCount);

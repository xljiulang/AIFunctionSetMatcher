using Microsoft.Extensions.AI;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace AIFunctionSetMatcher.Internal;

/// <summary>
/// 在一次匹配索引构建中复用原始 Schema 字节完全相同的语义签名。
/// </summary>
internal sealed class SchemaSignatureCache
{
    private readonly List<CacheEntry> _entries = [];

    /// <summary>
    /// 获取已有 Schema 的语义签名，或计算并缓存新的语义签名。
    /// </summary>
    /// <param name="declaration">要计算 Schema 语义签名的函数声明。</param>
    /// <param name="workspace">可复用的 canonical UTF-8 工作区。</param>
    /// <returns>Schema 语义签名结果。</returns>
    public SchemaSignatureResult GetOrCompute(
        AIFunctionDeclaration declaration,
        SchemaSignatureWorkspace workspace)
    {
        var schema = declaration.JsonSchema;
        var rawSchema = JsonMarshal.GetRawUtf8Value(schema);
        foreach (var entry in _entries)
        {
            var cachedRawSchema = JsonMarshal.GetRawUtf8Value(entry.Schema);
            if (rawSchema.SequenceEqual(cachedRawSchema))
            {
                return entry.Signature;
            }
        }

        var signature = SchemaSignature.Compute(declaration, workspace);
        _entries.Add(new CacheEntry(schema, signature));
        return signature;
    }

    /// <summary>
    /// 保存原始 Schema 和已计算签名。
    /// </summary>
    /// <param name="Schema">原始 Schema 元素。</param>
    /// <param name="Signature">Schema 语义签名结果。</param>
    private sealed record CacheEntry(
        JsonElement Schema,
        SchemaSignatureResult Signature);
}

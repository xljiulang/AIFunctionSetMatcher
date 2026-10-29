namespace AIFunctionSetMatcher.Internal;

/// <summary>
/// 保存 Schema 语义签名和原始 UTF-8 字节数。
/// </summary>
/// <param name="Value">十六进制 SHA-256 语义签名。</param>
/// <param name="Utf8ByteCount">Schema 原始文本的 UTF-8 字节数。</param>
internal readonly record struct SchemaSignatureResult(
    string Value,
    int Utf8ByteCount);

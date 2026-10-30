using System.Security.Cryptography;
using System.Text.Json;

namespace AIFunctionSetMatcher.Internal;

/// <summary>
/// 保存可在多个描述指纹计算之间复用的池化 UTF-8 写入器。
/// </summary>
internal sealed class DescriptionFingerprintWorkspace : IDisposable
{
    private const int InitialCapacity = 1_024;
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = false,
        SkipValidation = true,
    };

    private bool _disposed;

    /// <summary>
    /// 初始化描述指纹规范化工作区。
    /// </summary>
    public DescriptionFingerprintWorkspace()
    {
        Buffer = new PooledUtf8BufferWriter(InitialCapacity);
        Writer = new Utf8JsonWriter(Buffer, WriterOptions);
    }

    /// <summary>
    /// 获取池化 UTF-8 缓冲区。
    /// </summary>
    public PooledUtf8BufferWriter Buffer { get; }

    /// <summary>
    /// 获取可复位的 UTF-8 JSON 写入器。
    /// </summary>
    public Utf8JsonWriter Writer { get; }

    /// <summary>
    /// 计算当前工作区中规范化 token 集合的 SHA-256 指纹。
    /// </summary>
    /// <returns>描述语义指纹。</returns>
    public DescriptionFingerprint ComputeFingerprint()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Writer.Flush();
        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(Buffer.WrittenSpan, digest);
        return DescriptionFingerprint.FromDigest(digest);
    }

    /// <summary>
    /// 在计算下一个描述前重置缓冲区和写入器。
    /// </summary>
    public void Reset()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Writer.Flush();
        Writer.Reset();
        Buffer.Reset();
    }

    /// <summary>
    /// 释放 JSON 写入器和池化缓冲区。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Writer.Dispose();
        Buffer.Dispose();
    }
}

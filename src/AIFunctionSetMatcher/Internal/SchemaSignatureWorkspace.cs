using System.Text.Json;

namespace AIFunctionSetMatcher.Internal;

/// <summary>
/// 保存可在多个 Schema 指纹计算之间复用的池化 UTF-8 写入器。
/// </summary>
internal sealed class SchemaSignatureWorkspace : IDisposable
{
    private const int InitialCapacity = 4_096;
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = false,
        SkipValidation = true,
    };

    private bool disposed;

    /// <summary>
    /// 初始化 Schema canonical UTF-8 工作区。
    /// </summary>
    public SchemaSignatureWorkspace()
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
    /// 在计算下一个 Schema 前重置缓冲区和写入器。
    /// </summary>
    public void Reset()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Writer.Flush();
        Writer.Reset();
        Buffer.Reset();
    }

    /// <summary>
    /// 释放 JSON 写入器和池化缓冲区。
    /// </summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Writer.Dispose();
        Buffer.Dispose();
    }
}

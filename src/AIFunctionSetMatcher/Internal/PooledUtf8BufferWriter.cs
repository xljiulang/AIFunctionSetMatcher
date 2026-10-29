using System.Buffers;

namespace AIFunctionSetMatcher.Internal;

/// <summary>
/// 使用共享数组池承载 UTF-8 输出的缓冲区写入器。
/// </summary>
internal sealed class PooledUtf8BufferWriter : IBufferWriter<byte>, IDisposable
{
    private byte[] buffer;
    private int writtenCount;
    private bool disposed;

    /// <summary>
    /// 初始化池化 UTF-8 缓冲区写入器。
    /// </summary>
    /// <param name="initialCapacity">首次租用的最小字节数。</param>
    public PooledUtf8BufferWriter(int initialCapacity = 4_096)
    {
        buffer = ArrayPool<byte>.Shared.Rent(Math.Max(initialCapacity, 1));
    }

    /// <summary>
    /// 获取已写入的 UTF-8 字节。
    /// </summary>
    public ReadOnlySpan<byte> WrittenSpan => buffer.AsSpan(0, writtenCount);

    /// <summary>
    /// 通知缓冲区写入器已写入指定数量的字节。
    /// </summary>
    /// <param name="count">已写入的字节数。</param>
    public void Advance(int count)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (writtenCount > buffer.Length - count)
        {
            throw new InvalidOperationException("UTF-8 写入计数超过缓冲区容量。");
        }

        writtenCount += count;
    }

    /// <summary>
    /// 获取指定最小大小的可写内存区域。
    /// </summary>
    /// <param name="sizeHint">所需的最小字节数。</param>
    /// <returns>可写内存区域。</returns>
    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        EnsureCapacity(sizeHint);
        return buffer.AsMemory(writtenCount);
    }

    /// <summary>
    /// 获取指定最小大小的可写字节跨度。
    /// </summary>
    /// <param name="sizeHint">所需的最小字节数。</param>
    /// <returns>可写字节跨度。</returns>
    public Span<byte> GetSpan(int sizeHint = 0)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        EnsureCapacity(sizeHint);
        return buffer.AsSpan(writtenCount);
    }

    /// <summary>
    /// 将缓冲区归还共享数组池。
    /// </summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        ArrayPool<byte>.Shared.Return(buffer);
        buffer = [];
        writtenCount = 0;
    }

    /// <summary>
    /// 确保缓冲区在当前位置之后拥有指定数量的可写字节。
    /// </summary>
    /// <param name="sizeHint">所需的最小可写字节数；为零时至少保留一个字节。</param>
    private void EnsureCapacity(int sizeHint)
    {
        var requiredSize = Math.Max(sizeHint, 1);
        if (buffer.Length - writtenCount >= requiredSize)
        {
            return;
        }

        var newCapacity = Math.Max(
            buffer.Length * 2,
            checked(writtenCount + requiredSize));
        var replacement = ArrayPool<byte>.Shared.Rent(newCapacity);
        buffer.AsSpan(0, writtenCount).CopyTo(replacement);
        ArrayPool<byte>.Shared.Return(buffer);
        buffer = replacement;
    }
}

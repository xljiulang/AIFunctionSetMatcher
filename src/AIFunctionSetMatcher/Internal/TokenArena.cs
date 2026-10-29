using System.Buffers;

namespace AIFunctionSetMatcher.Internal;

/// <summary>
/// 在单个 Schema 组构建阶段聚合 token 引用，并在完成后冻结为连续数组。
/// </summary>
internal sealed class TokenArena : IDisposable
{
    private string[]? scratch;
    private string[]? storage;
    private int count;
    private bool disposed;

    /// <summary>
    /// 将一组有序 token 追加到 Arena。
    /// </summary>
    /// <param name="tokens">要追加的 token 引用。</param>
    /// <returns>该组 token 在 Arena 中的起始偏移。</returns>
    public int Append(ReadOnlySpan<string> tokens)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (storage is not null)
        {
            throw new InvalidOperationException("已冻结的 TokenArena 不能继续追加。");
        }

        EnsureCapacity(tokens.Length);
        var offset = count;
        tokens.CopyTo(scratch.AsSpan(count));
        count += tokens.Length;
        return offset;
    }

    /// <summary>
    /// 冻结 Arena 并创建精确大小的持久 token 数组。
    /// </summary>
    public void Seal()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (storage is not null)
        {
            return;
        }

        storage = new string[count];
        if (count > 0)
        {
            scratch.AsSpan(0, count).CopyTo(storage);
        }

        ReleaseScratch();
    }

    /// <summary>
    /// 获取冻结后的 token slice。
    /// </summary>
    /// <param name="offset">slice 起始偏移。</param>
    /// <param name="length">slice token 数量。</param>
    /// <returns>对应 token 只读跨度。</returns>
    public ReadOnlySpan<string> GetSpan(int offset, int length)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (storage is null)
        {
            throw new InvalidOperationException("TokenArena 尚未冻结。");
        }

        return storage.AsSpan(offset, length);
    }

    /// <summary>
    /// 释放尚未冻结的池化临时数组。
    /// </summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        ReleaseScratch();
    }

    /// <summary>
    /// 确保临时数组至少还能追加指定数量的 token。
    /// </summary>
    /// <param name="additionalCount">要追加的 token 数量。</param>
    private void EnsureCapacity(int additionalCount)
    {
        var required = checked(count + additionalCount);
        if (scratch is not null && scratch.Length >= required)
        {
            return;
        }

        var replacement = ArrayPool<string>.Shared.Rent(
            Math.Max(Math.Max(required, 16), (scratch?.Length ?? 0) * 2));
        if (scratch is not null)
        {
            scratch.AsSpan(0, count).CopyTo(replacement);
            ArrayPool<string>.Shared.Return(scratch);
        }

        scratch = replacement;
    }

    /// <summary>
    /// 将池化临时数组归还共享数组池。
    /// </summary>
    private void ReleaseScratch()
    {
        if (scratch is null)
        {
            return;
        }

        ArrayPool<string>.Shared.Return(scratch);
        scratch = null;
    }
}

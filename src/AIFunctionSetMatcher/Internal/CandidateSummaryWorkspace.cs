using System.Buffers;

namespace AIFunctionSetMatcher.Internal;

/// <summary>
/// 保存唯一候选摘要扫描所需的池化工作数组。
/// </summary>
internal sealed class CandidateSummaryWorkspace : IDisposable
{
    private bool disposed;

    /// <summary>
    /// 根据 Schema 分组尺寸租用候选摘要数组。
    /// </summary>
    /// <param name="rowCount">分组左侧声明数量。</param>
    /// <param name="columnCount">分组右侧声明数量。</param>
    public CandidateSummaryWorkspace(int rowCount, int columnCount)
    {
        CandidateCounts = ArrayPool<int>.Shared.Rent(rowCount);
        UniqueCandidateByLeft = ArrayPool<int>.Shared.Rent(rowCount);
        RightUniqueCounts = ArrayPool<int>.Shared.Rent(columnCount);
    }

    /// <summary>
    /// 获取每个左侧声明的合格候选数量。
    /// </summary>
    public int[] CandidateCounts { get; }

    /// <summary>
    /// 获取唯一候选左侧声明对应的右侧分组内下标。
    /// </summary>
    public int[] UniqueCandidateByLeft { get; }

    /// <summary>
    /// 获取每个右侧声明被唯一候选左侧引用的次数。
    /// </summary>
    public int[] RightUniqueCounts { get; }

    /// <summary>
    /// 将候选摘要工作数组归还共享数组池。
    /// </summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        ArrayPool<int>.Shared.Return(CandidateCounts);
        ArrayPool<int>.Shared.Return(UniqueCandidateByLeft);
        ArrayPool<int>.Shared.Return(RightUniqueCounts);
    }
}

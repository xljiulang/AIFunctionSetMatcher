using System.Buffers;

namespace AIFunctionSetMatcher.Internal;

/// <summary>
/// 保存稀疏候选扫描使用的池化数组。
/// </summary>
internal sealed class SparseCandidateWorkspace : IDisposable
{
    private readonly int[] candidateMarks;
    private int rowStamp;
    private bool disposed;

    /// <summary>
    /// 根据分组尺寸租用稀疏候选工作数组。
    /// </summary>
    /// <param name="rowCount">分组左侧声明数量。</param>
    /// <param name="columnCount">分组右侧声明数量。</param>
    /// <param name="pivotCapacity">左侧 token 锚点最大数量。</param>
    public SparseCandidateWorkspace(
        int rowCount,
        int columnCount,
        int pivotCapacity)
    {
        CandidateIndexes = ArrayPool<int>.Shared.Rent(columnCount);
        candidateMarks = ArrayPool<int>.Shared.Rent(columnCount);
        PivotIndexes = ArrayPool<int>.Shared.Rent(Math.Max(pivotCapacity, 1));
        CandidateCounts = ArrayPool<int>.Shared.Rent(rowCount);
        UniqueCandidateByLeft = ArrayPool<int>.Shared.Rent(rowCount);
        RightUniqueCounts = ArrayPool<int>.Shared.Rent(columnCount);
    }

    /// <summary>
    /// 获取当前左侧声明的候选右侧下标数组。
    /// </summary>
    public int[] CandidateIndexes { get; }

    /// <summary>
    /// 获取当前左侧声明选中的 token 锚点下标数组。
    /// </summary>
    public int[] PivotIndexes { get; }

    /// <summary>
    /// 获取当前左侧声明的候选数量。
    /// </summary>
    public int CandidateCount { get; private set; }

    /// <summary>
    /// 获取每个左侧声明的真实合格候选数量。
    /// </summary>
    public int[] CandidateCounts { get; }

    /// <summary>
    /// 获取唯一真实候选左侧声明对应的右侧下标。
    /// </summary>
    public int[] UniqueCandidateByLeft { get; }

    /// <summary>
    /// 获取每个右侧声明被唯一候选左侧引用的次数。
    /// </summary>
    public int[] RightUniqueCounts { get; }

    /// <summary>
    /// 为新的左侧声明开始一轮候选收集。
    /// </summary>
    public void BeginRow()
    {
        if (rowStamp == int.MaxValue)
        {
            Array.Clear(candidateMarks);
            rowStamp = 0;
        }

        rowStamp++;
        CandidateCount = 0;
    }

    /// <summary>
    /// 添加候选右侧下标并忽略本轮重复项。
    /// </summary>
    /// <param name="candidateIndex">候选右侧分组内下标。</param>
    public void AddCandidate(int candidateIndex)
    {
        if (candidateMarks[candidateIndex] == rowStamp)
        {
            return;
        }

        candidateMarks[candidateIndex] = rowStamp;
        CandidateIndexes[CandidateCount++] = candidateIndex;
    }

    /// <summary>
    /// 将全部稀疏候选工作数组归还共享数组池。
    /// </summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        ArrayPool<int>.Shared.Return(CandidateIndexes);
        ArrayPool<int>.Shared.Return(candidateMarks);
        ArrayPool<int>.Shared.Return(PivotIndexes);
        ArrayPool<int>.Shared.Return(CandidateCounts);
        ArrayPool<int>.Shared.Return(UniqueCandidateByLeft);
        ArrayPool<int>.Shared.Return(RightUniqueCounts);
    }
}

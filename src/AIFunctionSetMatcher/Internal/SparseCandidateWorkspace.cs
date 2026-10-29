using System.Buffers;

namespace AIFunctionSetMatcher.Internal;

/// <summary>
/// 保存稀疏候选扫描使用的池化数组。
/// </summary>
internal sealed class SparseCandidateWorkspace : IDisposable
{
    private readonly int[] storage;
    private readonly int candidateIndexesOffset;
    private readonly int candidateMarksOffset;
    private readonly int pivotIndexesOffset;
    private readonly int candidateCountsOffset;
    private readonly int uniqueCandidateByLeftOffset;
    private readonly int rightUniqueCountsOffset;
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
        candidateIndexesOffset = 0;
        candidateMarksOffset = candidateIndexesOffset + columnCount;
        pivotIndexesOffset = candidateMarksOffset + columnCount;
        candidateCountsOffset = pivotIndexesOffset + Math.Max(pivotCapacity, 1);
        uniqueCandidateByLeftOffset = candidateCountsOffset + rowCount;
        rightUniqueCountsOffset = uniqueCandidateByLeftOffset + rowCount;
        storage = ArrayPool<int>.Shared.Rent(
            rightUniqueCountsOffset + columnCount);
        storage.AsSpan(candidateMarksOffset, columnCount).Clear();
    }

    /// <summary>
    /// 获取当前左侧声明的候选右侧下标数组。
    /// </summary>
    public Span<int> CandidateIndexes =>
        storage.AsSpan(candidateIndexesOffset, candidateMarksOffset - candidateIndexesOffset);

    /// <summary>
    /// 获取当前左侧声明选中的 token 锚点下标数组。
    /// </summary>
    public Span<int> PivotIndexes =>
        storage.AsSpan(pivotIndexesOffset, candidateCountsOffset - pivotIndexesOffset);

    /// <summary>
    /// 获取当前左侧声明的候选数量。
    /// </summary>
    public int CandidateCount { get; private set; }

    /// <summary>
    /// 获取每个左侧声明的真实合格候选数量。
    /// </summary>
    public Span<int> CandidateCounts =>
        storage.AsSpan(candidateCountsOffset, uniqueCandidateByLeftOffset - candidateCountsOffset);

    /// <summary>
    /// 获取唯一真实候选左侧声明对应的右侧下标。
    /// </summary>
    public Span<int> UniqueCandidateByLeft =>
        storage.AsSpan(uniqueCandidateByLeftOffset, rightUniqueCountsOffset - uniqueCandidateByLeftOffset);

    /// <summary>
    /// 获取每个右侧声明被唯一候选左侧引用的次数。
    /// </summary>
    public Span<int> RightUniqueCounts =>
        storage.AsSpan(rightUniqueCountsOffset, storage.Length - rightUniqueCountsOffset);

    /// <summary>
    /// 为新的左侧声明开始一轮候选收集。
    /// </summary>
    public void BeginRow()
    {
        if (rowStamp == int.MaxValue)
        {
            storage.AsSpan(
                candidateMarksOffset,
                pivotIndexesOffset - candidateMarksOffset).Clear();
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
        var marks = storage.AsSpan(
            candidateMarksOffset,
            pivotIndexesOffset - candidateMarksOffset);
        if (marks[candidateIndex] == rowStamp)
        {
            return;
        }

        marks[candidateIndex] = rowStamp;
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
        ArrayPool<int>.Shared.Return(storage);
    }
}

using System.Buffers;

namespace AIFunctionSetMatcher.Internal;

/// <summary>
/// 保存一次 Schema 分组匹配所需的池化工作数组。
/// </summary>
internal sealed class MatchWorkspace : IDisposable
{
    private readonly int scoreCount;
    private readonly int matrixSize;
    private bool disposed;

    /// <summary>
    /// 根据分组行列数量租用匹配工作数组。
    /// </summary>
    /// <param name="rowCount">分组左侧声明数量。</param>
    /// <param name="columnCount">分组右侧声明数量。</param>
    public MatchWorkspace(int rowCount, int columnCount)
    {
        scoreCount = rowCount * columnCount;
        matrixSize = Math.Max(rowCount, columnCount);
        Scores = ArrayPool<double>.Shared.Rent(scoreCount);
        CandidateCounts = ArrayPool<int>.Shared.Rent(rowCount);
        UniqueCandidateByLeft = ArrayPool<int>.Shared.Rent(rowCount);
        RightUniqueCounts = ArrayPool<int>.Shared.Rent(columnCount);
        RowPotential = ArrayPool<double>.Shared.Rent(matrixSize + 1);
        ColumnPotential = ArrayPool<double>.Shared.Rent(matrixSize + 1);
        MatchedRow = ArrayPool<int>.Shared.Rent(matrixSize + 1);
        PreviousColumn = ArrayPool<int>.Shared.Rent(matrixSize + 1);
        Minimum = ArrayPool<double>.Shared.Rent(matrixSize + 1);
        Used = ArrayPool<bool>.Shared.Rent(matrixSize + 1);
    }

    /// <summary>
    /// 获取按行优先存储的分组候选分数矩阵。
    /// </summary>
    public double[] Scores { get; }

    /// <summary>
    /// 获取每个左侧声明的合格候选数量。
    /// </summary>
    public int[] CandidateCounts { get; }

    /// <summary>
    /// 获取每个合格候选数量为一的左侧声明对应的右侧下标。
    /// </summary>
    public int[] UniqueCandidateByLeft { get; }

    /// <summary>
    /// 获取每个右侧声明被唯一候选左侧引用的次数。
    /// </summary>
    public int[] RightUniqueCounts { get; }

    /// <summary>
    /// 获取 Hungarian 算法的行势能数组。
    /// </summary>
    public double[] RowPotential { get; }

    /// <summary>
    /// 获取 Hungarian 算法的列势能数组。
    /// </summary>
    public double[] ColumnPotential { get; }

    /// <summary>
    /// 获取 Hungarian 算法当前行与列匹配数组。
    /// </summary>
    public int[] MatchedRow { get; }

    /// <summary>
    /// 获取 Hungarian 算法增广路径的前驱列数组。
    /// </summary>
    public int[] PreviousColumn { get; }

    /// <summary>
    /// 获取 Hungarian 算法当前行的最小归约成本。
    /// </summary>
    public double[] Minimum { get; }

    /// <summary>
    /// 获取 Hungarian 算法当前增广路径已使用列标记。
    /// </summary>
    public bool[] Used { get; }

    /// <summary>
    /// 获取分数矩阵有效元素数量。
    /// </summary>
    public int ScoreCount => scoreCount;

    /// <summary>
    /// 获取补边后的 Hungarian 方阵边长。
    /// </summary>
    public int MatrixSize => matrixSize;

    /// <summary>
    /// 归还全部分组匹配工作数组。
    /// </summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        ArrayPool<double>.Shared.Return(Scores);
        ArrayPool<int>.Shared.Return(CandidateCounts);
        ArrayPool<int>.Shared.Return(UniqueCandidateByLeft);
        ArrayPool<int>.Shared.Return(RightUniqueCounts);
        ArrayPool<double>.Shared.Return(RowPotential);
        ArrayPool<double>.Shared.Return(ColumnPotential);
        ArrayPool<int>.Shared.Return(MatchedRow);
        ArrayPool<int>.Shared.Return(PreviousColumn);
        ArrayPool<double>.Shared.Return(Minimum);
        ArrayPool<bool>.Shared.Return(Used);
    }
}

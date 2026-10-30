namespace AIFunctionSetMatcher.Internal;

/// <summary>
/// 在每行或每列都有不冲突的唯一最高分时直接创建一对一分配。
/// </summary>
internal static class AssignmentShortcut
{
    /// <summary>
    /// 尝试使用唯一最大值快捷路径创建分配。
    /// </summary>
    /// <param name="scores">按行优先存储的完整分数矩阵。</param>
    /// <param name="rowCount">左侧行数。</param>
    /// <param name="columnCount">右侧列数。</param>
    /// <param name="rightByLeft">接收每个左侧行匹配右侧列；未匹配为 -1。</param>
    /// <returns>找到无冲突唯一最大值分配时返回 <see langword="true"/>。</returns>
    public static bool TryAssign(
        ReadOnlySpan<double> scores,
        int rowCount,
        int columnCount,
        Span<int> rightByLeft)
    {
        rightByLeft.Fill(-1);
        return rowCount <= columnCount
            ? TryAssignUniqueRowMaxima(
                scores,
                rowCount,
                columnCount,
                rightByLeft)
            : TryAssignUniqueColumnMaxima(
                scores,
                rowCount,
                columnCount,
                rightByLeft);
    }

    /// <summary>
    /// 检查每行唯一最高分目标是否互不冲突。
    /// </summary>
    /// <param name="scores">按行优先存储的完整分数矩阵。</param>
    /// <param name="rowCount">左侧行数。</param>
    /// <param name="columnCount">右侧列数。</param>
    /// <param name="rightByLeft">接收左侧行到右侧列的分配。</param>
    /// <returns>全部行均有不冲突唯一最大值时返回 <see langword="true"/>。</returns>
    private static bool TryAssignUniqueRowMaxima(
        ReadOnlySpan<double> scores,
        int rowCount,
        int columnCount,
        Span<int> rightByLeft)
    {
        Span<bool> usedColumns = stackalloc bool[columnCount];
        usedColumns.Clear();
        for (var row = 0; row < rowCount; row++)
        {
            var rowOffset = row * columnCount;
            var bestColumn = -1;
            var bestScore = double.NegativeInfinity;
            var hasTie = false;
            for (var column = 0; column < columnCount; column++)
            {
                var score = scores[rowOffset + column];
                if (score > bestScore)
                {
                    bestScore = score;
                    bestColumn = column;
                    hasTie = false;
                }
                else if (score == bestScore)
                {
                    hasTie = true;
                }
            }

            if (bestColumn < 0 ||
                hasTie ||
                usedColumns[bestColumn])
            {
                return false;
            }

            usedColumns[bestColumn] = true;
            rightByLeft[row] = bestColumn;
        }

        return true;
    }

    /// <summary>
    /// 检查每列唯一最高分左侧是否互不冲突。
    /// </summary>
    /// <param name="scores">按行优先存储的完整分数矩阵。</param>
    /// <param name="rowCount">左侧行数。</param>
    /// <param name="columnCount">右侧列数。</param>
    /// <param name="rightByLeft">接收左侧行到右侧列的分配。</param>
    /// <returns>全部列均有不冲突唯一最大值时返回 <see langword="true"/>。</returns>
    private static bool TryAssignUniqueColumnMaxima(
        ReadOnlySpan<double> scores,
        int rowCount,
        int columnCount,
        Span<int> rightByLeft)
    {
        Span<bool> usedRows = stackalloc bool[rowCount];
        Span<int> rowByColumn = stackalloc int[columnCount];
        usedRows.Clear();
        rowByColumn.Fill(-1);
        for (var column = 0; column < columnCount; column++)
        {
            var bestRow = -1;
            var bestScore = double.NegativeInfinity;
            var hasTie = false;
            for (var row = 0; row < rowCount; row++)
            {
                var score = scores[row * columnCount + column];
                if (score > bestScore)
                {
                    bestScore = score;
                    bestRow = row;
                    hasTie = false;
                }
                else if (score == bestScore)
                {
                    hasTie = true;
                }
            }

            if (bestRow < 0 ||
                hasTie ||
                usedRows[bestRow])
            {
                return false;
            }

            usedRows[bestRow] = true;
            rowByColumn[column] = bestRow;
        }

        for (var column = 0; column < columnCount; column++)
        {
            rightByLeft[rowByColumn[column]] = column;
        }

        return true;
    }
}

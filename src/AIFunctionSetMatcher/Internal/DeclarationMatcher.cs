using Microsoft.Extensions.AI;

namespace AIFunctionSetMatcher.Internal;

/// <summary>
/// 使用 Schema 和描述硬过滤、名称相似度和全局一对一分配匹配函数声明。
/// </summary>
internal static class DeclarationMatcher
{
    private const double InvalidCost = 1_000;

    /// <summary>
    /// 查找左侧集合中无法与右侧集合建立匹配的声明。
    /// </summary>
    /// <param name="left">左侧函数声明匹配索引。</param>
    /// <param name="right">右侧函数声明匹配索引。</param>
    /// <param name="options">匹配选项。</param>
    /// <returns>按左侧原始顺序排列的未匹配声明。</returns>
    public static AIFunctionDeclaration[] FindUnmatched(
        DeclarationMatchIndex left,
        DeclarationMatchIndex right,
        AIFunctionMatchOptions options)
    {
        Span<bool> matchedLeft = stackalloc bool[left.Count];
        FindMatchedIndexes(left, right, options, matchedLeft);
        return CreateResult(left, matchedLeft, keepMatched: false);
    }

    /// <summary>
    /// 查找左侧集合中能够与右侧集合建立匹配的声明。
    /// </summary>
    /// <param name="left">左侧函数声明匹配索引。</param>
    /// <param name="right">右侧函数声明匹配索引。</param>
    /// <param name="options">匹配选项。</param>
    /// <returns>按左侧原始顺序排列的匹配声明。</returns>
    public static AIFunctionDeclaration[] FindMatched(
        DeclarationMatchIndex left,
        DeclarationMatchIndex right,
        AIFunctionMatchOptions options)
    {
        Span<bool> matchedLeft = stackalloc bool[left.Count];
        FindMatchedIndexes(left, right, options, matchedLeft);
        return CreateResult(left, matchedLeft, keepMatched: true);
    }

    /// <summary>
    /// 计算左侧集合中成功匹配项的下标标记。
    /// </summary>
    /// <param name="left">左侧函数声明匹配索引。</param>
    /// <param name="right">右侧函数声明匹配索引。</param>
    /// <param name="options">匹配选项。</param>
    /// <param name="matchedLeft">接收左侧匹配下标的栈上标记。</param>
    private static void FindMatchedIndexes(
        DeclarationMatchIndex left,
        DeclarationMatchIndex right,
        AIFunctionMatchOptions options,
        Span<bool> matchedLeft)
    {
        if (left.Count == 0 || right.Count == 0)
        {
            return;
        }

        ValidateSchemaSizes(left, right, options.MaximumSchemaBytes);

        foreach (var (fingerprint, leftGroup) in left.GroupsByFingerprint)
        {
            if (!right.GroupsByFingerprint.TryGetValue(
                fingerprint,
                out var rightGroup))
            {
                continue;
            }

            MatchDescriptionBuckets(
                leftGroup,
                rightGroup,
                matchedLeft);
        }
    }

    /// <summary>
    /// 匹配同一 Schema 分组内具有相同描述指纹的声明。
    /// </summary>
    /// <param name="leftGroup">左侧 Schema 分组。</param>
    /// <param name="rightGroup">右侧 Schema 分组。</param>
    /// <param name="matchedLeft">接收左侧匹配下标的栈上标记。</param>
    private static void MatchDescriptionBuckets(
        SchemaGroupIndex leftGroup,
        SchemaGroupIndex rightGroup,
        Span<bool> matchedLeft)
    {
        var leftBuckets = leftGroup.DescriptionBuckets;
        var rightBuckets = rightGroup.DescriptionBuckets;
        foreach (var (descriptionFingerprint, leftSlot) in
                 leftBuckets.BucketSlots)
        {
            if (!rightBuckets.BucketSlots.TryGetValue(
                descriptionFingerprint,
                out var rightSlot))
            {
                continue;
            }

            var leftProfileIndexes =
                leftBuckets.GetProfileIndexes(leftSlot);
            var rightProfileIndexes =
                rightBuckets.GetProfileIndexes(rightSlot);
            if (leftProfileIndexes.Length == 1 &&
                rightProfileIndexes.Length == 1)
            {
                matchedLeft[
                    leftGroup.GlobalIndexes[leftProfileIndexes[0]]] = true;
                continue;
            }

            MatchDescriptionBucket(
                leftGroup,
                rightGroup,
                leftProfileIndexes,
                rightProfileIndexes,
                matchedLeft);
        }
    }

    /// <summary>
    /// 使用名称相似度为重复描述桶执行全局一对一分配。
    /// </summary>
    /// <param name="leftGroup">左侧 Schema 分组。</param>
    /// <param name="rightGroup">右侧 Schema 分组。</param>
    /// <param name="leftProfileIndexes">左侧描述桶中的组内 profile 下标。</param>
    /// <param name="rightProfileIndexes">右侧描述桶中的组内 profile 下标。</param>
    /// <param name="matchedLeft">接收左侧匹配下标的栈上标记。</param>
    private static void MatchDescriptionBucket(
        SchemaGroupIndex leftGroup,
        SchemaGroupIndex rightGroup,
        ReadOnlySpan<int> leftProfileIndexes,
        ReadOnlySpan<int> rightProfileIndexes,
        Span<bool> matchedLeft)
    {
        using var workspace = new MatchWorkspace(
            leftProfileIndexes.Length,
            rightProfileIndexes.Length);
        BuildNameScoreMatrix(
            leftGroup.Profiles,
            leftProfileIndexes,
            rightGroup.Profiles,
            rightProfileIndexes,
            workspace);

        MaximumWeightAssignment.Find(
            leftProfileIndexes.Length,
            rightProfileIndexes.Length,
            workspace,
            leftGroup.GlobalIndexes,
            leftProfileIndexes,
            matchedLeft);
    }

    /// <summary>
    /// 按当前调用选项验证左右匹配索引中的 Schema 大小。
    /// </summary>
    /// <param name="left">左侧函数声明匹配索引。</param>
    /// <param name="right">右侧函数声明匹配索引。</param>
    /// <param name="maximumSchemaBytes">当前调用允许的最大 Schema UTF-8 字节数。</param>
    private static void ValidateSchemaSizes(
        DeclarationMatchIndex left,
        DeclarationMatchIndex right,
        int maximumSchemaBytes)
    {
        if (left.MaximumSchemaByteCount <= maximumSchemaBytes &&
            right.MaximumSchemaByteCount <= maximumSchemaBytes)
        {
            return;
        }

        for (var index = 0; index < left.Count; index++)
        {
            var entry = left.GetEntry(index);
            if (entry.SchemaByteCount > maximumSchemaBytes)
            {
                throw new AIFunctionDeclarationSetParseException(
                    $"函数“{entry.Declaration.Name}”的 JsonSchema 超过 {maximumSchemaBytes} 字节限制。");
            }
        }

        for (var index = 0; index < right.Count; index++)
        {
            var entry = right.GetEntry(index);
            if (entry.SchemaByteCount > maximumSchemaBytes)
            {
                throw new AIFunctionDeclarationSetParseException(
                    $"函数“{entry.Declaration.Name}”的 JsonSchema 超过 {maximumSchemaBytes} 字节限制。");
            }
        }
    }

    /// <summary>
    /// 为描述桶内全部合法配对构建名称相似度矩阵。
    /// </summary>
    /// <param name="leftProfiles">左侧 Schema 分组中的全部匹配档案。</param>
    /// <param name="leftProfileIndexes">左侧描述桶中的组内 profile 下标。</param>
    /// <param name="rightProfiles">右侧 Schema 分组中的全部匹配档案。</param>
    /// <param name="rightProfileIndexes">右侧描述桶中的组内 profile 下标。</param>
    /// <param name="workspace">当前描述桶的池化工作数组。</param>
    private static void BuildNameScoreMatrix(
        IReadOnlyList<DeclarationMatchProfile> leftProfiles,
        ReadOnlySpan<int> leftProfileIndexes,
        IReadOnlyList<DeclarationMatchProfile> rightProfiles,
        ReadOnlySpan<int> rightProfileIndexes,
        MatchWorkspace workspace)
    {
        var columnCount = rightProfileIndexes.Length;
        for (var localLeft = 0;
             localLeft < leftProfileIndexes.Length;
             localLeft++)
        {
            var left = leftProfiles[leftProfileIndexes[localLeft]];
            var rowOffset = localLeft * columnCount;
            for (var localRight = 0;
                 localRight < rightProfileIndexes.Length;
                 localRight++)
            {
                var right = rightProfiles[rightProfileIndexes[localRight]];
                workspace.Scores[rowOffset + localRight] =
                    TextSimilarity.ComputeNameSimilarity(
                        left.NameTokens,
                        right.NameTokens);
            }
        }
    }

    /// <summary>
    /// 根据左侧匹配标记创建精确大小的结果数组。
    /// </summary>
    /// <param name="left">左侧函数声明匹配索引。</param>
    /// <param name="matchedLeft">左侧匹配下标标记。</param>
    /// <param name="keepMatched">保留匹配项而不是未匹配项时为 <see langword="true"/>。</param>
    /// <returns>按左侧原始顺序排列的声明数组。</returns>
    private static AIFunctionDeclaration[] CreateResult(
        DeclarationMatchIndex left,
        ReadOnlySpan<bool> matchedLeft,
        bool keepMatched)
    {
        var resultCount = 0;
        for (var index = 0; index < left.Count; index++)
        {
            if (matchedLeft[index] == keepMatched)
            {
                resultCount++;
            }
        }

        var result = new AIFunctionDeclaration[resultCount];
        var resultIndex = 0;
        for (var index = 0; index < left.Count; index++)
        {
            if (matchedLeft[index] == keepMatched)
            {
                result[resultIndex++] = left.GetDeclaration(index);
            }
        }

        return result;
    }

    /// <summary>
    /// 计算最大匹配数优先、名称相似度总和其次的全局一对一分配。
    /// </summary>
    private static class MaximumWeightAssignment
    {
        /// <summary>
        /// 查找描述桶内的全局匹配对，并写入左侧匹配标记。
        /// </summary>
        /// <param name="rowCount">描述桶左侧声明数量。</param>
        /// <param name="columnCount">描述桶右侧声明数量。</param>
        /// <param name="workspace">当前描述桶的池化工作数组。</param>
        /// <param name="groupLeftIndexes">Schema 分组中的左侧全局下标。</param>
        /// <param name="bucketLeftIndexes">描述桶中的组内 profile 下标。</param>
        /// <param name="matchedLeft">接收左侧匹配下标的栈上标记。</param>
        public static void Find(
            int rowCount,
            int columnCount,
            MatchWorkspace workspace,
            IReadOnlyList<int> groupLeftIndexes,
            ReadOnlySpan<int> bucketLeftIndexes,
            Span<bool> matchedLeft)
        {
            var size = workspace.MatrixSize;
            Array.Clear(workspace.RowPotential, 0, size + 1);
            Array.Clear(workspace.ColumnPotential, 0, size + 1);
            Array.Clear(workspace.MatchedRow, 0, size + 1);

            FindMinimumCostAssignment(
                rowCount,
                columnCount,
                workspace,
                size);

            for (var column = 1; column <= size; column++)
            {
                var row = workspace.MatchedRow[column];
                if (row == 0 || row > rowCount || column > columnCount)
                {
                    continue;
                }

                var profileIndex = bucketLeftIndexes[row - 1];
                matchedLeft[groupLeftIndexes[profileIndex]] = true;
            }
        }

        /// <summary>
        /// 使用 Hungarian 算法计算最小成本完全分配。
        /// </summary>
        /// <param name="rowCount">实际左侧桶行数。</param>
        /// <param name="columnCount">实际右侧桶列数。</param>
        /// <param name="workspace">当前描述桶的池化工作数组。</param>
        /// <param name="size">补边后的方阵边长。</param>
        private static void FindMinimumCostAssignment(
            int rowCount,
            int columnCount,
            MatchWorkspace workspace,
            int size)
        {
            for (var row = 1; row <= size; row++)
            {
                Array.Fill(
                    workspace.Minimum,
                    double.PositiveInfinity,
                    0,
                    size + 1);
                Array.Fill(workspace.Used, false, 0, size + 1);
                workspace.MatchedRow[0] = row;
                var currentColumn = 0;

                do
                {
                    workspace.Used[currentColumn] = true;
                    var currentRow = workspace.MatchedRow[currentColumn];
                    var delta = double.PositiveInfinity;
                    var nextColumn = 0;

                    for (var column = 1; column <= size; column++)
                    {
                        if (workspace.Used[column])
                        {
                            continue;
                        }

                        var reducedCost =
                            GetCost(
                                currentRow,
                                column,
                                rowCount,
                                columnCount,
                                workspace.Scores) -
                            workspace.RowPotential[currentRow] -
                            workspace.ColumnPotential[column];
                        if (reducedCost < workspace.Minimum[column])
                        {
                            workspace.Minimum[column] = reducedCost;
                            workspace.PreviousColumn[column] = currentColumn;
                        }

                        if (workspace.Minimum[column] < delta)
                        {
                            delta = workspace.Minimum[column];
                            nextColumn = column;
                        }
                    }

                    for (var column = 0; column <= size; column++)
                    {
                        if (workspace.Used[column])
                        {
                            workspace.RowPotential[workspace.MatchedRow[column]] +=
                                delta;
                            workspace.ColumnPotential[column] -= delta;
                        }
                        else
                        {
                            workspace.Minimum[column] -= delta;
                        }
                    }

                    currentColumn = nextColumn;
                }
                while (workspace.MatchedRow[currentColumn] != 0);

                do
                {
                    var nextColumn = workspace.PreviousColumn[currentColumn];
                    workspace.MatchedRow[currentColumn] =
                        workspace.MatchedRow[nextColumn];
                    currentColumn = nextColumn;
                }
                while (currentColumn != 0);
            }
        }

        /// <summary>
        /// 按实际名称分数或补边无效成本计算 Hungarian 归约前的成本。
        /// </summary>
        /// <param name="row">一基左侧行号。</param>
        /// <param name="column">一基右侧列号。</param>
        /// <param name="rowCount">实际左侧桶行数。</param>
        /// <param name="columnCount">实际右侧桶列数。</param>
        /// <param name="scores">按行优先存储的名称相似度矩阵。</param>
        /// <returns>有效候选为 <c>1 - score</c>，否则为固定无效成本。</returns>
        private static double GetCost(
            int row,
            int column,
            int rowCount,
            int columnCount,
            double[] scores)
        {
            if (row <= rowCount && column <= columnCount)
            {
                return 1 - scores[(row - 1) * columnCount + (column - 1)];
            }

            return InvalidCost;
        }
    }
}

using Microsoft.Extensions.AI;

namespace AIFunctionSetMatcher.Internal;

/// <summary>
/// 使用预计算匹配档案、语义签名和全局一对一分配匹配函数声明。
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
        Span<bool> matchedLeft = stackalloc bool[left.Profiles.Count];
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
        Span<bool> matchedLeft = stackalloc bool[left.Profiles.Count];
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
        if (left.Profiles.Count == 0 || right.Profiles.Count == 0)
        {
            return;
        }

        ValidateSchemaSizes(left, right, options.MaximumSchemaBytes);

        foreach (var (fingerprint, leftIndexes) in left.GroupsByFingerprint)
        {
            if (!right.GroupsByFingerprint.TryGetValue(
                fingerprint,
                out var rightIndexes))
            {
                continue;
            }

            using var workspace = new MatchWorkspace(
                leftIndexes.Count,
                rightIndexes.Count);
            var hasUniqueCandidates = BuildScoreMatrix(
                left.Profiles,
                leftIndexes,
                right.Profiles,
                rightIndexes,
                options,
                workspace);
            if (hasUniqueCandidates &&
                TryApplyUniqueCandidates(
                    leftIndexes,
                    rightIndexes.Count,
                    workspace,
                    matchedLeft))
            {
                continue;
            }

            MaximumWeightAssignment.Find(
                leftIndexes.Count,
                rightIndexes.Count,
                workspace,
                options.MinimumMatchScore,
                leftIndexes,
                matchedLeft);
        }
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
        foreach (var profile in left.Profiles)
        {
            if (profile.SchemaByteCount > maximumSchemaBytes)
            {
                throw new AIFunctionDeclarationSetParseException(
                    $"函数“{profile.Declaration.Name}”的 JsonSchema 超过 {maximumSchemaBytes} 字节限制。");
            }
        }

        foreach (var profile in right.Profiles)
        {
            if (profile.SchemaByteCount > maximumSchemaBytes)
            {
                throw new AIFunctionDeclarationSetParseException(
                    $"函数“{profile.Declaration.Name}”的 JsonSchema 超过 {maximumSchemaBytes} 字节限制。");
            }
        }
    }

    /// <summary>
    /// 构建同一 Schema 分组内经过阈值和歧义过滤的候选分数矩阵。
    /// </summary>
    /// <param name="leftProfiles">左侧全部匹配档案。</param>
    /// <param name="leftIndexes">当前分组的左侧下标。</param>
    /// <param name="rightProfiles">右侧全部匹配档案。</param>
    /// <param name="rightIndexes">当前分组的右侧下标。</param>
    /// <param name="options">匹配选项。</param>
    /// <param name="workspace">当前分组的池化工作数组。</param>
    /// <returns>每个左侧最多只有一个合格候选时返回 <see langword="true"/>。</returns>
    private static bool BuildScoreMatrix(
        IReadOnlyList<DeclarationMatchProfile> leftProfiles,
        IReadOnlyList<int> leftIndexes,
        IReadOnlyList<DeclarationMatchProfile> rightProfiles,
        IReadOnlyList<int> rightIndexes,
        AIFunctionMatchOptions options,
        MatchWorkspace workspace)
    {
        var columnCount = rightIndexes.Count;
        var hasUniqueCandidates = true;
        for (var localLeft = 0; localLeft < leftIndexes.Count; localLeft++)
        {
            var left = leftProfiles[leftIndexes[localLeft]];
            var bestScore = -1.0;
            var secondScore = -1.0;
            var candidateCount = 0;
            var candidateRight = -1;
            var rowOffset = localLeft * columnCount;

            for (var localRight = 0; localRight < columnCount; localRight++)
            {
                var right = rightProfiles[rightIndexes[localRight]];
                var scoreIndex = rowOffset + localRight;
                if (!IsDescriptionCompatible(
                    left,
                    right,
                    options,
                    out var descriptionScore))
                {
                    workspace.Scores[scoreIndex] = -1;
                    continue;
                }

                var nameScore = TextSimilarity.ComputeNameSimilarity(
                    left.NameTokens,
                    right.NameTokens);
                var score = left.HasDescription
                    ? (0.75 * nameScore) + (0.25 * descriptionScore)
                    : nameScore;
                workspace.Scores[scoreIndex] = score;

                if (score < options.MinimumMatchScore)
                {
                    continue;
                }

                candidateCount++;
                candidateRight = candidateCount == 1 ? localRight : -1;
                if (score > bestScore)
                {
                    secondScore = bestScore;
                    bestScore = score;
                }
                else if (score > secondScore)
                {
                    secondScore = score;
                }
            }

            if (secondScore >= options.MinimumMatchScore &&
                bestScore - secondScore < options.MinimumScoreGap)
            {
                for (var localRight = 0; localRight < columnCount; localRight++)
                {
                    var scoreIndex = rowOffset + localRight;
                    if (workspace.Scores[scoreIndex] >= options.MinimumMatchScore)
                    {
                        workspace.Scores[scoreIndex] = -1;
                    }
                }

                candidateCount = 0;
                candidateRight = -1;
            }

            workspace.CandidateCounts[localLeft] = candidateCount;
            workspace.UniqueCandidateByLeft[localLeft] = candidateRight;
            hasUniqueCandidates &= candidateCount <= 1;
        }

        return hasUniqueCandidates;
    }

    /// <summary>
    /// 在候选和右侧使用均唯一时直接应用匹配。
    /// </summary>
    /// <param name="leftIndexes">当前分组的左侧下标。</param>
    /// <param name="rightCount">当前分组的右侧数量。</param>
    /// <param name="workspace">当前分组的池化工作数组。</param>
    /// <param name="matchedLeft">接收左侧匹配下标的栈上标记。</param>
    /// <returns>成功应用唯一候选匹配时返回 <see langword="true"/>。</returns>
    private static bool TryApplyUniqueCandidates(
        IReadOnlyList<int> leftIndexes,
        int rightCount,
        MatchWorkspace workspace,
        Span<bool> matchedLeft)
    {
        for (var right = 0; right < rightCount; right++)
        {
            workspace.RightUniqueCounts[right] = 0;
        }

        for (var localLeft = 0; localLeft < leftIndexes.Count; localLeft++)
        {
            if (workspace.CandidateCounts[localLeft] != 1)
            {
                continue;
            }

            var localRight = workspace.UniqueCandidateByLeft[localLeft];
            if (localRight < 0 ||
                localRight >= rightCount ||
                ++workspace.RightUniqueCounts[localRight] > 1)
            {
                return false;
            }
        }

        for (var localLeft = 0; localLeft < leftIndexes.Count; localLeft++)
        {
            if (workspace.CandidateCounts[localLeft] == 1)
            {
                matchedLeft[leftIndexes[localLeft]] = true;
            }
        }

        return true;
    }

    /// <summary>
    /// 验证两个预计算档案的顶层描述存在性和相似度。
    /// </summary>
    /// <param name="expected">权威匹配档案。</param>
    /// <param name="actual">客户端匹配档案。</param>
    /// <param name="options">匹配选项。</param>
    /// <param name="descriptionScore">接收双方描述的 Jaccard 相似度。</param>
    /// <returns>描述允许参与匹配时返回 <see langword="true"/>。</returns>
    private static bool IsDescriptionCompatible(
        DeclarationMatchProfile expected,
        DeclarationMatchProfile actual,
        AIFunctionMatchOptions options,
        out double descriptionScore)
    {
        if (expected.HasDescription != actual.HasDescription)
        {
            descriptionScore = 0;
            return false;
        }

        if (!expected.HasDescription)
        {
            descriptionScore = 0;
            return true;
        }

        descriptionScore = TextSimilarity.ComputeDescriptionJaccard(
            expected.DescriptionTokens,
            actual.DescriptionTokens);
        return descriptionScore >= options.MinimumDescriptionSimilarity;
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
        for (var index = 0; index < left.Profiles.Count; index++)
        {
            if (matchedLeft[index] == keepMatched)
            {
                resultCount++;
            }
        }

        var result = new AIFunctionDeclaration[resultCount];
        var resultIndex = 0;
        for (var index = 0; index < left.Profiles.Count; index++)
        {
            if (matchedLeft[index] == keepMatched)
            {
                result[resultIndex++] = left.Profiles[index].Declaration;
            }
        }

        return result;
    }

    /// <summary>
    /// 计算最大匹配数优先、总相似度其次的全局一对一分配。
    /// </summary>
    private static class MaximumWeightAssignment
    {
        /// <summary>
        /// 查找左右分组之间的全局匹配对，并写入左侧匹配标记。
        /// </summary>
        /// <param name="rowCount">左侧分组声明数量。</param>
        /// <param name="columnCount">右侧分组声明数量。</param>
        /// <param name="workspace">当前分组的池化工作数组。</param>
        /// <param name="minimumScore">合格匹配所需的最低分数。</param>
        /// <param name="leftIndexes">当前分组的左侧全局下标。</param>
        /// <param name="matchedLeft">接收左侧匹配下标的栈上标记。</param>
        public static void Find(
            int rowCount,
            int columnCount,
            MatchWorkspace workspace,
            double minimumScore,
            IReadOnlyList<int> leftIndexes,
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

                var score = workspace.Scores[(row - 1) * columnCount + (column - 1)];
                if (score >= minimumScore)
                {
                    matchedLeft[leftIndexes[row - 1]] = true;
                }
            }
        }

        /// <summary>
        /// 使用 Hungarian 算法计算最小成本完全分配。
        /// </summary>
        /// <param name="rowCount">实际左侧分组行数。</param>
        /// <param name="columnCount">实际右侧分组列数。</param>
        /// <param name="workspace">当前分组的池化工作数组。</param>
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
                            workspace.RowPotential[workspace.MatchedRow[column]] += delta;
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
                    workspace.MatchedRow[currentColumn] = workspace.MatchedRow[nextColumn];
                    currentColumn = nextColumn;
                }
                while (currentColumn != 0);
            }
        }

        /// <summary>
        /// 按实际分数或补边无效成本计算 Hungarian 归约前的成本。
        /// </summary>
        /// <param name="row">一基左侧行号。</param>
        /// <param name="column">一基右侧列号。</param>
        /// <param name="rowCount">实际左侧分组行数。</param>
        /// <param name="columnCount">实际右侧分组列数。</param>
        /// <param name="scores">按行优先存储的候选分数矩阵。</param>
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
                var score = scores[(row - 1) * columnCount + (column - 1)];
                if (score >= 0)
                {
                    return 1 - score;
                }
            }

            return InvalidCost;
        }
    }
}

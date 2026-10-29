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
    /// <param name="left">左侧函数声明集合。</param>
    /// <param name="right">右侧函数声明集合。</param>
    /// <param name="options">匹配选项。</param>
    /// <returns>按左侧原始顺序排列的未匹配声明。</returns>
    public static IReadOnlyList<AIFunctionDeclaration> FindUnmatched(
        IReadOnlyList<AIFunctionDeclaration> left,
        IReadOnlyList<AIFunctionDeclaration> right,
        AIFunctionMatchOptions options)
    {
        var matchedLeft = FindMatchedIndexes(left, right, options);
        return left
            .Where((_, index) => !matchedLeft.Contains(index))
            .ToArray();
    }

    /// <summary>
    /// 查找左侧集合中能够与右侧集合建立匹配的声明。
    /// </summary>
    /// <param name="left">左侧函数声明集合。</param>
    /// <param name="right">右侧函数声明集合。</param>
    /// <param name="options">匹配选项。</param>
    /// <returns>按左侧原始顺序排列的匹配声明。</returns>
    public static IReadOnlyList<AIFunctionDeclaration> FindMatched(
        IReadOnlyList<AIFunctionDeclaration> left,
        IReadOnlyList<AIFunctionDeclaration> right,
        AIFunctionMatchOptions options)
    {
        var matchedLeft = FindMatchedIndexes(left, right, options);
        return left
            .Where((_, index) => matchedLeft.Contains(index))
            .ToArray();
    }

    /// <summary>
    /// 计算左侧集合中成功匹配项的下标。
    /// </summary>
    /// <param name="left">左侧函数声明集合。</param>
    /// <param name="right">右侧函数声明集合。</param>
    /// <param name="options">匹配选项。</param>
    /// <returns>左侧匹配下标集合。</returns>
    private static HashSet<int> FindMatchedIndexes(
        IReadOnlyList<AIFunctionDeclaration> left,
        IReadOnlyList<AIFunctionDeclaration> right,
        AIFunctionMatchOptions options)
    {
        var matchedLeft = new HashSet<int>();
        if (left.Count == 0 || right.Count == 0)
        {
            return matchedLeft;
        }

        var leftProfiles = BuildProfiles(left, options.MaximumSchemaBytes);
        var rightProfiles = BuildProfiles(right, options.MaximumSchemaBytes);
        var leftGroups = GroupBySchemaSignature(leftProfiles);
        var rightGroups = GroupBySchemaSignature(rightProfiles);

        foreach (var (signature, leftIndexes) in leftGroups)
        {
            if (!rightGroups.TryGetValue(signature, out var rightIndexes))
            {
                continue;
            }

            var scores = BuildScoreMatrix(
                leftProfiles,
                leftIndexes,
                rightProfiles,
                rightIndexes,
                options);
            var assignment = MaximumWeightAssignment.Find(
                leftIndexes.Count,
                rightIndexes.Count,
                scores);

            foreach (var (localLeft, localRight) in assignment)
            {
                if (scores[localLeft, localRight] >= options.MinimumMatchScore)
                {
                    matchedLeft.Add(leftIndexes[localLeft]);
                }
            }
        }

        return matchedLeft;
    }

    /// <summary>
    /// 为声明集合创建单次匹配调用使用的预计算档案。
    /// </summary>
    /// <param name="declarations">要建立档案的函数声明。</param>
    /// <param name="maximumSchemaBytes">允许的最大 Schema UTF-8 字节数。</param>
    /// <returns>与输入顺序一致的匹配档案数组。</returns>
    private static DeclarationMatchProfile[] BuildProfiles(
        IReadOnlyList<AIFunctionDeclaration> declarations,
        int maximumSchemaBytes)
    {
        var profiles = new DeclarationMatchProfile[declarations.Count];
        for (var index = 0; index < declarations.Count; index++)
        {
            profiles[index] = DeclarationMatchProfile.Create(
                declarations[index],
                maximumSchemaBytes);
        }

        return profiles;
    }

    /// <summary>
    /// 按 Schema 语义签名对匹配档案分组。
    /// </summary>
    /// <param name="profiles">要分组的匹配档案。</param>
    /// <returns>签名到原始下标列表的映射。</returns>
    private static Dictionary<string, List<int>> GroupBySchemaSignature(
        IReadOnlyList<DeclarationMatchProfile> profiles)
    {
        var groups = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (var index = 0; index < profiles.Count; index++)
        {
            var signature = profiles[index].SchemaSignature;
            if (!groups.TryGetValue(signature, out var indexes))
            {
                indexes = [];
                groups.Add(signature, indexes);
            }

            indexes.Add(index);
        }

        return groups;
    }

    /// <summary>
    /// 构建同一 Schema 分组内经过阈值和歧义过滤的候选分数矩阵。
    /// </summary>
    /// <param name="leftProfiles">左侧全部匹配档案。</param>
    /// <param name="leftIndexes">当前分组的左侧下标。</param>
    /// <param name="rightProfiles">右侧全部匹配档案。</param>
    /// <param name="rightIndexes">当前分组的右侧下标。</param>
    /// <param name="options">匹配选项。</param>
    /// <returns>无效候选为 -1 的分数矩阵。</returns>
    private static double[,] BuildScoreMatrix(
        IReadOnlyList<DeclarationMatchProfile> leftProfiles,
        IReadOnlyList<int> leftIndexes,
        IReadOnlyList<DeclarationMatchProfile> rightProfiles,
        IReadOnlyList<int> rightIndexes,
        AIFunctionMatchOptions options)
    {
        var scores = new double[leftIndexes.Count, rightIndexes.Count];
        for (var localLeft = 0; localLeft < leftIndexes.Count; localLeft++)
        {
            var left = leftProfiles[leftIndexes[localLeft]];
            var bestScore = -1.0;
            var secondScore = -1.0;

            for (var localRight = 0; localRight < rightIndexes.Count; localRight++)
            {
                var right = rightProfiles[rightIndexes[localRight]];
                if (!IsDescriptionCompatible(left, right, options))
                {
                    scores[localLeft, localRight] = -1;
                    continue;
                }

                var nameScore = TextSimilarity.ComputeNameSimilarity(
                    left.NameTokens,
                    right.NameTokens);
                var descriptionScore = left.HasDescription
                    ? TextSimilarity.ComputeDescriptionJaccard(
                        left.DescriptionTokens,
                        right.DescriptionTokens)
                    : 0;
                var score = left.HasDescription
                    ? (0.75 * nameScore) + (0.25 * descriptionScore)
                    : nameScore;
                scores[localLeft, localRight] = score;

                if (score >= options.MinimumMatchScore)
                {
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
            }

            if (secondScore >= options.MinimumMatchScore &&
                bestScore - secondScore < options.MinimumScoreGap)
            {
                for (var localRight = 0; localRight < rightIndexes.Count; localRight++)
                {
                    if (scores[localLeft, localRight] >= options.MinimumMatchScore)
                    {
                        scores[localLeft, localRight] = -1;
                    }
                }
            }
        }

        return scores;
    }

    /// <summary>
    /// 验证两个预计算档案的顶层描述存在性和相似度。
    /// </summary>
    /// <param name="expected">权威匹配档案。</param>
    /// <param name="actual">客户端匹配档案。</param>
    /// <param name="options">匹配选项。</param>
    /// <returns>描述允许参与匹配时返回 <see langword="true"/>。</returns>
    private static bool IsDescriptionCompatible(
        DeclarationMatchProfile expected,
        DeclarationMatchProfile actual,
        AIFunctionMatchOptions options)
    {
        if (expected.HasDescription != actual.HasDescription)
        {
            return false;
        }

        if (!expected.HasDescription)
        {
            return true;
        }

        return TextSimilarity.ComputeDescriptionJaccard(
            expected.DescriptionTokens,
            actual.DescriptionTokens) >= options.MinimumDescriptionSimilarity;
    }

    /// <summary>
    /// 计算最大匹配数优先、总相似度其次的全局一对一分配。
    /// </summary>
    private static class MaximumWeightAssignment
    {
        /// <summary>
        /// 查找左右分组之间的全局匹配对。
        /// </summary>
        /// <param name="rowCount">左侧分组声明数量。</param>
        /// <param name="columnCount">右侧分组声明数量。</param>
        /// <param name="scores">分组候选分数矩阵。</param>
        /// <returns>匹配的左右分组内下标对。</returns>
        public static IReadOnlyList<(int Left, int Right)> Find(
            int rowCount,
            int columnCount,
            double[,] scores)
        {
            var size = Math.Max(rowCount, columnCount);
            var cost = new double[size + 1, size + 1];
            for (var row = 1; row <= size; row++)
            {
                for (var column = 1; column <= size; column++)
                {
                    cost[row, column] =
                        row <= rowCount &&
                        column <= columnCount &&
                        scores[row - 1, column - 1] >= 0
                            ? 1 - scores[row - 1, column - 1]
                            : InvalidCost;
                }
            }

            var assignment = FindMinimumCostAssignment(cost, size);
            return assignment
                .Where(pair => pair.Left < rowCount && pair.Right < columnCount)
                .ToArray();
        }

        /// <summary>
        /// 使用 Hungarian 算法计算最小成本完全分配。
        /// </summary>
        /// <param name="cost">补边后的方阵成本。</param>
        /// <param name="size">方阵边长。</param>
        /// <returns>左右分组内下标分配结果。</returns>
        private static IReadOnlyList<(int Left, int Right)> FindMinimumCostAssignment(
            double[,] cost,
            int size)
        {
            var rowPotential = new double[size + 1];
            var columnPotential = new double[size + 1];
            var matchedRow = new int[size + 1];
            var previousColumn = new int[size + 1];
            var minimum = new double[size + 1];
            var used = new bool[size + 1];

            for (var row = 1; row <= size; row++)
            {
                Array.Fill(minimum, double.PositiveInfinity);
                Array.Fill(used, false);
                matchedRow[0] = row;
                var currentColumn = 0;

                do
                {
                    used[currentColumn] = true;
                    var currentRow = matchedRow[currentColumn];
                    var delta = double.PositiveInfinity;
                    var nextColumn = 0;

                    for (var column = 1; column <= size; column++)
                    {
                        if (used[column])
                        {
                            continue;
                        }

                        var reducedCost =
                            cost[currentRow, column] -
                            rowPotential[currentRow] -
                            columnPotential[column];
                        if (reducedCost < minimum[column])
                        {
                            minimum[column] = reducedCost;
                            previousColumn[column] = currentColumn;
                        }

                        if (minimum[column] < delta)
                        {
                            delta = minimum[column];
                            nextColumn = column;
                        }
                    }

                    for (var column = 0; column <= size; column++)
                    {
                        if (used[column])
                        {
                            rowPotential[matchedRow[column]] += delta;
                            columnPotential[column] -= delta;
                        }
                        else
                        {
                            minimum[column] -= delta;
                        }
                    }

                    currentColumn = nextColumn;
                }
                while (matchedRow[currentColumn] != 0);

                do
                {
                    var nextColumn = previousColumn[currentColumn];
                    matchedRow[currentColumn] = matchedRow[nextColumn];
                    currentColumn = nextColumn;
                }
                while (currentColumn != 0);
            }

            var assignments = new List<(int Left, int Right)>(size);
            for (var column = 1; column <= size; column++)
            {
                if (matchedRow[column] != 0)
                {
                    assignments.Add((matchedRow[column] - 1, column - 1));
                }
            }

            return assignments;
        }
    }
}

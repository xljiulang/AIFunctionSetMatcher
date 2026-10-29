using Microsoft.Extensions.AI;

namespace AIFunctionSetMatcher.Internal;

/// <summary>
/// 使用预计算匹配档案、语义签名和全局一对一分配匹配函数声明。
/// </summary>
internal static class DeclarationMatcher
{
    private const double InvalidCost = 1_000;
    private const int SparseMatchingMinimumCount = 16;

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

            if (leftGroup.Count >= SparseMatchingMinimumCount &&
                rightGroup.Count >= SparseMatchingMinimumCount &&
                TryMatchSparseCandidates(
                leftGroup,
                rightGroup,
                options,
                matchedLeft))
            {
                continue;
            }

            if (leftGroup.Count < SparseMatchingMinimumCount &&
                TryMatchUniqueCandidates(
                leftGroup.Profiles,
                leftGroup.GlobalIndexes,
                rightGroup.Profiles,
                options,
                matchedLeft))
            {
                continue;
            }

            using var workspace = new MatchWorkspace(
                leftGroup.Count,
                rightGroup.Count);
            BuildScoreMatrix(
                leftGroup.Profiles,
                rightGroup.Profiles,
                options,
                workspace);

            MaximumWeightAssignment.Find(
                leftGroup.Count,
                rightGroup.Count,
                workspace,
                options.MinimumMatchScore,
                leftGroup.GlobalIndexes,
                matchedLeft);
        }
    }

    /// <summary>
    /// 在不分配分数矩阵的情况下查找一一对应的唯一候选。
    /// </summary>
    /// <param name="leftProfiles">左侧全部匹配档案。</param>
    /// <param name="globalLeftIndexes">当前分组的左侧全局下标。</param>
    /// <param name="rightProfiles">右侧全部匹配档案。</param>
    /// <param name="options">匹配选项。</param>
    /// <param name="matchedLeft">接收左侧匹配下标的栈上标记。</param>
    /// <returns>所有候选均唯一且一一对应时返回 <see langword="true"/>。</returns>
    private static bool TryMatchUniqueCandidates(
        IReadOnlyList<DeclarationMatchProfile> leftProfiles,
        IReadOnlyList<int> globalLeftIndexes,
        IReadOnlyList<DeclarationMatchProfile> rightProfiles,
        AIFunctionMatchOptions options,
        Span<bool> matchedLeft)
    {
        using var summary = new CandidateSummaryWorkspace(
            leftProfiles.Count,
            rightProfiles.Count);

        for (var localLeft = 0; localLeft < leftProfiles.Count; localLeft++)
        {
            var left = leftProfiles[localLeft];
            var candidateCount = 0;
            var candidateRight = -1;
            for (var localRight = 0; localRight < rightProfiles.Count; localRight++)
            {
                var right = rightProfiles[localRight];
                if (!TryComputeScore(left, right, options, out var score) ||
                    score < options.MinimumMatchScore)
                {
                    continue;
                }

                candidateCount++;
                candidateRight = candidateCount == 1 ? localRight : -1;
                if (candidateCount > 1)
                {
                    return false;
                }
            }

            summary.CandidateCounts[localLeft] = candidateCount;
            summary.UniqueCandidateByLeft[localLeft] = candidateRight;
        }

        for (var right = 0; right < rightProfiles.Count; right++)
        {
            summary.RightUniqueCounts[right] = 0;
        }

        for (var localLeft = 0; localLeft < leftProfiles.Count; localLeft++)
        {
            if (summary.CandidateCounts[localLeft] != 1)
            {
                continue;
            }

            var localRight = summary.UniqueCandidateByLeft[localLeft];
            if (++summary.RightUniqueCounts[localRight] > 1)
            {
                return false;
            }
        }

        for (var localLeft = 0; localLeft < leftProfiles.Count; localLeft++)
        {
            if (summary.CandidateCounts[localLeft] == 1)
            {
                matchedLeft[globalLeftIndexes[localLeft]] = true;
            }
        }

        return true;
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
    /// 使用右侧 token 倒排索引生成安全候选超集，并匹配一一对应的唯一候选。
    /// </summary>
    /// <param name="leftGroup">左侧 Schema 分组。</param>
    /// <param name="rightGroup">右侧 Schema 分组。</param>
    /// <param name="options">匹配选项。</param>
    /// <param name="matchedLeft">接收左侧匹配下标的栈上标记。</param>
    /// <returns>所有候选均唯一且一一对应时返回 <see langword="true"/>。</returns>
    private static bool TryMatchSparseCandidates(
        SchemaGroupIndex leftGroup,
        SchemaGroupIndex rightGroup,
        AIFunctionMatchOptions options,
        Span<bool> matchedLeft)
    {
        if (options.MinimumMatchScore <= 0 ||
            options.MinimumDescriptionSimilarity <= 0)
        {
            return false;
        }

        var leftProfiles = leftGroup.Profiles;
        var rightProfiles = rightGroup.Profiles;
        var queryIndex = rightGroup.QueryIndex;
        var pivotCapacity = 0;
        foreach (var profile in leftProfiles)
        {
            pivotCapacity = Math.Max(
                pivotCapacity,
                profile.HasDescription
                    ? profile.DescriptionTokens.Count
                    : profile.NameTokens.Count);
        }

        using var workspace = new SparseCandidateWorkspace(
            leftProfiles.Count,
            rightProfiles.Count,
            pivotCapacity);

        for (var localLeft = 0; localLeft < leftProfiles.Count; localLeft++)
        {
            var left = leftProfiles[localLeft];
            workspace.BeginRow();
            var canPrune = left.HasDescription
                ? AddDescriptionCandidates(
                    left,
                    rightProfiles,
                    queryIndex,
                    options,
                    workspace)
                : AddNameCandidates(
                    left,
                    rightProfiles,
                    queryIndex,
                    options,
                    workspace);
            if (!canPrune)
            {
                return false;
            }

            var candidateCount = 0;
            var candidateRight = -1;
            for (var candidate = 0; candidate < workspace.CandidateCount; candidate++)
            {
                var localRight = workspace.CandidateIndexes[candidate];
                var right = rightProfiles[localRight];
                if (!TryComputeScore(left, right, options, out var score) ||
                    score < options.MinimumMatchScore)
                {
                    continue;
                }

                candidateCount++;
                candidateRight = candidateCount == 1 ? localRight : -1;
                if (candidateCount > 1)
                {
                    return false;
                }
            }

            workspace.CandidateCounts[localLeft] = candidateCount;
            workspace.UniqueCandidateByLeft[localLeft] = candidateRight;
        }

        for (var right = 0; right < rightProfiles.Count; right++)
        {
            workspace.RightUniqueCounts[right] = 0;
        }

        for (var localLeft = 0; localLeft < leftProfiles.Count; localLeft++)
        {
            if (workspace.CandidateCounts[localLeft] != 1)
            {
                continue;
            }

            var localRight = workspace.UniqueCandidateByLeft[localLeft];
            if (++workspace.RightUniqueCounts[localRight] > 1)
            {
                return false;
            }
        }

        for (var localLeft = 0; localLeft < leftProfiles.Count; localLeft++)
        {
            if (workspace.CandidateCounts[localLeft] == 1)
            {
                matchedLeft[leftGroup.GlobalIndexes[localLeft]] = true;
            }
        }

        return true;
    }

    /// <summary>
    /// 根据描述 Jaccard 上界添加稀有 token 候选。
    /// </summary>
    /// <param name="left">左侧匹配档案。</param>
    /// <param name="rightProfiles">右侧分组匹配档案。</param>
    /// <param name="queryIndex">右侧 token 查询索引。</param>
    /// <param name="options">匹配选项。</param>
    /// <param name="workspace">稀疏候选工作数组。</param>
    /// <returns>可以安全使用候选剪枝时返回 <see langword="true"/>。</returns>
    private static bool AddDescriptionCandidates(
        DeclarationMatchProfile left,
        IReadOnlyList<DeclarationMatchProfile> rightProfiles,
        SchemaGroupQueryIndex queryIndex,
        AIFunctionMatchOptions options,
        SparseCandidateWorkspace workspace)
    {
        var leftCount = left.DescriptionTokens.Count;
        if (leftCount == 0)
        {
            return true;
        }

        var threshold = options.MinimumDescriptionSimilarity;
        foreach (var (rightCount, _) in queryIndex.DescriptionsByTokenCount.Buckets)
        {
            if (rightCount == 0)
            {
                continue;
            }

            var smallerCount = Math.Min(leftCount, rightCount);
            var largerCount = Math.Max(leftCount, rightCount);
            if ((double)smallerCount / largerCount < threshold)
            {
                continue;
            }

            var requiredIntersection = (int)Math.Ceiling(
                threshold * (leftCount + rightCount) / (1 + threshold));
            if (requiredIntersection <= 0 ||
                requiredIntersection > leftCount)
            {
                continue;
            }

            var pivotCount = leftCount - requiredIntersection + 1;
            if (pivotCount <= 0 || pivotCount > leftCount)
            {
                return false;
            }

            AddRareTokenCandidates(
                left.DescriptionTokens,
                pivotCount,
                queryIndex.DescriptionPostings,
                rightProfiles,
                rightCount,
                useDescriptionTokens: true,
                workspace);
        }

        return true;
    }

    /// <summary>
    /// 根据无描述名称相似度上界添加稀有 token 候选。
    /// </summary>
    /// <param name="left">左侧匹配档案。</param>
    /// <param name="rightProfiles">右侧分组匹配档案。</param>
    /// <param name="queryIndex">右侧 token 查询索引。</param>
    /// <param name="options">匹配选项。</param>
    /// <param name="workspace">稀疏候选工作数组。</param>
    /// <returns>可以安全使用候选剪枝时返回 <see langword="true"/>。</returns>
    private static bool AddNameCandidates(
        DeclarationMatchProfile left,
        IReadOnlyList<DeclarationMatchProfile> rightProfiles,
        SchemaGroupQueryIndex queryIndex,
        AIFunctionMatchOptions options,
        SparseCandidateWorkspace workspace)
    {
        var leftCount = left.NameTokens.Count;
        if (leftCount == 0)
        {
            return true;
        }

        var threshold = options.MinimumMatchScore;
        foreach (var (rightCount, _) in queryIndex.NamesByTokenCount.Buckets)
        {
            if (rightCount == 0)
            {
                continue;
            }

            var smallerCount = Math.Min(leftCount, rightCount);
            var requiredIntersection = (int)Math.Ceiling(
                threshold * smallerCount);
            if (requiredIntersection <= 0 ||
                requiredIntersection > leftCount)
            {
                if (requiredIntersection <= 0)
                {
                    return false;
                }

                continue;
            }

            var pivotCount = leftCount - requiredIntersection + 1;
            if (pivotCount <= 0 || pivotCount > leftCount)
            {
                return false;
            }

            AddRareTokenCandidates(
                left.NameTokens,
                pivotCount,
                queryIndex.NamePostings,
                rightProfiles,
                rightCount,
                useDescriptionTokens: false,
                workspace);
        }

        return true;
    }

    /// <summary>
    /// 将左侧最稀有的 token posting 成员加入候选集。
    /// </summary>
    /// <param name="sourceTokens">左侧 token 集合。</param>
    /// <param name="pivotCount">需要选择的稀有 token 数量。</param>
    /// <param name="postings">token 到右侧分组内下标的倒排表。</param>
    /// <param name="rightProfiles">右侧分组匹配档案。</param>
    /// <param name="rightTokenCount">当前右侧 token 数量桶。</param>
    /// <param name="useDescriptionTokens">按描述 token 数量过滤时为 <see langword="true"/>。</param>
    /// <param name="workspace">稀疏候选工作数组。</param>
    private static void AddRareTokenCandidates(
        TokenSet sourceTokens,
        int pivotCount,
        CompactPostingIndex postings,
        IReadOnlyList<DeclarationMatchProfile> rightProfiles,
        int rightTokenCount,
        bool useDescriptionTokens,
        SparseCandidateWorkspace workspace)
    {
        SelectRareTokens(
            sourceTokens.Tokens,
            pivotCount,
            postings,
            workspace.PivotIndexes);
        for (var pivot = 0; pivot < pivotCount; pivot++)
        {
            var token = sourceTokens.Tokens[workspace.PivotIndexes[pivot]];
            var candidates = postings.GetPostings(token);
            foreach (var candidate in candidates)
            {
                var right = rightProfiles[candidate];
                var tokenCount = useDescriptionTokens
                    ? right.DescriptionTokens.Count
                    : right.NameTokens.Count;
                if (tokenCount == rightTokenCount)
                {
                    workspace.AddCandidate(candidate);
                }
            }
        }
    }

    /// <summary>
    /// 从左侧 token 中按 posting 长度选择最稀有的 token。
    /// </summary>
    /// <param name="tokens">左侧排序 token。</param>
    /// <param name="pivotCount">需要选择的 token 数量。</param>
    /// <param name="postings">token 到右侧分组内下标的倒排表。</param>
    /// <param name="pivotIndexes">接收选中 token 下标的池化数组。</param>
    private static void SelectRareTokens(
        ReadOnlySpan<string> tokens,
        int pivotCount,
        CompactPostingIndex postings,
        Span<int> pivotIndexes)
    {
        for (var pivot = 0; pivot < pivotCount; pivot++)
        {
            var bestIndex = -1;
            var bestLength = int.MaxValue;
            for (var tokenIndex = 0; tokenIndex < tokens.Length; tokenIndex++)
            {
                var alreadySelected = false;
                for (var previous = 0; previous < pivot; previous++)
                {
                    if (pivotIndexes[previous] == tokenIndex)
                    {
                        alreadySelected = true;
                        break;
                    }
                }

                if (alreadySelected)
                {
                    continue;
                }

                var length = postings.GetPostingCount(tokens[tokenIndex]);
                if (length < bestLength)
                {
                    bestLength = length;
                    bestIndex = tokenIndex;
                }
            }

            pivotIndexes[pivot] = bestIndex;
        }
    }

    /// <summary>
    /// 构建同一 Schema 分组内经过阈值和歧义过滤的候选分数矩阵。
    /// </summary>
    /// <param name="leftProfiles">左侧全部匹配档案。</param>
    /// <param name="rightProfiles">右侧全部匹配档案。</param>
    /// <param name="options">匹配选项。</param>
    /// <param name="workspace">当前分组的池化工作数组。</param>
    private static void BuildScoreMatrix(
        IReadOnlyList<DeclarationMatchProfile> leftProfiles,
        IReadOnlyList<DeclarationMatchProfile> rightProfiles,
        AIFunctionMatchOptions options,
        MatchWorkspace workspace)
    {
        var columnCount = rightProfiles.Count;
        for (var localLeft = 0; localLeft < leftProfiles.Count; localLeft++)
        {
            var left = leftProfiles[localLeft];
            var bestScore = -1.0;
            var secondScore = -1.0;
            var rowOffset = localLeft * columnCount;

            for (var localRight = 0; localRight < columnCount; localRight++)
            {
                var right = rightProfiles[localRight];
                var scoreIndex = rowOffset + localRight;
                if (!TryComputeScore(
                    left,
                    right,
                    options,
                    out var score))
                {
                    workspace.Scores[scoreIndex] = -1;
                    continue;
                }

                workspace.Scores[scoreIndex] = score;

                if (score < options.MinimumMatchScore)
                {
                    continue;
                }

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

            }
        }
    }

    /// <summary>
    /// 计算两个预计算档案的最终匹配分数。
    /// </summary>
    /// <param name="expected">权威匹配档案。</param>
    /// <param name="actual">客户端匹配档案。</param>
    /// <param name="options">匹配选项。</param>
    /// <param name="score">接收综合匹配分数。</param>
    /// <returns>描述门禁允许比较时返回 <see langword="true"/>。</returns>
    private static bool TryComputeScore(
        DeclarationMatchProfile expected,
        DeclarationMatchProfile actual,
        AIFunctionMatchOptions options,
        out double score)
    {
        if (!IsDescriptionCompatible(
            expected,
            actual,
            options,
            out var descriptionScore))
        {
            score = 0;
            return false;
        }

        var nameScore = TextSimilarity.ComputeNameSimilarity(
            expected.NameTokens,
            actual.NameTokens);
        score = expected.HasDescription
            ? (0.75 * nameScore) + (0.25 * descriptionScore)
            : nameScore;
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

using Microsoft.Extensions.AI;

namespace AIFunctionSetMatcher.Internal;

/// <summary>
/// 使用已缓存索引侧和流式探测声明执行 Schema 与描述硬过滤匹配。
/// </summary>
internal static class DeclarationMatcher
{
    private const double InvalidCost = 1_000;

    /// <summary>
    /// 计算两个已建立索引集合的左侧匹配下标标记。
    /// </summary>
    /// <param name="left">左侧函数声明匹配索引。</param>
    /// <param name="right">右侧函数声明匹配索引。</param>
    /// <param name="options">匹配选项。</param>
    /// <param name="matchedLeft">接收左侧匹配下标的栈上标记。</param>
    public static void FindMatchedIndexes(
        DeclarationMatchIndex left,
        DeclarationMatchIndex right,
        AIFunctionMatchOptions options,
        Span<bool> matchedLeft)
    {
        if (matchedLeft.IsEmpty || left.Count == 0 || right.Count == 0)
        {
            return;
        }

        ValidateIndexedSchemaSizes(left, right, options.MaximumSchemaBytes);
        foreach (var (fingerprint, leftGroup) in left.GroupsByFingerprint)
        {
            if (!right.GroupsByFingerprint.TryGetValue(
                fingerprint,
                out var rightGroup))
            {
                continue;
            }

            MatchIndexedDescriptionBuckets(
                leftGroup,
                rightGroup,
                matchedLeft);
        }
    }

    /// <summary>
    /// 计算流式探测声明中与索引目标匹配的左侧下标标记。
    /// </summary>
    /// <param name="target">已建立匹配索引的一侧。</param>
    /// <param name="probeDeclarations">逐条探测的声明。</param>
    /// <param name="probeIsLeft">探测声明属于调用左侧时为 <see langword="true"/>。</param>
    /// <param name="options">匹配选项。</param>
    /// <param name="matchedLeft">接收调用左侧匹配下标的栈上标记。</param>
    public static void FindMatchedIndexes(
        DeclarationMatchIndex target,
        ReadOnlySpan<AIFunctionDeclaration> probeDeclarations,
        bool probeIsLeft,
        AIFunctionMatchOptions options,
        Span<bool> matchedLeft)
    {
        if (matchedLeft.IsEmpty ||
            target.Count == 0 ||
            probeDeclarations.IsEmpty)
        {
            return;
        }

        ValidateTargetSchemaSizes(target, options.MaximumSchemaBytes);

        using var schemaWorkspace = new SchemaSignatureWorkspace();
        using var descriptionWorkspace = new DescriptionFingerprintWorkspace();
        var signatureCache = new SchemaSignatureCache();
        ProbeEntry?[] firstProbeByTarget = new ProbeEntry?[target.Count];
        Dictionary<DescriptionBucketKey, List<ProbeEntry>>? ambiguousBuckets = null;
        for (var probeIndex = 0;
             probeIndex < probeDeclarations.Length;
             probeIndex++)
        {
            var declaration = probeDeclarations[probeIndex];
            var signature = signatureCache.GetOrCompute(
                declaration,
                schemaWorkspace);
            ValidateProbeSchemaSize(
                declaration,
                signature.Utf8ByteCount,
                options.MaximumSchemaBytes);
            var descriptionFingerprint =
                TextSimilarity.ComputeDescriptionFingerprint(
                    declaration.Description,
                    descriptionWorkspace);
            if (!target.GroupsByFingerprint.TryGetValue(
                    signature.Fingerprint,
                    out var targetGroup))
            {
                continue;
            }

            var targetBuckets = targetGroup.DescriptionBuckets;
            if (!targetBuckets.BucketSlots.TryGetValue(
                    descriptionFingerprint,
                    out var targetSlot))
            {
                continue;
            }

            var targetProfileIndexes =
                targetBuckets.GetProfileIndexes(targetSlot);
            var key = new DescriptionBucketKey(
                signature.Fingerprint,
                targetSlot);
            var probeEntry = new ProbeEntry(
                declaration,
                probeIndex,
                signature.Fingerprint,
                signature.Utf8ByteCount,
                descriptionFingerprint);
            if (targetProfileIndexes.Length > 1)
            {
                AddAmbiguousProbe(
                    ref ambiguousBuckets,
                    key,
                    probeEntry);
                continue;
            }

            if (ambiguousBuckets is not null &&
                ambiguousBuckets.TryGetValue(key, out var existingEntries))
            {
                existingEntries.Add(probeEntry);
                continue;
            }

            var targetGlobalIndex =
                targetGroup.GlobalIndexes[targetProfileIndexes[0]];
            var firstProbe = firstProbeByTarget[targetGlobalIndex];
            if (firstProbe is null)
            {
                firstProbeByTarget[targetGlobalIndex] = probeEntry;
                MarkTentativeMatch(
                    probeEntry,
                    targetGlobalIndex,
                    probeIsLeft,
                    matchedLeft);
                continue;
            }

            ClearTentativeMatch(
                firstProbe.Value,
                targetGlobalIndex,
                probeIsLeft,
                matchedLeft);
            AddAmbiguousProbe(
                ref ambiguousBuckets,
                key,
                firstProbe.Value);
            AddAmbiguousProbe(
                ref ambiguousBuckets,
                key,
                probeEntry);
        }

        if (ambiguousBuckets is null)
        {
            return;
        }

        foreach (var (key, probeEntries) in ambiguousBuckets)
        {
            MatchAmbiguousBucket(
                target,
                key,
                probeEntries,
                probeIsLeft,
                matchedLeft);
        }
    }

    /// <summary>
    /// 匹配两个已建立索引的 Schema 分组中相同描述桶。
    /// </summary>
    /// <param name="leftGroup">左侧 Schema 分组。</param>
    /// <param name="rightGroup">右侧 Schema 分组。</param>
    /// <param name="matchedLeft">左侧匹配下标标记。</param>
    private static void MatchIndexedDescriptionBuckets(
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

            var leftIndexes = leftBuckets.GetProfileIndexes(leftSlot);
            var rightIndexes = rightBuckets.GetProfileIndexes(rightSlot);
            if (leftIndexes.Length == 1 && rightIndexes.Length == 1)
            {
                matchedLeft[leftGroup.GlobalIndexes[leftIndexes[0]]] = true;
                continue;
            }

            MatchIndexedBucket(
                leftGroup,
                rightGroup,
                leftIndexes,
                rightIndexes,
                matchedLeft);
        }
    }

    /// <summary>
    /// 使用两个已缓存分组为重复描述桶执行名称分配。
    /// </summary>
    /// <param name="leftGroup">左侧 Schema 分组。</param>
    /// <param name="rightGroup">右侧 Schema 分组。</param>
    /// <param name="leftIndexes">左侧描述桶下标。</param>
    /// <param name="rightIndexes">右侧描述桶下标。</param>
    /// <param name="matchedLeft">左侧匹配下标标记。</param>
    private static void MatchIndexedBucket(
        SchemaGroupIndex leftGroup,
        SchemaGroupIndex rightGroup,
        ReadOnlySpan<int> leftIndexes,
        ReadOnlySpan<int> rightIndexes,
        Span<bool> matchedLeft)
    {
        using var workspace = new MatchWorkspace(
            leftIndexes.Length,
            rightIndexes.Length);
        BuildNameScoreMatrix(
            leftGroup.Profiles,
            leftIndexes,
            rightGroup.Profiles,
            rightIndexes,
            workspace);
        Span<int> rightByLeft = stackalloc int[leftIndexes.Length];
        rightByLeft.Fill(-1);
        MaximumWeightAssignment.Find(
            leftIndexes.Length,
            rightIndexes.Length,
            workspace,
            rightByLeft);
        for (var row = 0; row < leftIndexes.Length; row++)
        {
            matchedLeft[leftGroup.GlobalIndexes[leftIndexes[row]]] =
                rightByLeft[row] >= 0;
        }
    }

    /// <summary>
    /// 将探测项加入需要名称分配的描述桶。
    /// </summary>
    /// <param name="ambiguousBuckets">需要名称分配的描述桶集合。</param>
    /// <param name="key">目标描述桶键。</param>
    /// <param name="probeEntry">要加入的探测项。</param>
    private static void AddAmbiguousProbe(
        ref Dictionary<DescriptionBucketKey, List<ProbeEntry>>?
            ambiguousBuckets,
        DescriptionBucketKey key,
        ProbeEntry probeEntry)
    {
        ambiguousBuckets ??=
            new Dictionary<DescriptionBucketKey, List<ProbeEntry>>();
        if (!ambiguousBuckets.TryGetValue(key, out var entries))
        {
            entries = [];
            ambiguousBuckets.Add(key, entries);
        }

        entries.Add(probeEntry);
    }

    /// <summary>
    /// 暂定标记单声明描述桶的匹配。
    /// </summary>
    /// <param name="probeEntry">探测项。</param>
    /// <param name="targetGlobalIndex">目标侧全局下标。</param>
    /// <param name="probeIsLeft">探测侧是否为调用左侧。</param>
    /// <param name="matchedLeft">调用左侧匹配标记。</param>
    private static void MarkTentativeMatch(
        ProbeEntry probeEntry,
        int targetGlobalIndex,
        bool probeIsLeft,
        Span<bool> matchedLeft)
    {
        matchedLeft[
            probeIsLeft ? probeEntry.ProbeIndex : targetGlobalIndex] = true;
    }

    /// <summary>
    /// 撤销单声明描述桶的暂定匹配。
    /// </summary>
    /// <param name="probeEntry">首个探测项。</param>
    /// <param name="targetGlobalIndex">目标侧全局下标。</param>
    /// <param name="probeIsLeft">探测侧是否为调用左侧。</param>
    /// <param name="matchedLeft">调用左侧匹配标记。</param>
    private static void ClearTentativeMatch(
        ProbeEntry probeEntry,
        int targetGlobalIndex,
        bool probeIsLeft,
        Span<bool> matchedLeft)
    {
        matchedLeft[
            probeIsLeft ? probeEntry.ProbeIndex : targetGlobalIndex] = false;
    }

    /// <summary>
    /// 对需要名称比较的描述桶执行全局一对一分配。
    /// </summary>
    /// <param name="target">已建立匹配索引的一侧。</param>
    /// <param name="key">目标描述桶键。</param>
    /// <param name="probeEntries">该桶中的流式探测项。</param>
    /// <param name="probeIsLeft">探测侧是否为调用左侧。</param>
    /// <param name="matchedLeft">调用左侧匹配标记。</param>
    private static void MatchAmbiguousBucket(
        DeclarationMatchIndex target,
        DescriptionBucketKey key,
        List<ProbeEntry> probeEntries,
        bool probeIsLeft,
        Span<bool> matchedLeft)
    {
        var targetGroup = target.GroupsByFingerprint[key.SchemaFingerprint];
        var targetProfileIndexes =
            targetGroup.DescriptionBuckets.GetProfileIndexes(key.Slot);
        var probeProfiles = CreateProbeProfiles(probeEntries);
        Span<int> probeProfileIndexes = stackalloc int[probeEntries.Count];
        for (var index = 0; index < probeProfileIndexes.Length; index++)
        {
            probeProfileIndexes[index] = index;
        }

        var rowCount = probeIsLeft
            ? probeProfileIndexes.Length
            : targetProfileIndexes.Length;
        var columnCount = probeIsLeft
            ? targetProfileIndexes.Length
            : probeProfileIndexes.Length;
        using var workspace = new MatchWorkspace(rowCount, columnCount);
        if (probeIsLeft)
        {
            BuildNameScoreMatrix(
                probeProfiles,
                probeProfileIndexes,
                targetGroup.Profiles,
                targetProfileIndexes,
                workspace);
        }
        else
        {
            BuildNameScoreMatrix(
                targetGroup.Profiles,
                targetProfileIndexes,
                probeProfiles,
                probeProfileIndexes,
                workspace);
        }

        Span<int> rightByLeft = stackalloc int[rowCount];
        rightByLeft.Fill(-1);
        MaximumWeightAssignment.Find(
            rowCount,
            columnCount,
            workspace,
            rightByLeft);

        for (var row = 0; row < rowCount; row++)
        {
            var leftIndex = probeIsLeft
                ? probeEntries[row].ProbeIndex
                : targetGroup.GlobalIndexes[targetProfileIndexes[row]];
            matchedLeft[leftIndex] = rightByLeft[row] >= 0;
        }
    }

    /// <summary>
    /// 为描述桶中的流式探测声明创建名称 token profile。
    /// </summary>
    /// <param name="probeEntries">探测项集合。</param>
    /// <returns>与探测顺序一致的名称 profile。</returns>
    private static DeclarationMatchProfile[] CreateProbeProfiles(
        IReadOnlyList<ProbeEntry> probeEntries)
    {
        var result = new DeclarationMatchProfile[probeEntries.Count];
        var interner = probeEntries.Count >= 16
            ? new TokenInterner()
            : null;
        var arena = new TokenArena();
        try
        {
            for (var index = 0; index < probeEntries.Count; index++)
            {
                var entry = probeEntries[index];
                result[index] = DeclarationMatchProfile.Create(
                    entry.Declaration,
                    entry.SchemaFingerprint,
                    entry.SchemaByteCount,
                    arena,
                    interner);
            }

            arena.Seal();
            return result;
        }
        catch
        {
            arena.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 验证两个已建立索引集合的全部 Schema 大小。
    /// </summary>
    /// <param name="left">左侧函数声明匹配索引。</param>
    /// <param name="right">右侧函数声明匹配索引。</param>
    /// <param name="maximumSchemaBytes">当前调用允许的最大 Schema UTF-8 字节数。</param>
    private static void ValidateIndexedSchemaSizes(
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
    /// 验证索引目标侧全部 Schema 的大小。
    /// </summary>
    /// <param name="target">已建立匹配索引的一侧。</param>
    /// <param name="maximumSchemaBytes">当前调用允许的最大 Schema UTF-8 字节数。</param>
    private static void ValidateTargetSchemaSizes(
        DeclarationMatchIndex target,
        int maximumSchemaBytes)
    {
        if (target.MaximumSchemaByteCount <= maximumSchemaBytes)
        {
            return;
        }

        for (var index = 0; index < target.Count; index++)
        {
            var entry = target.GetEntry(index);
            if (entry.SchemaByteCount > maximumSchemaBytes)
            {
                throw new AIFunctionDeclarationSetParseException(
                    $"函数“{entry.Declaration.Name}”的 JsonSchema 超过 {maximumSchemaBytes} 字节限制。");
            }
        }
    }

    /// <summary>
    /// 验证单个流式探测声明的 Schema 大小。
    /// </summary>
    /// <param name="declaration">流式探测声明。</param>
    /// <param name="schemaByteCount">Schema 原始 UTF-8 字节数。</param>
    /// <param name="maximumSchemaBytes">当前调用允许的最大 Schema UTF-8 字节数。</param>
    private static void ValidateProbeSchemaSize(
        AIFunctionDeclaration declaration,
        int schemaByteCount,
        int maximumSchemaBytes)
    {
        if (schemaByteCount > maximumSchemaBytes)
        {
            throw new AIFunctionDeclarationSetParseException(
                $"函数“{declaration.Name}”的 JsonSchema 超过 {maximumSchemaBytes} 字节限制。");
        }
    }

    /// <summary>
    /// 为描述桶内全部合法配对构建名称相似度矩阵。
    /// </summary>
    /// <param name="leftProfiles">左侧匹配档案集合。</param>
    /// <param name="leftProfileIndexes">左侧描述桶中的 profile 下标。</param>
    /// <param name="rightProfiles">右侧匹配档案集合。</param>
    /// <param name="rightProfileIndexes">右侧描述桶中的 profile 下标。</param>
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
    /// 保存流式探测声明的硬过滤键和原始下标。
    /// </summary>
    private readonly record struct ProbeEntry(
        AIFunctionDeclaration Declaration,
        int ProbeIndex,
        SchemaFingerprint SchemaFingerprint,
        int SchemaByteCount,
        DescriptionFingerprint DescriptionFingerprint);

    /// <summary>
    /// 标识索引目标侧的 Schema 和描述桶。
    /// </summary>
    private readonly record struct DescriptionBucketKey(
        SchemaFingerprint SchemaFingerprint,
        int Slot);

    /// <summary>
    /// 计算最大匹配数优先、名称相似度总和其次的全局一对一分配。
    /// </summary>
    private static class MaximumWeightAssignment
    {
        /// <summary>
        /// 查找描述桶内的全局匹配对，并为每个左侧行写入右侧列。
        /// </summary>
        /// <param name="rowCount">左侧声明数量。</param>
        /// <param name="columnCount">右侧声明数量。</param>
        /// <param name="workspace">当前描述桶的池化工作数组。</param>
        /// <param name="rightByLeft">接收每个左侧行匹配右侧列；未匹配为 -1。</param>
        public static void Find(
            int rowCount,
            int columnCount,
            MatchWorkspace workspace,
            Span<int> rightByLeft)
        {
            if (AssignmentShortcut.TryAssign(
                workspace.Scores.AsSpan(0, rowCount * columnCount),
                rowCount,
                columnCount,
                rightByLeft))
            {
                return;
            }

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

                rightByLeft[row - 1] = column - 1;
            }
        }

        /// <summary>
        /// 使用 Hungarian 算法计算最小成本完全分配。
        /// </summary>
        /// <param name="rowCount">实际左侧行数。</param>
        /// <param name="columnCount">实际右侧列数。</param>
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
        /// <param name="rowCount">实际左侧行数。</param>
        /// <param name="columnCount">实际右侧列数。</param>
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

namespace AIFunctionSetMatcher.Internal;

/// <summary>
/// 使用连续数组保存描述指纹到 Schema 组内声明下标的映射。
/// </summary>
internal sealed class DescriptionBucketIndex
{
    private readonly IReadOnlyDictionary<DescriptionFingerprint, int> _bucketSlots;
    private readonly int[] _offsets;
    private readonly int[] _profileIndexes;

    /// <summary>
    /// 初始化紧凑描述桶索引。
    /// </summary>
    /// <param name="bucketSlots">描述指纹到 bucket slot 的映射。</param>
    /// <param name="offsets">每个 slot 及其结束位置的下标偏移。</param>
    /// <param name="profileIndexes">按 slot 连续保存的组内 profile 下标。</param>
    private DescriptionBucketIndex(
        IReadOnlyDictionary<DescriptionFingerprint, int> bucketSlots,
        int[] offsets,
        int[] profileIndexes)
    {
        _bucketSlots = bucketSlots;
        _offsets = offsets;
        _profileIndexes = profileIndexes;
    }

    /// <summary>
    /// 获取描述指纹到 bucket slot 的映射。
    /// </summary>
    public IReadOnlyDictionary<DescriptionFingerprint, int> BucketSlots
    {
        get
        {
            return _bucketSlots;
        }
    }

    /// <summary>
    /// 获取指定 bucket 中的组内 profile 下标。
    /// </summary>
    /// <param name="slot">描述桶 slot。</param>
    /// <returns>组内 profile 下标跨度。</returns>
    public ReadOnlySpan<int> GetProfileIndexes(int slot)
    {
        return _profileIndexes.AsSpan(
            _offsets[slot],
            _offsets[slot + 1] - _offsets[slot]);
    }

    /// <summary>
    /// 根据 Schema 组条目构建紧凑描述桶索引。
    /// </summary>
    /// <param name="entries">拥有当前 Schema 指纹的全部轻量条目。</param>
    /// <param name="globalIndexes">当前 Schema 组的全局声明下标。</param>
    /// <returns>不可变紧凑描述桶索引。</returns>
    public static DescriptionBucketIndex Create(
        ReadOnlySpan<DeclarationSchemaEntry> entries,
        ReadOnlySpan<int> globalIndexes)
    {
        var bucketSlots =
            new Dictionary<DescriptionFingerprint, int>();
        var counts = new List<int>();
        for (var profileIndex = 0;
             profileIndex < globalIndexes.Length;
             profileIndex++)
        {
            var fingerprint =
                entries[globalIndexes[profileIndex]].DescriptionFingerprint;
            if (!bucketSlots.TryGetValue(fingerprint, out var slot))
            {
                slot = bucketSlots.Count;
                bucketSlots.Add(fingerprint, slot);
                counts.Add(0);
            }

            counts[slot]++;
        }

        var offsets = new int[bucketSlots.Count + 1];
        var cursors = new int[bucketSlots.Count];
        for (var slot = 0; slot < counts.Count; slot++)
        {
            offsets[slot + 1] = offsets[slot] + counts[slot];
            cursors[slot] = offsets[slot];
        }

        var profileIndexes = new int[offsets[^1]];
        for (var profileIndex = 0;
             profileIndex < globalIndexes.Length;
             profileIndex++)
        {
            var fingerprint =
                entries[globalIndexes[profileIndex]].DescriptionFingerprint;
            var slot = bucketSlots[fingerprint];
            profileIndexes[cursors[slot]++] = profileIndex;
        }

        return new DescriptionBucketIndex(
            bucketSlots,
            offsets,
            profileIndexes);
    }
}

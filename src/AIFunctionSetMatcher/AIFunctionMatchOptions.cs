namespace AIFunctionSetMatcher;

/// <summary>
/// 控制函数声明的语义匹配行为。
/// </summary>
public sealed class AIFunctionMatchOptions
{
    /// <summary>
    /// 获取默认匹配选项。
    /// </summary>
    public static AIFunctionMatchOptions Default { get; } = new();

    /// <summary>
    /// 获取或设置候选匹配所需的最低分数。
    /// </summary>
    public double MinimumMatchScore { get; init; } = 0.72;

    /// <summary>
    /// 获取或设置最佳候选相对次优候选所需的最低分数差。
    /// </summary>
    public double MinimumScoreGap { get; init; } = 0.08;

    /// <summary>
    /// 获取或设置两个非空顶层工具描述所需的最低 Jaccard 相似度。
    /// </summary>
    public double MinimumDescriptionSimilarity { get; init; } = 0.95;

    /// <summary>
    /// 获取或设置单个 JSON Schema 允许的最大 UTF-8 字节数。
    /// </summary>
    public int MaximumSchemaBytes { get; init; } = 256 * 1024;

    /// <summary>
    /// 验证匹配选项的取值范围。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">任一选项超出允许范围。</exception>
    internal void Validate()
    {
        if (!double.IsFinite(MinimumMatchScore) ||
            MinimumMatchScore is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MinimumMatchScore),
                "最低匹配分数必须位于 0 到 1 之间。");
        }

        if (!double.IsFinite(MinimumScoreGap) ||
            MinimumScoreGap is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MinimumScoreGap),
                "最低分数差必须位于 0 到 1 之间。");
        }

        if (!double.IsFinite(MinimumDescriptionSimilarity) ||
            MinimumDescriptionSimilarity is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MinimumDescriptionSimilarity),
                "最低描述相似度必须位于 0 到 1 之间。");
        }

        if (MaximumSchemaBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumSchemaBytes),
                "最大 Schema 字节数必须大于 0。");
        }
    }
}

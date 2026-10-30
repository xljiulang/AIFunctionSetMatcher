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
    /// 获取或设置单个 JSON Schema 允许的最大 UTF-8 字节数。
    /// </summary>
    public int MaximumSchemaBytes { get; init; } = 256 * 1024;

    /// <summary>
    /// 验证匹配选项的取值范围。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">任一选项超出允许范围。</exception>
    internal void Validate()
    {
        if (MaximumSchemaBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumSchemaBytes),
                "最大 Schema 字节数必须大于 0。");
        }
    }
}

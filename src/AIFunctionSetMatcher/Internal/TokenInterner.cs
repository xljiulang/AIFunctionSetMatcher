namespace AIFunctionSetMatcher.Internal;

/// <summary>
/// 在单个 Schema 组件档期间复用相同 token 字符串实例。
/// </summary>
internal sealed class TokenInterner
{
    private readonly Dictionary<string, string> tokens =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>>
        lookup;

    /// <summary>
    /// 初始化 token 驻留器及其跨度查询视图。
    /// </summary>
    public TokenInterner()
    {
        lookup = tokens.GetAlternateLookup<ReadOnlySpan<char>>();
    }

    /// <summary>
    /// 返回规范化 token 的组内唯一字符串实例。
    /// </summary>
    /// <param name="token">要驻留的规范化 token 字符跨度。</param>
    /// <returns>已存在的相同字符串或新加入的字符串。</returns>
    public string Intern(scoped ReadOnlySpan<char> token)
    {
        if (lookup.TryGetValue(token, out var existing))
        {
            return existing;
        }

        var value = token.ToString();
        tokens.Add(value, value);
        return value;
    }

    /// <summary>
    /// 返回 token 字符串的组内唯一实例。
    /// </summary>
    /// <param name="token">要驻留的 token。</param>
    /// <returns>已存在的相同字符串或新加入的字符串。</returns>
    public string Intern(string token)
    {
        return Intern(token.AsSpan());
    }
}

namespace AIFunctionSetMatcher.Internal;

/// <summary>
/// 在单个 Schema 组件档期间复用相同 token 字符串实例。
/// </summary>
internal sealed class TokenInterner
{
    private readonly Dictionary<string, string> tokens =
        new(StringComparer.Ordinal);

    /// <summary>
    /// 返回 token 的组内唯一字符串实例。
    /// </summary>
    /// <param name="token">要驻留的 token。</param>
    /// <returns>已存在的相同字符串或新加入的字符串。</returns>
    public string Intern(string token)
    {
        if (tokens.TryGetValue(token, out var existing))
        {
            return existing;
        }

        tokens.Add(token, token);
        return token;
    }
}

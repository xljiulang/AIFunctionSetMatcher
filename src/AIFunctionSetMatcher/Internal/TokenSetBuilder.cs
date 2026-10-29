namespace AIFunctionSetMatcher.Internal;

/// <summary>
/// 以低数量线性去重、高数量哈希去重的方式构建 <see cref="TokenSet"/>。
/// </summary>
internal sealed class TokenSetBuilder
{
    private const int LinearDeduplicationLimit = 16;
    private readonly List<string> tokens = [];
    private HashSet<string>? tokenIndex;

    /// <summary>
    /// 添加一个非空 token。
    /// </summary>
    /// <param name="token">要添加的 token。</param>
    public void Add(string token)
    {
        if (token.Length == 0)
        {
            return;
        }

        if (tokenIndex is not null)
        {
            if (tokenIndex.Add(token))
            {
                tokens.Add(token);
            }

            return;
        }

        foreach (var existing in tokens)
        {
            if (string.Equals(existing, token, StringComparison.Ordinal))
            {
                return;
            }
        }

        tokens.Add(token);
        if (tokens.Count <= LinearDeduplicationLimit)
        {
            return;
        }

        tokenIndex = new HashSet<string>(tokens, StringComparer.Ordinal);
    }

    /// <summary>
    /// 构建按序号排序且已去重的不可变 token 集合。
    /// </summary>
    /// <returns>新的文本 token 集合。</returns>
    public TokenSet Build()
    {
        var sortedTokens = tokens.ToArray();
        Array.Sort(sortedTokens, StringComparer.Ordinal);
        return new TokenSet(sortedTokens);
    }
}

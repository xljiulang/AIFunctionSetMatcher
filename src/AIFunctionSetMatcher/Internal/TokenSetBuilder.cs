using System.Buffers;

namespace AIFunctionSetMatcher.Internal;

/// <summary>
/// 使用池化字符串数组构建 <see cref="TokenSet"/>。
/// </summary>
internal ref struct TokenSetBuilder
{
    private const int LinearDeduplicationLimit = 16;
    private string[]? buffer;
    private int count;
    private HashSet<string>? tokenIndex;
    private TokenInterner? interner;

    /// <summary>
    /// 初始化使用共享数组池的 token 集合构建器。
    /// </summary>
    /// <param name="tokenInterner">可选的组内 token 驻留器。</param>
    public TokenSetBuilder(TokenInterner? tokenInterner)
    {
        buffer = ArrayPool<string>.Shared.Rent(16);
        count = 0;
        tokenIndex = null;
        interner = tokenInterner;
    }

    /// <summary>
    /// 添加一个非空 token。
    /// </summary>
    /// <param name="token">要添加的 token。</param>
    public void Add(scoped ReadOnlySpan<char> token)
    {
        if (token.Length == 0)
        {
            return;
        }

        var value = interner?.Intern(token) ?? token.ToString();
        if (tokenIndex is not null)
        {
            if (tokenIndex.Add(value))
            {
                AddToBuffer(value);
            }

            return;
        }

        for (var index = 0; index < count; index++)
        {
            if (string.Equals(buffer![index], value, StringComparison.Ordinal))
            {
                return;
            }
        }

        AddToBuffer(value);
        if (count <= LinearDeduplicationLimit)
        {
            return;
        }

        tokenIndex = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < count; index++)
        {
            tokenIndex.Add(buffer![index]);
        }
    }

    /// <summary>
    /// 添加一个非空 token 字符串。
    /// </summary>
    /// <param name="token">要添加的 token。</param>
    public void Add(string token)
    {
        Add(token.AsSpan());
    }

    /// <summary>
    /// 构建按序号排序且已去重的不可变 token 集合。
    /// </summary>
    /// <param name="arena">接收有序 token 的 Schema 组 Arena。</param>
    /// <returns>新的文本 token 集合。</returns>
    public TokenSet Build(TokenArena arena)
    {
        var sortedTokens = GetSortedTokens();
        var offset = arena.Append(sortedTokens);
        return new TokenSet(arena, offset, count);
    }

    /// <summary>
    /// 获取按序号排序且已去重的临时 token 跨度。
    /// </summary>
    /// <returns>仅在当前构建器释放前有效的 token 跨度。</returns>
    public ReadOnlySpan<string> GetSortedTokens()
    {
        var sortedTokens = buffer.AsSpan(0, count);
        if (count > 0)
        {
            sortedTokens.Sort(StringComparer.Ordinal);
        }

        return sortedTokens;
    }

    /// <summary>
    /// 将池化工作数组归还共享数组池。
    /// </summary>
    public void Dispose()
    {
        if (buffer is null)
        {
            return;
        }

        ArrayPool<string>.Shared.Return(buffer);
        buffer = null;
        tokenIndex = null;
        interner = null;
    }

    /// <summary>
    /// 将 token 写入池化数组，并在需要时扩容。
    /// </summary>
    /// <param name="token">要写入的 token。</param>
    private void AddToBuffer(string token)
    {
        if (buffer is null || count == buffer.Length)
        {
            GrowBuffer();
        }

        buffer![count++] = token;
    }

    /// <summary>
    /// 将池化 token 数组扩容一倍。
    /// </summary>
    private void GrowBuffer()
    {
        var current = buffer ?? [];
        var replacement = ArrayPool<string>.Shared.Rent(
            Math.Max(current.Length * 2, 16));
        current.AsSpan(0, count).CopyTo(replacement);
        if (buffer is not null)
        {
            ArrayPool<string>.Shared.Return(buffer);
        }

        buffer = replacement;
    }
}

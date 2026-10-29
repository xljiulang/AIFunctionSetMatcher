using System.Buffers.Binary;

namespace AIFunctionSetMatcher.Internal;

/// <summary>
/// 表示由 SHA-256 摘要构造的稳定 Schema 语义指纹。
/// </summary>
internal readonly struct SchemaFingerprint : IEquatable<SchemaFingerprint>
{
    private readonly ulong first;
    private readonly ulong second;
    private readonly ulong third;
    private readonly ulong fourth;

    /// <summary>
    /// 初始化 Schema 语义指纹。
    /// </summary>
    /// <param name="first">摘要前八个字节。</param>
    /// <param name="second">摘要第九至十六个字节。</param>
    /// <param name="third">摘要第十七至二十四个字节。</param>
    /// <param name="fourth">摘要最后八个字节。</param>
    private SchemaFingerprint(
        ulong first,
        ulong second,
        ulong third,
        ulong fourth)
    {
        this.first = first;
        this.second = second;
        this.third = third;
        this.fourth = fourth;
    }

    /// <summary>
    /// 从 32 字节 SHA-256 摘要创建 Schema 语义指纹。
    /// </summary>
    /// <param name="digest">SHA-256 摘要。</param>
    /// <returns>Schema 语义指纹。</returns>
    public static SchemaFingerprint FromDigest(ReadOnlySpan<byte> digest)
    {
        if (digest.Length != 32)
        {
            throw new ArgumentException("SHA-256 摘要必须正好为 32 字节。", nameof(digest));
        }

        return new SchemaFingerprint(
            BinaryPrimitives.ReadUInt64LittleEndian(digest),
            BinaryPrimitives.ReadUInt64LittleEndian(digest[8..]),
            BinaryPrimitives.ReadUInt64LittleEndian(digest[16..]),
            BinaryPrimitives.ReadUInt64LittleEndian(digest[24..]));
    }

    /// <summary>
    /// 判断当前指纹是否等于另一个指纹。
    /// </summary>
    /// <param name="other">另一个 Schema 语义指纹。</param>
    /// <returns>四个摘要分段全部相等时返回 <see langword="true"/>。</returns>
    public bool Equals(SchemaFingerprint other)
    {
        return first == other.first &&
            second == other.second &&
            third == other.third &&
            fourth == other.fourth;
    }

    /// <summary>
    /// 判断当前指纹是否等于指定对象。
    /// </summary>
    /// <param name="obj">要比较的对象。</param>
    /// <returns>对象为相同指纹且摘要分段全部相等时返回 <see langword="true"/>。</returns>
    public override bool Equals(object? obj)
    {
        return obj is SchemaFingerprint other && Equals(other);
    }

    /// <summary>
    /// 计算当前指纹的哈希码。
    /// </summary>
    /// <returns>四个摘要分段组合得到的哈希码。</returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(first, second, third, fourth);
    }

    /// <summary>
    /// 判断两个 Schema 语义指纹是否相等。
    /// </summary>
    /// <param name="left">左侧指纹。</param>
    /// <param name="right">右侧指纹。</param>
    /// <returns>两个指纹摘要分段全部相等时返回 <see langword="true"/>。</returns>
    public static bool operator ==(SchemaFingerprint left, SchemaFingerprint right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// 判断两个 Schema 语义指纹是否不相等。
    /// </summary>
    /// <param name="left">左侧指纹。</param>
    /// <param name="right">右侧指纹。</param>
    /// <returns>两个指纹任一摘要分段不同时返回 <see langword="true"/>。</returns>
    public static bool operator !=(SchemaFingerprint left, SchemaFingerprint right)
    {
        return !left.Equals(right);
    }
}

using System.Buffers.Binary;

namespace AIFunctionSetMatcher.Internal;

/// <summary>
/// 表示由规范化描述 token 集合构造的稳定语义指纹。
/// </summary>
internal readonly struct DescriptionFingerprint : IEquatable<DescriptionFingerprint>
{
    private readonly ulong _first;
    private readonly ulong _second;
    private readonly ulong _third;
    private readonly ulong _fourth;

    /// <summary>
    /// 初始化描述语义指纹。
    /// </summary>
    /// <param name="first">摘要前八个字节。</param>
    /// <param name="second">摘要第九至十六个字节。</param>
    /// <param name="third">摘要第十七至二十四个字节。</param>
    /// <param name="fourth">摘要最后八个字节。</param>
    private DescriptionFingerprint(
        ulong first,
        ulong second,
        ulong third,
        ulong fourth)
    {
        _first = first;
        _second = second;
        _third = third;
        _fourth = fourth;
    }

    /// <summary>
    /// 从 32 字节 SHA-256 摘要创建描述语义指纹。
    /// </summary>
    /// <param name="digest">SHA-256 摘要。</param>
    /// <returns>描述语义指纹。</returns>
    public static DescriptionFingerprint FromDigest(ReadOnlySpan<byte> digest)
    {
        if (digest.Length != 32)
        {
            throw new ArgumentException(
                "SHA-256 摘要必须正好为 32 字节。",
                nameof(digest));
        }

        return new DescriptionFingerprint(
            BinaryPrimitives.ReadUInt64LittleEndian(digest),
            BinaryPrimitives.ReadUInt64LittleEndian(digest[8..]),
            BinaryPrimitives.ReadUInt64LittleEndian(digest[16..]),
            BinaryPrimitives.ReadUInt64LittleEndian(digest[24..]));
    }

    /// <summary>
    /// 判断当前指纹是否等于另一个指纹。
    /// </summary>
    /// <param name="other">另一个描述语义指纹。</param>
    /// <returns>四个摘要分段全部相等时返回 <see langword="true"/>。</returns>
    public bool Equals(DescriptionFingerprint other)
    {
        return _first == other._first &&
            _second == other._second &&
            _third == other._third &&
            _fourth == other._fourth;
    }

    /// <summary>
    /// 判断当前指纹是否等于指定对象。
    /// </summary>
    /// <param name="obj">要比较的对象。</param>
    /// <returns>对象为相同描述指纹时返回 <see langword="true"/>。</returns>
    public override bool Equals(object? obj)
    {
        return obj is DescriptionFingerprint other && Equals(other);
    }

    /// <summary>
    /// 计算当前指纹的哈希码。
    /// </summary>
    /// <returns>四个摘要分段组合得到的哈希码。</returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(_first, _second, _third, _fourth);
    }

    /// <summary>
    /// 判断两个描述语义指纹是否相等。
    /// </summary>
    /// <param name="left">左侧描述语义指纹。</param>
    /// <param name="right">右侧描述语义指纹。</param>
    /// <returns>两个指纹相等时返回 <see langword="true"/>。</returns>
    public static bool operator ==(
        DescriptionFingerprint left,
        DescriptionFingerprint right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// 判断两个描述语义指纹是否不相等。
    /// </summary>
    /// <param name="left">左侧描述语义指纹。</param>
    /// <param name="right">右侧描述语义指纹。</param>
    /// <returns>两个指纹不相等时返回 <see langword="true"/>。</returns>
    public static bool operator !=(
        DescriptionFingerprint left,
        DescriptionFingerprint right)
    {
        return !left.Equals(right);
    }
}

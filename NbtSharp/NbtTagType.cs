namespace NbtSharp;

/// <summary>
/// Identifies an NBT tag type by its numeric identifier.
/// </summary>
/// <remarks>
/// The values match the tag identifiers used by the NBT binary format. <see cref="End"/> is a
/// structural terminator and is never the type of an <see cref="NbtValue"/>.
/// </remarks>
public enum NbtTagType : byte
{
    /// <summary>The terminator tag that closes a compound or describes an empty list.</summary>
    End = 0,

    /// <summary>A signed 8-bit integer.</summary>
    Byte = 1,

    /// <summary>A signed 16-bit integer.</summary>
    Short = 2,

    /// <summary>A signed 32-bit integer.</summary>
    Int = 3,

    /// <summary>A signed 64-bit integer.</summary>
    Long = 4,

    /// <summary>A 32-bit IEEE 754 floating point number.</summary>
    Float = 5,

    /// <summary>A 64-bit IEEE 754 floating point number.</summary>
    Double = 6,

    /// <summary>An array of signed 8-bit integers.</summary>
    ByteArray = 7,

    /// <summary>A length-prefixed string.</summary>
    String = 8,

    /// <summary>A homogeneous list of values.</summary>
    List = 9,

    /// <summary>A named collection of heterogeneous values.</summary>
    Compound = 10,

    /// <summary>An array of signed 32-bit integers.</summary>
    IntArray = 11,

    /// <summary>An array of signed 64-bit integers.</summary>
    LongArray = 12,
}

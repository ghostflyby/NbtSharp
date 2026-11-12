namespace NbtSharp;

/// <summary>
/// Represents the value of an NBT tag.
/// </summary>
/// <remarks>
/// <para>
/// Each case corresponds to exactly one scalar, array, list, or compound tag type. Scalars use the
/// CLR types whose width and signedness match the NBT type, so the distinction between, for
/// example, <c>TAG_Short</c> and <c>TAG_Int</c> is preserved in the type system.
/// </para>
/// <para>
/// <see cref="NbtTagType.End"/> is a structural terminator rather than a value, so it has no case
/// here. Tag names live on the enclosing compound or on the root tag, not on the value.
/// </para>
/// <para>
/// A union declaration stores its contents as a single boxed reference. A default-initialized
/// value therefore has no case; inspecting <see cref="TagType"/> on it throws <see cref="NbtException"/>.
/// </para>
/// </remarks>
public union NbtValue(
    sbyte,
    short,
    int,
    long,
    float,
    double,
    sbyte[],
    string,
    NbtList,
    NbtCompound,
    int[],
    long[]
)
{
    /// <summary>Gets the tag type that this value encodes as.</summary>
    /// <exception cref="NbtException">The union holds no value.</exception>
    public NbtTagType TagType => this switch
    {
        sbyte => NbtTagType.Byte,
        short => NbtTagType.Short,
        int => NbtTagType.Int,
        long => NbtTagType.Long,
        float => NbtTagType.Float,
        double => NbtTagType.Double,
        sbyte[] => NbtTagType.ByteArray,
        string => NbtTagType.String,
        NbtList => NbtTagType.List,
        NbtCompound => NbtTagType.Compound,
        int[] => NbtTagType.IntArray,
        long[] => NbtTagType.LongArray,
        null => throw new NbtException("The NBT value is uninitialized."),
    };
}

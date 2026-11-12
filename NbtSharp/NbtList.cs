namespace NbtSharp;

/// <summary>
/// Represents an NBT list, whose elements all share one tag type.
/// </summary>
/// <remarks>
/// <para>
/// Every case is a standard <see cref="List{T}"/> over one of the legal NBT element types, so the
/// generic parameter constrains the elements to be homogeneous while the union's closed case set
/// constrains which element types are legal. Recursion is expressed as <c>List&lt;NbtList&gt;</c>.
/// </para>
/// <para>
/// The element type is part of the list's binary encoding even when the list is empty, so an empty
/// <c>List&lt;int&gt;</c> encodes as <c>TAG_List&lt;TAG_Int&gt;</c> with length 0. The distinct
/// <c>TAG_List&lt;TAG_End&gt;</c> encoding is represented by <see cref="NbtEmptyList"/>.
/// </para>
/// <para>
/// A default-initialized value has no case; inspecting <see cref="ElementType"/> or
/// <see cref="Count"/> on it throws <see cref="NbtException"/>.
/// </para>
/// </remarks>
public union NbtList(
    List<sbyte>,
    List<short>,
    List<int>,
    List<long>,
    List<float>,
    List<double>,
    List<sbyte[]>,
    List<string>,
    List<NbtList>,
    List<NbtCompound>,
    List<int[]>,
    List<long[]>,
    NbtEmptyList
)
{
    /// <summary>Gets the tag type shared by every element of this list.</summary>
    /// <exception cref="NbtException">The union holds no value.</exception>
    public NbtTagType ElementType
    {
        get
        {
            if (Value is null)
            {
                throw new NbtException("The NBT list is uninitialized.");
            }

            return this switch
            {
                List<sbyte> => NbtTagType.Byte,
                List<short> => NbtTagType.Short,
                List<int> => NbtTagType.Int,
                List<long> => NbtTagType.Long,
                List<float> => NbtTagType.Float,
                List<double> => NbtTagType.Double,
                List<sbyte[]> => NbtTagType.ByteArray,
                List<string> => NbtTagType.String,
                List<NbtList> => NbtTagType.List,
                List<NbtCompound> => NbtTagType.Compound,
                List<int[]> => NbtTagType.IntArray,
                List<long[]> => NbtTagType.LongArray,
                NbtEmptyList => NbtTagType.End,
            };
        }
    }

    /// <summary>Gets the number of elements in this list.</summary>
    /// <exception cref="NbtException">The union holds no value.</exception>
    public int Count
    {
        get
        {
            if (Value is null)
            {
                throw new NbtException("The NBT list is uninitialized.");
            }

            return this switch
            {
                List<sbyte> list => list.Count,
                List<short> list => list.Count,
                List<int> list => list.Count,
                List<long> list => list.Count,
                List<float> list => list.Count,
                List<double> list => list.Count,
                List<sbyte[]> list => list.Count,
                List<string> list => list.Count,
                List<NbtList> list => list.Count,
                List<NbtCompound> list => list.Count,
                List<int[]> list => list.Count,
                List<long[]> list => list.Count,
                NbtEmptyList => 0,
            };
        }
    }
}

using System.Runtime.CompilerServices;

namespace NbtSharp;

/// <summary>
/// Represents an NBT list, whose elements all share one tag type.
/// </summary>
/// <remarks>
/// <para>
/// The twelve element cases are standard <see cref="List{T}"/> values over one of the legal NBT
/// element types, so the generic parameter constrains the elements to be homogeneous while the
/// union's closed case set constrains which element types are legal. The thirteenth case is the
/// <see cref="NbtEndList"/> marker. Recursion is expressed as <c>List&lt;NbtList&gt;</c>.
/// </para>
/// <para>
/// This is a hand-written union: a private field holds one of the case lists, and
/// <see cref="Value"/> returns that list or the shared <see cref="NbtEndList.Instance"/> marker for
/// <c>TAG_List&lt;TAG_End&gt;</c>, the empty list whose element type is
/// <see cref="NbtTagType.End"/>. An empty <c>List&lt;int&gt;</c> still encodes as
/// <c>TAG_List&lt;TAG_Int&gt;</c> with length 0, so the two empty-list encodings stay distinct.
/// </para>
/// <para>
/// A default-initialized value stores no list and therefore represents
/// <c>TAG_List&lt;TAG_End&gt;</c>. <see cref="Value"/> never returns <see langword="null"/> and
/// matches the <see cref="NbtEndList"/> case, so <c>default(NbtList) is null</c> is false while
/// <c>default(NbtList) is NbtEndList</c> is true.
/// </para>
/// </remarks>
[Union]
public readonly struct NbtList
{
    /// <summary>Initializes a list that wraps a list of signed 8-bit integers.</summary>
    public NbtList(List<sbyte> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
    }

    /// <summary>Initializes a list that wraps a list of signed 16-bit integers.</summary>
    public NbtList(List<short> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
    }

    /// <summary>Initializes a list that wraps a list of signed 32-bit integers.</summary>
    public NbtList(List<int> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
    }

    /// <summary>Initializes a list that wraps a list of signed 64-bit integers.</summary>
    public NbtList(List<long> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
    }

    /// <summary>Initializes a list that wraps a list of 32-bit IEEE 754 values.</summary>
    public NbtList(List<float> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
    }

    /// <summary>Initializes a list that wraps a list of 64-bit IEEE 754 values.</summary>
    public NbtList(List<double> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
    }

    /// <summary>Initializes a list that wraps a list of byte arrays.</summary>
    public NbtList(List<sbyte[]> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
    }

    /// <summary>Initializes a list that wraps a list of strings.</summary>
    public NbtList(List<string> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
    }

    /// <summary>Initializes a list that wraps a list of nested lists.</summary>
    public NbtList(List<NbtList> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
    }

    /// <summary>Initializes a list that wraps a list of compounds.</summary>
    public NbtList(List<NbtCompound> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
    }

    /// <summary>Initializes a list that wraps a list of integer arrays.</summary>
    public NbtList(List<int[]> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
    }

    /// <summary>Initializes a list that wraps a list of long arrays.</summary>
    public NbtList(List<long[]> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
    }

    /// <summary>Initializes a list whose elements are the <see cref="NbtTagType.End"/> marker.</summary>
    public NbtList(NbtEndList value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _ = value;
        Value = null;
    }

    /// <summary>
    /// Gets the wrapped list, or <see cref="NbtEndList.Instance"/> when this value represents
    /// <c>TAG_List&lt;TAG_End&gt;</c>. The property never returns <see langword="null"/>.
    /// </summary>
    public object Value => field ?? NbtEndList.Instance;

    /// <summary>
    /// Gets a value indicating whether this list holds a value. Always <see langword="true"/>
    /// because a default-initialized value is the empty <c>TAG_List&lt;TAG_End&gt;</c>, not an
    /// absent list.
    /// </summary>
    public bool HasValue => true;

    /// <summary>Gets the tag type shared by every element of this list.</summary>
    /// <remarks><see cref="NbtTagType.End"/> for a default-initialized value.</remarks>
    public NbtTagType ElementType => this switch
    {
        NbtEndList => NbtTagType.End,
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
    };

    /// <summary>Gets the number of elements in this list.</summary>
    /// <remarks>0 for a default-initialized value.</remarks>
    public int Count => this switch
    {
        NbtEndList => 0,
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
    };
}

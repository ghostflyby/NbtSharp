using System.Runtime.CompilerServices;

namespace NbtSharp;

/// <summary>
/// One NBT value: a numeric scalar, a string, a byte/int/long array, a list, or a compound.
/// </summary>
/// <remarks>
/// <para>
/// This is a hand-written union. Numeric cases are stored inline (no boxing). Every other case is a
/// view struct over the document: <see cref="NbtStringElement"/>,
/// <see cref="NbtByteArrayElement"/>, <see cref="NbtIntArrayElement"/>,
/// <see cref="NbtLongArrayElement"/>, <see cref="NbtListElement"/>, and
/// <see cref="NbtCompoundElement"/>; nothing is materialized until a materializing member
/// (<c>GetString</c>, <c>ToArray</c>, …) is called. Enumerating a compound's pairs also
/// materializes each member name as the pair key.
/// </para>
/// <para>
/// Non-boxing access is through <see cref="TagType"/> plus the per-case properties
/// (<see cref="SByte"/>, <see cref="Int32"/>, <see cref="String"/>, <see cref="ByteArray"/>,
/// <see cref="List"/>, …). The <see cref="Value"/> property and language-level pattern matching
/// route through a boxed reference and are the opt-in slow path.
/// </para>
/// <para>
/// Values are views over the owning document for the string, array, list, and compound cases, and
/// throw <see cref="ObjectDisposedException"/> after that document is disposed. Numeric cases are
/// self-contained.
/// </para>
/// <para>
/// A default-initialized value has no case: <see cref="TagType"/> throws
/// <see cref="NbtException"/> and <see cref="Value"/> is <see langword="null"/>, matching
/// <see cref="NbtValue"/>.
/// </para>
/// </remarks>
[Union]
public readonly struct NbtElement
{
    private readonly NbtTagType _tag;
    private readonly long _bits;
    private readonly ReadOnlyMemory<byte> _raw;
    private readonly NbtDocument? _owner;
    private readonly int _slotIndex;

    /// <summary>Initializes a <c>TAG_Byte</c> value.</summary>
    /// <param name="value">The value.</param>
    public NbtElement(sbyte value)
    {
        _tag = NbtTagType.Byte;
        _bits = value;
    }

    /// <summary>Initializes a <c>TAG_Short</c> value.</summary>
    /// <param name="value">The value.</param>
    public NbtElement(short value)
    {
        _tag = NbtTagType.Short;
        _bits = value;
    }

    /// <summary>Initializes a <c>TAG_Int</c> value.</summary>
    /// <param name="value">The value.</param>
    public NbtElement(int value)
    {
        _tag = NbtTagType.Int;
        _bits = value;
    }

    /// <summary>Initializes a <c>TAG_Long</c> value.</summary>
    /// <param name="value">The value.</param>
    public NbtElement(long value)
    {
        _tag = NbtTagType.Long;
        _bits = value;
    }

    /// <summary>Initializes a <c>TAG_Float</c> value.</summary>
    /// <param name="value">The value.</param>
    public NbtElement(float value)
    {
        _tag = NbtTagType.Float;
        _bits = BitConverter.SingleToInt32Bits(value);
    }

    /// <summary>Initializes a <c>TAG_Double</c> value.</summary>
    /// <param name="value">The value.</param>
    public NbtElement(double value)
    {
        _tag = NbtTagType.Double;
        _bits = BitConverter.DoubleToInt64Bits(value);
    }

    /// <summary>Initializes a <c>TAG_String</c> value over encoded modified UTF-8 bytes.</summary>
    /// <param name="value">The string view.</param>
    public NbtElement(NbtStringElement value)
    {
        _tag = NbtTagType.String;
        _bits = value.BytePosition;
        _raw = value.RawMemory;
        _owner = value.Owner;
    }

    /// <summary>Initializes a <c>TAG_Byte_Array</c> value over raw payload bytes.</summary>
    /// <param name="value">The array view.</param>
    public NbtElement(NbtByteArrayElement value)
    {
        _tag = NbtTagType.ByteArray;
        _raw = value.Memory;
        _owner = value.Owner;
    }

    /// <summary>Initializes a <c>TAG_Int_Array</c> value over raw big-endian payload bytes.</summary>
    /// <param name="value">The array view.</param>
    public NbtElement(NbtIntArrayElement value)
    {
        _tag = NbtTagType.IntArray;
        _raw = value.RawMemory;
        _owner = value.Owner;
    }

    /// <summary>Initializes a <c>TAG_Long_Array</c> value over raw big-endian payload bytes.</summary>
    /// <param name="value">The array view.</param>
    public NbtElement(NbtLongArrayElement value)
    {
        _tag = NbtTagType.LongArray;
        _raw = value.RawMemory;
        _owner = value.Owner;
    }

    /// <summary>Initializes a <c>TAG_List</c> value from its document view.</summary>
    /// <param name="value">The list view.</param>
    public NbtElement(NbtListElement value)
    {
        _tag = NbtTagType.List;
        _owner = value.Owner;
        _slotIndex = value.SlotIndex;
    }

    /// <summary>Initializes a <c>TAG_Compound</c> value from its document view.</summary>
    /// <param name="value">The compound view.</param>
    public NbtElement(NbtCompoundElement value)
    {
        _tag = NbtTagType.Compound;
        _owner = value.Owner;
        _slotIndex = value.SlotIndex;
    }

    /// <summary>Implicitly wraps a <c>TAG_Byte</c> value.</summary>
    /// <param name="value">The value.</param>
    public static implicit operator NbtElement(sbyte value) => new(value);

    /// <summary>Implicitly wraps a <c>TAG_Short</c> value.</summary>
    /// <param name="value">The value.</param>
    public static implicit operator NbtElement(short value) => new(value);

    /// <summary>Implicitly wraps a <c>TAG_Int</c> value.</summary>
    /// <param name="value">The value.</param>
    public static implicit operator NbtElement(int value) => new(value);

    /// <summary>Implicitly wraps a <c>TAG_Long</c> value.</summary>
    /// <param name="value">The value.</param>
    public static implicit operator NbtElement(long value) => new(value);

    /// <summary>Implicitly wraps a <c>TAG_Float</c> value.</summary>
    /// <param name="value">The value.</param>
    public static implicit operator NbtElement(float value) => new(value);

    /// <summary>Implicitly wraps a <c>TAG_Double</c> value.</summary>
    /// <param name="value">The value.</param>
    public static implicit operator NbtElement(double value) => new(value);

    /// <summary>Implicitly wraps a <c>TAG_String</c> value.</summary>
    /// <param name="value">The value.</param>
    public static implicit operator NbtElement(NbtStringElement value) => new(value);

    /// <summary>Implicitly wraps a <c>TAG_Byte_Array</c> value.</summary>
    /// <param name="value">The value.</param>
    public static implicit operator NbtElement(NbtByteArrayElement value) => new(value);

    /// <summary>Implicitly wraps a <c>TAG_Int_Array</c> value.</summary>
    /// <param name="value">The value.</param>
    public static implicit operator NbtElement(NbtIntArrayElement value) => new(value);

    /// <summary>Implicitly wraps a <c>TAG_Long_Array</c> value.</summary>
    /// <param name="value">The value.</param>
    public static implicit operator NbtElement(NbtLongArrayElement value) => new(value);

    /// <summary>Implicitly wraps a <c>TAG_List</c> value.</summary>
    /// <param name="value">The value.</param>
    public static implicit operator NbtElement(NbtListElement value) => new(value);

    /// <summary>Implicitly wraps a <c>TAG_Compound</c> value.</summary>
    /// <param name="value">The value.</param>
    public static implicit operator NbtElement(NbtCompoundElement value) => new(value);

    /// <summary>Gets the tag type of this value.</summary>
    /// <exception cref="NbtException">The value is uninitialized.</exception>
    public NbtTagType TagType => _tag == NbtTagType.End
        ? throw new NbtException("The NBT element is uninitialized.")
        : _tag;

    /// <summary>
    /// Gets the boxed case value, or <see langword="null"/> when the value is uninitialized.
    /// This is the slow, allocating path used by the union machinery.
    /// </summary>
    public object? Value => _tag switch
    {
        NbtTagType.Byte => (sbyte)_bits,
        NbtTagType.Short => (short)_bits,
        NbtTagType.Int => (int)_bits,
        NbtTagType.Long => _bits,
        NbtTagType.Float => BitConverter.Int32BitsToSingle((int)_bits),
        NbtTagType.Double => BitConverter.Int64BitsToDouble(_bits),
        NbtTagType.String => new NbtStringElement(_raw, _bits, _owner),
        NbtTagType.ByteArray => new NbtByteArrayElement(_raw, _owner),
        NbtTagType.IntArray => new NbtIntArrayElement(_raw, _owner),
        NbtTagType.LongArray => new NbtLongArrayElement(_raw, _owner),
        NbtTagType.List => new NbtListElement(Owner!, _slotIndex),
        NbtTagType.Compound => new NbtCompoundElement(Owner!, _slotIndex),
        _ => null,
    };

    /// <summary>Gets a <c>TAG_Byte</c> value.</summary>
    /// <exception cref="NbtException">The value has a different tag type or is uninitialized.</exception>
    public sbyte SByte
    {
        get
        {
            Expect(NbtTagType.Byte);
            return (sbyte)_bits;
        }
    }

    /// <summary>Gets a <c>TAG_Short</c> value.</summary>
    /// <exception cref="NbtException">The value has a different tag type or is uninitialized.</exception>
    public short Int16
    {
        get
        {
            Expect(NbtTagType.Short);
            return (short)_bits;
        }
    }

    /// <summary>Gets a <c>TAG_Int</c> value.</summary>
    /// <exception cref="NbtException">The value has a different tag type or is uninitialized.</exception>
    public int Int32
    {
        get
        {
            Expect(NbtTagType.Int);
            return (int)_bits;
        }
    }

    /// <summary>Gets a <c>TAG_Long</c> value.</summary>
    /// <exception cref="NbtException">The value has a different tag type or is uninitialized.</exception>
    public long Int64
    {
        get
        {
            Expect(NbtTagType.Long);
            return _bits;
        }
    }

    /// <summary>Gets a <c>TAG_Float</c> value.</summary>
    /// <exception cref="NbtException">The value has a different tag type or is uninitialized.</exception>
    public float Single
    {
        get
        {
            Expect(NbtTagType.Float);
            return BitConverter.Int32BitsToSingle((int)_bits);
        }
    }

    /// <summary>Gets a <c>TAG_Double</c> value.</summary>
    /// <exception cref="NbtException">The value has a different tag type or is uninitialized.</exception>
    public double Double
    {
        get
        {
            Expect(NbtTagType.Double);
            return BitConverter.Int64BitsToDouble(_bits);
        }
    }

    /// <summary>Gets a <c>TAG_String</c> view.</summary>
    /// <exception cref="NbtException">The value has a different tag type or is uninitialized.</exception>
    public NbtStringElement String
    {
        get
        {
            Expect(NbtTagType.String);
            return new NbtStringElement(_raw, _bits, _owner);
        }
    }

    /// <summary>Gets a <c>TAG_Byte_Array</c> view.</summary>
    /// <exception cref="NbtException">The value has a different tag type or is uninitialized.</exception>
    public NbtByteArrayElement ByteArray
    {
        get
        {
            Expect(NbtTagType.ByteArray);
            return new NbtByteArrayElement(_raw, _owner);
        }
    }

    /// <summary>Gets a <c>TAG_Int_Array</c> view.</summary>
    /// <exception cref="NbtException">The value has a different tag type or is uninitialized.</exception>
    public NbtIntArrayElement IntArray
    {
        get
        {
            Expect(NbtTagType.IntArray);
            return new NbtIntArrayElement(_raw, _owner);
        }
    }

    /// <summary>Gets a <c>TAG_Long_Array</c> view.</summary>
    /// <exception cref="NbtException">The value has a different tag type or is uninitialized.</exception>
    public NbtLongArrayElement LongArray
    {
        get
        {
            Expect(NbtTagType.LongArray);
            return new NbtLongArrayElement(_raw, _owner);
        }
    }

    /// <summary>Gets a <c>TAG_List</c> view.</summary>
    /// <exception cref="NbtException">The value has a different tag type or is uninitialized.</exception>
    public NbtListElement List
    {
        get
        {
            Expect(NbtTagType.List);
            return new NbtListElement(Owner!, _slotIndex);
        }
    }

    /// <summary>Gets a <c>TAG_Compound</c> view.</summary>
    /// <exception cref="NbtException">The value has a different tag type or is uninitialized.</exception>
    public NbtCompoundElement Compound
    {
        get
        {
            Expect(NbtTagType.Compound);
            return new NbtCompoundElement(Owner!, _slotIndex);
        }
    }

    private NbtDocument Owner => _owner ?? throw new NbtException(
        "The NBT element does not reference a document.");

    private void Expect(NbtTagType expected)
    {
        if (_tag != expected)
        {
            throw new NbtException(
                $"Expected a {expected} value but the current value is " +
                $"{(_tag == NbtTagType.End ? "uninitialized" : _tag.ToString())}.")
            {
                ExpectedTagType = expected,
                ActualTagType = _tag == NbtTagType.End ? null : _tag,
            };
        }
    }
}

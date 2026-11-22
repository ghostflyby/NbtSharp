using System.Buffers;
using System.Buffers.Binary;
using System.Collections;
using System.Runtime.InteropServices;
using Myitian.Text;

namespace NbtSharp;

/// <summary>
/// One indexed value of an <see cref="NbtDocument"/>: a scalar, an array, a list, or a compound.
/// </summary>
/// <remarks>
/// <para>
/// Elements are views, not copies: getters read the document's buffer directly and never advance
/// a cursor, so a getter can be called repeatedly. A default-initialized element belongs to no
/// document; every member of such an element throws <see cref="InvalidOperationException"/>.
/// </para>
/// <para>
/// The element stays valid only while its document is alive and undisposed; members throw
/// <see cref="ObjectDisposedException"/> after <see cref="NbtDocument.Dispose"/>.
/// </para>
/// </remarks>
public readonly struct NbtElement
{
    private const int NameStackBufferLength = 256;

    private readonly NbtDocument? _parent;
    private readonly int _slotIndex;

    internal NbtElement(NbtDocument parent, int slotIndex)
    {
        _parent = parent;
        _slotIndex = slotIndex;
    }

    /// <summary>Gets the tag type of this value.</summary>
    /// <exception cref="InvalidOperationException">This element is default-initialized.</exception>
    /// <exception cref="ObjectDisposedException">The owning document has been disposed.</exception>
    public NbtTagType TagType => Slot.TagType;

    /// <summary>
    /// Gets the name of this value: the member name for a compound member, the root name for the
    /// root element, and <see langword="null"/> for a list element or an unnamed root.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The name is re-decoded on every access and not cached; callers that read a name repeatedly
    /// should store the decoded value themselves. An empty name is returned as
    /// <see cref="string.Empty"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">This element is default-initialized.</exception>
    /// <exception cref="ObjectDisposedException">The owning document has been disposed.</exception>
    /// <exception cref="NbtException">The name is invalid modified UTF-8.</exception>
    public string? Name
    {
        get
        {
            ref readonly var slot = ref Slot;
            if ((slot.Flags & NbtDocumentSlotFlags.HasName) == 0)
            {
                return null;
            }

            return ModifiedUtf8.Decode(NameBytes(in slot), NamePosition(in slot));
        }
    }

    /// <summary>
    /// Compares the name of this value with pre-encoded modified UTF-8 name bytes.
    /// </summary>
    /// <param name="name">The name bytes, encoded as Java modified UTF-8.</param>
    /// <returns>
    /// <see langword="true"/> when the recorded name bytes match <paramref name="name"/> exactly,
    /// or when the recorded name is non-canonically encoded and its decoded value equals the
    /// decoded value of <paramref name="name"/>; the comparison is not symmetric in the encoding
    /// of <paramref name="name"/>. <see langword="false"/> for list elements, unnamed roots, and
    /// different names.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Names are compared by their decoded value; the byte comparison is a fast path that applies
    /// only when the wire name is canonically encoded. Wire names that use a non-canonical
    /// encoding (a raw 0x00 byte, or an overlong two- or three-byte form) fall back to decoding
    /// both names and comparing the decoded strings ordinally, so they match the input bytes of
    /// the equivalent canonical encoding.
    /// </para>
    /// <para>
    /// Input <paramref name="name"/> bytes that are not valid modified UTF-8 throw
    /// <see cref="NbtException"/> when the decoded comparison is reached.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">This element is default-initialized.</exception>
    /// <exception cref="ObjectDisposedException">The owning document has been disposed.</exception>
    /// <exception cref="NbtException">
    /// The decoded comparison was reached and either name is invalid modified UTF-8.
    /// </exception>
    public bool NameEquals(ReadOnlySpan<byte> name)
    {
        ref readonly var slot = ref Slot;
        if ((slot.Flags & NbtDocumentSlotFlags.HasName) == 0)
        {
            return false;
        }

        var recorded = NameBytes(in slot);
        if (recorded.SequenceEqual(name))
        {
            return true;
        }

        if ((slot.Flags & NbtDocumentSlotFlags.NameNonCanonical) == 0)
        {
            return false;
        }

        // Non-canonical wire names fall back to decoding both names at the wire name's position,
        // mirroring NbtReader.NameEquals.
        return string.Equals(
            ModifiedUtf8.Decode(recorded, NamePosition(in slot)),
            ModifiedUtf8.Decode(name, NamePosition(in slot)),
            StringComparison.Ordinal);
    }

    /// <summary>Reads this value as a signed 8-bit integer.</summary>
    /// <returns>The value.</returns>
    /// <remarks>The getter does not consume the value and may be called repeatedly.</remarks>
    /// <exception cref="InvalidOperationException">This element is default-initialized.</exception>
    /// <exception cref="ObjectDisposedException">The owning document has been disposed.</exception>
    /// <exception cref="NbtException">This value's tag type is not <see cref="NbtTagType.Byte"/>.</exception>
    public sbyte GetSByte()
    {
        ref readonly var slot = ref Expect(NbtTagType.Byte);
        return (sbyte)_parent!.DataSpan[slot.PayloadOffset];
    }

    /// <summary>Reads this value as a signed 16-bit integer.</summary>
    /// <returns>The value.</returns>
    /// <remarks>The getter does not consume the value and may be called repeatedly.</remarks>
    /// <exception cref="InvalidOperationException">This element is default-initialized.</exception>
    /// <exception cref="ObjectDisposedException">The owning document has been disposed.</exception>
    /// <exception cref="NbtException">This value's tag type is not <see cref="NbtTagType.Short"/>.</exception>
    public short GetInt16()
    {
        ref readonly var slot = ref Expect(NbtTagType.Short);
        return BinaryPrimitives.ReadInt16BigEndian(
            _parent!.DataSpan.Slice(slot.PayloadOffset, sizeof(short)));
    }

    /// <summary>Reads this value as a signed 32-bit integer.</summary>
    /// <returns>The value.</returns>
    /// <remarks>The getter does not consume the value and may be called repeatedly.</remarks>
    /// <exception cref="InvalidOperationException">This element is default-initialized.</exception>
    /// <exception cref="ObjectDisposedException">The owning document has been disposed.</exception>
    /// <exception cref="NbtException">This value's tag type is not <see cref="NbtTagType.Int"/>.</exception>
    public int GetInt32()
    {
        ref readonly var slot = ref Expect(NbtTagType.Int);
        return BinaryPrimitives.ReadInt32BigEndian(
            _parent!.DataSpan.Slice(slot.PayloadOffset, sizeof(int)));
    }

    /// <summary>Reads this value as a signed 64-bit integer.</summary>
    /// <returns>The value.</returns>
    /// <remarks>The getter does not consume the value and may be called repeatedly.</remarks>
    /// <exception cref="InvalidOperationException">This element is default-initialized.</exception>
    /// <exception cref="ObjectDisposedException">The owning document has been disposed.</exception>
    /// <exception cref="NbtException">This value's tag type is not <see cref="NbtTagType.Long"/>.</exception>
    public long GetInt64()
    {
        ref readonly var slot = ref Expect(NbtTagType.Long);
        return BinaryPrimitives.ReadInt64BigEndian(
            _parent!.DataSpan.Slice(slot.PayloadOffset, sizeof(long)));
    }

    /// <summary>Reads this value as a 32-bit IEEE 754 number.</summary>
    /// <returns>The value.</returns>
    /// <remarks>The getter does not consume the value and may be called repeatedly.</remarks>
    /// <exception cref="InvalidOperationException">This element is default-initialized.</exception>
    /// <exception cref="ObjectDisposedException">The owning document has been disposed.</exception>
    /// <exception cref="NbtException">This value's tag type is not <see cref="NbtTagType.Float"/>.</exception>
    public float GetSingle()
    {
        ref readonly var slot = ref Expect(NbtTagType.Float);
        return BinaryPrimitives.ReadSingleBigEndian(
            _parent!.DataSpan.Slice(slot.PayloadOffset, sizeof(float)));
    }

    /// <summary>Reads this value as a 64-bit IEEE 754 number.</summary>
    /// <returns>The value.</returns>
    /// <remarks>The getter does not consume the value and may be called repeatedly.</remarks>
    /// <exception cref="InvalidOperationException">This element is default-initialized.</exception>
    /// <exception cref="ObjectDisposedException">The owning document has been disposed.</exception>
    /// <exception cref="NbtException">This value's tag type is not <see cref="NbtTagType.Double"/>.</exception>
    public double GetDouble()
    {
        ref readonly var slot = ref Expect(NbtTagType.Double);
        return BinaryPrimitives.ReadDoubleBigEndian(
            _parent!.DataSpan.Slice(slot.PayloadOffset, sizeof(double)));
    }

    /// <summary>Reads this value as a string.</summary>
    /// <returns>The decoded string.</returns>
    /// <remarks>
    /// <para>
    /// Strings use Java modified UTF-8. Decoding follows <c>DataInputStream.readUTF</c>: lead
    /// bytes of 0xF0 or greater are rejected, while raw null bytes and overlong forms are
    /// accepted. The value's content is validated now, at access time — parsing only checked the
    /// length prefix — and the error position is the payload offset.
    /// </para>
    /// <para>
    /// The getter does not consume the value and may be called repeatedly.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">This element is default-initialized.</exception>
    /// <exception cref="ObjectDisposedException">The owning document has been disposed.</exception>
    /// <exception cref="NbtException">
    /// This value's tag type is not <see cref="NbtTagType.String"/>, or the payload is invalid
    /// modified UTF-8.
    /// </exception>
    public string GetString()
    {
        ref readonly var slot = ref Expect(NbtTagType.String);
        var payload = PayloadBytes(in slot);
        var length = BinaryPrimitives.ReadUInt16BigEndian(payload);
        return ModifiedUtf8.Decode(payload.Slice(sizeof(ushort), length), slot.PayloadOffset);
    }

    /// <summary>Reads this value as a byte array.</summary>
    /// <returns>A copy of the array's elements.</returns>
    /// <remarks>
    /// The length and collection limits were validated while parsing; the getter materializes a
    /// fresh array on every call.
    /// </remarks>
    /// <exception cref="InvalidOperationException">This element is default-initialized.</exception>
    /// <exception cref="ObjectDisposedException">The owning document has been disposed.</exception>
    /// <exception cref="NbtException">This value's tag type is not <see cref="NbtTagType.ByteArray"/>.</exception>
    public byte[] GetByteArray()
    {
        ref readonly var slot = ref Expect(NbtTagType.ByteArray);
        return PayloadBytes(in slot).Slice(sizeof(int)).ToArray();
    }

    /// <summary>Reads this value as a signed byte array.</summary>
    /// <returns>A copy of the array's elements.</returns>
    /// <remarks>
    /// The length and collection limits were validated while parsing; the getter materializes a
    /// fresh array on every call.
    /// </remarks>
    /// <exception cref="InvalidOperationException">This element is default-initialized.</exception>
    /// <exception cref="ObjectDisposedException">The owning document has been disposed.</exception>
    /// <exception cref="NbtException">This value's tag type is not <see cref="NbtTagType.ByteArray"/>.</exception>
    public sbyte[] GetSByteArray()
    {
        ref readonly var slot = ref Expect(NbtTagType.ByteArray);
        var payload = PayloadBytes(in slot);
        var values = new sbyte[payload.Length - sizeof(int)];
        payload.Slice(sizeof(int)).CopyTo(MemoryMarshal.AsBytes(values.AsSpan()));
        return values;
    }

    /// <summary>Reads this value as an integer array.</summary>
    /// <returns>A copy of the array's elements.</returns>
    /// <remarks>
    /// The length and collection limits were validated while parsing; the getter materializes a
    /// fresh array on every call.
    /// </remarks>
    /// <exception cref="InvalidOperationException">This element is default-initialized.</exception>
    /// <exception cref="ObjectDisposedException">The owning document has been disposed.</exception>
    /// <exception cref="NbtException">This value's tag type is not <see cref="NbtTagType.IntArray"/>.</exception>
    public int[] GetInt32Array()
    {
        ref readonly var slot = ref Expect(NbtTagType.IntArray);
        var payload = PayloadBytes(in slot);
        var values = new int[(payload.Length - sizeof(int)) / sizeof(int)];
        payload.Slice(sizeof(int)).CopyTo(MemoryMarshal.AsBytes(values.AsSpan()));
        if (BitConverter.IsLittleEndian)
        {
            BinaryPrimitives.ReverseEndianness(values.AsSpan(), values.AsSpan());
        }

        return values;
    }

    /// <summary>Reads this value as a long array.</summary>
    /// <returns>A copy of the array's elements.</returns>
    /// <remarks>
    /// The length and collection limits were validated while parsing; the getter materializes a
    /// fresh array on every call.
    /// </remarks>
    /// <exception cref="InvalidOperationException">This element is default-initialized.</exception>
    /// <exception cref="ObjectDisposedException">The owning document has been disposed.</exception>
    /// <exception cref="NbtException">This value's tag type is not <see cref="NbtTagType.LongArray"/>.</exception>
    public long[] GetInt64Array()
    {
        ref readonly var slot = ref Expect(NbtTagType.LongArray);
        var payload = PayloadBytes(in slot);
        var values = new long[(payload.Length - sizeof(int)) / sizeof(long)];
        payload.Slice(sizeof(int)).CopyTo(MemoryMarshal.AsBytes(values.AsSpan()));
        if (BitConverter.IsLittleEndian)
        {
            BinaryPrimitives.ReverseEndianness(values.AsSpan(), values.AsSpan());
        }

        return values;
    }

    /// <summary>Gets the number of members of this compound.</summary>
    /// <exception cref="InvalidOperationException">This element is default-initialized.</exception>
    /// <exception cref="ObjectDisposedException">The owning document has been disposed.</exception>
    /// <exception cref="NbtException">This value's tag type is not <see cref="NbtTagType.Compound"/>.</exception>
    public int MemberCount
    {
        get
        {
            ref readonly var slot = ref ExpectContainer(NbtTagType.Compound);
            return slot.ChildCount;
        }
    }

    /// <summary>Gets the number of elements of this list.</summary>
    /// <exception cref="InvalidOperationException">This element is default-initialized.</exception>
    /// <exception cref="ObjectDisposedException">The owning document has been disposed.</exception>
    /// <exception cref="NbtException">This value's tag type is not <see cref="NbtTagType.List"/>.</exception>
    public int ElementCount
    {
        get
        {
            ref readonly var slot = ref ExpectContainer(NbtTagType.List);
            return slot.ChildCount;
        }
    }

    /// <summary>
    /// Gets the element type declared by this list's header. An empty
    /// <c>TAG_List&lt;TAG_End&gt;</c> reports <see cref="NbtTagType.End"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">This element is default-initialized.</exception>
    /// <exception cref="ObjectDisposedException">The owning document has been disposed.</exception>
    /// <exception cref="NbtException">This value's tag type is not <see cref="NbtTagType.List"/>.</exception>
    public NbtTagType ElementType
    {
        get
        {
            ref readonly var slot = ref ExpectContainer(NbtTagType.List);
            return slot.ElementType;
        }
    }

    /// <summary>
    /// Enumerates the members of this compound in wire order. Every member is yielded, including
    /// duplicated names.
    /// </summary>
    /// <returns>An enumerator over the compound's members.</returns>
    /// <exception cref="InvalidOperationException">This element is default-initialized.</exception>
    /// <exception cref="ObjectDisposedException">The owning document has been disposed.</exception>
    /// <exception cref="NbtException">This value's tag type is not <see cref="NbtTagType.Compound"/>.</exception>
    public Enumerator EnumerateMembers()
    {
        ExpectContainer(NbtTagType.Compound);
        return new Enumerator(this);
    }

    /// <summary>Enumerates the elements of this list in wire order.</summary>
    /// <returns>An enumerator over the list's elements.</returns>
    /// <exception cref="InvalidOperationException">This element is default-initialized.</exception>
    /// <exception cref="ObjectDisposedException">The owning document has been disposed.</exception>
    /// <exception cref="NbtException">This value's tag type is not <see cref="NbtTagType.List"/>.</exception>
    public Enumerator EnumerateElements()
    {
        ExpectContainer(NbtTagType.List);
        return new Enumerator(this);
    }

    /// <summary>Gets the element of this list at the specified index.</summary>
    /// <param name="index">The zero-based element index.</param>
    /// <returns>The element.</returns>
    /// <remarks>
    /// Lists of scalars, strings, and arrays address their elements in constant time; lists of
    /// compounds or nested lists walk to the index, which is proportional to the index.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="index"/> is negative or not less than <see cref="ElementCount"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">This element is default-initialized.</exception>
    /// <exception cref="ObjectDisposedException">The owning document has been disposed.</exception>
    /// <exception cref="NbtException">This value's tag type is not <see cref="NbtTagType.List"/>.</exception>
    public NbtElement GetElementAt(int index)
    {
        var document = Parent;
        document.ThrowIfDisposed();
        ref readonly var slot = ref document.GetSlot(_slotIndex);
        if (slot.TagType != NbtTagType.List)
        {
            throw CreateContainerMismatch(NbtTagType.List, in slot);
        }

        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, slot.ChildCount);

        var child = _slotIndex + 1;
        if (slot.ElementType is not (NbtTagType.List or NbtTagType.Compound))
        {
            // Scalar, string, and array elements occupy consecutive slots.
            return document.CreateElement(child + index);
        }

        for (var i = 0; i < index; i++)
        {
            child += 1 + document.GetSlot(child).DescendantCount;
        }

        return document.CreateElement(child);
    }

    /// <summary>Gets the element of this list at the specified index.</summary>
    /// <param name="index">The zero-based element index.</param>
    /// <returns>The element.</returns>
    /// <remarks>Equivalent to <see cref="GetElementAt(int)"/>.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="index"/> is negative or not less than <see cref="ElementCount"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">This element is default-initialized.</exception>
    /// <exception cref="ObjectDisposedException">The owning document has been disposed.</exception>
    /// <exception cref="NbtException">This value's tag type is not <see cref="NbtTagType.List"/>.</exception>
    public NbtElement this[int index] => GetElementAt(index);

    /// <summary>
    /// Gets the member of this compound with the specified name. When the name occurs more than
    /// once, the last occurrence wins.
    /// </summary>
    /// <param name="name">The member name.</param>
    /// <returns>The member.</returns>
    /// <remarks>The lookup walks the members and compares the encoded name bytes.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="KeyNotFoundException">No member has the specified name.</exception>
    /// <exception cref="InvalidOperationException">This element is default-initialized.</exception>
    /// <exception cref="ObjectDisposedException">The owning document has been disposed.</exception>
    /// <exception cref="NbtException">
    /// This value's tag type is not <see cref="NbtTagType.Compound"/>, or a non-canonically
    /// encoded member name being compared is invalid modified UTF-8.
    /// </exception>
    public NbtElement GetMember(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!TryGetMember(name, out var value))
        {
            throw new KeyNotFoundException($"The given key '{name}' was not present in the dictionary.");
        }

        return value;
    }

    /// <summary>
    /// Gets the member of this compound with the specified name. When the name occurs more than
    /// once, the last occurrence wins.
    /// </summary>
    /// <param name="name">The member name.</param>
    /// <param name="value">
    /// The member, or a default <see cref="NbtElement"/> when no member has the specified name.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a member with the specified name was found.
    /// </returns>
    /// <remarks>
    /// The name is encoded as modified UTF-8 into a stack buffer, or a pooled buffer when it is
    /// longer than 256 bytes, and compared with each member's wire name.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">This element is default-initialized.</exception>
    /// <exception cref="ObjectDisposedException">The owning document has been disposed.</exception>
    /// <exception cref="NbtException">
    /// This value's tag type is not <see cref="NbtTagType.Compound"/>, or a non-canonically
    /// encoded member name being compared is invalid modified UTF-8.
    /// </exception>
    public bool TryGetMember(string name, out NbtElement value)
    {
        ArgumentNullException.ThrowIfNull(name);
        var document = Parent;
        document.ThrowIfDisposed();

        var byteCount = ModifiedUTF8Encoding.Instance.GetByteCount(name);
        byte[]? rented = null;
        Span<byte> buffer = stackalloc byte[NameStackBufferLength];
        if (byteCount > NameStackBufferLength)
        {
            rented = ArrayPool<byte>.Shared.Rent(byteCount);
            buffer = rented;
        }

        try
        {
            var written = ModifiedUTF8Encoding.Instance.GetBytes(name, buffer);
            return TryGetMemberCore(document, buffer[..written], out value);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    /// <summary>
    /// Materializes this value as an <see cref="NbtValue"/> tree: scalars are read directly,
    /// arrays and strings are copied, and containers are built recursively.
    /// </summary>
    /// <returns>The materialized value.</returns>
    /// <remarks>
    /// <para>
    /// A <c>TAG_List&lt;TAG_End&gt;</c> materializes as a default <see cref="NbtList"/>, while an
    /// empty list with a typed element header materializes as an empty typed list, so the two
    /// empty-list encodings stay distinct. A compound materializes as an <see cref="NbtCompound"/>
    /// filled in wire order, so a duplicated name resolves to its last value in the first
    /// occurrence's slot — the same compound <c>NbtSerializer</c> produces.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">This element is default-initialized.</exception>
    /// <exception cref="ObjectDisposedException">The owning document has been disposed.</exception>
    /// <exception cref="NbtException">A contained string is invalid modified UTF-8.</exception>
    public NbtValue ToNbtValue() => Slot.TagType switch
    {
        NbtTagType.Byte => GetSByte(),
        NbtTagType.Short => GetInt16(),
        NbtTagType.Int => GetInt32(),
        NbtTagType.Long => GetInt64(),
        NbtTagType.Float => GetSingle(),
        NbtTagType.Double => GetDouble(),
        NbtTagType.ByteArray => GetSByteArray(),
        NbtTagType.String => GetString(),
        NbtTagType.List => ToNbtList(),
        NbtTagType.Compound => ToNbtCompound(),
        NbtTagType.IntArray => GetInt32Array(),
        NbtTagType.LongArray => GetInt64Array(),
        _ => throw new NbtException($"Unexpected NBT tag type {TagType}."),
    };

    /// <summary>Returns a copy of this value's complete payload, including length prefixes.</summary>
    /// <returns>The raw payload bytes.</returns>
    /// <exception cref="InvalidOperationException">This element is default-initialized.</exception>
    /// <exception cref="ObjectDisposedException">The owning document has been disposed.</exception>
    internal byte[] GetRawBytes()
    {
        ref readonly var slot = ref Slot;
        return PayloadBytes(in slot).ToArray();
    }

    private NbtDocument Parent =>
        _parent ?? throw new InvalidOperationException(
            "The operation is not valid for a default-initialized NbtElement.");

    private ref readonly NbtDocumentSlot Slot
    {
        get
        {
            var document = Parent;
            document.ThrowIfDisposed();
            return ref document.GetSlot(_slotIndex);
        }
    }

    private ReadOnlySpan<byte> NameBytes(ref readonly NbtDocumentSlot slot) =>
        _parent!.DataSpan.Slice(slot.NameOffset, slot.NameLength);

    // The reader reports name decoding errors at the length prefix, which always sits two bytes
    // before the recorded name content.
    private static int NamePosition(ref readonly NbtDocumentSlot slot) =>
        slot.NameOffset - sizeof(ushort);

    private ReadOnlySpan<byte> PayloadBytes(ref readonly NbtDocumentSlot slot) =>
        _parent!.DataSpan.Slice(slot.PayloadOffset, slot.PayloadLength);

    private ref readonly NbtDocumentSlot Expect(NbtTagType expected)
    {
        ref readonly var slot = ref Slot;
        if (slot.TagType != expected)
        {
            throw CreateValueMismatch(expected, in slot);
        }

        return ref slot;
    }

    private ref readonly NbtDocumentSlot ExpectContainer(NbtTagType expected)
    {
        ref readonly var slot = ref Slot;
        if (slot.TagType != expected)
        {
            throw CreateContainerMismatch(expected, in slot);
        }

        return ref slot;
    }

    private bool TryGetMemberCore(NbtDocument document, ReadOnlySpan<byte> encodedName, out NbtElement value)
    {
        ref readonly var slot = ref document.GetSlot(_slotIndex);
        if (slot.TagType != NbtTagType.Compound)
        {
            throw CreateContainerMismatch(NbtTagType.Compound, in slot);
        }

        // Walk the members in wire order and keep the last match, so the last occurrence of a
        // duplicated name wins — the same member NbtCompound materializes.
        var child = _slotIndex + 1;
        var lastMatch = -1;
        for (var i = 0; i < slot.ChildCount; i++)
        {
            ref readonly var member = ref document.GetSlot(child);
            if (MemberNameEquals(in member, encodedName))
            {
                lastMatch = child;
            }

            child += 1 + member.DescendantCount;
        }

        if (lastMatch < 0)
        {
            value = default;
            return false;
        }

        value = document.CreateElement(lastMatch);
        return true;
    }

    private bool MemberNameEquals(ref readonly NbtDocumentSlot member, ReadOnlySpan<byte> encodedName)
    {
        if ((member.Flags & NbtDocumentSlotFlags.HasName) == 0)
        {
            return false;
        }

        var recorded = _parent!.DataSpan.Slice(member.NameOffset, member.NameLength);
        if (recorded.SequenceEqual(encodedName))
        {
            return true;
        }

        if ((member.Flags & NbtDocumentSlotFlags.NameNonCanonical) == 0)
        {
            return false;
        }

        return string.Equals(
            ModifiedUtf8.Decode(recorded, NamePosition(in member)),
            ModifiedUtf8.Decode(encodedName, NamePosition(in member)),
            StringComparison.Ordinal);
    }

    private NbtList ToNbtList()
    {
        ref readonly var slot = ref ExpectContainer(NbtTagType.List);
        switch (slot.ElementType)
        {
            case NbtTagType.End:
                return default;
            case NbtTagType.Byte:
            {
                var list = new List<sbyte>(slot.ChildCount);
                foreach (var element in EnumerateElements())
                {
                    list.Add(element.GetSByte());
                }

                return new NbtList(list);
            }

            case NbtTagType.Short:
            {
                var list = new List<short>(slot.ChildCount);
                foreach (var element in EnumerateElements())
                {
                    list.Add(element.GetInt16());
                }

                return new NbtList(list);
            }

            case NbtTagType.Int:
            {
                var list = new List<int>(slot.ChildCount);
                foreach (var element in EnumerateElements())
                {
                    list.Add(element.GetInt32());
                }

                return new NbtList(list);
            }

            case NbtTagType.Long:
            {
                var list = new List<long>(slot.ChildCount);
                foreach (var element in EnumerateElements())
                {
                    list.Add(element.GetInt64());
                }

                return new NbtList(list);
            }

            case NbtTagType.Float:
            {
                var list = new List<float>(slot.ChildCount);
                foreach (var element in EnumerateElements())
                {
                    list.Add(element.GetSingle());
                }

                return new NbtList(list);
            }

            case NbtTagType.Double:
            {
                var list = new List<double>(slot.ChildCount);
                foreach (var element in EnumerateElements())
                {
                    list.Add(element.GetDouble());
                }

                return new NbtList(list);
            }

            case NbtTagType.ByteArray:
            {
                var list = new List<sbyte[]>(slot.ChildCount);
                foreach (var element in EnumerateElements())
                {
                    list.Add(element.GetSByteArray());
                }

                return new NbtList(list);
            }

            case NbtTagType.String:
            {
                var list = new List<string>(slot.ChildCount);
                foreach (var element in EnumerateElements())
                {
                    list.Add(element.GetString());
                }

                return new NbtList(list);
            }

            case NbtTagType.List:
            {
                var list = new List<NbtList>(slot.ChildCount);
                foreach (var element in EnumerateElements())
                {
                    list.Add(element.ToNbtList());
                }

                return new NbtList(list);
            }

            case NbtTagType.Compound:
            {
                var list = new List<NbtCompound>(slot.ChildCount);
                foreach (var element in EnumerateElements())
                {
                    list.Add(element.ToNbtCompound());
                }

                return new NbtList(list);
            }

            case NbtTagType.IntArray:
            {
                var list = new List<int[]>(slot.ChildCount);
                foreach (var element in EnumerateElements())
                {
                    list.Add(element.GetInt32Array());
                }

                return new NbtList(list);
            }

            case NbtTagType.LongArray:
            {
                var list = new List<long[]>(slot.ChildCount);
                foreach (var element in EnumerateElements())
                {
                    list.Add(element.GetInt64Array());
                }

                return new NbtList(list);
            }

            default:
                throw new NbtException($"Unexpected NBT list element type {slot.ElementType}.");
        }
    }

    private NbtCompound ToNbtCompound()
    {
        var compound = new NbtCompound(MemberCount);
        foreach (var member in EnumerateMembers())
        {
            compound[member.Name!] = member.ToNbtValue();
        }

        return compound;
    }

    private static NbtException CreateValueMismatch(NbtTagType expected, ref readonly NbtDocumentSlot slot) =>
        new(
            $"Expected a {expected} value but the current token is {GetToken(slot.TagType)} ({slot.TagType}).")
        {
            BytePosition = slot.PayloadOffset,
            ExpectedTagType = expected,
            ActualTagType = slot.TagType,
        };

    private static NbtException CreateContainerMismatch(NbtTagType expected, ref readonly NbtDocumentSlot slot) =>
        new(
            $"Expected TAG_{expected} but the current token is {GetToken(slot.TagType)} ({slot.TagType}).")
        {
            BytePosition = slot.PayloadOffset,
            ExpectedTagType = expected,
            ActualTagType = slot.TagType,
        };

    private static NbtTokenType GetToken(NbtTagType tagType) => tagType switch
    {
        NbtTagType.Compound => NbtTokenType.StartCompound,
        NbtTagType.List => NbtTokenType.StartList,
        _ => NbtTokenType.Value,
    };

    /// <summary>
    /// An enumerator over the members of a compound or the elements of a list, following the
    /// container's wire order.
    /// </summary>
    /// <remarks>
    /// Member enumeration yields elements whose <see cref="NbtElement.Name"/> is the member name;
    /// element enumeration yields nameless elements. Use it directly in
    /// <see langword="foreach"/> through <see cref="GetEnumerator"/>, as returned by
    /// <see cref="EnumerateMembers"/> or <see cref="EnumerateElements"/>.
    /// </remarks>
    public struct Enumerator : IEnumerator<NbtElement>, IEnumerable<NbtElement>
    {
        private readonly NbtElement _parent;
        private int _nextSlot;
        private int _remaining;
        private NbtElement _current;

        internal Enumerator(NbtElement parent)
        {
            ref readonly var slot = ref parent.Slot;
            _parent = parent;
            _nextSlot = parent._slotIndex + 1;
            _remaining = slot.ChildCount;
            _current = default;
        }

        /// <summary>
        /// Gets the element the enumerator is positioned on.
        /// </summary>
        /// <value>
        /// The current <see cref="NbtElement"/>, or a default element before the first
        /// <see cref="MoveNext"/> and after the enumerator has passed the last child.
        /// </value>
        public NbtElement Current => _current;

        /// <summary>Advances to the next member or element, in wire order.</summary>
        /// <returns>
        /// <see langword="true"/> when the enumerator positioned itself on a child;
        /// <see langword="false"/> when there are no children left.
        /// </returns>
        public bool MoveNext()
        {
            if (_remaining == 0)
            {
                _current = default;
                return false;
            }

            var document = _parent._parent!;
            document.ThrowIfDisposed();
            _current = new NbtElement(document, _nextSlot);
            _nextSlot += 1 + document.GetSlot(_nextSlot).DescendantCount;
            _remaining--;
            return true;
        }

        /// <summary>Returns the enumerator itself, for <see langword="foreach"/> support.</summary>
        /// <returns>This enumerator.</returns>
        public Enumerator GetEnumerator() => this;

        /// <summary>Returns the enumerator to its initial position, before the first child.</summary>
        public void Reset()
        {
            var document = _parent._parent
                ?? throw new InvalidOperationException(
                    "The enumerator was not initialized with an NbtElement.");
            document.ThrowIfDisposed();
            _nextSlot = _parent._slotIndex + 1;
            _remaining = document.GetSlot(_parent._slotIndex).ChildCount;
            _current = default;
        }

        /// <summary>Releases no resources; the enumerator borrows its document.</summary>
        public void Dispose()
        {
        }

        object IEnumerator.Current => Current;

        IEnumerator<NbtElement> IEnumerable<NbtElement>.GetEnumerator() => this;

        IEnumerator IEnumerable.GetEnumerator() => this;
    }
}

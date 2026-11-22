using System.Buffers;
using System.Buffers.Binary;

namespace NbtSharp;

/// <summary>
/// One indexed tag of an <see cref="NbtDocument"/>. Slots are emitted in document (preorder)
/// order, so the subtree of a container occupies the contiguous range
/// <c>[slot + 1, slot + 1 + <see cref="DescendantCount"/>)</c>.
/// </summary>
/// <remarks>
/// <para>
/// The layout is 28 bytes: the tag type, flags, and list element type fill the first three bytes;
/// the name is addressed by an offset and a 16-bit length; the payload is addressed by an offset
/// and a length that cover the complete wire payload (length prefixes included; for a list the
/// element header plus every element; for a compound every member plus the end tag). The two
/// counts are backfilled when the container closes.
/// </para>
/// <para>
/// Offsets are 32-bit, which bounds the document size (and therefore the slot count) by
/// <see cref="int.MaxValue"/>.
/// </para>
/// </remarks>
internal struct NbtDocumentSlot
{
    /// <summary>The tag type of the indexed value.</summary>
    public NbtTagType TagType;

    /// <summary>The name and encoding flags.</summary>
    public NbtDocumentSlotFlags Flags;

    /// <summary>The element type declared by a list header; <see cref="NbtTagType.End"/> otherwise.</summary>
    public NbtTagType ElementType;

    /// <summary>Unused; keeps the header four bytes wide.</summary>
    public byte Pad1;

    /// <summary>The offset of the name bytes, when <see cref="NbtDocumentSlotFlags.HasName"/> is set.</summary>
    public int NameOffset;

    /// <summary>The length of the name bytes in bytes.</summary>
    public ushort NameLength;

    /// <summary>The offset of the payload, directly after the tag byte and name.</summary>
    public int PayloadOffset;

    /// <summary>The length of the payload in bytes.</summary>
    public int PayloadLength;

    /// <summary>The number of slots in this value's subtree, excluding the value itself.</summary>
    public int DescendantCount;

    /// <summary>The number of direct children: compound members or list elements.</summary>
    public int ChildCount;
}

/// <summary>The name and encoding flags recorded for a slot.</summary>
[Flags]
internal enum NbtDocumentSlotFlags : byte
{
    /// <summary>The value carries a name: a compound member or a named root.</summary>
    HasName = 1,

    /// <summary>
    /// The name bytes use a non-canonical modified UTF-8 encoding, so byte-wise comparisons must
    /// fall back to decoded string comparisons.
    /// </summary>
    NameNonCanonical = 2,
}

/// <summary>
/// Indexes a complete NBT document into a pooled slot array in a single pass, validating the
/// structure with the same messages, byte positions, and truncation reports as
/// <see cref="NbtReader"/>.
/// </summary>
internal static class NbtDocumentParser
{
    private const int DefaultMaxDepth = 512;
    private const int DefaultMaxCollectionLength = 16 * 1024 * 1024;
    private const int InitialSlotCapacity = 16;
    private const int InitialFrameCapacity = 16;

    /// <summary>Validates reader options shared by every parse entry point.</summary>
    /// <param name="options">The options to validate.</param>
    public static void ValidateOptions(NbtReaderOptions options)
    {
        if (options.RootFormat is not NbtRootFormat.Named and not NbtRootFormat.Unnamed)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Unknown root format.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(options.MaxDepth);
        ArgumentOutOfRangeException.ThrowIfNegative(options.MaxCollectionLength);
        ArgumentOutOfRangeException.ThrowIfNegative(options.MaxDocumentBytes);
    }

    /// <summary>
    /// Indexes a complete document. The returned slot array is rented from the pool and must be
    /// returned by the caller; on failure the parser returns it itself.
    /// </summary>
    /// <param name="data">The complete NBT document; trailing bytes after the root are ignored.</param>
    /// <param name="options">The reader options, or the defaults.</param>
    /// <returns>The rented slot array and the number of slots it holds.</returns>
    public static (NbtDocumentSlot[] Slots, int SlotCount) Parse(ReadOnlySpan<byte> data, NbtReaderOptions options)
    {
        ValidateOptions(options);
        var parser = new Parser(
            data,
            options.RootFormat,
            options.MaxDepth > 0 ? options.MaxDepth : DefaultMaxDepth,
            options.MaxCollectionLength > 0 ? options.MaxCollectionLength : DefaultMaxCollectionLength,
            options.MaxDocumentBytes);
        return parser.ParseDocument();
    }

    private ref struct Parser
    {
        private readonly ReadOnlySpan<byte> _data;
        private readonly NbtRootFormat _rootFormat;
        private readonly int _maxDepth;
        private readonly int _maxCollectionLength;
        private readonly long _maxDocumentBytes;
        private long _bytesConsumed;
        private NbtDocumentSlot[] _slots;
        private int _slotCount;
        private ContainerFrame[] _frames;
        private int _depth;

        public Parser(
            ReadOnlySpan<byte> data,
            NbtRootFormat rootFormat,
            int maxDepth,
            int maxCollectionLength,
            long maxDocumentBytes)
        {
            _data = data;
            _rootFormat = rootFormat;
            _maxDepth = maxDepth;
            _maxCollectionLength = maxCollectionLength;
            _maxDocumentBytes = maxDocumentBytes;
            _slots = ArrayPool<NbtDocumentSlot>.Shared.Rent(InitialSlotCapacity);
            _slotCount = 0;
            _frames = new ContainerFrame[InitialFrameCapacity];
            _depth = 0;
        }

        public (NbtDocumentSlot[] Slots, int SlotCount) ParseDocument()
        {
            try
            {
                ParseRoot();
                while (_depth > 0)
                {
                    if (_frames[_depth - 1].IsList)
                    {
                        if (_frames[_depth - 1].Remaining == 0)
                        {
                            CloseContainer();
                            continue;
                        }

                        _frames[_depth - 1].Remaining--;
                        var elementType = _frames[_depth - 1].ElementType;
                        if (elementType == NbtTagType.End)
                        {
                            throw new NbtException("A list of TAG_End cannot contain elements.");
                        }

                        // List elements carry no tag byte and no name.
                        BeginValue(elementType, _bytesConsumed, NameInfo.Unnamed);
                        continue;
                    }

                    var tagPosition = _bytesConsumed;
                    var tagType = ReadTagType();
                    if (tagType == NbtTagType.End)
                    {
                        CloseContainer();
                        continue;
                    }

                    BeginValue(tagType, tagPosition, ReadName());
                }

                return (_slots, _slotCount);
            }
            catch
            {
                ArrayPool<NbtDocumentSlot>.Shared.Return(_slots);
                _slots = [];
                throw;
            }
        }

        private void ParseRoot()
        {
            var tagPosition = _bytesConsumed;
            var tagType = ReadTagType();
            if (tagType == NbtTagType.End)
            {
                throw new NbtException("The root tag cannot be TAG_End.")
                {
                    BytePosition = tagPosition,
                    ActualTagType = NbtTagType.End,
                };
            }

            var name = _rootFormat == NbtRootFormat.Named ? ReadName() : NameInfo.Unnamed;
            BeginValue(tagType, tagPosition, name);
        }

        private void BeginValue(NbtTagType tagType, long tagPosition, NameInfo name)
        {
            if (tagType == NbtTagType.End)
            {
                throw new NbtException("Unexpected TAG_End.")
                {
                    BytePosition = tagPosition,
                    ActualTagType = NbtTagType.End,
                };
            }

            if (_slotCount == _slots.Length)
            {
                // Grow with long math: past 2^30 slots a plain doubling overflows int and reports
                // the wrong exception type. The document-size cap makes this unreachable in
                // practice, so simply refuse cleanly.
                var doubled = (long)_slots.Length * 2;
                var newLength = doubled > int.MaxValue ? int.MaxValue : _slots.Length + 1;
                if (newLength <= _slots.Length)
                {
                    throw new NbtException("The NBT document has too many values to index.");
                }

                var grown = ArrayPool<NbtDocumentSlot>.Shared.Rent(newLength);
                _slots.AsSpan(0, _slotCount).CopyTo(grown);
                ArrayPool<NbtDocumentSlot>.Shared.Return(_slots);
                _slots = grown;
            }

            if (_depth > 0)
            {
                _slots[_frames[_depth - 1].SlotIndex].ChildCount++;
            }

            var slotIndex = _slotCount++;
            ref var slot = ref _slots[slotIndex];
            slot = default;
            slot.TagType = tagType;
            slot.Flags = name.HasName
                ? NbtDocumentSlotFlags.HasName |
                    (name.NonCanonical ? NbtDocumentSlotFlags.NameNonCanonical : 0)
                : 0;
            slot.NameOffset = name.Offset;
            slot.NameLength = name.Length;
            slot.PayloadOffset = (int)_bytesConsumed;

            if (tagType == NbtTagType.Compound)
            {
                EnterContainer();
                PushFrame(slotIndex, isList: false, elementType: NbtTagType.End, remaining: 0);
                return;
            }

            if (tagType == NbtTagType.List)
            {
                var (elementType, count) = ReadListHeader();
                slot.ElementType = elementType;
                EnterContainer();
                PushFrame(slotIndex, isList: true, elementType, remaining: count);
                return;
            }

            slot.PayloadLength = ConsumePayload(tagType);
        }

        private void CloseContainer()
        {
            var frame = _frames[_depth - 1];
            _depth--;
            ref var slot = ref _slots[frame.SlotIndex];
            slot.DescendantCount = _slotCount - frame.SlotIndex - 1;
            slot.PayloadLength = (int)_bytesConsumed - slot.PayloadOffset;
        }

        private void EnterContainer()
        {
            if (_depth >= _maxDepth)
            {
                throw new NbtException($"The NBT nesting depth exceeds the limit of {_maxDepth}.")
                {
                    BytePosition = _bytesConsumed,
                };
            }
        }

        private void PushFrame(int slotIndex, bool isList, NbtTagType elementType, int remaining)
        {
            if (_depth == _frames.Length)
            {
                Array.Resize(ref _frames, _frames.Length * 2);
            }

            _frames[_depth++] = new ContainerFrame(slotIndex, isList, elementType, remaining);
        }

        private (NbtTagType ElementType, int Count) ReadListHeader()
        {
            var position = _bytesConsumed;
            var elementType = ReadTagType();
            var count = ReadInt32();
            if (count < 0)
            {
                throw new NbtException($"Invalid NBT list length {count}.")
                {
                    BytePosition = position,
                };
            }

            if (count > _maxCollectionLength)
            {
                throw new NbtException(
                    $"The NBT list length {count} exceeds the configured limit of {_maxCollectionLength}.")
                {
                    BytePosition = position,
                };
            }

            if (elementType == NbtTagType.End && count != 0)
            {
                throw new NbtException("A list of TAG_End must have length zero.")
                {
                    BytePosition = position,
                };
            }

            EnsureListElementsFit(elementType, count, position);
            return (elementType, count);
        }

        private void EnsureListElementsFit(NbtTagType elementType, int count, long position)
        {
            var minimumSize = elementType switch
            {
                NbtTagType.Byte => 1,
                NbtTagType.Short => 2,
                NbtTagType.Int or NbtTagType.Float or NbtTagType.ByteArray or NbtTagType.IntArray or NbtTagType.LongArray => 4,
                NbtTagType.Long or NbtTagType.Double => 8,
                NbtTagType.String => 2,
                NbtTagType.List => 5,
                NbtTagType.Compound => 1,
                _ => 0,
            };

            if (minimumSize == 0)
            {
                return;
            }

            var required = (long)count * minimumSize;
            EnsureDocumentSize(required);
            var remaining = _data.Length - _bytesConsumed;
            if (required > remaining)
            {
                throw new NbtException(
                    $"The NBT list declares {count} elements but the remaining data is too short.")
                {
                    IsTruncated = true,
                    TruncatedAdditionalBytes = required - remaining,
                    BytePosition = position,
                };
            }
        }

        private NameInfo ReadName()
        {
            var position = _bytesConsumed;
            var length = ReadUInt16();
            if (length == 0)
            {
                return new NameInfo((int)_bytesConsumed, 0, true, false);
            }

            EnsurePayloadFits(length);
            var offset = (int)_bytesConsumed;
            ModifiedUtf8.Scan(_data.Slice(offset, length), position, out var nonCanonical);
            _bytesConsumed += length;
            return new NameInfo(offset, length, true, nonCanonical);
        }

        private int ConsumePayload(NbtTagType tagType)
        {
            switch (tagType)
            {
                case NbtTagType.Byte:
                    AdvanceFixed(1);
                    return 1;
                case NbtTagType.Short:
                    AdvanceFixed(sizeof(short));
                    return sizeof(short);
                case NbtTagType.Int:
                case NbtTagType.Float:
                    AdvanceFixed(sizeof(int));
                    return sizeof(int);
                case NbtTagType.Long:
                case NbtTagType.Double:
                    AdvanceFixed(sizeof(long));
                    return sizeof(long);
                case NbtTagType.ByteArray:
                {
                    var count = ReadArrayLength();
                    EnsurePayloadFits(count);
                    _bytesConsumed += count;
                    return sizeof(int) + count;
                }

                case NbtTagType.String:
                {
                    var length = ReadUInt16();
                    EnsurePayloadFits(length);
                    _bytesConsumed += length;
                    return sizeof(ushort) + length;
                }

                case NbtTagType.IntArray:
                {
                    var count = ReadArrayLength();
                    var byteCount = (long)count * sizeof(int);
                    EnsurePayloadFits(byteCount);
                    _bytesConsumed += byteCount;
                    return sizeof(int) + (int)byteCount;
                }

                case NbtTagType.LongArray:
                {
                    var count = ReadArrayLength();
                    var byteCount = (long)count * sizeof(long);
                    EnsurePayloadFits(byteCount);
                    _bytesConsumed += byteCount;
                    return sizeof(int) + (int)byteCount;
                }

                default:
                    throw new NbtException($"Cannot skip a value of type {tagType}.");
            }
        }

        private int ReadArrayLength()
        {
            var position = _bytesConsumed;
            var length = ReadInt32();
            if (length < 0 || length > _maxCollectionLength)
            {
                throw new NbtException(
                    $"Invalid NBT array length {length} (limit {_maxCollectionLength}).")
                {
                    BytePosition = position,
                };
            }

            return length;
        }

        private NbtTagType ReadTagType()
        {
            var position = _bytesConsumed;
            if (_bytesConsumed >= _data.Length)
            {
                throw new NbtException("Unexpected end of NBT data.")
                {
                    IsTruncated = true,
                    TruncatedAdditionalBytes = 1,
                    BytePosition = position,
                };
            }

            EnsureDocumentSize(1);
            var value = _data[(int)_bytesConsumed];
            _bytesConsumed++;
            if (value > (byte)NbtTagType.LongArray)
            {
                throw new NbtException($"Unknown NBT tag type {value}.")
                {
                    BytePosition = position,
                };
            }

            return (NbtTagType)value;
        }

        private ushort ReadUInt16()
        {
            AdvanceFixed(sizeof(ushort));
            return BinaryPrimitives.ReadUInt16BigEndian(
                _data.Slice((int)_bytesConsumed - sizeof(ushort), sizeof(ushort)));
        }

        private int ReadInt32()
        {
            AdvanceFixed(sizeof(int));
            return BinaryPrimitives.ReadInt32BigEndian(
                _data.Slice((int)_bytesConsumed - sizeof(int), sizeof(int)));
        }

        private void AdvanceFixed(long count)
        {
            EnsureDocumentSize(count);
            var remaining = _data.Length - _bytesConsumed;
            if (count > remaining)
            {
                throw new NbtException("Unexpected end of NBT data.")
                {
                    IsTruncated = true,
                    TruncatedAdditionalBytes = count - remaining,
                    BytePosition = _bytesConsumed,
                };
            }

            _bytesConsumed += count;
        }

        private void EnsurePayloadFits(long byteCount)
        {
            EnsureDocumentSize(byteCount);
            var remaining = _data.Length - _bytesConsumed;
            if (byteCount > remaining)
            {
                throw new NbtException(
                    $"The NBT payload declares {byteCount} bytes but only {remaining} are available.")
                {
                    IsTruncated = true,
                    TruncatedAdditionalBytes = byteCount - remaining,
                    BytePosition = _bytesConsumed,
                };
            }
        }

        private void EnsureDocumentSize(long count)
        {
            if (_maxDocumentBytes > 0 && count > _maxDocumentBytes - _bytesConsumed)
            {
                throw new NbtException(
                    $"The NBT document exceeds the configured limit of {_maxDocumentBytes} bytes.")
                {
                    BytePosition = _bytesConsumed,
                };
            }
        }

        private readonly record struct NameInfo(int Offset, ushort Length, bool HasName, bool NonCanonical)
        {
            public static readonly NameInfo Unnamed = new(0, 0, false, false);
        }

        private struct ContainerFrame(int slotIndex, bool isList, NbtTagType elementType, int remaining)
        {
            public int SlotIndex { get; } = slotIndex;

            public bool IsList { get; } = isList;

            public NbtTagType ElementType { get; } = elementType;

            public int Remaining { get; set; } = remaining;
        }
    }
}

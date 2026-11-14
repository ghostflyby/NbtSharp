using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using Myitian.Text;

namespace NbtSharp;

/// <summary>
/// Reads Java Edition NBT as a forward-only sequence of tokens over a contiguous span, a sequence of
/// spans, or a stream read through a fixed-size buffer.
/// </summary>
/// <remarks>
/// <para>
/// A span is read directly. A <see cref="ReadOnlySequence{T}"/> is read segment by segment, where
/// each segment is a contiguous span. A stream is read through a fixed-size pooled buffer, which
/// makes it equivalent to a sequence of contiguous spans; <see cref="Dispose"/> returns that buffer.
/// A stream reader must not be copied, because the pooled buffer is shared between copies.
/// </para>
/// <para>
/// The reader mirrors the NBT wire structure. A compound member produces a value or container-start
/// token whose <see cref="Name"/> is the member name; list elements have no name. <c>TAG_End</c> is
/// reported as <see cref="NbtTokenType.EndCompound"/>, and the end of a list is synthesized from its
/// declared element count without consuming an extra byte.
/// </para>
/// <para>
/// Typed getters read the current value and do not advance to the next token. Calling a getter for a
/// different tag type throws <see cref="NbtException"/> with the expected and actual tag types and
/// the byte position. API misuse, such as reading a value twice or calling a getter before
/// <see cref="Read"/>, throws <see cref="InvalidOperationException"/>.
/// </para>
/// </remarks>
public ref struct NbtReader
{
    private const int DefaultMaxDepth = 512;
    private const int DefaultMaxCollectionLength = 16 * 1024 * 1024;
    private const int DefaultStreamBufferSize = 64 * 1024;

    private static readonly ModifiedUTF8Encoding StringEncoding = new(throwOnInvalidBytes: true);

    private ReadOnlySpan<byte> _span;
    private readonly NbtRootFormat _rootFormat;
    private readonly int _maxDepth;
    private readonly int _maxCollectionLength;
    private readonly long _maxDocumentBytes;
    private readonly ReadOnlySequence<byte> _buffer;
    private SequencePosition _nextSegmentPosition;
    private readonly Stream? _stream;
    private readonly StreamBuffer? _streamBuffer;
    private List<ContainerState>? _containers;
    private long[]? _valuesReadAtDepth;
    private long _tokenStart;
    private long _valueStart;
    private bool _streamCompleted;
    private bool _rootRead;
    private bool _rootComplete;

    /// <summary>Initializes a reader over a contiguous span of NBT bytes.</summary>
    /// <param name="data">The complete NBT document.</param>
    /// <param name="options">The reader options, or the defaults.</param>
    public NbtReader(ReadOnlySpan<byte> data, NbtReaderOptions options = default)
    {
        ValidateOptions(options);
        this = default;
        _span = data;
        _rootFormat = options.RootFormat;
        _maxDepth = options.MaxDepth > 0 ? options.MaxDepth : DefaultMaxDepth;
        _maxCollectionLength = options.MaxCollectionLength > 0 ? options.MaxCollectionLength : DefaultMaxCollectionLength;
        _maxDocumentBytes = options.MaxDocumentBytes;
    }

    /// <summary>
    /// Initializes a reader over a possibly segmented sequence of NBT bytes. Each segment is read as
    /// a contiguous span.
    /// </summary>
    /// <param name="data">The complete NBT document.</param>
    /// <param name="options">The reader options, or the defaults.</param>
    public NbtReader(ReadOnlySequence<byte> data, NbtReaderOptions options = default)
    {
        ValidateOptions(options);
        this = default;
        _buffer = data;
        _nextSegmentPosition = data.Start;
        _rootFormat = options.RootFormat;
        _maxDepth = options.MaxDepth > 0 ? options.MaxDepth : DefaultMaxDepth;
        _maxCollectionLength = options.MaxCollectionLength > 0 ? options.MaxCollectionLength : DefaultMaxCollectionLength;
        _maxDocumentBytes = options.MaxDocumentBytes;
    }

    /// <summary>
    /// Initializes a reader over the remaining bytes of a stream. The reader reads through a
    /// fixed-size pooled buffer; call <see cref="Dispose"/> to return it.
    /// </summary>
    /// <param name="stream">The stream that supplies the document. It is left open.</param>
    /// <param name="options">The reader options, or the defaults.</param>
    public NbtReader(Stream stream, NbtReaderOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ValidateOptions(options);
        this = default;
        _stream = stream;
        _streamBuffer = new StreamBuffer(DefaultStreamBufferSize);
        _rootFormat = options.RootFormat;
        _maxDepth = options.MaxDepth > 0 ? options.MaxDepth : DefaultMaxDepth;
        _maxCollectionLength = options.MaxCollectionLength > 0 ? options.MaxCollectionLength : DefaultMaxCollectionLength;
        _maxDocumentBytes = options.MaxDocumentBytes;
    }

    /// <summary>Returns the stream buffer rented by this reader, if any.</summary>
    /// <remarks>
    /// The stream itself is left open. Span and sequence readers have no resources to release.
    /// </remarks>
    public void Dispose()
    {
        if (_streamBuffer is null)
        {
            return;
        }

        _span = default;
        _streamCompleted = true;
        _streamBuffer.Dispose();
    }

    /// <summary>Gets the type of the most recently produced token.</summary>
    public NbtTokenType TokenType { get; private set; }

    /// <summary>Gets the NBT tag type of the most recently produced token.</summary>
    public NbtTagType TagType { get; private set; }

    /// <summary>Gets the name of the current compound member or root tag, if any.</summary>
    public string? Name { get; private set; }

    /// <summary>Gets the number of bytes consumed from the start of the document.</summary>
    public long BytesConsumed { get; private set; }

    /// <summary>Gets the number of containers currently open, including the container of the current token.</summary>
    public readonly int CurrentDepth => _containers?.Count ?? 0;

    /// <summary>Gets the element type declared by the most recently read list header.</summary>
    public NbtTagType ListElementType { get; private set; }

    /// <summary>Gets the element count declared by the most recently read list header.</summary>
    public int ListLength { get; private set; }

    internal bool HasPendingValue { get; private set; }

    internal bool HasPendingContainer { get; private set; }

    internal readonly long ValuesReadAtDepth(int depth) =>
        _valuesReadAtDepth is not null && depth >= 0 && depth < _valuesReadAtDepth.Length
            ? _valuesReadAtDepth[depth]
            : 0;

    private ref ContainerState Top => ref CollectionsMarshal.AsSpan(_containers!)[^1];

    /// <summary>Advances the reader to the next value or container-end token.</summary>
    /// <returns><see langword="false"/> once the root value has been consumed.</returns>
    /// <exception cref="InvalidOperationException">The current value has not been read yet.</exception>
    /// <exception cref="NbtException">The data is malformed.</exception>
    public bool Read()
    {
        EnsureInitialized();

        if (HasPendingValue)
        {
            throw new InvalidOperationException("The current value has not been read. Call the matching getter or Skip first.");
        }

        HasPendingContainer = false;

        if (!_rootRead)
        {
            return ReadRootStart();
        }

        if (_rootComplete)
        {
            return false;
        }

        if (_containers is null || _containers.Count == 0)
        {
            _rootComplete = true;
            return false;
        }

        if (Top.IsList)
        {
            if (Top.Remaining > 0)
            {
                var elementType = Top.ElementType;
                Top.Remaining--;
                StartListElement(elementType);
                return true;
            }

            PopContainer();
            SetToken(NbtTokenType.EndList, NbtTagType.End, null);
            FinishRootIfClosed();
            return true;
        }

        var position = BytesConsumed;
        var tagType = ReadTagType();
        if (tagType == NbtTagType.End)
        {
            PopContainer();
            SetToken(NbtTokenType.EndCompound, NbtTagType.End, null);
            FinishRootIfClosed();
            return true;
        }

        var name = ReadString();
        StartTag(tagType, name, position);
        return true;
    }

    /// <summary>Skips the current value or complete container.</summary>
    /// <exception cref="InvalidOperationException">The reader is not positioned on a value or container.</exception>
    /// <exception cref="NbtException">The data is malformed.</exception>
    public void Skip()
    {
        EnsureInitialized();

        if (HasPendingValue)
        {
            var tagType = TagType;
            SkipValue(tagType);
            HasPendingValue = false;
            FinishRootIfClosed();
            return;
        }

        if (!HasPendingContainer)
        {
            throw new InvalidOperationException("The reader is not positioned on a value or container.");
        }

        HasPendingContainer = false;
        var skippedList = Top.IsList;
        var targetDepth = _containers!.Count - 1;
        while (_containers.Count > targetDepth)
        {
            if (Top.IsList)
            {
                if (Top.Remaining > 0)
                {
                    var elementType = Top.ElementType;
                    Top.Remaining--;
                    SkipValue(elementType);
                }
                else
                {
                    PopContainer();
                }

                continue;
            }

            var tagType = ReadTagType();
            if (tagType == NbtTagType.End)
            {
                PopContainer();
            }
            else
            {
                _ = ReadString();
                SkipValue(tagType);
            }
        }

        SetToken(skippedList ? NbtTokenType.EndList : NbtTokenType.EndCompound, NbtTagType.End, null);
        FinishRootIfClosed();
    }

    /// <summary>Reads the current value as a signed 8-bit integer.</summary>
    public sbyte GetSByte() => Expect(NbtTagType.Byte) ? ReadSByte() : default;

    /// <summary>Reads the current value as a signed 16-bit integer.</summary>
    public short GetInt16() => Expect(NbtTagType.Short) ? ReadInt16() : default;

    /// <summary>Reads the current value as a signed 32-bit integer.</summary>
    public int GetInt32() => Expect(NbtTagType.Int) ? ReadInt32() : 0;

    /// <summary>Reads the current value as a signed 64-bit integer.</summary>
    public long GetInt64() => Expect(NbtTagType.Long) ? ReadInt64() : 0;

    /// <summary>Reads the current value as a 32-bit IEEE 754 number.</summary>
    public float GetSingle() => Expect(NbtTagType.Float) ? ReadSingle() : 0;

    /// <summary>Reads the current value as a 64-bit IEEE 754 number.</summary>
    public double GetDouble() => Expect(NbtTagType.Double) ? ReadDouble() : 0;

    /// <summary>Reads the current value as a string.</summary>
    /// <remarks>
    /// Strings use Java modified UTF-8. Decoding follows <c>DataInputStream.readUTF</c>: lead bytes
    /// of 0xF0 or greater are rejected, while raw null bytes and overlong three-byte forms are
    /// accepted and normalized when the value is written again.
    /// </remarks>
    public string GetString() => Expect(NbtTagType.String) ? ReadString() : string.Empty;

    /// <summary>Reads the current value as a byte array.</summary>
    public byte[] GetByteArray()
    {
        if (!Expect(NbtTagType.ByteArray))
        {
            return [];
        }

        var length = ReadArrayLength();
        return ReadBytes(length);
    }

    /// <summary>Reads the current value as an integer array.</summary>
    public int[] GetInt32Array()
    {
        if (!Expect(NbtTagType.IntArray))
        {
            return [];
        }

        var length = ReadArrayLength();
        EnsurePayloadFits((long)length * sizeof(int));
        var values = new int[GetInitialArrayCapacity(length, sizeof(int))];
        var totalBytes = (long)length * sizeof(int);
        var filled = 0L;
        while (filled < totalBytes)
        {
            var capacityBytes = (long)values.Length * sizeof(int);
            if (filled == capacityBytes)
            {
                var newCapacity = (int)Math.Min(
                    length,
                    Math.Max((long)values.Length * 2, values.Length + 1));
                Array.Resize(ref values, newCapacity);
                capacityBytes = (long)values.Length * sizeof(int);
            }

            var take = (int)Math.Min(capacityBytes - filled, totalBytes - filled);
            ReadExactly(MemoryMarshal.AsBytes(values.AsSpan()).Slice((int)filled, take));
            filled += take;
        }

        if (BitConverter.IsLittleEndian)
        {
            BinaryPrimitives.ReverseEndianness(values.AsSpan(), values.AsSpan());
        }

        return values;
    }

    /// <summary>Reads the current value as a long array.</summary>
    public long[] GetInt64Array()
    {
        if (!Expect(NbtTagType.LongArray))
        {
            return [];
        }

        var length = ReadArrayLength();
        EnsurePayloadFits((long)length * sizeof(long));
        var values = new long[GetInitialArrayCapacity(length, sizeof(long))];
        var totalBytes = (long)length * sizeof(long);
        var filled = 0L;
        while (filled < totalBytes)
        {
            var capacityBytes = (long)values.Length * sizeof(long);
            if (filled == capacityBytes)
            {
                var newCapacity = (int)Math.Min(
                    length,
                    Math.Max((long)values.Length * 2, values.Length + 1));
                Array.Resize(ref values, newCapacity);
                capacityBytes = (long)values.Length * sizeof(long);
            }

            var take = (int)Math.Min(capacityBytes - filled, totalBytes - filled);
            ReadExactly(MemoryMarshal.AsBytes(values.AsSpan()).Slice((int)filled, take));
            filled += take;
        }

        if (BitConverter.IsLittleEndian)
        {
            BinaryPrimitives.ReverseEndianness(values.AsSpan(), values.AsSpan());
        }

        return values;
    }

    private readonly void EnsureInitialized()
    {
        if (_maxDepth == 0)
        {
            throw new InvalidOperationException("The reader was not initialized with NBT data.");
        }

        ThrowIfDisposed();
    }

    private static void ValidateOptions(NbtReaderOptions options)
    {
        if (options.RootFormat is not NbtRootFormat.Named and not NbtRootFormat.Unnamed)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Unknown root format.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(options.MaxDepth);
        ArgumentOutOfRangeException.ThrowIfNegative(options.MaxCollectionLength);
        ArgumentOutOfRangeException.ThrowIfNegative(options.MaxDocumentBytes);
    }

    private bool ReadRootStart()
    {
        var position = BytesConsumed;
        var tagType = ReadTagType();
        if (tagType == NbtTagType.End)
        {
            throw new NbtException("The root tag cannot be TAG_End.")
            {
                BytePosition = position,
                ActualTagType = NbtTagType.End,
            };
        }

        var name = _rootFormat == NbtRootFormat.Named ? ReadString() : null;
        _rootRead = true;
        StartTag(tagType, name, position);
        return true;
    }

    private void StartTag(NbtTagType tagType, string? name, long position)
    {
        if (tagType == NbtTagType.End)
        {
            throw new NbtException("Unexpected TAG_End.")
            {
                BytePosition = position,
                ActualTagType = NbtTagType.End,
            };
        }

        _tokenStart = position;
        IncrementValuesReadAtDepth(_containers?.Count ?? 0);
        switch (tagType)
        {
            case NbtTagType.Compound:
                PushContainer(isList: false, NbtTagType.End, remaining: 0);
                SetToken(NbtTokenType.StartCompound, tagType, name);
                HasPendingContainer = true;
                break;
            case NbtTagType.List:
                SetToken(NbtTokenType.StartList, tagType, name);
                ReadListHeader();
                HasPendingContainer = true;
                break;
            default:
                _valueStart = BytesConsumed;
                SetToken(NbtTokenType.Value, tagType, name);
                HasPendingValue = true;
                break;
        }
    }

    private void StartListElement(NbtTagType elementType)
    {
        if (elementType == NbtTagType.End)
        {
            throw new NbtException("A list of TAG_End cannot contain elements.");
        }

        _tokenStart = BytesConsumed;
        IncrementValuesReadAtDepth(_containers?.Count ?? 0);
        switch (elementType)
        {
            case NbtTagType.Compound:
                PushContainer(isList: false, NbtTagType.End, remaining: 0);
                SetToken(NbtTokenType.StartCompound, elementType, null);
                HasPendingContainer = true;
                break;
            case NbtTagType.List:
                SetToken(NbtTokenType.StartList, elementType, null);
                ReadListHeader();
                HasPendingContainer = true;
                break;
            default:
                _valueStart = BytesConsumed;
                SetToken(NbtTokenType.Value, elementType, null);
                HasPendingValue = true;
                break;
        }
    }

    private void IncrementValuesReadAtDepth(int depth)
    {
        if (_valuesReadAtDepth is null)
        {
            _valuesReadAtDepth = new long[8];
        }
        else if (depth >= _valuesReadAtDepth.Length)
        {
            Array.Resize(ref _valuesReadAtDepth, Math.Max(depth + 1, _valuesReadAtDepth.Length * 2));
        }

        _valuesReadAtDepth[depth]++;
    }

    private void ReadListHeader()
    {
        var position = BytesConsumed;
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

        ListElementType = elementType;
        ListLength = count;
        EnterContainer(isList: true, elementType, count);
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
        if (TryGetRemainingBytes(out var remaining) && required > remaining)
        {
            throw new NbtException(
                $"The NBT list declares {count} elements but the remaining data is too short.")
            {
                IsTruncated = true,
                BytePosition = position,
            };
        }
    }

    private void PushContainer(bool isList, NbtTagType elementType, int remaining) =>
        EnterContainer(isList, elementType, remaining);

    private void EnterContainer(bool isList, NbtTagType elementType, int remaining)
    {
        _containers ??= [];

        if (_containers.Count >= _maxDepth)
        {
            throw new NbtException($"The NBT nesting depth exceeds the limit of {_maxDepth}.")
            {
                BytePosition = BytesConsumed,
            };
        }

        _containers.Add(new ContainerState(isList, elementType, remaining));
    }

    private void PopContainer() => _containers!.RemoveAt(_containers.Count - 1);

    private void FinishRootIfClosed()
    {
        if (_containers is null || _containers.Count == 0)
        {
            _rootComplete = true;
        }
    }

    private void SetToken(NbtTokenType tokenType, NbtTagType tagType, string? name)
    {
        TokenType = tokenType;
        TagType = tagType;
        Name = name;
    }

    private NbtTagType ReadTagType()
    {
        var position = BytesConsumed;
        if (!TryReadByte(out var value))
        {
            throw new NbtException("Unexpected end of NBT data.")
            {
                IsTruncated = true,
                BytePosition = position,
            };
        }

        if (value > (byte)NbtTagType.LongArray)
        {
            throw new NbtException($"Unknown NBT tag type {value}.")
            {
                BytePosition = position,
            };
        }

        return (NbtTagType)value;
    }

    private bool Expect(NbtTagType expected)
    {
        if (!HasPendingValue)
        {
            if (TokenType is NbtTokenType.StartCompound or NbtTokenType.StartList)
            {
                throw new NbtException(
                    $"Expected a {expected} value but the current token is {TokenType} ({TagType}).")
                {
                    BytePosition = _tokenStart,
                    ExpectedTagType = expected,
                    ActualTagType = TagType,
                };
            }

            throw new InvalidOperationException("The reader is not positioned on a value.");
        }

        if (TagType != expected)
        {
            throw new NbtException(
                $"Expected a {expected} value but the current token is {TokenType} ({TagType}).")
            {
                BytePosition = _valueStart,
                ExpectedTagType = expected,
                ActualTagType = TagType,
            };
        }

        HasPendingValue = false;
        if (_containers is null || _containers.Count == 0)
        {
            _rootComplete = true;
        }

        return true;
    }

    private int ReadArrayLength()
    {
        var position = BytesConsumed;
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

    private void SkipValue(NbtTagType tagType)
    {
        switch (tagType)
        {
            case NbtTagType.Byte:
                SkipBytes(1);
                break;
            case NbtTagType.Short:
                SkipBytes(sizeof(short));
                break;
            case NbtTagType.Int:
            case NbtTagType.Float:
                SkipBytes(sizeof(int));
                break;
            case NbtTagType.Long:
            case NbtTagType.Double:
                SkipBytes(sizeof(long));
                break;
            case NbtTagType.ByteArray:
                SkipBytes(ReadArrayLength());
                break;
            case NbtTagType.String:
                SkipBytes(ReadUInt16());
                break;
            case NbtTagType.List:
                ReadListHeader();
                break;
            case NbtTagType.Compound:
                PushContainer(isList: false, NbtTagType.End, remaining: 0);
                break;
            case NbtTagType.IntArray:
                SkipBytes((long)ReadArrayLength() * sizeof(int));
                break;
            case NbtTagType.LongArray:
                SkipBytes((long)ReadArrayLength() * sizeof(long));
                break;
            default:
                throw new NbtException($"Cannot skip a value of type {tagType}.");
        }
    }

    private byte ReadByte()
    {
        var position = BytesConsumed;
        if (!TryReadByte(out var value))
        {
            throw new NbtException("Unexpected end of NBT data.")
            {
                IsTruncated = true,
                BytePosition = position,
            };
        }

        return value;
    }

    private sbyte ReadSByte() => (sbyte)ReadByte();

    private short ReadInt16()
    {
        Span<byte> buffer = stackalloc byte[sizeof(short)];
        ReadExactly(buffer);
        return BinaryPrimitives.ReadInt16BigEndian(buffer);
    }

    private ushort ReadUInt16()
    {
        Span<byte> buffer = stackalloc byte[sizeof(ushort)];
        ReadExactly(buffer);
        return BinaryPrimitives.ReadUInt16BigEndian(buffer);
    }

    private int ReadInt32()
    {
        Span<byte> buffer = stackalloc byte[sizeof(int)];
        ReadExactly(buffer);
        return BinaryPrimitives.ReadInt32BigEndian(buffer);
    }

    private long ReadInt64()
    {
        Span<byte> buffer = stackalloc byte[sizeof(long)];
        ReadExactly(buffer);
        return BinaryPrimitives.ReadInt64BigEndian(buffer);
    }

    private float ReadSingle()
    {
        Span<byte> buffer = stackalloc byte[sizeof(float)];
        ReadExactly(buffer);
        return BinaryPrimitives.ReadSingleBigEndian(buffer);
    }

    private double ReadDouble()
    {
        Span<byte> buffer = stackalloc byte[sizeof(double)];
        ReadExactly(buffer);
        return BinaryPrimitives.ReadDoubleBigEndian(buffer);
    }

    private string ReadString()
    {
        var position = BytesConsumed;
        var length = ReadUInt16();
        if (length == 0)
        {
            return string.Empty;
        }

        EnsurePayloadFits(length);
        if (_span.Length >= length)
        {
            var bytes = _span[..length];
            var value = DecodeString(bytes, position);
            _span = _span[length..];
            BytesConsumed += length;
            return value;

        }

        var buffer = new byte[length];
        ReadExactly(buffer);
        return DecodeString(buffer, position);
    }

    private static string DecodeString(ReadOnlySpan<byte> bytes, long position)
    {
        foreach (var value in bytes)
        {
            if (value >= 0xF0)
            {
                throw new NbtException(
                    "Invalid modified UTF-8 string: lead bytes of 0xF0 or greater are not allowed.")
                {
                    BytePosition = position,
                };
            }
        }

        try
        {
            return StringEncoding.GetString(bytes);
        }
        catch (DecoderFallbackException ex)
        {
            throw new NbtException("Invalid modified UTF-8 string.", ex)
            {
                BytePosition = position,
            };
        }
    }

    private byte[] ReadBytes(int count)
    {
        if (count == 0)
        {
            return [];
        }

        EnsurePayloadFits(count);
        if (_stream is null)
        {
            var bytes = new byte[count];
            ReadExactly(bytes);
            return bytes;
        }

        var buffer = new byte[GetInitialArrayCapacity(count, sizeof(byte))];
        var offset = 0;
        while (offset < count)
        {
            if (offset == buffer.Length)
            {
                var newCapacity = (int)Math.Min(
                    count,
                    Math.Max((long)buffer.Length * 2, buffer.Length + 1));
                Array.Resize(ref buffer, newCapacity);
            }

            var take = Math.Min(buffer.Length - offset, count - offset);
            ReadExactly(buffer.AsSpan(offset, take));
            offset += take;
        }

        return buffer;
    }

    private void ReadExactly(scoped Span<byte> destination)
    {
        if (destination.Length == 0)
        {
            return;
        }

        EnsureDocumentSize(destination.Length);
        var offset = 0;
        while (offset < destination.Length)
        {
            if (!EnsureData())
            {
                throw new NbtException("Unexpected end of NBT data.")
                {
                    IsTruncated = true,
                    BytePosition = BytesConsumed,
                };
            }

            var take = Math.Min(_span.Length, destination.Length - offset);
            _span[..take].CopyTo(destination[offset..]);
            _span = _span[take..];
            BytesConsumed += take;
            offset += take;
        }
    }

    private void SkipBytes(long count)
    {
        if (count < 0)
        {
            throw new NbtException($"Invalid NBT length {count}.")
            {
                BytePosition = BytesConsumed,
            };
        }

        if (count == 0)
        {
            return;
        }

        EnsureDocumentSize(count);
        while (count > 0)
        {
            if (!EnsureData())
            {
                throw new NbtException("Unexpected end of NBT data.")
                {
                    IsTruncated = true,
                    BytePosition = BytesConsumed,
                };
            }

            var take = Math.Min(count, _span.Length);
            _span = _span[(int)take..];
            BytesConsumed += take;
            count -= take;
        }
    }

    private bool TryReadByte(out byte value)
    {
        if (!EnsureData())
        {
            value = 0;
            return false;
        }

        EnsureDocumentSize(1);
        value = _span[0];
        _span = _span[1..];
        BytesConsumed++;
        return true;
    }

    private bool EnsureData()
    {
        ThrowIfDisposed();
        return !_span.IsEmpty || MoveNext();
    }

    private readonly void ThrowIfDisposed()
    {
        if (_streamBuffer is { IsDisposed: true })
        {
            throw new ObjectDisposedException(nameof(NbtReader));
        }
    }

    private bool MoveNext()
    {
        if (_stream is not null)
        {
            if (_streamCompleted)
            {
                return false;
            }

            var buffer = _streamBuffer!.Buffer;
            var read = _stream.Read(buffer);
            if (read == 0)
            {
                _streamCompleted = true;
                return false;
            }

            _span = buffer.AsSpan(0, read);
            return true;
        }

        return TryGetNextSegment();
    }

    private bool TryGetNextSegment()
    {
        while (_buffer.TryGet(ref _nextSegmentPosition, out var memory, advance: true))
        {
            if (!memory.IsEmpty)
            {
                _span = memory.Span;
                return true;
            }
        }

        return false;
    }

    private bool TryGetRemainingBytes(out long remaining)
    {
        if (_stream is not null)
        {
            remaining = 0;
            return false;
        }

        remaining = _buffer.IsEmpty ? _span.Length : _buffer.Length - BytesConsumed;
        return true;
    }

    private void EnsurePayloadFits(long byteCount)
    {
        if (TryGetRemainingBytes(out var remaining) && byteCount > remaining)
        {
            throw new NbtException(
                $"The NBT payload declares {byteCount} bytes but only {remaining} are available.")
            {
                IsTruncated = true,
                BytePosition = BytesConsumed,
            };
        }

        EnsureDocumentSize(byteCount);
    }

    private int GetInitialArrayCapacity(int length, int elementSize)
    {
        if (length == 0)
        {
            return 0;
        }

        if (_stream is null)
        {
            return length;
        }

        var segmentElements = Math.Max(1, DefaultStreamBufferSize / elementSize);
        return Math.Min(length, segmentElements);
    }

    private void EnsureDocumentSize(long count)
    {
        if (_maxDocumentBytes > 0 && count > _maxDocumentBytes - BytesConsumed)
        {
            throw new NbtException(
                $"The NBT document exceeds the configured limit of {_maxDocumentBytes} bytes.")
            {
                BytePosition = BytesConsumed,
            };
        }
    }

    private sealed class StreamBuffer(int size) : IDisposable
    {
        private byte[]? _buffer = ArrayPool<byte>.Shared.Rent(size);

        public bool IsDisposed => _buffer is null;

        public byte[] Buffer => _buffer ?? throw new ObjectDisposedException(nameof(StreamBuffer));

        public void Dispose()
        {
            var buffer = _buffer;
            _buffer = null;
            if (buffer is not null)
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    private struct ContainerState(bool isList, NbtTagType elementType, int remaining)
    {
        public bool IsList { get; } = isList;

        public NbtTagType ElementType { get; } = elementType;

        public int Remaining { get; set; } = remaining;
    }
}

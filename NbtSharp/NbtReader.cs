using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Runtime.InteropServices;
using System.Text;
using Myitian.Text;

namespace NbtSharp;

/// <summary>
/// Reads Java Edition NBT as a forward-only sequence of tokens over a span or a pipe reader.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="PipeReader"/> supplies the document incrementally: the reader advances the pipe as
/// it consumes bytes and blocks for more data only when the current buffer is exhausted. A
/// <see cref="ReadOnlySequence{T}"/> is exposed through a pipe reader as well, while the span
/// constructor is a contiguous in-memory fast path.
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

    private static readonly ModifiedUTF8Encoding StringEncoding = new(throwOnInvalidBytes: true);

    private readonly ReadOnlySpan<byte> _span;
    private readonly PipeReader? _pipe;
    private readonly NbtRootFormat _rootFormat;
    private readonly int _maxDepth;
    private readonly int _maxCollectionLength;
    private readonly long _maxDocumentBytes;
    private SequenceReader<byte> _sequence;
    private ReadOnlySequence<byte> _buffer;
    private List<ContainerState>? _containers;
    private long[]? _valuesReadAtDepth;
    private long _consumedBeforeBuffer;
    private int _offset;
    private long _tokenStart;
    private long _valueStart;
    private bool _pipeInitialized;
    private bool _pipeCompleted;
    private bool _ownsPipe;
    private bool _rootRead;
    private bool _rootComplete;
    private bool _valuePending;
    private bool _containerPending;
    private NbtTokenType _tokenType;
    private NbtTagType _tagType;
    private string? _name;
    private NbtTagType _listElementType;
    private int _listLength;

    /// <summary>Initializes a reader over a contiguous span of NBT bytes.</summary>
    /// <param name="data">The complete NBT document.</param>
    /// <param name="options">The reader options, or the defaults.</param>
    /// <exception cref="NbtException">The document exceeds <see cref="NbtReaderOptions.MaxDocumentBytes"/>.</exception>
    public NbtReader(ReadOnlySpan<byte> data, NbtReaderOptions options = default)
    {
        ValidateOptions(options);
        if (options.MaxDocumentBytes > 0 && data.Length > options.MaxDocumentBytes)
        {
            throw new NbtException($"The NBT document is larger than the configured limit of {options.MaxDocumentBytes} bytes.");
        }

        this = default;
        _span = data;
        _rootFormat = options.RootFormat;
        _maxDepth = options.MaxDepth > 0 ? options.MaxDepth : DefaultMaxDepth;
        _maxCollectionLength = options.MaxCollectionLength > 0 ? options.MaxCollectionLength : DefaultMaxCollectionLength;
        _maxDocumentBytes = options.MaxDocumentBytes;
    }

    /// <summary>
    /// Initializes a reader over a possibly segmented sequence of NBT bytes. The sequence is exposed
    /// through a <see cref="PipeReader"/> whose lifetime this instance owns.
    /// </summary>
    /// <param name="data">The complete NBT document.</param>
    /// <param name="options">The reader options, or the defaults.</param>
    /// <exception cref="NbtException">The document exceeds <see cref="NbtReaderOptions.MaxDocumentBytes"/>.</exception>
    public NbtReader(ReadOnlySequence<byte> data, NbtReaderOptions options = default)
        : this(CreateSequencePipe(data, options), options)
    {
        _ownsPipe = true;
    }

    /// <summary>Initializes a reader that pulls NBT bytes from <paramref name="pipe"/>.</summary>
    /// <param name="pipe">The pipe reader that supplies the document. The caller owns it.</param>
    /// <param name="options">The reader options, or the defaults.</param>
    /// <remarks>
    /// Reads are incremental. The reader marks consumed data with
    /// <see cref="PipeReader.AdvanceTo(SequencePosition, SequencePosition)"/> and blocks on
    /// <see cref="PipeReader.ReadAsync(CancellationToken)"/> when the current buffer is exhausted.
    /// </remarks>
    public NbtReader(PipeReader pipe, NbtReaderOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(pipe);
        ValidateOptions(options);
        this = default;
        _pipe = pipe;
        _rootFormat = options.RootFormat;
        _maxDepth = options.MaxDepth > 0 ? options.MaxDepth : DefaultMaxDepth;
        _maxCollectionLength = options.MaxCollectionLength > 0 ? options.MaxCollectionLength : DefaultMaxCollectionLength;
        _maxDocumentBytes = options.MaxDocumentBytes;
    }

    /// <summary>
    /// Marks consumed data as processed and completes the pipe reader created by this instance, if
    /// any.
    /// </summary>
    /// <remarks>
    /// Disposing a reader over a caller-supplied pipe advances that pipe without completing it, so
    /// the pipe can be reused. The span constructor has no pipe to advance.
    /// </remarks>
    public void Dispose()
    {
        ReleasePipe();
        if (!_ownsPipe)
        {
            return;
        }

        _ownsPipe = false;
        _pipe?.Complete();
    }

    private void ReleasePipe()
    {
        if (_pipe is null || !_pipeInitialized)
        {
            return;
        }

        _consumedBeforeBuffer += _sequence.Consumed;
        _pipe.AdvanceTo(_sequence.Position, _buffer.End);
        _sequence = default;
        _buffer = default;
        _pipeInitialized = false;
    }

    private static PipeReader CreateSequencePipe(ReadOnlySequence<byte> data, NbtReaderOptions options)
    {
        ValidateOptions(options);
        if (options.MaxDocumentBytes > 0 && data.Length > options.MaxDocumentBytes)
        {
            throw new NbtException($"The NBT document is larger than the configured limit of {options.MaxDocumentBytes} bytes.");
        }

        return PipeReader.Create(data);
    }

    /// <summary>Gets the type of the most recently produced token.</summary>
    public readonly NbtTokenType TokenType => _tokenType;

    /// <summary>Gets the NBT tag type of the most recently produced token.</summary>
    public readonly NbtTagType TagType => _tagType;

    /// <summary>Gets the name of the current compound member or root tag, if any.</summary>
    public readonly string? Name => _name;

    /// <summary>Gets the number of bytes consumed from the start of the document.</summary>
    public readonly long BytesConsumed =>
        _pipe is null ? _offset : _consumedBeforeBuffer + _sequence.Consumed;

    /// <summary>Gets the number of containers currently open, including the container of the current token.</summary>
    public readonly int CurrentDepth => _containers?.Count ?? 0;

    /// <summary>Gets the element type declared by the most recently read list header.</summary>
    public readonly NbtTagType ListElementType => _listElementType;

    /// <summary>Gets the element count declared by the most recently read list header.</summary>
    public readonly int ListLength => _listLength;

    internal readonly bool HasPendingValue => _valuePending;

    internal readonly bool HasPendingContainer => _containerPending;

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

        if (_valuePending)
        {
            throw new InvalidOperationException("The current value has not been read. Call the matching getter or Skip first.");
        }

        _containerPending = false;

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

        if (_valuePending)
        {
            var tagType = _tagType;
            SkipValue(tagType);
            _valuePending = false;
            FinishRootIfClosed();
            return;
        }

        if (!_containerPending)
        {
            throw new InvalidOperationException("The reader is not positioned on a value or container.");
        }

        _containerPending = false;
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
        EnsureDocumentSize((long)length * sizeof(int));
        var values = new int[length];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = ReadInt32();
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
        EnsureDocumentSize((long)length * sizeof(long));
        var values = new long[length];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = ReadInt64();
        }

        return values;
    }

    private readonly void EnsureInitialized()
    {
        if (_maxDepth == 0)
        {
            throw new InvalidOperationException("The reader was not initialized with NBT data.");
        }
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
                _containerPending = true;
                break;
            case NbtTagType.List:
                SetToken(NbtTokenType.StartList, tagType, name);
                ReadListHeader();
                _containerPending = true;
                break;
            default:
                _valueStart = BytesConsumed;
                SetToken(NbtTokenType.Value, tagType, name);
                _valuePending = true;
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
                _containerPending = true;
                break;
            case NbtTagType.List:
                SetToken(NbtTokenType.StartList, elementType, null);
                ReadListHeader();
                _containerPending = true;
                break;
            default:
                _valueStart = BytesConsumed;
                SetToken(NbtTokenType.Value, elementType, null);
                _valuePending = true;
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

        _listElementType = elementType;
        _listLength = count;
        EnterContainer(isList: true, elementType, count);
    }

    private void EnsureListElementsFit(NbtTagType elementType, int count, long position)
    {
        if (_pipe is not null)
        {
            return;
        }

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
        if (required > _span.Length - _offset)
        {
            throw new NbtException(
                $"The NBT list declares {count} elements but the remaining data is too short.")
            {
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
        _tokenType = tokenType;
        _tagType = tagType;
        _name = name;
    }

    private NbtTagType ReadTagType()
    {
        var position = BytesConsumed;
        if (!TryReadByte(out var value))
        {
            throw new NbtException("Unexpected end of NBT data.")
            {
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
        if (!_valuePending)
        {
            if (_tokenType is NbtTokenType.StartCompound or NbtTokenType.StartList)
            {
                throw new NbtException(
                    $"Expected a {expected} value but the current token is {_tokenType} ({_tagType}).")
                {
                    BytePosition = _tokenStart,
                    ExpectedTagType = expected,
                    ActualTagType = _tagType,
                };
            }

            throw new InvalidOperationException("The reader is not positioned on a value.");
        }

        if (_tagType != expected)
        {
            throw new NbtException(
                $"Expected a {expected} value but the current token is {_tokenType} ({_tagType}).")
            {
                BytePosition = _valueStart,
                ExpectedTagType = expected,
                ActualTagType = _tagType,
            };
        }

        _valuePending = false;
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

        if (_pipe is null)
        {
            if (length > _span.Length - _offset)
            {
                throw new NbtException("Unexpected end of NBT data while reading a string.")
                {
                    BytePosition = position,
                };
            }

            var span = _span.Slice(_offset, length);
            var result = DecodeString(span, position);
            _offset += length;
            return result;
        }

        EnsurePipeInitialized();
        EnsureDocumentSize(length);
        if (_sequence.UnreadSpan.Length >= length)
        {
            var bytes = _sequence.UnreadSpan[..length];
            var value = DecodeString(bytes, position);
            _sequence.Advance(length);
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

        EnsureDocumentSize(count);
        var bytes = new byte[count];
        ReadExactly(bytes);
        return bytes;
    }

    private void ReadExactly(scoped Span<byte> destination)
    {
        if (destination.Length == 0)
        {
            return;
        }

        if (_pipe is null)
        {
            if (destination.Length > _span.Length - _offset)
            {
                throw new NbtException("Unexpected end of NBT data.")
                {
                    BytePosition = BytesConsumed,
                };
            }

            _span.Slice(_offset, destination.Length).CopyTo(destination);
            _offset += destination.Length;
            return;
        }

        EnsurePipeInitialized();
        EnsureDocumentSize(destination.Length);
        var offset = 0;
        while (offset < destination.Length)
        {
            if (_sequence.Remaining == 0)
            {
                if (!TryRefill())
                {
                    throw new NbtException("Unexpected end of NBT data.")
                    {
                        BytePosition = BytesConsumed,
                    };
                }

                continue;
            }

            var count = (int)Math.Min(_sequence.Remaining, destination.Length - offset);
            if (!_sequence.TryCopyTo(destination.Slice(offset, count)))
            {
                throw new NbtException("Unexpected end of NBT data.")
                {
                    BytePosition = BytesConsumed,
                };
            }

            _sequence.Advance(count);
            offset += count;
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

        if (_pipe is null)
        {
            if (count > _span.Length - _offset)
            {
                throw new NbtException("Unexpected end of NBT data.")
                {
                    BytePosition = BytesConsumed,
                };
            }

            _offset += (int)count;
            return;
        }

        EnsurePipeInitialized();
        EnsureDocumentSize(count);
        while (count > 0)
        {
            if (_sequence.Remaining == 0)
            {
                if (!TryRefill())
                {
                    throw new NbtException("Unexpected end of NBT data.")
                    {
                        BytePosition = BytesConsumed,
                    };
                }

                continue;
            }

            var take = Math.Min(count, _sequence.Remaining);
            _sequence.Advance(take);
            count -= take;
        }
    }

    private bool TryReadByte(out byte value)
    {
        if (_pipe is null)
        {
            if (_offset < _span.Length)
            {
                value = _span[_offset];
                _offset++;
                return true;
            }

            value = 0;
            return false;
        }

        EnsurePipeInitialized();
        while (_sequence.Remaining == 0)
        {
            if (!TryRefill())
            {
                value = 0;
                return false;
            }
        }

        EnsureDocumentSize(1);
        return _sequence.TryRead(out value);
    }

    private void EnsurePipeInitialized()
    {
        if (_pipe is null || _pipeInitialized)
        {
            return;
        }

        _pipeInitialized = true;
        FillFromPipe();
    }

    private void FillFromPipe()
    {
        if (!_pipe!.TryRead(out var result))
        {
            result = _pipe.ReadAsync().AsTask().GetAwaiter().GetResult();
        }

        _buffer = result.Buffer;
        _sequence = new SequenceReader<byte>(result.Buffer);
        _pipeCompleted = result.IsCompleted;
    }

    private bool TryRefill()
    {
        if (_pipeCompleted)
        {
            return false;
        }

        _consumedBeforeBuffer += _sequence.Consumed;
        _pipe!.AdvanceTo(_sequence.Position, _buffer.End);
        FillFromPipe();
        return _sequence.Remaining > 0 || !_pipeCompleted;
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

    private struct ContainerState(bool isList, NbtTagType elementType, int remaining)
    {
        public bool IsList { get; } = isList;

        public NbtTagType ElementType { get; } = elementType;

        public int Remaining { get; set; } = remaining;
    }
}

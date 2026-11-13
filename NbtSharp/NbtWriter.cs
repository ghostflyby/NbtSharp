using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Myitian.Text;

namespace NbtSharp;

/// <summary>
/// Writes Java Edition NBT as a forward-only sequence of tokens to an <see cref="IBufferWriter{T}"/>.
/// </summary>
/// <remarks>
/// <para>
/// Named writes are atomic: <see cref="WriteInt32(string, int)"/>,
/// <see cref="WriteStartCompound(string)"/>, and the other named methods write the tag type, name,
/// and payload together, matching the NBT wire order. Anonymous writes are used for list elements
/// and for value payloads whose header was already written by the serializer.
/// </para>
/// <para>
/// A list header must be written with its element type and count before any element. The writer
/// verifies that every element has the declared type and that the declared number of elements were
/// written. <see cref="Complete"/> validates that the root value and all containers are complete.
/// </para>
/// </remarks>
public ref struct NbtWriter
{
    private const int DefaultMaxDepth = 512;
    private const int DefaultMaxCollectionLength = 16 * 1024 * 1024;

    private readonly IBufferWriter<byte> _destination;
    private readonly NbtRootFormat _rootFormat;
    private readonly int _maxDepth;
    private readonly int _maxCollectionLength;
    private readonly long _maxDocumentBytes;
    private List<ContainerState>? _containers;
    private bool _rootWritten;
    private bool _rootComplete;
    private bool _completed;
    private bool _payloadExpected;
    private NbtTagType _expectedTagType;
    private long _bytesWritten;
    private long[]? _valuesAtDepth;

    /// <summary>Initializes a writer over <paramref name="destination"/>.</summary>
    /// <param name="destination">The buffer writer that receives the NBT bytes.</param>
    /// <param name="options">The writer options, or the defaults.</param>
    public NbtWriter(IBufferWriter<byte> destination, NbtWriterOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ValidateOptions(options);

        this = default;
        _destination = destination;
        _rootFormat = options.RootFormat;
        _maxDepth = options.MaxDepth > 0 ? options.MaxDepth : DefaultMaxDepth;
        _maxCollectionLength = options.MaxCollectionLength > 0 ? options.MaxCollectionLength : DefaultMaxCollectionLength;
        _maxDocumentBytes = options.MaxDocumentBytes;
    }

    /// <summary>Gets the number of bytes written so far.</summary>
    public readonly long BytesWritten => _bytesWritten;

    /// <summary>Gets the number of containers currently open.</summary>
    public readonly int CurrentDepth => _containers?.Count ?? 0;

    internal readonly long ValuesWrittenAtDepth(int depth) =>
        _valuesAtDepth is not null && depth >= 0 && depth < _valuesAtDepth.Length ? _valuesAtDepth[depth] : 0;

    internal readonly bool IsPayloadExpected => _payloadExpected;

    internal readonly bool IsInList => _containers is { Count: > 0 } && _containers[^1].IsList;

    internal readonly bool IsInCompound => _containers is { Count: > 0 } && !_containers[^1].IsList;

    private ref ContainerState Top => ref CollectionsMarshal.AsSpan(_containers!)[^1];

    /// <summary>Writes a named signed 8-bit integer.</summary>
    public void WriteSByte(string name, sbyte value)
    {
        WriteNamedHeader(NbtTagType.Byte, name);
        WriteByte((byte)value);
        FinishScalar();
    }

    /// <summary>Writes a named signed 16-bit integer.</summary>
    public void WriteInt16(string name, short value)
    {
        WriteNamedHeader(NbtTagType.Short, name);
        WriteInt16Payload(value);
        FinishScalar();
    }

    /// <summary>Writes a named signed 32-bit integer.</summary>
    public void WriteInt32(string name, int value)
    {
        WriteNamedHeader(NbtTagType.Int, name);
        WriteInt32Payload(value);
        FinishScalar();
    }

    /// <summary>Writes a named signed 64-bit integer.</summary>
    public void WriteInt64(string name, long value)
    {
        WriteNamedHeader(NbtTagType.Long, name);
        WriteInt64Payload(value);
        FinishScalar();
    }

    /// <summary>Writes a named 32-bit IEEE 754 number.</summary>
    public void WriteSingle(string name, float value)
    {
        WriteNamedHeader(NbtTagType.Float, name);
        WriteSinglePayload(value);
        FinishScalar();
    }

    /// <summary>Writes a named 64-bit IEEE 754 number.</summary>
    public void WriteDouble(string name, double value)
    {
        WriteNamedHeader(NbtTagType.Double, name);
        WriteDoublePayload(value);
        FinishScalar();
    }

    /// <summary>Writes a named string from a span of characters.</summary>
    public void WriteString(string name, ReadOnlySpan<char> value)
    {
        ValidateStringLength(value);
        WriteNamedHeader(NbtTagType.String, name);
        WriteStringPayload(value);
        FinishScalar();
    }

    /// <summary>Writes a named string.</summary>
    public void WriteString(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        WriteString(name, value.AsSpan());
    }

    /// <summary>Writes a named byte array.</summary>
    public void WriteByteArray(string name, ReadOnlySpan<byte> values)
    {
        ValidateCollectionLength(values.Length);
        WriteNamedHeader(NbtTagType.ByteArray, name);
        WriteInt32Payload(values.Length);
        WriteBytes(values);
        FinishScalar();
    }

    /// <summary>Writes a named integer array.</summary>
    public void WriteInt32Array(string name, ReadOnlySpan<int> values)
    {
        ValidateCollectionLength(values.Length);
        WriteNamedHeader(NbtTagType.IntArray, name);
        WriteInt32Payload(values.Length);
        foreach (var value in values)
        {
            WriteInt32Payload(value);
        }

        FinishScalar();
    }

    /// <summary>Writes a named long array.</summary>
    public void WriteInt64Array(string name, ReadOnlySpan<long> values)
    {
        ValidateCollectionLength(values.Length);
        WriteNamedHeader(NbtTagType.LongArray, name);
        WriteInt32Payload(values.Length);
        foreach (var value in values)
        {
            WriteInt64Payload(value);
        }

        FinishScalar();
    }

    /// <summary>Writes the start of a named compound.</summary>
    public void WriteStartCompound(string name)
    {
        EnsureCanEnterContainer();
        WriteNamedHeader(NbtTagType.Compound, name);
        PushContainer(isList: false, NbtTagType.End, remaining: 0);
    }

    /// <summary>Writes the start of a named list.</summary>
    public void WriteStartList(string name, NbtTagType elementType, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ValidateListHeader(elementType, count);
        EnsureCanEnterContainer();
        WriteNamedHeader(NbtTagType.List, name);
        WriteByte((byte)elementType);
        WriteInt32Payload(count);
        PushContainer(isList: true, elementType, count);
    }

    /// <summary>Writes an anonymous signed 8-bit integer.</summary>
    /// <remarks>Valid for a list element or a value payload whose header was already written.</remarks>
    public void WriteSByte(sbyte value)
    {
        WriteAnonymousHeader(NbtTagType.Byte);
        WriteByte((byte)value);
        FinishScalar();
    }

    /// <summary>Writes an anonymous signed 16-bit integer.</summary>
    public void WriteInt16(short value)
    {
        WriteAnonymousHeader(NbtTagType.Short);
        WriteInt16Payload(value);
        FinishScalar();
    }

    /// <summary>Writes an anonymous signed 32-bit integer.</summary>
    public void WriteInt32(int value)
    {
        WriteAnonymousHeader(NbtTagType.Int);
        WriteInt32Payload(value);
        FinishScalar();
    }

    /// <summary>Writes an anonymous signed 64-bit integer.</summary>
    public void WriteInt64(long value)
    {
        WriteAnonymousHeader(NbtTagType.Long);
        WriteInt64Payload(value);
        FinishScalar();
    }

    /// <summary>Writes an anonymous 32-bit IEEE 754 number.</summary>
    public void WriteSingle(float value)
    {
        WriteAnonymousHeader(NbtTagType.Float);
        WriteSinglePayload(value);
        FinishScalar();
    }

    /// <summary>Writes an anonymous 64-bit IEEE 754 number.</summary>
    public void WriteDouble(double value)
    {
        WriteAnonymousHeader(NbtTagType.Double);
        WriteDoublePayload(value);
        FinishScalar();
    }

    /// <summary>Writes an anonymous string from a span of characters.</summary>
    public void WriteString(ReadOnlySpan<char> value)
    {
        ValidateStringLength(value);
        WriteAnonymousHeader(NbtTagType.String);
        WriteStringPayload(value);
        FinishScalar();
    }

    /// <summary>Writes an anonymous string.</summary>
    public void WriteString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        WriteString(value.AsSpan());
    }

    /// <summary>Writes an anonymous byte array.</summary>
    public void WriteByteArray(ReadOnlySpan<byte> values)
    {
        ValidateCollectionLength(values.Length);
        WriteAnonymousHeader(NbtTagType.ByteArray);
        WriteInt32Payload(values.Length);
        WriteBytes(values);
        FinishScalar();
    }

    /// <summary>Writes an anonymous integer array.</summary>
    public void WriteInt32Array(ReadOnlySpan<int> values)
    {
        ValidateCollectionLength(values.Length);
        WriteAnonymousHeader(NbtTagType.IntArray);
        WriteInt32Payload(values.Length);
        foreach (var value in values)
        {
            WriteInt32Payload(value);
        }

        FinishScalar();
    }

    /// <summary>Writes an anonymous long array.</summary>
    public void WriteInt64Array(ReadOnlySpan<long> values)
    {
        ValidateCollectionLength(values.Length);
        WriteAnonymousHeader(NbtTagType.LongArray);
        WriteInt32Payload(values.Length);
        foreach (var value in values)
        {
            WriteInt64Payload(value);
        }

        FinishScalar();
    }

    /// <summary>Writes the start of an anonymous compound.</summary>
    public void WriteStartCompound()
    {
        EnsureCanEnterContainer();
        WriteAnonymousHeader(NbtTagType.Compound);
        PushContainer(isList: false, NbtTagType.End, remaining: 0);
    }

    /// <summary>Writes the start of an anonymous list.</summary>
    public void WriteStartList(NbtTagType elementType, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ValidateListHeader(elementType, count);
        EnsureCanEnterContainer();
        WriteAnonymousHeader(NbtTagType.List);
        WriteByte((byte)elementType);
        WriteInt32Payload(count);
        PushContainer(isList: true, elementType, count);
    }

    /// <summary>Writes the end of the current compound.</summary>
    public void WriteEndCompound()
    {
        EnsureNotCompleted();
        if (_payloadExpected)
        {
            throw new InvalidOperationException("A value payload is expected but no value was written.");
        }

        if (_containers is null || _containers.Count == 0 || _containers[^1].IsList)
        {
            throw new InvalidOperationException("There is no open compound to close.");
        }

        WriteByte((byte)NbtTagType.End);
        PopContainer();
        FinishRootIfClosed();
    }

    /// <summary>Writes the end of the current list.</summary>
    public void WriteEndList()
    {
        EnsureNotCompleted();
        if (_payloadExpected)
        {
            throw new InvalidOperationException("A value payload is expected but no value was written.");
        }

        if (_containers is null || _containers.Count == 0 || !_containers[^1].IsList)
        {
            throw new InvalidOperationException("There is no open list to close.");
        }

        if (_containers[^1].Remaining != 0)
        {
            throw new NbtException($"{_containers[^1].Remaining} list elements promised by the header were not written.")
            {
                BytePosition = _bytesWritten,
            };
        }

        PopContainer();
        FinishRootIfClosed();
    }

    /// <summary>Validates that the root value and all containers are complete.</summary>
    /// <exception cref="InvalidOperationException">The document is not complete.</exception>
    public void Complete()
    {
        EnsureNotCompleted();

        if (!_rootWritten)
        {
            throw new InvalidOperationException("No root value was written.");
        }

        if (_payloadExpected)
        {
            throw new InvalidOperationException("A value payload is expected but no value was written.");
        }

        if (!_rootComplete || (_containers is not null && _containers.Count > 0))
        {
            throw new InvalidOperationException("The root value is not complete; close all open containers first.");
        }

        _completed = true;
    }

    internal void WriteRootHeader(NbtTagType tagType, string rootName)
    {
        EnsureNotCompleted();
        if (_payloadExpected)
        {
            throw new InvalidOperationException("A value payload is already expected.");
        }

        if (_rootWritten || _rootComplete)
        {
            throw new InvalidOperationException("The root value has already been written.");
        }

        ArgumentNullException.ThrowIfNull(rootName);
        ValidateStringLength(rootName.AsSpan());
        WriteByte((byte)tagType);
        if (_rootFormat == NbtRootFormat.Named)
        {
            WriteStringPayload(rootName.AsSpan());
        }

        _rootWritten = true;
        IncrementValuesAtDepth(0);
        _payloadExpected = true;
        _expectedTagType = tagType;
    }

    internal void WriteMemberHeader(NbtTagType tagType, string name)
    {
        EnsureNotCompleted();
        if (_payloadExpected)
        {
            throw new InvalidOperationException("A value payload is already expected.");
        }

        if (_rootComplete)
        {
            throw new InvalidOperationException("The root value is already complete.");
        }

        if (_containers is null || _containers.Count == 0)
        {
            throw new InvalidOperationException("Root values use WriteRootHeader or a named write method.");
        }

        if (Top.IsList)
        {
            throw new InvalidOperationException("List elements use anonymous writes.");
        }

        ArgumentNullException.ThrowIfNull(name);
        ValidateStringLength(name.AsSpan());
        WriteByte((byte)tagType);
        WriteStringPayload(name.AsSpan());
        IncrementValuesAtDepth(_containers.Count);
        _payloadExpected = true;
        _expectedTagType = tagType;
    }

    private void WriteNamedHeader(NbtTagType tagType, string name)
    {
        EnsureNotCompleted();
        if (_payloadExpected)
        {
            throw new InvalidOperationException("A value payload is already expected.");
        }

        if (_rootComplete)
        {
            throw new InvalidOperationException("The root value is already complete.");
        }

        ArgumentNullException.ThrowIfNull(name);
        ValidateStringLength(name.AsSpan());

        if (_containers is null || _containers.Count == 0)
        {
            if (_rootWritten)
            {
                throw new InvalidOperationException("The root value has already been written.");
            }

            WriteByte((byte)tagType);
            if (_rootFormat == NbtRootFormat.Named)
            {
                WriteStringPayload(name.AsSpan());
            }

            _rootWritten = true;
            IncrementValuesAtDepth(0);
            return;
        }

        if (Top.IsList)
        {
            throw new InvalidOperationException("List elements use the anonymous write methods.");
        }

        WriteByte((byte)tagType);
        WriteStringPayload(name.AsSpan());
        IncrementValuesAtDepth(_containers.Count);
    }

    private void WriteAnonymousHeader(NbtTagType tagType)
    {
        EnsureNotCompleted();
        if (_payloadExpected)
        {
            if (tagType != _expectedTagType)
            {
                throw new NbtException($"The member header declared {_expectedTagType} but {tagType} was written.")
                {
                    BytePosition = _bytesWritten,
                    ExpectedTagType = _expectedTagType,
                    ActualTagType = tagType,
                };
            }

            _payloadExpected = false;
            return;
        }

        if (_rootComplete)
        {
            throw new InvalidOperationException("The root value is already complete.");
        }

        if (_containers is null || _containers.Count == 0)
        {
            throw new InvalidOperationException("Anonymous values require a list or a pending member/root payload.");
        }

        if (!Top.IsList)
        {
            throw new InvalidOperationException("Compound members require a name; use a named write method or SerializeMember.");
        }

        ref var top = ref Top;
        if (top.ElementType != tagType)
        {
            throw new NbtException($"The list declares elements of type {top.ElementType} but {tagType} was written.")
            {
                BytePosition = _bytesWritten,
                ExpectedTagType = top.ElementType,
                ActualTagType = tagType,
            };
        }

        if (top.Remaining == 0)
        {
            throw new NbtException("The list header declared no more elements.")
            {
                BytePosition = _bytesWritten,
            };
        }

        top.Remaining--;
        IncrementValuesAtDepth(_containers.Count);
    }

    private static void ValidateOptions(NbtWriterOptions options)
    {
        if (options.RootFormat is not NbtRootFormat.Named and not NbtRootFormat.Unnamed)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Unknown root format.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(options.MaxDepth);
        ArgumentOutOfRangeException.ThrowIfNegative(options.MaxCollectionLength);
        ArgumentOutOfRangeException.ThrowIfNegative(options.MaxDocumentBytes);
    }

    private void ValidateStringLength(ReadOnlySpan<char> value)
    {
        var byteCount = ModifiedUTF8Encoding.Instance.GetByteCount(value);
        if (byteCount > ushort.MaxValue)
        {
            throw new NbtException($"The string is too long to encode: {byteCount} bytes.")
            {
                BytePosition = _bytesWritten,
            };
        }
    }

    private void ValidateCollectionLength(int count)
    {
        if (count > _maxCollectionLength)
        {
            throw new NbtException(
                $"The collection length {count} exceeds the configured limit of {_maxCollectionLength}.")
            {
                BytePosition = _bytesWritten,
            };
        }
    }

    private void ValidateListHeader(NbtTagType elementType, int count)
    {
        if ((byte)elementType > (byte)NbtTagType.LongArray)
        {
            throw new NbtException($"Unknown NBT list element type {(byte)elementType}.")
            {
                BytePosition = _bytesWritten,
            };
        }

        if (elementType == NbtTagType.End && count != 0)
        {
            throw new NbtException("A list of TAG_End must have length zero.")
            {
                BytePosition = _bytesWritten,
                ExpectedTagType = NbtTagType.End,
                ActualTagType = elementType,
            };
        }

        ValidateCollectionLength(count);
    }

    private void EnsureNotCompleted()
    {
        if (_destination is null)
        {
            throw new InvalidOperationException("The writer was not initialized with a destination.");
        }

        if (_completed)
        {
            throw new InvalidOperationException("The writer has already been completed.");
        }
    }

    private void EnsureCanEnterContainer()
    {
        EnsureNotCompleted();
        if ((_containers?.Count ?? 0) >= _maxDepth)
        {
            throw new NbtException($"The NBT nesting depth exceeds the limit of {_maxDepth}.")
            {
                BytePosition = _bytesWritten,
            };
        }
    }

    private void IncrementValuesAtDepth(int depth)
    {
        if (_valuesAtDepth is null)
        {
            _valuesAtDepth = new long[8];
        }
        else if (depth >= _valuesAtDepth.Length)
        {
            Array.Resize(ref _valuesAtDepth, Math.Max(depth + 1, _valuesAtDepth.Length * 2));
        }

        _valuesAtDepth[depth]++;
    }

    private void FinishScalar()
    {
        if (_containers is null || _containers.Count == 0)
        {
            if (_rootWritten)
            {
                _rootComplete = true;
            }
        }
    }

    private void FinishRootIfClosed()
    {
        if (_containers is null || _containers.Count == 0)
        {
            _rootComplete = true;
        }
    }

    private void PushContainer(bool isList, NbtTagType elementType, int remaining)
    {
        _containers ??= [];
        _containers.Add(new ContainerState(isList, elementType, remaining));
    }

    private void PopContainer() => _containers!.RemoveAt(_containers.Count - 1);

    private void WriteByte(byte value)
    {
        var span = GetSpan(1);
        span[0] = value;
        Advance(1);
    }

    private void WriteInt16Payload(short value)
    {
        var span = GetSpan(sizeof(short));
        BinaryPrimitives.WriteInt16BigEndian(span, value);
        Advance(sizeof(short));
    }

    private void WriteUInt16Payload(ushort value)
    {
        var span = GetSpan(sizeof(ushort));
        BinaryPrimitives.WriteUInt16BigEndian(span, value);
        Advance(sizeof(ushort));
    }

    private void WriteInt32Payload(int value)
    {
        var span = GetSpan(sizeof(int));
        BinaryPrimitives.WriteInt32BigEndian(span, value);
        Advance(sizeof(int));
    }

    private void WriteInt64Payload(long value)
    {
        var span = GetSpan(sizeof(long));
        BinaryPrimitives.WriteInt64BigEndian(span, value);
        Advance(sizeof(long));
    }

    private void WriteSinglePayload(float value)
    {
        var span = GetSpan(sizeof(float));
        BinaryPrimitives.WriteSingleBigEndian(span, value);
        Advance(sizeof(float));
    }

    private void WriteDoublePayload(double value)
    {
        var span = GetSpan(sizeof(double));
        BinaryPrimitives.WriteDoubleBigEndian(span, value);
        Advance(sizeof(double));
    }

    private void WriteStringPayload(ReadOnlySpan<char> value)
    {
        var byteCount = ModifiedUTF8Encoding.Instance.GetByteCount(value);
        if (byteCount > ushort.MaxValue)
        {
            throw new NbtException($"The string is too long to encode: {byteCount} bytes.")
            {
                BytePosition = _bytesWritten,
            };
        }

        WriteUInt16Payload((ushort)byteCount);
        if (byteCount == 0)
        {
            return;
        }

        var span = GetSpan(byteCount);
        var written = ModifiedUTF8Encoding.Instance.GetBytes(value, span);
        if (written != byteCount)
        {
            throw new NbtException($"The string encoder wrote {written} bytes but {byteCount} were expected.")
            {
                BytePosition = _bytesWritten,
            };
        }

        Advance(written);
    }

    private void WriteBytes(ReadOnlySpan<byte> bytes)
    {
        while (bytes.Length > 0)
        {
            var span = GetSpan(Math.Min(bytes.Length, 4096));
            var count = Math.Min(span.Length, bytes.Length);
            EnsureDocumentSize(count);
            bytes[..count].CopyTo(span);
            Advance(count);
            bytes = bytes[count..];
        }
    }

    private Span<byte> GetSpan(int sizeHint)
    {
        EnsureDocumentSize(sizeHint);
        return _destination.GetSpan(sizeHint);
    }

    private void Advance(int count)
    {
        _destination.Advance(count);
        _bytesWritten += count;
    }

    private void EnsureDocumentSize(int count)
    {
        if (_maxDocumentBytes > 0 && _bytesWritten + count > _maxDocumentBytes)
        {
            throw new NbtException($"The NBT document exceeds the configured limit of {_maxDocumentBytes} bytes.")
            {
                BytePosition = _bytesWritten,
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

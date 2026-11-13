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
/// Compound member names are deferred: <see cref="WritePropertyName"/> stores the name, and the next
/// value or container-start call writes the tag type, name, and payload together. This lets a
/// converter choose the tag type from the value, including inside a list.
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
    private string? _pendingName;
    private bool _rootWritten;
    private bool _rootComplete;
    private bool _completed;
    private bool _payloadOnly;
    private long _bytesWritten;
    private long _valuesWritten;
    private long[]? _valuesAtDepth;
    private NbtTagType _rootTagType;

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

    internal NbtWriter(IBufferWriter<byte> destination, NbtWriterOptions options, bool payloadOnly)
        : this(destination, options)
    {
        _payloadOnly = payloadOnly;
    }

    /// <summary>Gets the number of bytes written so far.</summary>
    public readonly long BytesWritten => _bytesWritten;

    /// <summary>Gets the number of containers currently open.</summary>
    public readonly int CurrentDepth => _containers?.Count ?? 0;

    internal readonly long ValuesWritten => _valuesWritten;

    internal readonly long ValuesWrittenAtDepth(int depth) =>
        _valuesAtDepth is not null && depth >= 0 && depth < _valuesAtDepth.Length ? _valuesAtDepth[depth] : 0;

    internal readonly NbtTagType RootTagType => _rootTagType;

    internal readonly bool HasPendingName => _pendingName is not null;

    internal readonly bool IsInList => _containers is { Count: > 0 } && _containers[^1].IsList;

    internal readonly bool IsInCompound => _containers is { Count: > 0 } && !_containers[^1].IsList;

    private ref ContainerState Top => ref CollectionsMarshal.AsSpan(_containers!)[^1];

    /// <summary>Stores the name of the next compound member or the root tag.</summary>
    /// <param name="name">The tag name; may be empty.</param>
    /// <exception cref="InvalidOperationException">A name is already pending or names are not allowed here.</exception>
    public void WritePropertyName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        EnsureNotCompleted();
        ValidateStringLength(name.AsSpan());

        if (_pendingName is not null)
        {
            throw new InvalidOperationException("A property name is already pending.");
        }

        if (_containers is null || _containers.Count == 0)
        {
            if (_rootWritten || _rootComplete)
            {
                throw new InvalidOperationException("The root value has already been written.");
            }

            if (_payloadOnly)
            {
                throw new InvalidOperationException("A probe writer does not accept a root name.");
            }

            if (_rootFormat == NbtRootFormat.Unnamed)
            {
                throw new InvalidOperationException("The root format is unnamed; a property name is not allowed.");
            }

            _pendingName = name;
            return;
        }

        if (Top.IsList)
        {
            throw new InvalidOperationException("List elements do not have names.");
        }

        _pendingName = name;
    }

    /// <summary>Writes a signed 8-bit integer.</summary>
    public void WriteSByte(sbyte value)
    {
        WriteValueHeader(NbtTagType.Byte);
        WriteByte((byte)value);
        FinishScalar();
    }

    /// <summary>Writes a signed 16-bit integer.</summary>
    public void WriteInt16(short value)
    {
        WriteValueHeader(NbtTagType.Short);
        WriteInt16Payload(value);
        FinishScalar();
    }

    /// <summary>Writes a signed 32-bit integer.</summary>
    public void WriteInt32(int value)
    {
        WriteValueHeader(NbtTagType.Int);
        WriteInt32Payload(value);
        FinishScalar();
    }

    /// <summary>Writes a signed 64-bit integer.</summary>
    public void WriteInt64(long value)
    {
        WriteValueHeader(NbtTagType.Long);
        WriteInt64Payload(value);
        FinishScalar();
    }

    /// <summary>Writes a 32-bit IEEE 754 number.</summary>
    public void WriteSingle(float value)
    {
        WriteValueHeader(NbtTagType.Float);
        WriteSinglePayload(value);
        FinishScalar();
    }

    /// <summary>Writes a 64-bit IEEE 754 number.</summary>
    public void WriteDouble(double value)
    {
        WriteValueHeader(NbtTagType.Double);
        WriteDoublePayload(value);
        FinishScalar();
    }

    /// <summary>Writes a string from a span of characters.</summary>
    public void WriteString(ReadOnlySpan<char> value)
    {
        ValidateStringLength(value);
        WriteValueHeader(NbtTagType.String);
        WriteStringPayload(value);
        FinishScalar();
    }

    /// <summary>Writes a string.</summary>
    public void WriteString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        WriteString(value.AsSpan());
    }

    /// <summary>Writes a byte array.</summary>
    public void WriteByteArray(ReadOnlySpan<byte> values)
    {
        ValidateCollectionLength(values.Length);
        WriteValueHeader(NbtTagType.ByteArray);
        WriteInt32Payload(values.Length);
        WriteBytes(values);
        FinishScalar();
    }

    /// <summary>Writes an integer array.</summary>
    public void WriteInt32Array(ReadOnlySpan<int> values)
    {
        ValidateCollectionLength(values.Length);
        WriteValueHeader(NbtTagType.IntArray);
        WriteInt32Payload(values.Length);
        foreach (var value in values)
        {
            WriteInt32Payload(value);
        }

        FinishScalar();
    }

    /// <summary>Writes a long array.</summary>
    public void WriteInt64Array(ReadOnlySpan<long> values)
    {
        ValidateCollectionLength(values.Length);
        WriteValueHeader(NbtTagType.LongArray);
        WriteInt32Payload(values.Length);
        foreach (var value in values)
        {
            WriteInt64Payload(value);
        }

        FinishScalar();
    }

    /// <summary>Writes the start of a compound.</summary>
    public void WriteStartCompound()
    {
        EnsureCanEnterContainer();
        WriteValueHeader(NbtTagType.Compound);
        PushContainer(isList: false, NbtTagType.End, remaining: 0);
    }

    /// <summary>Writes the end of the current compound.</summary>
    public void WriteEndCompound()
    {
        EnsureNotCompleted();
        if (_containers is null || _containers.Count == 0 || _containers[^1].IsList)
        {
            throw new InvalidOperationException("There is no open compound to close.");
        }

        if (_pendingName is not null)
        {
            throw new InvalidOperationException("A compound member name is pending but no value was written.");
        }

        WriteByte((byte)NbtTagType.End);
        PopContainer();
        FinishRootIfClosed();
    }

    /// <summary>Writes the start of a list.</summary>
    /// <param name="elementType">The tag type shared by all elements.</param>
    /// <param name="count">The number of elements that will be written.</param>
    public void WriteStartList(NbtTagType elementType, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ValidateListHeader(elementType, count);
        EnsureCanEnterContainer();
        WriteValueHeader(NbtTagType.List);
        WriteByte((byte)elementType);
        WriteInt32Payload(count);
        PushContainer(isList: true, elementType, count);
    }

    /// <summary>Writes the end of the current list.</summary>
    public void WriteEndList()
    {
        EnsureNotCompleted();
        if (_containers is null || _containers.Count == 0 || !_containers[^1].IsList)
        {
            throw new InvalidOperationException("There is no open list to close.");
        }

        if (_pendingName is not null)
        {
            throw new InvalidOperationException("A property name was written inside a list.");
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

        if (!_rootComplete || (_containers is not null && _containers.Count > 0))
        {
            throw new InvalidOperationException("The root value is not complete; close all open containers first.");
        }

        if (_pendingName is not null)
        {
            throw new InvalidOperationException("A property name is pending but no value was written.");
        }

        _completed = true;
    }

    internal void SkipPendingPropertyName()
    {
        EnsureNotCompleted();
        if (_pendingName is null)
        {
            throw new InvalidOperationException("There is no pending property name.");
        }

        if (_containers is null || _containers.Count == 0)
        {
            throw new InvalidOperationException("The root value cannot be omitted.");
        }

        if (_containers[^1].IsList)
        {
            throw new InvalidOperationException("List elements cannot be omitted.");
        }

        _pendingName = null;
    }

    internal void WriteBufferedListElement(NbtTagType tagType, ReadOnlySpan<byte> payload)
    {
        EnsureNotCompleted();
        if (_containers is null || _containers.Count == 0 || !_containers[^1].IsList)
        {
            throw new InvalidOperationException("There is no open list to write an element into.");
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
        _valuesWritten++;
        IncrementValuesAtDepth(_containers.Count);
        WriteBytes(payload);
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

    private void WriteValueHeader(NbtTagType tagType)
    {
        EnsureNotCompleted();
        if (_rootComplete)
        {
            throw new InvalidOperationException("The root value is already complete.");
        }

        if (_containers is null || _containers.Count == 0)
        {
            if (_rootWritten)
            {
                throw new InvalidOperationException("The root value has already been written.");
            }

            if (_payloadOnly)
            {
                _pendingName = null;
                _rootTagType = tagType;
                _rootWritten = true;
                _valuesWritten++;
                IncrementValuesAtDepth(0);
                return;
            }

            if (_rootFormat == NbtRootFormat.Named)
            {
                if (_pendingName is null)
                {
                    throw new InvalidOperationException("A named root requires a property name.");
                }
            }
            else if (_pendingName is not null)
            {
                throw new InvalidOperationException("An unnamed root cannot carry a property name.");
            }

            WriteByte((byte)tagType);
            if (_rootFormat == NbtRootFormat.Named)
            {
                WriteStringPayload(_pendingName!.AsSpan());
            }

            _pendingName = null;
            _rootTagType = tagType;
            _rootWritten = true;
            _valuesWritten++;
            IncrementValuesAtDepth(0);
            return;
        }

        if (Top.IsList)
        {
            if (_pendingName is not null)
            {
                throw new InvalidOperationException("List elements do not have names.");
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
            _valuesWritten++;
            IncrementValuesAtDepth(_containers.Count);
            return;
        }

        if (_pendingName is null)
        {
            throw new InvalidOperationException("A compound member name is required.");
        }

        WriteByte((byte)tagType);
        WriteStringPayload(_pendingName.AsSpan());
        _pendingName = null;
        _valuesWritten++;
        IncrementValuesAtDepth(_containers.Count);
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
            _rootComplete = true;
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

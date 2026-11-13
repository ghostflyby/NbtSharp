using System.Runtime.InteropServices;
using NbtSharp.IO;

namespace NbtSharp;

/// <summary>
/// Reads Java Edition NBT as a sequence of tokens.
/// </summary>
/// <remarks>
/// <para>
/// The reader is forward-only. Calling <see cref="Read"/> advances past one token. A compound or list
/// start token is followed by its members or elements and then by a matching
/// <see cref="NbtTokenType.EndCompound"/> or <see cref="NbtTokenType.EndList"/> token. For a value
/// token, <see cref="TagType"/> identifies the payload and the matching typed getter reads it.
/// Reading a value through a getter for a different type throws <see cref="NbtException"/>.
/// </para>
/// <para>
/// Nested containers are limited to <see cref="MaxDepth"/> levels. Exceeding that limit throws
/// <see cref="NbtException"/> rather than risking a stack overflow.
/// </para>
/// </remarks>
public sealed class NbtReader : IDisposable
{
    /// <summary>The maximum nesting depth the reader accepts.</summary>
    public const int MaxDepth = 512;

    /// <summary>The maximum number of elements the reader accepts in an array tag.</summary>
    public const int MaxArrayLength = 16 * 1024 * 1024;

    private readonly NbtBinaryReader _reader;
    private readonly Stream? _streamToDispose;
    private readonly List<ContainerState> _containers = [];
    private bool _rootRead;
    private bool _valuePending;
    private bool _containerPending;
    private bool _disposed;

    /// <summary>Initializes a reader over <paramref name="stream"/>.</summary>
    /// <param name="stream">The stream that supplies the NBT data.</param>
    /// <param name="rootFormat">Whether the root tag is preceded by a name.</param>
    /// <param name="disposeStream">Whether disposing the reader also disposes <paramref name="stream"/>.</param>
    public NbtReader(Stream stream, NbtRootFormat rootFormat = NbtRootFormat.Named, bool disposeStream = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        _reader = new NbtBinaryReader(stream);
        _streamToDispose = disposeStream ? stream : null;
        RootFormat = rootFormat;
    }

    /// <summary>Gets whether the root tag is preceded by a name.</summary>
    public NbtRootFormat RootFormat { get; }

    /// <summary>Gets the type of the most recently read token.</summary>
    public NbtTokenType TokenType { get; private set; }

    /// <summary>Gets the tag type of the most recently read token.</summary>
    public NbtTagType TagType { get; private set; }

    /// <summary>Gets the name that accompanies the most recently read tag, if any.</summary>
    public string? Name { get; private set; }

    /// <summary>Gets the element type declared by the most recently read list header.</summary>
    public NbtTagType ListElementType { get; private set; }

    /// <summary>Gets the element count declared by the most recently read list header.</summary>
    public int ListCount { get; private set; }

    /// <summary>Gets the current nesting depth.</summary>
    public int Depth => _containers.Count;

    private ref ContainerState Top => ref CollectionsMarshal.AsSpan(_containers)[^1];

    /// <summary>Advances the reader to the next token.</summary>
    /// <returns><see langword="true"/> if a token was read; <see langword="false"/> once the root tag is exhausted.</returns>
    /// <exception cref="NbtException">The data is malformed or a value was left unread.</exception>
    public bool Read()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_valuePending)
        {
            throw new NbtException("The current value has not been read. Call the matching getter or Skip first.");
        }

        _containerPending = false;

        if (!_rootRead)
        {
            return ReadRootStart();
        }

        if (_containers.Count == 0)
        {
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
            return true;
        }

        var tagType = ReadTagType();
        if (tagType == NbtTagType.End)
        {
            PopContainer();
            SetToken(NbtTokenType.EndCompound, NbtTagType.End, null);
            return true;
        }

        var name = _reader.ReadString();
        StartTag(tagType, name);
        return true;
    }

    /// <summary>Skips the value or container at the current position.</summary>
    /// <exception cref="NbtException">The reader is not positioned on a value or container start.</exception>
    public void Skip()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_valuePending)
        {
            SkipValue(TagType);
            _valuePending = false;
            return;
        }

        if (!_containerPending)
        {
            throw new NbtException("The reader is not positioned on a value or container.");
        }

        _containerPending = false;
        var targetDepth = _containers.Count - 1;
        while (_containers.Count > targetDepth)
        {
            if (Top.IsList)
            {
                if (Top.Remaining > 0)
                {
                    var elementType = Top.ElementType;
                    Top.Remaining--;
                    SkipListElement(elementType);
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
                _ = _reader.ReadString();
                SkipValue(tagType);
            }
        }
    }

    /// <summary>Reads the current value as a signed 8-bit integer.</summary>
    public sbyte GetSByte() => Expect(NbtTagType.Byte) ? _reader.ReadSByte() : default;

    /// <summary>Reads the current value as a signed 16-bit integer.</summary>
    public short GetInt16() => Expect(NbtTagType.Short) ? _reader.ReadInt16() : default;

    /// <summary>Reads the current value as a signed 32-bit integer.</summary>
    public int GetInt32() => Expect(NbtTagType.Int) ? _reader.ReadInt32() : default;

    /// <summary>Reads the current value as a signed 64-bit integer.</summary>
    public long GetInt64() => Expect(NbtTagType.Long) ? _reader.ReadInt64() : default;

    /// <summary>Reads the current value as a 32-bit IEEE 754 number.</summary>
    public float GetSingle() => Expect(NbtTagType.Float) ? _reader.ReadSingle() : default;

    /// <summary>Reads the current value as a 64-bit IEEE 754 number.</summary>
    public double GetDouble() => Expect(NbtTagType.Double) ? _reader.ReadDouble() : default;

    /// <summary>Reads the current value as a string.</summary>
    /// <remarks>
    /// Strings use Java modified UTF-8. Decoding follows <c>DataInputStream.readUTF</c>: lead bytes
    /// of 0xF0 or greater are rejected, while raw null bytes and overlong three-byte forms are
    /// accepted and normalized when the value is written again.
    /// </remarks>
    public string GetString() => Expect(NbtTagType.String) ? _reader.ReadString() : string.Empty;

    /// <summary>Reads the current value as a byte array.</summary>
    public sbyte[] GetByteArray()
    {
        if (!Expect(NbtTagType.ByteArray))
        {
            return [];
        }

        var bytes = _reader.ReadBytes(ReadArrayLength());
        return Array.ConvertAll(bytes, static b => (sbyte)b);
    }

    /// <summary>Reads the current value as an integer array.</summary>
    public int[] GetIntArray()
    {
        if (!Expect(NbtTagType.IntArray))
        {
            return [];
        }

        var values = new int[ReadArrayLength()];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = _reader.ReadInt32();
        }

        return values;
    }

    /// <summary>Reads the current value as a long array.</summary>
    public long[] GetLongArray()
    {
        if (!Expect(NbtTagType.LongArray))
        {
            return [];
        }

        var values = new long[ReadArrayLength()];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = _reader.ReadInt64();
        }

        return values;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _streamToDispose?.Dispose();
    }

    private bool ReadRootStart()
    {
        var tagType = ReadTagType();
        var name = RootFormat == NbtRootFormat.Named ? _reader.ReadString() : null;
        _rootRead = true;
        StartTag(tagType, name);
        return true;
    }

    private void StartTag(NbtTagType tagType, string? name)
    {
        switch (tagType)
        {
            case NbtTagType.End:
                throw new NbtException("The root tag cannot be TAG_End.");
            case NbtTagType.Compound:
                PushCompound();
                SetToken(NbtTokenType.StartCompound, tagType, name);
                _containerPending = true;
                break;
            case NbtTagType.List:
                SetToken(NbtTokenType.StartList, tagType, name);
                ReadListHeader();
                _containerPending = true;
                break;
            default:
                SetToken(NbtTokenType.Value, tagType, name);
                _valuePending = true;
                break;
        }
    }

    private void StartListElement(NbtTagType elementType)
    {
        switch (elementType)
        {
            case NbtTagType.Compound:
                PushCompound();
                SetToken(NbtTokenType.StartCompound, elementType, null);
                _containerPending = true;
                break;
            case NbtTagType.List:
                SetToken(NbtTokenType.StartList, elementType, null);
                ReadListHeader();
                _containerPending = true;
                break;
            default:
                SetToken(NbtTokenType.Value, elementType, null);
                _valuePending = true;
                break;
        }
    }

    private void ReadListHeader()
    {
        var elementType = ReadTagType();
        var count = _reader.ReadInt32();
        if (count < 0)
        {
            throw new NbtException($"Invalid NBT list length {count}.");
        }

        if (elementType == NbtTagType.End)
        {
            if (count != 0)
            {
                throw new NbtException("A list of TAG_End must have length zero.");
            }
        }

        ListElementType = elementType;
        ListCount = count;
        EnterContainer(isList: true, elementType, count);
    }

    private void PushCompound() => EnterContainer(isList: false, NbtTagType.End, remaining: 0);

    private void PopContainer() => _containers.RemoveAt(_containers.Count - 1);

    private void EnterContainer(bool isList, NbtTagType elementType, int remaining)
    {
        if (_containers.Count >= MaxDepth)
        {
            throw new NbtException($"The NBT nesting depth exceeds the limit of {MaxDepth}.");
        }

        _containers.Add(new ContainerState(isList, elementType, remaining));
    }

    private void SetToken(NbtTokenType tokenType, NbtTagType tagType, string? name)
    {
        TokenType = tokenType;
        TagType = tagType;
        Name = name;
    }

    private NbtTagType ReadTagType()
    {
        var value = _reader.ReadByte();
        if (value > (byte)NbtTagType.LongArray)
        {
            throw new NbtException($"Unknown NBT tag type {value}.");
        }

        return (NbtTagType)value;
    }

    private int ReadArrayLength()
    {
        var length = _reader.ReadInt32();
        if (length < 0 || length > MaxArrayLength)
        {
            throw new NbtException($"Invalid NBT array length {length}.");
        }

        return length;
    }

    private void SkipArray(int elementSize)
    {
        int length = ReadArrayLength();
        long byteCount = (long)length * elementSize;
        if (byteCount > int.MaxValue)
        {
            throw new NbtException($"The NBT array is too large to skip ({length} elements).");
        }

        _reader.Skip((int)byteCount);
    }

    private bool Expect(NbtTagType expected)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_valuePending || TagType != expected)
        {
            throw new NbtException($"Expected a {expected} value but the current token is {TokenType} ({TagType}).");
        }

        _valuePending = false;
        return true;
    }

    private void SkipListElement(NbtTagType elementType) => SkipValue(elementType);

    private void SkipValue(NbtTagType tagType)
    {
        switch (tagType)
        {
            case NbtTagType.Byte:
                _reader.Skip(1);
                break;
            case NbtTagType.Short:
                _reader.Skip(2);
                break;
            case NbtTagType.Int:
            case NbtTagType.Float:
                _reader.Skip(4);
                break;
            case NbtTagType.Long:
            case NbtTagType.Double:
                _reader.Skip(8);
                break;
            case NbtTagType.ByteArray:
                _reader.Skip(ReadArrayLength());
                break;
            case NbtTagType.String:
                _reader.Skip(_reader.ReadUInt16());
                break;
            case NbtTagType.List:
                ReadListHeader();
                break;
            case NbtTagType.Compound:
                PushCompound();
                break;
            case NbtTagType.IntArray:
                SkipArray(sizeof(int));
                break;
            case NbtTagType.LongArray:
                SkipArray(sizeof(long));
                break;
            default:
                throw new NbtException($"Cannot skip a value of type {tagType}.");
        }
    }

    private struct ContainerState(bool isList, NbtTagType elementType, int remaining)
    {
        public bool IsList { get; } = isList;

        public NbtTagType ElementType { get; } = elementType;

        public int Remaining { get; set; } = remaining;
    }
}

using NbtSharp.IO;

namespace NbtSharp;

/// <summary>
/// Writes Java Edition NBT as a sequence of tokens.
/// </summary>
/// <remarks>
/// <para>
/// Writing is forward-only. The root tag is written first, every opened container must be closed, and
/// the unnamed root format omits the name of the root tag only.
/// </para>
/// <para>
/// A list header records both its element type and its element count before any element is written,
/// so <see cref="WriteStartList"/> requires the count up front and the writer verifies that exactly
/// that many elements follow. Calling <see cref="Dispose"/> while containers are still open throws
/// <see cref="NbtException"/> after the underlying stream is handled.
/// </para>
/// </remarks>
public sealed class NbtWriter : IDisposable
{
    private readonly NbtBinaryWriter _writer;
    private readonly Stream _stream;
    private readonly bool _disposeStream;
    private readonly bool _unnamedRoot;
    private readonly List<ContainerState> _containers = [];
    private bool _rootWritten;
    private bool _disposed;

    /// <summary>Initializes a writer over <paramref name="stream"/>.</summary>
    /// <param name="stream">The stream that receives the NBT data.</param>
    /// <param name="rootFormat">Whether the root tag is preceded by a name.</param>
    /// <param name="disposeStream">Whether disposing the writer also disposes <paramref name="stream"/>.</param>
    public NbtWriter(Stream stream, NbtRootFormat rootFormat = NbtRootFormat.Named, bool disposeStream = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        _stream = stream;
        _writer = new NbtBinaryWriter(stream);
        _disposeStream = disposeStream;
        _unnamedRoot = rootFormat == NbtRootFormat.Unnamed;
    }

    /// <summary>Gets the current nesting depth.</summary>
    public int Depth => _containers.Count;

    /// <summary>Writes the start of a compound tag.</summary>
    /// <param name="name">The tag name.</param>
    public void WriteStartCompound(string name)
    {
        EnsureCanEnterContainer();
        WriteTagHeader(NbtTagType.Compound, name);
        EnterContainer(isList: false, NbtTagType.End, remaining: 0);
    }

    /// <summary>Writes the end of the current compound tag.</summary>
    public void WriteEndCompound()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_containers.Count == 0 || _containers[^1].IsList)
        {
            throw new NbtException("There is no open compound to close.");
        }

        _writer.WriteByte((byte)NbtTagType.End);
        PopContainer();
    }

    /// <summary>Writes a signed 8-bit integer tag.</summary>
    public void WriteByte(string name, sbyte value)
    {
        WriteTagHeader(NbtTagType.Byte, name);
        _writer.WriteSByte(value);
    }

    /// <summary>Writes a signed 16-bit integer tag.</summary>
    public void WriteInt16(string name, short value)
    {
        WriteTagHeader(NbtTagType.Short, name);
        _writer.WriteInt16(value);
    }

    /// <summary>Writes a signed 32-bit integer tag.</summary>
    public void WriteInt32(string name, int value)
    {
        WriteTagHeader(NbtTagType.Int, name);
        _writer.WriteInt32(value);
    }

    /// <summary>Writes a signed 64-bit integer tag.</summary>
    public void WriteInt64(string name, long value)
    {
        WriteTagHeader(NbtTagType.Long, name);
        _writer.WriteInt64(value);
    }

    /// <summary>Writes a 32-bit IEEE 754 tag.</summary>
    public void WriteSingle(string name, float value)
    {
        WriteTagHeader(NbtTagType.Float, name);
        _writer.WriteSingle(value);
    }

    /// <summary>Writes a 64-bit IEEE 754 tag.</summary>
    public void WriteDouble(string name, double value)
    {
        WriteTagHeader(NbtTagType.Double, name);
        _writer.WriteDouble(value);
    }

    /// <summary>Writes a string tag.</summary>
    public void WriteString(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ValidateStringLength(value);
        WriteTagHeader(NbtTagType.String, name);
        _writer.WriteString(value);
    }

    /// <summary>Writes a byte array tag.</summary>
    public void WriteByteArray(string name, sbyte[] value)
    {
        ArgumentNullException.ThrowIfNull(value);
        WriteTagHeader(NbtTagType.ByteArray, name);
        WriteByteArrayPayload(value);
    }

    /// <summary>Writes an integer array tag.</summary>
    public void WriteIntArray(string name, int[] value)
    {
        ArgumentNullException.ThrowIfNull(value);
        WriteTagHeader(NbtTagType.IntArray, name);
        WriteIntArrayPayload(value);
    }

    /// <summary>Writes a long array tag.</summary>
    public void WriteLongArray(string name, long[] value)
    {
        ArgumentNullException.ThrowIfNull(value);
        WriteTagHeader(NbtTagType.LongArray, name);
        WriteLongArrayPayload(value);
    }

    /// <summary>Writes the start of a list tag.</summary>
    /// <param name="name">The tag name.</param>
    /// <param name="elementType">The tag type shared by all elements.</param>
    /// <param name="count">The number of elements that will be written.</param>
    /// <exception cref="NbtException">The element type and count combination is not encodable.</exception>
    public void WriteStartList(string name, NbtTagType elementType, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ValidateListHeader(elementType, count);
        EnsureCanEnterContainer();
        WriteTagHeader(NbtTagType.List, name);
        WriteListHeader(elementType, count);
    }

    /// <summary>Writes the start of a list tag as an element of the enclosing list.</summary>
    /// <param name="elementType">The element type of the nested list.</param>
    /// <param name="count">The number of elements that will be written.</param>
    public void WriteStartListElement(NbtTagType elementType, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ValidateListHeader(elementType, count);
        EnsureCanEnterContainer();
        BeginListElement(NbtTagType.List);
        WriteListHeader(elementType, count);
    }

    /// <summary>Writes the end of the current list tag.</summary>
    /// <exception cref="NbtException">Elements promised by the list header are missing.</exception>
    public void WriteEndList()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_containers.Count == 0 || !_containers[^1].IsList)
        {
            throw new NbtException("There is no open list to close.");
        }

        if (_containers[^1].Remaining != 0)
        {
            throw new NbtException($"{_containers[^1].Remaining} list elements promised by the header were not written.");
        }

        PopContainer();
    }

    /// <summary>Writes a signed 8-bit integer element inside the current list.</summary>
    public void WriteElement(sbyte value)
    {
        BeginListElement(NbtTagType.Byte);
        _writer.WriteSByte(value);
    }

    /// <summary>Writes a signed 16-bit integer element inside the current list.</summary>
    public void WriteElement(short value)
    {
        BeginListElement(NbtTagType.Short);
        _writer.WriteInt16(value);
    }

    /// <summary>Writes a signed 32-bit integer element inside the current list.</summary>
    public void WriteElement(int value)
    {
        BeginListElement(NbtTagType.Int);
        _writer.WriteInt32(value);
    }

    /// <summary>Writes a signed 64-bit integer element inside the current list.</summary>
    public void WriteElement(long value)
    {
        BeginListElement(NbtTagType.Long);
        _writer.WriteInt64(value);
    }

    /// <summary>Writes a 32-bit IEEE 754 element inside the current list.</summary>
    public void WriteElement(float value)
    {
        BeginListElement(NbtTagType.Float);
        _writer.WriteSingle(value);
    }

    /// <summary>Writes a 64-bit IEEE 754 element inside the current list.</summary>
    public void WriteElement(double value)
    {
        BeginListElement(NbtTagType.Double);
        _writer.WriteDouble(value);
    }

    /// <summary>Writes a string element inside the current list.</summary>
    public void WriteElement(string value)
    {
        BeginListElement(NbtTagType.String);
        _writer.WriteString(value);
    }

    /// <summary>Writes a byte array element inside the current list.</summary>
    public void WriteElement(sbyte[] value)
    {
        ArgumentNullException.ThrowIfNull(value);
        BeginListElement(NbtTagType.ByteArray);
        WriteByteArrayPayload(value);
    }

    /// <summary>Writes an integer array element inside the current list.</summary>
    public void WriteElement(int[] value)
    {
        ArgumentNullException.ThrowIfNull(value);
        BeginListElement(NbtTagType.IntArray);
        WriteIntArrayPayload(value);
    }

    /// <summary>Writes a long array element inside the current list.</summary>
    public void WriteElement(long[] value)
    {
        ArgumentNullException.ThrowIfNull(value);
        BeginListElement(NbtTagType.LongArray);
        WriteLongArrayPayload(value);
    }

    /// <summary>Writes the start of a compound element inside the current list.</summary>
    public void WriteStartCompoundElement()
    {
        EnsureCanEnterContainer();
        BeginListElement(NbtTagType.Compound);
        EnterContainer(isList: false, NbtTagType.End, remaining: 0);
    }

    /// <summary>Flushes the underlying stream.</summary>
    public void Flush() => _stream.Flush();

    internal void Abort()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _containers.Clear();
        if (_disposeStream)
        {
            _stream.Dispose();
        }
    }

    /// <inheritdoc/>
    /// <exception cref="NbtException">Containers remain open.</exception>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var openContainers = _containers.Count;
        _containers.Clear();
        if (_disposeStream)
        {
            _stream.Dispose();
        }

        if (openContainers > 0)
        {
            throw new NbtException($"{openContainers} container(s) were left open when the writer was disposed.");
        }
    }

    private void WriteTagHeader(NbtTagType tagType, string name)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(name);
        ValidateStringLength(name);

        var isRoot = !_rootWritten;
        if (!isRoot)
        {
            if (_containers.Count == 0)
            {
                throw new NbtException("The root tag is already complete; no further tags can be written.");
            }

            if (_containers[^1].IsList)
            {
                throw new NbtException("List elements are written with the element writer methods, not with named tag methods.");
            }
        }

        _writer.WriteByte((byte)tagType);
        if (!(isRoot && _unnamedRoot))
        {
            _writer.WriteString(name);
        }

        _rootWritten = true;
    }

    private static void ValidateStringLength(string value)
    {
        var byteCount = JavaModifiedUtf8.GetByteCount(value);
        if (byteCount > JavaModifiedUtf8.MaxByteCount)
        {
            throw new NbtException($"The string is too long to encode: {byteCount} bytes.");
        }
    }

    private static void ValidateListHeader(NbtTagType elementType, int count)
    {
        var isKnownType = elementType is NbtTagType.End or <= NbtTagType.LongArray;
        if (!isKnownType)
        {
            throw new NbtException($"Unknown NBT list element type {(byte)elementType}.");
        }

        if (elementType == NbtTagType.End && count != 0)
        {
            throw new NbtException("A list of TAG_End must have length zero.");
        }
    }

    private void WriteListHeader(NbtTagType elementType, int count)
    {
        _writer.WriteByte((byte)elementType);
        _writer.WriteInt32(count);
        EnterContainer(isList: true, elementType, count);
    }

    private void WriteByteArrayPayload(sbyte[] value)
    {
        _writer.WriteInt32(value.Length);
        foreach (var item in value)
        {
            _writer.WriteSByte(item);
        }
    }

    private void WriteIntArrayPayload(int[] value)
    {
        _writer.WriteInt32(value.Length);
        foreach (var item in value)
        {
            _writer.WriteInt32(item);
        }
    }

    private void WriteLongArrayPayload(long[] value)
    {
        _writer.WriteInt32(value.Length);
        foreach (var item in value)
        {
            _writer.WriteInt64(item);
        }
    }

    private void BeginListElement(NbtTagType tagType)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_containers.Count == 0 || !_containers[^1].IsList)
        {
            throw new NbtException("There is no open list to write an element into.");
        }

        var top = _containers[^1];
        if (top.ElementType != tagType)
        {
            throw new NbtException($"The list declares elements of type {top.ElementType} but {tagType} was written.");
        }

        if (top.Remaining == 0)
        {
            throw new NbtException("The list header declared no more elements.");
        }

        top.Remaining--;
        _containers[^1] = top;
    }

    private void EnterContainer(bool isList, NbtTagType elementType, int remaining)
    {
        _containers.Add(new ContainerState(isList, elementType, remaining));
    }

    private void EnsureCanEnterContainer()
    {
        if (_containers.Count >= NbtReader.MaxDepth)
        {
            throw new NbtException($"The NBT nesting depth exceeds the limit of {NbtReader.MaxDepth}.");
        }
    }

    private void PopContainer() => _containers.RemoveAt(_containers.Count - 1);

    private struct ContainerState(bool isList, NbtTagType elementType, int remaining)
    {
        public bool IsList { get; } = isList;

        public NbtTagType ElementType { get; } = elementType;

        public int Remaining { get; set; } = remaining;
    }
}

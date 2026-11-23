using System.Buffers;
using System.Buffers.Binary;

namespace NbtSharp;

/// <summary>
/// A pooled, read-only, in-memory view of one NBT document.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Parse(ReadOnlyMemory{byte},NbtReaderOptions)"/> indexes the whole document in one
/// pass and validates its complete structure; every element of the resulting
/// <see cref="RootElement"/> is then readable without further parsing. This mirrors
/// <c>System.Text.Json.JsonDocument</c>: parse once, read anywhere, and call
/// <see cref="Dispose"/> to return the pooled buffers. A document whose total length exceeds
/// <see cref="int.MaxValue"/> bytes cannot be indexed and throws <see cref="NbtException"/>,
/// because element offsets are 32-bit.
/// </para>
/// <para>
/// A document is safe to read from multiple threads concurrently, but <see cref="Dispose"/> must
/// not race with reads: using an element after its document is disposed throws
/// <see cref="ObjectDisposedException"/>.
/// </para>
/// </remarks>
public sealed class NbtDocument : IDisposable
{
    private const int DefaultStreamBufferSize = 64 * 1024;
    private const int StateAlive = 1;
    private const int StateDisposed = 2;

    private ReadOnlyMemory<byte> _data;
    private byte[]? _leasedBuffer;
    private NbtDocumentSlot[]? _slots;
    private readonly int _rootSlot;
    private int _state;

    private NbtDocument(
        ReadOnlyMemory<byte> data,
        byte[]? leasedBuffer,
        NbtDocumentSlot[] slots)
    {
        _data = data;
        _leasedBuffer = leasedBuffer;
        _slots = slots;
        _rootSlot = 0;
        _state = StateAlive;
    }

    /// <summary>
    /// Indexes a complete NBT document from a contiguous memory block, without copying it.
    /// </summary>
    /// <param name="data">The complete NBT document.</param>
    /// <param name="options">The reader options, or the defaults.</param>
    /// <returns>A document over the supplied memory.</returns>
    /// <remarks>
    /// <para>
    /// The document reads directly from <paramref name="data"/>, so the caller must not modify
    /// the underlying buffer while the document is alive; doing so — including returning it to an
    /// array pool early — is undefined behavior. Byte arrays convert implicitly to
    /// <see cref="ReadOnlyMemory{T}"/>, so <c>NbtDocument.Parse(byteArray)</c> shares the array
    /// the same way.
    /// </para>
    /// <para>
    /// Trailing bytes after the root value are ignored. There is no
    /// <see cref="ReadOnlySpan{T}"/> overload; span callers wrap the span in a memory themselves,
    /// which keeps the zero-copy contract explicit.
    /// </para>
    /// </remarks>
    public static NbtDocument Parse(ReadOnlyMemory<byte> data, NbtReaderOptions options = default)
    {
        NbtDocumentParser.ValidateOptions(options);
        NbtDocumentSlot[]? slots = null;
        try
        {
            var slotArray = NbtDocumentParser.Parse(data.Span, options);
            slots = slotArray;
            var document = new NbtDocument(data, leasedBuffer: null, slots);
            slots = null;
            return document;
        }
        finally
        {
            if (slots is not null)
            {
                ArrayPool<NbtDocumentSlot>.Shared.Return(slots);
            }
        }
    }

    /// <summary>
    /// Indexes a complete NBT document from a possibly segmented sequence, copying it into a
    /// pooled buffer first.
    /// </summary>
    /// <param name="data">The complete NBT document.</param>
    /// <param name="options">The reader options, or the defaults.</param>
    /// <returns>A document over a pooled copy of the sequence.</returns>
    /// <remarks>
    /// <para>
    /// The copy length is bounded by <see cref="NbtReaderOptions.MaxDocumentBytes"/> when it is
    /// set, so a sequence may be arbitrarily longer than the limit. If the parse then runs past
    /// the copied bytes, the sequence was longer than the remaining allowance and the document
    /// exceeds the configured limit; a sequence that ends within the allowance is genuinely
    /// truncated. In either case the failure is reported at parse time. Trailing bytes after the
    /// root value do not count towards the limit.
    /// </para>
    /// </remarks>
    public static NbtDocument Parse(ReadOnlySequence<byte> data, NbtReaderOptions options = default)
    {
        NbtDocumentParser.ValidateOptions(options);
        var cap = options.MaxDocumentBytes;
        var copyLength = cap > 0 ? Math.Min(data.Length, cap) : data.Length;
        if (copyLength > int.MaxValue)
        {
            throw CreateMaxDocumentLengthException();
        }

        var buffer = ArrayPool<byte>.Shared.Rent((int)copyLength);
        var transferred = false;
        try
        {
            data.Slice(0, copyLength).CopyTo(buffer);
            var memory = buffer.AsMemory(0, (int)copyLength);
            NbtDocumentSlot[]? slots = null;
            try
            {
                var slotArray = NbtDocumentParser.Parse(memory.Span, options);
                slots = slotArray;
                var document = new NbtDocument(memory, buffer, slots);
                slots = null;
                transferred = true;
                return document;
            }
            catch (NbtException ex) when (ex.IsTruncated)
            {
                // The whole sequence was copied when it fits the allowance: the document really
                // is truncated. Otherwise the copy was clamped at the allowance: the document
                // exceeds the configured limit.
                if (cap > 0 && copyLength >= cap)
                {
                    throw new NbtException(
                        $"The NBT document exceeds the configured limit of {cap} bytes.");
                }

                throw;
            }
            finally
            {
                if (slots is not null)
                {
                    ArrayPool<NbtDocumentSlot>.Shared.Return(slots);
                }
            }
        }
        finally
        {
            if (!transferred)
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    /// <summary>
    /// Indexes a complete NBT document from the remaining bytes of a stream, copying them into a
    /// pooled buffer.
    /// </summary>
    /// <param name="stream">The stream that supplies the document. It is left open.</param>
    /// <param name="options">The reader options, or the defaults.</param>
    /// <returns>A document over a pooled copy of the stream's remaining bytes.</returns>
    /// <remarks>
    /// <para>
    /// Bytes after the root value are ignored, but may already have been read from the stream.
    /// How far the read runs ahead depends on the buffering path: when a positive
    /// <see cref="NbtReaderOptions.MaxDocumentBytes"/> is set and the stream reports a positive
    /// remaining length, at most that many bytes are rented and read up front; otherwise reads
    /// start at 64 KiB and double when a truncated parse proves more data is available. A stream
    /// that reports a wrong <see cref="Stream.Length"/> is handled by the actual fill amount, not
    /// the report.
    /// </para>
    /// </remarks>
    public static NbtDocument Parse(Stream stream, NbtReaderOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        NbtDocumentParser.ValidateOptions(options);
        var cap = options.MaxDocumentBytes;
        var capped = cap > 0;

        long declaredRemaining = -1;
        if (stream.CanSeek)
        {
            declaredRemaining = stream.Length - stream.Position;
        }

        var initialSize = capped && declaredRemaining > 0
            ? (int)Math.Min(declaredRemaining, Math.Min(cap, int.MaxValue))
            : DefaultStreamBufferSize;

        var buffer = ArrayPool<byte>.Shared.Rent(initialSize);
        var transferred = false;
        try
        {
            // The rented array may be longer than requested; fill and grow track the intended
            // target, so an exact-sized rent does not read past the stream's reported length.
            var target = buffer.Length >= initialSize ? initialSize : buffer.Length;
            long filled = 0;
            var completed = false;
            while (true)
            {
                while (filled < target)
                {
                    var read = stream.Read(buffer, (int)filled, target - (int)filled);
                    if (read == 0)
                    {
                        completed = true;
                        break;
                    }

                    filled += read;
                }

                NbtDocumentSlot[]? slots = null;
                try
                {
                    var memory = buffer.AsMemory(0, (int)filled);
                    var slotArray = NbtDocumentParser.Parse(memory.Span, options);
                    slots = slotArray;
                    var document = new NbtDocument(memory, buffer, slots);
                    slots = null;
                    transferred = true;
                    return document;
                }
                catch (NbtException ex) when (ex.IsTruncated && !completed)
                {
                    if (filled >= int.MaxValue)
                    {
                        throw CreateMaxDocumentLengthException();
                    }

                    if (capped && filled >= cap)
                    {
                        throw new NbtException(
                            $"The NBT document exceeds the configured limit of {cap} bytes.");
                    }

                    // Retry with a larger buffer, keeping the bytes already read.
                    var nextSize = (int)Math.Min(
                        (long)target * 2,
                        capped ? Math.Min(cap, int.MaxValue) : int.MaxValue);
                    if (nextSize <= target)
                    {
                        throw CreateMaxDocumentLengthException();
                    }

                    var grown = ArrayPool<byte>.Shared.Rent(nextSize);
                    buffer.AsSpan(0, (int)filled).CopyTo(grown);
                    ArrayPool<byte>.Shared.Return(buffer);
                    buffer = grown;
                    target = nextSize;
                    completed = false;
                }
                finally
                {
                    if (slots is not null)
                    {
                        ArrayPool<NbtDocumentSlot>.Shared.Return(slots);
                    }
                }
            }
        }
        finally
        {
            if (!transferred)
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    /// <summary>Gets the root value of the document.</summary>
    /// <exception cref="ObjectDisposedException">The document has been disposed.</exception>
    public NbtElement RootElement
    {
        get
        {
            ThrowIfDisposed();
            return Translate(_rootSlot);
        }
    }

    /// <summary>
    /// Gets the root tag name: <see langword="null"/> for an unnamed root, and
    /// <see cref="string.Empty"/> for a named root with a zero-length name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The name is decoded on the first access and cached. The publication is a benign race: the
    /// first concurrent accesses may decode the same value more than once, and every reader still
    /// observes an equivalent string.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The document has been disposed.</exception>
    /// <exception cref="NbtException">The root name is invalid modified UTF-8.</exception>
    public string? RootName
    {
        get
        {
            ThrowIfDisposed();
            ref readonly var root = ref GetSlot(_rootSlot);
            if ((root.Flags & NbtDocumentSlotFlags.HasName) == 0)
            {
                return null;
            }

            return field ??= NbtText.DecodeAt(
                _data.Span.Slice(root.NameOffset, root.NameLength),
                // Report decoding errors at the name's length prefix, like NbtReader.
                root.NameOffset - sizeof(ushort));
        }
    }

    /// <summary>
    /// Returns the pooled buffers. Bytes that a memory-based document borrows from its caller are
    /// not the document's to return.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The method is idempotent. A document is safe to read concurrently, but disposing must not
    /// race with reads.
    /// </para>
    /// </remarks>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _state, StateDisposed) == StateDisposed)
        {
            return;
        }
        var slots = _slots;
        _slots = null;
        if (slots is not null)
        {
            ArrayPool<NbtDocumentSlot>.Shared.Return(slots);
        }

        var leased = _leasedBuffer;
        _leasedBuffer = null;
        _data = default;
        if (leased is not null)
        {
            ArrayPool<byte>.Shared.Return(leased);
        }
    }

    internal ReadOnlySpan<byte> DataSpan => _data.Span;

    internal ref readonly NbtDocumentSlot GetSlot(int index)
    {
        ThrowIfDisposed();
        return ref _slots![index];
    }

    internal NbtElement CreateElement(int slotIndex) => Translate(slotIndex);

    /// <summary>Decodes the recorded wire name of a slot.</summary>
    /// <param name="slotIndex">The slot index.</param>
    /// <returns>The decoded name.</returns>
    internal string NameAt(int slotIndex)
    {
        ref readonly var slot = ref GetSlot(slotIndex);
        return NbtText.DecodeAt(
            _data.Span.Slice(slot.NameOffset, slot.NameLength),
            slot.NameOffset - sizeof(ushort));
    }

    internal void ThrowIfDisposed()
    {
        if (_state == StateAlive) return;
        throw new ObjectDisposedException(nameof(NbtDocument));
    }

    private NbtElement Translate(int slotIndex)
    {
        ref readonly var slot = ref GetSlot(slotIndex);
        return slot.TagType switch
        {
            NbtTagType.Byte => (sbyte)_data.Span[slot.PayloadOffset],
            NbtTagType.Short => BinaryPrimitives.ReadInt16BigEndian(
                _data.Span.Slice(slot.PayloadOffset, sizeof(short))),
            NbtTagType.Int => BinaryPrimitives.ReadInt32BigEndian(
                _data.Span.Slice(slot.PayloadOffset, sizeof(int))),
            NbtTagType.Long => BinaryPrimitives.ReadInt64BigEndian(
                _data.Span.Slice(slot.PayloadOffset, sizeof(long))),
            NbtTagType.Float => BinaryPrimitives.ReadSingleBigEndian(
                _data.Span.Slice(slot.PayloadOffset, sizeof(float))),
            NbtTagType.Double => BinaryPrimitives.ReadDoubleBigEndian(
                _data.Span.Slice(slot.PayloadOffset, sizeof(double))),
            NbtTagType.String => new NbtElement(
                new NbtStringElement(StringPayload(in slot), slot.PayloadOffset, this)),
            NbtTagType.ByteArray => new NbtElement(new NbtByteArrayElement(ArrayPayload(in slot), this)),
            NbtTagType.IntArray => new NbtElement(new NbtIntArrayElement(ArrayPayload(in slot), this)),
            NbtTagType.LongArray => new NbtElement(new NbtLongArrayElement(ArrayPayload(in slot), this)),
            NbtTagType.List => new NbtElement(new NbtListElement(this, slotIndex)),
            NbtTagType.Compound => new NbtElement(new NbtCompoundElement(this, slotIndex)),
            _ => throw new NbtException("Unexpected TAG_End value."),
        };
    }

    private ReadOnlyMemory<byte> StringPayload(ref readonly NbtDocumentSlot slot) =>
        _data.Slice(slot.PayloadOffset + sizeof(ushort), slot.PayloadLength - sizeof(ushort));

    private ReadOnlyMemory<byte> ArrayPayload(ref readonly NbtDocumentSlot slot) =>
        _data.Slice(slot.PayloadOffset + sizeof(int), slot.PayloadLength - sizeof(int));

    private static NbtException CreateMaxDocumentLengthException() =>
        new(
            $"The NBT document exceeds the maximum length of {int.MaxValue} bytes supported by {nameof(NbtDocument)}.");
}

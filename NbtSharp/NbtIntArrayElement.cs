using System.Buffers.Binary;

namespace NbtSharp;

/// <summary>
/// A <c>TAG_Int_Array</c> value backed by its raw big-endian bytes.
/// </summary>
/// <remarks>
/// <para>
/// The value is a view: <see cref="RawSpan"/> and <see cref="RawMemory"/> expose the wire bytes
/// without copying. The wire stores the elements big-endian, so there is no zero-copy
/// <see cref="ReadOnlySpan{T}"/> of <see cref="int"/>; <see cref="CopyTo"/> and <see cref="ToArray"/>
/// byte-swap into host order as they write.
/// </para>
/// <para>
/// A view created by a document stays valid only while that document is alive and undisposed.
/// </para>
/// </remarks>
public readonly struct NbtIntArrayElement
{
    private readonly ReadOnlyMemory<byte> _raw;
    private readonly NbtDocument? _owner;

    /// <summary>Initializes an int-array view over raw big-endian bytes.</summary>
    /// <param name="raw">The payload bytes; the length must be a multiple of four.</param>
    public NbtIntArrayElement(ReadOnlyMemory<byte> raw)
        : this(raw, owner: null)
    {
    }

    internal NbtIntArrayElement(ReadOnlyMemory<byte> raw, NbtDocument? owner)
    {
        if (raw.Length % sizeof(int) != 0)
        {
            throw new ArgumentException(
                "An int array requires a payload length that is a multiple of four.",
                nameof(raw));
        }

        _raw = raw;
        _owner = owner;
    }

    /// <summary>Gets the number of elements.</summary>
    public int Length
    {
        get
        {
            ThrowIfOwnerDisposed();
            return _raw.Length / sizeof(int);
        }
    }

    /// <summary>Gets the raw big-endian payload bytes without copying.</summary>
    public ReadOnlySpan<byte> RawSpan
    {
        get
        {
            ThrowIfOwnerDisposed();
            return _raw.Span;
        }
    }

    /// <summary>Gets the raw big-endian payload bytes without copying.</summary>
    public ReadOnlyMemory<byte> RawMemory
    {
        get
        {
            ThrowIfOwnerDisposed();
            return _raw;
        }
    }

    /// <summary>Copies the elements into <paramref name="destination"/> in host byte order.</summary>
    /// <param name="destination">The destination, at least <see cref="Length"/> long.</param>
    public void CopyTo(Span<int> destination)
    {
        ThrowIfOwnerDisposed();
        if (destination.Length < Length)
        {
            throw new ArgumentException("The destination is too short.", nameof(destination));
        }

        var source = _raw.Span;
        for (var i = 0; i < Length; i++)
        {
            destination[i] = BinaryPrimitives.ReadInt32BigEndian(source.Slice(i * sizeof(int), sizeof(int)));
        }
    }

    /// <summary>Materializes the elements as a new array in host byte order.</summary>
    /// <returns>A new array holding the elements.</returns>
    public int[] ToArray()
    {
        ThrowIfOwnerDisposed();
        var values = new int[Length];
        CopyTo(values);
        return values;
    }

    /// <summary>Gets the owning document, when the view was created by one.</summary>
    internal NbtDocument? Owner => _owner;

    private void ThrowIfOwnerDisposed() => _owner?.ThrowIfDisposed();
}

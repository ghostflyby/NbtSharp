using System.Runtime.InteropServices;

namespace NbtSharp;

/// <summary>
/// A <c>TAG_Byte_Array</c> value backed by its raw bytes.
/// </summary>
/// <remarks>
/// The value is a view: the buffer is never copied unless <see cref="ToArray"/> or
/// <see cref="ToSByteArray"/> is called. A view created by a document stays valid only while that
/// document is alive and undisposed.
/// </remarks>
public readonly struct NbtByteArrayElement
{
    private readonly ReadOnlyMemory<byte> _raw;
    private readonly NbtDocument? _owner;

    /// <summary>Initializes a byte-array view over raw bytes.</summary>
    /// <param name="raw">The array payload bytes.</param>
    public NbtByteArrayElement(ReadOnlyMemory<byte> raw)
        : this(raw, owner: null)
    {
    }

    internal NbtByteArrayElement(ReadOnlyMemory<byte> raw, NbtDocument? owner)
    {
        _raw = raw;
        _owner = owner;
    }

    /// <summary>Gets the number of elements.</summary>
    public int Length
    {
        get
        {
            ThrowIfOwnerDisposed();
            return _raw.Length;
        }
    }

    /// <summary>Gets the payload bytes without copying.</summary>
    public ReadOnlySpan<byte> Span
    {
        get
        {
            ThrowIfOwnerDisposed();
            return _raw.Span;
        }
    }

    /// <summary>Gets the payload bytes without copying.</summary>
    public ReadOnlyMemory<byte> Memory
    {
        get
        {
            ThrowIfOwnerDisposed();
            return _raw;
        }
    }

    /// <summary>Gets the payload interpreted as signed bytes without copying.</summary>
    public ReadOnlySpan<sbyte> SByteSpan
    {
        get
        {
            ThrowIfOwnerDisposed();
            return MemoryMarshal.Cast<byte, sbyte>(_raw.Span);
        }
    }

    /// <summary>Copies the payload into <paramref name="destination"/>.</summary>
    /// <param name="destination">The destination, at least <see cref="Length"/> long.</param>
    public void CopyTo(Span<byte> destination)
    {
        ThrowIfOwnerDisposed();
        _raw.Span.CopyTo(destination);
    }

    /// <summary>Copies the payload into <paramref name="destination"/> as signed bytes.</summary>
    /// <param name="destination">The destination, at least <see cref="Length"/> long.</param>
    public void CopyTo(Span<sbyte> destination)
    {
        ThrowIfOwnerDisposed();
        SByteSpan.CopyTo(destination);
    }

    /// <summary>Materializes the payload as a new byte array.</summary>
    /// <returns>A new array holding the payload.</returns>
    public byte[] ToArray()
    {
        ThrowIfOwnerDisposed();
        return _raw.ToArray();
    }

    /// <summary>Materializes the payload as a new signed-byte array.</summary>
    /// <returns>A new array holding the payload.</returns>
    public sbyte[] ToSByteArray()
    {
        ThrowIfOwnerDisposed();
        return SByteSpan.ToArray();
    }

    /// <summary>Gets the owning document, when the view was created by one.</summary>
    internal NbtDocument? Owner => _owner;

    private void ThrowIfOwnerDisposed() => _owner?.ThrowIfDisposed();
}

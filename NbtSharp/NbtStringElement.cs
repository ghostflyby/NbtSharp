namespace NbtSharp;

/// <summary>
/// A <c>TAG_String</c> value backed by its raw modified UTF-8 bytes.
/// </summary>
/// <remarks>
/// <para>
/// The value is a view: <see cref="RawSpan"/> and <see cref="RawMemory"/> expose the encoded bytes
/// without decoding, and <see cref="GetString"/> decodes on demand. A view created by a document
/// stays valid only while that document is alive and undisposed.
/// </para>
/// </remarks>
public readonly struct NbtStringElement
{
    private readonly ReadOnlyMemory<byte> _raw;

    /// <summary>Initializes a string view over modified UTF-8 bytes.</summary>
    /// <param name="raw">The modified UTF-8 bytes.</param>
    /// <param name="bytePosition">The byte position reported when decoding fails.</param>
    public NbtStringElement(ReadOnlyMemory<byte> raw, long bytePosition = 0)
        : this(raw, bytePosition, owner: null)
    {
    }

    internal NbtStringElement(ReadOnlyMemory<byte> raw, long bytePosition, NbtDocument? owner)
    {
        _raw = raw;
        BytePosition = bytePosition;
        Owner = owner;
    }

    /// <summary>Gets the number of encoded bytes.</summary>
    public int ByteLength
    {
        get
        {
            ThrowIfOwnerDisposed();
            return _raw.Length;
        }
    }

    /// <summary>Gets a value indicating whether the string is empty.</summary>
    public bool IsEmpty
    {
        get
        {
            ThrowIfOwnerDisposed();
            return _raw.IsEmpty;
        }
    }

    /// <summary>Gets the encoded modified UTF-8 bytes without decoding them.</summary>
    public ReadOnlySpan<byte> RawSpan
    {
        get
        {
            ThrowIfOwnerDisposed();
            return _raw.Span;
        }
    }

    /// <summary>Gets the encoded modified UTF-8 bytes without decoding them.</summary>
    public ReadOnlyMemory<byte> RawMemory
    {
        get
        {
            ThrowIfOwnerDisposed();
            return _raw;
        }
    }

    /// <summary>Decodes the value as a string.</summary>
    /// <returns>The decoded string.</returns>
    /// <exception cref="ObjectDisposedException">The owning document has been disposed.</exception>
    /// <exception cref="NbtException">The bytes are not valid modified UTF-8.</exception>
    public string GetString()
    {
        ThrowIfOwnerDisposed();
        return NbtText.DecodeAt(_raw.Span, BytePosition);
    }

    /// <summary>Gets the byte position reported when decoding fails.</summary>
    internal long BytePosition { get; }

    /// <summary>Gets the owning document, when the view was created by one.</summary>
    internal NbtDocument? Owner { get; }

    private void ThrowIfOwnerDisposed() => Owner?.ThrowIfDisposed();
}

using System.Text;
using Myitian.Text;

namespace NbtSharp;

/// <summary>
/// The modified UTF-8 codec used by NbtSharp: a wrapper over Myitian's
/// <see cref="ModifiedUTF8Encoding"/> that adds the Java <c>readUTF</c> policy.
/// </summary>
/// <remarks>
/// <para>
/// Myitian's decoder is a lenient superset: it accepts standard UTF-8 four-byte sequences and
/// decodes them to surrogate pairs, accepts overlong forms and raw nulls, and reports invalid
/// input with exceptions that carry no document position. This codec adds the rule NBT requires:
/// lead bytes of 0xF0 or greater are rejected, and decoding failures surface as
/// <see cref="NbtException"/>.
/// </para>
/// <para>
/// The policy lives in the standard <see cref="Encoding"/> entry points, so callers use
/// <c>GetString</c>, <c>GetCharCount</c>, and <c>GetChars</c>; there is no bespoke decode method.
/// <c>GetDecoder</c> and <c>GetEncoder</c> expose the underlying codec's stateful objects and do
/// not apply the F0 rule.
/// </para>
/// </remarks>
public sealed class NbtModifiedUtf8Encoding : Encoding
{
    /// <summary>The shared codec instance.</summary>
    public static readonly NbtModifiedUtf8Encoding Instance = new();

    private readonly ModifiedUTF8Encoding _inner = new(throwOnInvalidBytes: true);

    private NbtModifiedUtf8Encoding()
    {
    }

    /// <inheritdoc/>
    public override string GetString(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        Validate(bytes);
        return DecodeCore(bytes);
    }

    /// <inheritdoc/>
    public override string GetString(byte[] bytes, int index, int count)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        Validate(bytes.AsSpan(index, count), index);
        return DecodeCore(bytes.AsSpan(index, count));
    }

    /// <inheritdoc/>
    public override int GetCharCount(byte[] bytes, int index, int count)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        Validate(bytes.AsSpan(index, count), index);
        try
        {
            return _inner.GetCharCount(bytes, index, count);
        }
        catch (DecoderFallbackException ex)
        {
            throw new NbtException("Invalid modified UTF-8 string.", ex);
        }
    }

    /// <inheritdoc/>
    public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        Validate(bytes.AsSpan(byteIndex, byteCount), byteIndex);
        try
        {
            return _inner.GetChars(bytes, byteIndex, byteCount, chars, charIndex);
        }
        catch (DecoderFallbackException ex)
        {
            throw new NbtException("Invalid modified UTF-8 string.", ex);
        }
    }

    /// <inheritdoc/>
    public override int GetCharCount(ReadOnlySpan<byte> bytes)
    {
        Validate(bytes);
        try
        {
            return _inner.GetCharCount(bytes);
        }
        catch (DecoderFallbackException ex)
        {
            throw new NbtException("Invalid modified UTF-8 string.", ex);
        }
    }

    /// <inheritdoc/>
    public override int GetChars(ReadOnlySpan<byte> bytes, Span<char> chars)
    {
        Validate(bytes);
        try
        {
            return _inner.GetChars(bytes, chars);
        }
        catch (DecoderFallbackException ex)
        {
            throw new NbtException("Invalid modified UTF-8 string.", ex);
        }
    }

    /// <summary>Gets the byte count of the encoded form.</summary>
    /// <param name="chars">The characters.</param>
    /// <returns>The encoded byte count.</returns>
    public override int GetByteCount(ReadOnlySpan<char> chars) => _inner.GetByteCount(chars);

    /// <summary>Encodes characters into <paramref name="bytes"/>.</summary>
    /// <param name="chars">The characters.</param>
    /// <param name="bytes">The destination.</param>
    /// <returns>The number of bytes written.</returns>
    public override int GetBytes(ReadOnlySpan<char> chars, Span<byte> bytes) => _inner.GetBytes(chars, bytes);

    /// <inheritdoc/>
    public override int GetByteCount(char[] chars, int index, int count) =>
        _inner.GetByteCount(chars, index, count);

    /// <inheritdoc/>
    public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex) =>
        _inner.GetBytes(chars, charIndex, charCount, bytes, byteIndex);

    /// <inheritdoc/>
    public override Encoder GetEncoder() => _inner.GetEncoder();

    /// <inheritdoc/>
    public override Decoder GetDecoder() => _inner.GetDecoder();

    /// <inheritdoc/>
    public override int GetMaxByteCount(int charCount) => _inner.GetMaxByteCount(charCount);

    /// <inheritdoc/>
    public override int GetMaxCharCount(int byteCount) => _inner.GetMaxCharCount(byteCount);

    /// <summary>
    /// Rejects modified UTF-8 bytes that Java's <c>readUTF</c> does not accept: any byte of 0xF0
    /// or greater (a standard UTF-8 four-byte lead) is invalid in modified UTF-8.
    /// </summary>
    /// <param name="bytes">The modified UTF-8 bytes.</param>
    /// <param name="offset">The position of <paramref name="bytes"/>'s first byte in the
    /// enclosing document, added to the offending byte's index when the exception is built.</param>
    /// <exception cref="NbtException">
    /// A byte is 0xF0 or greater; <see cref="NbtException.BytePosition"/> is
    /// <paramref name="offset"/> plus the offending byte's index.
    /// </exception>
    public static void Validate(ReadOnlySpan<byte> bytes, int offset = 0)
    {
        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] >= 0xF0)
            {
                throw new NbtException(
                    "Invalid modified UTF-8 string: lead bytes of 0xF0 or greater are not allowed.")
                {
                    BytePosition = offset + i,
                };
            }
        }
    }

    private string DecodeCore(ReadOnlySpan<byte> bytes)
    {
        try
        {
            return _inner.GetString(bytes);
        }
        catch (DecoderFallbackException ex)
        {
            throw new NbtException("Invalid modified UTF-8 string.", ex);
        }
    }
}

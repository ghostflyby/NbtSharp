using System.Buffers;

namespace NbtSharp;

/// <summary>
/// NBT string and name semantics layered on <see cref="NbtModifiedUtf8Encoding"/>: document
/// positions for decode failures, early name validation, and name comparison.
/// </summary>
internal static class NbtText
{
    private const int StackCharBufferLength = 256;

    /// <summary>
    /// Decodes modified UTF-8 bytes and reports a decode failure at
    /// <paramref name="position"/>.
    /// </summary>
    /// <param name="bytes">The modified UTF-8 bytes.</param>
    /// <param name="position">The byte position reported when decoding fails.</param>
    /// <returns>The decoded string.</returns>
    public static string DecodeAt(ReadOnlySpan<byte> bytes, long position)
    {
        var charCount = GetCharCount(bytes, position);
        if (charCount == 0)
        {
            return string.Empty;
        }

        Span<char> chars = charCount <= StackCharBufferLength
            ? stackalloc char[charCount]
            : new char[charCount];
        GetChars(bytes, chars, position);
        return new string(chars);
    }

    /// <summary>
    /// Rejects name bytes the codec does not accept: lead bytes of 0xF0 or greater. Deeper
    /// structural validation is deferred to the first decode.
    /// </summary>
    /// <param name="bytes">The name bytes.</param>
    /// <param name="position">The byte position reported when the input is invalid.</param>
    public static void ValidateName(ReadOnlySpan<byte> bytes, long position)
    {
        try
        {
            NbtModifiedUtf8Encoding.Validate(bytes);
        }
        catch (NbtException ex)
        {
            throw new NbtException(ex.Message, ex)
            {
                BytePosition = position,
            };
        }
    }

    /// <summary>
    /// Compares two encoded names by decoded value. Both names decode into stack buffers when the
    /// decoded length is at most 256 chars, or into pooled buffers otherwise; no strings are
    /// allocated.
    /// </summary>
    /// <param name="recorded">The recorded wire name bytes.</param>
    /// <param name="encoded">The query name bytes.</param>
    /// <param name="position">The byte position reported when decoding fails.</param>
    /// <returns><see langword="true"/> when the decoded names are equal.</returns>
    public static bool NameEquals(
        ReadOnlySpan<byte> recorded,
        ReadOnlySpan<byte> encoded,
        long position)
    {
        var charCount = GetCharCount(recorded, position);
        if (charCount != GetCharCount(encoded, position))
        {
            return false;
        }

        char[]? recordedRented = null;
        char[]? encodedRented = null;
        Span<char> recordedChars = charCount <= StackCharBufferLength
            ? stackalloc char[charCount]
            : (recordedRented = ArrayPool<char>.Shared.Rent(charCount)).AsSpan(0, charCount);
        Span<char> encodedChars = charCount <= StackCharBufferLength
            ? stackalloc char[charCount]
            : (encodedRented = ArrayPool<char>.Shared.Rent(charCount)).AsSpan(0, charCount);

        try
        {
            GetChars(recorded, recordedChars, position);
            GetChars(encoded, encodedChars, position);
            return recordedChars.SequenceEqual(encodedChars);
        }
        finally
        {
            if (recordedRented is not null)
            {
                ArrayPool<char>.Shared.Return(recordedRented);
            }

            if (encodedRented is not null)
            {
                ArrayPool<char>.Shared.Return(encodedRented);
            }
        }
    }

    /// <summary>
    /// Compares an encoded name with already-decoded query chars by ordinal value. The wire name
    /// decodes into a stack buffer when it is at most 256 chars, or a pooled buffer otherwise; no
    /// strings are allocated.
    /// </summary>
    /// <param name="recorded">The recorded wire name bytes.</param>
    /// <param name="query">The decoded query name.</param>
    /// <param name="position">The byte position reported when decoding fails.</param>
    /// <returns><see langword="true"/> when the decoded name equals <paramref name="query"/>.</returns>
    public static bool NameEquals(
        ReadOnlySpan<byte> recorded,
        ReadOnlySpan<char> query,
        long position)
    {
        var charCount = GetCharCount(recorded, position);
        if (charCount != query.Length)
        {
            return false;
        }

        char[]? rented = null;
        Span<char> chars = charCount <= StackCharBufferLength
            ? stackalloc char[charCount]
            : (rented = ArrayPool<char>.Shared.Rent(charCount)).AsSpan(0, charCount);

        try
        {
            GetChars(recorded, chars, position);
            return chars.SequenceEqual(query);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<char>.Shared.Return(rented);
            }
        }
    }

    private static int GetCharCount(ReadOnlySpan<byte> bytes, long position)
    {
        try
        {
            return NbtModifiedUtf8Encoding.Instance.GetCharCount(bytes);
        }
        catch (NbtException ex)
        {
            throw new NbtException(ex.Message, ex)
            {
                BytePosition = position,
            };
        }
    }

    private static void GetChars(ReadOnlySpan<byte> bytes, Span<char> chars, long position)
    {
        try
        {
            NbtModifiedUtf8Encoding.Instance.GetChars(bytes, chars);
        }
        catch (NbtException ex)
        {
            throw new NbtException(ex.Message, ex)
            {
                BytePosition = position,
            };
        }
    }
}

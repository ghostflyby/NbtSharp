using System.Text;
using Myitian.Text;

namespace NbtSharp;

/// <summary>
/// Shared modified UTF-8 decoding and name scanning for <see cref="NbtReader"/> and the read-only
/// DOM. The messages and byte positions are identical on both surfaces.
/// </summary>
internal static class ModifiedUtf8
{
    private static readonly ModifiedUTF8Encoding StringEncoding = new(throwOnInvalidBytes: true);

    /// <summary>
    /// Decodes modified UTF-8 bytes, rejecting lead bytes of 0xF0 or greater before decoding and
    /// reporting any other invalid sequence as <see cref="NbtException"/>.
    /// </summary>
    /// <param name="bytes">The modified UTF-8 bytes.</param>
    /// <param name="position">The byte position reported when the input is invalid.</param>
    /// <returns>The decoded string.</returns>
    public static string Decode(ReadOnlySpan<byte> bytes, long position)
    {
        foreach (var value in bytes)
        {
            if (value >= 0xF0)
            {
                throw new NbtException(
                    "Invalid modified UTF-8 string: lead bytes of 0xF0 or greater are not allowed.")
                {
                    BytePosition = position,
                };
            }
        }

        try
        {
            return StringEncoding.GetString(bytes);
        }
        catch (DecoderFallbackException ex)
        {
            throw new NbtException("Invalid modified UTF-8 string.", ex)
            {
                BytePosition = position,
            };
        }
    }

    /// <summary>
    /// Pre-scans name bytes: rejects lead bytes of 0xF0 or greater, and reports whether the
    /// encoding is non-canonical (a raw 0x00 byte, an overlong two-byte form, or an overlong
    /// three-byte form), which makes byte-wise name comparison fall back to a decoded string
    /// comparison. Deeper validation is deferred to the first decode.
    /// </summary>
    /// <param name="bytes">The name bytes.</param>
    /// <param name="position">The byte position reported when the input is invalid.</param>
    /// <param name="nonCanonical">
    /// <see langword="true"/> when the bytes use a non-canonical encoding.
    /// </param>
    public static void Scan(ReadOnlySpan<byte> bytes, long position, out bool nonCanonical)
    {
        nonCanonical = false;
        for (var i = 0; i < bytes.Length; i++)
        {
            var value = bytes[i];
            if (value >= 0xF0)
            {
                throw new NbtException(
                    "Invalid modified UTF-8 string: lead bytes of 0xF0 or greater are not allowed.")
                {
                    BytePosition = position,
                };
            }

            if (value >= 0x80)
            {
                if (value <= 0xBF)
                {
                    continue; // continuation byte of a multi-byte sequence
                }

                // Multi-byte lead byte.
                if (value is 0xC0 or 0xC1 ||
                    (value == 0xE0 &&
                     i + 1 < bytes.Length &&
                     bytes[i + 1] is >= 0x80 and <= 0x9F))
                {
                    nonCanonical = true; // overlong two- or three-byte form
                }
            }
            else if (value == 0x00)
            {
                nonCanonical = true; // raw null instead of the two-byte form
            }
        }
    }
}

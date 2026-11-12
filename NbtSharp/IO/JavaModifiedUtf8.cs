namespace NbtSharp.IO;

/// <summary>
/// Encodes and decodes Java's modified UTF-8, the string encoding used by Java Edition NBT.
/// </summary>
/// <remarks>
/// Modified UTF-8 differs from standard UTF-8 in two ways: U+0000 is encoded as the two-byte
/// sequence <c>C0 80</c> instead of a raw zero byte, and characters outside the Basic Multilingual
/// Plane are written as two separate three-byte sequences, one for each surrogate code unit,
/// instead of a single four-byte sequence.
/// </remarks>
internal static class JavaModifiedUtf8
{
    /// <summary>The largest byte count a length-prefixed string can represent.</summary>
    public const int MaxByteCount = ushort.MaxValue;

    /// <summary>Calculates the number of bytes needed to encode <paramref name="value"/>.</summary>
    /// <param name="value">The string to measure.</param>
    /// <returns>The encoded byte count.</returns>
    public static int GetByteCount(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var count = 0;
        foreach (var c in value)
        {
            if (c is >= '\u0001' and <= '\u007F')
            {
                count += 1;
            }
            else if (c <= '\u07FF')
            {
                // Includes U+0000, which is encoded as C0 80.
                count += 2;
            }
            else
            {
                // Includes each half of a surrogate pair.
                count += 3;
            }
        }

        return count;
    }

    /// <summary>Writes the modified UTF-8 encoding of <paramref name="value"/>.</summary>
    /// <param name="value">The string to encode.</param>
    /// <param name="destination">A buffer of at least <see cref="GetByteCount"/> bytes.</param>
    /// <returns>The number of bytes written.</returns>
    public static int Encode(string value, Span<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(value);

        var index = 0;
        foreach (var c in value)
        {
            if (c is >= '\u0001' and <= '\u007F')
            {
                destination[index++] = (byte)c;
            }
            else if (c <= '\u07FF')
            {
                destination[index++] = (byte)(0xC0 | (c >> 6));
                destination[index++] = (byte)(0x80 | (c & 0x3F));
            }
            else
            {
                destination[index++] = (byte)(0xE0 | (c >> 12));
                destination[index++] = (byte)(0x80 | ((c >> 6) & 0x3F));
                destination[index++] = (byte)(0x80 | (c & 0x3F));
            }
        }

        return index;
    }

    /// <summary>Decodes modified UTF-8 bytes into a string.</summary>
    /// <param name="bytes">The encoded bytes, excluding any length prefix.</param>
    /// <returns>The decoded string.</returns>
    /// <exception cref="NbtException">The bytes are not a valid modified UTF-8 sequence.</exception>
    public static string Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(bytes.Length);
        var index = 0;
        while (index < bytes.Length)
        {
            var first = bytes[index++];
            if (first < 0x80)
            {
                if (first == 0)
                {
                    throw new NbtException("Invalid modified UTF-8 string: a raw null byte is not allowed.");
                }

                builder.Append((char)first);
            }
            else if ((first & 0xE0) == 0xC0)
            {
                if (index >= bytes.Length)
                {
                    throw Truncated();
                }

                var second = bytes[index++];
                if ((second & 0xC0) != 0x80)
                {
                    throw InvalidContinuation();
                }

                builder.Append((char)(((first & 0x1F) << 6) | (second & 0x3F)));
            }
            else if ((first & 0xF0) == 0xE0)
            {
                if (index + 1 >= bytes.Length)
                {
                    throw Truncated();
                }

                var second = bytes[index++];
                var third = bytes[index++];
                if ((second & 0xC0) != 0x80 || (third & 0xC0) != 0x80)
                {
                    throw InvalidContinuation();
                }

                builder.Append((char)(((first & 0x0F) << 12) | ((second & 0x3F) << 6) | (third & 0x3F)));
            }
            else
            {
                throw new NbtException($"Invalid modified UTF-8 string: unsupported lead byte 0x{first:X2}.");
            }
        }

        return builder.ToString();
    }

    private static NbtException Truncated() =>
        new("Invalid modified UTF-8 string: the sequence is truncated.");

    private static NbtException InvalidContinuation() =>
        new("Invalid modified UTF-8 string: a continuation byte is malformed.");
}

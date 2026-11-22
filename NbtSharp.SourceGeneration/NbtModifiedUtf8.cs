namespace NbtSharp.SourceGeneration;

/// <summary>
/// Encodes strings as Java modified UTF-8 at generation time, so member names can be embedded as
/// pre-encoded byte literals that match the runtime reader byte for byte.
/// </summary>
/// <remarks>
/// <para>
/// This is a self-contained encoder that mirrors the repository's pinned Java baselines: U+0000
/// encodes as the two-byte form <c>C0 80</c>, supplementary characters encode as six-byte CESU-8
/// surrogate pairs (<c>ED A0 BD ED B8 80</c>-style), and lone surrogates encode as three bytes
/// each instead of being replaced.
/// </para>
/// <para>
/// It exists because C# <c>u8</c> literals are standard UTF-8 and cannot represent the modified
/// forms; the emitter therefore writes the encoded bytes as decimal byte array initializers.
/// </para>
/// </remarks>
internal static class NbtModifiedUtf8
{
    /// <summary>Encodes a string as Java modified UTF-8.</summary>
    /// <param name="value">The string to encode.</param>
    /// <returns>The modified UTF-8 bytes of <paramref name="value"/>.</returns>
    public static byte[] GetBytes(string value)
    {
        var bytes = new byte[GetByteCount(value)];
        var index = 0;
        for (var i = 0; i < value.Length; i++)
        {
            var character = value[i];
            if (character == '\0')
            {
                bytes[index++] = 0xC0;
                bytes[index++] = 0x80;
            }
            else if (character < 0x80)
            {
                bytes[index++] = (byte)character;
            }
            else if (character < 0x800)
            {
                bytes[index++] = (byte)(0xC0 | (character >> 6));
                bytes[index++] = (byte)(0x80 | (character & 0x3F));
            }
            else
            {
                // Surrogates (paired or lone) encode as one three-byte sequence per UTF-16 code
                // unit, which yields the CESU-8 form Java's modified UTF-8 uses.
                bytes[index++] = (byte)(0xE0 | (character >> 12));
                bytes[index++] = (byte)(0x80 | ((character >> 6) & 0x3F));
                bytes[index++] = (byte)(0x80 | (character & 0x3F));
            }
        }

        return bytes;
    }

    private static int GetByteCount(string value)
    {
        var count = 0;
        for (var i = 0; i < value.Length; i++)
        {
            var character = value[i];
            if (character == '\0')
            {
                count += 2;
            }
            else if (character < 0x80)
            {
                count++;
            }
            else if (character < 0x800)
            {
                count += 2;
            }
            else
            {
                count += 3;
            }
        }

        return count;
    }
}

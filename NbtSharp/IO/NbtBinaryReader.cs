using System.Buffers.Binary;
using System.Text;
using Myitian.Text;

namespace NbtSharp.IO;

/// <summary>
/// Reads the primitive values of Java Edition NBT from a stream.
/// </summary>
/// <remarks>
/// Multi-byte values are big-endian and strings use Java's modified UTF-8. End of stream is
/// reported as <see cref="NbtException"/> rather than <see cref="EndOfStreamException"/>.
/// </remarks>
internal sealed class NbtBinaryReader(Stream stream)
{
    private readonly ModifiedUTF8Encoding _stringEncoding = new(throwOnInvalidBytes: true);

    /// <summary>Reads exactly <paramref name="destination"/>.Length bytes.</summary>
    /// <param name="destination">The buffer to fill.</param>
    /// <exception cref="NbtException">The stream ends before the buffer is filled.</exception>
    public void ReadExactly(Span<byte> destination)
    {
        var read = 0;
        while (read < destination.Length)
        {
            var count = stream.Read(destination[read..]);
            if (count == 0)
            {
                throw new NbtException("Unexpected end of NBT data.");
            }

            read += count;
        }
    }

    /// <summary>Reads a single unsigned byte.</summary>
    public byte ReadByte()
    {
        Span<byte> buffer = stackalloc byte[1];
        ReadExactly(buffer);
        return buffer[0];
    }

    /// <summary>Reads a signed 8-bit integer.</summary>
    public sbyte ReadSByte() => (sbyte)ReadByte();

    /// <summary>Reads a big-endian signed 16-bit integer.</summary>
    public short ReadInt16()
    {
        Span<byte> buffer = stackalloc byte[sizeof(short)];
        ReadExactly(buffer);
        return BinaryPrimitives.ReadInt16BigEndian(buffer);
    }

    /// <summary>Reads a big-endian unsigned 16-bit integer.</summary>
    public ushort ReadUInt16()
    {
        Span<byte> buffer = stackalloc byte[sizeof(ushort)];
        ReadExactly(buffer);
        return BinaryPrimitives.ReadUInt16BigEndian(buffer);
    }

    /// <summary>Reads a big-endian signed 32-bit integer.</summary>
    public int ReadInt32()
    {
        Span<byte> buffer = stackalloc byte[sizeof(int)];
        ReadExactly(buffer);
        return BinaryPrimitives.ReadInt32BigEndian(buffer);
    }

    /// <summary>Reads a big-endian signed 64-bit integer.</summary>
    public long ReadInt64()
    {
        Span<byte> buffer = stackalloc byte[sizeof(long)];
        ReadExactly(buffer);
        return BinaryPrimitives.ReadInt64BigEndian(buffer);
    }

    /// <summary>Reads a big-endian 32-bit IEEE 754 value.</summary>
    public float ReadSingle()
    {
        Span<byte> buffer = stackalloc byte[sizeof(float)];
        ReadExactly(buffer);
        return BinaryPrimitives.ReadSingleBigEndian(buffer);
    }

    /// <summary>Reads a big-endian 64-bit IEEE 754 value.</summary>
    public double ReadDouble()
    {
        Span<byte> buffer = stackalloc byte[sizeof(double)];
        ReadExactly(buffer);
        return BinaryPrimitives.ReadDoubleBigEndian(buffer);
    }

    /// <summary>Reads <paramref name="count"/> bytes.</summary>
    /// <param name="count">The number of bytes to read; must be non-negative.</param>
    public byte[] ReadBytes(int count)
    {
        if (count < 0)
        {
            throw new NbtException($"Invalid NBT length {count}.");
        }

        var bytes = new byte[count];
        ReadExactly(bytes);
        return bytes;
    }

    /// <summary>Discards <paramref name="count"/> bytes.</summary>
    /// <param name="count">The number of bytes to discard; must be non-negative.</param>
    public void Skip(int count)
    {
        if (count < 0)
        {
            throw new NbtException($"Invalid NBT length {count}.");
        }

        Span<byte> buffer = stackalloc byte[512];
        while (count > 0)
        {
            var chunk = Math.Min(count, buffer.Length);
            ReadExactly(buffer[..chunk]);
            count -= chunk;
        }
    }

    /// <summary>Reads a length-prefixed modified UTF-8 string.</summary>
    /// <remarks>
    /// Java's <c>DataInputStream.readUTF</c> rejects any lead byte of 0xF0 or greater, so standard
    /// UTF-8 four-byte sequences are rejected here as well. Raw null bytes and overlong three-byte
    /// forms are accepted, matching that reader.
    /// </remarks>
    public string ReadString()
    {
        int length = ReadUInt16();
        var bytes = ReadBytes(length);
        foreach (var value in bytes)
        {
            if (value >= 0xF0)
            {
                throw new NbtException("Invalid modified UTF-8 string: lead bytes of 0xF0 or greater are not allowed.");
            }
        }

        try
        {
            return _stringEncoding.GetString(bytes);
        }
        catch (DecoderFallbackException ex)
        {
            throw new NbtException("Invalid modified UTF-8 string.", ex);
        }
    }
}

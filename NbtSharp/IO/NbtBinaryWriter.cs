using System.Buffers.Binary;
using Myitian.Text;

namespace NbtSharp.IO;

/// <summary>
/// Writes the primitive values of Java Edition NBT to a stream.
/// </summary>
/// <remarks>Multi-byte values are big-endian and strings use Java's modified UTF-8.</remarks>
internal sealed class NbtBinaryWriter(Stream stream)
{
    /// <summary>Writes a single unsigned byte.</summary>
    public void WriteByte(byte value)
    {
        Span<byte> buffer = stackalloc byte[1];
        buffer[0] = value;
        stream.Write(buffer);
    }

    /// <summary>Writes a signed 8-bit integer.</summary>
    public void WriteSByte(sbyte value) => WriteByte((byte)value);

    /// <summary>Writes a big-endian signed 16-bit integer.</summary>
    public void WriteInt16(short value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(short)];
        BinaryPrimitives.WriteInt16BigEndian(buffer, value);
        stream.Write(buffer);
    }

    /// <summary>Writes a big-endian unsigned 16-bit integer.</summary>
    public void WriteUInt16(ushort value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(ushort)];
        BinaryPrimitives.WriteUInt16BigEndian(buffer, value);
        stream.Write(buffer);
    }

    /// <summary>Writes a big-endian signed 32-bit integer.</summary>
    public void WriteInt32(int value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(buffer, value);
        stream.Write(buffer);
    }

    /// <summary>Writes a big-endian signed 64-bit integer.</summary>
    public void WriteInt64(long value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(buffer, value);
        stream.Write(buffer);
    }

    /// <summary>Writes a big-endian 32-bit IEEE 754 value.</summary>
    public void WriteSingle(float value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(float)];
        BinaryPrimitives.WriteSingleBigEndian(buffer, value);
        stream.Write(buffer);
    }

    /// <summary>Writes a big-endian 64-bit IEEE 754 value.</summary>
    public void WriteDouble(double value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(double)];
        BinaryPrimitives.WriteDoubleBigEndian(buffer, value);
        stream.Write(buffer);
    }

    /// <summary>Writes raw bytes.</summary>
    public void WriteBytes(ReadOnlySpan<byte> bytes) => stream.Write(bytes);

    /// <summary>Writes a length-prefixed modified UTF-8 string.</summary>
    /// <exception cref="NbtException">The encoded string exceeds 65535 bytes.</exception>
    public void WriteString(string value)
    {
        var bytes = ModifiedUTF8Encoding.Instance.GetBytes(value);
        if (bytes.Length > ushort.MaxValue)
        {
            throw new NbtException($"The string is too long to encode: {bytes.Length} bytes.");
        }

        WriteUInt16((ushort)bytes.Length);
        if (bytes.Length == 0)
        {
            return;
        }

        WriteBytes(bytes);
    }
}

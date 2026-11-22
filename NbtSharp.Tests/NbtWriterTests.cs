using System.Buffers;
using Myitian.Text;
using Xunit.Sdk;

namespace NbtSharp.Tests;

public sealed class NbtWriterTests
{
    [Fact]
    public void WritesNamedCompoundWithScalars()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer);
        writer.WriteStartCompound("root");
        writer.WriteInt32("Health", 20);
        writer.WriteString("Name", "Steve");
        writer.WriteEndCompound();
        writer.Complete();

        Assert.Equal(
            [
                0x0A, 0x00, 0x04, (byte)'r', (byte)'o', (byte)'o', (byte)'t', // TAG_Compound "root"
                0x03, 0x00, 0x06, (byte)'H', (byte)'e', (byte)'a', (byte)'l', (byte)'t', (byte)'h', 0x00, 0x00, 0x00, 0x14,
                0x08, 0x00, 0x04, (byte)'N', (byte)'a', (byte)'m', (byte)'e', 0x00, 0x05, (byte)'S', (byte)'t', (byte)'e', (byte)'v', (byte)'e',
                0x00, // TAG_End
            ],
            buffer.WrittenSpan.ToArray());
    }

    [Fact]
    public void WritesStringFromACharacterSpan()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer);
        writer.WriteStartCompound("root");
        writer.WriteString("Text", "hello".AsSpan());
        writer.WriteEndCompound();
        writer.Complete();

        var root = (NbtCompound)NbtSerializer.Deserialize(buffer.WrittenSpan).Value.Value!;
        Assert.Equal("hello", root["Text"] is string text ? text : "");
    }

    [Fact]
    public void NullStringIsRejected()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer);

        Throws<ArgumentNullException>(ref writer, static (ref value) => value.WriteString(null!));
    }

    [Fact]
    public void UnnamedRootOmitsOnlyTheRootName()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer, new NbtWriterOptions { RootFormat = NbtRootFormat.Unnamed });
        writer.WriteStartCompound("ignored");
        writer.WriteInt32("Value", 1);
        writer.WriteEndCompound();
        writer.Complete();

        var bytes = buffer.WrittenSpan;

        Assert.Equal(0x0A, bytes[0]);
        Assert.Equal(0x03, bytes[1]);
        Assert.Equal(0x00, bytes[2]);
        Assert.Equal(0x05, bytes[3]);
        Assert.Equal((byte)'V', bytes[4]);
    }

    [Fact]
    public void WritesEmptyListOfEnd()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer);
        writer.WriteStartCompound("root");
        writer.WriteStartList("Empty", NbtTagType.End, 0);
        writer.WriteEndList();
        writer.WriteEndCompound();
        writer.Complete();

        var bytes = buffer.WrittenSpan;
        ReadOnlySpan<byte> expectedTail =
        [
            0x09, 0x00, 0x05, (byte)'E', (byte)'m', (byte)'p', (byte)'t', (byte)'y',
            0x00, // element type TAG_End
            0x00, 0x00, 0x00, 0x00, // element count
            0x00, // end of the enclosing compound
        ];

        Assert.Equal(expectedTail.ToArray(), bytes[^expectedTail.Length..].ToArray());
    }

    [Fact]
    public void ListHeaderMustMatchWrittenElements()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer);
        writer.WriteStartCompound("root");
        writer.WriteStartList("Numbers", NbtTagType.Int, 2);

        Throws<NbtException>(ref writer, static (ref value) => value.WriteEndList());
        Throws<InvalidOperationException>(ref writer, static (ref value) => value.Complete());
    }

    [Fact]
    public void ElementTypeMustMatchHeader()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer);
        writer.WriteStartCompound("root");
        writer.WriteStartList("Numbers", NbtTagType.Int, 1);

        Throws<NbtException>(ref writer, static (ref value) => value.WriteString("wrong"));
        Throws<InvalidOperationException>(ref writer, static (ref value) => value.Complete());
    }

    [Fact]
    public void WritingMoreElementsThanDeclaredFails()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer);
        writer.WriteStartCompound("root");
        writer.WriteStartList("Numbers", NbtTagType.Int, 1);
        writer.WriteInt32(1);

        Throws<NbtException>(ref writer, static (ref value) => value.WriteInt32(2));
        Throws<InvalidOperationException>(ref writer, static (ref value) => value.Complete());
    }

    [Fact]
    public void CompletingWithOpenContainersThrows()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer);
        writer.WriteStartCompound("root");

        Throws<InvalidOperationException>(ref writer, static (ref value) => value.Complete());
    }

    [Fact]
    public void ExceedingMaxDepthThrows()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer, new NbtWriterOptions { MaxDepth = 2 });
        writer.WriteStartCompound("root");
        writer.WriteStartCompound("nested");

        Throws<NbtException>(ref writer, static (ref value) => value.WriteStartCompound("tooDeep"));
        Throws<InvalidOperationException>(ref writer, static (ref value) => value.Complete());
    }

    [Fact]
    public void OverlongTagNameThrowsBeforeWriting()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer);
        var name = new string('a', ushort.MaxValue + 1);

        Throws<NbtException>(ref writer, (ref value) => value.WriteStartCompound(name));
        Assert.Equal(0, buffer.WrittenCount);
    }

    [Fact]
    public void OverlongStringValueThrowsWithoutPartialWrite()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer);
        writer.WriteStartCompound("root");
        var headerLength = buffer.WrittenCount;
        var value = new string('中', 21846); // 65538 bytes

        Throws<NbtException>(ref writer, (ref inner) => inner.WriteString("v", value));
        Assert.Equal(headerLength, buffer.WrittenCount);
        Throws<InvalidOperationException>(ref writer, static (ref inner) => inner.Complete());
    }

    [Fact]
    public void ArraysAreWrittenWithLengthPrefix()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer);
        writer.WriteStartCompound("root");
        writer.WriteInt32Array("Values", [1, 2]);
        writer.WriteEndCompound();
        writer.Complete();

        var bytes = buffer.WrittenSpan;
        ReadOnlySpan<byte> expectedTail =
        [
            0x0B, 0x00, 0x06, (byte)'V', (byte)'a', (byte)'l', (byte)'u', (byte)'e', (byte)'s',
            0x00, 0x00, 0x00, 0x02,
            0x00, 0x00, 0x00, 0x01,
            0x00, 0x00, 0x00, 0x02,
            0x00,
        ];

        Assert.Equal(expectedTail.ToArray(), bytes[^expectedTail.Length..].ToArray());
    }

    [Fact]
    public void CompletingTwiceThrows()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer);
        writer.WriteInt32("root", 1);
        Assert.Equal(11, writer.BytesWritten);
        writer.Complete();

        Throws<InvalidOperationException>(ref writer, static (ref value) => value.Complete());
    }

    [Fact]
    public void EndListWithNonZeroCountThrows()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer);
        writer.WriteStartCompound("root");

        Throws<NbtException>(ref writer, static (ref value) =>
            value.WriteStartList("Empty", NbtTagType.End, 1));
    }

    [Fact]
    public void AnonymousRootWriteThrows()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer);

        Throws<InvalidOperationException>(ref writer, static (ref value) => value.WriteInt32(1));
    }

    [Fact]
    public void NamedWriteInsideListThrows()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer);
        writer.WriteStartCompound("root");
        writer.WriteStartList("Values", NbtTagType.Int, 1);

        Throws<InvalidOperationException>(ref writer, static (ref value) =>
            value.WriteInt32("element", 1));
    }

    [Fact]
    public void DefaultWriterIsRejected()
    {
        var writer = new NbtWriter();

        Throws<InvalidOperationException>(ref writer, static (ref value) => value.WriteInt32("x", 1));
    }

    [Fact]
    public void CompleteWithoutRootThrows()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer);

        Throws<InvalidOperationException>(ref writer, static (ref value) => value.Complete());
    }

    [Theory]
    [InlineData("Health")]
    [InlineData("\0")]
    [InlineData("é")]
    [InlineData("€")]
    [InlineData("中文")]
    [InlineData("\U0001F600")]
    [InlineData("")]
    public void ByteNameOverloadsProduceIdenticalBytes(string name)
    {
        var fromStrings = new ArrayBufferWriter<byte>();
        var stringWriter = new NbtWriter(fromStrings);
        stringWriter.WriteStartCompound("root");
        stringWriter.WriteSByte(name, -1);
        stringWriter.WriteInt16(name, 2);
        stringWriter.WriteInt32(name, 3);
        stringWriter.WriteInt64(name, 4);
        stringWriter.WriteSingle(name, 0.5f);
        stringWriter.WriteDouble(name, 0.25);
        stringWriter.WriteString(name, "v");
        stringWriter.WriteEndCompound();
        stringWriter.Complete();

        var encoded = EncodeName(name);
        var fromBytes = new ArrayBufferWriter<byte>();
        var byteWriter = new NbtWriter(fromBytes);
        byteWriter.WriteStartCompound("root");
        byteWriter.WriteSByte(encoded, -1);
        byteWriter.WriteInt16(encoded, 2);
        byteWriter.WriteInt32(encoded, 3);
        byteWriter.WriteInt64(encoded, 4);
        byteWriter.WriteSingle(encoded, 0.5f);
        byteWriter.WriteDouble(encoded, 0.25);
        byteWriter.WriteString(encoded, "v");
        byteWriter.WriteEndCompound();
        byteWriter.Complete();

        Assert.Equal(fromStrings.WrittenSpan.ToArray(), fromBytes.WrittenSpan.ToArray());
    }

    [Fact]
    public void ByteNameOverloadsAcceptTheLargestName()
    {
        var name = new string('a', ushort.MaxValue);
        var encoded = EncodeName(name);
        Assert.Equal(ushort.MaxValue, encoded.Length);

        var fromStrings = new ArrayBufferWriter<byte>();
        var stringWriter = new NbtWriter(fromStrings);
        stringWriter.WriteInt32(name, 7);
        stringWriter.Complete();

        var fromBytes = new ArrayBufferWriter<byte>();
        var byteWriter = new NbtWriter(fromBytes);
        byteWriter.WriteInt32(encoded, 7);
        byteWriter.Complete();

        Assert.Equal(fromStrings.WrittenSpan.ToArray(), fromBytes.WrittenSpan.ToArray());
    }

    [Fact]
    public void ByteMemberHeaderProducesIdenticalBytes()
    {
        var fromStrings = new ArrayBufferWriter<byte>();
        var stringWriter = new NbtWriter(fromStrings);
        stringWriter.WriteStartCompound("root");
        stringWriter.WriteInt32("é\0A", 7);
        stringWriter.WriteEndCompound();
        stringWriter.Complete();

        var fromBytes = new ArrayBufferWriter<byte>();
        var byteWriter = new NbtWriter(fromBytes);
        byteWriter.WriteStartCompound("root");
        byteWriter.WriteMemberHeader(NbtTagType.Int, EncodeName("é\0A"));
        byteWriter.WriteInt32(7);
        byteWriter.WriteEndCompound();
        byteWriter.Complete();

        Assert.Equal(fromStrings.WrittenSpan.ToArray(), fromBytes.WrittenSpan.ToArray());
    }

    [Fact]
    public void OverlongByteNameThrowsBeforeWriting()
    {
        var name = new byte[ushort.MaxValue + 1];
        for (var overload = 0; overload < 7; overload++)
        {
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new NbtWriter(buffer);
            var selected = overload;
            Throws<NbtException>(ref writer, (ref value) =>
            {
                switch (selected)
                {
                    case 0: value.WriteSByte(name, 1); break;
                    case 1: value.WriteInt16(name, 2); break;
                    case 2: value.WriteInt32(name, 3); break;
                    case 3: value.WriteInt64(name, 4); break;
                    case 4: value.WriteSingle(name, 0.5f); break;
                    case 5: value.WriteDouble(name, 0.25); break;
                    case 6: value.WriteString(name, "v"); break;
                }
            });

            Assert.Equal(0, buffer.WrittenCount);
        }

        // WriteMemberHeader requires an open compound, so the root header is already on the wire.
        var memberBuffer = new ArrayBufferWriter<byte>();
        var memberWriter = new NbtWriter(memberBuffer);
        memberWriter.WriteStartCompound("root");
        var headerLength = memberBuffer.WrittenCount;
        Throws<NbtException>(ref memberWriter, (ref value) =>
            value.WriteMemberHeader(NbtTagType.Int, name));
        Assert.Equal(headerLength, memberBuffer.WrittenCount);
    }

    [Fact]
    public void ByteNameOverloadWritesLeadingF0ThatTheReaderRejects()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer);
        writer.WriteStartCompound("root");
        writer.WriteInt32([0xF0, 0x41], 7);
        writer.WriteEndCompound();
        writer.Complete();

        Assert.Throws<NbtException>(() =>
        {
            var reader = new NbtReader(buffer.WrittenSpan);
            try
            {
                while (reader.Read())
                {
                }
            }
            finally
            {
                reader.Dispose();
            }
        });
    }

    private static byte[] EncodeName(string name) => ModifiedUTF8Encoding.Instance.GetBytes(name);

    private delegate void NbtWriterAction(ref NbtWriter writer);

    private static void Throws<TException>(ref NbtWriter writer, NbtWriterAction action)
        where TException : Exception
    {
        try
        {
            action(ref writer);
        }
        catch (TException)
        {
            return;
        }
        catch (Exception exception)
        {
            throw new XunitException(
                $"Expected {typeof(TException).Name} but caught {exception.GetType().Name}.",
                exception);
        }

        throw new XunitException($"Expected {typeof(TException).Name} but nothing was thrown.");
    }
}

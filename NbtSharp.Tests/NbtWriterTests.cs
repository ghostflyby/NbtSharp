using System.Buffers;
using Xunit.Sdk;

namespace NbtSharp.Tests;

public sealed class NbtWriterTests
{
    [Fact]
    public void WritesNamedCompoundWithScalars()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer);
        writer.WritePropertyName("root");
        writer.WriteStartCompound();
        writer.WritePropertyName("Health");
        writer.WriteInt32(20);
        writer.WritePropertyName("Name");
        writer.WriteString("Steve");
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
        writer.WritePropertyName("root");
        writer.WriteStartCompound();
        writer.WritePropertyName("Text");
        writer.WriteString("hello".AsSpan());
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

        Throws<ArgumentNullException>(ref writer, static (ref NbtWriter value) => value.WriteString((string)null!));
    }

    [Fact]
    public void UnnamedRootOmitsOnlyTheRootName()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer, new NbtWriterOptions { RootFormat = NbtRootFormat.Unnamed });
        writer.WriteStartCompound();
        writer.WritePropertyName("Value");
        writer.WriteInt32(1);
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
        writer.WritePropertyName("root");
        writer.WriteStartCompound();
        writer.WritePropertyName("Empty");
        writer.WriteStartList(NbtTagType.End, 0);
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
        writer.WritePropertyName("root");
        writer.WriteStartCompound();
        writer.WritePropertyName("Numbers");
        writer.WriteStartList(NbtTagType.Int, 2);

        Throws<NbtException>(ref writer, static (ref value) => value.WriteEndList());
        Throws<InvalidOperationException>(ref writer, static (ref value) => value.Complete());
    }

    [Fact]
    public void ElementTypeMustMatchHeader()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer);
        writer.WritePropertyName("root");
        writer.WriteStartCompound();
        writer.WritePropertyName("Numbers");
        writer.WriteStartList(NbtTagType.Int, 1);

        Throws<NbtException>(ref writer, static (ref value) => value.WriteString("wrong"));
        Throws<InvalidOperationException>(ref writer, static (ref value) => value.Complete());
    }

    [Fact]
    public void WritingMoreElementsThanDeclaredFails()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer);
        writer.WritePropertyName("root");
        writer.WriteStartCompound();
        writer.WritePropertyName("Numbers");
        writer.WriteStartList(NbtTagType.Int, 1);
        writer.WriteInt32(1);

        Throws<NbtException>(ref writer, static (ref value) => value.WriteInt32(2));
        Throws<InvalidOperationException>(ref writer, static (ref value) => value.Complete());
    }

    [Fact]
    public void CompletingWithOpenContainersThrows()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer);
        writer.WritePropertyName("root");
        writer.WriteStartCompound();

        Throws<InvalidOperationException>(ref writer, static (ref value) => value.Complete());
    }

    [Fact]
    public void ExceedingMaxDepthThrows()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer, new NbtWriterOptions { MaxDepth = 2 });
        writer.WritePropertyName("root");
        writer.WriteStartCompound();
        writer.WritePropertyName("nested");
        writer.WriteStartCompound();

        writer.WritePropertyName("tooDeep");
        Throws<NbtException>(ref writer, static (ref value) => value.WriteStartCompound());
        Throws<InvalidOperationException>(ref writer, static (ref value) => value.Complete());
    }

    [Fact]
    public void OverlongTagNameThrowsBeforeWriting()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer);
        var name = new string('a', ushort.MaxValue + 1);

        Throws<NbtException>(ref writer, (ref value) => value.WritePropertyName(name));
        Assert.Equal(0, buffer.WrittenCount);
    }

    [Fact]
    public void OverlongStringValueThrowsWithoutPartialWrite()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer);
        writer.WritePropertyName("root");
        writer.WriteStartCompound();
        var headerLength = buffer.WrittenCount;
        var value = new string('中', 21846); // 65538 bytes

        writer.WritePropertyName("v");
        Throws<NbtException>(ref writer, (ref inner) => inner.WriteString(value));
        Assert.Equal(headerLength, buffer.WrittenCount);
        Throws<InvalidOperationException>(ref writer, static (ref inner) => inner.Complete());
    }

    [Fact]
    public void ArraysAreWrittenWithLengthPrefix()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer);
        writer.WritePropertyName("root");
        writer.WriteStartCompound();
        writer.WritePropertyName("Values");
        writer.WriteInt32Array([1, 2]);
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
    public void PropertyNameInsideListThrows()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer);
        writer.WritePropertyName("root");
        writer.WriteStartCompound();
        writer.WritePropertyName("Values");
        writer.WriteStartList(NbtTagType.Int, 1);

        Throws<InvalidOperationException>(ref writer, static (ref value) => value.WritePropertyName("element"));
    }

    [Fact]
    public void CompletingTwiceThrows()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer);
        writer.WritePropertyName("root");
        writer.WriteInt32(1);
        Assert.Equal(11, writer.BytesWritten);
        writer.Complete();

        Throws<InvalidOperationException>(ref writer, static (ref value) => value.Complete());
    }

    [Fact]
    public void EndListWithNonZeroCountThrows()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer);
        writer.WritePropertyName("root");
        writer.WriteStartCompound();
        writer.WritePropertyName("Empty");

        Throws<NbtException>(ref writer, static (ref value) => value.WriteStartList(NbtTagType.End, 1));
    }

    [Fact]
    public void DefaultWriterIsRejected()
    {
        var writer = new NbtWriter();

        Throws<InvalidOperationException>(ref writer, static (ref value) => value.WriteInt32(1));
    }

    [Fact]
    public void CompleteWithoutRootThrows()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer);

        Throws<InvalidOperationException>(ref writer, static (ref value) => value.Complete());
    }

    [Fact]
    public void NamedRootRequiresAPropertyName()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer);

        Throws<InvalidOperationException>(ref writer, static (ref value) => value.WriteInt32(1));
    }

    private delegate void NbtWriterAction(ref NbtWriter writer);

    private static TException Throws<TException>(ref NbtWriter writer, NbtWriterAction action)
        where TException : Exception
    {
        try
        {
            action(ref writer);
        }
        catch (TException exception)
        {
            return exception;
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

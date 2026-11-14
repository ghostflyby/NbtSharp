using System.Buffers;
using Xunit.Sdk;

namespace NbtSharp.Tests;

public sealed class NbtReaderTests
{
    [Fact]
    public void ProducesTheExpectedTokenSequence()
    {
        var bytes = BuildSampleDocument();
        var reader = new NbtReader(bytes);

        Assert.True(reader.Read());
        Assert.Equal(NbtTokenType.StartCompound, reader.TokenType);
        Assert.Equal("root", reader.Name);
        Assert.Equal(1, reader.CurrentDepth);

        Assert.True(reader.Read());
        Assert.Equal(NbtTokenType.Value, reader.TokenType);
        Assert.Equal(NbtTagType.Int, reader.TagType);
        Assert.Equal("Health", reader.Name);
        Assert.Equal(20, reader.GetInt32());

        Assert.True(reader.Read());
        Assert.Equal(NbtTagType.String, reader.TagType);
        Assert.Equal("Name", reader.Name);
        Assert.Equal("Steve", reader.GetString());

        Assert.True(reader.Read());
        Assert.Equal(NbtTokenType.StartList, reader.TokenType);
        Assert.Equal("Position", reader.Name);
        Assert.Equal(NbtTagType.Double, reader.ListElementType);
        Assert.Equal(3, reader.ListLength);

        foreach (var expected in new[] { 1.0, 64.0, 2.0 })
        {
            Assert.True(reader.Read());
            Assert.Equal(NbtTokenType.Value, reader.TokenType);
            Assert.Equal(expected, reader.GetDouble());
        }

        Assert.True(reader.Read());
        Assert.Equal(NbtTokenType.EndList, reader.TokenType);

        Assert.True(reader.Read());
        Assert.Equal(NbtTokenType.EndCompound, reader.TokenType);
        Assert.Equal(0, reader.CurrentDepth);

        Assert.False(reader.Read());
    }

    [Fact]
    public void SkipConsumesTheCurrentValue()
    {
        var bytes = BuildSampleDocument();
        var reader = new NbtReader(bytes);

        reader.Read(); // start compound
        reader.Read(); // Health
        reader.Skip();

        reader.Read();
        Assert.Equal("Name", reader.Name);
        Assert.Equal("Steve", reader.GetString());
    }

    [Fact]
    public void DefaultReaderIsRejected()
    {
        var reader = new NbtReader();

        Throws<InvalidOperationException>(ref reader, static (ref value) => value.Read());
    }

    [Fact]
    public void SkipBeforeReadingThrows()
    {
        var reader = new NbtReader([]);

        Throws<InvalidOperationException>(ref reader, static (ref value) => value.Skip());
    }

    [Fact]
    public void SkippingAContainerTwiceThrows()
    {
        var bytes = BuildSampleDocument();
        var reader = new NbtReader(bytes);

        reader.Read(); // start compound
        reader.Read(); // Health
        reader.GetInt32();
        reader.Read(); // Name
        reader.GetString();
        reader.Read(); // start list
        reader.Skip();
        Throws<InvalidOperationException>(ref reader, static (ref value) => value.Skip());

        reader.Read();
        Assert.Equal(NbtTokenType.EndCompound, reader.TokenType);
    }

    [Fact]
    public void NegativeListCountThrows()
    {
        var reader = new NbtReader(
            [0x09, 0x03, 0xFF, 0xFF, 0xFF, 0xFF],
            new NbtReaderOptions { RootFormat = NbtRootFormat.Unnamed });

        Throws<NbtException>(ref reader, static (ref value) => value.Read());
    }

    [Fact]
    public void NegativeArrayLengthThrows()
    {
        var reader = new NbtReader(
            [0x0B, 0xFF, 0xFF, 0xFF, 0xFF],
            new NbtReaderOptions { RootFormat = NbtRootFormat.Unnamed });
        reader.Read();

        Throws<NbtException>(ref reader, static (ref value) => value.GetInt32Array());
    }

    [Fact]
    public void ExcessiveArrayLengthThrows()
    {
        var reader = new NbtReader(
            [0x0B, 0x01, 0x00, 0x00, 0x01],
            new NbtReaderOptions { RootFormat = NbtRootFormat.Unnamed });
        reader.Read();

        Throws<NbtException>(ref reader, static (ref value) => value.GetInt32Array());
    }

    [Fact]
    public void DeclaredArrayLengthBeyondAvailableDataThrows()
    {
        // Unnamed root: TAG_IntArray, length 1000, one payload byte.
        var reader = new NbtReader(
            [0x0B, 0x00, 0x00, 0x03, 0xE8, 0x00],
            new NbtReaderOptions { RootFormat = NbtRootFormat.Unnamed });
        reader.Read();

        Throws<NbtException>(ref reader, static (ref value) => value.GetInt32Array());
    }

    [Fact]
    public void HugeListCountFailsWithoutPreallocation()
    {
        // A root list of ints that claims int.MaxValue elements but supplies none.
        var bytes = new byte[] { 0x09, 0x03, 0x7F, 0xFF, 0xFF, 0xFF };

        Assert.Throws<NbtException>(() =>
            NbtSerializer.Deserialize(bytes, new NbtSerializerOptions { RootFormat = NbtRootFormat.Unnamed }));
    }

    [Fact]
    public void UnnamedRootHasNoName()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer, new NbtWriterOptions { RootFormat = NbtRootFormat.Unnamed });
        writer.WriteStartCompound("ignored");
        writer.WriteInt32("Value", 7);
        writer.WriteEndCompound();
        writer.Complete();

        var reader = new NbtReader(
            buffer.WrittenSpan,
            new NbtReaderOptions { RootFormat = NbtRootFormat.Unnamed });
        reader.Read();

        Assert.Null(reader.Name);
        reader.Read();
        Assert.Equal("Value", reader.Name);
    }

    [Fact]
    public void WrongGetterThrows()
    {
        var bytes = BuildSampleDocument();
        var reader = new NbtReader(bytes);

        reader.Read();
        reader.Read();

        var exception = Throws<NbtException>(ref reader, static (ref value) => value.GetString());
        Assert.Equal(NbtTagType.String, exception.ExpectedTagType);
        Assert.Equal(NbtTagType.Int, exception.ActualTagType);
        Assert.NotNull(exception.BytePosition);
    }

    [Fact]
    public void UnknownTagTypeThrows()
    {
        var reader = new NbtReader([0x0D, 0x00, 0x00]);

        Throws<NbtException>(ref reader, static (ref value) => value.Read());
    }

    [Fact]
    public void TruncatedDataThrows()
    {
        var reader = new NbtReader([0x0A, 0x00, 0x04, (byte)'r', (byte)'o', (byte)'o', (byte)'t']);
        reader.Read();

        Throws<NbtException>(ref reader, static (ref value) => value.Read());
    }

    [Fact]
    public void ListOfEndWithNonZeroLengthThrows()
    {
        var reader = new NbtReader([0x09, 0x00, 0x01, (byte)'x', 0x00, 0x00, 0x00, 0x01]);

        Throws<NbtException>(ref reader, static (ref value) => value.Read());
    }

    [Fact]
    public void ExceedingMaxDepthThrows()
    {
        // A root list containing a list containing a list of TAG_End.
        var bytes = new byte[]
        {
            0x09, 0x09, 0x00, 0x00, 0x00, 0x01,
            0x09, 0x00, 0x00, 0x00, 0x01,
            0x00, 0x00, 0x00, 0x00, 0x00,
        };

        var reader = new NbtReader(
            bytes,
            new NbtReaderOptions { RootFormat = NbtRootFormat.Unnamed, MaxDepth = 2 });

        Throws<NbtException>(ref reader, static (ref value) =>
        {
            while (value.Read())
            {
            }
        });
    }

    [Fact]
    public void BytesConsumedTracksHeadersAndPayloads()
    {
        var reader = new NbtReader([0x03, 0x00, 0x01, (byte)'v', 0x00, 0x00, 0x00, 0x2A]);

        Assert.True(reader.Read());
        Assert.Equal(4, reader.BytesConsumed);
        Assert.Equal(42, reader.GetInt32());
        Assert.Equal(8, reader.BytesConsumed);
        Assert.False(reader.Read());
    }

    private delegate void NbtReaderAction(ref NbtReader reader);

    private static TException Throws<TException>(ref NbtReader reader, NbtReaderAction action)
        where TException : Exception
    {
        try
        {
            action(ref reader);
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

    private static byte[] BuildSampleDocument()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer);
        writer.WriteStartCompound("root");
        writer.WriteInt32("Health", 20);
        writer.WriteString("Name", "Steve");
        writer.WriteStartList("Position", NbtTagType.Double, 3);
        writer.WriteDouble(1.0);
        writer.WriteDouble(64.0);
        writer.WriteDouble(2.0);
        writer.WriteEndList();
        writer.WriteEndCompound();
        writer.Complete();
        return buffer.WrittenSpan.ToArray();
    }
}

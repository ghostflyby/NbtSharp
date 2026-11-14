namespace NbtSharp.Tests;

public sealed class NbtReaderTests
{
    [Fact]
    public void ProducesTheExpectedTokenSequence()
    {
        var bytes = BuildSampleDocument();
        using var stream = new MemoryStream(bytes);
        using var reader = new NbtReader(stream);

        Assert.True(reader.Read());
        Assert.Equal(NbtTokenType.StartCompound, reader.TokenType);
        Assert.Equal("root", reader.Name);

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
        Assert.Equal(3, reader.ListCount);

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

        Assert.False(reader.Read());
    }

    [Fact]
    public void SkipConsumesTheCurrentValue()
    {
        var bytes = BuildSampleDocument();
        using var stream = new MemoryStream(bytes);
        using var reader = new NbtReader(stream);

        reader.Read(); // start compound
        reader.Read(); // Health
        reader.Skip();

        reader.Read();
        Assert.Equal("Name", reader.Name);
        Assert.Equal("Steve", reader.GetString());
    }

    [Fact]
    public void SkipBeforeReadingThrows()
    {
        using var stream = new MemoryStream();

        using var reader = new NbtReader(stream);

        Assert.Throws<NbtException>(() => reader.Skip());
    }

    [Fact]
    public void SkippingAContainerTwiceThrows()
    {
        var bytes = BuildSampleDocument();
        using var stream = new MemoryStream(bytes);
        using var reader = new NbtReader(stream);

        reader.Read(); // start compound
        reader.Read(); // Health
        reader.GetInt32();
        reader.Read(); // Name
        reader.GetString();
        reader.Read(); // start list
        reader.Skip();
        Assert.Throws<NbtException>(() => reader.Skip());

        reader.Read();
        Assert.Equal(NbtTokenType.EndCompound, reader.TokenType);
    }

    [Fact]
    public void NegativeListCountThrows()
    {
        using var stream = new MemoryStream([0x09, 0x03, 0xFF, 0xFF, 0xFF, 0xFF]);
        using var reader = new NbtReader(stream, NbtRootFormat.Unnamed);

        Assert.Throws<NbtException>(() => reader.Read());
    }

    [Fact]
    public void NegativeArrayLengthThrows()
    {
        using var stream = new MemoryStream([0x0B, 0xFF, 0xFF, 0xFF, 0xFF]);
        using var reader = new NbtReader(stream, NbtRootFormat.Unnamed);
        reader.Read();

        Assert.Throws<NbtException>(() => reader.GetIntArray());
    }

    [Fact]
    public void ExcessiveArrayLengthThrows()
    {
        using var stream = new MemoryStream([0x0B, 0x01, 0x00, 0x00, 0x01]);
        using var reader = new NbtReader(stream, NbtRootFormat.Unnamed);
        reader.Read();

        Assert.Throws<NbtException>(() => reader.GetIntArray());
    }

    [Fact]
    public void HugeListCountFailsWithoutPreallocation()
    {
        // A root list of ints that claims int.MaxValue elements but supplies none.
        using var stream = new MemoryStream([0x09, 0x03, 0x7F, 0xFF, 0xFF, 0xFF]);

        Assert.Throws<NbtException>(() =>
            NbtSerializer.Deserialize(stream, new NbtSerializerOptions { RootFormat = NbtRootFormat.Unnamed }));
    }

    [Fact]
    public void UnnamedRootHasNoName()
    {
        using var stream = new MemoryStream();
        using (var writer = new NbtWriter(stream, NbtRootFormat.Unnamed))
        {
            writer.WriteStartCompound("ignored");
            writer.WriteInt32("Value", 7);
            writer.WriteEndCompound();
        }

        stream.Position = 0;
        using var reader = new NbtReader(stream, NbtRootFormat.Unnamed);
        reader.Read();

        Assert.Null(reader.Name);
        reader.Read();
        Assert.Equal("Value", reader.Name);
    }

    [Fact]
    public void WrongGetterThrows()
    {
        var bytes = BuildSampleDocument();
        using var stream = new MemoryStream(bytes);
        using var reader = new NbtReader(stream);

        reader.Read();
        reader.Read();

        Assert.Throws<NbtException>(() => reader.GetString());
    }

    [Fact]
    public void UnknownTagTypeThrows()
    {
        using var stream = new MemoryStream([0x0D, 0x00, 0x00]);
        using var reader = new NbtReader(stream);

        Assert.Throws<NbtException>(() => reader.Read());
    }

    [Fact]
    public void TruncatedDataThrows()
    {
        using var stream = new MemoryStream([0x0A, 0x00, 0x04, (byte)'r', (byte)'o', (byte)'o', (byte)'t']);
        using var reader = new NbtReader(stream);
        reader.Read();

        Assert.Throws<NbtException>(() => reader.Read());
    }

    [Fact]
    public void ListOfEndWithNonZeroLengthThrows()
    {
        using var stream = new MemoryStream([0x09, 0x00, 0x01, (byte)'x', 0x00, 0x00, 0x00, 0x01]);
        using var reader = new NbtReader(stream);

        Assert.Throws<NbtException>(() => reader.Read());
    }

    [Fact]
    public void ExceedingMaxDepthThrows()
    {
        // Build nested lists of lists: the root tag, then one list per level, ending with an empty list.
        var nested = new MemoryStream();
        nested.WriteByte((byte)NbtTagType.List);
        for (var i = 1; i < NbtReader.MaxDepth + 1; i++)
        {
            nested.WriteByte((byte)NbtTagType.List);
            nested.WriteByte(0x00);
            nested.WriteByte(0x00);
            nested.WriteByte(0x00);
            nested.WriteByte(0x01);
        }

        nested.WriteByte((byte)NbtTagType.List);
        nested.WriteByte(0x00);
        nested.WriteByte(0x00);
        nested.WriteByte(0x00);
        nested.WriteByte(0x00);

        nested.Position = 0;
        using var reader = new NbtReader(nested, NbtRootFormat.Unnamed);

        Assert.Throws<NbtException>(() =>
        {
            while (reader.Read())
            {
            }
        });
    }

    private static byte[] BuildSampleDocument()
    {
        using var stream = new MemoryStream();
        using (var writer = new NbtWriter(stream))
        {
            writer.WriteStartCompound("root");
            writer.WriteInt32("Health", 20);
            writer.WriteString("Name", "Steve");
            writer.WriteStartList("Position", NbtTagType.Double, 3);
            writer.WriteElement(1.0);
            writer.WriteElement(64.0);
            writer.WriteElement(2.0);
            writer.WriteEndList();
            writer.WriteEndCompound();
        }

        return stream.ToArray();
    }
}

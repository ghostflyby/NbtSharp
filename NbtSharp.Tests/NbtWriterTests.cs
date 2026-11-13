namespace NbtSharp.Tests;

public sealed class NbtWriterTests
{
    [Fact]
    public void WritesNamedCompoundWithScalars()
    {
        using var stream = new MemoryStream();

        using (var writer = new NbtWriter(stream))
        {
            writer.WriteStartCompound("root");
            writer.WriteInt32("Health", 20);
            writer.WriteString("Name", "Steve");
            writer.WriteEndCompound();
        }

        Assert.Equal(
            [
                0x0A, 0x00, 0x04, (byte)'r', (byte)'o', (byte)'o', (byte)'t', // TAG_Compound "root"
                0x03, 0x00, 0x06, (byte)'H', (byte)'e', (byte)'a', (byte)'l', (byte)'t', (byte)'h', 0x00, 0x00, 0x00, 0x14,
                0x08, 0x00, 0x04, (byte)'N', (byte)'a', (byte)'m', (byte)'e', 0x00, 0x05, (byte)'S', (byte)'t', (byte)'e', (byte)'v', (byte)'e',
                0x00, // TAG_End
            ],
            stream.ToArray());
    }

    [Fact]
    public void UnnamedRootOmitsOnlyTheRootName()
    {
        using var stream = new MemoryStream();

        using (var writer = new NbtWriter(stream, NbtRootFormat.Unnamed))
        {
            writer.WriteStartCompound("ignored");
            writer.WriteInt32("Value", 1);
            writer.WriteEndCompound();
        }

        var bytes = stream.ToArray();

        Assert.Equal(0x0A, bytes[0]);
        Assert.Equal(0x03, bytes[1]);
        Assert.Equal(0x00, bytes[2]);
        Assert.Equal(0x05, bytes[3]);
        Assert.Equal((byte)'V', bytes[4]);
    }

    [Fact]
    public void WritesEmptyListOfEnd()
    {
        using var stream = new MemoryStream();

        using (var writer = new NbtWriter(stream))
        {
            writer.WriteStartCompound("root");
            writer.WriteStartList("Empty", NbtTagType.End, 0);
            writer.WriteEndList();
            writer.WriteEndCompound();
        }

        var bytes = stream.ToArray();
        ReadOnlySpan<byte> expectedTail =
        [
            0x09, 0x00, 0x05, (byte)'E', (byte)'m', (byte)'p', (byte)'t', (byte)'y',
            0x00, // element type TAG_End
            0x00, 0x00, 0x00, 0x00, // element count
            0x00, // end of the enclosing compound
        ];

        Assert.Equal(expectedTail.ToArray(), bytes[^expectedTail.Length..]);
    }

    [Fact]
    public void ListHeaderMustMatchWrittenElements()
    {
        using var stream = new MemoryStream();
        var writer = new NbtWriter(stream);
        writer.WriteStartCompound("root");
        writer.WriteStartList("Numbers", NbtTagType.Int, 2);

        Assert.Throws<NbtException>(() => writer.WriteEndList());
        Assert.Throws<NbtException>(() => writer.Dispose());
    }

    [Fact]
    public void ElementTypeMustMatchHeader()
    {
        using var stream = new MemoryStream();
        var writer = new NbtWriter(stream);
        writer.WriteStartCompound("root");
        writer.WriteStartList("Numbers", NbtTagType.Int, 1);

        Assert.Throws<NbtException>(() => writer.WriteElement("wrong"));
        Assert.Throws<NbtException>(() => writer.Dispose());
    }

    [Fact]
    public void WritingMoreElementsThanDeclaredFails()
    {
        using var stream = new MemoryStream();
        var writer = new NbtWriter(stream);
        writer.WriteStartCompound("root");
        writer.WriteStartList("Numbers", NbtTagType.Int, 1);
        writer.WriteElement(1);

        Assert.Throws<NbtException>(() => writer.WriteElement(2));
        Assert.Throws<NbtException>(() => writer.Dispose());
    }

    [Fact]
    public void DisposingWithOpenContainersThrows()
    {
        using var stream = new MemoryStream();
        var writer = new NbtWriter(stream);
        writer.WriteStartCompound("root");

        Assert.Throws<NbtException>(() => writer.Dispose());
    }

    [Fact]
    public void ExceedingMaxDepthThrows()
    {
        using var stream = new MemoryStream();
        var writer = new NbtWriter(stream);
        writer.WriteStartCompound("root");
        for (var i = 1; i < NbtReader.MaxDepth; i++)
        {
            writer.WriteStartCompound("nested");
        }

        Assert.Throws<NbtException>(() => writer.WriteStartCompound("tooDeep"));
        Assert.Throws<NbtException>(() => writer.Dispose());
    }

    [Fact]
    public void OverlongTagNameThrowsBeforeWriting()
    {
        using var stream = new MemoryStream();
        var writer = new NbtWriter(stream);
        var name = new string('a', ushort.MaxValue + 1);

        Assert.Throws<NbtException>(() => writer.WriteStartCompound(name));
        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public void OverlongStringValueThrowsWithoutPartialWrite()
    {
        using var stream = new MemoryStream();
        var writer = new NbtWriter(stream);
        writer.WriteStartCompound("root");
        long headerLength = stream.Length;
        var value = new string('中', 21846); // 65538 bytes

        Assert.Throws<NbtException>(() => writer.WriteString("v", value));
        Assert.Equal(headerLength, stream.Length);
        Assert.Throws<NbtException>(() => writer.Dispose());
    }

    [Fact]
    public void ArraysAreWrittenWithLengthPrefix()
    {
        using var stream = new MemoryStream();

        using (var writer = new NbtWriter(stream))
        {
            writer.WriteStartCompound("root");
            writer.WriteIntArray("Values", [1, 2]);
            writer.WriteEndCompound();
        }

        var bytes = stream.ToArray();
        ReadOnlySpan<byte> expectedTail = [0x0B, 0x00, 0x06, (byte)'V', (byte)'a', (byte)'l', (byte)'u', (byte)'e', (byte)'s', 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02, 0x00];

        Assert.Equal(expectedTail.ToArray(), bytes[^expectedTail.Length..]);
    }
}

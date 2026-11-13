namespace NbtSharp.Tests;

public sealed class NbtStreamAdapterTests
{
    [Fact]
    public void GenericStreamRoundTrips()
    {
        using var stream = new MemoryStream();
        NbtSerializer.Serialize(stream, 42, "answer");
        stream.Position = 0;

        Assert.Equal(42, NbtSerializer.Deserialize<int>(stream));
    }

    [Fact]
    public void DomStreamRoundTrips()
    {
        using var stream = new MemoryStream();
        var root = new NbtRoot("root", new NbtCompound { ["X"] = 1 });
        NbtSerializer.Serialize(stream, root);
        stream.Position = 0;

        var actual = NbtSerializer.Deserialize(stream);

        Assert.Equal("root", actual.Name);
        var compound = (NbtCompound)actual.Value.Value!;
        Assert.Equal(1, compound["X"] is int value ? value : 0);
    }

    [Fact]
    public void StreamDeserializationReadsToTheEnd()
    {
        using var stream = new MemoryStream(NbtSerializer.Serialize(42, "root"));

        var actual = NbtSerializer.Deserialize<int>(stream);

        Assert.Equal(42, actual);
        Assert.Equal(stream.Length, stream.Position);
    }

    [Fact]
    public void StreamDeserializationReportsTheRootName()
    {
        using var stream = new MemoryStream(NbtSerializer.Serialize(42, "answer"));

        var actual = NbtSerializer.Deserialize<int>(stream, out var rootName);

        Assert.Equal(42, actual);
        Assert.Equal("answer", rootName);
    }

    [Fact]
    public void StreamDeserializationHonorsMaxDocumentBytes()
    {
        using var stream = new MemoryStream(new byte[100]);
        var options = new NbtSerializerOptions { MaxDocumentBytes = 4 };

        Assert.Throws<NbtException>(() => NbtSerializer.Deserialize<int>(stream, options));
    }

    [Fact]
    public void StreamSerializationHonorsMaxDocumentBytes()
    {
        using var stream = new MemoryStream();
        var options = new NbtSerializerOptions { MaxDocumentBytes = 2 };

        Assert.Throws<NbtException>(() => NbtSerializer.Serialize(stream, 42, "root", options));
    }

    [Fact]
    public void UnnamedRootRejectsANameInTheGenericApi()
    {
        var options = new NbtSerializerOptions { RootFormat = NbtRootFormat.Unnamed };

        Assert.Throws<NbtException>(() => NbtSerializer.Serialize(42, "named", options));
    }

    [Fact]
    public void UnnamedRootRoundTripsThroughTheGenericApi()
    {
        var options = new NbtSerializerOptions { RootFormat = NbtRootFormat.Unnamed };
        var data = NbtSerializer.Serialize(42, options: options);

        Assert.Equal(42, NbtSerializer.Deserialize<int>(data, out var rootName, options));
        Assert.Null(rootName);
    }
}

namespace NbtSharp.Tests;

public sealed class NbtSerializerTests
{
    [Fact]
    public void RoundTripsAScalarRoot()
    {
        var root = new NbtRoot("value", 42);

        var actual = RoundTrip(root);

        Assert.Equal("value", actual.Name);
        Assert.True(actual.Value is int value && value == 42);
    }

    [Fact]
    public void RoundTripsAllScalarAndArrayTypes()
    {
        var root = new NbtRoot("root", new NbtCompound
        {
            ["Byte"] = (sbyte)-8,
            ["Short"] = (short)-300,
            ["Int"] = -70000,
            ["Long"] = -5_000_000_000L,
            ["Float"] = 0.25f,
            ["Double"] = 0.125,
            ["String"] = "hello",
            ["ByteArray"] = new sbyte[] { -1, 0, 1 },
            ["IntArray"] = new[] { -1, 0, 1 },
            ["LongArray"] = new[] { -1L, 0L, 1L },
        });

        var actual = (NbtCompound)RoundTrip(root).Value.Value!;

        Assert.Equal((sbyte)-8, actual["Byte"] is sbyte b ? b : sbyte.MinValue);
        Assert.Equal((short)-300, actual["Short"] is short s ? s : short.MinValue);
        Assert.Equal(-70000, actual["Int"] is int i ? i : int.MinValue);
        Assert.Equal(-5_000_000_000L, actual["Long"] is long l ? l : long.MinValue);
        Assert.Equal(0.25f, actual["Float"] is float f ? f : float.NaN);
        Assert.Equal(0.125, actual["Double"] is double d ? d : double.NaN);
        Assert.Equal("hello", actual["String"] is string text ? text : string.Empty);
        Assert.Equal(new sbyte[] { -1, 0, 1 }, actual["ByteArray"] is sbyte[] bytes ? bytes : []);
        Assert.Equal(new[] { -1, 0, 1 }, actual["IntArray"] is int[] ints ? ints : []);
        Assert.Equal(new[] { -1L, 0L, 1L }, actual["LongArray"] is long[] longs ? longs : []);
    }

    [Fact]
    public void RoundTripsEveryListElementType()
    {
        var root = new NbtRoot("root", new NbtCompound
        {
            ["Bytes"] = new NbtList(new List<sbyte> { 1, 2 }),
            ["Shorts"] = new NbtList(new List<short> { 1, 2 }),
            ["Ints"] = new NbtList(new List<int> { 1, 2 }),
            ["Longs"] = new NbtList(new List<long> { 1, 2 }),
            ["Floats"] = new NbtList(new List<float> { 1.5f }),
            ["Doubles"] = new NbtList(new List<double> { 1.5 }),
            ["Strings"] = new NbtList(new List<string> { "a", "b" }),
            ["ByteArrays"] = new NbtList(new List<sbyte[]> { new sbyte[] { 1 } }),
            ["IntArrays"] = new NbtList(new List<int[]> { new[] { 1 } }),
            ["LongArrays"] = new NbtList(new List<long[]> { new[] { 1L } }),
            ["Lists"] = new NbtList(new List<NbtList> { new List<int> { 7 } }),
            ["Compounds"] = new NbtList(new List<NbtCompound> { new() { ["Value"] = 9 } }),
        });

        var actual = (NbtCompound)RoundTrip(root).Value.Value!;

        Assert.Equal(NbtTagType.Byte, (actual["Bytes"] is NbtList b ? b : default).ElementType);
        Assert.Equal(NbtTagType.Short, (actual["Shorts"] is NbtList s ? s : default).ElementType);
        Assert.Equal(NbtTagType.Int, (actual["Ints"] is NbtList i ? i : default).ElementType);
        Assert.Equal(NbtTagType.Long, (actual["Longs"] is NbtList l ? l : default).ElementType);
        Assert.Equal(NbtTagType.Float, (actual["Floats"] is NbtList f ? f : default).ElementType);
        Assert.Equal(NbtTagType.Double, (actual["Doubles"] is NbtList d ? d : default).ElementType);
        Assert.Equal(NbtTagType.String, (actual["Strings"] is NbtList str ? str : default).ElementType);
        Assert.Equal(NbtTagType.ByteArray, (actual["ByteArrays"] is NbtList ba ? ba : default).ElementType);
        Assert.Equal(NbtTagType.IntArray, (actual["IntArrays"] is NbtList ia ? ia : default).ElementType);
        Assert.Equal(NbtTagType.LongArray, (actual["LongArrays"] is NbtList la ? la : default).ElementType);
        Assert.Equal(NbtTagType.List, (actual["Lists"] is NbtList nested ? nested : default).ElementType);
        Assert.Equal(NbtTagType.Compound, (actual["Compounds"] is NbtList comp ? comp : default).ElementType);

        var intList = actual["Ints"] is NbtList value ? value : default;
        Assert.Equal(new List<int> { 1, 2 }, (List<int>)intList.Value!);
    }

    [Fact]
    public void EmptyTypedListKeepsItsElementType()
    {
        var root = new NbtRoot("root", new NbtCompound { ["Empty"] = new NbtList(new List<int>()) });

        var actual = (NbtCompound)RoundTrip(root).Value.Value!;
        var list = actual["Empty"] is NbtList value ? value : default;

        Assert.Equal(0, list.Count);
        Assert.NotEqual(NbtTagType.End, list.ElementType);
        Assert.Equal(NbtTagType.Int, list.ElementType);
    }

    [Fact]
    public void EmptyListOfEndRoundTrips()
    {
        var root = new NbtRoot("root", new NbtCompound { ["Empty"] = new NbtList(new NbtEmptyList()) });

        var actual = (NbtCompound)RoundTrip(root).Value.Value!;
        var list = actual["Empty"] is NbtList value ? value : default;

        Assert.Equal(0, list.Count);
        Assert.Equal(NbtTagType.End, list.ElementType);
    }

    [Fact]
    public void UnnamedRootRoundTripsWithoutAName()
    {
        var root = new NbtRoot(null, new NbtCompound { ["Value"] = 1 });
        var options = new NbtSerializerOptions { RootFormat = NbtRootFormat.Unnamed };

        var actual = RoundTrip(root, options);

        Assert.Null(actual.Name);
    }

    [Fact]
    public void NestedCompoundsAndListsRoundTrip()
    {
        var root = new NbtRoot("root", new NbtCompound
        {
            ["Player"] = new NbtCompound
            {
                ["Health"] = 20,
                ["Tags"] = new NbtList(new List<string> { "a", "b" }),
                ["Inventory"] = new NbtList(new List<NbtCompound>
                {
                    new() { ["Slot"] = (sbyte)0, ["Count"] = (sbyte)64 },
                }),
            },
        });

        var player = (NbtCompound)RoundTrip(root).Value.Value!;
        Assert.True(player["Player"] is NbtCompound);
        var inner = (NbtCompound)player["Player"].Value!;

        Assert.Equal(20, inner["Health"] is int health ? health : 0);

        var tags = inner["Tags"] is NbtList list ? list : default;
        Assert.Equal(2, tags.Count);

        var inventory = inner["Inventory"] is NbtList items ? items : default;
        Assert.Equal(1, inventory.Count);
        Assert.True(((List<NbtCompound>)inventory.Value!)[0]["Count"] is sbyte count && count == 64);
    }

    [Fact]
    public void DeserializingAnUnknownTagTypeThrows()
    {
        using var stream = new MemoryStream([0x0D, 0x00, 0x04, (byte)'r', (byte)'o', (byte)'o', (byte)'t']);

        Assert.Throws<NbtException>(() => NbtSerializer.Deserialize(stream));
    }

    [Fact]
    public void SerializingAnUninitializedValueThrows()
    {
        var root = new NbtRoot("root", default);
        using var stream = new MemoryStream();

        Assert.Throws<NbtException>(() => NbtSerializer.Serialize(stream, root));
    }

    [Fact]
    public void NestedUninitializedValueKeepsTheOriginalError()
    {
        var root = new NbtRoot("root", new NbtCompound { ["Bad"] = default });
        using var stream = new MemoryStream();

        var exception = Assert.Throws<NbtException>(() => NbtSerializer.Serialize(stream, root));

        Assert.Contains("uninitialized", exception.Message);
    }

    [Fact]
    public void NamedRootRequiresAName()
    {
        using var stream = new MemoryStream();

        Assert.Throws<NbtException>(() => NbtSerializer.Serialize(stream, new NbtRoot(null, 1)));
    }

    [Fact]
    public void UnnamedRootRejectsAName()
    {
        var options = new NbtSerializerOptions { RootFormat = NbtRootFormat.Unnamed };
        using var stream = new MemoryStream();

        Assert.Throws<NbtException>(() => NbtSerializer.Serialize(stream, new NbtRoot("root", 1), options));
    }

    [Fact]
    public void DuplicateCompoundNamesKeepTheLastValue()
    {
        using var stream = new MemoryStream();
        using (var writer = new NbtWriter(stream))
        {
            writer.WriteStartCompound("root");
            writer.WriteInt32("X", 1);
            writer.WriteInt32("X", 2);
            writer.WriteEndCompound();
        }

        stream.Position = 0;
        var compound = (NbtCompound)NbtSerializer.Deserialize(stream).Value.Value!;

        Assert.Equal(2, compound["X"] is int value ? value : 0);
    }

    private static NbtRoot RoundTrip(NbtRoot root, NbtSerializerOptions? options = null)
    {
        using var stream = new MemoryStream();
        NbtSerializer.Serialize(stream, root, options);
        stream.Position = 0;
        return NbtSerializer.Deserialize(stream, options);
    }
}

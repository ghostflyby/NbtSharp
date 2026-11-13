namespace NbtSharp.Tests;

public sealed class NbtListTests
{
    [Fact]
    public void ElementTypeFollowsTheListCase()
    {
        Assert.Equal(NbtTagType.Byte, new NbtList(new List<sbyte>()).ElementType);
        Assert.Equal(NbtTagType.Short, new NbtList(new List<short>()).ElementType);
        Assert.Equal(NbtTagType.Int, new NbtList(new List<int>()).ElementType);
        Assert.Equal(NbtTagType.Long, new NbtList(new List<long>()).ElementType);
        Assert.Equal(NbtTagType.Float, new NbtList(new List<float>()).ElementType);
        Assert.Equal(NbtTagType.Double, new NbtList(new List<double>()).ElementType);
        Assert.Equal(NbtTagType.ByteArray, new NbtList(new List<sbyte[]>()).ElementType);
        Assert.Equal(NbtTagType.String, new NbtList(new List<string>()).ElementType);
        Assert.Equal(NbtTagType.List, new NbtList(new List<NbtList>()).ElementType);
        Assert.Equal(NbtTagType.Compound, new NbtList(new List<NbtCompound>()).ElementType);
        Assert.Equal(NbtTagType.IntArray, new NbtList(new List<int[]>()).ElementType);
        Assert.Equal(NbtTagType.LongArray, new NbtList(new List<long[]>()).ElementType);
    }

    [Fact]
    public void EmptyListKeepsItsElementType()
    {
        NbtList emptyInts = new List<int>();

        Assert.Equal(0, emptyInts.Count);
        Assert.Equal(NbtTagType.Int, emptyInts.ElementType);
        Assert.NotEqual(NbtTagType.End, emptyInts.ElementType);
    }

    [Fact]
    public void DefaultListRepresentsListOfEnd()
    {
        NbtList empty = default;

        Assert.Same(NbtEndList.Instance, empty.Value);
        Assert.Equal(0, empty.Count);
        Assert.Equal(NbtTagType.End, empty.ElementType);
        Assert.True(empty is NbtEndList);
    }

    [Fact]
    public void EndMarkerConstructorCanonicalizesToDefault()
    {
        Assert.Equal(default(NbtList), new NbtList(NbtEndList.Instance));
    }

    [Fact]
    public void ExhaustiveSwitchHandlesDefault()
    {
        Assert.Equal("end", Describe(default));
        Assert.Equal("int", Describe(new List<int>()));
        Assert.Equal("string", Describe(new List<string>()));
    }

    [Fact]
    public void EveryListHasAValue()
    {
        NbtList empty = default;
        NbtList ints = new List<int>();

        Assert.True(empty.HasValue);
        Assert.True(ints.HasValue);
        Assert.False(empty is null);
        Assert.False(ints is null);
        Assert.True(empty is NbtEndList);
        Assert.False(ints is NbtEndList);
    }

    [Fact]
    public void CountReflectsTheUnderlyingList()
    {
        NbtList numbers = new List<int> { 1, 2, 3 };

        Assert.Equal(3, numbers.Count);
        Assert.True(numbers is List<int>);
    }

    [Fact]
    public void NestedListsAreExpressible()
    {
        var inner = new List<NbtList>
        {
            new List<int> { 1, 2 },
            new List<string> { "a" },
        };
        NbtList outer = inner;

        Assert.Equal(NbtTagType.List, outer.ElementType);
        Assert.Equal(2, outer.Count);
        Assert.True(((List<NbtList>)outer.Value!)[0] is List<int>);
    }

    [Fact]
    public void ConstructingWithANullListIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new NbtList((List<int>)null!));
    }

    [Fact]
    public void ConstructingWithANullEndMarkerIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new NbtList((NbtEndList)null!));
    }

    private static string Describe(NbtList list) => list switch
    {
        NbtEndList => "end",
        List<sbyte> => "byte",
        List<short> => "short",
        List<int> => "int",
        List<long> => "long",
        List<float> => "float",
        List<double> => "double",
        List<sbyte[]> => "byteArray",
        List<string> => "string",
        List<NbtList> => "list",
        List<NbtCompound> => "compound",
        List<int[]> => "intArray",
        List<long[]> => "longArray",
    };
}

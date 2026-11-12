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
    public void EmptyListOfEndIsDistinct()
    {
        NbtList empty = new NbtEmptyList();

        Assert.Equal(0, empty.Count);
        Assert.Equal(NbtTagType.End, empty.ElementType);
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
    public void UninitializedListHasNoElementType()
    {
        NbtList list = default;

        Assert.Throws<NbtException>(() => list.ElementType);
        Assert.Throws<NbtException>(() => list.Count);
    }
}

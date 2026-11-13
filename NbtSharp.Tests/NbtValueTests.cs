namespace NbtSharp.Tests;

public sealed class NbtValueTests
{
    [Fact]
    public void IntegerWidthSelectsTagType()
    {
        NbtValue a = (short)20;
        NbtValue b = 20;
        NbtValue c = 20L;

        Assert.Equal(NbtTagType.Short, a.TagType);
        Assert.Equal(NbtTagType.Int, b.TagType);
        Assert.Equal(NbtTagType.Long, c.TagType);
    }

    [Fact]
    public void FloatingPointWidthSelectsTagType()
    {
        NbtValue single = 0.1f;
        NbtValue number = 0.1;

        Assert.Equal(NbtTagType.Float, single.TagType);
        Assert.Equal(NbtTagType.Double, number.TagType);
    }

    [Fact]
    public void RemainingCasesSelectTagType()
    {
        Assert.Equal(NbtTagType.Byte, new NbtValue((sbyte)1).TagType);
        Assert.Equal(NbtTagType.ByteArray, new NbtValue(new sbyte[] { 1, 0, 1 }).TagType);
        Assert.Equal(NbtTagType.String, new NbtValue("Steve").TagType);
        Assert.Equal(NbtTagType.IntArray, new NbtValue(new[] { 1, 2 }).TagType);
        Assert.Equal(NbtTagType.LongArray, new NbtValue(new[] { 1L }).TagType);
        Assert.Equal(NbtTagType.Compound, new NbtValue(new NbtCompound()).TagType);
        Assert.Equal(NbtTagType.List, new NbtValue(new NbtList(new List<int>())).TagType);
    }

    [Fact]
    public void PatternMatchingReadsTheContainedValue()
    {
        NbtValue value = 20;

        Assert.True(value is int);
        Assert.False(value is long);
        Assert.True(value is int and 20);

        if (value is int health)
        {
            Assert.Equal(20, health);
        }
        else
        {
            Assert.Fail("The value should have matched an int.");
        }
    }

    [Fact]
    public void SwitchOverCasesIsExhaustive()
    {
        Assert.Equal("int", Describe(20));
        Assert.Equal("string", Describe("Steve"));
        Assert.Equal("float", Describe(0.1f));
        Assert.Equal("list", Describe(new NbtList(new List<int>())));
        Assert.Equal("compound", Describe(new NbtCompound()));
        Assert.Equal("other", Describe(1L));
    }

    [Fact]
    public void UninitializedValueHasNoTagType()
    {
        NbtValue value = default;

        Assert.Null(value.Value);
        Assert.Throws<NbtException>(() => value.TagType);
    }

    [Fact]
    public void DefaultListValueHasTheListTagType()
    {
        NbtValue value = default(NbtList);

        Assert.Equal(NbtTagType.List, value.TagType);
    }

    [Fact]
    public void EndMarkerIsOnlyVisibleAfterUnwrappingTheList()
    {
        NbtValue value = default(NbtList);

        Assert.False(value.Value is NbtEndList);
        Assert.True(value is NbtList list && list is NbtEndList);
    }

    private static string Describe(NbtValue value) => value switch
    {
        int => "int",
        string => "string",
        float => "float",
        NbtList => "list",
        NbtCompound => "compound",
        not null => "other",
        null => "none",
    };
}

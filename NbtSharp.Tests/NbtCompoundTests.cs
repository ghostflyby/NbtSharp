namespace NbtSharp.Tests;

public sealed class NbtCompoundTests
{
    [Fact]
    public void CompoundHoldsHeterogeneousValues()
    {
        var player = new NbtCompound
        {
            ["Health"] = 20,
            ["Name"] = "Steve",
            ["Speed"] = 0.1f,
            ["Alive"] = (sbyte)1,
        };

        Assert.Equal(4, player.Count);
        Assert.Equal(NbtTagType.Int, player["Health"].TagType);
        Assert.Equal(NbtTagType.String, player["Name"].TagType);
        Assert.Equal(NbtTagType.Float, player["Speed"].TagType);
        Assert.Equal(NbtTagType.Byte, player["Alive"].TagType);
    }

    [Fact]
    public void ReadingBackUsesPatternMatching()
    {
        var player = new NbtCompound { ["Health"] = 20 };

        Assert.True(player["Health"] is int);
        Assert.Equal(20, player["Health"] is int value ? value : 0);
        Assert.Equal(20, player["Health"] is int h ? h : 0);
    }

    [Fact]
    public void AssignedMemberReplacesPreviousValue()
    {
        var compound = new NbtCompound { ["Value"] = 1 };

        compound["Value"] = 2L;

        Assert.Equal(NbtTagType.Long, compound["Value"].TagType);
        Assert.True(compound["Value"] is long);
    }

    [Fact]
    public void MissingMemberThrows()
    {
        // ReSharper disable once CollectionNeverUpdated.Local
        var compound = new NbtCompound();

        Assert.Throws<KeyNotFoundException>(() => compound["Missing"]);
    }

    [Fact]
    public void NestedCompoundsAreExpressible()
    {
        var child = new NbtCompound { ["Value"] = 42 };
        var root = new NbtCompound { ["Child"] = child };

        Assert.True(root["Child"] is NbtCompound);
        Assert.True(child["Value"] is int value && value == 42);
    }
}

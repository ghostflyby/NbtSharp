using System.Reflection;

namespace NbtSharp.Tests;

public sealed class NbtInheritanceMatrixTests
{
    [Fact]
    public void ReflectionPropertyLookupSeesInheritedAttributes()
    {
        var property = typeof(InheritedDerived).GetProperty(nameof(InheritedDerived.Hidden))!;

        Assert.NotNull(property.GetCustomAttribute<NbtIgnoreAttribute>(inherit: true));

        // IsDefined ignores its inherit argument for properties, so the resolver must not use it
        // for inherited member attributes.
        Assert.False(property.IsDefined(typeof(NbtIgnoreAttribute), inherit: true));
    }

    [Fact]
    public void IgnoredOverridesDoNotLeaveHiddenBaseMembers()
    {
        var reflection = new NbtSerializerOptions
        {
            TypeInfoResolver = new NbtReflectionTypeInfoResolver(),
        };
        var leaf = new HiddenLeaf { Value = 5 };
        var baseIgnored = new HiddenLeafFromIgnoredBase { Value = 6 };

        var leafData = NbtSerializer.Serialize(
            leaf,
            InheritanceContext.Default.HiddenLeaf,
            "root");
        var baseIgnoredData = NbtSerializer.Serialize(
            baseIgnored,
            InheritanceContext.Default.HiddenLeafFromIgnoredBase,
            "root");
        var reflectedLeafData = NbtSerializer.Serialize(leaf, "root", reflection);
        var reflectedBaseIgnoredData = NbtSerializer.Serialize(baseIgnored, "root", reflection);

        Assert.Equal(5, NbtSerializer.Deserialize(leafData, InheritanceContext.Default.HiddenLeaf).Value);
        Assert.Equal(
            6,
            NbtSerializer.Deserialize(
                baseIgnoredData,
                InheritanceContext.Default.HiddenLeafFromIgnoredBase).Value);
        Assert.Equal(5, NbtSerializer.Deserialize<HiddenLeaf>(reflectedLeafData, reflection).Value);
        Assert.Equal(
            6,
            NbtSerializer.Deserialize<HiddenLeafFromIgnoredBase>(
                reflectedBaseIgnoredData,
                reflection).Value);
    }

    [Fact]
    public void OverriddenMembersInheritBaseAttributes()
    {
        var value = new InheritedDerived
        {
            Renamed = 3,
            Hidden = 99,
        };
        value.IncludedField = "base-field";
        var reflection = new NbtSerializerOptions
        {
            TypeInfoResolver = new NbtReflectionTypeInfoResolver(),
        };

        var generatedData = NbtSerializer.Serialize(
            value,
            InheritanceContext.Default.InheritedDerived,
            "root");
        var reflectedData = NbtSerializer.Serialize(value, "root", reflection);
        var generatedRoot = (NbtCompound)NbtSerializer.Deserialize(generatedData).Value.Value!;
        var reflectedRoot = (NbtCompound)NbtSerializer.Deserialize(reflectedData).Value.Value!;

        Assert.Equal(
            generatedRoot.Keys.Order(StringComparer.Ordinal),
            reflectedRoot.Keys.Order(StringComparer.Ordinal));
        Assert.True(generatedRoot.ContainsKey("renamed_base"));
        Assert.True(generatedRoot.ContainsKey("IncludedField"));
        Assert.False(generatedRoot.ContainsKey("Renamed"));
        Assert.False(generatedRoot.ContainsKey("Hidden"));
        Assert.True(reflectedRoot.ContainsKey("renamed_base"));
        Assert.True(reflectedRoot.ContainsKey("IncludedField"));
        Assert.False(reflectedRoot.ContainsKey("Hidden"));
        Assert.Equal(3, (int)generatedRoot["renamed_base"].Value!);
        Assert.Equal("base-field", (string)generatedRoot["IncludedField"].Value!);
        Assert.Equal(3, (int)reflectedRoot["renamed_base"].Value!);
        Assert.Equal("base-field", (string)reflectedRoot["IncludedField"].Value!);

        var generatedCopy = NbtSerializer.Deserialize(
            generatedData,
            InheritanceContext.Default.InheritedDerived);
        var reflectedCopy = NbtSerializer.Deserialize<InheritedDerived>(reflectedData, reflection);

        Assert.Equal(3, generatedCopy.Renamed);
        Assert.Equal(0, generatedCopy.Hidden);
        Assert.Equal("base-field", generatedCopy.IncludedField);
        Assert.Equal(3, reflectedCopy.Renamed);
        Assert.Equal(0, reflectedCopy.Hidden);
        Assert.Equal("base-field", reflectedCopy.IncludedField);
    }
}

internal class InheritedBase
{
    [NbtPropertyName("renamed_base")]
    public virtual int Renamed { get; set; }

    [NbtIgnore]
    public virtual int Hidden { get; set; }

    [NbtInclude]
    public string IncludedField = "";
}

internal sealed class InheritedDerived : InheritedBase
{
    public override int Renamed { get; set; }

    public override int Hidden { get; set; }
}

internal class HiddenBase
{
    public virtual int Value { get; set; }
}

internal class HiddenMid : HiddenBase
{
    [NbtIgnore]
    public override int Value { get; set; }
}

internal sealed class HiddenLeaf : HiddenMid
{
    public new int Value { get; set; }
}

internal class IgnoredBase
{
    [NbtIgnore]
    public virtual int Value { get; set; }
}

internal class IgnoredMid : IgnoredBase
{
    public override int Value { get; set; }
}

internal sealed class HiddenLeafFromIgnoredBase : IgnoredMid
{
    public new int Value { get; set; }
}

[NbtSerializable(typeof(InheritedDerived))]
[NbtSerializable(typeof(HiddenLeaf))]
[NbtSerializable(typeof(HiddenLeafFromIgnoredBase))]
internal partial class InheritanceContext : NbtSerializerContext
{
}

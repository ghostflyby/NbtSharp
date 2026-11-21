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

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void IgnoredOverridesDoNotLeaveHiddenBaseMembers(SerializationMode mode)
    {
        var path = CreatePath(mode);
        var leaf = new HiddenLeaf { Value = 5 };
        var baseIgnored = new HiddenLeafFromIgnoredBase { Value = 6 };

        var leafData = path.Serialize(leaf);
        var baseIgnoredData = path.Serialize(baseIgnored);

        Assert.Equal(5, path.Deserialize<HiddenLeaf>(leafData).Value);
        Assert.Equal(6, path.Deserialize<HiddenLeafFromIgnoredBase>(baseIgnoredData).Value);
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void OverridesInheritConverterAttributes(SerializationMode mode)
    {
        var path = CreatePath(mode);
        var value = new InheritedConverterDerived { Value = 5 };

        var data = path.Serialize(value);
        var root = (NbtCompound)NbtSerializer.Deserialize(data).Value.Value!;
        var copy = path.Deserialize<InheritedConverterDerived>(data);

        Assert.Equal(105, (int)root["Value"].Value!);
        Assert.Equal(5, copy.Value);
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void OverriddenMembersInheritBaseAttributes(SerializationMode mode)
    {
        var path = CreatePath(mode);
        var value = new InheritedDerived
        {
            Renamed = 3,
            Hidden = 99,
        };
        value.IncludedField = "base-field";

        var data = path.Serialize(value);
        var root = (NbtCompound)NbtSerializer.Deserialize(data).Value.Value!;

        Assert.True(root.ContainsKey("renamed_base"));
        Assert.True(root.ContainsKey("IncludedField"));
        Assert.False(root.ContainsKey("Renamed"));
        Assert.False(root.ContainsKey("Hidden"));
        Assert.Equal(3, (int)root["renamed_base"].Value!);
        Assert.Equal("base-field", (string)root["IncludedField"].Value!);

        var copy = path.Deserialize<InheritedDerived>(data);

        Assert.Equal(3, copy.Renamed);
        Assert.Equal(0, copy.Hidden);
        Assert.Equal("base-field", copy.IncludedField);
    }

    private static SerializationTestPath CreatePath(SerializationMode mode) =>
        mode == SerializationMode.Reflection
            ? SerializationTestPath.Reflection(new NbtSerializerOptions
            {
                TypeInfoResolver = new NbtReflectionTypeInfoResolver(),
            })
            : SerializationTestPath.Generated(InheritanceContext.Default);
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

internal class InheritedConverterBase
{
    [NbtUseConverter(typeof(OffsetIntConverter))]
    public virtual int Value { get; set; }
}

internal sealed class InheritedConverterDerived : InheritedConverterBase
{
    public override int Value { get; set; }
}

[NbtSerializable(typeof(InheritedDerived))]
[NbtSerializable(typeof(InheritedConverterDerived))]
[NbtSerializable(typeof(HiddenLeaf))]
[NbtSerializable(typeof(HiddenLeafFromIgnoredBase))]
internal partial class InheritanceContext : NbtSerializerContext
{
}

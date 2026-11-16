namespace NbtSharp.Aot.Tests;

public sealed class GeneratedSerializationAotTests
{
    [Fact]
    public void RoundTripsSimpleDto()
    {
        var value = new AotSimple
        {
            Name = "Steve",
            Health = 20,
            Enabled = true,
            Kind = AotKind.Creeper,
        };

        var data = NbtSerializer.Serialize(value, AotContext.Default.AotSimple, "root");
        var copy = NbtSerializer.Deserialize(data, AotContext.Default.AotSimple);

        Assert.Equal(value.Name, copy.Name);
        Assert.Equal(value.Health, copy.Health);
        Assert.Equal(value.Enabled, copy.Enabled);
        Assert.Equal(value.Kind, copy.Kind);
    }

    [Fact]
    public void RoundTripsRequiredAndInitMembers()
    {
        var value = new AotRequired
        {
            Name = "Alex",
            Value = 7,
            Optional = null,
        };

        var data = NbtSerializer.Serialize(value, AotContext.Default.AotRequired, "root");
        var copy = NbtSerializer.Deserialize(data, AotContext.Default.AotRequired);

        Assert.Equal(value.Name, copy.Name);
        Assert.Equal(value.Value, copy.Value);
        Assert.Null(copy.Optional);
    }

    [Fact]
    public void RoundTripsEnums()
    {
        var value = new AotEnums
        {
            Kind = AotKind.Skeleton,
            Optional = AotKind.Zombie,
            Values = [AotKind.Creeper, AotKind.Skeleton],
        };

        var data = NbtSerializer.Serialize(value, AotContext.Default.AotEnums, "root");
        var copy = NbtSerializer.Deserialize(data, AotContext.Default.AotEnums);

        Assert.Equal(value.Kind, copy.Kind);
        Assert.Equal(value.Optional, copy.Optional);
        Assert.Equal(value.Values, copy.Values);
    }

    [Fact]
    public void RoundTripsClosedGenericDto()
    {
        var value = new AotWrapper<AotSimple>
        {
            Value = new AotSimple { Name = "wrapped", Health = 3 },
            Values = [new AotSimple { Name = "many", Health = 4 }],
        };

        var data = NbtSerializer.Serialize(
            value,
            AotContext.Default.AotWrapperOfAotSimple,
            "root");
        var copy = NbtSerializer.Deserialize(
            data,
            AotContext.Default.AotWrapperOfAotSimple);

        Assert.Equal(value.Value.Name, copy.Value.Name);
        Assert.Equal(value.Value.Health, copy.Value.Health);
        Assert.Single(copy.Values);
        Assert.Equal(value.Values[0].Name, copy.Values[0].Name);
    }

    [Fact]
    public void RoundTripsCollectionsAndNullableValues()
    {
        var value = new AotCollections
        {
            Numbers = [1, 2, 3],
            Labels = ["a", "b"],
            Items = [new AotSimple { Name = "item", Health = 1 }],
            Values = [4, 5],
            Map = new Dictionary<string, AotSimple>
            {
                ["first"] = new AotSimple { Name = "mapped", Health = 2 },
            },
            OptionalNumber = 9,
        };

        var data = NbtSerializer.Serialize(value, AotContext.Default.AotCollections, "root");
        var copy = NbtSerializer.Deserialize(data, AotContext.Default.AotCollections);

        Assert.Equal(value.Numbers, copy.Numbers);
        Assert.Equal(value.Labels, copy.Labels);
        Assert.Single(copy.Items);
        Assert.Equal(value.Items[0].Name, copy.Items[0].Name);
        Assert.Equal(value.Values, copy.Values);
        Assert.Equal(value.Map["first"].Name, copy.Map["first"].Name);
        Assert.Equal(value.OptionalNumber, copy.OptionalNumber);
    }

    [Fact]
    public void RoundTripsPositionalRecord()
    {
        var value = new AotRecord(3, 4);

        var data = NbtSerializer.Serialize(value, AotContext.Default.AotRecord, "root");
        var copy = NbtSerializer.Deserialize(data, AotContext.Default.AotRecord);

        Assert.Equal(value.X, copy.X);
        Assert.Equal(value.Y, copy.Y);
    }

    [Fact]
    public void RoundTripsExplicitMemberConverters()
    {
        var value = new AotConverters
        {
            Id = new Guid("11111111-1111-1111-1111-111111111111"),
            Amount = 12.34m,
        };

        var data = NbtSerializer.Serialize(value, AotContext.Default.AotConverters, "root");
        var copy = NbtSerializer.Deserialize(data, AotContext.Default.AotConverters);

        Assert.Equal(value.Id, copy.Id);
        Assert.Equal(value.Amount, copy.Amount);
    }
}

[NbtSerializable(typeof(AotSimple))]
[NbtSerializable(typeof(AotRequired))]
[NbtSerializable(typeof(AotEnums))]
[NbtSerializable(typeof(AotWrapper<AotSimple>))]
[NbtSerializable(typeof(AotCollections))]
[NbtSerializable(typeof(AotRecord))]
[NbtSerializable(typeof(AotConverters))]
internal partial class AotContext : NbtSerializerContext
{
    public AotContext()
    {
    }

    public AotContext(NbtSerializerOptions options)
        : base(options)
    {
    }
}

internal sealed class AotSimple
{
    public string Name { get; init; } = "";

    public int Health { get; init; }

    public bool Enabled { get; init; }

    public AotKind Kind { get; init; }
}

internal sealed class AotRequired
{
    public required string Name { get; init; }

    public int Value { get; init; }

    public string? Optional { get; init; }
}

internal sealed class AotEnums
{
    public AotKind Kind { get; init; }

    public AotKind? Optional { get; init; }

    public List<AotKind> Values { get; init; } = [];
}

internal sealed class AotWrapper<T>
{
    public T Value { get; init; } = default!;

    public List<T> Values { get; init; } = [];
}

internal sealed class AotCollections
{
    public int[] Numbers { get; init; } = [];

    public string[] Labels { get; init; } = [];

    public List<AotSimple> Items { get; init; } = [];

    public IReadOnlyList<int> Values { get; init; } = [];

    public Dictionary<string, AotSimple> Map { get; init; } = [];

    public int? OptionalNumber { get; init; }
}

internal sealed record AotRecord(int X, int Y);

internal sealed class AotConverters
{
    [NbtUseConverter(typeof(AotGuidConverter))]
    public Guid Id { get; set; }

    [NbtUseConverter(typeof(AotDecimalConverter))]
    public decimal Amount { get; set; }
}

internal sealed class AotGuidConverter : NbtConverter<Guid>
{
    public AotGuidConverter()
    {
    }

    public override Guid Read(ref NbtReader reader, NbtSerializerOptions options) =>
        new(reader.GetByteArray());

    public override void Write(ref NbtWriter writer, Guid value, NbtSerializerOptions options) =>
        writer.WriteByteArray(value.ToByteArray());

    public override bool TryGetTagType(
        Guid value,
        NbtSerializerOptions options,
        out NbtTagType tagType)
    {
        tagType = NbtTagType.ByteArray;
        return true;
    }
}

internal sealed class AotDecimalConverter : NbtConverter<decimal>
{
    public AotDecimalConverter()
    {
    }

    public override decimal Read(ref NbtReader reader, NbtSerializerOptions options) =>
        decimal.Parse(reader.GetString(), global::System.Globalization.CultureInfo.InvariantCulture);

    public override void Write(ref NbtWriter writer, decimal value, NbtSerializerOptions options) =>
        writer.WriteString(value.ToString(global::System.Globalization.CultureInfo.InvariantCulture));

    public override bool TryGetTagType(
        decimal value,
        NbtSerializerOptions options,
        out NbtTagType tagType)
    {
        tagType = NbtTagType.String;
        return true;
    }
}

internal enum AotKind
{
    Zombie,
    Creeper,
    Skeleton,
}

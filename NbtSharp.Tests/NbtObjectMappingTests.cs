using System.Buffers;

namespace NbtSharp.Tests;

public sealed class NbtObjectMappingTests
{
    [Fact]
    public void MapsPropertiesAndNestedObjects()
    {
        var options = ReflectionOptions();
        var player = new Player
        {
            Name = "Steve",
            Health = 20,
            Items =
            [
                new Item { Id = "apple", Count = 3 },
                new Item { Id = "sword", Count = 1 },
            ],
        };

        var data = NbtSerializer.Serialize(player, "root", options);
        var actual = NbtSerializer.Deserialize<Player>(data, options);

        Assert.Equal("Steve", actual.Name);
        Assert.Equal(20, actual.Health);
        Assert.Equal(2, actual.Items.Count);
        Assert.Equal("apple", actual.Items[0].Id);
        Assert.Equal(3, actual.Items[0].Count);
        Assert.Equal("sword", actual.Items[1].Id);
    }

    [Fact]
    public void AppliesNameAndIgnoreAttributes()
    {
        var options = ReflectionOptions();
        var value = new RenamedDto { Value = 7, Ignored = 9 };

        var root = NbtSerializer.Deserialize(NbtSerializer.Serialize(value, "root", options));
        var compound = (NbtCompound)root.Value.Value!;

        Assert.True(compound.ContainsKey("n"));
        Assert.False(compound.ContainsKey("Value"));
        Assert.False(compound.ContainsKey("Ignored"));
        Assert.Equal(7, NbtSerializer.Deserialize<RenamedDto>(NbtSerializer.Serialize(value, "root", options), options).Value);
    }

    [Fact]
    public void IncludesOptedInFields()
    {
        var options = ReflectionOptions();
        var value = new FieldDto { Value = 42 };

        var actual = NbtSerializer.Deserialize<FieldDto>(
            NbtSerializer.Serialize(value, "root", options),
            options);

        Assert.Equal(42, actual.Value);
    }

    [Fact]
    public void BindsConstructorParametersToMembers()
    {
        var options = ReflectionOptions();
        var value = new ImmutableDto(7, "seven");

        var actual = NbtSerializer.Deserialize<ImmutableDto>(
            NbtSerializer.Serialize(value, "root", options),
            options);

        Assert.Equal(7, actual.Value);
        Assert.Equal("seven", actual.Name);
    }

    [Fact]
    public void BindsPositionalRecords()
    {
        var options = ReflectionOptions();
        var value = new PointRecord(3, 4);

        var actual = NbtSerializer.Deserialize<PointRecord>(
            NbtSerializer.Serialize(value, "root", options),
            options);

        Assert.Equal(3, actual.X);
        Assert.Equal(4, actual.Y);
    }

    [Fact]
    public void BindsRenamedMembersThroughTheirConstructorParameters()
    {
        var options = ReflectionOptions();
        var value = new RenamedRecord(7);

        var actual = NbtSerializer.Deserialize<RenamedRecord>(
            NbtSerializer.Serialize(value, "root", options),
            options);

        Assert.Equal(7, actual.Value);
    }

    [Fact]
    public void SkipsUnknownMembersAndLeavesMissingMembersAtTheirDefaults()
    {
        var options = ReflectionOptions();
        var root = new NbtRoot("root", new NbtCompound
        {
            ["Health"] = 20,
            ["Extra"] = "ignored",
        });

        var actual = NbtSerializer.Deserialize<SimpleDto>(NbtSerializer.Serialize(root), options);

        Assert.Equal(20, actual.Health);
        Assert.Equal("", actual.Name);
    }

    [Fact]
    public void OmittedNullMembersStayNull()
    {
        var options = ReflectionOptions();
        var value = new NullableDto { Required = 1, Optional = null };

        var root = (NbtCompound)NbtSerializer.Deserialize(NbtSerializer.Serialize(value, "root", options)).Value.Value!;
        Assert.False(root.ContainsKey("Optional"));

        var actual = NbtSerializer.Deserialize<NullableDto>(NbtSerializer.Serialize(value, "root", options), options);
        Assert.Equal(1, actual.Required);
        Assert.Null(actual.Optional);
    }

    [Fact]
    public void NullMembersCanBeRejected()
    {
        var options = ReflectionOptions(nullValueHandling: NbtNullValueHandling.Throw);

        Assert.Throws<NbtException>(() => NbtSerializer.Serialize(new NullableDto { Required = 1 }, "root", options));
    }

    [Fact]
    public void DuplicateCompoundNamesKeepTheLastValueOrThrow()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer);
        writer.WriteStartCompound("root");
        writer.WriteInt32("X", 1);
        writer.WriteInt32("X", 2);
        writer.WriteEndCompound();
        writer.Complete();

        var options = ReflectionOptions();
        Assert.Equal(2, NbtSerializer.Deserialize<SimpleDto>(buffer.WrittenSpan, options).X);

        var throwingOptions = ReflectionOptions(rejectDuplicateCompoundNames: true);
        Assert.Throws<NbtException>(() => NbtSerializer.Deserialize<SimpleDto>(buffer.WrittenSpan, throwingOptions));
    }

    [Fact]
    public void TypesWithoutAUsableConstructorAreRejected()
    {
        var options = ReflectionOptions();

        Assert.Throws<NotSupportedException>(() =>
            NbtSerializer.Serialize(new UnsupportedDto(1), "root", options));
    }

    private static NbtSerializerOptions ReflectionOptions(
        bool rejectDuplicateCompoundNames = false,
        NbtNullValueHandling nullValueHandling = NbtNullValueHandling.Omit) => new()
        {
            TypeInfoResolver = new NbtReflectionTypeInfoResolver(),
            RejectDuplicateCompoundNames = rejectDuplicateCompoundNames,
            NullValueHandling = nullValueHandling,
        };

    private sealed class Player
    {
        public string Name { get; init; } = "";

        public int Health { get; init; }

        public List<Item> Items { get; init; } = [];
    }

    private sealed class Item
    {
        public string Id { get; init; } = "";

        public int Count { get; init; }
    }

    private sealed class RenamedDto
    {
        [NbtPropertyName("n")]
        public int Value { get; init; }

        [NbtIgnore]
        public int Ignored { get; set; }
    }

    private sealed class FieldDto
    {
        [NbtInclude]
        public int Value;
    }

    private sealed class ImmutableDto
    {
        public ImmutableDto(int value, string name)
        {
            Value = value;
            Name = name;
        }

        public int Value { get; }

        public string Name { get; }
    }

    private sealed record PointRecord(int X, int Y);

    private sealed record RenamedRecord([property: NbtPropertyName("v")] int Value);

    private sealed class SimpleDto
    {
        public int Health { get; set; }

        public int X { get; set; }

        public string Name { get; set; } = "";
    }

    private sealed class NullableDto
    {
        public int Required { get; init; }

        public string? Optional { get; init; }
    }

    private sealed class UnsupportedDto
    {
        public UnsupportedDto(int unrelated)
        {
            _ = unrelated;
        }
    }
}

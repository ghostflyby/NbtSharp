using System.Buffers;
using System.Diagnostics.CodeAnalysis;

namespace NbtSharp.Tests;

public sealed class NbtObjectMappingTests
{
    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void MapsPropertiesAndNestedObjects(SerializationMode mode)
    {
        var path = CreatePath(mode);
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

        var data = path.Serialize(player);
        var actual = path.Deserialize<Player>(data);

        Assert.Equal("Steve", actual.Name);
        Assert.Equal(20, actual.Health);
        Assert.Equal(2, actual.Items.Count);
        Assert.Equal("apple", actual.Items[0].Id);
        Assert.Equal(3, actual.Items[0].Count);
        Assert.Equal("sword", actual.Items[1].Id);
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void AppliesNameAndIgnoreAttributes(SerializationMode mode)
    {
        var path = CreatePath(mode);
        var value = new RenamedDto { Value = 7, Ignored = 9 };

        var data = path.Serialize(value);
        var root = NbtSerializer.Deserialize(data);
        var compound = (NbtCompound)root.Value.Value!;

        Assert.True(compound.ContainsKey("n"));
        Assert.False(compound.ContainsKey("Value"));
        Assert.False(compound.ContainsKey("Ignored"));
        Assert.Equal(7, path.Deserialize<RenamedDto>(data).Value);
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void IncludesOptedInFields(SerializationMode mode)
    {
        var path = CreatePath(mode);
        var value = new FieldDto { Value = 42 };

        var actual = path.Deserialize<FieldDto>(path.Serialize(value));

        Assert.Equal(42, actual.Value);
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void BindsConstructorParametersToMembers(SerializationMode mode)
    {
        var path = CreatePath(mode);
        var value = new ImmutableDto(7, "seven");

        var actual = path.Deserialize<ImmutableDto>(path.Serialize(value));

        Assert.Equal(7, actual.Value);
        Assert.Equal("seven", actual.Name);
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void BindsPositionalRecords(SerializationMode mode)
    {
        var path = CreatePath(mode);
        var value = new PointRecord(3, 4);

        var actual = path.Deserialize<PointRecord>(path.Serialize(value));

        Assert.Equal(3, actual.X);
        Assert.Equal(4, actual.Y);
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void BindsRenamedMembersThroughTheirConstructorParameters(SerializationMode mode)
    {
        var path = CreatePath(mode);
        var value = new RenamedRecord(7);

        var actual = path.Deserialize<RenamedRecord>(path.Serialize(value));

        Assert.Equal(7, actual.Value);
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void SkipsUnknownMembersAndLeavesMissingMembersAtTheirDefaults(SerializationMode mode)
    {
        var path = CreatePath(mode);
        var root = new NbtRoot("root", new NbtCompound
        {
            ["Health"] = 20,
            ["Extra"] = "ignored",
        });

        var actual = path.Deserialize<SimpleDto>(NbtSerializer.Serialize(root));

        Assert.Equal(20, actual.Health);
        Assert.Equal("", actual.Name);
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void OmittedNullMembersStayNull(SerializationMode mode)
    {
        var path = CreatePath(mode);
        var value = new NullableDto { Required = 1, Optional = null };

        var data = path.Serialize(value);
        var root = (NbtCompound)NbtSerializer.Deserialize(data).Value.Value!;
        Assert.False(root.ContainsKey("Optional"));

        var actual = path.Deserialize<NullableDto>(data);
        Assert.Equal(1, actual.Required);
        Assert.Null(actual.Optional);
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void NullMembersCanBeRejected(SerializationMode mode)
    {
        var path = CreatePath(mode, nullValueHandling: NbtNullValueHandling.Throw);

        Assert.Throws<NbtException>(() => path.Serialize(new NullableDto { Required = 1 }));
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void DuplicateCompoundNamesKeepTheLastValueOrThrow(SerializationMode mode)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer);
        writer.WriteStartCompound("root");
        writer.WriteInt32("X", 1);
        writer.WriteInt32("X", 2);
        writer.WriteEndCompound();
        writer.Complete();

        var path = CreatePath(mode);
        Assert.Equal(2, path.Deserialize<SimpleDto>(buffer.WrittenSpan).X);

        var throwingPath = CreatePath(mode, rejectDuplicateCompoundNames: true);
        Assert.Throws<NbtException>(() =>
            throwingPath.Deserialize<SimpleDto>(buffer.WrittenSpan));
    }

    [Fact]
    public void TypesWithoutAUsableConstructorAreRejected()
    {
        var options = ReflectionOptions();

        Assert.Throws<NotSupportedException>(() =>
            NbtSerializer.Serialize(new UnsupportedDto(1), "root", options));
    }

    [Fact]
    public void UnmappedRequiredMembersAreRejectedByReflection()
    {
        var options = ReflectionOptions();
        var unmapped = new UnmappedRequiredDto { Ignored = "", Value = 1 };

        Assert.Equal(1, unmapped.Value);
        Assert.Throws<NotSupportedException>(() =>
            NbtSerializer.Serialize(unmapped, "root", options));
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void ConstructorInitializedRequiredMembersRoundTrip(SerializationMode mode)
    {
        var path = CreatePath(mode);
        var value = new InitializedRequiredDto { Value = 2 };

        var copy = path.Deserialize<InitializedRequiredDto>(path.Serialize(value));

        Assert.Equal(2, copy.Value);
    }

    [Fact]
    public void RefConstructorsAreNotUsable()
    {
        var options = ReflectionOptions();

        Assert.Throws<NotSupportedException>(() =>
            options.GetConverter(typeof(RefConstructorDto)));
    }

    [Fact]
    public void ReflectionResolverLeavesBuiltInTypesToTheBuiltInConverters()
    {
        var options = ReflectionOptions();
        var resolver = options.TypeInfoResolver!;

        Assert.Null(resolver.GetTypeInfo(typeof(int?), options));
        Assert.Null(resolver.GetTypeInfo(typeof(NbtValue), options));
        Assert.Null(resolver.GetTypeInfo(typeof(NbtList), options));
        Assert.Null(resolver.GetTypeInfo(typeof(NbtCompound), options));
        Assert.Null(resolver.GetTypeInfo(typeof(NbtEndList), options));
    }

    private static SerializationTestPath CreatePath(
        SerializationMode mode,
        bool rejectDuplicateCompoundNames = false,
        NbtNullValueHandling nullValueHandling = NbtNullValueHandling.Omit)
    {
        if (mode == SerializationMode.Reflection)
        {
            return SerializationTestPath.Reflection(ReflectionOptions(
                rejectDuplicateCompoundNames,
                nullValueHandling));
        }

        return SerializationTestPath.Generated(new ObjectMappingContext(new NbtSerializerOptions
        {
            RejectDuplicateCompoundNames = rejectDuplicateCompoundNames,
            NullValueHandling = nullValueHandling,
        }));
    }

    private static NbtSerializerOptions ReflectionOptions(
        bool rejectDuplicateCompoundNames = false,
        NbtNullValueHandling nullValueHandling = NbtNullValueHandling.Omit) => new()
        {
            TypeInfoResolver = new NbtReflectionTypeInfoResolver(),
            RejectDuplicateCompoundNames = rejectDuplicateCompoundNames,
            NullValueHandling = nullValueHandling,
        };

    internal sealed class Player
    {
        public string Name { get; init; } = "";

        public int Health { get; init; }

        public List<Item> Items { get; init; } = [];
    }

    internal sealed class Item
    {
        public string Id { get; init; } = "";

        public int Count { get; init; }
    }

    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Local")]
    internal sealed class RenamedDto
    {
        [NbtPropertyName("n")]
        public int Value { get; init; }

        [NbtIgnore]
        public int Ignored { get; set; }
    }

    internal sealed class FieldDto
    {
        [NbtInclude]
        public int Value;
    }

    internal sealed class ImmutableDto
    {
        public ImmutableDto(int value, string name)
        {
            Value = value;
            Name = name;
        }

        public int Value { get; }

        public string Name { get; }
    }

    internal sealed record PointRecord(int X, int Y);

    internal sealed record RenamedRecord([property: NbtPropertyName("v")] int Value);

    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Local")]
    internal sealed class SimpleDto
    {
        public int Health { get; set; }

        public int X { get; set; }

        public string Name { get; set; } = "";
    }

    internal sealed class NullableDto
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

    private sealed class UnmappedRequiredDto
    {
        [NbtIgnore]
        // ReSharper disable once UnusedAutoPropertyAccessor.Local
        public required string Ignored { get; init; }

        public int Value { get; set; }
    }

    internal sealed class InitializedRequiredDto
    {
        [SetsRequiredMembers]
        public InitializedRequiredDto()
        {
            Ignored = "";
        }

        [NbtIgnore]
        // ReSharper disable once UnusedAutoPropertyAccessor.Local
        public required string Ignored { get; init; }

        public int Value { get; set; }
    }

    private sealed class RefConstructorDto
    {
        public RefConstructorDto(ref int value)
        {
            _ = value;
        }
    }
}

[NbtSerializable(typeof(NbtObjectMappingTests.Player))]
[NbtSerializable(typeof(NbtObjectMappingTests.RenamedDto))]
[NbtSerializable(typeof(NbtObjectMappingTests.FieldDto))]
[NbtSerializable(typeof(NbtObjectMappingTests.ImmutableDto))]
[NbtSerializable(typeof(NbtObjectMappingTests.PointRecord))]
[NbtSerializable(typeof(NbtObjectMappingTests.RenamedRecord))]
[NbtSerializable(typeof(NbtObjectMappingTests.SimpleDto))]
[NbtSerializable(typeof(NbtObjectMappingTests.NullableDto))]
[NbtSerializable(typeof(NbtObjectMappingTests.InitializedRequiredDto))]
internal partial class ObjectMappingContext : NbtSerializerContext
{
    public ObjectMappingContext()
    {
    }

    public ObjectMappingContext(NbtSerializerOptions options)
        : base(options)
    {
    }
}

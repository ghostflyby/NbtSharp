using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.CompilerServices;

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
    public async Task RoundTripsSimpleDtoAsynchronously()
    {
        var value = new AotSimple
        {
            Name = "Steve",
            Health = 20,
            Enabled = true,
            Kind = AotKind.Creeper,
        };
        using var stream = new MemoryStream();

        await NbtSerializer.SerializeAsync(
            stream,
            value,
            AotContext.Default.AotSimple,
            cancellationToken: TestContext.Current.CancellationToken);
        stream.Position = 0;
        var copy = NbtSerializer.Deserialize(stream, AotContext.Default.AotSimple);

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
    public void RoundTripsAdditionalCollectionShapes()
    {
        var value = new AotCollectionShapes
        {
            Queue = new Queue<int>([1, 2]),
            Stack = new Stack<int>([1, 2]),
            Set = new HashSet<string> { "a", "b" },
            ListInterface = new List<int> { 3, 4 },
            ReadOnly = new ReadOnlyCollection<int>([5, 6]),
            ImmutableArray = [7, 8],
            ImmutableList = ImmutableList.Create(9, 10),
            ImmutableSet = ImmutableHashSet.Create("c", "d"),
            ReadOnlyDictionary = new ReadOnlyDictionary<string, int>(
                new Dictionary<string, int> { ["a"] = 1 }),
            ImmutableDictionary = ImmutableDictionary.CreateRange(
                new Dictionary<string, int> { ["b"] = 2 }),
            FrozenSet = new HashSet<int> { 11, 12 }.ToFrozenSet(),
            FrozenDictionary = new Dictionary<string, int> { ["c"] = 3 }.ToFrozenDictionary(),
            ReadOnlyObservable = new ReadOnlyObservableCollection<int>(
                new ObservableCollection<int> { 13, 14 }),
            SortedStrings = new SortedSet<string>(StringComparer.Ordinal) { "a", "B" },
            ImmutableSortedStrings = ImmutableSortedSet.Create(StringComparer.Ordinal, "a", "B"),
        };

        var data = NbtSerializer.Serialize(
            value,
            AotContext.Default.AotCollectionShapes,
            "root");
        var copy = NbtSerializer.Deserialize(data, AotContext.Default.AotCollectionShapes);

        Assert.Equal([1, 2], copy.Queue.ToArray());
        Assert.Equal([2, 1], copy.Stack.ToArray());
        Assert.True(copy.Set.SetEquals(["a", "b"]));
        Assert.Equal([3, 4], copy.ListInterface.ToArray());
        Assert.Equal([5, 6], copy.ReadOnly.ToArray());
        Assert.Equal([7, 8], copy.ImmutableArray.ToArray());
        Assert.Equal([9, 10], copy.ImmutableList.ToArray());
        Assert.True(copy.ImmutableSet.SetEquals(["c", "d"]));
        Assert.Equal(1, copy.ReadOnlyDictionary["a"]);
        Assert.Equal(2, copy.ImmutableDictionary["b"]);
        Assert.True(copy.FrozenSet.SetEquals([11, 12]));
        Assert.Equal(3, copy.FrozenDictionary["c"]);
        Assert.Equal([13, 14], copy.ReadOnlyObservable.ToArray());
        Assert.Equal(["B", "a"], copy.SortedStrings.ToArray());
        Assert.Equal(["B", "a"], copy.ImmutableSortedStrings.ToArray());
        Assert.Same(StringComparer.Ordinal, copy.SortedStrings.Comparer);
        Assert.Same(StringComparer.Ordinal, copy.ImmutableSortedStrings.KeyComparer);
    }

    [Fact]
    public void RoundTripsUnsignedAndCharacterScalars()
    {
        var value = new AotUnsignedScalars
        {
            UShort = ushort.MaxValue,
            UInt = uint.MaxValue,
            ULong = ulong.MaxValue,
            Character = '你',
            OptionalUShort = 3,
            OptionalUInt = 4,
            OptionalULong = 5,
            OptionalCharacter = '\n',
            UShorts = [0, 1],
            UInts = [0, 1],
            ULongs = [0, 1],
            Characters = ['a', '你'],
        };

        var data = NbtSerializer.Serialize(
            value,
            AotContext.Default.AotUnsignedScalars,
            "root");
        var copy = NbtSerializer.Deserialize(data, AotContext.Default.AotUnsignedScalars);

        Assert.Equal(value.UShort, copy.UShort);
        Assert.Equal(value.UInt, copy.UInt);
        Assert.Equal(value.ULong, copy.ULong);
        Assert.Equal(value.Character, copy.Character);
        Assert.Equal(value.OptionalUShort, copy.OptionalUShort);
        Assert.Equal(value.OptionalUInt, copy.OptionalUInt);
        Assert.Equal(value.OptionalULong, copy.OptionalULong);
        Assert.Equal(value.OptionalCharacter, copy.OptionalCharacter);
        Assert.Equal(value.UShorts, copy.UShorts);
        Assert.Equal(value.UInts, copy.UInts);
        Assert.Equal(value.ULongs, copy.ULongs);
        Assert.Equal(value.Characters, copy.Characters);
    }

    [Fact]
    public void RoundTripsDiscoveredCollectionPatterns()
    {
        var value = new AotDiscoveredCollections
        {
            ConcurrentMap = new ConcurrentDictionary<string, int>(
                new Dictionary<string, int> { ["a"] = 1 }),
            SortedSet = [2, 1],
            ImmutableSortedSet = ImmutableSortedSet.Create(4, 3),
            ImmutableSortedMap = ImmutableSortedDictionary.CreateRange(
                StringComparer.Ordinal,
                new Dictionary<string, int> { ["b"] = 2 }),
            Custom = new AotCustomCollection<int>([5, 6]),
        };

        var data = NbtSerializer.Serialize(
            value,
            AotContext.Default.AotDiscoveredCollections,
            "root");
        var copy = NbtSerializer.Deserialize(data, AotContext.Default.AotDiscoveredCollections);

        Assert.Equal(1, copy.ConcurrentMap["a"]);
        Assert.Equal([1, 2], copy.SortedSet.ToArray());
        Assert.Equal([3, 4], copy.ImmutableSortedSet.ToArray());
        Assert.Equal(2, copy.ImmutableSortedMap["b"]);
        Assert.Equal([5, 6], copy.Custom.ToArray());
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

    [Fact]
    public void RoundTripsStringEnums()
    {
        var value = new AotStringEnums
        {
            Kind = AotMob.Creeper,
            Undefined = (AotMob)200,
            Flags = AotStringFlags.Walk | AotStringFlags.Swim,
        };

        var data = NbtSerializer.Serialize(value, AotContext.Default.AotStringEnums, "root");
        var copy = NbtSerializer.Deserialize(data, AotContext.Default.AotStringEnums);

        var root = (NbtCompound)NbtSerializer.Deserialize(data).Value.Value!;
        Assert.Equal(NbtTagType.String, root["Kind"].TagType);
        Assert.Equal("Creeper", root["Kind"].Value);
        Assert.Equal("200", root["Undefined"].Value);
        Assert.Equal("Walk, Swim", root["Flags"].Value);

        Assert.Equal(value.Kind, copy.Kind);
        Assert.Equal(value.Undefined, copy.Undefined);
        Assert.Equal(value.Flags, copy.Flags);
    }

    [Fact]
    public void RoundTripsPolymorphicMembers()
    {
        var value = new AotPetOwner
        {
            Pet = new AotCat { Name = "Milo", Lives = 9 },
        };

        var data = NbtSerializer.Serialize(value, AotContext.Default.AotPetOwner, "root");
        var copy = NbtSerializer.Deserialize(data, AotContext.Default.AotPetOwner);

        var cat = Assert.IsType<AotCat>(copy.Pet);
        Assert.Equal("Milo", cat.Name);
        Assert.Equal(9, cat.Lives);

        var rockOwner = new AotPetOwner { Pet = new AotRock { Name = "rock" } };
        var rockData = NbtSerializer.Serialize(
            rockOwner,
            AotContext.Default.AotPetOwner,
            "root");
        var rockCopy = NbtSerializer.Deserialize(rockData, AotContext.Default.AotPetOwner);

        var rock = Assert.IsType<AotRock>(rockCopy.Pet);
        Assert.Equal("rock", rock.Name);
    }
}

[NbtDerived(typeof(AotCat))]
[NbtDerived(typeof(AotRock))]
internal interface IAotAnimal
{
    string Name { get; set; }
}

internal sealed class AotCat : IAotAnimal
{
    public string Name { get; set; } = "";

    public int Lives { get; set; }
}

internal struct AotRock : IAotAnimal
{
    public string Name { get; set; }
}

internal sealed class AotPetOwner
{
    public IAotAnimal? Pet { get; set; }
}

[NbtSerializable(typeof(AotSimple))]
[NbtSerializable(typeof(AotRequired))]
[NbtSerializable(typeof(AotEnums))]
[NbtSerializable(typeof(AotStringEnums))]
[NbtSerializable(typeof(AotWrapper<AotSimple>))]
[NbtSerializable(typeof(AotCollections))]
[NbtSerializable(typeof(AotCollectionShapes))]
[NbtSerializable(typeof(AotUnsignedScalars))]
[NbtSerializable(typeof(AotDiscoveredCollections))]
[NbtSerializable(typeof(AotRecord))]
[NbtSerializable(typeof(AotConverters))]
[NbtSerializable(typeof(AotPetOwner))]
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

[NbtStringEnum]
internal enum AotMob : byte
{
    Zombie,
    Creeper,
    Skeleton,
}

[NbtStringEnum]
[Flags]
internal enum AotStringFlags
{
    None = 0,
    Walk = 1,
    Swim = 2,
    Fly = 4,
}

internal sealed class AotStringEnums
{
    public AotMob Kind { get; init; }

    public AotMob Undefined { get; init; }

    public AotStringFlags Flags { get; init; }
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

internal sealed class AotCollectionShapes
{
    public Queue<int> Queue { get; init; } = new();

    public Stack<int> Stack { get; init; } = new();

    public HashSet<string> Set { get; init; } = [];

    public IList<int> ListInterface { get; init; } = new List<int>();

    public ReadOnlyCollection<int> ReadOnly { get; init; } = new(new List<int>());

    public ImmutableArray<int> ImmutableArray { get; init; }

    public ImmutableList<int> ImmutableList { get; init; } = ImmutableList<int>.Empty;

    public ImmutableHashSet<string> ImmutableSet { get; init; } = ImmutableHashSet<string>.Empty;

    public ReadOnlyDictionary<string, int> ReadOnlyDictionary { get; init; } =
        new(new Dictionary<string, int>());

    public ImmutableDictionary<string, int> ImmutableDictionary { get; init; } =
        ImmutableDictionary<string, int>.Empty;

    public FrozenSet<int> FrozenSet { get; init; } = new HashSet<int>().ToFrozenSet();

    public FrozenDictionary<string, int> FrozenDictionary { get; init; } =
        new Dictionary<string, int>().ToFrozenDictionary();

    public ReadOnlyObservableCollection<int> ReadOnlyObservable { get; init; } =
        new(new ObservableCollection<int>());

    public SortedSet<string> SortedStrings { get; init; } = new(StringComparer.Ordinal);

    public ImmutableSortedSet<string> ImmutableSortedStrings { get; init; } =
        ImmutableSortedSet.Create<string>(StringComparer.Ordinal);
}

internal sealed class AotUnsignedScalars
{
    public ushort UShort { get; init; }

    public uint UInt { get; init; }

    public ulong ULong { get; init; }

    public char Character { get; init; }

    public ushort? OptionalUShort { get; init; }

    public uint? OptionalUInt { get; init; }

    public ulong? OptionalULong { get; init; }

    public char? OptionalCharacter { get; init; }

    public ushort[] UShorts { get; init; } = [];

    public uint[] UInts { get; init; } = [];

    public ulong[] ULongs { get; init; } = [];

    public char[] Characters { get; init; } = [];
}

internal sealed class AotDiscoveredCollections
{
    public ConcurrentDictionary<string, int> ConcurrentMap { get; init; } =
        new(StringComparer.Ordinal);

    public SortedSet<int> SortedSet { get; init; } = [];

    public ImmutableSortedSet<int> ImmutableSortedSet { get; init; } =
        ImmutableSortedSet<int>.Empty;

    public ImmutableSortedDictionary<string, int> ImmutableSortedMap { get; init; } =
        ImmutableSortedDictionary<string, int>.Empty;

    public AotCustomCollection<int> Custom { get; init; } = new([]);
}

[CollectionBuilder(typeof(AotCustomCollectionFactory), "Create")]
internal sealed class AotCustomCollection<T> : IReadOnlyList<T>
{
    private readonly T[] _items;

    public AotCustomCollection(T[] items) => _items = items;

    public T this[int index] => _items[index];

    public int Count => _items.Length;

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
}

internal static class AotCustomCollectionFactory
{
    public static AotCustomCollection<T> Create<T>(ReadOnlySpan<T> items) =>
        new(items.ToArray());
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
    public override decimal Read(ref NbtReader reader, NbtSerializerOptions options) =>
        decimal.Parse(reader.GetString(), CultureInfo.InvariantCulture);

    public override void Write(ref NbtWriter writer, decimal value, NbtSerializerOptions options) =>
        writer.WriteString(value.ToString(CultureInfo.InvariantCulture));

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

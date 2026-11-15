using System.Buffers;

namespace NbtSharp.Tests;

public sealed class NbtSourceGenerationTests
{
    [Fact]
    public void GeneratesTypeInformationAndRoundTripsObjects()
    {
        var context = SourceGenerationContext.Default;
        var player = new SourceGenPlayer
        {
            Name = "Steve",
            Health = 20,
            Items =
            [
                new SourceGenItem { Id = "apple", Count = 3 },
                new SourceGenItem { Id = "sword", Count = 1 },
            ],
        };

        var data = NbtSerializer.Serialize(player, context.SourceGenPlayer, "root");
        var actual = NbtSerializer.Deserialize(data, context.SourceGenPlayer);

        Assert.Equal("Steve", actual.Name);
        Assert.Equal(20, actual.Health);
        Assert.Equal(2, actual.Items.Count);
        Assert.Equal("apple", actual.Items[0].Id);
        Assert.Equal(3, actual.Items[0].Count);
        Assert.Equal("sword", actual.Items[1].Id);
    }

    [Fact]
    public void MatchesReflectionSerialization()
    {
        var context = SourceGenerationContext.Default;
        var reflectionOptions = new NbtSerializerOptions
        {
            TypeInfoResolver = new NbtReflectionTypeInfoResolver(),
        };
        var player = new SourceGenPlayer
        {
            Name = "Alex",
            Health = 12,
            Items = [new SourceGenItem { Id = "torch", Count = 8 }],
        };

        var generated = NbtSerializer.Serialize(player, context.SourceGenPlayer, "root");
        var reflected = NbtSerializer.Serialize(player, "root", reflectionOptions);

        Assert.Equal(reflected, generated);
    }

    [Fact]
    public void AppliesNameAndIgnoreAttributes()
    {
        var context = SourceGenerationContext.Default;
        var value = new SourceGenRenamed { Value = 7, Ignored = 9 };

        var data = NbtSerializer.Serialize(value, context.SourceGenRenamed, "root");
        var copy = NbtSerializer.Deserialize(data, context.SourceGenRenamed);
        var root = NbtSerializer.Deserialize(data);
        var compound = (NbtCompound)root.Value.Value!;

        Assert.True(compound.ContainsKey("n"));
        Assert.False(compound.ContainsKey("Value"));
        Assert.False(compound.ContainsKey("Ignored"));
        Assert.Equal(7, copy.Value);
    }

    [Fact]
    public void IncludesOptedInFields()
    {
        var context = SourceGenerationContext.Default;
        var value = new SourceGenFields { Value = 42 };

        var copy = NbtSerializer.Deserialize(
            NbtSerializer.Serialize(value, context.SourceGenFields, "root"),
            context.SourceGenFields);

        Assert.Equal(42, copy.Value);
    }

    [Fact]
    public void BindsConstructors()
    {
        var context = SourceGenerationContext.Default;
        var value = new SourceGenImmutable(7, "seven");

        var copy = NbtSerializer.Deserialize(
            NbtSerializer.Serialize(value, context.SourceGenImmutable, "root"),
            context.SourceGenImmutable);

        Assert.Equal(7, copy.Value);
        Assert.Equal("seven", copy.Name);
    }

    [Fact]
    public void BindsPositionalRecords()
    {
        var context = SourceGenerationContext.Default;
        var value = new SourceGenRecord(3, 4);

        var copy = NbtSerializer.Deserialize(
            NbtSerializer.Serialize(value, context.SourceGenRecord, "root"),
            context.SourceGenRecord);

        Assert.Equal(3, copy.X);
        Assert.Equal(4, copy.Y);
    }

    [Fact]
    public void OmittedNullMembersStayNull()
    {
        var context = SourceGenerationContext.Default;
        var value = new SourceGenNullable { Required = 1, Optional = null };

        var data = NbtSerializer.Serialize(value, context.SourceGenNullable, "root");
        var root = (NbtCompound)NbtSerializer.Deserialize(data).Value.Value!;
        var copy = NbtSerializer.Deserialize(data, context.SourceGenNullable);

        Assert.False(root.ContainsKey("Optional"));
        Assert.Equal(1, copy.Required);
        Assert.Null(copy.Optional);
    }

    [Fact]
    public void SkipsUnknownMembersAndLeavesMissingMembersAtTheirDefaults()
    {
        var context = SourceGenerationContext.Default;
        var root = new NbtRoot("root", new NbtCompound
        {
            ["Health"] = 20,
            ["Extra"] = "ignored",
        });

        var data = NbtSerializer.Serialize(root);
        var copy = NbtSerializer.Deserialize(data, context.SourceGenDefaults);

        Assert.Equal(20, copy.Health);
        Assert.Equal("", copy.Name);
    }

    [Fact]
    public void SupportsRequiredMembers()
    {
        var context = SourceGenerationContext.Default;
        var value = new SourceGenRequired { Name = "required", Value = 3 };

        var data = NbtSerializer.Serialize(value, context.SourceGenRequired, "root");
        var copy = NbtSerializer.Deserialize(data, context.SourceGenRequired);

        Assert.Equal("required", copy.Name);
        Assert.Equal(3, copy.Value);
    }

    [Fact]
    public void SupportsCollectionsDictionariesAndNullableValues()
    {
        var context = SourceGenerationContext.Default;
        var value = new SourceGenCollections
        {
            Numbers = [1, 2, 3],
            Names = ["a", "b"],
            Items = [new SourceGenItem { Id = "one", Count = 1 }],
            Values = [4, 5],
            Map = new Dictionary<string, SourceGenItem>
            {
                ["first"] = new() { Id = "first", Count = 10 },
            },
            Counts = new Dictionary<string, int> { ["a"] = 1 },
            ReadOnlyCounts = new Dictionary<string, int> { ["b"] = 2 },
            ReadOnlyNames = ["r"],
            ItemArray = [new SourceGenItem { Id = "array", Count = 4 }],
            OptionalNumber = 7,
            Labels = ["x", "y"],
        };

        var data = NbtSerializer.Serialize(value, context.SourceGenCollections, "root");
        var copy = NbtSerializer.Deserialize(data, context.SourceGenCollections);
        var reflectionOptions = new NbtSerializerOptions
        {
            TypeInfoResolver = new NbtReflectionTypeInfoResolver(),
        };
        var reflected = NbtSerializer.Serialize(value, "root", reflectionOptions);

        Assert.Equal(reflected, data);
        Assert.Equal([1, 2, 3], copy.Numbers);
        Assert.Equal(["a", "b"], copy.Names);
        Assert.Single(copy.Items);
        Assert.Equal("one", copy.Items[0].Id);
        Assert.Equal([4, 5], copy.Values);
        Assert.Equal(10, copy.Map["first"].Count);
        Assert.Equal(1, copy.Counts["a"]);
        Assert.Equal(2, copy.ReadOnlyCounts["b"]);
        Assert.Equal(["r"], copy.ReadOnlyNames);
        Assert.Equal("array", copy.ItemArray[0].Id);
        Assert.Equal(7, copy.OptionalNumber);
        Assert.Equal(["x", "y"], copy.Labels);
    }

    [Fact]
    public void SupportsArrayRootTypes()
    {
        var context = SourceGenerationContext.Default;
        SourceGenItem[] value =
        [
            new SourceGenItem { Id = "one", Count = 1 },
            new SourceGenItem { Id = "two", Count = 2 },
        ];

        var data = NbtSerializer.Serialize(value, context.ArrayOfSourceGenItem, "root");
        var copy = NbtSerializer.Deserialize(data, context.ArrayOfSourceGenItem);

        Assert.Equal(2, copy.Length);
        Assert.Equal("one", copy[0].Id);
        Assert.Equal(2, copy[1].Count);
    }

    [Fact]
    public void SupportsClosedGenericDtoTypes()
    {
        var context = SourceGenerationContext.Default;
        var value = new SourceGenWrapper<SourceGenItem>
        {
            Value = new SourceGenItem { Id = "wrapped", Count = 2 },
            Values = [new SourceGenItem { Id = "many", Count = 3 }],
        };

        var data = NbtSerializer.Serialize(value, context.SourceGenWrapperOfSourceGenItem, "root");
        var copy = NbtSerializer.Deserialize(data, context.SourceGenWrapperOfSourceGenItem);

        Assert.Equal("wrapped", copy.Value.Id);
        Assert.Equal(2, copy.Value.Count);
        Assert.Equal("many", copy.Values[0].Id);
        Assert.Equal(3, copy.Values[0].Count);
    }

    [Fact]
    public void SupportsConstrainedGenericDtoTypes()
    {
        var context = SourceGenerationContext.Default;
        var value = new SourceGenConstrained<SourceGenItem>
        {
            Value = new SourceGenItem { Id = "constrained", Count = 4 },
        };

        var data = NbtSerializer.Serialize(
            value,
            context.SourceGenConstrainedOfSourceGenItem,
            "root");
        var copy = NbtSerializer.Deserialize(
            data,
            context.SourceGenConstrainedOfSourceGenItem);

        Assert.Equal("constrained", copy.Value!.Id);
        Assert.Equal(4, copy.Value.Count);
    }

    [Fact]
    public void SupportsRequiredMembersOnGenericTypes()
    {
        var context = SourceGenerationContext.Default;
        var value = new SourceGenRequiredGeneric<SourceGenItem>
        {
            Value = new SourceGenItem { Id = "required-generic", Count = 5 },
        };

        var data = NbtSerializer.Serialize(
            value,
            context.SourceGenRequiredGenericOfSourceGenItem,
            "root");
        var copy = NbtSerializer.Deserialize(
            data,
            context.SourceGenRequiredGenericOfSourceGenItem);

        Assert.Equal("required-generic", copy.Value.Id);
        Assert.Equal(5, copy.Value.Count);
    }

    [Fact]
    public void SupportsMutableStructs()
    {
        var context = SourceGenerationContext.Default;
        var value = new SourceGenStruct { X = 1, Y = 2, Z = 3 };

        var data = NbtSerializer.Serialize(value, context.SourceGenStruct, "root");
        var copy = NbtSerializer.Deserialize(data, context.SourceGenStruct);
        var reflectionOptions = new NbtSerializerOptions
        {
            TypeInfoResolver = new NbtReflectionTypeInfoResolver(),
        };
        var reflected = NbtSerializer.Serialize(value, "root", reflectionOptions);

        Assert.Equal(reflected, data);
        Assert.Equal(1, copy.X);
        Assert.Equal(2, copy.Y);
        Assert.Equal(3, copy.Z);
    }

    [Fact]
    public void MemberOrderMatchesReflection()
    {
        var context = SourceGenerationContext.Default;
        var value = new SourceGenCollections
        {
            Numbers = [1],
            Names = ["a"],
            Items = [new SourceGenItem { Id = "one", Count = 1 }],
            Values = [4],
            Map = new Dictionary<string, SourceGenItem>
            {
                ["first"] = new() { Id = "first", Count = 10 },
            },
            OptionalNumber = 7,
            Labels = ["x"],
        };
        var reflectionOptions = new NbtSerializerOptions
        {
            TypeInfoResolver = new NbtReflectionTypeInfoResolver(),
        };

        var reflected = (NbtCompound)NbtSerializer
            .Deserialize(NbtSerializer.Serialize(value, "root", reflectionOptions))
            .Value.Value!;
        var generated = (NbtCompound)NbtSerializer
            .Deserialize(NbtSerializer.Serialize(value, context.SourceGenCollections, "root"))
            .Value.Value!;

        Assert.Equal(reflected.Keys.ToArray(), generated.Keys.ToArray());
        foreach (var key in reflected.Keys)
        {
            Assert.True(
                reflected[key].TagType == generated[key].TagType,
                $"{key}: expected {reflected[key].TagType}, actual {generated[key].TagType}");
        }
    }

    [Fact]
    public async Task SupportsAsyncTypeInfoOverloads()
    {
        var context = SourceGenerationContext.Default;
        var value = new SourceGenItem { Id = "async", Count = 9 };
        var data = NbtSerializer.Serialize(value, context.SourceGenItem, "root");

        var fromSequence = await NbtSerializer.DeserializeAsync(
            new ReadOnlySequence<byte>(data),
            context.SourceGenItem,
            TestContext.Current.CancellationToken);
        using var stream = new MemoryStream(data);
        var fromStream = await NbtSerializer.DeserializeAsync(
            stream,
            context.SourceGenItem,
            TestContext.Current.CancellationToken);

        Assert.Equal("async", fromSequence.Id);
        Assert.Equal(9, fromSequence.Count);
        Assert.Equal("async", fromStream.Id);
        Assert.Equal(9, fromStream.Count);
    }

    [Fact]
    public void SupportsStreamAndSequenceTypeInfoOverloads()
    {
        var context = SourceGenerationContext.Default;
        var value = new SourceGenItem { Id = "stream", Count = 5 };

        using var stream = new MemoryStream();
        NbtSerializer.Serialize(stream, value, context.SourceGenItem, "root");
        stream.Position = 0;
        var fromStream = NbtSerializer.Deserialize(stream, context.SourceGenItem);

        var data = NbtSerializer.Serialize(value, context.SourceGenItem, "root");
        var fromSequence = NbtSerializer.Deserialize(new ReadOnlySequence<byte>(data), context.SourceGenItem);

        Assert.Equal("stream", fromStream.Id);
        Assert.Equal(5, fromStream.Count);
        Assert.Equal("stream", fromSequence.Id);
        Assert.Equal(5, fromSequence.Count);
    }

    [Fact]
    public void SupportsInheritedMembersAndOverriddenAttributes()
    {
        var context = SourceGenerationContext.Default;
        var value = new SourceGenDerived
        {
            BaseValue = 1,
            DerivedValue = 2,
            Ignored = 3,
        };
        var reflectionOptions = new NbtSerializerOptions
        {
            TypeInfoResolver = new NbtReflectionTypeInfoResolver(),
        };

        var data = NbtSerializer.Serialize(value, context.SourceGenDerived, "root");
        var reflected = NbtSerializer.Serialize(value, "root", reflectionOptions);
        var copy = NbtSerializer.Deserialize(data, context.SourceGenDerived);

        Assert.Equal(reflected, data);
        Assert.Equal(1, copy.BaseValue);
        Assert.Equal(2, copy.DerivedValue);
    }

    [Fact]
    public void SupportsScalarFastPaths()
    {
        var context = SourceGenerationContext.Default;
        var value = new SourceGenScalars
        {
            SByte = -12,
            Byte = 250,
            Int16 = -32000,
            Int32 = -123456,
            Int64 = long.MinValue + 1,
            Single = 1.25f,
            Double = -2.5,
            Boolean = true,
            Text = "text",
        };
        var reflectionOptions = new NbtSerializerOptions
        {
            TypeInfoResolver = new NbtReflectionTypeInfoResolver(),
        };

        var data = NbtSerializer.Serialize(value, context.SourceGenScalars, "root");
        var reflected = NbtSerializer.Serialize(value, "root", reflectionOptions);
        var copy = NbtSerializer.Deserialize(data, context.SourceGenScalars);

        Assert.Equal(reflected, data);
        Assert.Equal(-12, copy.SByte);
        Assert.Equal(250, copy.Byte);
        Assert.Equal(-32000, copy.Int16);
        Assert.Equal(-123456, copy.Int32);
        Assert.Equal(long.MinValue + 1, copy.Int64);
        Assert.Equal(1.25f, copy.Single);
        Assert.Equal(-2.5, copy.Double);
        Assert.True(copy.Boolean);
        Assert.Equal("text", copy.Text);
    }

    [Fact]
    public void CustomScalarConvertersDisableGeneratedFastPath()
    {
        var options = new NbtSerializerOptions();
        options.Converters.Add(new PlusOneInt32Converter());
        var context = new SourceGenerationContext(options);
        var value = new SourceGenDefaults { Health = 5 };

        var data = NbtSerializer.Serialize(value, context.SourceGenDefaults, "root");
        var root = (NbtCompound)NbtSerializer.Deserialize(data).Value.Value!;
        var copy = NbtSerializer.Deserialize(data, context.SourceGenDefaults);

        Assert.Equal(6, root["Health"] is int stored ? stored : 0);
        Assert.Equal(5, copy.Health);
    }

    [Fact]
    public void DirectScalarReadsKeepMemberErrorPaths()
    {
        var context = SourceGenerationContext.Default;
        var data = NbtSerializer.Serialize(new NbtRoot(
            "root",
            new NbtCompound { ["Health"] = "wrong" }));
        var reflectionOptions = new NbtSerializerOptions
        {
            TypeInfoResolver = new NbtReflectionTypeInfoResolver(),
        };

        var generated = Assert.Throws<NbtException>(() =>
            NbtSerializer.Deserialize(data, context.SourceGenDefaults));
        var reflected = Assert.Throws<NbtException>(() =>
            NbtSerializer.Deserialize<SourceGenDefaults>(data, reflectionOptions));

        Assert.Equal(reflected.Path, generated.Path);
        Assert.Equal(reflected.ExpectedTagType, generated.ExpectedTagType);
        Assert.Equal(reflected.ActualTagType, generated.ActualTagType);
    }

    [Fact]
    public void RequiredMembersCannotBeNull()
    {
        var context = SourceGenerationContext.Default;
        var value = new SourceGenRequiredNullable { Name = null };
        var reflectionOptions = new NbtSerializerOptions
        {
            TypeInfoResolver = new NbtReflectionTypeInfoResolver(),
        };

        var generated = Assert.Throws<NbtException>(() =>
            NbtSerializer.Serialize(value, context.SourceGenRequiredNullable, "root"));
        var reflected = Assert.Throws<NbtException>(() =>
            NbtSerializer.Serialize(value, "root", reflectionOptions));

        Assert.Equal(reflected.Message, generated.Message);
        Assert.Equal(reflected.Path, generated.Path);
    }

    [Fact]
    public void PrivateGettersAreNotMapped()
    {
        var context = SourceGenerationContext.Default;
        var value = new SourceGenPrivateGetter { Visible = 1, Hidden = 2 };
        var reflectionOptions = new NbtSerializerOptions
        {
            TypeInfoResolver = new NbtReflectionTypeInfoResolver(),
        };

        var generatedRoot = (NbtCompound)NbtSerializer
            .Deserialize(NbtSerializer.Serialize(value, context.SourceGenPrivateGetter, "root"))
            .Value.Value!;
        var reflectedRoot = (NbtCompound)NbtSerializer
            .Deserialize(NbtSerializer.Serialize(value, "root", reflectionOptions))
            .Value.Value!;

        Assert.True(generatedRoot.ContainsKey("Visible"));
        Assert.False(generatedRoot.ContainsKey("Hidden"));
        Assert.True(reflectedRoot.ContainsKey("Visible"));
        Assert.False(reflectedRoot.ContainsKey("Hidden"));
    }

    [Fact]
    public void MissingRequiredMembersThrow()
    {
        var context = SourceGenerationContext.Default;
        var data = NbtSerializer.Serialize(new NbtRoot(
            "root",
            new NbtCompound { ["Name"] = "only-name" }));
        var reflectionOptions = new NbtSerializerOptions
        {
            TypeInfoResolver = new NbtReflectionTypeInfoResolver(),
        };

        var generated = Assert.Throws<NbtException>(() =>
            NbtSerializer.Deserialize(data, context.SourceGenRequired));
        var reflected = Assert.Throws<NbtException>(() =>
            NbtSerializer.Deserialize<SourceGenRequired>(data, reflectionOptions));

        Assert.Equal(reflected.Message, generated.Message);
        Assert.Equal(reflected.Path, generated.Path);
        Assert.Contains("Value", generated.Path, StringComparison.Ordinal);
    }

    [Fact]
    public void SupportsEnumsWithUnderlyingWidth()
    {
        var context = SourceGenerationContext.Default;
        var value = new SourceGenEnumHolder
        {
            Default = SourceGenDefaultEnum.Two,
            SByte = SourceGenSByteEnum.Max,
            Byte = SourceGenByteEnum.Max,
            Int16 = SourceGenInt16Enum.Minus,
            UInt16 = SourceGenUInt16Enum.Max,
            Int32 = SourceGenInt32Enum.Minus,
            UInt32 = SourceGenUInt32Enum.Max,
            Int64 = SourceGenInt64Enum.Minus,
            UInt64 = SourceGenUInt64Enum.Max,
            Optional = SourceGenDefaultEnum.One,
            Values = [SourceGenDefaultEnum.Zero, SourceGenDefaultEnum.Two],
            EnumArray = [SourceGenDefaultEnum.One, (SourceGenDefaultEnum)42],
            EnumMap = new Dictionary<string, SourceGenDefaultEnum>
            {
                ["one"] = SourceGenDefaultEnum.One,
            },
            Undefined = (SourceGenDefaultEnum)42,
        };
        var reflectionOptions = new NbtSerializerOptions
        {
            TypeInfoResolver = new NbtReflectionTypeInfoResolver(),
        };

        var data = NbtSerializer.Serialize(value, context.SourceGenEnumHolder, "root");
        var reflected = NbtSerializer.Serialize(value, "root", reflectionOptions);
        var copy = NbtSerializer.Deserialize(data, context.SourceGenEnumHolder);
        var compound = (NbtCompound)NbtSerializer.Deserialize(data).Value.Value!;

        Assert.Equal(reflected, data);
        Assert.Equal(SourceGenDefaultEnum.Two, copy.Default);
        Assert.Equal(SourceGenSByteEnum.Max, copy.SByte);
        Assert.Equal(SourceGenByteEnum.Max, copy.Byte);
        Assert.Equal(SourceGenInt16Enum.Minus, copy.Int16);
        Assert.Equal(SourceGenUInt16Enum.Max, copy.UInt16);
        Assert.Equal(SourceGenInt32Enum.Minus, copy.Int32);
        Assert.Equal(SourceGenUInt32Enum.Max, copy.UInt32);
        Assert.Equal(SourceGenInt64Enum.Minus, copy.Int64);
        Assert.Equal(SourceGenUInt64Enum.Max, copy.UInt64);
        Assert.Equal(SourceGenDefaultEnum.One, copy.Optional);
        Assert.Equal(value.Values, copy.Values);
        Assert.Equal(value.EnumArray, copy.EnumArray);
        Assert.Equal(SourceGenDefaultEnum.One, copy.EnumMap["one"]);
        Assert.Equal((SourceGenDefaultEnum)42, copy.Undefined);
        Assert.Equal(NbtTagType.Byte, compound["SByte"].TagType);
        Assert.Equal(NbtTagType.Byte, compound["Byte"].TagType);
        Assert.Equal(NbtTagType.Short, compound["Int16"].TagType);
        Assert.Equal(NbtTagType.Short, compound["UInt16"].TagType);
        Assert.Equal(NbtTagType.Int, compound["Int32"].TagType);
        Assert.Equal(NbtTagType.Int, compound["UInt32"].TagType);
        Assert.Equal(NbtTagType.Long, compound["Int64"].TagType);
        Assert.Equal(NbtTagType.Long, compound["UInt64"].TagType);
    }

    [Fact]
    public void SupportsEnumRootTypes()
    {
        var context = SourceGenerationContext.Default;
        var value = SourceGenDefaultEnum.Two;
        var reflectionOptions = new NbtSerializerOptions
        {
            TypeInfoResolver = new NbtReflectionTypeInfoResolver(),
        };

        var data = NbtSerializer.Serialize(value, context.SourceGenDefaultEnum, "root");
        var reflected = NbtSerializer.Serialize(value, "root", reflectionOptions);
        var copy = NbtSerializer.Deserialize(data, context.SourceGenDefaultEnum);
        var root = NbtSerializer.Deserialize(data);

        Assert.Equal(reflected, data);
        Assert.Equal(value, copy);
        Assert.Equal(NbtTagType.Int, root.Value.TagType);
    }

    [Fact]
    public void SupportsExplicitTypeInfoPropertyNames()
    {
        var context = SourceGenerationContext.Default;
        var value = new SourceGenExplicit { Value = 11 };

        var data = NbtSerializer.Serialize(value, context.ExplicitInfo, "root");
        var copy = NbtSerializer.Deserialize(data, context.ExplicitInfo);

        Assert.Equal(11, copy.Value);
    }

    [Fact]
    public void EscapesKeywordMemberNames()
    {
        var context = SourceGenerationContext.Default;
        var value = new SourceGenKeywords { @class = 13 };

        var data = NbtSerializer.Serialize(value, context.SourceGenKeywords, "root");
        var copy = NbtSerializer.Deserialize(data, context.SourceGenKeywords);

        Assert.Equal(13, copy.@class);
    }

    [Fact]
    public void CustomConvertersOverrideGeneratedRootConverters()
    {
        var options = new NbtSerializerOptions();
        options.Converters.Add(new CustomItemConverter());
        var context = new SourceGenerationContext(options);
        var value = new SourceGenItem { Id = "ignored", Count = 1 };

        var data = NbtSerializer.Serialize(value, context.SourceGenItem, "root");
        var copy = NbtSerializer.Deserialize(data, context.SourceGenItem);

        Assert.Equal("custom", copy.Id);
        Assert.Equal(999, copy.Count);
    }

    [Fact]
    public void RejectsDuplicateMembersWithGeneratedConverters()
    {
        var context = new SourceGenerationContext(new NbtSerializerOptions
        {
            RejectDuplicateCompoundNames = true,
        });
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer);
        writer.WriteStartCompound("root");
        writer.WriteInt32("Health", 1);
        writer.WriteInt32("Health", 2);
        writer.WriteEndCompound();
        writer.Complete();

        var exception = Assert.Throws<NbtException>(() =>
            NbtSerializer.Deserialize(buffer.WrittenSpan, context.SourceGenDefaults));

        Assert.Contains("Health", exception.Path, StringComparison.Ordinal);
    }
}

[NbtSerializable(typeof(SourceGenPlayer))]
[NbtSerializable(typeof(SourceGenItem))]
[NbtSerializable(typeof(SourceGenRenamed))]
[NbtSerializable(typeof(SourceGenFields))]
[NbtSerializable(typeof(SourceGenImmutable))]
[NbtSerializable(typeof(SourceGenRecord))]
[NbtSerializable(typeof(SourceGenNullable))]
[NbtSerializable(typeof(SourceGenDefaults))]
[NbtSerializable(typeof(SourceGenRequired))]
[NbtSerializable(typeof(SourceGenCollections))]
[NbtSerializable(typeof(SourceGenStruct))]
[NbtSerializable(typeof(SourceGenWrapper<SourceGenItem>))]
[NbtSerializable(typeof(SourceGenConstrained<SourceGenItem>))]
[NbtSerializable(typeof(SourceGenRequiredGeneric<SourceGenItem>))]
[NbtSerializable(typeof(SourceGenExplicit), TypeInfoPropertyName = "ExplicitInfo")]
[NbtSerializable(typeof(SourceGenKeywords))]
[NbtSerializable(typeof(SourceGenDerived))]
[NbtSerializable(typeof(SourceGenItem[]))]
[NbtSerializable(typeof(SourceGenScalars))]
[NbtSerializable(typeof(SourceGenPrivateGetter))]
[NbtSerializable(typeof(SourceGenRequiredNullable))]
[NbtSerializable(typeof(SourceGenDefaultEnum))]
[NbtSerializable(typeof(SourceGenEnumHolder))]
internal partial class SourceGenerationContext : NbtSerializerContext
{
    public SourceGenerationContext()
    {
    }

    public SourceGenerationContext(NbtSerializerOptions options)
        : base(options)
    {
    }
}

internal sealed class SourceGenPlayer
{
    public string Name { get; init; } = "";

    public int Health { get; init; }

    public List<SourceGenItem> Items { get; init; } = [];
}

internal sealed class SourceGenItem
{
    public string Id { get; init; } = "";

    public int Count { get; init; }
}

internal sealed class SourceGenRenamed
{
    [NbtPropertyName("n")]
    public int Value { get; init; }

    [NbtIgnore]
    public int Ignored { get; set; }
}

internal sealed class SourceGenFields
{
    [NbtInclude]
    public int Value;
}

internal sealed class SourceGenImmutable
{
    public SourceGenImmutable(int value, string name)
    {
        Value = value;
        Name = name;
    }

    public int Value { get; }

    public string Name { get; }
}

internal sealed record SourceGenRecord(int X, int Y);

internal sealed class SourceGenNullable
{
    public int Required { get; init; }

    public string? Optional { get; init; }
}

internal sealed class SourceGenDefaults
{
    public int Health { get; set; }

    public string Name { get; set; } = "";
}

internal sealed class SourceGenRequired
{
    public required string Name { get; init; }

    public required int Value { get; init; }
}

internal sealed class SourceGenCollections
{
    public int[] Numbers { get; init; } = [];

    public List<string> Names { get; init; } = [];

    public IReadOnlyList<SourceGenItem> Items { get; init; } = [];

    public IReadOnlyCollection<int> Values { get; init; } = [];

    public Dictionary<string, SourceGenItem> Map { get; init; } = [];

    public IDictionary<string, int> Counts { get; init; } = new Dictionary<string, int>();

    public IReadOnlyDictionary<string, int> ReadOnlyCounts { get; init; } =
        new Dictionary<string, int>();

    public IReadOnlyList<string> ReadOnlyNames { get; init; } = [];

    public SourceGenItem[] ItemArray { get; init; } = [];

    public int? OptionalNumber { get; init; }

    public string[] Labels { get; init; } = [];
}

internal sealed class SourceGenWrapper<T>
{
    public T Value { get; init; } = default!;

    public List<T> Values { get; init; } = [];
}

internal sealed class SourceGenConstrained<T>
    where T : class, new()
{
    public T? Value { get; init; }
}

internal sealed class SourceGenRequiredGeneric<T>
    where T : class, new()
{
    public required T Value { get; init; }
}

internal sealed class SourceGenExplicit
{
    public int Value { get; set; }
}

#pragma warning disable IDE1006 // Intentionally tests a C# keyword member name.
internal sealed class SourceGenKeywords
{
    public int @class { get; set; }
}

internal enum SourceGenDefaultEnum
{
    Zero,
    One,
    Two,
}

internal enum SourceGenSByteEnum : sbyte
{
    Minus = -1,
    Max = sbyte.MaxValue,
}

internal enum SourceGenByteEnum : byte
{
    One = 1,
    Max = byte.MaxValue,
}

internal enum SourceGenInt16Enum : short
{
    Minus = -1,
    Max = short.MaxValue,
}

internal enum SourceGenUInt16Enum : ushort
{
    One = 1,
    Max = ushort.MaxValue,
}

internal enum SourceGenInt32Enum : int
{
    Minus = -1,
    Max = int.MaxValue,
}

internal enum SourceGenUInt32Enum : uint
{
    One = 1,
    Max = uint.MaxValue,
}

internal enum SourceGenInt64Enum : long
{
    Minus = -1,
    Max = long.MaxValue,
}

internal enum SourceGenUInt64Enum : ulong
{
    One = 1,
    Max = ulong.MaxValue,
}

internal sealed class SourceGenEnumHolder
{
    public SourceGenDefaultEnum Default { get; set; }

    public SourceGenSByteEnum SByte { get; set; }

    public SourceGenByteEnum Byte { get; set; }

    public SourceGenInt16Enum Int16 { get; set; }

    public SourceGenUInt16Enum UInt16 { get; set; }

    public SourceGenInt32Enum Int32 { get; set; }

    public SourceGenUInt32Enum UInt32 { get; set; }

    public SourceGenInt64Enum Int64 { get; set; }

    public SourceGenUInt64Enum UInt64 { get; set; }

    public SourceGenDefaultEnum? Optional { get; set; }

    public List<SourceGenDefaultEnum> Values { get; set; } = [];

    public SourceGenDefaultEnum[] EnumArray { get; set; } = [];

    public Dictionary<string, SourceGenDefaultEnum> EnumMap { get; set; } = [];

    public SourceGenDefaultEnum Undefined { get; set; }
}
#pragma warning restore IDE1006

internal sealed class SourceGenScalars
{
    public sbyte SByte { get; set; }

    public byte Byte { get; set; }

    public short Int16 { get; set; }

    public int Int32 { get; set; }

    public long Int64 { get; set; }

    public float Single { get; set; }

    public double Double { get; set; }

    public bool Boolean { get; set; }

    public string Text { get; set; } = "";
}

internal sealed class SourceGenPrivateGetter
{
    public int Visible { get; set; }

    public int Hidden { private get; set; }
}

internal sealed class SourceGenRequiredNullable
{
    public required string? Name { get; init; }
}

internal sealed class PlusOneInt32Converter : NbtConverter<int>
{
    public override int Read(ref NbtReader reader, NbtSerializerOptions options) =>
        reader.GetInt32() - 1;

    public override void Write(
        ref NbtWriter writer,
        int value,
        NbtSerializerOptions options) =>
        writer.WriteInt32(value + 1);

    public override bool TryGetTagType(
        int value,
        NbtSerializerOptions options,
        out NbtTagType tagType)
    {
        tagType = NbtTagType.Int;
        return true;
    }
}

internal class SourceGenBase
{
    public int BaseValue { get; init; }

    [NbtIgnore]
    public virtual int Ignored { get; set; }
}

internal sealed class SourceGenDerived : SourceGenBase
{
    public int DerivedValue { get; init; }

    [NbtIgnore]
    public override int Ignored { get; set; }
}

internal struct SourceGenStruct
{
    public int X { get; set; }

    public int Y { get; init; }

    [NbtInclude]
    public int Z;
}

internal sealed class CustomItemConverter : NbtConverter<SourceGenItem>
{
    public override SourceGenItem Read(ref NbtReader reader, NbtSerializerOptions options) =>
        new() { Id = "custom", Count = reader.GetInt32() };

    public override void Write(
        ref NbtWriter writer,
        SourceGenItem value,
        NbtSerializerOptions options) =>
        writer.WriteInt32(999);

    public override bool TryGetTagType(
        SourceGenItem value,
        NbtSerializerOptions options,
        out NbtTagType tagType)
    {
        tagType = NbtTagType.Int;
        return true;
    }
}

namespace NbtSharp.Tests;

public sealed class NbtStringEnumTests
{
    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void DefinedValuesRoundTrip(SerializationMode mode)
    {
        var path = CreatePath(mode);
        var value = new StringEnumDto
        {
            Kind = StringEnumKind.Creeper,
            Optional = StringEnumKind.Zombie,
            Flags = StringEnumFlags.Alpha | StringEnumFlags.Gamma,
            Values = [StringByteEnum.One],
            Map = new Dictionary<string, StringByteEnum>
            {
                ["defined"] = StringByteEnum.One,
            },
            Aliased = StringAliasEnum.B,
            Negative = (StringSByteEnum)(-1),
            Wide = (StringLongEnum)(-5_000_000_000L),
        };

        var data = path.Serialize(value);
        var copy = path.Deserialize<StringEnumDto>(data);

        Assert.Equal(value.Kind, copy.Kind);
        Assert.Equal(value.Optional, copy.Optional);
        Assert.Equal(value.Flags, copy.Flags);
        Assert.Equal(value.Values, copy.Values);
        Assert.Equal(value.Map, copy.Map);
        Assert.Equal(StringAliasEnum.A, copy.Aliased);
        Assert.Equal(value.Negative, copy.Negative);
        Assert.Equal(value.Wide, copy.Wide);
    }

    [Fact]
    public void BothPathsProduceTheSameBytes()
    {
        var value = new StringEnumDto
        {
            Kind = StringEnumKind.Skeleton,
            Optional = StringEnumKind.Zombie,
            Flags = StringEnumFlags.Alpha | StringEnumFlags.Beta | StringEnumFlags.Gamma,
            Values = [StringByteEnum.Zero, (StringByteEnum)200],
            Map = new Dictionary<string, StringByteEnum>
            {
                ["defined"] = StringByteEnum.One,
                ["undefined"] = (StringByteEnum)200,
            },
            Aliased = StringAliasEnum.A,
            Negative = (StringSByteEnum)(-1),
            Wide = (StringLongEnum)(-5_000_000_000L),
        };

        Assert.Equal(
            CreatePath(SerializationMode.Reflection).Serialize(value),
            CreatePath(SerializationMode.Generated).Serialize(value));
        Assert.Equal(
            CreatePath(SerializationMode.Reflection).Serialize(StringEnumKind.Creeper),
            CreatePath(SerializationMode.Generated).Serialize(StringEnumKind.Creeper));
        Assert.Equal(
            CreatePath(SerializationMode.Reflection).Serialize(StringEnumFlags.Alpha | StringEnumFlags.Gamma),
            CreatePath(SerializationMode.Generated).Serialize(StringEnumFlags.Alpha | StringEnumFlags.Gamma));
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void MembersUseTheStringTag(SerializationMode mode)
    {
        var path = CreatePath(mode);
        var value = new StringEnumDto
        {
            Kind = StringEnumKind.Zombie,
            Optional = StringEnumKind.Creeper,
            Flags = StringEnumFlags.Beta,
            Values = [StringByteEnum.One],
            Map = new Dictionary<string, StringByteEnum>
            {
                ["defined"] = StringByteEnum.One,
            },
            Aliased = StringAliasEnum.A,
            Negative = (StringSByteEnum)(-1),
            Wide = StringLongEnum.One,
        };

        var compound = RootCompound(path.Serialize(value));

        Assert.Equal(NbtTagType.String, compound["Kind"].TagType);
        Assert.Equal(NbtTagType.String, compound["Optional"].TagType);
        Assert.Equal(NbtTagType.String, compound["Flags"].TagType);
        Assert.Equal(NbtTagType.String, compound["Aliased"].TagType);
        Assert.Equal(NbtTagType.String, compound["Negative"].TagType);
        Assert.Equal(NbtTagType.String, compound["Wide"].TagType);
        var list = (NbtList)compound["Values"].Value!;
        Assert.Equal(NbtTagType.String, list.ElementType);
        var map = (NbtCompound)compound["Map"].Value!;
        Assert.Equal(NbtTagType.String, map["defined"].TagType);
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void RootEnumValueInheritsTheStringShape(SerializationMode mode)
    {
        var path = CreatePath(mode);
        var data = path.Serialize(StringEnumKind.Creeper);

        var root = NbtSerializer.Deserialize(data);
        Assert.Equal(NbtTagType.String, root.Value.TagType);
        Assert.Equal("Creeper", root.Value.Value);
        Assert.Equal(StringEnumKind.Creeper, path.Deserialize<StringEnumKind>(data));
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void UndefinedValueWritesTheInvariantNumericString(SerializationMode mode)
    {
        var path = CreatePath(mode);

        var memberData = path.Serialize(new StringEnumDto
        {
            Values = [StringByteEnum.Zero, (StringByteEnum)200],
            Map = new Dictionary<string, StringByteEnum>
            {
                ["undefined"] = (StringByteEnum)200,
            },
        });
        var memberCompound = RootCompound(memberData);
        Assert.Equal("200", ((List<string>)((NbtList)memberCompound["Values"].Value!).Value!)[1]);
        Assert.Equal("200", ((NbtCompound)memberCompound["Map"].Value!)["undefined"].Value);

        var data = path.Serialize((StringByteEnum)200);
        Assert.Equal("200", NbtSerializer.Deserialize(data).Value.Value);
        Assert.Equal((StringByteEnum)200, path.Deserialize<StringByteEnum>(data));
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void FlagsRoundTripWithSeparatedNames(SerializationMode mode)
    {
        var path = CreatePath(mode);
        var value = StringEnumFlags.Alpha | StringEnumFlags.Gamma;

        var data = path.Serialize(value);
        Assert.Equal("Alpha, Gamma", NbtSerializer.Deserialize(data).Value.Value);
        Assert.Equal(value, path.Deserialize<StringEnumFlags>(data));
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void UndefinedFlagsCombinationRoundTrips(SerializationMode mode)
    {
        var path = CreatePath(mode);
        var value = StringEnumFlags.Alpha | (StringEnumFlags)2;

        var data = path.Serialize(value);
        Assert.Equal("3", NbtSerializer.Deserialize(data).Value.Value);
        Assert.Equal(value, path.Deserialize<StringEnumFlags>(data));
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void AliasEnumWritesTheFirstDeclaredName(SerializationMode mode)
    {
        var path = CreatePath(mode);

        var data = path.Serialize(StringAliasEnum.B);
        Assert.Equal("A", NbtSerializer.Deserialize(data).Value.Value);
        Assert.Equal(StringAliasEnum.A, path.Deserialize<StringAliasEnum>(data));

        var memberData = path.Serialize(new StringEnumDto { Aliased = StringAliasEnum.B });
        Assert.Equal("A", RootCompound(memberData)["Aliased"].Value);
    }

    public static readonly string[] InvalidNames =
    [
        "Nope",
        "creeper",
        "",
        "0x2",
        " 1",
    ];

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void InvalidNamesThrowWithPath(SerializationMode mode)
    {
        var path = CreatePath(mode);
        foreach (var text in InvalidNames)
        {
            var data = SerializeRaw(text);

            var exception = Assert.Throws<NbtException>(
                () => path.Deserialize<StringKindReaderDto>(data));
            Assert.Contains(".Kind", exception.Path, StringComparison.Ordinal);
        }
    }

    public static readonly string[] InvalidFlags =
    [
        "Alpha,,Gamma",
        "Alpha, Nope",
        "Alpha, 0x2",
        ",",
    ];

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void InvalidFlagsSegmentsThrowWithPath(SerializationMode mode)
    {
        var path = CreatePath(mode);
        foreach (var text in InvalidFlags)
        {
            var data = SerializeRaw(text);

            var exception = Assert.Throws<NbtException>(
                () => path.Deserialize<StringFlagsReaderDto>(data));
            Assert.Contains(".Flags", exception.Path, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void NumericAndMixedSegmentsAreReadForFlags(SerializationMode mode)
    {
        var path = CreatePath(mode);

        Assert.Equal(
            StringEnumFlags.Alpha | StringEnumFlags.Gamma,
            path.Deserialize<StringFlagsReaderDto>(SerializeRaw("1, 32768")).Flags);
        Assert.Equal(
            StringEnumFlags.Alpha | (StringEnumFlags)2,
            path.Deserialize<StringFlagsReaderDto>(SerializeRaw("Alpha, 2")).Flags);
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void OptionsConverterAppliesWithoutTheAttribute(SerializationMode mode)
    {
        var path = CreatePath(mode, OptionsWithPlainKindConverter());

        var data = path.Serialize(new PlainEnumDto { Kind = PlainKind.Creeper });

        Assert.Equal(NbtTagType.String, RootCompound(data)["Kind"].TagType);
        Assert.Equal("Creeper", RootCompound(data)["Kind"].Value);
        Assert.Equal(PlainKind.Creeper, path.Deserialize<PlainEnumDto>(data).Kind);
    }

    [Fact]
    public void OptionsConverterProducesTheSameBytesOnBothPaths()
    {
        var value = new PlainEnumDto { Kind = PlainKind.Creeper };

        Assert.Equal(
            CreatePath(SerializationMode.Reflection, OptionsWithPlainKindConverter()).Serialize(value),
            CreatePath(SerializationMode.Generated, OptionsWithPlainKindConverter()).Serialize(value));
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void MemberLevelConverterAppliesWithoutTheAttribute(SerializationMode mode)
    {
        var path = CreatePath(mode);

        var data = path.Serialize(new ExplicitConverterDto { Kind = ExplicitKind.Creeper });

        Assert.Equal(NbtTagType.String, RootCompound(data)["Kind"].TagType);
        Assert.Equal(ExplicitKind.Creeper, path.Deserialize<ExplicitConverterDto>(data).Kind);
    }

    [Fact]
    public void MemberLevelConverterProducesTheSameBytesOnBothPaths()
    {
        var value = new ExplicitConverterDto { Kind = ExplicitKind.Zombie };

        Assert.Equal(
            CreatePath(SerializationMode.Reflection).Serialize(value),
            CreatePath(SerializationMode.Generated).Serialize(value));
    }

    private static NbtSerializerOptions OptionsWithPlainKindConverter()
    {
        var options = new NbtSerializerOptions
        {
            TypeInfoResolver = new NbtReflectionTypeInfoResolver(),
        };
        options.Converters.Add(new NbtStringEnumConverter<PlainKind>());
        return options;
    }

    private static SerializationTestPath CreatePath(
        SerializationMode mode,
        NbtSerializerOptions? options = null) =>
        mode == SerializationMode.Reflection
            ? SerializationTestPath.Reflection(options ?? new NbtSerializerOptions
            {
                TypeInfoResolver = new NbtReflectionTypeInfoResolver(),
            })
            : SerializationTestPath.Generated(
                options is null ? StringEnumContext.Default : new StringEnumContext(options));

    private static byte[] SerializeRaw(string text)
    {
        var options = new NbtSerializerOptions
        {
            TypeInfoResolver = new NbtReflectionTypeInfoResolver(),
        };
        return NbtSerializer.Serialize(new RawStringDto { Kind = text, Flags = text }, "root", options);
    }

    private static NbtCompound RootCompound(byte[] data) =>
        (NbtCompound)NbtSerializer.Deserialize(data).Value.Value!;
}

[NbtStringEnum]
internal enum StringEnumKind
{
    Zombie,
    Creeper,
    Skeleton,
}

[NbtStringEnum]
[Flags]
internal enum StringEnumFlags
{
    None = 0,
    Alpha = 1,
    Beta = 0x0100,
    Gamma = 0x8000,
}

[NbtStringEnum]
internal enum StringByteEnum : byte
{
    Zero = 0,
    One = 1,
}

[NbtStringEnum]
internal enum StringAliasEnum
{
    A = 1,
    B = 1,
}

[NbtStringEnum]
internal enum StringSByteEnum : sbyte
{
    Zero = 0,
    One = 1,
}

[NbtStringEnum]
internal enum StringLongEnum : long
{
    Zero = 0,
    One = 1,
}

internal enum PlainKind
{
    Zombie,
    Creeper,
}

internal enum ExplicitKind
{
    Zombie,
    Creeper,
}

internal sealed class StringEnumDto
{
    public StringEnumKind Kind { get; set; }

    public StringEnumKind? Optional { get; set; }

    public StringEnumFlags Flags { get; set; }

    public List<StringByteEnum> Values { get; set; } = [];

    public Dictionary<string, StringByteEnum> Map { get; set; } = [];

    public StringAliasEnum Aliased { get; set; }

    public StringSByteEnum Negative { get; set; }

    public StringLongEnum Wide { get; set; }
}

internal sealed class StringKindReaderDto
{
    public StringEnumKind Kind { get; set; }
}

internal sealed class StringFlagsReaderDto
{
    public StringEnumFlags Flags { get; set; }
}

internal sealed class PlainEnumDto
{
    public PlainKind Kind { get; set; }
}

internal sealed class ExplicitConverterDto
{
    [NbtUseConverter(typeof(NbtStringEnumConverter<ExplicitKind>))]
    public ExplicitKind Kind { get; set; }
}

internal sealed class RawStringDto
{
    public string Kind { get; set; } = "";

    public string Flags { get; set; } = "";
}

[NbtSerializable(typeof(StringEnumDto))]
[NbtSerializable(typeof(StringKindReaderDto))]
[NbtSerializable(typeof(StringFlagsReaderDto))]
[NbtSerializable(typeof(PlainEnumDto))]
[NbtSerializable(typeof(ExplicitConverterDto))]
[NbtSerializable(typeof(StringEnumKind))]
[NbtSerializable(typeof(StringEnumFlags))]
[NbtSerializable(typeof(StringAliasEnum))]
internal partial class StringEnumContext : NbtSerializerContext
{
    public StringEnumContext()
    {
    }

    public StringEnumContext(NbtSerializerOptions options)
        : base(options)
    {
    }
}

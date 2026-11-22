using System.Buffers;
using System.Diagnostics.CodeAnalysis;

namespace NbtSharp.Tests;

public sealed class NbtExtensionDataTests
{
    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void RoundTripsUnknownMembers(SerializationMode mode)
    {
        var path = CreatePath(mode);
        var data = NbtSerializer.Serialize(new NbtRoot("root", new NbtCompound
        {
            ["Name"] = "Steve",
            ["Health"] = 20,
            ["Label"] = "extra",
            ["Score"] = 7,
            ["Nested"] = new NbtCompound { ["Inner"] = 3 },
        }));

        var actual = path.Deserialize<ExtensionDto>(data);

        Assert.Equal("Steve", actual.Name);
        Assert.Equal(20, actual.Health);
        Assert.NotNull(actual.Extra);
        Assert.Equal(3, actual.Extra!.Count);
        Assert.Equal("extra", (string)actual.Extra["Label"].Value!);
        Assert.Equal(7, (int)actual.Extra["Score"].Value!);
        var nested = (NbtCompound)actual.Extra["Nested"].Value!;
        Assert.Equal(3, (int)nested["Inner"].Value!);

        var rewritten = path.Serialize(actual);
        var root = (NbtCompound)NbtSerializer.Deserialize(rewritten).Value.Value!;
        Assert.Equal(
            ["Name", "Health", "Label", "Score", "Nested"],
            root.Keys.ToArray());
        var rewrittenNested = (NbtCompound)root["Nested"].Value!;
        Assert.Equal(3, (int)rewrittenNested["Inner"].Value!);

        var copy = path.Deserialize<ExtensionDto>(rewritten);
        Assert.Equal("Steve", copy.Name);
        Assert.Equal(20, copy.Health);
        Assert.Equal(3, copy.Extra!.Count);
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void DictionaryExtensionRoundTripsUnknownMembers(SerializationMode mode)
    {
        var path = CreatePath(mode);
        var data = NbtSerializer.Serialize(new NbtRoot("root", new NbtCompound
        {
            ["Name"] = "Alex",
            ["Extra"] = "unknown",
        }));

        var actual = path.Deserialize<DictionaryExtensionDto>(data);

        Assert.Equal("Alex", actual.Name);
        Assert.NotNull(actual.Extra);
        Assert.Equal("unknown", (string)actual.Extra!["Extra"].Value!);

        var rewritten = path.Serialize(actual);
        var root = (NbtCompound)NbtSerializer.Deserialize(rewritten).Value.Value!;
        Assert.Equal(
            ["Name", "Extra"],
            root.Keys.ToArray());
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void DocumentsWithoutUnknownMembersLeaveTheExtensionMemberNull(SerializationMode mode)
    {
        var path = CreatePath(mode);
        var value = new ExtensionDto { Name = "Steve", Health = 20 };

        var data = path.Serialize(value);
        var root = (NbtCompound)NbtSerializer.Deserialize(data).Value.Value!;
        Assert.Equal(
            ["Name", "Health"],
            root.Keys.ToArray());

        var actual = path.Deserialize<ExtensionDto>(data);
        Assert.Null(actual.Extra);
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void TheExtensionMemberIsNotWrittenAsANamedMember(SerializationMode mode)
    {
        var path = CreatePath(mode);
        var value = new ExtensionDto
        {
            Name = "Steve",
            Health = 20,
            Extra = new NbtCompound { ["Bonus"] = 1 },
        };

        var data = path.Serialize(value);
        var root = (NbtCompound)NbtSerializer.Deserialize(data).Value.Value!;

        Assert.Equal(
            ["Name", "Health", "Bonus"],
            root.Keys.ToArray());
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void PreInitializedExtensionStaysWhenNoUnknownMembersAppear(SerializationMode mode)
    {
        var path = CreatePath(mode);
        var data = NbtSerializer.Serialize(new NbtRoot("root", new NbtCompound { ["Health"] = 5 }));

        var actual = path.Deserialize<PreInitializedExtensionDto>(data);

        Assert.Equal(5, actual.Health);
        Assert.NotNull(actual.Extra);
        Assert.Equal(1, (int)actual.Extra!["seed"].Value!);
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void PreInitializedExtensionIsReplacedWhenUnknownMembersAppear(SerializationMode mode)
    {
        var path = CreatePath(mode);
        var data = NbtSerializer.Serialize(new NbtRoot("root", new NbtCompound
        {
            ["Health"] = 5,
            ["Unknown"] = 9,
        }));

        var actual = path.Deserialize<PreInitializedExtensionDto>(data);

        Assert.Equal(5, actual.Health);
        Assert.NotNull(actual.Extra);
        Assert.Single(actual.Extra!);
        Assert.Equal(9, (int)actual.Extra["Unknown"].Value!);
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void DuplicateUnknownMembersKeepTheLastValueOrThrow(SerializationMode mode)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer);
        writer.WriteStartCompound("root");
        writer.WriteString("Label", "first");
        writer.WriteString("Label", "second");
        writer.WriteEndCompound();
        writer.Complete();

        var path = CreatePath(mode);
        var actual = path.Deserialize<ExtensionDto>(buffer.WrittenSpan);
        Assert.Equal("second", (string)actual.Extra!["Label"].Value!);

        var throwingPath = CreatePath(mode, rejectDuplicateCompoundNames: true);
        var exception = Assert.Throws<NbtException>(() =>
            throwingPath.Deserialize<ExtensionDto>(buffer.WrittenSpan));
        Assert.Equal("$.Label", exception.Path);
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void RenamedMappedMembersConflictWithExtensionEntries(SerializationMode mode)
    {
        var path = CreatePath(mode);
        var value = new RenamedExtensionDto
        {
            Value = "v",
            Extra = new NbtCompound { ["renamed"] = 1 },
        };

        var exception = Assert.Throws<NbtException>(() => path.Serialize(value));

        Assert.Equal("$.renamed", exception.Path);
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void NamingPolicyRenamedMembersConflictWithExtensionEntries(SerializationMode mode)
    {
        var path = CreatePath(mode, namingPolicy: true);
        var value = new CamelConflictDto
        {
            Value = "v",
            Extra = new NbtCompound { ["value"] = 1 },
        };

        var exception = Assert.Throws<NbtException>(() => path.Serialize(value));

        Assert.Equal("$.value", exception.Path);
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void OmittedNullMembersStillOwnTheirName(SerializationMode mode)
    {
        var path = CreatePath(mode);
        var value = new OmitConflictDto
        {
            Label = null,
            Extra = new NbtCompound { ["Label"] = 1 },
        };

        var exception = Assert.Throws<NbtException>(() => path.Serialize(value));

        Assert.Equal("$.Label", exception.Path);
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void NestedUnknownCompoundsAndListsRoundTrip(SerializationMode mode)
    {
        var path = CreatePath(mode);
        var data = NbtSerializer.Serialize(new NbtRoot("root", new NbtCompound
        {
            ["Name"] = "Steve",
            ["Matrix"] = new NbtList(new List<NbtList>
            {
                new(new List<NbtCompound> { new() { ["Cell"] = 1 } }),
            }),
            ["Numbers"] = new NbtList(new List<int> { 3, 4 }),
        }));

        var actual = path.Deserialize<ExtensionDto>(data);

        var matrix = (NbtList)actual.Extra!["Matrix"].Value!;
        Assert.Equal(NbtTagType.List, matrix.ElementType);
        var row = ((List<NbtList>)matrix.Value)[0];
        var cell = (NbtCompound)((List<NbtCompound>)row.Value)[0];
        Assert.Equal(1, (int)cell["Cell"].Value!);
        var numbers = (List<int>)((NbtList)actual.Extra["Numbers"].Value!).Value;
        Assert.Equal([3, 4], numbers);

        var copy = path.Deserialize<ExtensionDto>(path.Serialize(actual));
        var copyMatrix = (NbtList)copy.Extra!["Matrix"].Value!;
        var copyRow = ((List<NbtList>)copyMatrix.Value)[0];
        var copyCell = (NbtCompound)((List<NbtCompound>)copyRow.Value)[0];
        Assert.Equal(1, (int)copyCell["Cell"].Value!);
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void PolymorphicPayloadUnknownMembersRoundTripInsideTheEnvelope(SerializationMode mode)
    {
        var data = NbtSerializer.Serialize(new NbtRoot("root", new NbtCompound
        {
            ["Star"] = new NbtCompound
            {
                ["$type"] = "ExtCat",
                ["$value"] = new NbtCompound
                {
                    ["Name"] = "Milo",
                    ["Lives"] = 9,
                },
            },
        }));

        var path = CreatePath(mode);
        var actual = path.Deserialize<ExtZoo>(data);

        var cat = Assert.IsType<ExtCat>(actual.Star);
        Assert.Equal("Milo", cat.Name);
        Assert.Equal(9, (int)cat.Extra!["Lives"].Value!);

        var rewritten = path.Serialize(actual);
        var root = (NbtCompound)NbtSerializer.Deserialize(rewritten).Value.Value!;
        var envelope = (NbtCompound)root["Star"].Value!;
        Assert.Equal("ExtCat", (string)envelope["$type"].Value!);
        var payload = (NbtCompound)envelope["$value"].Value!;
        Assert.Equal(
            ["Name", "Lives"],
            payload.Keys.ToArray());

        var copy = Assert.IsType<ExtCat>(path.Deserialize<ExtZoo>(rewritten).Star);
        Assert.Equal(9, (int)copy.Extra!["Lives"].Value!);
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void TruncatedUnknownCompoundErrorsCarryTheMemberPath(SerializationMode mode)
    {
        var data = NbtSerializer.Serialize(new NbtRoot("root", new NbtCompound
        {
            ["X"] = new NbtCompound
            {
                ["Known"] = 1,
                ["foo"] = new NbtCompound { ["a"] = 1 },
            },
        }));

        var path = CreatePath(mode);

        var exception = Assert.Throws<NbtException>(() =>
            path.Deserialize<NestedRootDto>(data[..^3]));

        Assert.Equal("$.X.foo", exception.Path);
        Assert.True(exception.IsTruncated);
    }

    [Fact]
    public void ReflectionRejectsTwoExtensionMembers()
    {
        AssertExtensionContractRejected(new TwoExtensionDto { First = new(), Second = new() });
    }

    [Fact]
    public void ReflectionRejectsNbtValueExtensionMember()
    {
        AssertExtensionContractRejected(new NbtValueExtensionDto { Extra = "" });
    }

    [Fact]
    public void ReflectionRejectsScalarExtensionMember()
    {
        AssertExtensionContractRejected(new IntExtensionDto { Extra = 1 });
    }

    [Fact]
    public void ReflectionRejectsInterfaceExtensionMember()
    {
        AssertExtensionContractRejected(
            new InterfaceExtensionDto { Extra = new Dictionary<string, NbtValue>() });
    }

    [Fact]
    public void ReflectionRejectsMismatchedDictionaryExtensionMember()
    {
        AssertExtensionContractRejected(new DictionaryOfIntExtensionDto { Extra = [] });
    }

    [Fact]
    public void ReflectionRejectsCombinedAttributes()
    {
        AssertExtensionContractRejected(new PropertyNameExtensionDto { Extra = new() });
        AssertExtensionContractRejected(new UseConverterExtensionDto { Extra = new() });
    }

    [Fact]
    public void ReflectionRejectsMembersWithoutSetterAndRequiredMembers()
    {
        var noSetter = new NoSetterExtensionDto();
        noSetter.Extra["Known"] = 1;
        Assert.Equal(1, (int)noSetter.Extra["Known"].Value!);
        var required = new RequiredExtensionDto { Extra = new() { ["Known"] = 1 } };
        Assert.Equal(1, (int)required.Extra!["Known"].Value!);

        AssertExtensionContractRejected(noSetter);
        AssertExtensionContractRejected(required);
    }

    [Fact]
    public void ReflectionRejectsFieldsWithoutInclude()
    {
        var value = new FieldWithoutIncludeDto { Extra = new() };
        Assert.NotNull(value.Extra);

        AssertExtensionContractRejected(value);
    }

    [Fact]
    public void RegisteredDomConvertersDoNotAffectExtensionCapture()
    {
        var options = ReflectionOptions();
        options.Converters.Add(new ReversingValueConverter());
        options.Converters.Add(new StringifyingCompoundConverter());
        var data = NbtSerializer.Serialize(new NbtRoot("root", new NbtCompound
        {
            ["Name"] = "Steve",
            ["Label"] = "abc",
            ["Nested"] = new NbtCompound { ["Inner"] = 1 },
        }));

        var actual = NbtSerializer.Deserialize<ExtensionDto>(data, options);

        Assert.Equal("abc", (string)actual.Extra!["Label"].Value!);
        var nested = Assert.IsType<NbtCompound>(actual.Extra["Nested"].Value);
        Assert.Equal(1, (int)nested["Inner"].Value!);

        var rewritten = NbtSerializer.Serialize(actual, "root", options);
        var root = Assert.IsType<NbtCompound>(NbtSerializer.Deserialize(rewritten).Value.Value!);
        Assert.Equal("abc", (string)root["Label"].Value!);
        var rewrittenNested = Assert.IsType<NbtCompound>(root["Nested"].Value!);
        Assert.Equal(1, (int)rewrittenNested["Inner"].Value!);
    }

    [Fact]
    public void RegisteredDomConvertersStillApplyAtDeclaredEntryPoints()
    {
        var options = ReflectionOptions();
        options.Converters.Add(new ReversingValueConverter());
        options.Converters.Add(new StringifyingCompoundConverter());

        // Declared NbtCompound member:
        var memberData = NbtSerializer.Serialize(
            new DomMemberDto { Direct = new() { ["a"] = 1 } },
            "root",
            options);
        var memberRoot = (NbtCompound)NbtSerializer.Deserialize(memberData).Value.Value!;
        Assert.Equal("MARKED", (string)memberRoot["Direct"].Value!);

        // Declared NbtValue member:
        var valueData = NbtSerializer.Serialize(new DomMemberDto { Value = "abc" }, "root", options);
        var valueRoot = (NbtCompound)NbtSerializer.Deserialize(valueData).Value.Value!;
        Assert.Equal("cba", (string)valueRoot["Value"].Value!);

        // DOM root:
        var rootData = NbtSerializer.SerializeDocument(new NbtRoot("root", "abc"), options);
        Assert.Equal("cba", (string)NbtSerializer.Deserialize(rootData).Value.Value!);
    }

    [Fact]
    public void RegisteredListAndCompoundConvertersDoNotMaterializeNestedDomValues()
    {
        var options = ReflectionOptions();
        options.Converters.Add(new ThrowingListConverter());
        options.Converters.Add(new StringifyingCompoundConverter());
        var data = NbtSerializer.Serialize(new NbtRoot("root", new NbtCompound
        {
            ["Name"] = "Steve",
            ["Matrix"] = new NbtList(new List<NbtList>
            {
                new(new List<NbtCompound> { new() { ["Cell"] = 1 } }),
            }),
        }));

        var actual = NbtSerializer.Deserialize<ExtensionDto>(data, options);

        var matrix = (NbtList)actual.Extra!["Matrix"].Value!;
        var row = ((List<NbtList>)matrix.Value)[0];
        var cell = (NbtCompound)((List<NbtCompound>)row.Value)[0];
        Assert.Equal(1, (int)cell["Cell"].Value!);

        var rewritten = NbtSerializer.Serialize(actual, "root", options);
        var root = (NbtCompound)NbtSerializer.Deserialize(rewritten).Value.Value!;
        var rewrittenMatrix = Assert.IsType<NbtList>(root["Matrix"].Value!);
        var rewrittenRow = ((List<NbtList>)rewrittenMatrix.Value)[0];
        var rewrittenCell = (NbtCompound)((List<NbtCompound>)rewrittenRow.Value)[0];
        Assert.Equal(1, (int)rewrittenCell["Cell"].Value!);
    }

    [Fact]
    public void NestedScalarElementsStaySymmetricWithRegisteredConverters()
    {
        // A registered scalar converter applies when the scalar is the declared value (the entry
        // point), but nested DOM list elements must use the built-in converters on both the read
        // and the write side, so serialize(deserialize(x)) round-trips byte-stably.
        var options = ReflectionOptions();
        options.Converters.Add(new ReversingStringConverter());

        Assert.Equal(
            "cba",
            NbtSerializer.Deserialize<string>(
                NbtSerializer.Serialize("abc", "root", options),
                options));

        var list = new NbtList(new List<string> { "abc" });
        var data = NbtSerializer.Serialize(list, "root", options);
        var copy = NbtSerializer.Deserialize<NbtList>(data, options);
        var elements = (List<string>)copy.Value;
        var element = Assert.Single(elements);
        Assert.Equal("abc", element);
        Assert.Equal(data, NbtSerializer.Serialize(copy, "root", options));
    }

    private static SerializationTestPath CreatePath(
        SerializationMode mode,
        bool rejectDuplicateCompoundNames = false,
        bool namingPolicy = false)
    {
        if (mode == SerializationMode.Reflection)
        {
            return SerializationTestPath.Reflection(new NbtSerializerOptions
            {
                TypeInfoResolver = new NbtReflectionTypeInfoResolver(),
                RejectDuplicateCompoundNames = rejectDuplicateCompoundNames,
                PropertyNamingPolicy = namingPolicy ? NbtNamingPolicy.CamelCase : null,
            });
        }

        if (namingPolicy)
        {
            return SerializationTestPath.Generated(new CamelExtensionContext());
        }

        return SerializationTestPath.Generated(
            new ExtensionDataContext(new NbtSerializerOptions
            {
                RejectDuplicateCompoundNames = rejectDuplicateCompoundNames,
            }));
    }

    private static NbtSerializerOptions ReflectionOptions() => new()
    {
        TypeInfoResolver = new NbtReflectionTypeInfoResolver(),
    };

    private static void AssertExtensionContractRejected(object dto)
    {
        var options = ReflectionOptions();

        Assert.Throws<NotSupportedException>(() => options.GetConverter(dto.GetType()));
    }

    internal sealed class ExtensionDto
    {
        public string Name { get; set; } = "";

        public int Health { get; set; }

        [NbtExtensionData]
        public NbtCompound? Extra { get; set; }
    }

    internal sealed class DictionaryExtensionDto
    {
        public string Name { get; set; } = "";

        [NbtExtensionData]
        public Dictionary<string, NbtValue>? Extra { get; set; }
    }

    internal sealed class PreInitializedExtensionDto
    {
        public int Health { get; set; }

        [NbtExtensionData]
        public NbtCompound? Extra { get; set; } = new() { ["seed"] = 1 };
    }

    internal sealed class RenamedExtensionDto
    {
        [NbtPropertyName("renamed")]
        public string Value { get; set; } = "";

        [NbtExtensionData]
        public NbtCompound? Extra { get; set; }
    }

    internal sealed class CamelConflictDto
    {
        public string Value { get; set; } = "";

        [NbtExtensionData]
        public NbtCompound? Extra { get; set; }
    }

    internal sealed class OmitConflictDto
    {
        public string? Label { get; set; }

        [NbtExtensionData]
        public NbtCompound? Extra { get; set; }
    }

    internal sealed class NestedRootDto
    {
        public NestedPayloadDto X { get; set; } = new();
    }

    internal sealed class NestedPayloadDto
    {
        public int Known { get; set; }

        [NbtExtensionData]
        public NbtCompound? Extra { get; set; }
    }

    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Local")]
    private sealed class TwoExtensionDto
    {
        [NbtExtensionData]
        public NbtCompound? First { get; set; }

        [NbtExtensionData]
        public NbtCompound? Second { get; set; }
    }

    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Local")]
    private sealed class NbtValueExtensionDto
    {
        [NbtExtensionData]
        public NbtValue Extra { get; set; }
    }

    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Local")]
    private sealed class IntExtensionDto
    {
        [NbtExtensionData]
        public int Extra { get; set; }
    }

    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Local")]
    private sealed class InterfaceExtensionDto
    {
        [NbtExtensionData]
        public IDictionary<string, NbtValue> Extra { get; set; } =
            new Dictionary<string, NbtValue>();
    }

    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Local")]
    private sealed class DictionaryOfIntExtensionDto
    {
        [NbtExtensionData]
        public Dictionary<string, int> Extra { get; set; } = [];
    }

    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Local")]
    private sealed class PropertyNameExtensionDto
    {
        [NbtExtensionData]
        [NbtPropertyName("extra")]
        public NbtCompound? Extra { get; set; }
    }

    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Local")]
    private sealed class UseConverterExtensionDto
    {
        [NbtExtensionData]
        [NbtUseConverter]
        public NbtCompound? Extra { get; set; }
    }

    private sealed class NoSetterExtensionDto
    {
        [NbtExtensionData]
        public NbtCompound Extra { get; } = new();
    }

    private sealed class RequiredExtensionDto
    {
        [NbtExtensionData]
        public required NbtCompound? Extra { get; set; }
    }

    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Local")]
    private sealed class FieldWithoutIncludeDto
    {
        [NbtExtensionData]
        public NbtCompound? Extra;
    }

    internal sealed class DomMemberDto
    {
        public NbtCompound Direct { get; set; } = new();

        public NbtValue Value { get; set; } = "";
    }

    [NbtDerived(typeof(ExtCat))]
    internal interface IExtAnimal
    {
        string Name { get; set; }
    }

    internal sealed class ExtCat : IExtAnimal
    {
        public string Name { get; set; } = "";

        [NbtExtensionData]
        public NbtCompound? Extra { get; set; }
    }

    internal sealed class ExtZoo
    {
        public IExtAnimal? Star { get; set; }
    }

    private sealed class ReversingStringConverter : NbtConverter<string>
    {
        public override string Read(ref NbtReader reader, NbtSerializerOptions _) => reader.GetString();

        public override void Write(ref NbtWriter writer, string value, NbtSerializerOptions _)
        {
            var chars = value.ToCharArray();
            Array.Reverse(chars);
            writer.WriteString(new string(chars));
        }

        public override bool TryGetTagType(string value, NbtSerializerOptions _, out NbtTagType tagType)
        {
            tagType = NbtTagType.String;
            return true;
        }
    }

    private sealed class ReversingValueConverter : NbtConverter<NbtValue>
    {
        public override NbtValue Read(ref NbtReader reader, NbtSerializerOptions options) =>
            new string(reader.GetString().Reverse().ToArray());

        public override void Write(ref NbtWriter writer, NbtValue value, NbtSerializerOptions options) =>
            writer.WriteString(new string(((string)value.Value!).Reverse().ToArray()));

        public override bool TryGetTagType(NbtValue value, NbtSerializerOptions options, out NbtTagType tagType)
        {
            tagType = NbtTagType.String;
            return true;
        }
    }

    private sealed class StringifyingCompoundConverter : NbtConverter<NbtCompound>
    {
        public override NbtCompound Read(ref NbtReader reader, NbtSerializerOptions options)
        {
            var compound = new NbtCompound();
            while (reader.Read())
            {
                if (reader.TokenType == NbtTokenType.EndCompound)
                {
                    return compound;
                }

                compound[reader.Name!] = NbtSerializer.DeserializeValue<NbtValue>(ref reader, options);
            }

            throw new NbtException("The compound was not terminated.");
        }

        public override void Write(ref NbtWriter writer, NbtCompound value, NbtSerializerOptions options) =>
            writer.WriteString("MARKED");

        public override bool TryGetTagType(NbtCompound value, NbtSerializerOptions options, out NbtTagType tagType)
        {
            tagType = NbtTagType.String;
            return true;
        }
    }

    private sealed class ThrowingListConverter : NbtConverter<NbtList>
    {
        public override NbtList Read(ref NbtReader reader, NbtSerializerOptions options) =>
            throw new NotSupportedException("The nested NbtList was intercepted by a registered converter.");

        public override void Write(ref NbtWriter writer, NbtList value, NbtSerializerOptions options) =>
            writer.WriteString("MARKED");

        public override bool TryGetTagType(NbtList value, NbtSerializerOptions options, out NbtTagType tagType)
        {
            tagType = NbtTagType.String;
            return true;
        }
    }
}

[NbtSerializable(typeof(NbtExtensionDataTests.ExtensionDto))]
[NbtSerializable(typeof(NbtExtensionDataTests.DictionaryExtensionDto))]
[NbtSerializable(typeof(NbtExtensionDataTests.PreInitializedExtensionDto))]
[NbtSerializable(typeof(NbtExtensionDataTests.RenamedExtensionDto))]
[NbtSerializable(typeof(NbtExtensionDataTests.OmitConflictDto))]
[NbtSerializable(typeof(NbtExtensionDataTests.NestedRootDto))]
[NbtSerializable(typeof(NbtExtensionDataTests.ExtZoo))]
[NbtSerializable(typeof(NbtExtensionDataTests.DomMemberDto))]
internal partial class ExtensionDataContext : NbtSerializerContext
{
    public ExtensionDataContext()
    {
    }

    public ExtensionDataContext(NbtSerializerOptions options)
        : base(options)
    {
    }
}

[NbtSourceGenerationOptions(NamingPolicy = NbtNamingPolicyKind.CamelCase)]
[NbtSerializable(typeof(NbtExtensionDataTests.CamelConflictDto))]
internal partial class CamelExtensionContext : NbtSerializerContext
{
}

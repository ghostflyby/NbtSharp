using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace NbtSharp.SourceGeneration.Tests;

/// <summary>
/// Invalid contract declarations are rejected by both paths: the source generator reports the
/// matching NBTSG diagnostic at compile time, and the reflection resolver throws
/// <see cref="NotSupportedException"/> when the same contract is built at runtime.
/// </summary>
public sealed class ContractValidationParityTests
{
    private static readonly ImmutableArray<MetadataReference> References = GetReferences();

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void IgnoredRequiredMemberThrowsNotSupportedException(SerializationMode mode)
    {
        if (mode == SerializationMode.Generated)
        {
            var output = Run(Source("""
                internal sealed class Dto
                {
                    [NbtIgnore]
                    public required string Ignored { get; init; }

                    public int Value { get; set; }
                }
                """));
            Assert.Contains(output.GeneratorDiagnostics, static diagnostic => diagnostic.Id == "NBTSG015");
            return;
        }

        var value = new IgnoredRequiredDto { Name = "x", Value = 1 };
        Assert.Equal("x", value.Name);
        Assert.Equal(1, value.Value);
        var options = ModifierOptions(static contract => contract.GetMember("Name")!.Ignore = true);

        var exception = Assert.Throws<NotSupportedException>(() =>
            NbtSerializer.Serialize(value, "root", options));

        Assert.Equal(
            $"The required member 'Name' on {typeof(IgnoredRequiredDto)} is not serialized and the " +
            "selected constructor is not marked with SetsRequiredMembers.",
            exception.Message);
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void IgnoringAConstructorBoundMemberWithoutOtherConstructorsThrows(SerializationMode mode)
    {
        if (mode == SerializationMode.Generated)
        {
            var output = Run(Source("""
                internal sealed class Dto
                {
                    public Dto(int a, int b)
                    {
                        A = a;
                        B = b;
                    }

                    [NbtIgnore]
                    public int A { get; }

                    public int B { get; }
                }
                """));
            Assert.Contains(output.GeneratorDiagnostics, static diagnostic => diagnostic.Id == "NBTSG004");
            return;
        }

        var value = new BoundOnlyDto(1, 2);
        Assert.Equal(1, value.A);
        Assert.Equal(2, value.B);
        var options = ModifierOptions(static contract => contract.GetMember("A")!.Ignore = true);

        var exception = Assert.Throws<NotSupportedException>(() =>
            NbtSerializer.Serialize(value, "root", options));

        Assert.Equal(
            $"The type {typeof(BoundOnlyDto)} needs a public parameterless constructor or a public " +
            "constructor whose parameters match its NBT members.",
            exception.Message);
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void ModifierCreatedDuplicateNameThrows(SerializationMode mode)
    {
        if (mode == SerializationMode.Generated)
        {
            var output = Run(Source("""
                internal sealed class Dto
                {
                    [NbtPropertyName("Name")]
                    public string Name { get; set; } = "";

                    [NbtPropertyName("Name")]
                    public int Health { get; set; }
                }
                """));
            Assert.Contains(output.GeneratorDiagnostics, static diagnostic => diagnostic.Id == "NBTSG005");
            return;
        }

        var value = new RenameDto { Name = "x", Health = 1 };
        Assert.Equal("x", value.Name);
        Assert.Equal(1, value.Health);
        var options = ModifierOptions(static contract => contract.GetMember("Health")!.NbtName = "Name");

        var exception = Assert.Throws<NotSupportedException>(() =>
            NbtSerializer.Serialize(value, "root", options));

        Assert.Equal(
            $"The type {typeof(RenameDto)} maps more than one member to the NBT name 'Name'.",
            exception.Message);
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void InjectedConverterWithMismatchedCanConvertThrows(SerializationMode mode)
    {
        if (mode == SerializationMode.Generated)
        {
            var output = Run(Source("""
                internal sealed class Dto
                {
                    [NbtUseConverter(typeof(StringConverter))]
                    public int Value { get; set; }
                }

                internal sealed class StringConverter : NbtConverter<string>
                {
                    public override string Read(ref NbtReader reader, NbtSerializerOptions options) =>
                        reader.GetString();

                    public override void Write(
                        ref NbtWriter writer,
                        string value,
                        NbtSerializerOptions options) =>
                        writer.WriteString(value);
                }
                """));
            Assert.Contains(output.GeneratorDiagnostics, static diagnostic => diagnostic.Id == "NBTSG016");
            return;
        }

        var value = new PlainIntDto { Value = 5 };
        Assert.Equal(5, value.Value);
        var options = ModifierOptions(static contract =>
            contract.GetMember("Value")!.Converter = new ParityStringConverter());

        var exception = Assert.Throws<NotSupportedException>(() =>
            NbtSerializer.Serialize(value, "root", options));

        Assert.Equal(
            $"The converter {typeof(ParityStringConverter)} assigned to member 'Value' does not " +
            $"support the member type '{typeof(int)}'.",
            exception.Message);
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void HiddenMembersStillThrowWithoutModifiers(SerializationMode mode)
    {
        if (mode == SerializationMode.Generated)
        {
            var output = Run(Source("""
                internal class Base
                {
                    public virtual int Value { get; set; }
                }

                internal sealed class Dto : Base
                {
                    public new int Value { get; set; }
                }
                """));
            Assert.Contains(output.GeneratorDiagnostics, static diagnostic => diagnostic.Id == "NBTSG017");
            Assert.DoesNotContain(output.GeneratorDiagnostics, static diagnostic => diagnostic.Id == "NBTSG005");
            return;
        }

        var value = new HiddenMemberDerived { Value = 2 };
        ((HiddenMemberBase)value).Value = 1;
        Assert.Equal(2, value.Value);
        Assert.Equal(1, ((HiddenMemberBase)value).Value);

        var exception = Assert.Throws<NotSupportedException>(() =>
            NbtSerializer.Serialize(value, "root", ReflectionOptions()));

        Assert.Equal(
            $"The member 'Value' on {typeof(HiddenMemberDerived)} hides a base member with the same " +
            "name. Rename the derived member or exclude one of the members with [NbtIgnore].",
            exception.Message);
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void DuplicateAttributeNamesStillThrowWithoutModifiers(SerializationMode mode)
    {
        if (mode == SerializationMode.Generated)
        {
            var output = Run(Source("""
                internal sealed class Dto
                {
                    [NbtPropertyName("x")]
                    public int First { get; set; }

                    [NbtPropertyName("x")]
                    public int Second { get; set; }
                }
                """));
            Assert.Contains(output.GeneratorDiagnostics, static diagnostic => diagnostic.Id == "NBTSG005");
            return;
        }

        var value = new AttributeCollisionDto { A = 1, B = 2 };
        Assert.Equal(1, value.A);
        Assert.Equal(2, value.B);

        var exception = Assert.Throws<NotSupportedException>(() =>
            NbtSerializer.Serialize(value, "root", ReflectionOptions()));

        Assert.Equal(
            $"The type {typeof(AttributeCollisionDto)} maps more than one member to the NBT name 'x'.",
            exception.Message);
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void MissingConstructorsStillThrowWithoutModifiers(SerializationMode mode)
    {
        if (mode == SerializationMode.Generated)
        {
            var output = Run(Source("""
                internal sealed class Dto
                {
                    public Dto(int unrelated)
                    {
                        _ = unrelated;
                    }

                    public int Value { get; }
                }
                """));
            Assert.Contains(output.GeneratorDiagnostics, static diagnostic => diagnostic.Id == "NBTSG004");
            return;
        }

        var value = new NoUsableCtorDto(1);
        var exception = Assert.Throws<NotSupportedException>(() =>
            NbtSerializer.Serialize(value, "root", ReflectionOptions()));

        Assert.Equal(
            $"The type {typeof(NoUsableCtorDto)} needs a public parameterless constructor or a public " +
            "constructor whose parameters match its NBT members.",
            exception.Message);
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void UnmappedRequiredMembersStillThrowWithoutModifiers(SerializationMode mode)
    {
        if (mode == SerializationMode.Generated)
        {
            var output = Run(Source("""
                internal sealed class Dto
                {
                    [NbtIgnore]
                    public required string Ignored { get; init; }

                    public int Value { get; set; }
                }
                """));
            Assert.Contains(output.GeneratorDiagnostics, static diagnostic => diagnostic.Id == "NBTSG015");
            return;
        }

        var value = new SnapshotUnmappedRequiredDto { Ignored = "", Value = 1 };
        Assert.Equal("", value.Ignored);
        Assert.Equal(1, value.Value);

        var exception = Assert.Throws<NotSupportedException>(() =>
            NbtSerializer.Serialize(value, "root", ReflectionOptions()));

        Assert.Equal(
            $"The required member 'Ignored' on {typeof(SnapshotUnmappedRequiredDto)} is not serialized " +
            "and the selected constructor is not marked with SetsRequiredMembers.",
            exception.Message);
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void FieldUseConverterWithoutIncludeStillThrowsWithoutModifiers(SerializationMode mode)
    {
        if (mode == SerializationMode.Generated)
        {
            var output = Run(Source("""
                internal sealed class Dto
                {
                    [NbtUseConverter(typeof(IntConverter))]
                    public int Value;
                }

                internal sealed class IntConverter : NbtConverter<int>
                {
                    public override int Read(ref NbtReader reader, NbtSerializerOptions options) =>
                        reader.GetInt32();

                    public override void Write(
                        ref NbtWriter writer,
                        int value,
                        NbtSerializerOptions options) =>
                        writer.WriteInt32(value);
                }
                """));
            Assert.Contains(output.GeneratorDiagnostics, static diagnostic => diagnostic.Id == "NBTSG016");
            return;
        }

        var value = new FieldUseConverterWithoutIncludeDto();
        Assert.Equal(1, value.Value);

        var exception = Assert.Throws<NotSupportedException>(() =>
            NbtSerializer.Serialize(value, "root", ReflectionOptions()));

        Assert.Equal(
            "The [NbtUseConverter] declaration on member 'Value' is invalid: fields require " +
            "[NbtInclude] in addition to [NbtUseConverter].",
            exception.Message);
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void FieldExtensionDataWithoutIncludeStillThrowsWithoutModifiers(SerializationMode mode)
    {
        if (mode == SerializationMode.Generated)
        {
            var output = Run(Source("""
                internal sealed class Dto
                {
                    [NbtExtensionData]
                    public NbtCompound Extra = new();
                }
                """));
            Assert.Contains(output.GeneratorDiagnostics, static diagnostic => diagnostic.Id == "NBTSG010");
            return;
        }

        var value = new ExtensionFieldWithoutIncludeDto();
        Assert.NotNull(value.Extra);

        var exception = Assert.Throws<NotSupportedException>(() =>
            NbtSerializer.Serialize(value, "root", ReflectionOptions()));

        Assert.Equal(
            "The [NbtExtensionData] declaration on member 'Extra' is invalid: fields require " +
            "[NbtInclude] in addition to [NbtExtensionData].",
            exception.Message);
    }

    [Theory]
    [InlineData(SerializationMode.Generated, "abstract")]
    [InlineData(SerializationMode.Reflection, "abstract")]
    [InlineData(SerializationMode.Generated, "no-default-constructor")]
    [InlineData(SerializationMode.Reflection, "no-default-constructor")]
    [InlineData(SerializationMode.Generated, "wrong-type")]
    [InlineData(SerializationMode.Reflection, "wrong-type")]
    public void InvalidAttributeConverterDeclarationsStillThrowWithoutModifiers(
        SerializationMode mode,
        string variant)
    {
        // The same reason text is the contract on both paths: the generated diagnostic embeds it in
        // the message, so asserting the message (not just the ID) keeps each row attributable to
        // the rule it exercises. Without it, the abstract row would also pass through the
        // "no public parameterless constructor" branch. The wrong-type row spells types differently
        // by design: the generator renders symbols as keywords ('string'/'int'), while the runtime
        // message uses Type names ('System.String'/'System.Int32').
        var generatedReason = variant switch
        {
            "abstract" => "the converter type must not be abstract",
            "no-default-constructor" =>
                "the converter type must have a public parameterless constructor",
            "wrong-type" => "the converter handles 'string' but the member type is 'int'",
            _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, "Unknown variant."),
        };
        var reason = variant switch
        {
            "abstract" => "the converter type must not be abstract",
            "no-default-constructor" =>
                "the converter type must have a public parameterless constructor",
            "wrong-type" =>
                "the converter handles 'System.String' but the member type is 'System.Int32'",
            _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, "Unknown variant."),
        };

        if (mode == SerializationMode.Generated)
        {
            var output = Run(Source(CreateInvalidConverterDeclaration(variant)));
            var diagnostic = Assert.Single(
                output.GeneratorDiagnostics,
                static candidate => candidate.Id == "NBTSG016");
            Assert.Contains(generatedReason, diagnostic.GetMessage(), StringComparison.Ordinal);
            return;
        }

        Type dtoType;
        switch (variant)
        {
            case "abstract":
            {
                var value = new AbstractConverterDto { Value = 1 };
                Assert.Equal(1, value.Value);
                dtoType = typeof(AbstractConverterDto);
                break;
            }
            case "no-default-constructor":
            {
                var value = new NoDefaultCtorConverterDto { Value = 1 };
                Assert.Equal(1, value.Value);
                Assert.IsType<ParityNoDefaultCtorConverter>(ParityNoDefaultCtorConverter.Create());
                dtoType = typeof(NoDefaultCtorConverterDto);
                break;
            }
            case "wrong-type":
            {
                var value = new WrongTypeConverterDto { Value = 1 };
                Assert.Equal(1, value.Value);
                dtoType = typeof(WrongTypeConverterDto);
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(variant), variant, "Unknown variant.");
        }

        var options = ReflectionOptions();

        var exception = Assert.Throws<NotSupportedException>(() =>
            new NbtReflectionTypeInfoResolver().GetTypeInfo(dtoType, options));

        Assert.Equal(
            $"The [NbtUseConverter] declaration on member 'Value' is invalid: {reason}.",
            exception.Message);
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void IgnoredMembersSkipConverterValidationWithoutModifiers(SerializationMode mode)
    {
        if (mode == SerializationMode.Generated)
        {
            var output = Run(Source("""
                internal sealed class Dto
                {
                    [NbtIgnore]
                    [NbtUseConverter(typeof(IntConverter))]
                    public int Property { get; set; }

                    [NbtIgnore]
                    [NbtInclude]
                    [NbtUseConverter(typeof(IntConverter))]
                    public int Field = 2;

                    public int Kept { get; set; }
                }

                internal abstract class IntConverter : NbtConverter<int>
                {
                    public override int Read(ref NbtReader reader, NbtSerializerOptions options) =>
                        reader.GetInt32();

                    public override void Write(
                        ref NbtWriter writer,
                        int value,
                        NbtSerializerOptions options) =>
                        writer.WriteInt32(value);
                }
                """));

            Assert.DoesNotContain(output.GeneratorDiagnostics, static diagnostic => diagnostic.Id == "NBTSG016");
            Assert.DoesNotContain(output.GeneratorDiagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            Assert.DoesNotContain(output.CompilationDiagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            Assert.True(output.GeneratedSourceCount > 0);
            // The DTO converter must still be emitted for the mapped member; otherwise a silent
            // "nothing generated for Dto" regression would pass every assertion above.
            Assert.Contains("Kept", output.GeneratedSource, StringComparison.Ordinal);
            return;
        }

        var value = new IgnoredInvalidConverterDto { Property = 4, Kept = 8 };
        Assert.Equal(4, value.Property);
        Assert.Equal(2, value.Field);
        Assert.Equal(8, value.Kept);

        var data = NbtSerializer.Serialize(value, "root", ReflectionOptions());
        var compound = (NbtCompound)NbtSerializer.Deserialize(data).Value.Value!;
        Assert.True(compound.ContainsKey("Kept"));
        Assert.False(compound.ContainsKey("Property"));
        Assert.False(compound.ContainsKey("Field"));

        var copy = NbtSerializer.Deserialize<IgnoredInvalidConverterDto>(data, ReflectionOptions());
        Assert.Equal(8, copy.Kept);
        Assert.Equal(0, copy.Property);
        Assert.Equal(2, copy.Field);
    }

    private static string CreateInvalidConverterDeclaration(string variant) => variant switch
    {
        "abstract" => """
            internal sealed class Dto
            {
                [NbtUseConverter(typeof(IntConverter))]
                public int Value { get; set; }
            }

            internal abstract class IntConverter : NbtConverter<int>
            {
                public override int Read(ref NbtReader reader, NbtSerializerOptions options) =>
                    reader.GetInt32();

                public override void Write(
                    ref NbtWriter writer,
                    int value,
                    NbtSerializerOptions options) =>
                    writer.WriteInt32(value);
            }
            """,
        "no-default-constructor" => """
            internal sealed class Dto
            {
                [NbtUseConverter(typeof(IntConverter))]
                public int Value { get; set; }
            }

            internal sealed class IntConverter : NbtConverter<int>
            {
                private IntConverter()
                {
                }

                public override int Read(ref NbtReader reader, NbtSerializerOptions options) =>
                    reader.GetInt32();

                public override void Write(
                    ref NbtWriter writer,
                    int value,
                    NbtSerializerOptions options) =>
                    writer.WriteInt32(value);
            }
            """,
        "wrong-type" => """
            internal sealed class Dto
            {
                [NbtUseConverter(typeof(StringConverter))]
                public int Value { get; set; }
            }

            internal sealed class StringConverter : NbtConverter<string>
            {
                public override string Read(ref NbtReader reader, NbtSerializerOptions options) =>
                    reader.GetString();

                public override void Write(
                    ref NbtWriter writer,
                    string value,
                    NbtSerializerOptions options) =>
                    writer.WriteString(value);
            }
            """,
        _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, "Unknown variant."),
    };

    private static string Source(string declaration) => $$"""
        using NbtSharp;

        [NbtSerializable(typeof(Dto))]
        internal partial class TestContext : NbtSerializerContext
        {
        }

        {{declaration}}
        """;

    private static NbtSerializerOptions ReflectionOptions() => new()
    {
        TypeInfoResolver = new NbtReflectionTypeInfoResolver(),
    };

    private static NbtSerializerOptions ModifierOptions(Action<NbtContractInfo> modifier) => new()
    {
        TypeInfoResolver = new NbtReflectionTypeInfoResolver(modifier),
    };

    private static GeneratorOutput Run(string source)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            parseOptions,
            cancellationToken: TestContext.Current.CancellationToken);
        var compilation = CSharpCompilation.Create(
            "ContractValidationTests",
            [syntaxTree],
            References,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var driver = CSharpGeneratorDriver.Create(
            [new NbtSourceGenerator().AsSourceGenerator()],
            parseOptions: parseOptions);
        GeneratorDriver generatorDriver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var generatorDiagnostics);

        return new GeneratorOutput(
            generatorDiagnostics,
            outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken),
            generatorDriver.GetRunResult().Results.Sum(static result => result.GeneratedSources.Length),
            string.Join(
                Environment.NewLine,
                generatorDriver.GetRunResult().Results
                    .SelectMany(static result => result.GeneratedSources)
                    .Select(static source => source.SourceText.ToString())));
    }

    private static ImmutableArray<MetadataReference> GetReferences()
    {
        var trustedPlatformAssemblies =
            (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "";
        return trustedPlatformAssemblies
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(
                typeof(NbtSerializer).Assembly.Location))
            .ToImmutableArray();
    }

    private sealed class GeneratorOutput
    {
        public GeneratorOutput(
            ImmutableArray<Diagnostic> generatorDiagnostics,
            ImmutableArray<Diagnostic> compilationDiagnostics,
            int generatedSourceCount,
            string generatedSource)
        {
            GeneratorDiagnostics = generatorDiagnostics;
            CompilationDiagnostics = compilationDiagnostics;
            GeneratedSourceCount = generatedSourceCount;
            GeneratedSource = generatedSource;
        }

        public ImmutableArray<Diagnostic> GeneratorDiagnostics { get; }

        public ImmutableArray<Diagnostic> CompilationDiagnostics { get; }

        public int GeneratedSourceCount { get; }

        public string GeneratedSource { get; }
    }

    private sealed class IgnoredRequiredDto
    {
        public required string Name { get; set; }

        public int Value { get; set; }
    }

    private sealed class BoundOnlyDto
    {
        public BoundOnlyDto(int a, int b)
        {
            A = a;
            B = b;
        }

        public int A { get; }

        public int B { get; }
    }

    private sealed class RenameDto
    {
        public string Name { get; set; } = "";

        public int Health { get; set; }
    }

    private sealed class PlainIntDto
    {
        public int Value { get; set; }
    }

    private class HiddenMemberBase
    {
        public virtual int Value { get; set; }
    }

    private sealed class HiddenMemberDerived : HiddenMemberBase
    {
        public new int Value { get; set; }
    }

    private sealed class AttributeCollisionDto
    {
        [NbtPropertyName("x")]
        public int A { get; set; }

        [NbtPropertyName("x")]
        public int B { get; set; }
    }

    private sealed class NoUsableCtorDto
    {
        public NoUsableCtorDto(int unrelated)
        {
            _ = unrelated;
        }
    }

    private sealed class SnapshotUnmappedRequiredDto
    {
        [NbtIgnore]
        public required string Ignored { get; init; }

        public int Value { get; set; }
    }

    private sealed class FieldUseConverterWithoutIncludeDto
    {
        [NbtUseConverter(typeof(ParityOffsetIntConverter))]
        public int Value = 1;
    }

    private sealed class ExtensionFieldWithoutIncludeDto
    {
        [NbtExtensionData]
        public NbtCompound Extra = new();
    }

    private sealed class AbstractConverterDto
    {
        [NbtUseConverter(typeof(ParityAbstractIntConverter))]
        public int Value { get; set; }
    }

    private sealed class NoDefaultCtorConverterDto
    {
        [NbtUseConverter(typeof(ParityNoDefaultCtorConverter))]
        public int Value { get; set; }
    }

    private sealed class WrongTypeConverterDto
    {
        [NbtUseConverter(typeof(ParityStringConverter))]
        public int Value { get; set; }
    }

    private sealed class IgnoredInvalidConverterDto
    {
        [NbtIgnore]
        [NbtUseConverter(typeof(ParityAbstractIntConverter))]
        public int Property { get; set; }

        [NbtIgnore]
        [NbtInclude]
        [NbtUseConverter(typeof(ParityAbstractIntConverter))]
        public int Field = 2;

        public int Kept { get; set; }
    }
}

public enum SerializationMode
{
    Generated,
    Reflection,
}

internal sealed class ParityOffsetIntConverter : NbtConverter<int>
{
    public override int Read(ref NbtReader reader, NbtSerializerOptions options) =>
        reader.GetInt32();

    public override void Write(ref NbtWriter writer, int value, NbtSerializerOptions options) =>
        writer.WriteInt32(value);

    public override bool TryGetTagType(
        int value,
        NbtSerializerOptions options,
        out NbtTagType tagType)
    {
        tagType = NbtTagType.Int;
        return true;
    }
}

internal abstract class ParityAbstractIntConverter : NbtConverter<int>
{
    public override int Read(ref NbtReader reader, NbtSerializerOptions options) =>
        reader.GetInt32();

    public override void Write(ref NbtWriter writer, int value, NbtSerializerOptions options) =>
        writer.WriteInt32(value);
}

internal sealed class ParityNoDefaultCtorConverter : NbtConverter<int>
{
    private ParityNoDefaultCtorConverter()
    {
    }

    public static ParityNoDefaultCtorConverter Create() => new();

    public override int Read(ref NbtReader reader, NbtSerializerOptions options) =>
        reader.GetInt32();

    public override void Write(ref NbtWriter writer, int value, NbtSerializerOptions options) =>
        writer.WriteInt32(value);
}

internal sealed class ParityStringConverter : NbtConverter<string>
{
    public override string Read(ref NbtReader reader, NbtSerializerOptions options) =>
        reader.GetString();

    public override void Write(ref NbtWriter writer, string value, NbtSerializerOptions options) =>
        writer.WriteString(value);
}

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace NbtSharp.SourceGeneration.Tests;

public sealed class NbtSourceGeneratorTests
{
    private static readonly ImmutableArray<MetadataReference> References = GetReferences();

    [Fact]
    public void GeneratesCompilableCodeForASupportedType()
    {
        var result = Run("""
            using NbtSharp;

            [NbtSerializable(typeof(Player))]
            internal partial class TestContext : NbtSerializerContext
            {
            }

            internal sealed class Player
            {
                public int Health { get; set; }
            }
            """);

        AssertNoErrors(result);
        Assert.Contains("partial class TestContext", result.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains("Converter_0", result.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains("GetTypeInfo", result.GeneratedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsNonPartialContext()
    {
        var result = Run("""
            using NbtSharp;

            [NbtSerializable(typeof(Player))]
            internal class TestContext : NbtSerializerContext
            {
            }

            internal sealed class Player
            {
                public int Health { get; set; }
            }
            """);

        Assert.Contains(result.GeneratorDiagnostics, static diagnostic => diagnostic.Id == "NBTSG001");
    }

    [Fact]
    public void ReportsInvalidContextBase()
    {
        var result = Run("""
            using NbtSharp;

            [NbtSerializable(typeof(Player))]
            internal partial class TestContext
            {
            }

            internal sealed class Player
            {
                public int Health { get; set; }
            }
            """);

        Assert.Contains(result.GeneratorDiagnostics, static diagnostic => diagnostic.Id == "NBTSG002");
    }

    [Fact]
    public void ReportsUnsupportedMemberType()
    {
        var result = Run("""
            using NbtSharp;

            [NbtSerializable(typeof(Player))]
            internal partial class TestContext : NbtSerializerContext
            {
            }

            internal sealed class Player
            {
                public decimal Amount { get; set; }
            }
            """);

        Assert.Contains(result.GeneratorDiagnostics, static diagnostic => diagnostic.Id == "NBTSG003");
    }

    [Fact]
    public void ReportsMissingConstructor()
    {
        var result = Run("""
            using NbtSharp;

            [NbtSerializable(typeof(NoConstructor))]
            internal partial class TestContext : NbtSerializerContext
            {
            }

            internal sealed class NoConstructor
            {
                public NoConstructor(int unrelated)
                {
                    _ = unrelated;
                }

                public int Value { get; }
            }
            """);

        Assert.Contains(result.GeneratorDiagnostics, static diagnostic => diagnostic.Id == "NBTSG004");
    }

    [Fact]
    public void ReportsDuplicateMemberNames()
    {
        var result = Run("""
            using NbtSharp;

            [NbtSerializable(typeof(DuplicateNames))]
            internal partial class TestContext : NbtSerializerContext
            {
            }

            internal sealed class DuplicateNames
            {
                [NbtPropertyName("x")]
                public int First { get; set; }

                [NbtPropertyName("x")]
                public int Second { get; set; }
            }
            """);

        Assert.Contains(result.GeneratorDiagnostics, static diagnostic => diagnostic.Id == "NBTSG005");
    }

    [Fact]
    public void ReportsBuiltInRootAsWarning()
    {
        var result = Run("""
            using NbtSharp;

            [NbtSerializable(typeof(int))]
            internal partial class TestContext : NbtSerializerContext
            {
            }
            """);

        var diagnostic = Assert.Single(
            result.GeneratorDiagnostics,
            static diagnostic => diagnostic.Id == "NBTSG013");
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
    }

    private static void AssertNoErrors(GeneratorRunResult result)
    {
        Assert.DoesNotContain(
            result.GeneratorDiagnostics,
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(
            result.CompilationDiagnostics,
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private static GeneratorRunResult Run(string source)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);
        var compilation = CSharpCompilation.Create(
            "GeneratorTests",
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

        var runResult = generatorDriver.GetRunResult();
        var generatedSource = string.Join(
            "\n",
            runResult.Results
                .SelectMany(static result => result.GeneratedSources)
                .Select(static result => result.SourceText.ToString()));

        return new GeneratorRunResult(
            generatorDiagnostics,
            outputCompilation.GetDiagnostics(),
            generatedSource);
    }

    private static ImmutableArray<MetadataReference> GetReferences()
    {
        var trustedPlatformAssemblies =
            (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "";
        return trustedPlatformAssemblies
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .Append((MetadataReference)MetadataReference.CreateFromFile(
                typeof(NbtSerializer).Assembly.Location))
            .ToImmutableArray();
    }

    private sealed class GeneratorRunResult
    {
        public GeneratorRunResult(
            ImmutableArray<Diagnostic> generatorDiagnostics,
            ImmutableArray<Diagnostic> compilationDiagnostics,
            string generatedSource)
        {
            GeneratorDiagnostics = generatorDiagnostics;
            CompilationDiagnostics = compilationDiagnostics;
            GeneratedSource = generatedSource;
        }

        public ImmutableArray<Diagnostic> GeneratorDiagnostics { get; }

        public ImmutableArray<Diagnostic> CompilationDiagnostics { get; }

        public string GeneratedSource { get; }
    }
}

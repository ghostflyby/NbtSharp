using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace NbtSharp.SourceGeneration.Tests;

public sealed class NbtSourceGeneratorTests
{
    private static readonly ImmutableArray<MetadataReference> References = GetReferences();

    [Fact]
    public void CachedContextsKeepDiagnostics()
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var tree = CSharpSyntaxTree.ParseText(
            """
            using NbtSharp;

            [NbtSerializable(typeof(Player))]
            internal partial class TestContext : NbtSerializerContext
            {
            }

            internal sealed class Player
            {
                public decimal Amount { get; set; }
            }
            """,
            parseOptions,
            cancellationToken: TestContext.Current.CancellationToken);
        var compilation = CSharpCompilation.Create(
            "GeneratorTests",
            [tree],
            References,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new NbtSourceGenerator().AsSourceGenerator()],
            parseOptions: parseOptions,
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true));

        driver = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);
        Assert.Contains(
            driver.GetRunResult().Diagnostics,
            static diagnostic => diagnostic.Id == "NBTSG003");

        var unrelatedTree = CSharpSyntaxTree.ParseText(
            "internal sealed class Unrelated { public int Value { get; set; } }",
            parseOptions,
            cancellationToken: TestContext.Current.CancellationToken);
        driver = driver.RunGenerators(
            compilation.AddSyntaxTrees(unrelatedTree),
            TestContext.Current.CancellationToken);

        Assert.Contains(
            driver.GetRunResult().Diagnostics,
            static diagnostic => diagnostic.Id == "NBTSG003");
    }

    [Fact]
    public void RefreshesDiagnosticLocationsWhenDeclarationsShift()
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var source = """
            using NbtSharp;

            [NbtSerializable(typeof(Player))]
            internal partial class TestContext : NbtSerializerContext
            {
            }

            internal sealed class Player
            {
                public decimal Amount { get; set; }
            }
            """;
        var tree = CSharpSyntaxTree.ParseText(
            source,
            parseOptions,
            path: "Player.cs",
            cancellationToken: TestContext.Current.CancellationToken);
        var compilation = CSharpCompilation.Create(
            "GeneratorTests",
            [tree],
            References,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        var driver = CreateTrackingDriver(parseOptions);
        ContextSpecCache.ResetForTests();

        driver = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);
        var before = GetDiagnosticLine(driver.GetRunResult(), "NBTSG003");

        var shiftedTree = CSharpSyntaxTree.ParseText(
            "\n" + source,
            parseOptions,
            path: "Player.cs",
            cancellationToken: TestContext.Current.CancellationToken);
        driver = driver.RunGenerators(
            compilation.ReplaceSyntaxTree(tree, shiftedTree),
            TestContext.Current.CancellationToken);

        var after = GetDiagnosticLine(driver.GetRunResult(), "NBTSG003");
        Assert.Equal(before + 1, after);
    }

    private static int GetDiagnosticLine(GeneratorDriverRunResult result, string diagnosticId)
    {
        var diagnostics = result.Diagnostics
            .Where(diagnostic => diagnostic.Id == diagnosticId)
            .ToArray();
        Assert.Single(diagnostics);
        return diagnostics[0].Location.GetLineSpan().StartLinePosition.Line;
    }

    [Fact]
    public void CachesGeneratedContextsForUnrelatedChanges()
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var sourceTree = CSharpSyntaxTree.ParseText(
            """
            using NbtSharp;

            [NbtSerializable(typeof(Player))]
            internal partial class TestContext : NbtSerializerContext
            {
            }

            internal sealed class Player
            {
                public int Health { get; set; }
            }
            """,
            parseOptions,
            cancellationToken: TestContext.Current.CancellationToken);
        var compilation = CSharpCompilation.Create(
            "GeneratorTests",
            [sourceTree],
            References,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new NbtSourceGenerator().AsSourceGenerator()],
            parseOptions: parseOptions,
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true));

        driver = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);

        var unrelatedTree = CSharpSyntaxTree.ParseText(
            "internal sealed class Unrelated { public int Value { get; set; } }",
            parseOptions,
            cancellationToken: TestContext.Current.CancellationToken);
        driver = driver.RunGenerators(
            compilation.AddSyntaxTrees(unrelatedTree),
            TestContext.Current.CancellationToken);

        var reasons = GetReasons(driver.GetRunResult(), "NbtGeneratedContexts");
        Assert.All(
            reasons,
            static reason => Assert.True(
                reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
                $"Unrelated changes must reuse generated contexts, but found {reason}."));

        var preparedReasons = GetReasons(driver.GetRunResult(), "NbtPreparedContexts");
        Assert.All(
            preparedReasons,
            static reason => Assert.True(
                reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
                $"Unrelated changes must reuse prepared contexts, but found {reason}."));

        var changedTree = CSharpSyntaxTree.ParseText(
            """
            using NbtSharp;

            [NbtSerializable(typeof(Player))]
            internal partial class TestContext : NbtSerializerContext
            {
            }

            internal sealed class Player
            {
                public int Health { get; set; }

                public int Armor { get; set; }
            }
            """,
            parseOptions,
            cancellationToken: TestContext.Current.CancellationToken);
        driver = driver.RunGenerators(
            compilation.ReplaceSyntaxTree(sourceTree, changedTree),
            TestContext.Current.CancellationToken);

        var changedReasons = GetReasons(driver.GetRunResult(), "NbtGeneratedContexts");
        Assert.Contains(
            changedReasons,
            static reason => reason is IncrementalStepRunReason.New or IncrementalStepRunReason.Modified);

        var changedPreparedReasons = GetReasons(driver.GetRunResult(), "NbtPreparedContexts");
        Assert.Contains(
            changedPreparedReasons,
            static reason => reason is IncrementalStepRunReason.New or IncrementalStepRunReason.Modified);
    }

    [Fact]
    public void InvalidatesPreparedContextWhenValueTypeKindChanges()
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var contextTree = CSharpSyntaxTree.ParseText(
            """
            using NbtSharp;

            [NbtSerializable(typeof(Dto))]
            internal partial class TestContext : NbtSerializerContext
            {
            }
            """,
            parseOptions,
            cancellationToken: TestContext.Current.CancellationToken);
        var structTree = CSharpSyntaxTree.ParseText(
            """
            internal record struct Dto
            {
                public int Value { get; init; }
            }
            """,
            parseOptions,
            cancellationToken: TestContext.Current.CancellationToken);
        var compilation = CSharpCompilation.Create(
            "GeneratorTests",
            [contextTree, structTree],
            References,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new NbtSourceGenerator().AsSourceGenerator()],
            parseOptions: parseOptions,
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true));

        driver = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);
        var structSource = GetGeneratedSource(driver.GetRunResult());

        var classTree = CSharpSyntaxTree.ParseText(
            """
            internal record class Dto
            {
                public int Value { get; init; }
            }
            """,
            parseOptions,
            cancellationToken: TestContext.Current.CancellationToken);
        driver = driver.RunGenerators(
            compilation.ReplaceSyntaxTree(structTree, classTree),
            TestContext.Current.CancellationToken);

        var result = driver.GetRunResult();
        var classSource = GetGeneratedSource(result);

        Assert.NotEqual(structSource, classSource);
        Assert.Contains(
            GetReasons(result, "NbtPreparedContexts"),
            static reason => reason is IncrementalStepRunReason.New or IncrementalStepRunReason.Modified);
    }

    [Fact]
    public void CachesGraphConstructionForUnrelatedChanges()
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var contextTree = CSharpSyntaxTree.ParseText(
            """
            using NbtSharp;

            [NbtSerializable(typeof(Player))]
            internal partial class TestContext : NbtSerializerContext
            {
            }
            """,
            parseOptions,
            cancellationToken: TestContext.Current.CancellationToken);
        var dtoTree = CSharpSyntaxTree.ParseText(
            """
            internal sealed class Player
            {
                public int Health { get; set; }
            }
            """,
            parseOptions,
            cancellationToken: TestContext.Current.CancellationToken);
        var compilation = CSharpCompilation.Create(
            "GeneratorTests",
            [contextTree, dtoTree],
            References,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        var driver = CreateTrackingDriver(parseOptions);
        ContextSpecCache.ResetForTests();
        ContextEmitter.ContextSpecBuilder.ResetGraphBuildCountForTests();

        driver = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);
        Assert.Equal(1, ContextEmitter.ContextSpecBuilder.GraphBuildCount);

        var unrelatedTree = CSharpSyntaxTree.ParseText(
            "internal sealed class Unrelated { public int Value { get; set; } }",
            parseOptions,
            cancellationToken: TestContext.Current.CancellationToken);
        driver = driver.RunGenerators(
            compilation.AddSyntaxTrees(unrelatedTree),
            TestContext.Current.CancellationToken);

        var result = driver.GetRunResult();
        Assert.Equal(1, ContextEmitter.ContextSpecBuilder.GraphBuildCount);
        AssertCached(result, "NbtContextModels");
        AssertCached(result, "NbtPreparedContexts");
        AssertCached(result, "NbtGeneratedContexts");
    }

    [Fact]
    public void RebuildsGraphWhenReachableTypeChanges()
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var contextTree = CSharpSyntaxTree.ParseText(
            """
            using NbtSharp;

            [NbtSerializable(typeof(Player))]
            internal partial class TestContext : NbtSerializerContext
            {
            }
            """,
            parseOptions,
            cancellationToken: TestContext.Current.CancellationToken);
        var dtoTree = CSharpSyntaxTree.ParseText(
            """
            internal sealed class Player
            {
                public int Health { get; set; }
            }
            """,
            parseOptions,
            cancellationToken: TestContext.Current.CancellationToken);
        var compilation = CSharpCompilation.Create(
            "GeneratorTests",
            [contextTree, dtoTree],
            References,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        var driver = CreateTrackingDriver(parseOptions);
        ContextSpecCache.ResetForTests();
        ContextEmitter.ContextSpecBuilder.ResetGraphBuildCountForTests();

        driver = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);
        Assert.Equal(1, ContextEmitter.ContextSpecBuilder.GraphBuildCount);

        var changedDtoTree = CSharpSyntaxTree.ParseText(
            """
            internal sealed class Player
            {
                public int Health { get; set; }

                public int Armor { get; set; }
            }
            """,
            parseOptions,
            cancellationToken: TestContext.Current.CancellationToken);
        driver = driver.RunGenerators(
            compilation.ReplaceSyntaxTree(dtoTree, changedDtoTree),
            TestContext.Current.CancellationToken);

        var result = driver.GetRunResult();
        Assert.True(
            ContextEmitter.ContextSpecBuilder.GraphBuildCount > 1,
            "Changing a reachable DTO must rebuild the context graph.");
        Assert.Contains(
            GetReasons(result, "NbtPreparedContexts"),
            static reason => reason is IncrementalStepRunReason.New or IncrementalStepRunReason.Modified);
        Assert.Contains(
            GetReasons(result, "NbtGeneratedContexts"),
            static reason => reason is IncrementalStepRunReason.New or IncrementalStepRunReason.Modified);
    }

    [Fact]
    public void OnlyRebuildsChangedContextInLargeCompilation()
    {
        const int contextCount = 300;
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var trees = new List<SyntaxTree>(contextCount);
        for (var i = 0; i < contextCount; i++)
        {
            var source = $$"""
                using NbtSharp;

                [NbtSerializable(typeof(Dto{{i}}))]
                internal partial class Context{{i}} : NbtSerializerContext
                {
                }

                internal sealed class Dto{{i}}
                {
                    public int Value { get; set; }
                }
                """;
            trees.Add(CSharpSyntaxTree.ParseText(
                source,
                parseOptions,
                path: $"Context{i}.cs",
                cancellationToken: TestContext.Current.CancellationToken));
        }

        var compilation = CSharpCompilation.Create(
            "LargeGeneratorTests",
            trees,
            References,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        var driver = CreateTrackingDriver(parseOptions);
        ContextSpecCache.ResetForTests();
        ContextEmitter.ContextSpecBuilder.ResetGraphBuildCountForTests();

        driver = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);
        Assert.Equal(contextCount, ContextEmitter.ContextSpecBuilder.GraphBuildCount);

        const int changedIndex = 42;
        var changedSource = $$"""
            using NbtSharp;

            [NbtSerializable(typeof(Dto{{changedIndex}}))]
            internal partial class Context{{changedIndex}} : NbtSerializerContext
            {
            }

            internal sealed class Dto{{changedIndex}}
            {
                public int Value { get; set; }

                public int Armor { get; set; }
            }
            """;
        var changedTree = CSharpSyntaxTree.ParseText(
            changedSource,
            parseOptions,
            path: $"Context{changedIndex}.cs",
            cancellationToken: TestContext.Current.CancellationToken);
        driver = driver.RunGenerators(
            compilation.ReplaceSyntaxTree(trees[changedIndex], changedTree),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            1,
            ContextEmitter.ContextSpecBuilder.GraphBuildCount - contextCount);
        var preparedReasons = GetReasons(driver.GetRunResult(), "NbtPreparedContexts");
        Assert.Equal(contextCount, preparedReasons.Count);
        Assert.Equal(
            1,
            preparedReasons.Count(static reason =>
                reason is IncrementalStepRunReason.New or IncrementalStepRunReason.Modified));
    }

    [Fact]
    public void OnlyRebuildsContextsWhoseDeclarationsChangeInSharedFile()
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var sourceTree = CSharpSyntaxTree.ParseText(
            """
            using NbtSharp;

            [NbtSerializable(typeof(DtoA))]
            internal partial class ContextA : NbtSerializerContext
            {
            }

            [NbtSerializable(typeof(DtoB))]
            internal partial class ContextB : NbtSerializerContext
            {
            }

            internal sealed class DtoA
            {
                public int Value { get; set; }
            }

            internal sealed class DtoB
            {
                public int Value { get; set; }
            }
            """,
            parseOptions,
            path: "Shared.cs",
            cancellationToken: TestContext.Current.CancellationToken);
        var compilation = CSharpCompilation.Create(
            "SharedFileGeneratorTests",
            [sourceTree],
            References,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        var driver = CreateTrackingDriver(parseOptions);
        ContextSpecCache.ResetForTests();
        ContextEmitter.ContextSpecBuilder.ResetGraphBuildCountForTests();

        driver = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);
        Assert.Equal(2, ContextEmitter.ContextSpecBuilder.GraphBuildCount);

        var changedTree = CSharpSyntaxTree.ParseText(
            """
            using NbtSharp;

            [NbtSerializable(typeof(DtoA))]
            internal partial class ContextA : NbtSerializerContext
            {
            }

            [NbtSerializable(typeof(DtoB))]
            internal partial class ContextB : NbtSerializerContext
            {
            }

            internal sealed class DtoA
            {
                public int Value { get; set; }

                public int Armor { get; set; }
            }

            internal sealed class DtoB
            {
                public int Value { get; set; }
            }
            """,
            parseOptions,
            path: "Shared.cs",
            cancellationToken: TestContext.Current.CancellationToken);
        driver = driver.RunGenerators(
            compilation.ReplaceSyntaxTree(sourceTree, changedTree),
            TestContext.Current.CancellationToken);

        Assert.Equal(1, ContextEmitter.ContextSpecBuilder.GraphBuildCount - 2);
        var preparedReasons = GetReasons(driver.GetRunResult(), "NbtPreparedContexts");
        Assert.Equal(2, preparedReasons.Count);
        Assert.Equal(
            1,
            preparedReasons.Count(static reason =>
                reason is IncrementalStepRunReason.New or IncrementalStepRunReason.Modified));
    }

    [Fact]
    public void InvalidatesNestedTypeWhenContainingTypeShapeChanges()
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var sourceTree = CSharpSyntaxTree.ParseText(
            """
            using NbtSharp;

            [NbtSerializable(typeof(Outer.Inner))]
            public partial class TestContext : NbtSerializerContext
            {
            }

            public static class Outer
            {
                public sealed class Inner
                {
                    public int Value { get; set; }
                }
            }
            """,
            parseOptions,
            path: "Nested.cs",
            cancellationToken: TestContext.Current.CancellationToken);
        var compilation = CSharpCompilation.Create(
            "NestedGeneratorTests",
            [sourceTree],
            References,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        var driver = CreateTrackingDriver(parseOptions);
        ContextSpecCache.ResetForTests();

        driver = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);
        var before = GetGeneratedSource(driver.GetRunResult());

        var changedTree = CSharpSyntaxTree.ParseText(
            """
            using NbtSharp;

            [NbtSerializable(typeof(Outer.Inner))]
            public partial class TestContext : NbtSerializerContext
            {
            }

            internal static class Outer
            {
                public sealed class Inner
                {
                    public int Value { get; set; }
                }
            }
            """,
            parseOptions,
            path: "Nested.cs",
            cancellationToken: TestContext.Current.CancellationToken);
        driver = driver.RunGenerators(
            compilation.ReplaceSyntaxTree(sourceTree, changedTree),
            TestContext.Current.CancellationToken);

        var result = driver.GetRunResult();
        Assert.NotEqual(before, GetGeneratedSource(result));
        Assert.Contains(
            GetReasons(result, "NbtPreparedContexts"),
            static reason => reason is IncrementalStepRunReason.New or IncrementalStepRunReason.Modified);
    }

    private static GeneratorDriver CreateTrackingDriver(CSharpParseOptions parseOptions) =>
        CSharpGeneratorDriver.Create(
            [new NbtSourceGenerator().AsSourceGenerator()],
            parseOptions: parseOptions,
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true));

    private static void AssertCached(GeneratorDriverRunResult result, string stepName)
    {
        var reasons = GetReasons(result, stepName);
        Assert.NotEmpty(reasons);
        Assert.All(
            reasons,
            static reason => Assert.True(
                reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
                $"Expected cached step output, but found {reason}."));
    }

    private static string GetGeneratedSource(GeneratorDriverRunResult result) =>
        string.Join(
            "\n",
            result.Results
                .SelectMany(static generatorResult => generatorResult.GeneratedSources)
                .Select(static source => source.SourceText.ToString()));

    private static IReadOnlyList<IncrementalStepRunReason> GetReasons(
        GeneratorDriverRunResult result,
        string stepName)
    {
        Assert.True(
            result.Results[0].TrackedSteps.TryGetValue(stepName, out var steps),
            $"The generator did not track the step '{stepName}'.");
        return steps
            .SelectMany(static step => step.Outputs)
            .Select(static output => output.Reason)
            .ToArray();
    }

    [Fact]
    public void SupportsPublicContextWithInternalRootTypes()
    {
        var result = Run("""
            using NbtSharp;

            [NbtSerializable(typeof(InternalDto))]
            public partial class PublicContext : NbtSerializerContext
            {
            }

            internal sealed class InternalDto
            {
                public int Value { get; set; }
            }
            """);

        AssertNoErrors(result);
        Assert.Contains(
            "internal global::NbtSharp.NbtTypeInfo<global::InternalDto> InternalDto",
            result.GeneratedSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsDefaultPropertyNameConflicts()
    {
        var result = Run("""
            using NbtSharp;

            [NbtSerializable(typeof(First.Player))]
            [NbtSerializable(typeof(Second.Player))]
            internal partial class TestContext : NbtSerializerContext
            {
            }

            namespace First
            {
                internal sealed class Player
                {
                    public int Value { get; set; }
                }
            }

            namespace Second
            {
                internal sealed class Player
                {
                    public int Value { get; set; }
                }
            }
            """);

        Assert.Contains(result.GeneratorDiagnostics, static diagnostic => diagnostic.Id == "NBTSG008");
    }

    [Fact]
    public void ReportsReservedGeneratedNameConflicts()
    {
        var result = Run("""
            using NbtSharp;

            [NbtSerializable(typeof(Dto), TypeInfoPropertyName = "__typeInfo0")]
            public partial class TestContext : NbtSerializerContext
            {
            }

            public sealed class Dto
            {
                public int Value { get; set; }
            }
            """);

        Assert.Contains(result.GeneratorDiagnostics, static diagnostic => diagnostic.Id == "NBTSG008");
    }

    [Fact]
    public void ReportsDefaultContextNameConflict()
    {
        var result = Run("""
            using NbtSharp;

            [NbtSerializable(typeof(Dto))]
            public partial class Default : NbtSerializerContext
            {
            }

            public sealed class Dto
            {
                public int Value { get; set; }
            }
            """);

        Assert.Contains(result.GeneratorDiagnostics, static diagnostic => diagnostic.Id == "NBTSG008");
    }

    [Fact]
    public void ReportsFileLocalRootTypes()
    {
        var result = Run("""
            using NbtSharp;

            [NbtSerializable(typeof(FileDto))]
            internal partial class TestContext : NbtSerializerContext
            {
            }

            file sealed class FileDto
            {
                public int Value { get; set; }
            }
            """);

        Assert.Contains(result.GeneratorDiagnostics, static diagnostic => diagnostic.Id == "NBTSG006");
    }

    [Fact]
    public void ReportsContextWithoutParameterlessConstructor()
    {
        var result = Run("""
            using NbtSharp;

            [NbtSerializable(typeof(Dto))]
            public partial class TestContext : NbtSerializerContext
            {
                public TestContext(NbtSerializerOptions options)
                    : base(options)
                {
                }
            }

            public sealed class Dto
            {
                public int Value { get; set; }
            }
            """);

        Assert.Contains(result.GeneratorDiagnostics, static diagnostic => diagnostic.Id == "NBTSG002");
    }

    [Fact]
    public void ReportsRefConstructorAsUnusable()
    {
        var result = Run("""
            using NbtSharp;

            [NbtSerializable(typeof(Dto))]
            internal partial class TestContext : NbtSerializerContext
            {
            }

            internal sealed class Dto
            {
                public Dto(ref int value)
                {
                    _ = value;
                }

                public int Value { get; set; }
            }
            """);

        Assert.Contains(result.GeneratorDiagnostics, static diagnostic => diagnostic.Id == "NBTSG004");
    }

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
    public void SupportsRuntimeConverterOptOutForUnsupportedType()
    {
        var result = Run("""
            using NbtSharp;

            [NbtSerializable(typeof(Player))]
            internal partial class TestContext : NbtSerializerContext
            {
            }

            internal sealed class Player
            {
                [NbtUseConverter]
                public decimal Amount { get; set; }
            }
            """);

        Assert.DoesNotContain(result.GeneratorDiagnostics, static diagnostic => diagnostic.Id == "NBTSG003");
        Assert.DoesNotContain(result.GeneratorDiagnostics, static diagnostic => diagnostic.Id == "NBTSG016");
        AssertNoErrors(result);
        Assert.Contains("ReadValue<decimal>", result.GeneratedSource, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "UnconditionalSuppressMessage",
            result.GeneratedSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SupportsExplicitConverterOptOutForUnsupportedType()
    {
        var result = Run("""
            using System;
            using NbtSharp;

            [NbtSerializable(typeof(Player))]
            internal partial class TestContext : NbtSerializerContext
            {
            }

            internal sealed class Player
            {
                [NbtUseConverter(typeof(GuidConverter))]
                public Guid Id { get; set; }
            }

            internal sealed class GuidConverter : NbtConverter<Guid>
            {
                public override Guid Read(ref NbtReader reader, NbtSerializerOptions options) =>
                    Guid.Parse(reader.GetString());

                public override void Write(ref NbtWriter writer, Guid value, NbtSerializerOptions options) =>
                    writer.WriteString(value.ToString());

                public override bool TryGetTagType(
                    Guid value,
                    NbtSerializerOptions options,
                    out NbtTagType tagType)
                {
                    tagType = NbtTagType.String;
                    return true;
                }
            }
            """);

        Assert.DoesNotContain(result.GeneratorDiagnostics, static diagnostic => diagnostic.Id == "NBTSG003");
        Assert.DoesNotContain(result.GeneratorDiagnostics, static diagnostic => diagnostic.Id == "NBTSG016");
        AssertNoErrors(result);
        Assert.Contains(
            "ReadValueWithConverter<global::System.Guid>",
            result.GeneratedSource,
            StringComparison.Ordinal);
        Assert.Contains("WriteMemberWithConverter(", result.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains("new global::GuidConverter()", result.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains(
            "UnconditionalSuppressMessage",
            result.GeneratedSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsInvalidExplicitConverterType()
    {
        var result = Run("""
            using System;
            using NbtSharp;

            [NbtSerializable(typeof(Player))]
            internal partial class TestContext : NbtSerializerContext
            {
            }

            internal sealed class Player
            {
                [NbtUseConverter(typeof(string))]
                public Guid Id { get; set; }
            }
            """);

        Assert.Contains(result.GeneratorDiagnostics, static diagnostic => diagnostic.Id == "NBTSG016");
    }

    [Fact]
    public void RejectsConverterThatHandlesDifferentMemberType()
    {
        var result = Run("""
            using System;
            using NbtSharp;

            [NbtSerializable(typeof(Player))]
            internal partial class TestContext : NbtSerializerContext
            {
            }

            internal sealed class Player
            {
                [NbtUseConverter(typeof(GuidConverter))]
                public Guid? Id { get; set; }
            }

            internal sealed class GuidConverter : NbtConverter<Guid>
            {
                public override Guid Read(ref NbtReader reader, NbtSerializerOptions options) =>
                    Guid.Parse(reader.GetString());

                public override void Write(ref NbtWriter writer, Guid value, NbtSerializerOptions options) =>
                    writer.WriteString(value.ToString());

                public override bool TryGetTagType(
                    Guid value,
                    NbtSerializerOptions options,
                    out NbtTagType tagType)
                {
                    tagType = NbtTagType.String;
                    return true;
                }
            }
            """);

        Assert.Contains(result.GeneratorDiagnostics, static diagnostic => diagnostic.Id == "NBTSG016");
    }

    [Fact]
    public void RejectsAbstractExplicitConverter()
    {
        var result = Run("""
            using System;
            using NbtSharp;

            [NbtSerializable(typeof(Player))]
            internal partial class TestContext : NbtSerializerContext
            {
            }

            internal sealed class Player
            {
                [NbtUseConverter(typeof(GuidConverter))]
                public Guid Id { get; set; }
            }

            internal abstract class GuidConverter : NbtConverter<Guid>
            {
                public override Guid Read(ref NbtReader reader, NbtSerializerOptions options) =>
                    Guid.Parse(reader.GetString());

                public override void Write(ref NbtWriter writer, Guid value, NbtSerializerOptions options) =>
                    writer.WriteString(value.ToString());
            }
            """);

        Assert.Contains(result.GeneratorDiagnostics, static diagnostic => diagnostic.Id == "NBTSG016");
    }

    [Fact]
    public void RejectsConverterWithoutPublicParameterlessConstructor()
    {
        var result = Run("""
            using System;
            using NbtSharp;

            [NbtSerializable(typeof(Player))]
            internal partial class TestContext : NbtSerializerContext
            {
            }

            internal sealed class Player
            {
                [NbtUseConverter(typeof(GuidConverter))]
                public Guid Id { get; set; }
            }

            internal sealed class GuidConverter : NbtConverter<Guid>
            {
                private GuidConverter()
                {
                }

                public override Guid Read(ref NbtReader reader, NbtSerializerOptions options) =>
                    Guid.Parse(reader.GetString());

                public override void Write(ref NbtWriter writer, Guid value, NbtSerializerOptions options) =>
                    writer.WriteString(value.ToString());
            }
            """);

        Assert.Contains(result.GeneratorDiagnostics, static diagnostic => diagnostic.Id == "NBTSG016");
    }

    [Fact]
    public void ReportsFieldConverterWithoutInclude()
    {
        var result = Run("""
            using NbtSharp;

            [NbtSerializable(typeof(Player))]
            internal partial class TestContext : NbtSerializerContext
            {
            }

            internal sealed class Player
            {
                [NbtUseConverter]
                public decimal Amount;
            }
            """);

        Assert.Contains(result.GeneratorDiagnostics, static diagnostic => diagnostic.Id == "NBTSG016");
    }

    [Fact]
    public void ExplicitConverterFactoryDoesNotSuppressAotWarnings()
    {
        var result = Run("""
            using System;
            using NbtSharp;

            [NbtSerializable(typeof(Player))]
            internal partial class TestContext : NbtSerializerContext
            {
            }

            internal sealed class Player
            {
                [NbtUseConverter(typeof(GuidFactory))]
                public Guid Id { get; set; }
            }

            internal sealed class GuidFactory : NbtConverterFactory
            {
                public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(Guid);

                public override NbtConverter CreateConverter(
                    Type typeToConvert,
                    NbtSerializerOptions options) => new GuidConverter();
            }

            internal sealed class GuidConverter : NbtConverter<Guid>
            {
                public override Guid Read(ref NbtReader reader, NbtSerializerOptions options) =>
                    Guid.Parse(reader.GetString());

                public override void Write(ref NbtWriter writer, Guid value, NbtSerializerOptions options) =>
                    writer.WriteString(value.ToString());
            }
            """);

        Assert.DoesNotContain(result.GeneratorDiagnostics, static diagnostic => diagnostic.Id == "NBTSG016");
        AssertNoErrors(result);
        Assert.Contains("new global::GuidFactory()", result.GeneratedSource, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "UnconditionalSuppressMessage",
            result.GeneratedSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsUnmappedRequiredMembers()
    {
        var result = Run("""
            using NbtSharp;

            [NbtSerializable(typeof(Dto))]
            internal partial class TestContext : NbtSerializerContext
            {
            }

            internal sealed class Dto
            {
                [NbtIgnore]
                public required string Ignored { get; init; }

                public int Value { get; set; }
            }
            """);

        Assert.Contains(result.GeneratorDiagnostics, static diagnostic => diagnostic.Id == "NBTSG015");
    }

    [Fact]
    public void AllowsUnmappedRequiredMembersWithSetsRequiredMembers()
    {
        var result = Run("""
            using System.Diagnostics.CodeAnalysis;
            using NbtSharp;

            [NbtSerializable(typeof(Dto))]
            internal partial class TestContext : NbtSerializerContext
            {
            }

            internal sealed class Dto
            {
                [NbtIgnore]
                public required string Ignored { get; init; }

                public int Value { get; set; }

                [SetsRequiredMembers]
                public Dto()
                {
                    Ignored = "";
                }
            }
            """);

        AssertNoErrors(result);
        Assert.DoesNotContain(
            result.GeneratorDiagnostics,
            static diagnostic => diagnostic.Id == "NBTSG015");
    }

    [Fact]
    public void ReportsRequiredValueTypes()
    {
        var result = Run("""
            using NbtSharp;

            [NbtSerializable(typeof(Dto))]
            internal partial class TestContext : NbtSerializerContext
            {
            }

            internal struct Dto
            {
                public required int Value { get; init; }
            }
            """);

        Assert.Contains(result.GeneratorDiagnostics, static diagnostic => diagnostic.Id == "NBTSG007");
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

    [SuppressMessage("ReSharper", "ParameterOnlyUsedForPreconditionCheck.Local")]
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
            .Append(MetadataReference.CreateFromFile(
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

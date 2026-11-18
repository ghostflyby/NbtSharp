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
        AssertCached(result, "NbtPreparedContexts");
        AssertCached(result, "NbtGeneratedContexts");
    }

    [Fact]
    public void ContextSpecCacheEvictsOldEntriesAndSkipsDiagnosticSpecs()
    {
        ContextSpecCache.ResetForTests();
        ContextSpecCache.SetCapacityForTests(2);
        try
        {
            for (var i = 0; i < 3; i++)
            {
                var spec = new ContextSpec(
                    $"C{i}.g.cs",
                    "",
                    $"C{i}",
                    "internal",
                    isRecord: false,
                    hasSource: true,
                    ImmutableArray<TypeSpec>.Empty,
                    ImmutableArray<DiagnosticSpec>.Empty);
                ContextSpecCache.Add($"key{i}", new PreparedContext(spec));
            }

            Assert.False(ContextSpecCache.TryGet("key0", out _));
            Assert.True(ContextSpecCache.TryGet("key1", out _));
            Assert.True(ContextSpecCache.TryGet("key2", out _));

            var diagnostic = DiagnosticSpec.Create(Diagnostics.ContextInvalid, null, "C", "invalid");
            var diagnosticSpec = new ContextSpec(
                "Diagnostic.g.cs",
                "",
                "Diagnostic",
                "internal",
                isRecord: false,
                hasSource: false,
                ImmutableArray<TypeSpec>.Empty,
                ImmutableArray.Create(diagnostic));
            ContextSpecCache.Add("diagnostic", new PreparedContext(diagnosticSpec));
            Assert.False(ContextSpecCache.TryGet("diagnostic", out _));
        }
        finally
        {
            ContextSpecCache.ResetForTests();
        }
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
    public void RebuildsGraphWhenInterfaceElementTypeChanges()
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var contextTree = CSharpSyntaxTree.ParseText(
            """
            using System.Collections.Generic;
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
            using System.Collections.Generic;

            internal sealed class Player
            {
                public IReadOnlyList<Item> Items { get; set; } = [];
            }
            """,
            parseOptions,
            cancellationToken: TestContext.Current.CancellationToken);
        var itemTree = CSharpSyntaxTree.ParseText(
            """
            internal sealed class Item
            {
                public int Health { get; set; }
            }
            """,
            parseOptions,
            cancellationToken: TestContext.Current.CancellationToken);
        var compilation = CSharpCompilation.Create(
            "GeneratorTests",
            [contextTree, dtoTree, itemTree],
            References,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        var driver = CreateTrackingDriver(parseOptions);
        ContextSpecCache.ResetForTests();
        ContextEmitter.ContextSpecBuilder.ResetGraphBuildCountForTests();

        driver = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);
        Assert.Equal(1, ContextEmitter.ContextSpecBuilder.GraphBuildCount);

        var changedItemTree = CSharpSyntaxTree.ParseText(
            """
            internal sealed class Item
            {
                public int Health { get; set; }

                public int Armor { get; set; }
            }
            """,
            parseOptions,
            cancellationToken: TestContext.Current.CancellationToken);
        driver = driver.RunGenerators(
            compilation.ReplaceSyntaxTree(itemTree, changedItemTree),
            TestContext.Current.CancellationToken);

        Assert.True(
            ContextEmitter.ContextSpecBuilder.GraphBuildCount > 1,
            "Changing an element type reached through a collection interface must rebuild the graph.");
        Assert.Contains(
            "Armor",
            GetGeneratedSource(driver.GetRunResult()),
            StringComparison.Ordinal);
        Assert.Contains(
            GetReasons(driver.GetRunResult(), "NbtPreparedContexts"),
            static reason => reason is IncrementalStepRunReason.New or IncrementalStepRunReason.Modified);
    }

    [Fact]
    public void RebuildsWhenCollectionBuilderMethodChanges()
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
                public CustomBox<int> Items { get; set; } = new([]);
            }
            """,
            parseOptions,
            cancellationToken: TestContext.Current.CancellationToken);
        var boxTree = CSharpSyntaxTree.ParseText(
            """
            using System.Collections;
            using System.Collections.Generic;
            using System.Runtime.CompilerServices;

            [CollectionBuilder(typeof(BoxFactory), "Create")]
            internal sealed class CustomBox<T> : IEnumerable<T>
            {
                private readonly T[] _items;
                public CustomBox(T[] items) => _items = items;
                public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();
                IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
            }

            internal static class BoxFactory
            {
                public static CustomBox<T> Create<T>(T[] items) => new(items);
            }
            """,
            parseOptions,
            cancellationToken: TestContext.Current.CancellationToken);
        var compilation = CSharpCompilation.Create(
            "GeneratorTests",
            [contextTree, dtoTree, boxTree],
            References,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        var driver = CreateTrackingDriver(parseOptions);
        ContextSpecCache.ResetForTests();
        ContextEmitter.ContextSpecBuilder.ResetGraphBuildCountForTests();

        driver = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);
        Assert.Equal(1, ContextEmitter.ContextSpecBuilder.GraphBuildCount);

        var changedBoxTree = CSharpSyntaxTree.ParseText(
            """
            using System.Collections;
            using System.Collections.Generic;
            using System.Runtime.CompilerServices;

            [CollectionBuilder(typeof(BoxFactory), "Create")]
            internal sealed class CustomBox<T> : IEnumerable<T>
            {
                private readonly T[] _items;
                public CustomBox(T[] items) => _items = items;
                public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();
                IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
            }

            internal static class BoxFactory
            {
                // The builder implementation is part of the reachable fingerprint.
                public static CustomBox<T> Create<T>(T[] items) => new(items);
            }
            """,
            parseOptions,
            cancellationToken: TestContext.Current.CancellationToken);
        driver.RunGenerators(
            compilation.ReplaceSyntaxTree(boxTree, changedBoxTree),
            TestContext.Current.CancellationToken);

        Assert.True(
            ContextEmitter.ContextSpecBuilder.GraphBuildCount > 1,
            "Changing a CollectionBuilderAttribute builder method must rebuild the graph.");
    }

    [Fact]
    public void ReportsCollectionWithoutUsableConstruction()
    {
        var result = Run("""
            using System;
            using System.Collections;
            using System.Collections.Generic;
            using NbtSharp;

            [NbtSerializable(typeof(Player))]
            internal partial class TestContext : NbtSerializerContext
            {
            }

            internal sealed class Player
            {
                public MyCol<int> Values { get; set; } = new(Comparer<int>.Default);
            }

            internal sealed class MyCol<T> : IEnumerable<T>
            {
                public MyCol(IComparer<T> comparer)
                {
                }

                public void Add(T item)
                {
                }

                public IEnumerator<T> GetEnumerator() =>
                    ((IEnumerable<T>)Array.Empty<T>()).GetEnumerator();

                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            }
            """);

        Assert.Contains(result.GeneratorDiagnostics, static diagnostic => diagnostic.Id == "NBTSG011");
    }

    [Fact]
    public void ReportsPriorityQueueAsUnsupported()
    {
        var result = Run("""
            using System.Collections.Generic;
            using NbtSharp;

            [NbtSerializable(typeof(Player))]
            internal partial class TestContext : NbtSerializerContext
            {
            }

            internal sealed class Player
            {
                public PriorityQueue<int, int> Values { get; set; } = new();
            }
            """);

        var diagnostic = Assert.Single(
            result.GeneratorDiagnostics,
            static diagnostic => diagnostic.Id == "NBTSG003");
        Assert.Contains("priority queue", diagnostic.GetMessage(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReportsNestedPriorityQueueAsUnsupported()
    {
        var result = Run("""
            using System.Collections.Generic;
            using NbtSharp;

            [NbtSerializable(typeof(Player))]
            internal partial class TestContext : NbtSerializerContext
            {
            }

            internal sealed class Player
            {
                public List<PriorityQueue<int, int>> Values { get; set; } = [];
            }
            """);

        Assert.Contains(
            result.GeneratorDiagnostics,
            static diagnostic =>
                diagnostic.Id == "NBTSG003" &&
                diagnostic.GetMessage().Contains("priority queue", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PriorityQueueMemberCanUseAConverter()
    {
        var result = Run("""
            using System.Collections.Generic;
            using NbtSharp;

            [NbtSerializable(typeof(Player))]
            internal partial class TestContext : NbtSerializerContext
            {
            }

            internal sealed class Player
            {
                [NbtUseConverter(typeof(PriorityQueueConverter))]
                public PriorityQueue<int, int> Values { get; set; } = new();
            }

            internal sealed class PriorityQueueConverter : NbtConverter<PriorityQueue<int, int>>
            {
                public override PriorityQueue<int, int> Read(
                    ref NbtReader reader,
                    NbtSerializerOptions options) => new();

                public override void Write(
                    ref NbtWriter writer,
                    PriorityQueue<int, int> value,
                    NbtSerializerOptions options)
                {
                }
            }
            """);

        Assert.DoesNotContain(
            result.GeneratorDiagnostics,
            static diagnostic => diagnostic.Id == "NBTSG003");
        AssertNoErrors(result);
    }

    [Fact]
    public void ReportsReadOnlyMapWithoutMutation()
    {
        var result = Run("""
            using System.Collections;
            using System.Collections.Generic;
            using NbtSharp;

            [NbtSerializable(typeof(Player))]
            internal partial class TestContext : NbtSerializerContext
            {
            }

            internal sealed class Player
            {
                public MyMap Values { get; set; } = new();
            }

            internal sealed class MyMap : IReadOnlyDictionary<string, int>
            {
                private readonly Dictionary<string, int> _inner = [];

                public int this[string key] => _inner[key];

                public IEnumerable<string> Keys => _inner.Keys;

                public IEnumerable<int> Values => _inner.Values;

                public int Count => _inner.Count;

                public bool ContainsKey(string key) => _inner.ContainsKey(key);

                public bool TryGetValue(string key, out int value) =>
                    _inner.TryGetValue(key, out value);

                public IEnumerator<KeyValuePair<string, int>> GetEnumerator() =>
                    _inner.GetEnumerator();

                IEnumerator IEnumerable.GetEnumerator() => _inner.GetEnumerator();
            }
            """);

        Assert.Contains(result.GeneratorDiagnostics, static diagnostic => diagnostic.Id == "NBTSG011");
    }

    [Fact]
    public void RebuildsWhenNonGenericCollectionElementTypeChanges()
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
                public ItemBag Items { get; set; } = new();
            }
            """,
            parseOptions,
            cancellationToken: TestContext.Current.CancellationToken);
        var itemTree = CSharpSyntaxTree.ParseText(
            """
            internal sealed class Item
            {
                public int Health { get; set; }
            }
            """,
            parseOptions,
            cancellationToken: TestContext.Current.CancellationToken);
        var bagTree = CSharpSyntaxTree.ParseText(
            """
            using System.Collections;
            using System.Collections.Generic;

            internal sealed class ItemBag : IEnumerable<Item>
            {
                private readonly List<Item> _items = [];
                public void Add(Item item) => _items.Add(item);
                public IEnumerator<Item> GetEnumerator() => _items.GetEnumerator();
                IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
            }
            """,
            parseOptions,
            cancellationToken: TestContext.Current.CancellationToken);
        var compilation = CSharpCompilation.Create(
            "GeneratorTests",
            [contextTree, dtoTree, itemTree, bagTree],
            References,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        var driver = CreateTrackingDriver(parseOptions);
        ContextSpecCache.ResetForTests();
        ContextEmitter.ContextSpecBuilder.ResetGraphBuildCountForTests();

        driver = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);
        Assert.Equal(1, ContextEmitter.ContextSpecBuilder.GraphBuildCount);

        var changedItemTree = CSharpSyntaxTree.ParseText(
            """
            internal sealed class Item
            {
                public int Health { get; set; }

                public int Armor { get; set; }
            }
            """,
            parseOptions,
            cancellationToken: TestContext.Current.CancellationToken);
        driver.RunGenerators(
            compilation.ReplaceSyntaxTree(itemTree, changedItemTree),
            TestContext.Current.CancellationToken);

        Assert.True(
            ContextEmitter.ContextSpecBuilder.GraphBuildCount > 1,
            "Changing an element of a non-generic collection wrapper must rebuild the graph.");
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

        var unrelatedTree = CSharpSyntaxTree.ParseText(
            "internal sealed class Unrelated { public int Value { get; set; } }",
            parseOptions,
            path: "Unrelated.cs",
            cancellationToken: TestContext.Current.CancellationToken);
        var withUnrelatedTree = compilation.AddSyntaxTrees(unrelatedTree);
        driver = driver.RunGenerators(withUnrelatedTree, TestContext.Current.CancellationToken);
        Assert.Equal(contextCount, ContextEmitter.ContextSpecBuilder.GraphBuildCount);
        var unrelatedReasons = GetReasons(driver.GetRunResult(), "NbtPreparedContexts");
        Assert.Equal(contextCount, unrelatedReasons.Count);
        Assert.DoesNotContain(
            unrelatedReasons,
            static reason => reason is IncrementalStepRunReason.New or IncrementalStepRunReason.Modified);

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
            withUnrelatedTree.ReplaceSyntaxTree(trees[changedIndex], changedTree),
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

    [Fact]
    public void InvalidatesNestedEnumWhenContainingTypeShapeChanges()
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var sourceTree = CSharpSyntaxTree.ParseText(
            """
            using NbtSharp;

            [NbtSerializable(typeof(Outer.Status))]
            public partial class TestContext : NbtSerializerContext
            {
            }

            public static class Outer
            {
                public enum Status
                {
                    Idle,
                }
            }
            """,
            parseOptions,
            path: "NestedEnum.cs",
            cancellationToken: TestContext.Current.CancellationToken);
        var compilation = CSharpCompilation.Create(
            "NestedEnumGeneratorTests",
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

            [NbtSerializable(typeof(Outer.Status))]
            public partial class TestContext : NbtSerializerContext
            {
            }

            internal static class Outer
            {
                public enum Status
                {
                    Idle,
                }
            }
            """,
            parseOptions,
            path: "NestedEnum.cs",
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
    public void EmitsDirectPathsForBooleanStringAndNullableScalars()
    {
        var result = Run("""
            using NbtSharp;

            [NbtSerializable(typeof(Player))]
            internal partial class TestContext : NbtSerializerContext
            {
            }

            internal sealed class Player
            {
                public bool Flag { get; set; }

                public string Name { get; set; } = "";

                public int? Count { get; set; }

                public bool? OptionalFlag { get; set; }

                public required int? RequiredCount { get; set; }
            }
            """);

        AssertNoErrors(result);
        Assert.Contains("NbtMetadataServices.ReadBoolean(ref reader)", result.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains("reader.GetString()", result.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains("reader.GetInt32()", result.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains("writer.WriteString(\"Name\", __m", result.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains(
            "writer.WriteSByte(\"OptionalFlag\", __m",
            result.GeneratedSource,
            StringComparison.Ordinal);
        Assert.Contains(
            ".Value ? (sbyte)1 : (sbyte)0",
            result.GeneratedSource,
            StringComparison.Ordinal);
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

    [Fact]
    public void SupportsAttributesOnMultiplePartialContextDeclarations()
    {
        var result = RunWithSources(
            """
            using NbtSharp;

            [NbtSerializable(typeof(DtoA))]
            internal partial class TestContext : NbtSerializerContext
            {
            }

            internal sealed class DtoA
            {
                public int A { get; set; }
            }
            """,
            """
            using NbtSharp;

            [NbtSerializable(typeof(DtoB))]
            internal partial class TestContext : NbtSerializerContext
            {
            }

            internal sealed class DtoB
            {
                public int B { get; set; }
            }
            """);

        AssertNoErrors(result);
        Assert.Equal(1, result.GeneratedSourceCount);
        Assert.Contains("DtoA", result.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains("DtoB", result.GeneratedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void UpdatesContextWhenNonPrimaryPartialChanges()
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var treeA = CSharpSyntaxTree.ParseText(
            """
            using NbtSharp;

            [NbtSerializable(typeof(DtoA))]
            internal partial class TestContext : NbtSerializerContext
            {
            }

            internal sealed class DtoA
            {
                public int A { get; set; }
            }
            """,
            parseOptions,
            path: "A.cs",
            cancellationToken: TestContext.Current.CancellationToken);
        var treeB = CSharpSyntaxTree.ParseText(
            """
            using NbtSharp;

            [NbtSerializable(typeof(DtoB))]
            internal partial class TestContext : NbtSerializerContext
            {
            }

            internal sealed class DtoB
            {
                public int B { get; set; }
            }
            """,
            parseOptions,
            path: "B.cs",
            cancellationToken: TestContext.Current.CancellationToken);
        var compilation = CSharpCompilation.Create(
            "PartialGeneratorTests",
            [treeA, treeB],
            References,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        var driver = CreateTrackingDriver(parseOptions);
        ContextSpecCache.ResetForTests();

        driver = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);
        var before = GetGeneratedSource(driver.GetRunResult());

        var changedTreeB = CSharpSyntaxTree.ParseText(
            """
            using NbtSharp;

            [NbtSerializable(typeof(DtoB))]
            internal partial class TestContext : NbtSerializerContext
            {
            }

            internal sealed class DtoB
            {
                public int B { get; set; }

                public int Armor { get; set; }
            }
            """,
            parseOptions,
            path: "B.cs",
            cancellationToken: TestContext.Current.CancellationToken);
        driver = driver.RunGenerators(
            compilation.ReplaceSyntaxTree(treeB, changedTreeB),
            TestContext.Current.CancellationToken);

        var after = GetGeneratedSource(driver.GetRunResult());
        Assert.NotEqual(before, after);
        Assert.Contains("Armor", after, StringComparison.Ordinal);
    }

    [Fact]
    public void RebuildsWhenPrimaryPartialLosesItsAttribute()
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var treeA = CSharpSyntaxTree.ParseText(
            """
            using NbtSharp;

            [NbtSerializable(typeof(DtoA))]
            internal partial class TestContext : NbtSerializerContext
            {
            }

            internal sealed class DtoA
            {
                public int A { get; set; }
            }
            """,
            parseOptions,
            path: "A.cs",
            cancellationToken: TestContext.Current.CancellationToken);
        var treeB = CSharpSyntaxTree.ParseText(
            """
            using NbtSharp;

            [NbtSerializable(typeof(DtoB))]
            internal partial class TestContext : NbtSerializerContext
            {
            }

            internal sealed class DtoB
            {
                public int B { get; set; }
            }
            """,
            parseOptions,
            path: "B.cs",
            cancellationToken: TestContext.Current.CancellationToken);
        var compilation = CSharpCompilation.Create(
            "PartialLifecycleTests",
            [treeA, treeB],
            References,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        var driver = CreateTrackingDriver(parseOptions);
        ContextSpecCache.ResetForTests();

        driver = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);
        var before = GetGeneratedSource(driver.GetRunResult());
        Assert.Contains("DtoA", before, StringComparison.Ordinal);
        Assert.Contains("DtoB", before, StringComparison.Ordinal);

        var treeAWithoutAttribute = CSharpSyntaxTree.ParseText(
            """
            using NbtSharp;

            internal partial class TestContext : NbtSerializerContext
            {
            }

            internal sealed class DtoA
            {
                public int A { get; set; }
            }
            """,
            parseOptions,
            path: "A.cs",
            cancellationToken: TestContext.Current.CancellationToken);
        var afterPrimaryRemoval = compilation.ReplaceSyntaxTree(treeA, treeAWithoutAttribute);
        driver = driver.RunGenerators(afterPrimaryRemoval, TestContext.Current.CancellationToken);
        var after = GetGeneratedSource(driver.GetRunResult());
        Assert.DoesNotContain("DtoA", after, StringComparison.Ordinal);
        Assert.Contains("DtoB", after, StringComparison.Ordinal);

        var treeBWithoutAttribute = CSharpSyntaxTree.ParseText(
            """
            using NbtSharp;

            internal partial class TestContext : NbtSerializerContext
            {
            }

            internal sealed class DtoB
            {
                public int B { get; set; }
            }
            """,
            parseOptions,
            path: "B.cs",
            cancellationToken: TestContext.Current.CancellationToken);
        driver = driver.RunGenerators(
            afterPrimaryRemoval.ReplaceSyntaxTree(treeB, treeBWithoutAttribute),
            TestContext.Current.CancellationToken);

        Assert.Equal("", GetGeneratedSource(driver.GetRunResult()));
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
            generatedSource,
            runResult.Results.Sum(static result => result.GeneratedSources.Length));
    }

    private static GeneratorRunResult RunWithSources(params string[] sources)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var syntaxTrees = sources
            .Select((source, index) => CSharpSyntaxTree.ParseText(
                source,
                parseOptions,
                path: $"Source{index}.cs",
                cancellationToken: TestContext.Current.CancellationToken))
            .ToArray();
        var compilation = CSharpCompilation.Create(
            "GeneratorTests",
            syntaxTrees,
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
            generatedSource,
            runResult.Results.Sum(static result => result.GeneratedSources.Length));
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
            string generatedSource,
            int generatedSourceCount)
        {
            GeneratorDiagnostics = generatorDiagnostics;
            CompilationDiagnostics = compilationDiagnostics;
            GeneratedSource = generatedSource;
            GeneratedSourceCount = generatedSourceCount;
        }

        public ImmutableArray<Diagnostic> GeneratorDiagnostics { get; }

        public ImmutableArray<Diagnostic> CompilationDiagnostics { get; }

        public string GeneratedSource { get; }

        public int GeneratedSourceCount { get; }
    }
}

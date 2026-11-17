using System.Collections.Immutable;
using BenchmarkDotNet.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace NbtSharp.SourceGeneration.Benchmarks;

[MemoryDiagnoser]
public class GeneratorIncrementalityBenchmarks
{
    private CSharpParseOptions _parseOptions = null!;
    private ImmutableArray<SyntaxTree> _trees;
    private ImmutableArray<MetadataReference> _references;
    private Compilation _coldCompilation = null!;
    private Compilation _editedCompilation = null!;
    private GeneratorDriver _warmDriver = null!;
    private int _unrelatedCounter;

    [Params(100, 300)]
    // ReSharper disable once UnusedAutoPropertyAccessor.Global
    public int ContextCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        _references = GetReferences();
        var trees = ImmutableArray.CreateBuilder<SyntaxTree>(ContextCount);
        for (var i = 0; i < ContextCount; i++)
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
                _parseOptions,
                path: $"Context{i}.cs"));
        }

        _trees = trees.MoveToImmutable();
    }

    [IterationSetup(Target = nameof(Cold))]
    public void ResetColdCache()
    {
        ContextSpecCache.ResetForTests();
        ContextEmitter.ContextSpecBuilder.ResetGraphBuildCountForTests();
        _coldCompilation = CreateCompilation();
    }

    [Benchmark(Baseline = true)]
    [InvocationCount(1)]
    public GeneratorDriver Cold()
    {
        var driver = Run(_coldCompilation);
        if (ContextEmitter.ContextSpecBuilder.GraphBuildCount != ContextCount)
        {
            throw new InvalidOperationException(
                $"Cold generation built {ContextEmitter.ContextSpecBuilder.GraphBuildCount} " +
                $"graphs instead of {ContextCount}.");
        }

        return driver;
    }

    [IterationSetup(Target = nameof(UnrelatedEditWarm))]
    public void PrepareWarmDriver()
    {
        ContextSpecCache.ResetForTests();
        ContextEmitter.ContextSpecBuilder.ResetGraphBuildCountForTests();
        var compilation = CreateCompilation();
        _warmDriver = Run(compilation);
        if (ContextEmitter.ContextSpecBuilder.GraphBuildCount != ContextCount)
        {
            throw new InvalidOperationException("Warm setup did not build every graph.");
        }

        var unrelatedIndex = _unrelatedCounter++;
        var unrelatedTree = CSharpSyntaxTree.ParseText(
            $"internal sealed class Unrelated{unrelatedIndex} " +
            "{ public int Value { get; set; } }",
            _parseOptions,
            path: $"Unrelated{unrelatedIndex}.cs");
        _editedCompilation = compilation.AddSyntaxTrees(unrelatedTree);
    }

    [Benchmark]
    [InvocationCount(1)]
    public GeneratorDriver UnrelatedEditWarm()
    {
        var before = ContextEmitter.ContextSpecBuilder.GraphBuildCount;
        var driver = _warmDriver.RunGenerators(_editedCompilation);
        if (ContextEmitter.ContextSpecBuilder.GraphBuildCount != before)
        {
            throw new InvalidOperationException(
                "An unrelated edit rebuilt a graph instead of reusing the content cache.");
        }

        return driver;
    }

    private Compilation CreateCompilation() =>
        CSharpCompilation.Create(
            "GeneratorBenchmarks",
            _trees,
            _references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

    private GeneratorDriver Run(Compilation compilation)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new NbtSourceGenerator().AsSourceGenerator()],
            parseOptions: _parseOptions,
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: false));
        return driver.RunGenerators(compilation);
    }

    private static ImmutableArray<MetadataReference> GetReferences()
    {
        var trustedPlatformAssemblies =
            (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "";
        return trustedPlatformAssemblies
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(NbtSerializer).Assembly.Location))
            .ToImmutableArray();
    }
}

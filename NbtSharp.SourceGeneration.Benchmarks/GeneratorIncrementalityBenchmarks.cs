using System.Collections.Immutable;
using BenchmarkDotNet.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace NbtSharp.SourceGeneration.Benchmarks;

[MemoryDiagnoser]
public class GeneratorIncrementalityBenchmarks
{
    private CSharpParseOptions _parseOptions = null!;
    private Compilation _coldCompilation = null!;
    private Compilation _unrelatedEditCompilation = null!;
    private GeneratorDriver _warmDriver = null!;

    [Params(100, 300)]
    // ReSharper disable once UnusedAutoPropertyAccessor.Global
    public int ContextCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var trees = new List<SyntaxTree>(ContextCount);
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

        _coldCompilation = CSharpCompilation.Create(
            "GeneratorBenchmarks",
            trees,
            GetReferences(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        var unrelatedTree = CSharpSyntaxTree.ParseText(
            "internal sealed class Unrelated { public int Value { get; set; } }",
            _parseOptions,
            path: "Unrelated.cs");
        _unrelatedEditCompilation = _coldCompilation.AddSyntaxTrees(unrelatedTree);
    }

    [IterationSetup(Target = nameof(Cold))]
    public void ResetColdCache() => ContextSpecCache.ResetForTests();

    [Benchmark(Baseline = true)]
    public GeneratorDriver Cold() => Run(_coldCompilation);

    [IterationSetup(Target = nameof(UnrelatedEditWarm))]
    public void PrepareWarmDriver()
    {
        ContextSpecCache.ResetForTests();
        _warmDriver = Run(_coldCompilation);
    }

    [Benchmark]
    public GeneratorDriver UnrelatedEditWarm() => _warmDriver.RunGenerators(_unrelatedEditCompilation);

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

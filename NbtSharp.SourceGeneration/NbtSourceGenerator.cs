using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NbtSharp.SourceGeneration;

[Generator(LanguageNames.CSharp)]
public sealed class NbtSourceGenerator : IIncrementalGenerator
{
    private const string SerializableAttributeName = "NbtSharp.NbtSerializableAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // ContextModel remains symbol-carrying because Prepare needs semantic symbols. The comparer
        // compares only its dependency fingerprint, and every cached prepared/generated stage is
        // symbol-free.
        var models = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                SerializableAttributeName,
                static (node, _) => node is TypeDeclarationSyntax,
                static (syntaxContext, _) => ContextModel.Create(syntaxContext))
            .Where(static model => model is not null)
            .Select(static (model, _) => model!)
            .Collect()
            .SelectMany(static (allModels, _) => Merge(allModels))
            .WithComparer(ContextModelComparer.Instance)
            .WithTrackingName("NbtContextModels");

        var prepared = models
            .Select(static (model, _) => ContextEmitter.Prepare(model))
            .WithComparer(PreparedContextComparer.Instance)
            .WithTrackingName("NbtPreparedContexts");

        var generated = prepared
            .Select(static (preparedContext, _) => GeneratedContext.Create(preparedContext))
            .WithComparer(GeneratedContextComparer.Instance)
            .WithTrackingName("NbtGeneratedContexts");

        context.RegisterSourceOutput(generated, static (productionContext, generatedContext) =>
        {
            foreach (var diagnostic in generatedContext.Diagnostics)
            {
                productionContext.ReportDiagnostic(diagnostic.ToDiagnostic());
            }

            if (generatedContext.Source.Length != 0)
            {
                productionContext.AddSource(generatedContext.HintName, generatedContext.Source);
            }
        });
    }

    private static IEnumerable<ContextModel> Merge(ImmutableArray<ContextModel> models)
    {
        var groups = new Dictionary<INamedTypeSymbol, List<RootType>>(SymbolEqualityComparer.Default);
        var fingerprints = new Dictionary<INamedTypeSymbol, List<string>>(SymbolEqualityComparer.Default);
        var order = new List<INamedTypeSymbol>();
        var sources = new Dictionary<INamedTypeSymbol, Location?>(SymbolEqualityComparer.Default);

        foreach (var model in models)
        {
            if (!groups.TryGetValue(model.ContextType, out var roots))
            {
                roots = [];
                groups.Add(model.ContextType, roots);
                fingerprints.Add(model.ContextType, []);
                order.Add(model.ContextType);
                sources.Add(model.ContextType, model.Location);
            }

            roots.AddRange(model.Roots);
            fingerprints[model.ContextType].Add(model.Fingerprint);
        }

        foreach (var contextType in order)
        {
            var roots = groups[contextType]
                .OrderBy(
                    static root => root.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    StringComparer.Ordinal)
                .ThenBy(static root => root.PropertyName ?? "", StringComparer.Ordinal)
                .ToImmutableArray();
            var fingerprint = ReachableFingerprint.Combine(contextType, fingerprints[contextType]);
            yield return new ContextModel(contextType, roots, sources[contextType], fingerprint);
        }
    }
}

internal sealed class GeneratedContext : IEquatable<GeneratedContext>
{
    public GeneratedContext(
        string hintName,
        string source,
        ImmutableArray<DiagnosticSpec> diagnostics)
    {
        HintName = hintName;
        Source = source;
        Diagnostics = diagnostics;
    }

    public string HintName { get; }

    public string Source { get; }

    public ImmutableArray<DiagnosticSpec> Diagnostics { get; }

    public static GeneratedContext Create(PreparedContext prepared)
    {
        var source = prepared.HasSource
            ? new ContextEmitter(prepared).EmitSource()
            : "";
        return new GeneratedContext(prepared.HintName, source, prepared.Diagnostics);
    }

    public bool Equals(GeneratedContext? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return HintName == other.HintName &&
               Source == other.Source &&
               DiagnosticsEqual(Diagnostics, other.Diagnostics);
    }

    public override bool Equals(object? obj) => obj is GeneratedContext other && Equals(other);

    public override int GetHashCode() =>
        (StringComparer.Ordinal.GetHashCode(HintName) * 397) ^
        StringComparer.Ordinal.GetHashCode(Source);

    private static bool DiagnosticsEqual(
        ImmutableArray<DiagnosticSpec> left,
        ImmutableArray<DiagnosticSpec> right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Length; i++)
        {
            if (!left[i].Equals(right[i]))
            {
                return false;
            }
        }

        return true;
    }
}

internal sealed class GeneratedContextComparer : IEqualityComparer<GeneratedContext>
{
    public static readonly GeneratedContextComparer Instance = new();

    public bool Equals(GeneratedContext? x, GeneratedContext? y) => x?.Equals(y) ?? y is null;

    public int GetHashCode(GeneratedContext obj) => obj.GetHashCode();
}

internal sealed class ContextModel : IEquatable<ContextModel>
{
    public ContextModel(
        INamedTypeSymbol contextType,
        ImmutableArray<RootType> roots,
        Location? location,
        string fingerprint)
    {
        ContextType = contextType;
        Roots = roots;
        Location = location;
        Fingerprint = fingerprint;
    }

    public INamedTypeSymbol ContextType { get; }

    public ImmutableArray<RootType> Roots { get; }

    public Location? Location { get; }

    public string Fingerprint { get; }

    public static ContextModel? Create(GeneratorAttributeSyntaxContext syntaxContext)
    {
        if (syntaxContext.TargetSymbol is not INamedTypeSymbol contextType)
        {
            return null;
        }

        var roots = ImmutableArray.CreateBuilder<RootType>();
        foreach (var attribute in syntaxContext.Attributes)
        {
            if (attribute.ConstructorArguments.Length != 1 ||
                attribute.ConstructorArguments[0].Value is not ITypeSymbol rootType)
            {
                continue;
            }

            var propertyName = attribute.NamedArguments
                .FirstOrDefault(static argument => argument.Key == "TypeInfoPropertyName")
                .Value.Value as string;
            roots.Add(new RootType(
                rootType,
                propertyName,
                attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation()));
        }

        var orderedRoots = roots
            .OrderBy(
                static root => root.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                StringComparer.Ordinal)
            .ThenBy(static root => root.PropertyName ?? "", StringComparer.Ordinal)
            .ToImmutableArray();
        var fingerprint = ReachableFingerprint.Compute(
            contextType,
            orderedRoots,
            syntaxContext.SemanticModel.Compilation);

        return new ContextModel(
            contextType,
            orderedRoots,
            contextType.Locations.FirstOrDefault(),
            fingerprint);
    }

    public bool Equals(ContextModel? other) =>
        other is not null &&
        StringComparer.Ordinal.Equals(Fingerprint, other.Fingerprint);

    public override bool Equals(object? obj) => obj is ContextModel other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Fingerprint);
}

internal sealed class ContextModelComparer : IEqualityComparer<ContextModel>
{
    public static readonly ContextModelComparer Instance = new();

    public bool Equals(ContextModel? x, ContextModel? y) => x?.Equals(y) ?? y is null;

    public int GetHashCode(ContextModel obj) => obj.GetHashCode();
}

internal sealed class RootType
{
    public RootType(ITypeSymbol type, string? propertyName, Location? location)
    {
        Type = type;
        PropertyName = propertyName;
        Location = location;
    }

    public ITypeSymbol Type { get; }

    public string? PropertyName { get; }

    public Location? Location { get; }
}

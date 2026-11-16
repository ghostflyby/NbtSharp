using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NbtSharp.SourceGeneration;

[Generator(LanguageNames.CSharp)]
public sealed class NbtSourceGenerator : IIncrementalGenerator
{
    private const string SerializableAttributeName = "NbtSharp.NbtSerializableAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var models = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                SerializableAttributeName,
                static (node, _) => node is TypeDeclarationSyntax,
                static (syntaxContext, _) => ContextModel.Create(syntaxContext))
            .Where(static model => model is not null)
            .Select(static (model, _) => model!)
            .Collect()
            .SelectMany(static (allModels, _) => Merge(allModels))
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
                productionContext.ReportDiagnostic(diagnostic);
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
        var order = new List<INamedTypeSymbol>();
        var sources = new Dictionary<INamedTypeSymbol, Location?>(SymbolEqualityComparer.Default);

        foreach (var model in models)
        {
            if (!groups.TryGetValue(model.ContextType, out var roots))
            {
                roots = [];
                groups.Add(model.ContextType, roots);
                order.Add(model.ContextType);
                sources.Add(model.ContextType, model.Location);
            }

            roots.AddRange(model.Roots);
        }

        foreach (var contextType in order)
        {
            yield return new ContextModel(contextType, groups[contextType].ToImmutableArray(), sources[contextType]);
        }
    }
}

internal sealed class GeneratedContext : IEquatable<GeneratedContext>
{
    public GeneratedContext(
        string hintName,
        string source,
        ImmutableArray<Diagnostic> diagnostics)
    {
        HintName = hintName;
        Source = source;
        Diagnostics = diagnostics;
    }

    public string HintName { get; }

    public string Source { get; }

    public ImmutableArray<Diagnostic> Diagnostics { get; }

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
        ImmutableArray<Diagnostic> left,
        ImmutableArray<Diagnostic> right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Length; i++)
        {
            var leftDiagnostic = left[i];
            var rightDiagnostic = right[i];
            if (leftDiagnostic.Id != rightDiagnostic.Id ||
                leftDiagnostic.Severity != rightDiagnostic.Severity ||
                leftDiagnostic.GetMessage(CultureInfo.InvariantCulture) !=
                rightDiagnostic.GetMessage(CultureInfo.InvariantCulture) ||
                (leftDiagnostic.Location.ToString()) !=
                (rightDiagnostic.Location.ToString()))
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

internal sealed class ContextModel
{
    public ContextModel(
        INamedTypeSymbol contextType,
        ImmutableArray<RootType> roots,
        Location? location)
    {
        ContextType = contextType;
        Roots = roots;
        Location = location;
    }

    public INamedTypeSymbol ContextType { get; }

    public ImmutableArray<RootType> Roots { get; }

    public Location? Location { get; }

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

        return new ContextModel(
            contextType,
            roots.ToImmutable(),
            contextType.Locations.FirstOrDefault());
    }
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

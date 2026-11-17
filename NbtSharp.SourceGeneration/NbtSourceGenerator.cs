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
        // Each attributed context is prepared inside the incremental transform and yields a
        // symbol-free PreparedContext. The content-keyed cache skips graph construction for
        // unchanged contexts; no symbol-carrying value is cached by the pipeline.
        var transformed = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                SerializableAttributeName,
                static (node, _) => node is TypeDeclarationSyntax,
                static (syntaxContext, _) => ContextInput.CreatePrepared(syntaxContext))
            .WithTrackingName("NbtContextInputs");

        var prepared = transformed
            .Where(static preparedContext => preparedContext is not null)
            .Select(static (preparedContext, _) => preparedContext!)
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

/// <summary>
/// Transient symbol-carrying context used only while preparing an uncached context. It is never a
/// cached incremental-step value.
/// </summary>
internal sealed class ContextModel
{
    public ContextModel(
        INamedTypeSymbol contextType,
        ImmutableArray<RootType> roots,
        Location? location,
        string cacheKey)
    {
        ContextType = contextType;
        Roots = roots;
        Location = location;
        CacheKey = cacheKey;
    }

    public INamedTypeSymbol ContextType { get; }

    public ImmutableArray<RootType> Roots { get; }

    public Location? Location { get; }

    public string CacheKey { get; }
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

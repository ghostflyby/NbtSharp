using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NbtSharp.SourceGeneration;

/// <summary>
/// Builds a symbol-free <see cref="PreparedContext"/> directly in the incremental transform. The
/// graph is constructed only on a content-cache miss; unchanged contexts return the cached spec
/// without retaining symbols in the pipeline.
/// </summary>
internal static class ContextInput
{
    private const string SerializableAttributeName = "NbtSharp.NbtSerializableAttribute";

    public static PreparedContext? CreatePrepared(GeneratorAttributeSyntaxContext syntaxContext)
    {
        if (syntaxContext.TargetSymbol is not INamedTypeSymbol contextType)
        {
            return null;
        }

        var attributes = contextType.GetAttributes()
            .Where(static attribute =>
                attribute.AttributeClass?.ToDisplayString() == SerializableAttributeName)
            .ToImmutableArray();
        if (attributes.Length == 0)
        {
            return null;
        }

        // Partial context declarations may carry attributes in more than one file. Exactly one
        // attributed declaration emits the combined context so the rest of the pipeline stays
        // per-context and symbol-free.
        var primaryDeclaration = attributes
            .Select(static attribute => attribute.ApplicationSyntaxReference?
                .GetSyntax()
                .FirstAncestorOrSelf<TypeDeclarationSyntax>())
            .Where(static declaration => declaration is not null)
            .OrderBy(static declaration => declaration!.SyntaxTree.FilePath, StringComparer.Ordinal)
            .ThenBy(static declaration => declaration!.Span.Start)
            .FirstOrDefault();
        if (primaryDeclaration is null ||
            !ReferenceEquals(primaryDeclaration.SyntaxTree, syntaxContext.TargetNode.SyntaxTree) ||
            primaryDeclaration.Span != syntaxContext.TargetNode.Span)
        {
            return null;
        }

        var roots = ImmutableArray.CreateBuilder<RootType>();
        foreach (var attribute in attributes)
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
        var cacheKey = ReachableFingerprint.Compute(
            contextType,
            orderedRoots,
            syntaxContext.SemanticModel.Compilation);
        var model = new ContextModel(
            contextType,
            orderedRoots,
            contextType.Locations.FirstOrDefault(),
            cacheKey);
        return ContextEmitter.PrepareResolved(
            model,
            ContextEmitter.GetHintName(contextType),
            cacheKey);
    }
}

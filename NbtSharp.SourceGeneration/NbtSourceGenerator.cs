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
        var models = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                SerializableAttributeName,
                static (node, _) => node is TypeDeclarationSyntax,
                static (syntaxContext, _) => ContextModel.Create(syntaxContext))
            .Where(static model => model is not null)
            .Select(static (model, _) => model!)
            .Collect();

        context.RegisterSourceOutput(models, static (productionContext, allModels) =>
        {
            foreach (var model in Merge(allModels))
            {
                new ContextEmitter(productionContext, model).Emit();
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

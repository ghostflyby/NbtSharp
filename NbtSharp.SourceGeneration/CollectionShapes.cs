using Microsoft.CodeAnalysis;

namespace NbtSharp.SourceGeneration;

/// <summary>
/// The supported sequence-collection shapes. The shape selects both the generated factory and the
/// runtime construction strategy.
/// </summary>
internal enum CollectionShape
{
    None,
    List,
    Set,
    Queue,
    Stack,
    LinkedList,
    Collection,
    ObservableCollection,
    ReadOnlyCollection,
    ImmutableArray,
    ImmutableList,
    ImmutableHashSet,
    ImmutableQueue,
    ImmutableStack,
    FrozenSet,
    Generated,
}

/// <summary>
/// The supported string-keyed map shapes.
/// </summary>
internal enum MapShape
{
    None,
    Dictionary,
    ReadOnlyDictionary,
    ImmutableDictionary,
    FrozenDictionary,
    Generated,
}

/// <summary>
/// Recognizes collection and map shapes from their metadata definitions. The type graph and the
/// reachability fingerprint both use this classifier so their supported-shape lists cannot drift.
/// </summary>
internal static class CollectionShapeClassifier
{
    private const string GenericNamespace = "System.Collections.Generic";
    private const string ObjectModelNamespace = "System.Collections.ObjectModel";
    private const string ImmutableNamespace = "System.Collections.Immutable";
    private const string FrozenNamespace = "System.Collections.Frozen";

    public static bool TryGetSequence(INamedTypeSymbol definition, out CollectionShape shape)
    {
        shape = CollectionShape.None;
        if (definition.Arity != 1)
        {
            return false;
        }

        var ns = definition.ContainingNamespace?.ToDisplayString();
        if (ns == GenericNamespace)
        {
            shape = definition.Name switch
            {
                "List" or "IEnumerable" or "ICollection" or "IList" or
                    "IReadOnlyList" or "IReadOnlyCollection" => CollectionShape.List,
                "HashSet" or "ISet" or "IReadOnlySet" => CollectionShape.Set,
                "Queue" => CollectionShape.Queue,
                "Stack" => CollectionShape.Stack,
                "LinkedList" => CollectionShape.LinkedList,
                _ => CollectionShape.None,
            };
        }
        else if (ns == ObjectModelNamespace)
        {
            shape = definition.Name switch
            {
                "Collection" => CollectionShape.Collection,
                "ObservableCollection" => CollectionShape.ObservableCollection,
                "ReadOnlyCollection" => CollectionShape.ReadOnlyCollection,
                _ => CollectionShape.None,
            };
        }
        else if (ns == ImmutableNamespace)
        {
            shape = definition.Name switch
            {
                "ImmutableArray" => CollectionShape.ImmutableArray,
                "ImmutableList" or "IImmutableList" => CollectionShape.ImmutableList,
                "ImmutableHashSet" or "IImmutableSet" => CollectionShape.ImmutableHashSet,
                "ImmutableQueue" or "IImmutableQueue" => CollectionShape.ImmutableQueue,
                "ImmutableStack" or "IImmutableStack" => CollectionShape.ImmutableStack,
                _ => CollectionShape.None,
            };
        }
        else if (ns == FrozenNamespace)
        {
            shape = definition.Name == "FrozenSet"
                ? CollectionShape.FrozenSet
                : CollectionShape.None;
        }

        return shape != CollectionShape.None;
    }

    public static bool TryGetMap(INamedTypeSymbol definition, out MapShape shape)
    {
        shape = MapShape.None;
        if (definition.Arity != 2)
        {
            return false;
        }

        var ns = definition.ContainingNamespace?.ToDisplayString();
        if (ns == GenericNamespace)
        {
            shape = definition.Name switch
            {
                "Dictionary" or "IDictionary" or "IReadOnlyDictionary" => MapShape.Dictionary,
                _ => MapShape.None,
            };
        }
        else if (ns == ObjectModelNamespace)
        {
            shape = definition.Name == "ReadOnlyDictionary"
                ? MapShape.ReadOnlyDictionary
                : MapShape.None;
        }
        else if (ns == ImmutableNamespace)
        {
            shape = definition.Name is "ImmutableDictionary" or "IImmutableDictionary"
                ? MapShape.ImmutableDictionary
                : MapShape.None;
        }
        else if (ns == FrozenNamespace)
        {
            shape = definition.Name == "FrozenDictionary"
                ? MapShape.FrozenDictionary
                : MapShape.None;
        }

        return shape != MapShape.None;
    }

    public static bool IsCollectionOrMap(INamedTypeSymbol named) =>
        named.IsGenericType &&
        (TryGetSequence(named.OriginalDefinition, out _) || TryGetMap(named.OriginalDefinition, out _));
}

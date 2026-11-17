using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace NbtSharp.SourceGeneration;

/// <summary>
/// Discovers how a concrete collection is constructed. Collection-category detection is based on
/// implemented interfaces; the construction strategy is selected from
/// <c>CollectionBuilderAttribute</c>, a companion static factory, or constructor/Add patterns.
/// </summary>
internal static class CollectionPatterns
{
    private const string CollectionBuilderAttributeName =
        "System.Runtime.CompilerServices.CollectionBuilderAttribute";
    private const string GenericNamespace = "System.Collections.Generic";
    private const string ObjectModelNamespace = "System.Collections.ObjectModel";
    private static readonly SymbolDisplayFormat FullyQualified = SymbolDisplayFormat.FullyQualifiedFormat;

    private enum BuilderArgumentKind
    {
        Enumerable,
        Span,
        Array,
    }

    public static bool TryGetMapTypes(
        INamedTypeSymbol type,
        out ITypeSymbol keyType,
        out ITypeSymbol valueType)
    {
        foreach (var candidate in SelfAndInterfaces(type))
        {
            var definition = candidate.OriginalDefinition;
            if (IsNamed(definition, GenericNamespace, "IDictionary", 2) ||
                IsNamed(definition, GenericNamespace, "IReadOnlyDictionary", 2))
            {
                keyType = candidate.TypeArguments[0];
                valueType = candidate.TypeArguments[1];
                return true;
            }
        }

        keyType = null!;
        valueType = null!;
        return false;
    }

    public static bool TryGetSequenceElementType(
        INamedTypeSymbol type,
        out ITypeSymbol elementType)
    {
        foreach (var candidate in SelfAndInterfaces(type))
        {
            if (IsNamed(candidate.OriginalDefinition, GenericNamespace, "IEnumerable", 1))
            {
                elementType = candidate.TypeArguments[0];
                return true;
            }
        }

        elementType = null!;
        return false;
    }

    public static GeneratedBuilderSpec? TryCreateSequence(
        INamedTypeSymbol type,
        ITypeSymbol elementType,
        IAssemblySymbol contextAssembly)
    {
        if (type.TypeKind is not TypeKind.Class and not TypeKind.Struct || type.IsAbstract)
        {
            return null;
        }

        if (TryGetStaticBuilderMethod(
                type,
                elementType,
                valueType: null,
                isMap: false,
                contextAssembly,
                out var builderType,
                out var method,
                out var argumentKind))
        {
            return CreateSequenceStaticSpec(
                type,
                elementType,
                builderType,
                method,
                argumentKind,
                contextAssembly);
        }

        return CreateSequenceConstructorSpec(type, elementType, contextAssembly);
    }

    public static GeneratedBuilderSpec? TryCreateMap(
        INamedTypeSymbol type,
        ITypeSymbol valueType,
        IAssemblySymbol contextAssembly)
    {
        if (type.TypeKind is not TypeKind.Class and not TypeKind.Struct || type.IsAbstract)
        {
            return null;
        }

        if (TryGetStaticBuilderMethod(
                type,
                elementType: null!,
                valueType,
                isMap: true,
                contextAssembly,
                out var builderType,
                out var method,
                out var argumentKind))
        {
            return CreateMapStaticSpec(type, valueType, builderType, method, argumentKind);
        }

        return CreateMapConstructorSpec(type, valueType, contextAssembly);
    }

    private static bool TryGetStaticBuilderMethod(
        INamedTypeSymbol type,
        ITypeSymbol? elementType,
        ITypeSymbol? valueType,
        bool isMap,
        IAssemblySymbol contextAssembly,
        out INamedTypeSymbol builderType,
        out IMethodSymbol method,
        out BuilderArgumentKind argumentKind)
    {
        builderType = null!;
        method = null!;
        argumentKind = default;

        var attribute = type.OriginalDefinition.GetAttributes()
            .FirstOrDefault(static attribute =>
                attribute.AttributeClass?.ToDisplayString() == CollectionBuilderAttributeName);
        if (attribute is not null &&
            attribute.ConstructorArguments.Length >= 2 &&
            attribute.ConstructorArguments[0].Value is INamedTypeSymbol attributeBuilder &&
            attribute.ConstructorArguments[1].Value is string attributeMethod &&
            IsAccessible(attributeBuilder, contextAssembly))
        {
            builderType = attributeBuilder;
            if (TryFindBuilderMethod(
                    type,
                    attributeBuilder,
                    [attributeMethod],
                    elementType,
                    valueType,
                    isMap,
                    contextAssembly,
                    out method,
                    out argumentKind))
            {
                return true;
            }
        }

        var companion = FindCompanionType(type);
        if (companion is null || !IsAccessible(companion, contextAssembly))
        {
            return false;
        }

        builderType = companion;
        return TryFindBuilderMethod(
            type,
            companion,
            ["CreateRange", "Create"],
            elementType,
            valueType,
            isMap,
            contextAssembly,
            out method,
            out argumentKind);
    }

    private static bool TryFindBuilderMethod(
        INamedTypeSymbol collectionType,
        INamedTypeSymbol builderType,
        string[] methodNames,
        ITypeSymbol? elementType,
        ITypeSymbol? valueType,
        bool isMap,
        IAssemblySymbol contextAssembly,
        out IMethodSymbol method,
        out BuilderArgumentKind argumentKind)
    {
        method = null!;
        argumentKind = default;
        foreach (var methodName in methodNames)
        {
            var bestKind = (BuilderArgumentKind?)null;
            IMethodSymbol? bestMethod = null;
            foreach (var candidate in builderType.GetMembers(methodName).OfType<IMethodSymbol>())
            {
                if (!candidate.IsStatic ||
                    !IsAccessible(candidate, contextAssembly) ||
                    candidate.Parameters.Length != 1 ||
                    !SymbolEqualityComparer.Default.Equals(
                        candidate.ReturnType.OriginalDefinition,
                        collectionType.OriginalDefinition) ||
                    !TryMatchBuilderParameter(
                        candidate.Parameters[0].Type,
                        elementType,
                        valueType,
                        isMap,
                        out var kind))
                {
                    continue;
                }

                if (bestKind is null || Priority(kind) < Priority(bestKind.Value))
                {
                    bestKind = kind;
                    bestMethod = candidate;
                }
            }

            if (bestMethod is not null)
            {
                method = bestMethod;
                argumentKind = bestKind!.Value;
                return true;
            }
        }

        return false;
    }

    private static int Priority(BuilderArgumentKind kind) => kind switch
    {
        BuilderArgumentKind.Enumerable => 0,
        BuilderArgumentKind.Span => 1,
        _ => 2,
    };

    private static bool TryMatchBuilderParameter(
        ITypeSymbol parameterType,
        ITypeSymbol? elementType,
        ITypeSymbol? valueType,
        bool isMap,
        out BuilderArgumentKind kind)
    {
        kind = default;
        if (parameterType is IArrayTypeSymbol array)
        {
            if (isMap)
            {
                if (!MatchesKeyValuePair(array.ElementType, valueType!))
                {
                    return false;
                }
            }
            else if (!Matches(array.ElementType, elementType!))
            {
                return false;
            }

            kind = BuilderArgumentKind.Array;
            return true;
        }

        if (parameterType is not INamedTypeSymbol named)
        {
            return false;
        }

        var definition = named.OriginalDefinition;
        if (isMap)
        {
            if (IsNamed(definition, GenericNamespace, "IEnumerable", 1) &&
                MatchesKeyValuePair(named.TypeArguments[0], valueType!))
            {
                kind = BuilderArgumentKind.Enumerable;
                return true;
            }

            if ((IsNamed(definition, GenericNamespace, "IDictionary", 2) ||
                 IsNamed(definition, GenericNamespace, "IReadOnlyDictionary", 2) ||
                 IsNamed(definition, GenericNamespace, "Dictionary", 2) ||
                 IsNamed(definition, ObjectModelNamespace, "ReadOnlyDictionary", 2)) &&
                Matches(named.TypeArguments[0], null, SpecialType.System_String) &&
                Matches(named.TypeArguments[1], valueType!))
            {
                kind = BuilderArgumentKind.Enumerable;
                return true;
            }

            if ((IsNamed(definition, "System", "ReadOnlySpan", 1) ||
                 IsNamed(definition, "System", "Span", 1)) &&
                MatchesKeyValuePair(named.TypeArguments[0], valueType!))
            {
                kind = BuilderArgumentKind.Span;
                return true;
            }

            return false;
        }

        if ((IsNamed(definition, GenericNamespace, "IEnumerable", 1) ||
             IsNamed(definition, GenericNamespace, "IReadOnlyList", 1) ||
             IsNamed(definition, GenericNamespace, "IReadOnlyCollection", 1) ||
             IsNamed(definition, GenericNamespace, "ICollection", 1) ||
             IsNamed(definition, GenericNamespace, "IList", 1) ||
             IsNamed(definition, GenericNamespace, "List", 1)) &&
            Matches(named.TypeArguments[0], elementType!))
        {
            kind = BuilderArgumentKind.Enumerable;
            return true;
        }

        if ((IsNamed(definition, "System", "ReadOnlySpan", 1) ||
             IsNamed(definition, "System", "Span", 1)) &&
            Matches(named.TypeArguments[0], elementType!))
        {
            kind = BuilderArgumentKind.Span;
            return true;
        }

        return false;
    }

    private static bool MatchesKeyValuePair(ITypeSymbol candidate, ITypeSymbol valueType)
    {
        if (candidate is ITypeParameterSymbol)
        {
            return true;
        }

        if (candidate is not INamedTypeSymbol named ||
            !IsNamed(named.OriginalDefinition, GenericNamespace, "KeyValuePair", 2))
        {
            return false;
        }

        return Matches(named.TypeArguments[0], null, SpecialType.System_String) &&
               Matches(named.TypeArguments[1], valueType);
    }

    private static bool Matches(
        ITypeSymbol candidate,
        ITypeSymbol? expected,
        SpecialType specialType = SpecialType.None)
    {
        if (candidate is ITypeParameterSymbol)
        {
            return true;
        }

        if (specialType != SpecialType.None)
        {
            return candidate.SpecialType == specialType;
        }

        return expected is not null &&
               SymbolEqualityComparer.Default.Equals(candidate, expected);
    }

    private static GeneratedBuilderSpec CreateSequenceStaticSpec(
        INamedTypeSymbol type,
        ITypeSymbol elementType,
        INamedTypeSymbol builderType,
        IMethodSymbol method,
        BuilderArgumentKind argumentKind,
        IAssemblySymbol contextAssembly)
    {
        var listType = $"global::System.Collections.Generic.List<{Fq(elementType)}>";
        var complete = ImmutableArray.CreateBuilder<string>();
        if (HasInstanceMethod(type, "Push", elementType, contextAssembly) &&
            !HasInstanceMethod(type, "Add", elementType, contextAssembly))
        {
            complete.Add("__state.Reverse();");
        }

        complete.Add(
            $"return {Fq(builderType)}.{method.Name}({BuilderArgument(argumentKind)});");
        return new GeneratedBuilderSpec(
            isMap: false,
            listType,
            ImmutableArray.Create($"__state = new {listType}(count);"),
            ImmutableArray.Create("__state.Add(item);"),
            containsKeyExpression: null,
            complete.ToImmutable());
    }

    private static GeneratedBuilderSpec CreateMapStaticSpec(
        INamedTypeSymbol type,
        ITypeSymbol valueType,
        INamedTypeSymbol builderType,
        IMethodSymbol method,
        BuilderArgumentKind argumentKind)
    {
        _ = type;
        var valueName = Fq(valueType);
        if (argumentKind == BuilderArgumentKind.Span)
        {
            var pairType = $"global::System.Collections.Generic.KeyValuePair<string, {valueName}>";
            var listType = $"global::System.Collections.Generic.List<{pairType}>";
            return new GeneratedBuilderSpec(
                isMap: true,
                listType,
                ImmutableArray.Create($"__state = new {listType}();"),
                ImmutableArray.Create(
                    $"__state.Add(new {pairType}(key, value));"),
                "__state.Exists(pair => pair.Key == key)",
                ImmutableArray.Create(
                    $"return {Fq(builderType)}.{method.Name}(" +
                    "global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(__state));"));
        }

        var dictionaryType =
            $"global::System.Collections.Generic.Dictionary<string, {valueName}>";
        var argument = argumentKind == BuilderArgumentKind.Array
            ? "global::System.Linq.Enumerable.ToArray(__state)"
            : "__state";
        return new GeneratedBuilderSpec(
            isMap: true,
            dictionaryType,
            ImmutableArray.Create(
                $"__state = new {dictionaryType}(global::System.StringComparer.Ordinal);"),
            ImmutableArray.Create("__state[key] = value;"),
            "__state.ContainsKey(key)",
            ImmutableArray.Create(
                $"return {Fq(builderType)}.{method.Name}({argument});"));
    }

    private static GeneratedBuilderSpec? CreateSequenceConstructorSpec(
        INamedTypeSymbol type,
        ITypeSymbol elementType,
        IAssemblySymbol contextAssembly)
    {
        var typeName = Fq(type);
        var elementName = Fq(elementType);
        var addMethod = FindInsertMethod(type, elementType, contextAssembly);
        if (addMethod is not null && !addMethod.Name.Equals("Push", StringComparison.Ordinal))
        {
            var capacityConstructor = FindConstructor(type, "int", contextAssembly);
            var parameterlessConstructor = FindParameterlessConstructor(type, contextAssembly);
            var create = capacityConstructor is not null && parameterlessConstructor is not null
                ? $"__state = count == 0 ? new {typeName}() : new {typeName}(count);"
                : capacityConstructor is not null
                    ? $"__state = new {typeName}(count);"
                    : $"__state = new {typeName}();";
            return new GeneratedBuilderSpec(
                isMap: false,
                typeName,
                ImmutableArray.Create(create),
                ImmutableArray.Create($"__state.{addMethod.Name}(item);"),
                containsKeyExpression: null,
                ImmutableArray.Create("return __state;"));
        }

        if (addMethod?.Name == "Push")
        {
            var pushConstructor = FindConstructor(type, "int", contextAssembly) ??
                FindParameterlessConstructor(type, contextAssembly);
            if (pushConstructor is null)
            {
                return null;
            }

            var construction = pushConstructor.Parameters.Length == 0
                ? $"new {typeName}()"
                : $"new {typeName}(__state.Count)";
            var listType = $"global::System.Collections.Generic.List<{elementName}>";
            return new GeneratedBuilderSpec(
                isMap: false,
                listType,
                ImmutableArray.Create($"__state = new {listType}(count);"),
                ImmutableArray.Create("__state.Add(item);"),
                containsKeyExpression: null,
                ImmutableArray.Create(
                    $"var __result = {construction};",
                    "for (var __index = __state.Count - 1; __index >= 0; __index--)",
                    "{",
                    "    __result.Push(__state[__index]);",
                    "}",
                    "return __result;"));
        }

        if (FindConstructor(type, "IEnumerable", contextAssembly) is not null ||
            FindConstructor(type, typeof(IReadOnlyList<>).Name, contextAssembly) is not null ||
            FindConstructor(type, typeof(IList<>).Name, contextAssembly) is not null ||
            FindConstructor(type, typeof(ICollection<>).Name, contextAssembly) is not null)
        {
            var listType = $"global::System.Collections.Generic.List<{elementName}>";
            return new GeneratedBuilderSpec(
                isMap: false,
                listType,
                ImmutableArray.Create($"__state = new {listType}(count);"),
                ImmutableArray.Create("__state.Add(item);"),
                containsKeyExpression: null,
                ImmutableArray.Create($"return new {typeName}(__state);"));
        }

        if (FindArrayConstructor(type, elementType, contextAssembly) is not null)
        {
            var listType = $"global::System.Collections.Generic.List<{elementName}>";
            return new GeneratedBuilderSpec(
                isMap: false,
                listType,
                ImmutableArray.Create($"__state = new {listType}(count);"),
                ImmutableArray.Create("__state.Add(item);"),
                containsKeyExpression: null,
                ImmutableArray.Create($"return new {typeName}(__state.ToArray());"));
        }

        return null;
    }

    private static GeneratedBuilderSpec? CreateMapConstructorSpec(
        INamedTypeSymbol type,
        ITypeSymbol valueType,
        IAssemblySymbol contextAssembly)
    {
        var typeName = Fq(type);
        var valueName = Fq(valueType);
        var dictionaryInterface = MapInterface(type, valueType);
        if (dictionaryInterface is null)
        {
            return null;
        }

        if (FindParameterlessConstructor(type, contextAssembly) is not null ||
            FindConstructor(type, "int", contextAssembly) is not null ||
            FindComparerConstructor(type, contextAssembly) is not null)
        {
            var parameterless = FindParameterlessConstructor(type, contextAssembly);
            var comparerConstructor = FindComparerConstructor(type, contextAssembly);
            var construction = comparerConstructor is not null
                ? $"new {typeName}(global::System.StringComparer.Ordinal)"
                : parameterless is not null
                    ? $"new {typeName}()"
                    : $"new {typeName}(0)";
            var addLine = dictionaryInterface.StartsWith(
                "global::System.Collections.Generic.IDictionary",
                StringComparison.Ordinal)
                ? $"(({dictionaryInterface})__state)[key] = value;"
                : HasInstanceMethod(
                    type,
                    "Add",
                    SpecialType.System_String,
                    valueType,
                    contextAssembly)
                    ? "__state.Add(key, value);"
                    : $"(({dictionaryInterface})__state)[key] = value;";
            var containsKey = HasInstanceMethod(
                type,
                "ContainsKey",
                SpecialType.System_String,
                contextAssembly)
                ? "__state.ContainsKey(key)"
                : $"(({dictionaryInterface})__state).ContainsKey(key)";
            return new GeneratedBuilderSpec(
                isMap: true,
                typeName,
                ImmutableArray.Create($"__state = {construction};"),
                ImmutableArray.Create(addLine),
                containsKey,
                ImmutableArray.Create("return __state;"));
        }

        if (FindConstructor(type, "IDictionary", contextAssembly) is not null ||
            FindConstructor(type, "IReadOnlyDictionary", contextAssembly) is not null ||
            FindConstructor(type, "IEnumerable", contextAssembly) is not null)
        {
            var dictionaryType =
                $"global::System.Collections.Generic.Dictionary<string, {valueName}>";
            return new GeneratedBuilderSpec(
                isMap: true,
                dictionaryType,
                ImmutableArray.Create(
                    $"__state = new {dictionaryType}(global::System.StringComparer.Ordinal);"),
                ImmutableArray.Create("__state[key] = value;"),
                "__state.ContainsKey(key)",
                ImmutableArray.Create($"return new {typeName}(__state);"));
        }

        return null;
    }

    private static IMethodSymbol? FindInsertMethod(
        INamedTypeSymbol type,
        ITypeSymbol elementType,
        IAssemblySymbol contextAssembly)
    {
        foreach (var name in new[] { "Add", "Enqueue", "AddLast", "Push" })
        {
            var method = FindInstanceMethod(type, name, elementType, contextAssembly);
            if (method is not null)
            {
                return method;
            }
        }

        return null;
    }

    private static IMethodSymbol? FindInstanceMethod(
        INamedTypeSymbol type,
        string name,
        ITypeSymbol argumentType,
        IAssemblySymbol contextAssembly) =>
        type.GetMembers(name)
            .OfType<IMethodSymbol>()
            .FirstOrDefault(method =>
                !method.IsStatic &&
                IsAccessible(method, contextAssembly) &&
                method.Parameters.Length == 1 &&
                SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, argumentType));

    private static bool HasInstanceMethod(
        INamedTypeSymbol type,
        string name,
        ITypeSymbol argumentType,
        IAssemblySymbol contextAssembly) =>
        FindInstanceMethod(type, name, argumentType, contextAssembly) is not null;

    private static bool HasInstanceMethod(
        INamedTypeSymbol type,
        string name,
        SpecialType argumentType,
        IAssemblySymbol contextAssembly) =>
        type.GetMembers(name)
            .OfType<IMethodSymbol>()
            .Any(method =>
                !method.IsStatic &&
                IsAccessible(method, contextAssembly) &&
                method.Parameters.Length == 1 &&
                method.Parameters[0].Type.SpecialType == argumentType);

    private static bool HasInstanceMethod(
        INamedTypeSymbol type,
        string name,
        SpecialType firstArgumentType,
        ITypeSymbol secondArgumentType,
        IAssemblySymbol contextAssembly) =>
        type.GetMembers(name)
            .OfType<IMethodSymbol>()
            .Any(method =>
                !method.IsStatic &&
                IsAccessible(method, contextAssembly) &&
                method.Parameters.Length == 2 &&
                method.Parameters[0].Type.SpecialType == firstArgumentType &&
                SymbolEqualityComparer.Default.Equals(
                    method.Parameters[1].Type,
                    secondArgumentType));

    private static IMethodSymbol? FindConstructor(
        INamedTypeSymbol type,
        string parameterTypeName,
        IAssemblySymbol contextAssembly) =>
        type.InstanceConstructors.FirstOrDefault(constructor =>
            IsAccessible(constructor, contextAssembly) &&
            constructor.Parameters.Length == 1 &&
            (constructor.Parameters[0].Type.Name == parameterTypeName ||
             constructor.Parameters[0].Type.ToDisplayString(FullyQualified) == parameterTypeName));

    private static IMethodSymbol? FindArrayConstructor(
        INamedTypeSymbol type,
        ITypeSymbol elementType,
        IAssemblySymbol contextAssembly) =>
        type.InstanceConstructors.FirstOrDefault(constructor =>
            IsAccessible(constructor, contextAssembly) &&
            constructor.Parameters.Length == 1 &&
            constructor.Parameters[0].Type is IArrayTypeSymbol array &&
            SymbolEqualityComparer.Default.Equals(array.ElementType, elementType));

    private static IMethodSymbol? FindParameterlessConstructor(
        INamedTypeSymbol type,
        IAssemblySymbol contextAssembly) =>
        type.InstanceConstructors.FirstOrDefault(constructor =>
            IsAccessible(constructor, contextAssembly) &&
            constructor.Parameters.Length == 0);

    private static IMethodSymbol? FindComparerConstructor(
        INamedTypeSymbol type,
        IAssemblySymbol contextAssembly) =>
        type.InstanceConstructors.FirstOrDefault(constructor =>
            IsAccessible(constructor, contextAssembly) &&
            constructor.Parameters.Length == 1 &&
            IsStringComparerInterface(constructor.Parameters[0].Type));

    private static bool IsStringComparerInterface(ITypeSymbol type) =>
        type is INamedTypeSymbol named &&
        named.TypeArguments.Length == 1 &&
        named.TypeArguments[0].SpecialType == SpecialType.System_String &&
        named.OriginalDefinition.ContainingNamespace?.ToDisplayString() == GenericNamespace &&
        named.OriginalDefinition.Name is "IComparer" or "IEqualityComparer";

    private static string? MapInterface(INamedTypeSymbol type, ITypeSymbol valueType)
    {
        var valueName = Fq(valueType);
        foreach (var candidate in SelfAndInterfaces(type))
        {
            var definition = candidate.OriginalDefinition;
            if (IsNamed(definition, GenericNamespace, "IDictionary", 2) &&
                candidate.TypeArguments[0].SpecialType == SpecialType.System_String &&
                SymbolEqualityComparer.Default.Equals(candidate.TypeArguments[1], valueType))
            {
                return $"global::System.Collections.Generic.IDictionary<string, {valueName}>";
            }
        }

        foreach (var candidate in SelfAndInterfaces(type))
        {
            var definition = candidate.OriginalDefinition;
            if (IsNamed(definition, GenericNamespace, "IReadOnlyDictionary", 2) &&
                candidate.TypeArguments[0].SpecialType == SpecialType.System_String &&
                SymbolEqualityComparer.Default.Equals(candidate.TypeArguments[1], valueType))
            {
                return
                    $"global::System.Collections.Generic.IReadOnlyDictionary<string, {valueName}>";
            }
        }

        return null;
    }

    internal static INamedTypeSymbol? FindCompanionType(INamedTypeSymbol type)
    {
        foreach (var candidate in type.ContainingNamespace.GetTypeMembers(type.Name))
        {
            if (candidate.Arity == 0 && candidate.IsStatic)
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<INamedTypeSymbol> SelfAndInterfaces(INamedTypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Interface)
        {
            yield return type;
        }

        foreach (var interfaceType in type.AllInterfaces)
        {
            yield return interfaceType;
        }
    }

    private static bool IsAccessible(ISymbol symbol, IAssemblySymbol contextAssembly) =>
        symbol.DeclaredAccessibility switch
        {
            Accessibility.Public => true,
            Accessibility.Internal => SymbolEqualityComparer.Default.Equals(
                symbol.ContainingAssembly,
                contextAssembly),
            _ => false,
        };

    private static bool IsNamed(INamedTypeSymbol type, string ns, string name, int arity) =>
        type.Name == name &&
        type.Arity == arity &&
        type.ContainingNamespace?.ToDisplayString() == ns;

    private static string Fq(ITypeSymbol type) => type.ToDisplayString(FullyQualified);

    private static string BuilderArgument(BuilderArgumentKind kind) => kind switch
    {
        BuilderArgumentKind.Enumerable => "__state",
        BuilderArgumentKind.Span =>
            "global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(__state)",
        _ => "__state.ToArray()",
    };
}

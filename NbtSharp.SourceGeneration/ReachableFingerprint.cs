using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;

namespace NbtSharp.SourceGeneration;

/// <summary>
/// Computes a conservative dependency fingerprint for a context without constructing the type
/// graph. The walk visits every type that can influence generated source and hashes declaration
/// text for source types and assembly identities for metadata types.
/// </summary>
internal static class ReachableFingerprint
{
    private const string FormatVersion = "reachable-fingerprint-v1";
    private const string UseConverterAttribute = "NbtSharp.NbtUseConverterAttribute";

    private static readonly SymbolDisplayFormat FullyQualified = SymbolDisplayFormat.FullyQualifiedFormat;

    public static string Compute(
        INamedTypeSymbol contextType,
        ImmutableArray<RootType> roots,
        Compilation compilation)
    {
        var hash = StableHash.Create();
        hash.Add(FormatVersion);
        hash.Add((int)compilation.Options.NullableContextOptions);
        hash.Add(contextType.ToDisplayString(FullyQualified));
        AddAssemblyIdentity(ref hash, contextType);
        AddDeclarationHashes(ref hash, contextType);

        var visited = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        var pending = new Stack<ITypeSymbol>();
        pending.Push(contextType);

        hash.Add(roots.Length);
        foreach (var root in roots)
        {
            hash.Add(root.Type.ToDisplayString(FullyQualified));
            hash.Add(root.PropertyName);
            pending.Push(root.Type);
        }

        while (pending.Count != 0)
        {
            var type = pending.Pop();
            if (!visited.Add(type))
            {
                continue;
            }

            AddType(ref hash, type, pending);
        }

        return hash.ToHex();
    }

    public static string Combine(INamedTypeSymbol contextType, IEnumerable<string> fingerprints)
    {
        var hash = StableHash.Create();
        hash.Add("context-merge-v1");
        hash.Add(contextType.ToDisplayString(FullyQualified));
        foreach (var fingerprint in fingerprints.OrderBy(static value => value, StringComparer.Ordinal))
        {
            hash.Add(fingerprint);
        }

        return hash.ToHex();
    }

    private static void AddType(
        ref StableHash hash,
        ITypeSymbol type,
        Stack<ITypeSymbol> pending)
    {
        hash.Add((int)type.TypeKind);
        hash.Add((int)type.SpecialType);
        hash.Add((int)type.DeclaredAccessibility);
        hash.Add(type.IsValueType);
        hash.Add(type.ToDisplayString(FullyQualified));
        AddAssemblyIdentity(ref hash, type);
        AddDeclarationHashes(ref hash, type);
        AddAttributes(ref hash, type);

        switch (type)
        {
            case IArrayTypeSymbol array:
                pending.Push(array.ElementType);
                return;

            case IPointerTypeSymbol pointer:
                pending.Push(pointer.PointedAtType);
                return;

            case IFunctionPointerTypeSymbol:
                return;

            case ITypeParameterSymbol typeParameter:
                AddTypeParameter(ref hash, typeParameter, pending);
                return;

            case INamedTypeSymbol named:
                AddNamedType(ref hash, named, pending);
                return;
        }
    }

    private static void AddNamedType(
        ref StableHash hash,
        INamedTypeSymbol named,
        Stack<ITypeSymbol> pending)
    {
        if (named.TypeKind == TypeKind.Enum)
        {
            if (named.EnumUnderlyingType is { } underlying)
            {
                pending.Push(underlying);
            }

            return;
        }

        if (IsStoppedType(named))
        {
            return;
        }

        if (named.IsGenericType)
        {
            foreach (var argument in named.TypeArguments)
            {
                pending.Push(argument);
            }

            foreach (var parameter in named.TypeParameters)
            {
                AddTypeParameter(ref hash, parameter, pending);
            }
        }

        if (IsCollectionShape(named))
        {
            return;
        }

        if (named.BaseType is { } baseType && baseType.SpecialType != SpecialType.System_Object)
        {
            pending.Push(baseType);
        }

        hash.Add(named.IsRecord);
        hash.Add(named.IsAbstract);
        hash.Add(named.IsStatic);
        hash.Add(named.IsSealed);
        hash.Add(named.BaseType?.ToDisplayString(FullyQualified));
        hash.Add(named.AllInterfaces.Length);
        foreach (var interfaceType in named.AllInterfaces
                     .OrderBy(static value => value.ToDisplayString(FullyQualified), StringComparer.Ordinal))
        {
            hash.Add(interfaceType.ToDisplayString(FullyQualified));
        }

        foreach (var member in named.GetMembers())
        {
            hash.Add(member.Name);
            hash.Add((int)member.DeclaredAccessibility);
            hash.Add(member.IsStatic);
            AddAttributes(ref hash, member);

            switch (member)
            {
                case IPropertySymbol property when !property.IsStatic:
                    hash.Add(property.IsRequired);
                    hash.Add(property.IsIndexer);
                    hash.Add(property.IsOverride);
                    hash.Add((int)(property.GetMethod?.DeclaredAccessibility ?? Accessibility.NotApplicable));
                    hash.Add((int)(property.SetMethod?.DeclaredAccessibility ?? Accessibility.NotApplicable));
                    hash.Add(property.SetMethod?.IsInitOnly ?? false);
                    for (var overridden = property.OverriddenProperty;
                         overridden is not null;
                         overridden = overridden.OverriddenProperty)
                    {
                        hash.Add(overridden.OriginalDefinition.ToDisplayString(FullyQualified));
                    }

                    pending.Push(property.Type);
                    AddExplicitConverterType(property, pending);
                    break;
                case IFieldSymbol field when !field.IsStatic:
                    hash.Add(field.IsRequired);
                    hash.Add(field.IsReadOnly);
                    hash.Add(field.IsConst);
                    pending.Push(field.Type);
                    AddExplicitConverterType(field, pending);
                    break;
                case IMethodSymbol { MethodKind: MethodKind.Constructor } constructor:
                    hash.Add(constructor.Parameters.Length);
                    foreach (var parameter in constructor.Parameters)
                    {
                        hash.Add(parameter.Name);
                        hash.Add((int)parameter.RefKind);
                        pending.Push(parameter.Type);
                    }

                    break;
            }
        }
    }

    private static void AddAttributes(ref StableHash hash, ISymbol symbol)
    {
        var attributes = symbol.GetAttributes();
        hash.Add(attributes.Length);
        foreach (var attribute in attributes)
        {
            hash.Add(attribute.AttributeClass?.ToDisplayString(FullyQualified));
            hash.Add(attribute.ConstructorArguments.Length);
            foreach (var argument in attribute.ConstructorArguments)
            {
                AddTypedConstant(ref hash, argument);
            }

            hash.Add(attribute.NamedArguments.Length);
            foreach (var pair in attribute.NamedArguments)
            {
                hash.Add(pair.Key);
                AddTypedConstant(ref hash, pair.Value);
            }
        }
    }

    private static void AddTypedConstant(ref StableHash hash, TypedConstant constant)
    {
        hash.Add((int)constant.Kind);
        if (constant.IsNull)
        {
            hash.Add(-1);
            return;
        }

        if (constant.Kind == TypedConstantKind.Array)
        {
            hash.Add(constant.Values.Length);
            foreach (var item in constant.Values)
            {
                AddTypedConstant(ref hash, item);
            }

            return;
        }

        if (constant.Value is ITypeSymbol type)
        {
            hash.Add(type.ToDisplayString(FullyQualified));
            return;
        }

        hash.Add(Convert.ToString(constant.Value, CultureInfo.InvariantCulture));
    }

    private static void AddExplicitConverterType(ISymbol symbol, Stack<ITypeSymbol> pending)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != UseConverterAttribute ||
                attribute.ConstructorArguments.Length != 1 ||
                attribute.ConstructorArguments[0].Value is not ITypeSymbol converterType)
            {
                continue;
            }

            pending.Push(converterType);
            return;
        }
    }

    private static void AddTypeParameter(
        ref StableHash hash,
        ITypeParameterSymbol typeParameter,
        Stack<ITypeSymbol> pending)
    {
        hash.Add("type-parameter");
        hash.Add(typeParameter.Name);
        hash.Add(typeParameter.HasUnmanagedTypeConstraint);
        hash.Add(typeParameter.HasValueTypeConstraint);
        hash.Add(typeParameter.HasReferenceTypeConstraint);
        hash.Add(typeParameter.HasNotNullConstraint);
        hash.Add(typeParameter.HasConstructorConstraint);
        foreach (var constraint in typeParameter.ConstraintTypes)
        {
            pending.Push(constraint);
        }
    }

    private static void AddAssemblyIdentity(ref StableHash hash, ITypeSymbol type)
    {
        hash.Add(type.ContainingAssembly?.Identity.ToString());
    }

    private static void AddDeclarationHashes(ref StableHash hash, ITypeSymbol type)
    {
        var references = type.DeclaringSyntaxReferences;
        if (references.Length == 0)
        {
            hash.Add(-1);
            return;
        }

        var orderedReferences = references
            .OrderBy(static reference => reference.SyntaxTree.FilePath, StringComparer.Ordinal)
            .ThenBy(static reference => reference.Span.Start)
            .ToArray();
        hash.Add(orderedReferences.Length);
        foreach (var reference in orderedReferences)
        {
            var text = reference.SyntaxTree.GetText();
            var span = reference.Span;
            hash.Add(reference.SyntaxTree.FilePath);
            for (var index = span.Start; index < span.End; index++)
            {
                hash.Add(text[index]);
            }
        }
    }

    private static bool IsCollectionShape(INamedTypeSymbol named)
    {
        if (!named.IsGenericType)
        {
            return false;
        }

        var definition = named.OriginalDefinition;
        return IsNamed(definition, "System", "Nullable", 1) ||
               IsNamed(definition, "System.Collections.Generic", "List", 1) ||
               IsNamed(definition, "System.Collections.Generic", "IReadOnlyList", 1) ||
               IsNamed(definition, "System.Collections.Generic", "IReadOnlyCollection", 1) ||
               IsNamed(definition, "System.Collections.Generic", "Dictionary", 2) ||
               IsNamed(definition, "System.Collections.Generic", "IDictionary", 2) ||
               IsNamed(definition, "System.Collections.Generic", "IReadOnlyDictionary", 2);
    }

    private static bool IsStoppedType(INamedTypeSymbol named)
    {
        if (named.SpecialType != SpecialType.None)
        {
            return true;
        }

        if (named.TypeKind is TypeKind.Interface or TypeKind.Delegate or TypeKind.Error)
        {
            return true;
        }

        var name = named.ToDisplayString(FullyQualified);
        return name is
            "global::NbtSharp.NbtValue" or
            "global::NbtSharp.NbtList" or
            "global::NbtSharp.NbtCompound" or
            "global::NbtSharp.NbtRoot" or
            "global::NbtSharp.NbtEndList" or
            "global::System.Guid" or
            "global::System.DateTime" or
            "global::System.DateTimeOffset" or
            "global::System.TimeSpan" or
            "global::System.DateOnly" or
            "global::System.TimeOnly" or
            "global::System.Version" or
            "global::System.Uri" or
            "global::System.Type" or
            "global::System.Int128" or
            "global::System.UInt128" or
            "global::System.Half" or
            "global::System.Numerics.BigInteger";
    }

    private static bool IsNamed(INamedTypeSymbol type, string ns, string name, int arity) =>
        type.Name == name &&
        type.Arity == arity &&
        type.ContainingNamespace?.ToDisplayString() == ns;
}

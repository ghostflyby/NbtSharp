using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;

namespace NbtSharp.SourceGeneration;

/// <summary>
/// Computes a conservative content fingerprint for a context without constructing the type graph.
/// The walk visits every type that can influence generated source and hashes declaration text for
/// source types and assembly identities for metadata types.
/// </summary>
internal static class ReachableFingerprint
{
    private const string FormatVersion = "reachable-fingerprint-v10";
    private const string UseConverterAttribute = "NbtSharp.NbtUseConverterAttribute";
    private const string DerivedAttribute = "NbtSharp.NbtDerivedAttribute";
    private const string StringEnumAttribute = "NbtSharp.NbtStringEnumAttribute";
    private const string CollectionBuilderAttributeName =
        "System.Runtime.CompilerServices.CollectionBuilderAttribute";

    private static readonly SymbolDisplayFormat FullyQualified = SymbolDisplayFormat.FullyQualifiedFormat;

    public static string Compute(
        INamedTypeSymbol contextType,
        ImmutableArray<RootType> roots,
        Compilation compilation)
    {
        var hash = StableHash.Create();
        hash.Add(FormatVersion);
        hash.Add(typeof(ReachableFingerprint).Assembly.ManifestModule.ModuleVersionId.ToString());
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

    private static void AddType(
        ref StableHash hash,
        ITypeSymbol type,
        Stack<ITypeSymbol> pending)
    {
        hash.Add((int)type.TypeKind);
        hash.Add((int)type.SpecialType);
        hash.Add((int)type.DeclaredAccessibility);
        hash.Add(type.IsValueType);
        hash.Add(type.IsRefLikeType);
        if (type is INamedTypeSymbol namedType)
        {
            hash.Add(namedType.IsFileLocal);
            hash.Add(namedType.IsUnboundGenericType);
        }

        hash.Add(type.ToDisplayString(FullyQualified));
        AddAssemblyIdentity(ref hash, type);
        AddDeclarationHashes(ref hash, type);
        AddAttributes(ref hash, type);
        if (type is INamedTypeSymbol attributedType)
        {
            AddDerivedTypes(attributedType, pending);
        }

        AddCollectionBuilderTypes(type, pending);

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
                AddContainingTypeShape(ref hash, named);
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
            // Defensive: AddAttributes and the declaration hashes in AddType already cover the
            // string-enum marker; hashing it here keeps the enum branch self-describing if that
            // ever changes.
            hash.Add(named.GetAttributes().Any(static attribute =>
                attribute.AttributeClass?.ToDisplayString() == StringEnumAttribute));

            if (named.EnumUnderlyingType is { } underlying)
            {
                pending.Push(underlying);
            }

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

        if (IsCollectionType(named))
        {
            if (CollectionPatterns.TryGetMapTypes(named, out var keyType, out var valueType))
            {
                pending.Push(keyType);
                pending.Push(valueType);
            }
            else if (CollectionPatterns.TryGetSequenceElementType(named, out var elementType))
            {
                pending.Push(elementType);
            }

            return;
        }

        if (IsStoppedType(named))
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
            // [NbtExtensionData] members need no dedicated walk: the attribute is hashed here,
            // the member shape is covered by the declaration text hashed in AddType, and the
            // generator MVID gates emission-format changes.
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
                        hash.Add(parameter.HasExplicitDefaultValue);
                        if (parameter.HasExplicitDefaultValue)
                        {
                            AddConstant(ref hash, parameter.ExplicitDefaultValue);
                        }

                        pending.Push(parameter.Type);
                    }

                    break;
            }
        }
    }

    private static void AddContainingTypeShape(ref StableHash hash, INamedTypeSymbol named)
    {
        for (var containingType = named.ContainingType;
             containingType is not null;
             containingType = containingType.ContainingType)
        {
            hash.Add("containing");
            hash.Add(containingType.ToDisplayString(FullyQualified));
            hash.Add((int)containingType.DeclaredAccessibility);
            hash.Add(containingType.IsFileLocal);
            hash.Add(containingType.IsGenericType);
        }

        hash.Add(-1);
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

    private static void AddConstant(ref StableHash hash, object? value)
    {
        if (value is null)
        {
            hash.Add("null");
            return;
        }

        hash.Add(value.GetType().FullName);
        hash.Add(value switch
        {
            string text => text,
            bool flag => flag ? "true" : "false",
            char character => character.ToString(),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString(),
        });
    }

    private static bool IsCollectionType(INamedTypeSymbol named) =>
        CollectionShapeClassifier.IsCollectionOrMap(named) ||
        CollectionPatterns.TryGetMapTypes(named, out _, out _) ||
        CollectionPatterns.TryGetSequenceElementType(named, out _);

    private static void AddCollectionBuilderTypes(
        ITypeSymbol type,
        Stack<ITypeSymbol> pending)
    {
        if (type is not INamedTypeSymbol named)
        {
            return;
        }

        foreach (var attribute in type.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != CollectionBuilderAttributeName)
            {
                continue;
            }

            if (attribute.ConstructorArguments.Length >= 1 &&
                attribute.ConstructorArguments[0].Value is ITypeSymbol builderType)
            {
                pending.Push(builderType);
            }

            break;
        }

        if (IsCollectionType(named))
        {
            var companion = CollectionPatterns.FindCompanionType(named.OriginalDefinition);
            if (companion is not null)
            {
                pending.Push(companion);
            }
        }
    }

    private static void AddDerivedTypes(INamedTypeSymbol type, Stack<ITypeSymbol> pending)
    {
        foreach (var attribute in type.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != DerivedAttribute)
            {
                continue;
            }

            if (attribute.ConstructorArguments.Length == 1 &&
                attribute.ConstructorArguments[0].Value is ITypeSymbol derivedType)
            {
                pending.Push(derivedType);
            }
        }
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
        // Metadata types are tracked by assembly identity; a referenced assembly rebuilt with the same
        // identity is assumed to be unchanged apart from the shape facts hashed by AddType.
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
}

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace NbtSharp.SourceGeneration;

internal enum TypeEntryKind
{
    Object,
    Array,
    Nullable,
    Collection,
    Map,
    Enum,
    Polymorphic,
}

internal sealed class DerivedTypeModel
{
    public DerivedTypeModel(INamedTypeSymbol type, string discriminator)
    {
        Type = type;
        Discriminator = discriminator;
    }

    public INamedTypeSymbol Type { get; }

    public string Discriminator { get; }
}

internal sealed class TypeEntry
{
    public TypeEntry(ITypeSymbol type, TypeEntryKind kind)
    {
        Type = type;
        Kind = kind;
    }

    public ITypeSymbol Type { get; }

    public TypeEntryKind Kind { get; }

    public string AccessorName { get; set; } = "";

    public string BackingFieldName { get; set; } = "";

    public string ConverterName { get; set; } = "";

    public string StaticConverterName { get; set; } = "";

    public string? BuilderName { get; set; }

    public string? PublicPropertyName { get; set; }

    public bool IsRoot { get; set; }

    public ObjectModel? Object { get; set; }

    public ITypeSymbol? ElementType { get; set; }

    public CollectionShape CollectionShape { get; set; }

    public GeneratedBuilderSpec? GeneratedBuilder { get; set; }

    public ITypeSymbol? ValueType { get; set; }

    public MapShape MapShape { get; set; }

    public ITypeSymbol? UnderlyingType { get; set; }

    public bool EnumIsString { get; set; }

    public ImmutableArray<DerivedTypeModel> DerivedTypes { get; set; } = [];
}

internal sealed class ObjectModel
{
    public ObjectModel(
        INamedTypeSymbol type,
        ImmutableArray<MemberModel> members,
        ConstructorModel? constructor,
        bool useConstructorAccessor,
        MemberModel? extensionMember = null)
    {
        Type = type;
        Members = members;
        Constructor = constructor;
        UseConstructorAccessor = useConstructorAccessor;
        ExtensionMember = extensionMember;
    }

    public INamedTypeSymbol Type { get; }

    public ImmutableArray<MemberModel> Members { get; }

    public ConstructorModel? Constructor { get; }

    public bool UseConstructorAccessor { get; }

    /// <summary>
    /// The [NbtExtensionData] member, excluded from <see cref="Members"/> so it never participates
    /// in name grouping, constructor binding, or converter emission. Its Index is Members.Length.
    /// </summary>
    public MemberModel? ExtensionMember { get; }
}

internal sealed class ConstructorModel
{
    public ConstructorModel(IMethodSymbol symbol, int[] parameterMemberIndexes)
    {
        Symbol = symbol;
        ParameterMemberIndexes = parameterMemberIndexes;
    }

    public IMethodSymbol Symbol { get; }

    public int[] ParameterMemberIndexes { get; }
}

internal sealed class MemberModel
{
    public MemberModel(
        ISymbol symbol,
        string clrName,
        string nbtName,
        ITypeSymbol type,
        bool isField,
        bool canWrite,
        bool isInitOnly,
        ITypeSymbol? setterContainingType)
    {
        Symbol = symbol;
        ClrName = clrName;
        NbtName = nbtName;
        Type = type;
        IsField = isField;
        CanWrite = canWrite;
        IsInitOnly = isInitOnly;
        SetterContainingType = setterContainingType;
    }

    public ISymbol Symbol { get; }

    public string ClrName { get; }

    public string NbtName { get; }

    public ITypeSymbol Type { get; }

    public bool IsField { get; }

    public bool CanWrite { get; }

    public bool IsRequired { get; set; }

    public bool IsExtensionData { get; set; }

    public ConverterMode ConverterMode { get; set; } = ConverterMode.Generated;

    public ITypeSymbol? ExplicitConverterType { get; set; }

    public bool ExplicitConverterIsFactory { get; set; }

    public bool IsInitOnly { get; }

    public ITypeSymbol? SetterContainingType { get; }

    public ITypeSymbol? DeclaringType { get; set; }

    public int Index { get; set; }

    public int ConstructorParameterIndex { get; set; } = -1;

    public bool NeedsSetterAccessor => CanWrite && !IsField && (IsInitOnly || !IsPublicSetter);

    private bool IsPublicSetter =>
        Symbol is IPropertySymbol { SetMethod.DeclaredAccessibility: Accessibility.Public };
}

internal sealed class TypeGraph
{
    private const string IgnoreAttribute = "NbtSharp.NbtIgnoreAttribute";
    private const string IncludeAttribute = "NbtSharp.NbtIncludeAttribute";
    private const string PropertyNameAttribute = "NbtSharp.NbtPropertyNameAttribute";
    private const string UseConverterAttribute = "NbtSharp.NbtUseConverterAttribute";
    private const string DerivedAttribute = "NbtSharp.NbtDerivedAttribute";
    private const string StringEnumAttribute = "NbtSharp.NbtStringEnumAttribute";
    private const string ExtensionDataAttribute = "NbtSharp.NbtExtensionDataAttribute";

    private static readonly SymbolDisplayFormat FullyQualified = SymbolDisplayFormat.FullyQualifiedFormat;

    private readonly Action<DiagnosticSpec> _report;
    private readonly IAssemblySymbol _contextAssembly;
    private readonly Func<string, string>? _namePolicy;
    private readonly Dictionary<ITypeSymbol, TypeEntry> _entries =
        new(SymbolEqualityComparer.Default);
    private readonly HashSet<ITypeSymbol> _reported = new(SymbolEqualityComparer.Default);

    public TypeGraph(
        IAssemblySymbol contextAssembly,
        Action<DiagnosticSpec> report,
        Func<string, string>? namePolicy = null)
    {
        _contextAssembly = contextAssembly;
        _report = report;
        _namePolicy = namePolicy;
    }

    public List<TypeEntry> Entries { get; } = [];

    public bool HasErrors { get; private set; }

    public TypeEntry? AddRoot(ITypeSymbol type, string? propertyName, Location? location)
    {
        var entry = AddType(type, location, "root type");
        if (entry is not null)
        {
            entry.IsRoot = true;
            if (propertyName is not null)
            {
                entry.PublicPropertyName = propertyName;
            }
        }

        return entry;
    }

    public TypeEntry? AddType(ITypeSymbol type, Location? location, string origin)
    {
        if (type.TypeKind == TypeKind.Error)
        {
            ReportUnsupported(type, origin, location);
            return null;
        }

        if (IsBuiltIn(type))
        {
            return null;
        }

        if (_entries.TryGetValue(type, out var existing))
        {
            return existing;
        }

        if (!IsAccessible(type))
        {
            Report(Diagnostics.InaccessibleType, location, type.ToDisplayString(FullyQualified));
            HasErrors = true;
            return null;
        }

        if (type is IArrayTypeSymbol array)
        {
            if (array.Rank != 1)
            {
                ReportUnsupported(type, "has more than one dimension", location);
                return null;
            }

            var arrayEntry = new TypeEntry(type, TypeEntryKind.Array)
            {
                ElementType = array.ElementType,
            };
            Register(arrayEntry);
            AddType(array.ElementType, location, $"element type of {type.ToDisplayString(FullyQualified)}");
            return arrayEntry;
        }

        if (type is not INamedTypeSymbol named)
        {
            ReportUnsupported(type, "is not a named type", location);
            return null;
        }

        if (named.ContainingType is { IsGenericType: true })
        {
            ReportUnsupported(type, "is a nested generic type", location);
            return null;
        }

        if (named.TypeKind == TypeKind.Enum)
        {
            var underlying = named.EnumUnderlyingType;
            if (underlying is null || !IsSupportedEnumUnderlying(underlying.SpecialType))
            {
                ReportUnsupported(type, "has an unsupported enum underlying type", location);
                return null;
            }

            var enumEntry = new TypeEntry(type, TypeEntryKind.Enum)
            {
                UnderlyingType = underlying,
                EnumIsString = HasStringEnumAttribute(named),
            };
            Register(enumEntry);
            return enumEntry;
        }

        if (HasDerivedRegistrations(named))
        {
            if (ImplementsEnumerable(named))
            {
                return ReportInvalidDerived(
                    named,
                    location,
                    "the base type is a collection shape and cannot be polymorphic");
            }

            return AddPolymorphicType(named, location);
        }

        if (named.IsGenericType)
        {
            var definition = named.OriginalDefinition;
            if (CollectionShapeClassifier.TryGetUnsupportedReason(definition, out var unsupportedReason))
            {
                Report(
                    Diagnostics.UnsupportedType,
                    location,
                    type.ToDisplayString(FullyQualified),
                    unsupportedReason);
                HasErrors = true;
                return null;
            }

            if (IsNamed(definition, "System", "Nullable", 1))
            {
                var underlying = named.TypeArguments[0];
                var nullableEntry = new TypeEntry(type, TypeEntryKind.Nullable)
                {
                    UnderlyingType = underlying,
                };
                Register(nullableEntry);
                AddType(underlying, location, $"underlying type of {type.ToDisplayString(FullyQualified)}");
                return nullableEntry;
            }

            if (CollectionShapeClassifier.TryGetMap(definition, out var mapShape))
            {
                if (named.TypeArguments[0].SpecialType != SpecialType.System_String)
                {
                    Report(
                        Diagnostics.UnsupportedType,
                        location,
                        type.ToDisplayString(FullyQualified),
                        "has a dictionary key type other than string");
                    HasErrors = true;
                    return null;
                }

                var value = named.TypeArguments[1];
                var mapEntry = new TypeEntry(type, TypeEntryKind.Map)
                {
                    MapShape = mapShape,
                    ValueType = value,
                };
                Register(mapEntry);
                AddType(value, location, $"value type of {type.ToDisplayString(FullyQualified)}");
                return mapEntry;
            }

            if (CollectionShapeClassifier.TryGetSequence(definition, out var collectionShape))
            {
                var element = named.TypeArguments[0];
                var collectionEntry = new TypeEntry(type, TypeEntryKind.Collection)
                {
                    CollectionShape = collectionShape,
                    ElementType = element,
                };
                Register(collectionEntry);
                AddType(element, location, $"element type of {type.ToDisplayString(FullyQualified)}");
                return collectionEntry;
            }
        }

        if (named.TypeKind is TypeKind.Class or TypeKind.Struct && !named.IsAbstract)
        {
            if (CollectionPatterns.TryGetMapTypes(named, out var keyType, out var valueType))
            {
                if (keyType.SpecialType != SpecialType.System_String)
                {
                    Report(
                        Diagnostics.UnsupportedType,
                        location,
                        type.ToDisplayString(FullyQualified),
                        "has a dictionary key type other than string");
                    HasErrors = true;
                    return null;
                }

                var builder = CollectionPatterns.TryCreateMap(
                    named,
                    valueType,
                    _contextAssembly);
                if (builder is null)
                {
                    Report(
                        Diagnostics.UnsupportedCollection,
                        location,
                        type.ToDisplayString(FullyQualified));
                    HasErrors = true;
                    return null;
                }

                var mapEntry = new TypeEntry(type, TypeEntryKind.Map)
                {
                    MapShape = MapShape.Generated,
                    ValueType = valueType,
                    GeneratedBuilder = builder,
                };
                Register(mapEntry);
                AddType(valueType, location, $"value type of {type.ToDisplayString(FullyQualified)}");
                return mapEntry;
            }

            if (CollectionPatterns.TryGetSequenceElementType(named, out var elementType))
            {
                var builder = CollectionPatterns.TryCreateSequence(
                    named,
                    elementType,
                    _contextAssembly);
                if (builder is null)
                {
                    Report(
                        Diagnostics.UnsupportedCollection,
                        location,
                        type.ToDisplayString(FullyQualified));
                    HasErrors = true;
                    return null;
                }

                var collectionEntry = new TypeEntry(type, TypeEntryKind.Collection)
                {
                    CollectionShape = CollectionShape.Generated,
                    ElementType = elementType,
                    GeneratedBuilder = builder,
                };
                Register(collectionEntry);
                AddType(elementType, location, $"element type of {type.ToDisplayString(FullyQualified)}");
                return collectionEntry;
            }
        }

        if (IsUnsupported(type, out var reason))
        {
            ReportUnsupported(type, reason, location);
            return null;
        }

        if (named.TypeKind is not TypeKind.Class and not TypeKind.Struct)
        {
            ReportUnsupported(type, "is not a class or struct", location);
            return null;
        }

        if (ImplementsEnumerable(named))
        {
            Report(
                Diagnostics.UnsupportedCollection,
                location,
                type.ToDisplayString(FullyQualified));
            HasErrors = true;
            return null;
        }

        var objectEntry = new TypeEntry(type, TypeEntryKind.Object);
        Register(objectEntry);
        objectEntry.Object = BuildObjectModel(named, location);
        return objectEntry;
    }

    private TypeEntry? AddPolymorphicType(INamedTypeSymbol type, Location? location)
    {
        if (type.IsValueType)
        {
            return ReportInvalidDerived(type, location, "the base type must be a class or interface");
        }

        if (!type.IsAbstract && type.TypeKind != TypeKind.Interface)
        {
            return ReportInvalidDerived(
                type,
                location,
                "the base type must be abstract or an interface");
        }

        if (type.IsSealed)
        {
            return ReportInvalidDerived(type, location, "the base type must not be sealed");
        }

        if (type.IsGenericType)
        {
            return ReportInvalidDerived(
                type,
                location,
                "generic polymorphic base types are not supported");
        }

        var derivedTypes = ImmutableArray.CreateBuilder<DerivedTypeModel>();
        var discriminators = new HashSet<string>(StringComparer.Ordinal);
        var registeredTypes = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var attribute in type.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != DerivedAttribute)
            {
                continue;
            }

            if (attribute.ConstructorArguments.Length != 1 ||
                attribute.ConstructorArguments[0].Value is not INamedTypeSymbol derivedType)
            {
                return ReportInvalidDerived(
                    type,
                    location,
                    "the constructor argument must be a type");
            }

            if (SymbolEqualityComparer.Default.Equals(derivedType, type) ||
                derivedType.TypeKind is not TypeKind.Class and not TypeKind.Struct ||
                derivedType.IsAbstract ||
                derivedType.IsUnboundGenericType ||
                derivedType.IsGenericType &&
                derivedType.TypeArguments.Any(static argument =>
                    argument.TypeKind == TypeKind.TypeParameter))
            {
                return ReportInvalidDerived(
                    type,
                    location,
                    $"'{derivedType.ToDisplayString(FullyQualified)}' must be a closed concrete class or struct");
            }

            if (ImplementsEnumerable(derivedType))
            {
                return ReportInvalidDerived(
                    type,
                    location,
                    $"'{derivedType.ToDisplayString(FullyQualified)}' must not be a collection shape");
            }

            if (HasDerivedRegistrations(derivedType))
            {
                return ReportInvalidDerived(
                    type,
                    location,
                    $"'{derivedType.ToDisplayString(FullyQualified)}' is itself polymorphic and cannot be registered as a derived type");
            }

            if (!IsDerivedFrom(type, derivedType))
            {
                return ReportInvalidDerived(
                    type,
                    location,
                    $"'{derivedType.ToDisplayString(FullyQualified)}' is not assignable to the base type");
            }

            if (!registeredTypes.Add(derivedType))
            {
                return ReportInvalidDerived(
                    type,
                    location,
                    $"'{derivedType.ToDisplayString(FullyQualified)}' is registered more than once");
            }

            string? discriminator = null;
            foreach (var argument in attribute.NamedArguments)
            {
                if (argument.Key == "TypeDiscriminator" &&
                    argument.Value.Value is string explicitDiscriminator)
                {
                    discriminator = explicitDiscriminator;
                }
            }

            discriminator ??= derivedType.Name;
            if (string.IsNullOrEmpty(discriminator))
            {
                return ReportInvalidDerived(
                    type,
                    location,
                    $"'{derivedType.ToDisplayString(FullyQualified)}' has an empty discriminator");
            }

            if (!discriminators.Add(discriminator))
            {
                return ReportInvalidDerived(
                    type,
                    location,
                    $"the discriminator '{discriminator}' is registered more than once");
            }

            derivedTypes.Add(new DerivedTypeModel(derivedType, discriminator));
        }

        var entry = new TypeEntry(type, TypeEntryKind.Polymorphic)
        {
            DerivedTypes = derivedTypes.ToImmutable(),
        };
        Register(entry);
        foreach (var derivedType in entry.DerivedTypes)
        {
            AddType(
                derivedType.Type,
                location,
                $"derived type of {type.ToDisplayString(FullyQualified)}");
        }

        return entry;
    }

    private TypeEntry? ReportInvalidDerived(
        INamedTypeSymbol type,
        Location? location,
        string reason)
    {
        Report(
            Diagnostics.InvalidDerived,
            location ?? type.Locations.FirstOrDefault(),
            type.ToDisplayString(FullyQualified),
            reason);
        HasErrors = true;
        return null;
    }

    private static bool HasDerivedRegistrations(INamedTypeSymbol type) =>
        type.GetAttributes().Any(static attribute =>
            attribute.AttributeClass?.ToDisplayString() == DerivedAttribute);

    private static bool HasStringEnumAttribute(INamedTypeSymbol type) =>
        type.GetAttributes().Any(static attribute =>
            attribute.AttributeClass?.ToDisplayString() == StringEnumAttribute);

    private static bool IsDerivedFrom(INamedTypeSymbol baseType, INamedTypeSymbol derivedType)
    {
        if (baseType.TypeKind == TypeKind.Interface)
        {
            return derivedType.AllInterfaces.Any(candidate =>
                SymbolEqualityComparer.Default.Equals(
                    candidate.OriginalDefinition,
                    baseType.OriginalDefinition));
        }

        for (var current = derivedType.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(
                    current.OriginalDefinition,
                    baseType.OriginalDefinition))
            {
                return true;
            }
        }

        return false;
    }

    private ObjectModel? BuildObjectModel(INamedTypeSymbol type, Location? location)
    {
        var collected = CollectMembers(type, location);
        if (collected is null)
        {
            HasErrors = true;
            return null;
        }

        var members = collected.Members;
        var extensionMember = collected.Extension;

        var duplicate = members
            .GroupBy(static member => member.NbtName, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
        {
            Report(
                Diagnostics.DuplicateMemberName,
                location,
                type.ToDisplayString(FullyQualified),
                duplicate.Key);
            HasErrors = true;
            return null;
        }

        var constructor = SelectConstructor(type, members, location);
        if (constructor is not null)
        {
            for (var i = 0; i < constructor.ParameterMemberIndexes.Length; i++)
            {
                members[constructor.ParameterMemberIndexes[i]].ConstructorParameterIndex = i;
            }
        }

        var requiredSymbols = CollectRequiredMembers(type);
        var unmappedRequired = requiredSymbols
            .Where(required => !members.Any(member => MatchesMember(member, required)))
            .ToArray();
        if (unmappedRequired.Length > 0 &&
            (constructor is null || !SetsRequiredMembers(constructor.Symbol)))
        {
            var required = unmappedRequired[0];
            Report(
                Diagnostics.RequiredMemberNotMapped,
                required.Locations.FirstOrDefault() ?? location,
                type.ToDisplayString(FullyQualified),
                required.Name);
            HasErrors = true;
            return null;
        }

        foreach (var member in members)
        {
            member.IsRequired = requiredSymbols.Any(required => MatchesMember(member, required));
        }

        var mappedRequired = members.Where(static member => member.IsRequired).ToArray();
        var useConstructorAccessor = false;
        if (mappedRequired.Length > 0 &&
            (constructor is null || !SetsRequiredMembers(constructor.Symbol)))
        {
            if (type.IsValueType)
            {
                Report(Diagnostics.RequiredValueType, location, type.ToDisplayString(FullyQualified));
                HasErrors = true;
                return null;
            }

            useConstructorAccessor = true;
        }

        for (var i = 0; i < members.Count; i++)
        {
            members[i].Index = i;
        }

        if (extensionMember is not null)
        {
            // The extension member indexes past every mapped member so generated local names do
            // not collide; it is not part of Members and is never constructor-bound.
            extensionMember.Index = members.Count;
        }

        foreach (var member in members)
        {
            if (member.ConverterMode != ConverterMode.Generated)
            {
                continue;
            }

            AddType(
                member.Type,
                member.Symbol.Locations.FirstOrDefault(),
                $"member '{member.ClrName}' of {type.ToDisplayString(FullyQualified)}");
        }

        return new ObjectModel(
            type,
            members.ToImmutableArray(),
            constructor,
            useConstructorAccessor,
            extensionMember);
    }

    private sealed class CollectedMembers
    {
        public List<MemberModel> Members { get; set; } = [];

        public MemberModel? Extension { get; set; }
    }

    private CollectedMembers? CollectMembers(INamedTypeSymbol type, Location? location)
    {
        var fields = new List<MemberModel>();
        var properties = new List<MemberModel>();
        var collected = new CollectedMembers();
        var overriddenProperties = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        var declarationTypes = new List<INamedTypeSymbol>();

        for (var current = type; current is not null; current = current.BaseType)
        {
            declarationTypes.Add(current);
        }

        declarationTypes.Reverse();

        foreach (var declarationType in declarationTypes)
        {
            foreach (var member in declarationType.GetMembers())
            {
                if (member is not IPropertySymbol property)
                {
                    continue;
                }

                for (var baseProperty = property.OverriddenProperty;
                     baseProperty is not null;
                     baseProperty = baseProperty.OverriddenProperty)
                {
                    overriddenProperties.Add(baseProperty.OriginalDefinition);
                }
            }
        }

        foreach (var current in declarationTypes)
        {
            foreach (var member in current.GetMembers())
            {
                if (member is IPropertySymbol property)
                {
                    if (property.IsStatic ||
                        property.IsIndexer ||
                        property.GetMethod is not { DeclaredAccessibility: Accessibility.Public } ||
                        overriddenProperties.Contains(property.OriginalDefinition))
                    {
                        continue;
                    }

                    if (FindAttribute(property, IgnoreAttribute) is not null)
                    {
                        continue;
                    }

                    if (FindAttribute(property, ExtensionDataAttribute) is not null)
                    {
                        if (!TryClaimExtensionMember(collected, property, canWrite: property.SetMethod is not null, location))
                        {
                            return null;
                        }

                        continue;
                    }

                    var nbtName = FindAttribute(property, PropertyNameAttribute)?.ConstructorArguments
                            .FirstOrDefault().Value as string ??
                        _namePolicy?.Invoke(property.Name) ??
                        property.Name;
                    var setter = property.SetMethod;
                    var memberModel = new MemberModel(
                        property,
                        property.Name,
                        nbtName,
                        property.Type,
                        isField: false,
                        canWrite: setter is not null,
                        isInitOnly: setter?.IsInitOnly == true,
                        setterContainingType: current)
                    {
                        DeclaringType = current,
                    };
                    if (!TryConfigureConverter(memberModel, property, location))
                    {
                        return null;
                    }

                    properties.Add(memberModel);
                }
                else if (member is IFieldSymbol field)
                {
                    if (field.IsStatic ||
                        field.IsConst ||
                        field.IsReadOnly ||
                        field.DeclaredAccessibility != Accessibility.Public)
                    {
                        continue;
                    }

                    if (FindAttribute(field, IgnoreAttribute) is not null)
                    {
                        continue;
                    }

                    if (FindAttribute(field, ExtensionDataAttribute) is not null)
                    {
                        if (FindAttribute(field, IncludeAttribute) is null)
                        {
                            Report(
                                Diagnostics.InvalidExtensionData,
                                field.Locations.FirstOrDefault() ?? location,
                                field.Name,
                                "fields require [NbtInclude] in addition to [NbtExtensionData]");
                            return null;
                        }

                        if (!TryClaimExtensionMember(collected, field, canWrite: true, location))
                        {
                            return null;
                        }

                        continue;
                    }

                    if (FindAttribute(field, IncludeAttribute) is null)
                    {
                        if (FindAttribute(field, UseConverterAttribute) is not null)
                        {
                            Report(
                                Diagnostics.InvalidUseConverter,
                                field.Locations.FirstOrDefault() ?? location,
                                field.Name,
                                "fields require [NbtInclude] in addition to [NbtUseConverter]");
                            return null;
                        }

                        continue;
                    }

                    var nbtName = FindAttribute(field, PropertyNameAttribute)?.ConstructorArguments
                            .FirstOrDefault().Value as string ??
                        _namePolicy?.Invoke(field.Name) ??
                        field.Name;
                    var memberModel = new MemberModel(
                        field,
                        field.Name,
                        nbtName,
                        field.Type,
                        isField: true,
                        canWrite: true,
                        isInitOnly: false,
                        setterContainingType: null)
                    {
                        DeclaringType = current,
                    };
                    if (!TryConfigureConverter(memberModel, field, location))
                    {
                        return null;
                    }

                    fields.Add(memberModel);
                }
            }
        }

        fields.AddRange(properties);
        var hidden = FindHiddenMember(fields, declarationTypes);
        if (hidden is not null)
        {
            Report(
                Diagnostics.HiddenMember,
                hidden.Value.Location ?? location,
                type.ToDisplayString(FullyQualified),
                hidden.Value.Name);
            return null;
        }

        collected.Members = fields;
        return collected;
    }

    // Claims one [NbtExtensionData] member for the type, reporting NBTSG010 and returning false on
    // any invalid declaration. The member is kept out of the mapped member lists so it never
    // reaches name grouping, constructor binding, or converter configuration.
    private bool TryClaimExtensionMember(
        CollectedMembers collected,
        ISymbol symbol,
        bool canWrite,
        Location? containingLocation)
    {
        var location = symbol.Locations.FirstOrDefault() ?? containingLocation;
        var clrName = symbol.Name;
        if (collected.Extension is not null)
        {
            Report(
                Diagnostics.InvalidExtensionData,
                location,
                clrName,
                "the type maps more than one extension member");
            return false;
        }

        var memberType = symbol switch
        {
            IPropertySymbol property => property.Type,
            IFieldSymbol field => field.Type,
            _ => null,
        };
        if (memberType is null || !IsExtensionMemberType(memberType))
        {
            Report(
                Diagnostics.InvalidExtensionData,
                location,
                clrName,
                "the member type must be NbtSharp.NbtCompound or Dictionary<string, NbtSharp.NbtValue>");
            return false;
        }

        if (FindAttribute(symbol, PropertyNameAttribute) is not null ||
            FindAttribute(symbol, UseConverterAttribute) is not null)
        {
            Report(
                Diagnostics.InvalidExtensionData,
                location,
                clrName,
                "it cannot be combined with [NbtPropertyName] or [NbtUseConverter]");
            return false;
        }

        if (!canWrite)
        {
            Report(
                Diagnostics.InvalidExtensionData,
                location,
                clrName,
                "the member must have a setter");
            return false;
        }

        if (symbol is IPropertySymbol { IsRequired: true } or IFieldSymbol { IsRequired: true })
        {
            Report(
                Diagnostics.InvalidExtensionData,
                location,
                clrName,
                "required members are not supported");
            return false;
        }

        collected.Extension = new MemberModel(
            symbol,
            clrName,
            nbtName: clrName,
            memberType,
            isField: symbol is IFieldSymbol,
            canWrite,
            isInitOnly: symbol is IPropertySymbol { SetMethod.IsInitOnly: true },
            setterContainingType: symbol.ContainingType)
        {
            IsExtensionData = true,
            DeclaringType = symbol.ContainingType,
        };
        return true;
    }

    // NbtCompound is sealed, so an exact display-name match is an exact symbol match. The
    // dictionary form must be a closed Dictionary<string, NbtValue>; derived, interface, and
    // differently-parameterized shapes are rejected.
    private static bool IsExtensionMemberType(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named)
        {
            return false;
        }

        if (named.ToDisplayString(FullyQualified) == "global::NbtSharp.NbtCompound")
        {
            return true;
        }

        return IsNamed(named.OriginalDefinition, "System.Collections.Generic", "Dictionary", 2) &&
            named.TypeArguments.Length == 2 &&
            named.TypeArguments[0].SpecialType == SpecialType.System_String &&
            named.TypeArguments[1].ToDisplayString(FullyQualified) == "global::NbtSharp.NbtValue";
    }

    private static (string Name, Location? Location)? FindHiddenMember(
        List<MemberModel> members,
        List<INamedTypeSymbol> declarationTypes)
    {
        var indexes = new Dictionary<INamedTypeSymbol, int>(SymbolEqualityComparer.Default);
        for (var i = 0; i < declarationTypes.Count; i++)
        {
            indexes[declarationTypes[i]] = i;
        }

        foreach (var member in members)
        {
            if (member.DeclaringType is not INamedTypeSymbol declaringType ||
                !indexes.TryGetValue(declaringType, out var start))
            {
                continue;
            }

            for (var i = start + 1; i < declarationTypes.Count; i++)
            {
                foreach (var candidate in declarationTypes[i].GetMembers(member.ClrName))
                {
                    if (IsOverrideOf(member.Symbol, candidate))
                    {
                        continue;
                    }

                    return (
                        member.ClrName,
                        candidate.Locations.FirstOrDefault() ??
                            member.Symbol.Locations.FirstOrDefault());
                }
            }
        }

        return null;
    }

    private static bool IsOverrideOf(ISymbol mappedMember, ISymbol candidate) =>
        mappedMember is IPropertySymbol mappedProperty &&
        candidate is IPropertySymbol candidateProperty &&
        Overrides(candidateProperty, mappedProperty);

    private static bool Overrides(IPropertySymbol property, IPropertySymbol baseProperty)
    {
        for (var current = property.OverriddenProperty;
             current is not null;
             current = current.OverriddenProperty)
        {
            if (SymbolEqualityComparer.Default.Equals(
                    current.OriginalDefinition,
                    baseProperty.OriginalDefinition))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryConfigureConverter(MemberModel member, ISymbol symbol, Location? containingLocation)
    {
        var attribute = FindAttribute(symbol, UseConverterAttribute);
        if (attribute is null)
        {
            return true;
        }

        var location = symbol.Locations.FirstOrDefault() ?? containingLocation;
        if (!CanUseRuntimeConverter(member, location))
        {
            return false;
        }

        if (attribute.ConstructorArguments.Length == 0)
        {
            member.ConverterMode = ConverterMode.Runtime;
            return true;
        }

        if (attribute.ConstructorArguments.Length != 1 ||
            attribute.ConstructorArguments[0].Value is not ITypeSymbol converterType)
        {
            Report(
                Diagnostics.InvalidUseConverter,
                location,
                member.ClrName,
                "the converter argument must be a type");
            return false;
        }

        if (!IsValidExplicitConverter(
                member.Type,
                converterType,
                out var reason,
                out var isFactory))
        {
            Report(Diagnostics.InvalidUseConverter, location, member.ClrName, reason);
            return false;
        }

        member.ConverterMode = ConverterMode.Explicit;
        member.ExplicitConverterType = converterType;
        member.ExplicitConverterIsFactory = isFactory;
        return true;
    }

    private bool CanUseRuntimeConverter(MemberModel member, Location? location)
    {
        var reason = member.Type switch
        {
            _ when member.Type.IsRefLikeType =>
                "the member type is a ref-like type and cannot be used as a generic argument",
            IPointerTypeSymbol or IFunctionPointerTypeSymbol =>
                "the member type is a pointer and cannot be used as a generic argument",
            _ when member.Type.SpecialType == SpecialType.System_Void =>
                "the member type is void",
            _ => null,
        };
        if (reason is null)
        {
            return true;
        }

        Report(Diagnostics.InvalidUseConverter, location, member.ClrName, reason);
        return false;
    }

    private bool IsValidExplicitConverter(
        ITypeSymbol memberType,
        ITypeSymbol type,
        out string reason,
        out bool isFactory)
    {
        isFactory = false;
        if (type is not INamedTypeSymbol named)
        {
            reason = "the converter type must be a named type";
            return false;
        }

        if (named.TypeKind == TypeKind.Error)
        {
            reason = "the converter type could not be resolved";
            return false;
        }

        if (named.IsUnboundGenericType)
        {
            reason = "the converter type must be a closed generic type";
            return false;
        }

        if (named.IsAbstract)
        {
            reason = "the converter type must not be abstract";
            return false;
        }

        if (!InheritsFrom(named, "global::NbtSharp.NbtConverter"))
        {
            reason = "the converter type must derive from NbtSharp.NbtConverter";
            return false;
        }

        if (!named.InstanceConstructors.Any(static constructor =>
                constructor.Parameters.Length == 0 &&
                constructor.DeclaredAccessibility == Accessibility.Public))
        {
            reason = "the converter type must have a public parameterless constructor";
            return false;
        }

        if (!IsAccessible(type))
        {
            reason = "the converter type must be accessible from the context assembly";
            return false;
        }

        isFactory = InheritsFrom(named, "global::NbtSharp.NbtConverterFactory");
        if (isFactory)
        {
            reason = "";
            return true;
        }

        var converterBase = FindConverterBase(named);
        if (converterBase is null)
        {
            reason = "the converter type must derive from NbtConverter<T> or NbtConverterFactory";
            return false;
        }

        var handledType = converterBase.TypeArguments[0];
        if (!SymbolEqualityComparer.Default.Equals(handledType, memberType))
        {
            reason =
                $"the converter handles '{handledType.ToDisplayString(FullyQualified)}' " +
                $"but the member type is '{memberType.ToDisplayString(FullyQualified)}'";
            return false;
        }

        reason = "";
        return true;
    }

    private static INamedTypeSymbol? FindConverterBase(ITypeSymbol type)
    {
        for (var current = type as INamedTypeSymbol; current is not null; current = current.BaseType)
        {
            var definition = current.OriginalDefinition;
            if (definition.Name == "NbtConverter" &&
                definition.Arity == 1 &&
                definition.ContainingNamespace?.ToDisplayString() == "NbtSharp")
            {
                return current;
            }
        }

        return null;
    }

    private static bool InheritsFrom(ITypeSymbol type, string baseTypeName)
    {
        for (var current = type as INamedTypeSymbol; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString(FullyQualified) == baseTypeName)
            {
                return true;
            }
        }

        return false;
    }

    private ConstructorModel? SelectConstructor(
        INamedTypeSymbol type,
        List<MemberModel> members,
        Location? location)
    {
        var constructors = type.InstanceConstructors
            .Where(static ctor =>
                !ctor.IsStatic &&
                ctor.DeclaredAccessibility == Accessibility.Public &&
                ctor.Parameters.All(static parameter => parameter.RefKind == RefKind.None))
            .ToArray();

        var parameterless = constructors.FirstOrDefault(static ctor => ctor.Parameters.Length == 0);
        if (parameterless is not null)
        {
            return new ConstructorModel(parameterless, []);
        }

        ConstructorModel? best = null;
        foreach (var constructor in constructors)
        {
            var indexes = new int[constructor.Parameters.Length];
            var matches = true;
            for (var i = 0; i < constructor.Parameters.Length; i++)
            {
                var parameter = constructor.Parameters[i];
                var index = members.FindIndex(member =>
                    string.Equals(member.ClrName, parameter.Name, StringComparison.OrdinalIgnoreCase) &&
                    SymbolEqualityComparer.Default.Equals(member.Type, parameter.Type));
                if (index < 0)
                {
                    matches = false;
                    break;
                }

                indexes[i] = index;
            }

            if (matches &&
                (best is null || indexes.Length > best.ParameterMemberIndexes.Length))
            {
                best = new ConstructorModel(constructor, indexes);
            }
        }

        if (best is not null)
        {
            return best;
        }

        if (type.IsValueType)
        {
            return null;
        }

        Report(Diagnostics.NoUsableConstructor, location, type.ToDisplayString(FullyQualified));
        HasErrors = true;
        return null;
    }

    private void Register(TypeEntry entry)
    {
        _entries[entry.Type] = entry;
        Entries.Add(entry);
    }

    private void ReportUnsupported(ITypeSymbol type, string reason, Location? location)
    {
        if (_reported.Add(type))
        {
            Report(Diagnostics.UnsupportedType, location, type.ToDisplayString(FullyQualified), reason);
        }

        HasErrors = true;
    }

    private void Report(DiagnosticDescriptor descriptor, Location? location, params object[] arguments)
    {
        _report(DiagnosticSpec.Create(descriptor, location, arguments));
    }

    private static AttributeData? FindAttribute(ISymbol symbol, string metadataName)
    {
        // All mapping attributes looked up here declare Inherited = true, so following the
        // override chain matches their AttributeUsage contract.
        for (ISymbol? current = symbol;
             current is not null;
             current = current is IPropertySymbol property ? property.OverriddenProperty : null)
        {
            var attribute = current.GetAttributes().FirstOrDefault(attribute =>
                attribute.AttributeClass?.ToDisplayString() == metadataName);
            if (attribute is not null)
            {
                return attribute;
            }
        }

        return null;
    }

    private static List<ISymbol> CollectRequiredMembers(INamedTypeSymbol type)
    {
        var requiredMembers = new List<ISymbol>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers())
            {
                var isRequired = member switch
                {
                    IPropertySymbol { IsRequired: true } => true,
                    IFieldSymbol { IsRequired: true } => true,
                    _ => false,
                };
                if (!isRequired)
                {
                    continue;
                }

                var memberType = GetMemberType(member);
                if (memberType is null)
                {
                    continue;
                }

                var key = (member is IFieldSymbol ? "F:" : "P:") +
                          member.Name +
                          ":" +
                          memberType.ToDisplayString(FullyQualified);
                if (seen.Add(key))
                {
                    requiredMembers.Add(member);
                }
            }
        }

        return requiredMembers;
    }

    private static ITypeSymbol? GetMemberType(ISymbol member) => member switch
    {
        IPropertySymbol property => property.Type,
        IFieldSymbol field => field.Type,
        _ => null,
    };

    private static bool MatchesMember(MemberModel member, ISymbol requiredMember)
    {
        var requiredType = GetMemberType(requiredMember);
        return requiredType is not null &&
               member.ClrName == requiredMember.Name &&
               member.IsField == requiredMember is IFieldSymbol &&
               SymbolEqualityComparer.Default.Equals(member.Type, requiredType);
    }

    private static bool SetsRequiredMembers(IMethodSymbol constructor) =>
        constructor.GetAttributes().Any(static attribute =>
            attribute.AttributeClass?.ToDisplayString() ==
            "System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute");

    private static bool IsNamed(INamedTypeSymbol type, string ns, string name, int arity) =>
        type.Name == name &&
        type.Arity == arity &&
        type.ContainingNamespace?.ToDisplayString() == ns;

    private static bool ImplementsEnumerable(INamedTypeSymbol type) =>
        type.SpecialType == SpecialType.System_Collections_IEnumerable ||
        type.AllInterfaces.Any(static interfaceType =>
            interfaceType.SpecialType == SpecialType.System_Collections_IEnumerable);

    private static bool IsBuiltIn(ITypeSymbol type)
    {
        if (type.SpecialType is
            SpecialType.System_SByte or
            SpecialType.System_Byte or
            SpecialType.System_Int16 or
            SpecialType.System_Int32 or
            SpecialType.System_Int64 or
            SpecialType.System_UInt16 or
            SpecialType.System_UInt32 or
            SpecialType.System_UInt64 or
            SpecialType.System_Char or
            SpecialType.System_Single or
            SpecialType.System_Double or
            SpecialType.System_String or
            SpecialType.System_Boolean)
        {
            return true;
        }

        if (type is IArrayTypeSymbol { Rank: 1 } array)
        {
            if (array.ElementType.SpecialType is
                SpecialType.System_Byte or
                SpecialType.System_SByte or
                SpecialType.System_Int32 or
                SpecialType.System_Int64)
            {
                return true;
            }
        }

        var name = type.ToDisplayString(FullyQualified);
        return name is
            "global::NbtSharp.NbtValue" or
            "global::NbtSharp.NbtList" or
            "global::NbtSharp.NbtCompound";
    }

    private static bool IsSupportedEnumUnderlying(SpecialType type) =>
        type is
            SpecialType.System_SByte or
            SpecialType.System_Byte or
            SpecialType.System_Int16 or
            SpecialType.System_UInt16 or
            SpecialType.System_Int32 or
            SpecialType.System_UInt32 or
            SpecialType.System_Int64 or
            SpecialType.System_UInt64;

    private static bool IsUnsupported(ITypeSymbol type, out string reason)
    {
        if (type.SpecialType is
            SpecialType.System_Object or
            SpecialType.System_IntPtr or
            SpecialType.System_UIntPtr or
            SpecialType.System_Decimal)
        {
            reason = "is not supported by the NBT object mapping";
            return true;
        }

        if (type is ITypeParameterSymbol)
        {
            reason = "is an open type parameter";
            return true;
        }

        if (type.IsRefLikeType)
        {
            reason = "is a ref-like type";
            return true;
        }

        if (type.TypeKind is TypeKind.Interface or TypeKind.Delegate)
        {
            reason = type.TypeKind switch
            {
                TypeKind.Interface => "is an interface",
                TypeKind.Delegate => "is a delegate",
                _ => $"is a {type.TypeKind.ToString().ToLowerInvariant()}",
            };
            return true;
        }

        if (type is INamedTypeSymbol named && named.IsAbstract)
        {
            reason = "is abstract";
            return true;
        }

        var name = type.ToDisplayString(FullyQualified);
        if (name is
            "global::System.Object" or
            "global::NbtSharp.NbtRoot" or
            "global::NbtSharp.NbtEndList" or
            "global::System.IntPtr" or
            "global::System.UIntPtr" or
            "global::System.Decimal" or
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
            "global::System.Numerics.BigInteger" or
            "nint" or
            "nuint")
        {
            reason = "is not supported by the NBT object mapping";
            return true;
        }

        reason = "";
        return false;
    }

    private bool IsAccessible(ITypeSymbol type)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                return IsAccessible(array.ElementType);
            case IPointerTypeSymbol or IFunctionPointerTypeSymbol:
                return false;
            case ITypeParameterSymbol:
                return false;
            case INamedTypeSymbol named:
                if (named.TypeKind == TypeKind.Error)
                {
                    return false;
                }

                for (var current = named; current is not null; current = current.ContainingType)
                {
                    if (!IsAccessible(named: current))
                    {
                        return false;
                    }
                }

                foreach (var argument in named.TypeArguments)
                {
                    if (!IsAccessible(argument))
                    {
                        return false;
                    }
                }

                return true;
            default:
                return false;
        }
    }

    private bool IsAccessible(INamedTypeSymbol named)
    {
        if (named.IsFileLocal)
        {
            return false;
        }

        switch (named.DeclaredAccessibility)
        {
            case Accessibility.Public:
                return true;
            case Accessibility.Internal:
            case Accessibility.ProtectedOrInternal:
                return SymbolEqualityComparer.Default.Equals(
                    named.ContainingAssembly,
                    _contextAssembly);
            default:
                return false;
        }
    }
}

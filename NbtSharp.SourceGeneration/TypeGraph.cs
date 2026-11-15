using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace NbtSharp.SourceGeneration;

internal enum TypeEntryKind
{
    Object,
    Array,
    List,
    ReadOnlyList,
    ReadOnlyCollection,
    Nullable,
    Dictionary,
    IDictionary,
    IReadOnlyDictionary,
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

    public string? PublicPropertyName { get; set; }

    public bool IsRoot { get; set; }

    public ObjectModel? Object { get; set; }

    public ITypeSymbol? ElementType { get; set; }

    public ITypeSymbol? ValueType { get; set; }

    public ITypeSymbol? UnderlyingType { get; set; }
}

internal sealed class ObjectModel
{
    public ObjectModel(
        INamedTypeSymbol type,
        ImmutableArray<MemberModel> members,
        ConstructorModel? constructor,
        bool useConstructorAccessor)
    {
        Type = type;
        Members = members;
        Constructor = constructor;
        UseConstructorAccessor = useConstructorAccessor;
    }

    public INamedTypeSymbol Type { get; }

    public ImmutableArray<MemberModel> Members { get; }

    public ConstructorModel? Constructor { get; }

    public bool UseConstructorAccessor { get; }
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

    public bool IsInitOnly { get; }

    public ITypeSymbol? SetterContainingType { get; }

    public int Index { get; set; }

    public int ConstructorParameterIndex { get; set; } = -1;

    public bool NeedsSetterAccessor => CanWrite && !IsField && (IsInitOnly || !IsPublicSetter);

    private bool IsPublicSetter =>
        Symbol is IPropertySymbol { SetMethod.DeclaredAccessibility: Accessibility.Public };
}

internal sealed class TypeGraph
{
    private const string SerializableAttribute = "NbtSharp.NbtSerializableAttribute";
    private const string IgnoreAttribute = "NbtSharp.NbtIgnoreAttribute";
    private const string IncludeAttribute = "NbtSharp.NbtIncludeAttribute";
    private const string PropertyNameAttribute = "NbtSharp.NbtPropertyNameAttribute";

    private static readonly SymbolDisplayFormat FullyQualified = SymbolDisplayFormat.FullyQualifiedFormat;

    private readonly Action<Diagnostic> _report;
    private readonly IAssemblySymbol _contextAssembly;
    private readonly Dictionary<ITypeSymbol, TypeEntry> _entries =
        new(SymbolEqualityComparer.Default);
    private readonly HashSet<ITypeSymbol> _reported = new(SymbolEqualityComparer.Default);

    public TypeGraph(IAssemblySymbol contextAssembly, Action<Diagnostic> report)
    {
        _contextAssembly = contextAssembly;
        _report = report;
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

        if (named.IsGenericType)
        {
            var definition = named.OriginalDefinition;
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

            if (IsNamed(definition, "System.Collections.Generic", "List", 1) ||
                IsNamed(definition, "System.Collections.Generic", "IReadOnlyList", 1) ||
                IsNamed(definition, "System.Collections.Generic", "IReadOnlyCollection", 1))
            {
                var kind = IsNamed(definition, "System.Collections.Generic", "List", 1)
                    ? TypeEntryKind.List
                    : IsNamed(definition, "System.Collections.Generic", "IReadOnlyList", 1)
                        ? TypeEntryKind.ReadOnlyList
                        : TypeEntryKind.ReadOnlyCollection;
                var element = named.TypeArguments[0];
                var collectionEntry = new TypeEntry(type, kind)
                {
                    ElementType = element,
                };
                Register(collectionEntry);
                AddType(element, location, $"element type of {type.ToDisplayString(FullyQualified)}");
                return collectionEntry;
            }

            if (IsNamed(definition, "System.Collections.Generic", "Dictionary", 2) ||
                IsNamed(definition, "System.Collections.Generic", "IDictionary", 2) ||
                IsNamed(definition, "System.Collections.Generic", "IReadOnlyDictionary", 2))
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

                var kind = IsNamed(definition, "System.Collections.Generic", "Dictionary", 2)
                    ? TypeEntryKind.Dictionary
                    : IsNamed(definition, "System.Collections.Generic", "IDictionary", 2)
                        ? TypeEntryKind.IDictionary
                        : TypeEntryKind.IReadOnlyDictionary;
                var value = named.TypeArguments[1];
                var dictionaryEntry = new TypeEntry(type, kind)
                {
                    ValueType = value,
                };
                Register(dictionaryEntry);
                AddType(value, location, $"value type of {type.ToDisplayString(FullyQualified)}");
                return dictionaryEntry;
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

    private ObjectModel? BuildObjectModel(INamedTypeSymbol type, Location? location)
    {
        var members = CollectMembers(type);
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

        foreach (var member in members)
        {
            AddType(
                member.Type,
                member.Symbol.Locations.FirstOrDefault(),
                $"member '{member.ClrName}' of {type.ToDisplayString(FullyQualified)}");
        }

        return new ObjectModel(type, members.ToImmutableArray(), constructor, useConstructorAccessor);
    }

    private List<MemberModel> CollectMembers(INamedTypeSymbol type)
    {
        var fields = new List<MemberModel>();
        var properties = new List<MemberModel>();
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

                    var nbtName = FindAttribute(property, PropertyNameAttribute)?.ConstructorArguments
                        .FirstOrDefault().Value as string ?? property.Name;
                    var setter = property.SetMethod;
                    properties.Add(new MemberModel(
                        property,
                        property.Name,
                        nbtName,
                        property.Type,
                        isField: false,
                        canWrite: setter is not null,
                        isInitOnly: setter?.IsInitOnly == true,
                        setterContainingType: current));
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

                    if (FindAttribute(field, IncludeAttribute) is null ||
                        FindAttribute(field, IgnoreAttribute) is not null)
                    {
                        continue;
                    }

                    var nbtName = FindAttribute(field, PropertyNameAttribute)?.ConstructorArguments
                        .FirstOrDefault().Value as string ?? field.Name;
                    fields.Add(new MemberModel(
                        field,
                        field.Name,
                        nbtName,
                        field.Type,
                        isField: true,
                        canWrite: true,
                        isInitOnly: false,
                        setterContainingType: null));
                }
            }
        }

        fields.AddRange(properties);
        return fields;
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
        _report(Diagnostic.Create(descriptor, location, arguments));
    }

    private static AttributeData? FindAttribute(ISymbol symbol, string metadataName) =>
        symbol.GetAttributes().FirstOrDefault(attribute =>
            attribute.AttributeClass?.ToDisplayString() == metadataName);

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

    private static bool IsUnsupported(ITypeSymbol type, out string reason)
    {
        if (type.SpecialType is
            SpecialType.System_Object or
            SpecialType.System_Char or
            SpecialType.System_UInt16 or
            SpecialType.System_UInt32 or
            SpecialType.System_UInt64 or
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

        if (type.TypeKind is TypeKind.Interface or TypeKind.Enum or TypeKind.Delegate)
        {
            reason = type.TypeKind switch
            {
                TypeKind.Interface => "is an interface",
                TypeKind.Enum => "is an enum",
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
            "global::System.Char" or
            "global::System.UInt16" or
            "global::System.UInt32" or
            "global::System.UInt64" or
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

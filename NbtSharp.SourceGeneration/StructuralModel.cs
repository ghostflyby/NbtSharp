using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using RoslynLocation = Microsoft.CodeAnalysis.Location;

namespace NbtSharp.SourceGeneration;

/// <summary>
/// The scalar kinds that generated members can read and write directly.
/// </summary>
internal enum ScalarKind
{
    None,
    SByte,
    Byte,
    Int16,
    Int32,
    Int64,
    UInt16,
    UInt32,
    UInt64,
    Char,
    Single,
    Double,
    Boolean,
    String,
    NullableSByte,
    NullableByte,
    NullableInt16,
    NullableInt32,
    NullableInt64,
    NullableSingle,
    NullableDouble,
    NullableBoolean,
    NullableUInt16,
    NullableUInt32,
    NullableUInt64,
    NullableChar,
}

/// <summary>
/// A symbol-free description of a generated serializer context.
/// </summary>
internal sealed class ContextSpec
{
    public ContextSpec(
        string hintName,
        string containingNamespace,
        string contextName,
        string accessibility,
        bool isRecord,
        bool hasSource,
        ImmutableArray<TypeSpec> entries,
        ImmutableArray<DiagnosticSpec> diagnostics)
    {
        HintName = hintName;
        ContainingNamespace = containingNamespace;
        ContextName = contextName;
        Accessibility = accessibility;
        IsRecord = isRecord;
        HasSource = hasSource;
        Entries = entries;
        Diagnostics = diagnostics;
        Fingerprint = ContextSpecFingerprint.Compute(this);
    }

    public string HintName { get; }

    public string ContainingNamespace { get; }

    public string ContextName { get; }

    public string Accessibility { get; }

    public bool IsRecord { get; }

    public bool HasSource { get; }

    public ImmutableArray<TypeSpec> Entries { get; }

    public ImmutableArray<DiagnosticSpec> Diagnostics { get; }

    public string Fingerprint { get; }
}

/// <summary>
/// A symbol-free description of a collection builder emitted into the generated context.
/// </summary>
internal sealed class GeneratedBuilderSpec
{
    public GeneratedBuilderSpec(
        bool isMap,
        string storageTypeName,
        ImmutableArray<string> createLines,
        ImmutableArray<string> addLines,
        string? containsKeyExpression,
        ImmutableArray<string> completeLines)
    {
        IsMap = isMap;
        StorageTypeName = storageTypeName;
        CreateLines = createLines;
        AddLines = addLines;
        ContainsKeyExpression = containsKeyExpression;
        CompleteLines = completeLines;
    }

    public bool IsMap { get; }

    public string StorageTypeName { get; }

    public ImmutableArray<string> CreateLines { get; }

    public ImmutableArray<string> AddLines { get; }

    public string? ContainsKeyExpression { get; }

    public ImmutableArray<string> CompleteLines { get; }
}

/// <summary>
/// A symbol-free description of one entry in a generated context.
/// </summary>
internal sealed class TypeSpec
{
    public TypeSpec(TypeEntryKind kind, string typeName, bool isValueType)
    {
        Kind = kind;
        TypeName = typeName;
        IsValueType = isValueType;
    }

    public TypeEntryKind Kind { get; }

    public string TypeName { get; }

    public bool IsValueType { get; }

    public string AccessorName { get; set; } = "";

    public string BackingFieldName { get; set; } = "";

    public string ConverterName { get; set; } = "";

    public string? BuilderName { get; set; }

    public GeneratedBuilderSpec? GeneratedBuilder { get; set; }

    public string? PublicPropertyName { get; set; }

    public bool IsRoot { get; set; }

    public bool IsPubliclyAccessible { get; set; }

    public ObjectSpec? Object { get; set; }

    public string? ElementTypeName { get; set; }

    public CollectionShape CollectionShape { get; set; }

    public string? ValueTypeName { get; set; }

    public MapShape MapShape { get; set; }

    public string? UnderlyingTypeName { get; set; }

    public EnumMappingSpec? EnumMapping { get; set; }
}

/// <summary>
/// A symbol-free description of an object converter.
/// </summary>
internal sealed class ObjectSpec
{
    public ObjectSpec(
        string typeName,
        string name,
        bool isValueType,
        bool isGenericType,
        string typeArguments,
        string typeParameterList,
        ImmutableArray<string> constraintLines,
        ImmutableArray<MemberSpec> members,
        ConstructorSpec? constructor,
        bool useConstructorAccessor,
        bool requiresRuntimeResolution)
    {
        TypeName = typeName;
        Name = name;
        IsValueType = isValueType;
        IsGenericType = isGenericType;
        TypeArguments = typeArguments;
        TypeParameterList = typeParameterList;
        ConstraintLines = constraintLines;
        Members = members;
        Constructor = constructor;
        UseConstructorAccessor = useConstructorAccessor;
        RequiresRuntimeResolution = requiresRuntimeResolution;
    }

    public string TypeName { get; }

    public string Name { get; }

    public bool IsValueType { get; }

    public bool IsGenericType { get; }

    public string TypeArguments { get; }

    public string TypeParameterList { get; }

    public ImmutableArray<string> ConstraintLines { get; }

    public ImmutableArray<MemberSpec> Members { get; }

    public ConstructorSpec? Constructor { get; }

    public bool UseConstructorAccessor { get; }

    public bool RequiresRuntimeResolution { get; }
}

/// <summary>
/// How a member resolves its converter.
/// </summary>
internal enum ConverterMode
{
    /// <summary>The source generator emits a converter for the member type.</summary>
    Generated,

    /// <summary>The converter is resolved from <c>NbtSerializerOptions.Converters</c> at runtime.</summary>
    Runtime,

    /// <summary>The member declares an explicit converter type.</summary>
    Explicit,
}

/// <summary>
/// A symbol-free description of one mapped member.
/// </summary>
internal sealed class MemberSpec
{
    public MemberSpec(
        int index,
        string clrName,
        string escapedClrName,
        string nbtName,
        string typeName,
        bool isField,
        bool canWrite,
        bool isInitOnly,
        bool needsSetterAccessor,
        bool canBeNull,
        ScalarKind scalarKind,
        int constructorParameterIndex,
        bool isRequired,
        bool setterIsGeneric,
        string setterReceiverTypeName,
        bool setterReceiverIsValueType,
        string setterReceiverTypeParameterList,
        ImmutableArray<string> setterReceiverConstraintLines,
        string setterTypeArguments,
        string setterValueTypeName,
        ConverterMode converterMode,
        string? explicitConverterTypeName,
        bool explicitConverterIsFactory)
    {
        Index = index;
        ClrName = clrName;
        EscapedClrName = escapedClrName;
        NbtName = nbtName;
        TypeName = typeName;
        IsField = isField;
        CanWrite = canWrite;
        IsInitOnly = isInitOnly;
        NeedsSetterAccessor = needsSetterAccessor;
        CanBeNull = canBeNull;
        ScalarKind = scalarKind;
        ConstructorParameterIndex = constructorParameterIndex;
        IsRequired = isRequired;
        SetterIsGeneric = setterIsGeneric;
        SetterReceiverTypeName = setterReceiverTypeName;
        SetterReceiverIsValueType = setterReceiverIsValueType;
        SetterReceiverTypeParameterList = setterReceiverTypeParameterList;
        SetterReceiverConstraintLines = setterReceiverConstraintLines;
        SetterTypeArguments = setterTypeArguments;
        SetterValueTypeName = setterValueTypeName;
        ConverterMode = converterMode;
        ExplicitConverterTypeName = explicitConverterTypeName;
        ExplicitConverterIsFactory = explicitConverterIsFactory;
    }

    public int Index { get; }

    public string ClrName { get; }

    public string EscapedClrName { get; }

    public string NbtName { get; }

    public string TypeName { get; }

    public bool IsField { get; }

    public bool CanWrite { get; }

    public bool IsInitOnly { get; }

    public bool NeedsSetterAccessor { get; }

    public bool CanBeNull { get; }

    public ScalarKind ScalarKind { get; }

    public int ConstructorParameterIndex { get; }

    public bool IsRequired { get; }

    public bool SetterIsGeneric { get; }

    public string SetterReceiverTypeName { get; }

    public bool SetterReceiverIsValueType { get; }

    public string SetterReceiverTypeParameterList { get; }

    public ImmutableArray<string> SetterReceiverConstraintLines { get; }

    public string SetterTypeArguments { get; }

    public string SetterValueTypeName { get; }

    public ConverterMode ConverterMode { get; }

    public string? ExplicitConverterTypeName { get; }

    public bool ExplicitConverterIsFactory { get; }
}

/// <summary>
/// A symbol-free description of a selected constructor.
/// </summary>
internal sealed class ConstructorSpec
{
    public ConstructorSpec(
        int[] parameterMemberIndexes,
        string parameterList,
        ImmutableArray<string?> parameterDefaults,
        string typeParameterList,
        ImmutableArray<string> constraintLines,
        string definitionTypeName,
        string createMethodName)
    {
        ParameterMemberIndexes = parameterMemberIndexes;
        ParameterList = parameterList;
        ParameterDefaults = parameterDefaults;
        TypeParameterList = typeParameterList;
        ConstraintLines = constraintLines;
        DefinitionTypeName = definitionTypeName;
        CreateMethodName = createMethodName;
    }

    public int[] ParameterMemberIndexes { get; }

    public string ParameterList { get; }

    /// <summary>
    /// Optional default expressions for constructor parameters, aligned with
    /// <see cref="ParameterMemberIndexes"/>. A null entry means the parameter has no usable default.
    /// </summary>
    public ImmutableArray<string?> ParameterDefaults { get; }

    public string TypeParameterList { get; }

    public ImmutableArray<string> ConstraintLines { get; }

    public string DefinitionTypeName { get; }

    public string CreateMethodName { get; }
}

/// <summary>
/// A symbol-free description of an enum converter's direct read/write mapping.
/// </summary>
internal sealed class EnumMappingSpec
{
    public EnumMappingSpec(string tag, string readExpression, string writeStatement)
    {
        Tag = tag;
        ReadExpression = readExpression;
        WriteStatement = writeStatement;
    }

    public string Tag { get; }

    public string ReadExpression { get; }

    public string WriteStatement { get; }
}

/// <summary>
/// A diagnostic description that can be reconstructed without retaining a compilation.
/// </summary>
internal sealed class DiagnosticSpec : IEquatable<DiagnosticSpec>
{
    public DiagnosticSpec(
        DiagnosticDescriptor descriptor,
        object[] arguments,
        string message,
        LocationSpec? location)
    {
        Descriptor = descriptor;
        Arguments = arguments;
        Message = message;
        Location = location;
    }

    public DiagnosticDescriptor Descriptor { get; }

    public object[] Arguments { get; }

    public string Message { get; }

    public LocationSpec? Location { get; }

    public static DiagnosticSpec Create(
        DiagnosticDescriptor descriptor,
        RoslynLocation? location,
        params object[] arguments)
    {
        var message = Diagnostic.Create(descriptor, RoslynLocation.None, arguments)
            .GetMessage(CultureInfo.InvariantCulture);
        return new DiagnosticSpec(
            descriptor,
            arguments,
            message,
            LocationSpec.From(location));
    }

    public Diagnostic ToDiagnostic() =>
        Diagnostic.Create(
            Descriptor,
            Location?.ToLocation() ?? RoslynLocation.None,
            Arguments);

    public bool Equals(DiagnosticSpec? other) =>
        other is not null &&
        Descriptor.Id == other.Descriptor.Id &&
        Descriptor.DefaultSeverity == other.Descriptor.DefaultSeverity &&
        StringComparer.Ordinal.Equals(Message, other.Message) &&
        Equals(Location, other.Location);

    public override bool Equals(object? obj) => obj is DiagnosticSpec other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(Descriptor.Id);
            hash = (hash * 31) + (int)Descriptor.DefaultSeverity;
            hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(Message);
            hash = (hash * 31) + (Location?.GetHashCode() ?? 0);
            return hash;
        }
    }
}

/// <summary>
/// A symbol-free description of a diagnostic location.
/// </summary>
internal sealed class LocationSpec : IEquatable<LocationSpec>
{
    private LocationSpec(string filePath, TextSpan span, LinePositionSpan lineSpan)
    {
        FilePath = filePath;
        Span = span;
        LineSpan = lineSpan;
    }

    public string FilePath { get; }

    public TextSpan Span { get; }

    public LinePositionSpan LineSpan { get; }

    public static LocationSpec? From(RoslynLocation? location)
    {
        if (location?.SourceTree is null)
        {
            return null;
        }

        return new LocationSpec(
            location.SourceTree.FilePath,
            location.SourceSpan,
            location.GetLineSpan().Span);
    }

    public RoslynLocation ToLocation() =>
        RoslynLocation.Create(FilePath, Span, LineSpan);

    public bool Equals(LocationSpec? other) =>
        other is not null &&
        StringComparer.Ordinal.Equals(FilePath, other.FilePath) &&
        Span == other.Span &&
        LineSpan == other.LineSpan;

    public override bool Equals(object? obj) => obj is LocationSpec other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(FilePath);
            hash = (hash * 31) + Span.Start;
            hash = (hash * 31) + Span.Length;
            hash = (hash * 31) + LineSpan.Start.Line;
            hash = (hash * 31) + LineSpan.Start.Character;
            hash = (hash * 31) + LineSpan.End.Line;
            hash = (hash * 31) + LineSpan.End.Character;
            return hash;
        }
    }
}

/// <summary>
/// Computes a stable fingerprint for a <see cref="ContextSpec"/>.
/// </summary>
internal static class ContextSpecFingerprint
{
    public static string Compute(ContextSpec spec)
    {
        var hash = StableHash.Create();
        hash.Add("context-spec-v3");
        hash.Add(spec.HintName);
        hash.Add(spec.ContainingNamespace);
        hash.Add(spec.ContextName);
        hash.Add(spec.Accessibility);
        hash.Add(spec.IsRecord);
        hash.Add(spec.HasSource);
        hash.Add(spec.Entries.Length);

        foreach (var entry in spec.Entries)
        {
            AddType(ref hash, entry);
        }

        hash.Add(spec.Diagnostics.Length);
        foreach (var diagnostic in spec.Diagnostics)
        {
            hash.Add(diagnostic.Descriptor.Id);
            hash.Add((int)diagnostic.Descriptor.DefaultSeverity);
            hash.Add(diagnostic.Message);
            if (diagnostic.Location is { } location)
            {
                hash.Add(location.FilePath);
                hash.Add(location.Span.Start);
                hash.Add(location.Span.Length);
                hash.Add(location.LineSpan.Start.Line);
                hash.Add(location.LineSpan.Start.Character);
                hash.Add(location.LineSpan.End.Line);
                hash.Add(location.LineSpan.End.Character);
            }
            else
            {
                hash.Add(-1);
            }
        }

        return hash.ToHex();
    }

    private static void AddType(ref StableHash hash, TypeSpec entry)
    {
        hash.Add((int)entry.Kind);
        hash.Add(entry.TypeName);
        hash.Add(entry.IsValueType);
        hash.Add(entry.AccessorName);
        hash.Add(entry.BackingFieldName);
        hash.Add(entry.ConverterName);
        hash.Add(entry.BuilderName);
        hash.Add(entry.PublicPropertyName);
        hash.Add(entry.IsRoot);
        hash.Add(entry.IsPubliclyAccessible);
        hash.Add(entry.ElementTypeName);
        hash.Add((int)entry.CollectionShape);
        hash.Add(entry.ValueTypeName);
        hash.Add((int)entry.MapShape);
        hash.Add(entry.UnderlyingTypeName);

        if (entry.GeneratedBuilder is { } builder)
        {
            hash.Add("builder");
            hash.Add(builder.IsMap);
            hash.Add(builder.StorageTypeName);
            AddStrings(ref hash, builder.CreateLines);
            AddStrings(ref hash, builder.AddLines);
            hash.Add(builder.ContainsKeyExpression);
            AddStrings(ref hash, builder.CompleteLines);
        }
        else
        {
            hash.Add(-1);
        }

        if (entry.EnumMapping is { } mapping)
        {
            hash.Add("enum");
            hash.Add(mapping.Tag);
            hash.Add(mapping.ReadExpression);
            hash.Add(mapping.WriteStatement);
        }
        else
        {
            hash.Add(-1);
        }

        if (entry.Object is not { } model)
        {
            hash.Add(-1);
            return;
        }

        hash.Add("object");
        hash.Add(model.TypeName);
        hash.Add(model.Name);
        hash.Add(model.IsValueType);
        hash.Add(model.IsGenericType);
        hash.Add(model.TypeArguments);
        hash.Add(model.TypeParameterList);
        AddStrings(ref hash, model.ConstraintLines);
        hash.Add(model.UseConstructorAccessor);
        hash.Add(model.RequiresRuntimeResolution);

        if (model.Constructor is { } constructor)
        {
            hash.Add("ctor");
            hash.Add(constructor.ParameterMemberIndexes.Length);
            foreach (var index in constructor.ParameterMemberIndexes)
            {
                hash.Add(index);
            }

            hash.Add(constructor.ParameterList);
            hash.Add(constructor.ParameterDefaults.Length);
            foreach (var parameterDefault in constructor.ParameterDefaults)
            {
                hash.Add(parameterDefault);
            }

            hash.Add(constructor.TypeParameterList);
            AddStrings(ref hash, constructor.ConstraintLines);
            hash.Add(constructor.DefinitionTypeName);
            hash.Add(constructor.CreateMethodName);
        }
        else
        {
            hash.Add(-1);
        }

        hash.Add(model.Members.Length);
        foreach (var member in model.Members)
        {
            hash.Add(member.Index);
            hash.Add(member.ClrName);
            hash.Add(member.EscapedClrName);
            hash.Add(member.NbtName);
            hash.Add(member.TypeName);
            hash.Add(member.IsField);
            hash.Add(member.CanWrite);
            hash.Add(member.IsInitOnly);
            hash.Add(member.NeedsSetterAccessor);
            hash.Add(member.CanBeNull);
            hash.Add((int)member.ScalarKind);
            hash.Add(member.ConstructorParameterIndex);
            hash.Add(member.IsRequired);
            hash.Add(member.SetterIsGeneric);
            hash.Add(member.SetterReceiverTypeName);
            hash.Add(member.SetterReceiverIsValueType);
            hash.Add(member.SetterReceiverTypeParameterList);
            AddStrings(ref hash, member.SetterReceiverConstraintLines);
            hash.Add(member.SetterTypeArguments);
            hash.Add(member.SetterValueTypeName);
            hash.Add((int)member.ConverterMode);
            hash.Add(member.ExplicitConverterTypeName);
            hash.Add(member.ExplicitConverterIsFactory);
        }
    }

    private static void AddStrings(ref StableHash hash, ImmutableArray<string> values)
    {
        hash.Add(values.Length);
        foreach (var value in values)
        {
            hash.Add(value);
        }
    }
}

using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NbtSharp.SourceGeneration;

internal sealed partial class ContextEmitter
{
    private const string ContextBaseType = "global::NbtSharp.NbtSerializerContext";
    private const string MetadataServices = "global::NbtSharp.NbtMetadataServices";

    private static readonly SymbolDisplayFormat FullyQualified = SymbolDisplayFormat.FullyQualifiedFormat;

    private readonly Action<DiagnosticSpec> _reportDiagnostic;
    private readonly ContextModel _model;
    private readonly CodeWriter _writer = new();
    private readonly Dictionary<string, TypeSpec> _entriesByTypeName = new(StringComparer.Ordinal);
    private TypeGraph _graph = null!;
    private ContextSpec? _spec;
    private bool _hasNameErrors;

    public ContextEmitter(ContextModel model, Action<DiagnosticSpec> reportDiagnostic)
    {
        _model = model;
        _reportDiagnostic = reportDiagnostic;
    }

    internal ContextEmitter(PreparedContext prepared)
    {
        _model = null!;
        _spec = prepared.Spec;
        _reportDiagnostic = static _ => { };
        foreach (var entry in _spec.Entries)
        {
            _entriesByTypeName[entry.TypeName] = entry;
        }
    }

    private string ContextTypeName =>
        _spec!.ContainingNamespace.Length == 0
            ? $"global::{_spec.ContextName}"
            : $"global::{_spec.ContainingNamespace}.{_spec.ContextName}";

    public static PreparedContext PrepareResolved(
        ContextModel model,
        string hintName,
        string cacheKey)
    {
        // The hint name is derived from the context display name, which is part of cacheKey, so a hit
        // always has the same hint. Add rejects diagnostic-bearing specs; the length check is defense
        // in depth in case the cache is populated from another path.
        if (ContextSpecCache.TryGet(cacheKey, out var cached) &&
            cached.Diagnostics.Length == 0)
        {
            return cached;
        }

        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticSpec>();
        return PrepareCore(model, hintName, diagnostics);
    }

    private static PreparedContext PrepareCore(
        ContextModel model,
        string hintName,
        ImmutableArray<DiagnosticSpec>.Builder diagnostics)
    {
        var emitter = new ContextEmitter(model, diagnostics.Add);
        var contextType = model.ContextType;

        if (!emitter.Validate(contextType))
        {
            return Cache(model.CacheKey, CreatePrepared(model, graph: null, hintName, diagnostics));
        }

        emitter._graph = new TypeGraph(
            contextType.ContainingAssembly,
            emitter._reportDiagnostic,
            GetNamingPolicy(contextType));
        ContextSpecBuilder.RecordGraphBuild();
        foreach (var root in model.Roots)
        {
            var entry = emitter._graph.AddRoot(root.Type, root.PropertyName, root.Location);
            if (entry is null && !emitter._graph.HasErrors)
            {
                emitter._reportDiagnostic(DiagnosticSpec.Create(
                    Diagnostics.BuiltInRoot,
                    root.Location,
                    root.Type.ToDisplayString(FullyQualified)));
            }
        }

        if (emitter._graph.HasErrors)
        {
            return Cache(model.CacheKey, CreatePrepared(model, graph: null, hintName, diagnostics));
        }

        emitter.AssignNames(contextType);
        if (emitter._hasNameErrors || emitter._graph.HasErrors)
        {
            return Cache(model.CacheKey, CreatePrepared(model, graph: null, hintName, diagnostics));
        }

        return Cache(model.CacheKey, CreatePrepared(model, emitter._graph, hintName, diagnostics));
    }

    public string EmitSource()
    {
        WriteSource();
        return _writer.ToString();
    }

    private static PreparedContext Cache(string cacheKey, PreparedContext prepared)
    {
        ContextSpecCache.Add(cacheKey, prepared);
        return prepared;
    }

    private static PreparedContext CreatePrepared(
        ContextModel model,
        TypeGraph? graph,
        string hintName,
        ImmutableArray<DiagnosticSpec>.Builder diagnostics)
    {
        var immutableDiagnostics = diagnostics.ToImmutable();
        var spec = ContextSpecBuilder.Build(model, graph, hintName, immutableDiagnostics);
        return new PreparedContext(spec);
    }

    private static AttributeData? GetGenerationOptionsAttribute(INamedTypeSymbol contextType) =>
        contextType.GetAttributes().FirstOrDefault(static attribute =>
            attribute.AttributeClass?.ToDisplayString() ==
            "NbtSharp.NbtSourceGenerationOptionsAttribute");

    private static Func<string, string>? GetNamingPolicy(INamedTypeSymbol contextType)
    {
        var attribute = GetGenerationOptionsAttribute(contextType);
        if (attribute is null)
        {
            return null;
        }

        foreach (var argument in attribute.NamedArguments)
        {
            if (argument is { Key: "NamingPolicy", Value.Value: int kind })
            {
                return NbtNamingPolicyConverter.Create(kind);
            }
        }

        return null;
    }

    private bool Validate(INamedTypeSymbol contextType)
    {
        var location = _model.Location;

        if (contextType.ContainingType is not null)
        {
            Report(Diagnostics.NestedContext, location, contextType.ToDisplayString());
            return false;
        }

        if (contextType.TypeKind != TypeKind.Class)
        {
            Report(Diagnostics.ContextInvalid, location, contextType.ToDisplayString(), "must be a class");
            return false;
        }

        if (contextType.IsStatic || contextType.IsAbstract || contextType.IsGenericType)
        {
            Report(
                Diagnostics.ContextInvalid,
                location,
                contextType.ToDisplayString(),
                "must be a non-static, non-abstract, non-generic class");
            return false;
        }

        if (contextType.IsFileLocal)
        {
            Report(
                Diagnostics.ContextInvalid,
                location,
                contextType.ToDisplayString(),
                "must not be file-local");
            return false;
        }

        if (!contextType.DeclaringSyntaxReferences.Any(reference =>
                reference.GetSyntax() is TypeDeclarationSyntax declaration &&
                declaration.Modifiers.Any(SyntaxKind.PartialKeyword)))
        {
            Report(Diagnostics.ContextNotPartial, location, contextType.ToDisplayString());
            return false;
        }

        var derivesFromContext = false;
        for (var baseType = contextType.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            if (baseType.ToDisplayString(FullyQualified) == ContextBaseType)
            {
                derivesFromContext = true;
                break;
            }
        }

        if (!derivesFromContext)
        {
            Report(
                Diagnostics.ContextInvalid,
                location,
                contextType.ToDisplayString(),
                "must derive from NbtSharp.NbtSerializerContext");
            return false;
        }

        if (contextType.DeclaredAccessibility is not Accessibility.Public and not Accessibility.Internal)
        {
            Report(
                Diagnostics.ContextInvalid,
                location,
                contextType.ToDisplayString(),
                "must be public or internal");
            return false;
        }

        if (!contextType.InstanceConstructors.Any(static constructor =>
                !constructor.IsStatic &&
                (constructor.Parameters.Length == 0 ||
                 constructor.Parameters.All(static parameter =>
                     parameter.IsOptional || parameter.IsParams))))
        {
            Report(
                Diagnostics.ContextInvalid,
                location,
                contextType.ToDisplayString(),
                "must declare a parameterless constructor");
            return false;
        }

        if (GetGenerationOptionsAttribute(contextType) is { } generationOptions)
        {
            foreach (var argument in generationOptions.NamedArguments)
            {
                if (argument is { Key: "NamingPolicy", Value.Value: int and not (>= 0 and <= 5) })
                {
                    Report(
                        Diagnostics.ContextInvalid,
                        location,
                        contextType.ToDisplayString(),
                        "NbtSourceGenerationOptions.NamingPolicy must be a defined NbtNamingPolicyKind");
                    return false;
                }

                if (argument is { Key: "NullValueHandling", Value.Value: int and not 0 and not 1 })
                {
                    Report(
                        Diagnostics.ContextInvalid,
                        location,
                        contextType.ToDisplayString(),
                        "NbtSourceGenerationOptions.NullValueHandling must be NbtNullValueHandling.Omit or Throw");
                    return false;
                }
            }

            if (HasConflictingDefaultOptions(contextType))
            {
                Report(
                    Diagnostics.GeneratedNameConflict,
                    location,
                    contextType.ToDisplayString(),
                    "CreateDefaultOptions");
                return false;
            }
        }

        return true;
    }

    private static bool HasConflictingDefaultOptions(INamedTypeSymbol contextType)
    {
        for (var type = contextType; type is not null; type = type.BaseType)
        {
            if (type.ToDisplayString(FullyQualified) == ContextBaseType)
            {
                return false;
            }

            foreach (var member in type.GetMembers("CreateDefaultOptions").OfType<IMethodSymbol>())
            {
                if (member.IsStatic ||
                    member.Parameters.Length != 0 ||
                    member.ReturnType.ToDisplayString(FullyQualified) !=
                    "global::NbtSharp.NbtSerializerOptions")
                {
                    continue;
                }

                if (SymbolEqualityComparer.Default.Equals(type, contextType) || member.IsSealed)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void AssignNames(INamedTypeSymbol contextType)
    {
        var location = _model.Location;
        var used = new HashSet<string>(StringComparer.Ordinal)
        {
            "Default",
            "Options",
            "GetTypeInfo",
            "CreateDefaultOptions",
            contextType.Name,
        };

        foreach (var member in contextType.GetMembers())
        {
            if (!member.IsImplicitlyDeclared)
            {
                used.Add(member.Name);
            }
        }

        if (contextType.Name == "Default" ||
            contextType.GetMembers("Default").Any(static member => !member.IsImplicitlyDeclared))
        {
            Report(
                Diagnostics.GeneratedNameConflict,
                location,
                contextType.ToDisplayString(),
                "Default");
            _hasNameErrors = true;
            return;
        }

        for (var index = 0; index < _graph.Entries.Count; index++)
        {
            var entry = _graph.Entries[index];
            entry.BackingFieldName = MakeUnique($"__typeInfo{index}", used);
            used.Add(entry.BackingFieldName);
            entry.ConverterName = MakeUnique($"Converter_{index}", used);
            used.Add(entry.ConverterName);
            entry.StaticConverterName = MakeUnique($"__staticConverter{index}", used);
            used.Add(entry.StaticConverterName);

            if (entry.GeneratedBuilder is not null)
            {
                entry.BuilderName = MakeUnique($"Builder_{index}", used);
                used.Add(entry.BuilderName);
            }

            if (!entry.IsRoot)
            {
                entry.AccessorName = MakeUnique($"TypeInfo{index}", used);
                used.Add(entry.AccessorName);
                continue;
            }

            var explicitName = entry.PublicPropertyName;
            var name = explicitName ?? CreateDefaultPropertyName(entry.Type);
            if (!SyntaxFacts.IsValidIdentifier(name) || used.Contains(name))
            {
                Report(
                    Diagnostics.GeneratedNameConflict,
                    location,
                    contextType.ToDisplayString(),
                    name);
                _hasNameErrors = true;
                return;
            }

            entry.PublicPropertyName = name;
            entry.AccessorName = name;
            used.Add(name);
        }
    }

    private void WriteSource()
    {
        var spec = _spec!;
        _writer.WriteLine("// <auto-generated/>");
        _writer.WriteLine("#nullable enable");
        _writer.WriteLine("#pragma warning disable CS1591");
        _writer.WriteLine();

        var hasNamespace = spec.ContainingNamespace.Length != 0;
        if (hasNamespace)
        {
            _writer.WriteLine($"namespace {spec.ContainingNamespace}");
            _writer.WriteLine("{");
            _writer.Indent();
        }

        var declarationKind = spec.IsRecord ? "record class" : "class";
        _writer.WriteLine($"{spec.Accessibility} partial {declarationKind} {spec.ContextName}");
        _writer.WriteLine("{");
        _writer.Indent();

        var memberTypeNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in spec.Entries)
        {
            if (entry.Object is not { } objectSpec)
            {
                continue;
            }

            foreach (var member in objectSpec.Members)
            {
                if (member.ConverterMode == ConverterMode.Generated &&
                    member.ScalarKind == ScalarKind.None &&
                    _entriesByTypeName.ContainsKey(member.TypeName))
                {
                    memberTypeNames.Add(member.TypeName);
                }
            }
        }

        foreach (var entry in spec.Entries)
        {
            if (entry.Polymorphic is not { } polymorphic)
            {
                continue;
            }

            foreach (var derivedType in polymorphic.DerivedTypes)
            {
                memberTypeNames.Add(derivedType.TypeName);
            }
        }

        foreach (var entry in spec.Entries)
        {
            if (!memberTypeNames.Contains(entry.TypeName))
            {
                continue;
            }

            _writer.WriteLine(
                $"private static readonly global::System.Lazy<" +
                $"global::NbtSharp.NbtConverter<{entry.TypeName}>> " +
                $"{entry.StaticConverterName} = new(static () => {GetFactory(entry)});");
        }

        _writer.WriteLine();
        _writer.WriteLine($"public static {spec.ContextName} Default {{ get; }} = new();");
        _writer.WriteLine();

        EmitDefaultOptions();

        foreach (var entry in spec.Entries)
        {
            EmitTypeInfoProperty(entry);
        }

        EmitGetTypeInfo();

        foreach (var entry in spec.Entries)
        {
            if (entry.GeneratedBuilder is { } builder)
            {
                _writer.WriteLine();
                EmitGeneratedBuilder(entry, builder);
            }
        }

        foreach (var entry in spec.Entries)
        {
            if (entry.Kind == TypeEntryKind.Object && entry.Object is not null)
            {
                _writer.WriteLine();
                EmitObjectConverter(entry, entry.Object);
            }
            else if (entry is { Kind: TypeEntryKind.Enum, EnumMapping: not null })
            {
                _writer.WriteLine();
                EmitEnumConverter(entry);
            }
            else if (entry is { Kind: TypeEntryKind.Polymorphic, Polymorphic: not null })
            {
                _writer.WriteLine();
                EmitPolymorphicConverter(entry, entry.Polymorphic);
            }
        }

        _writer.Outdent();
        _writer.WriteLine("}");

        if (hasNamespace)
        {
            _writer.Outdent();
            _writer.WriteLine("}");
        }
    }

    private void EmitDefaultOptions()
    {
        var spec = _spec!;
        if (spec.NullValueHandlingExpression is null)
        {
            return;
        }

        _writer.WriteLine(
            "protected override global::NbtSharp.NbtSerializerOptions CreateDefaultOptions() =>");
        _writer.Indent();
        _writer.WriteLine("new()");
        _writer.WriteLine("{");
        _writer.Indent();
        _writer.WriteLine("TypeInfoResolver = this,");
        if (spec.PropertyNamingPolicyExpression is not null)
        {
            _writer.WriteLine($"PropertyNamingPolicy = {spec.PropertyNamingPolicyExpression},");
        }

        _writer.WriteLine($"NullValueHandling = {spec.NullValueHandlingExpression},");
        _writer.Outdent();
        _writer.WriteLine("};");
        _writer.Outdent();
        _writer.WriteLine();
    }

    private void EmitTypeInfoProperty(TypeSpec entry)
    {
        var typeName = entry.TypeName;
        var visibility = !entry.IsRoot
            ? "private"
            : _spec!.Accessibility == "public" && !entry.IsPubliclyAccessible
                ? "internal"
                : "public";
        _writer.WriteLine($"private global::NbtSharp.NbtTypeInfo<{typeName}>? {entry.BackingFieldName};");
        _writer.WriteLine(
            $"{visibility} global::NbtSharp.NbtTypeInfo<{typeName}> {entry.AccessorName} =>");
        _writer.Indent();
        _writer.WriteLine(
            $"{entry.BackingFieldName} ??= new global::NbtSharp.NbtTypeInfo<{typeName}>(");
        _writer.Indent();
        _writer.WriteLine($"{GetFactory(entry)},");
        _writer.WriteLine("Options);");
        _writer.Outdent();
        _writer.Outdent();
        _writer.WriteLine();
    }

    private void EmitGetTypeInfo()
    {
        _writer.WriteLine("public override global::NbtSharp.NbtTypeInfo? GetTypeInfo(");
        _writer.Indent();
        _writer.WriteLine("global::System.Type type,");
        _writer.WriteLine("global::NbtSharp.NbtSerializerOptions options)");
        _writer.Outdent();
        _writer.WriteLine("{");
        _writer.Indent();

        foreach (var entry in _spec!.Entries)
        {
            _writer.WriteLine($"if (type == typeof({entry.TypeName}))");
            _writer.Indent();
            _writer.WriteLine($"return {entry.AccessorName};");
            _writer.Outdent();
        }

        _writer.WriteLine();
        _writer.WriteLine("return null;");
        _writer.Outdent();
        _writer.WriteLine("}");
        _writer.WriteLine();
    }

    private string GetFactory(TypeSpec entry) => entry.Kind switch
    {
        TypeEntryKind.Object => $"new {entry.ConverterName}()",
        TypeEntryKind.Enum => entry.EnumIsString
            ? $"new global::NbtSharp.NbtStringEnumConverter<{entry.TypeName}>()"
            : $"new {entry.ConverterName}()",
        TypeEntryKind.Array =>
            $"{MetadataServices}.CreateArrayConverter<{entry.ElementTypeName!}>()",
        TypeEntryKind.Collection => GetCollectionFactory(entry),
        TypeEntryKind.Map => GetMapFactory(entry),
        TypeEntryKind.Nullable =>
            $"{MetadataServices}.CreateNullableConverter<{entry.UnderlyingTypeName!}>()",
        TypeEntryKind.Polymorphic => $"new {entry.ConverterName}()",
        _ => throw new InvalidOperationException($"Unknown entry kind {entry.Kind}."),
    };

    private string GetCollectionFactory(TypeSpec entry) => entry.CollectionShape switch
    {
        CollectionShape.List =>
            $"{MetadataServices}.CreateListBackedCollectionConverter<" +
            $"{entry.TypeName}, {entry.ElementTypeName!}>()",
        CollectionShape.Set =>
            $"{MetadataServices}.CreateSetBackedCollectionConverter<" +
            $"{entry.TypeName}, {entry.ElementTypeName!}>()",
        CollectionShape.Queue =>
            $"{MetadataServices}.CreateQueueConverter<{entry.ElementTypeName!}>()",
        CollectionShape.Stack =>
            $"{MetadataServices}.CreateStackConverter<{entry.ElementTypeName!}>()",
        CollectionShape.LinkedList =>
            $"{MetadataServices}.CreateLinkedListConverter<{entry.ElementTypeName!}>()",
        CollectionShape.Collection =>
            $"{MetadataServices}.CreateCollectionConverter<{entry.ElementTypeName!}>()",
        CollectionShape.ObservableCollection =>
            $"{MetadataServices}.CreateObservableCollectionConverter<{entry.ElementTypeName!}>()",
        CollectionShape.ReadOnlyCollection =>
            $"{MetadataServices}.CreateReadOnlyCollectionWrapperConverter<{entry.ElementTypeName!}>()",
        CollectionShape.ReadOnlyObservableCollection =>
            $"{MetadataServices}.CreateReadOnlyObservableCollectionConverter<{entry.ElementTypeName!}>()",
        CollectionShape.SortedSet =>
            $"{MetadataServices}.CreateSortedSetConverter<{entry.ElementTypeName!}>()",
        CollectionShape.ImmutableArray =>
            $"{MetadataServices}.CreateImmutableArrayConverter<{entry.ElementTypeName!}>()",
        CollectionShape.ImmutableList =>
            $"{MetadataServices}.CreateImmutableListConverter<" +
            $"{entry.TypeName}, {entry.ElementTypeName!}>()",
        CollectionShape.ImmutableHashSet =>
            $"{MetadataServices}.CreateImmutableHashSetConverter<" +
            $"{entry.TypeName}, {entry.ElementTypeName!}>()",
        CollectionShape.ImmutableQueue =>
            $"{MetadataServices}.CreateImmutableQueueConverter<" +
            $"{entry.TypeName}, {entry.ElementTypeName!}>()",
        CollectionShape.ImmutableStack =>
            $"{MetadataServices}.CreateImmutableStackConverter<" +
            $"{entry.TypeName}, {entry.ElementTypeName!}>()",
        CollectionShape.ImmutableSortedSet =>
            $"{MetadataServices}.CreateImmutableSortedSetConverter<{entry.ElementTypeName!}>()",
        CollectionShape.FrozenSet =>
            $"{MetadataServices}.CreateFrozenSetConverter<{entry.ElementTypeName!}>()",
        CollectionShape.Generated =>
            $"{MetadataServices}.CreateSequenceConverter<" +
            $"{entry.TypeName}, {entry.ElementTypeName!}, {entry.BuilderName!}>()",
        _ => throw new InvalidOperationException(
            $"Unknown collection shape {entry.CollectionShape}."),
    };

    private string GetMapFactory(TypeSpec entry) => entry.MapShape switch
    {
        MapShape.Dictionary =>
            $"{MetadataServices}.CreateDictionaryBackedConverter<" +
            $"{entry.TypeName}, {entry.ValueTypeName!}>()",
        MapShape.ReadOnlyDictionary =>
            $"{MetadataServices}.CreateReadOnlyDictionaryConverter<{entry.ValueTypeName!}>()",
        MapShape.ImmutableDictionary =>
            $"{MetadataServices}.CreateImmutableDictionaryConverter<" +
            $"{entry.TypeName}, {entry.ValueTypeName!}>()",
        MapShape.ImmutableSortedDictionary =>
            $"{MetadataServices}.CreateImmutableSortedDictionaryConverter<" +
            $"{entry.TypeName}, {entry.ValueTypeName!}>()",
        MapShape.FrozenDictionary =>
            $"{MetadataServices}.CreateFrozenDictionaryConverter<{entry.ValueTypeName!}>()",
        MapShape.Generated =>
            $"{MetadataServices}.CreateMapConverter<" +
            $"{entry.TypeName}, {entry.ValueTypeName!}, {entry.BuilderName!}>()",
        _ => throw new InvalidOperationException($"Unknown map shape {entry.MapShape}."),
    };

    private void EmitObjectConverter(TypeSpec entry, ObjectSpec model)
    {
        var typeName = model.TypeName;
        if (!model.RequiresRuntimeResolution)
        {
            _writer.WriteLine(
                "[global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(" +
                "\"Trimming\", \"IL2026\", " +
                "Justification = \"The generated type graph covers every reachable type.\")]");
            _writer.WriteLine(
                "[global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(" +
                "\"AOT\", \"IL3050\", " +
                "Justification = \"The generated type graph covers every reachable type.\")]");
        }

        _writer.WriteLine($"private sealed class {entry.ConverterName} : global::NbtSharp.NbtConverter<{typeName}>");
        _writer.WriteLine("{");
        _writer.Indent();

        EmitMemberConverters(model);
        EmitNameTables(model);
        EmitAccessors(model);

        EmitRead(model, typeName);
        _writer.WriteLine();
        EmitWrite(model, typeName);
        _writer.WriteLine();
        EmitTryGetTagType(typeName);

        _writer.Outdent();
        _writer.WriteLine("}");
    }

    private void EmitPolymorphicConverter(TypeSpec entry, PolymorphicSpec model)
    {
        var typeName = entry.TypeName;
        _writer.WriteLine(
            "[global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(" +
            "\"Trimming\", \"IL2026\", " +
            "Justification = \"Polymorphic dispatch uses generated converters.\")]");
        _writer.WriteLine(
            "[global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(" +
            "\"AOT\", \"IL3050\", " +
            "Justification = \"Polymorphic dispatch uses generated converters.\")]");
        _writer.WriteLine(
            $"private sealed class {entry.ConverterName} : " +
            $"global::NbtSharp.NbtConverter<{typeName}>");
        _writer.WriteLine("{");
        _writer.Indent();

        _writer.WriteLine(
            $"public override {typeName} Read(" +
            "ref global::NbtSharp.NbtReader reader, " +
            "global::NbtSharp.NbtSerializerOptions options)");
        _writer.WriteLine("{");
        _writer.Indent();
        _writer.WriteLine(
            "if (reader.TokenType != global::NbtSharp.NbtTokenType.StartCompound)");
        _writer.WriteLine("{");
        _writer.Indent();
        _writer.WriteLine(
            "throw global::NbtSharp.NbtMetadataServices.ExpectedTag(" +
            "reader, global::NbtSharp.NbtTagType.Compound);");
        _writer.Outdent();
        _writer.WriteLine("}");
        _writer.WriteLine();
        _writer.WriteLine("string? __type = null;");
        _writer.WriteLine($"{typeName}? __result = default;");
        _writer.WriteLine("while (reader.Read())");
        _writer.WriteLine("{");
        _writer.Indent();
        _writer.WriteLine(
            "if (reader.TokenType == global::NbtSharp.NbtTokenType.EndCompound)");
        _writer.WriteLine("{");
        _writer.Indent();
        _writer.WriteLine("break;");
        _writer.Outdent();
        _writer.WriteLine("}");
        _writer.WriteLine();
        _writer.WriteLine("var __name = reader.Name;");
        _writer.WriteLine("if (__name is null)");
        _writer.WriteLine("{");
        _writer.Indent();
        _writer.WriteLine(
            "throw global::NbtSharp.NbtMetadataServices.MissingMemberName(reader);");
        _writer.Outdent();
        _writer.WriteLine("}");
        _writer.WriteLine();
        _writer.WriteLine("switch (__name)");
        _writer.WriteLine("{");
        _writer.Indent();
        _writer.WriteLine("case \"$type\":");
        _writer.Indent();
        _writer.WriteLine(
            "if (__type is not null && options.RejectDuplicateCompoundNames)");
        _writer.WriteLine("{");
        _writer.Indent();
        _writer.WriteLine(
            "throw global::NbtSharp.NbtMetadataServices.DuplicateMember(reader, \"$type\");");
        _writer.Outdent();
        _writer.WriteLine("}");
        _writer.WriteLine("__type = reader.GetString();");
        _writer.WriteLine("break;");
        _writer.Outdent();
        _writer.WriteLine("case \"$value\":");
        _writer.Indent();
        _writer.WriteLine("if (__type is null)");
        _writer.WriteLine("{");
        _writer.Indent();
        _writer.WriteLine(
            $"throw global::NbtSharp.NbtMetadataServices.MissingPolymorphicType(reader, typeof({typeName}));");
        _writer.Outdent();
        _writer.WriteLine("}");
        _writer.WriteLine();
        _writer.WriteLine(
            "if (__result is not null && options.RejectDuplicateCompoundNames)");
        _writer.WriteLine("{");
        _writer.Indent();
        _writer.WriteLine(
            "throw global::NbtSharp.NbtMetadataServices.DuplicateMember(reader, \"$value\");");
        _writer.Outdent();
        _writer.WriteLine("}");
        _writer.WriteLine();
        _writer.WriteLine("switch (__type)");
        _writer.WriteLine("{");
        _writer.Indent();
        foreach (var derivedType in model.DerivedTypes)
        {
            var discriminator = SymbolDisplay.FormatLiteral(
                derivedType.Discriminator,
                quote: true);
            var staticConverter = _entriesByTypeName[derivedType.TypeName].StaticConverterName;
            _writer.WriteLine($"case {discriminator}:");
            _writer.Indent();
            _writer.WriteLine(
                $"__result = global::NbtSharp.NbtMetadataServices.ReadValueWithConverter<" +
                $"{derivedType.TypeName}>(ref reader, " +
                $"{ContextTypeName}.{staticConverter}.Value, options, null);");
            _writer.WriteLine("break;");
            _writer.Outdent();
        }

        _writer.WriteLine("default:");
        _writer.Indent();
        _writer.WriteLine(
            $"throw global::NbtSharp.NbtMetadataServices.UnknownPolymorphicType(" +
            $"reader, __type, typeof({typeName}));");
        _writer.Outdent();
        _writer.Outdent();
        _writer.WriteLine("}");
        _writer.WriteLine("break;");
        _writer.Outdent();
        _writer.WriteLine("default:");
        _writer.Indent();
        _writer.WriteLine("reader.Skip();");
        _writer.WriteLine("break;");
        _writer.Outdent();
        _writer.Outdent();
        _writer.WriteLine("}");
        _writer.Outdent();
        _writer.WriteLine("}");
        _writer.WriteLine();
        _writer.WriteLine("if (__type is null)");
        _writer.WriteLine("{");
        _writer.Indent();
        _writer.WriteLine(
            $"throw global::NbtSharp.NbtMetadataServices.MissingPolymorphicType(reader, typeof({typeName}));");
        _writer.Outdent();
        _writer.WriteLine("}");
        _writer.WriteLine();
        _writer.WriteLine("if (__result is null)");
        _writer.WriteLine("{");
        _writer.Indent();
        _writer.WriteLine(
            $"throw global::NbtSharp.NbtMetadataServices.MissingPolymorphicValue(reader, typeof({typeName}));");
        _writer.Outdent();
        _writer.WriteLine("}");
        _writer.WriteLine();
        _writer.WriteLine("return __result;");
        _writer.Outdent();
        _writer.WriteLine("}");
        _writer.WriteLine();

        _writer.WriteLine(
            $"public override void Write(" +
            "ref global::NbtSharp.NbtWriter writer, " +
            $"{typeName} value, " +
            "global::NbtSharp.NbtSerializerOptions options)");
        _writer.WriteLine("{");
        _writer.Indent();
        _writer.WriteLine("var __runtimeType = value?.GetType();");
        var typeChecks = string.Join(
            " && ",
            model.DerivedTypes.Select(static derivedType =>
                $"__runtimeType != typeof({derivedType.TypeName})"));
        _writer.WriteLine($"if ({typeChecks})");
        _writer.WriteLine("{");
        _writer.Indent();
        _writer.WriteLine(
            $"throw global::NbtSharp.NbtMetadataServices.UnknownPolymorphicRuntimeType(" +
            $"writer, __runtimeType, typeof({typeName}));");
        _writer.Outdent();
        _writer.WriteLine("}");
        _writer.WriteLine();
        _writer.WriteLine("writer.WriteStartCompound();");
        _writer.WriteLine("switch (value)");
        _writer.WriteLine("{");
        _writer.Indent();
        foreach (var derivedType in model.DerivedTypes)
        {
            var discriminator = SymbolDisplay.FormatLiteral(
                derivedType.Discriminator,
                quote: true);
            var staticConverter = _entriesByTypeName[derivedType.TypeName].StaticConverterName;
            _writer.WriteLine(
                $"case {derivedType.TypeName} __value when " +
                $"value.GetType() == typeof({derivedType.TypeName}):");
            _writer.Indent();
            _writer.WriteLine($"writer.WriteString(\"$type\", {discriminator});");
            _writer.WriteLine(
                $"global::NbtSharp.NbtMetadataServices.WriteMemberWithConverter<" +
                $"{derivedType.TypeName}>(ref writer, \"$value\", __value, " +
                $"{ContextTypeName}.{staticConverter}.Value, options);");
            _writer.WriteLine("break;");
            _writer.Outdent();
        }

        _writer.WriteLine("default:");
        _writer.Indent();
        _writer.WriteLine(
            $"throw global::NbtSharp.NbtMetadataServices.UnknownPolymorphicRuntimeType(" +
            $"writer, value?.GetType(), typeof({typeName}));");
        _writer.Outdent();
        _writer.Outdent();
        _writer.WriteLine("}");
        _writer.WriteLine("writer.WriteEndCompound();");
        _writer.Outdent();
        _writer.WriteLine("}");
        _writer.WriteLine();

        _writer.WriteLine(
            $"public override bool TryGetTagType(" +
            $"{typeName} value, " +
            "global::NbtSharp.NbtSerializerOptions options, " +
            "out global::NbtSharp.NbtTagType tagType)");
        _writer.WriteLine("{");
        _writer.Indent();
        _writer.WriteLine("tagType = global::NbtSharp.NbtTagType.Compound;");
        _writer.WriteLine("return true;");
        _writer.Outdent();
        _writer.WriteLine("}");

        _writer.Outdent();
        _writer.WriteLine("}");
    }

    private TypeSpec? GetStaticConverterEntry(MemberSpec member) =>
        member is { ConverterMode: ConverterMode.Generated, ScalarKind: ScalarKind.None } &&
        _entriesByTypeName.TryGetValue(member.TypeName, out var entry)
            ? entry
            : null;

    private static IEnumerable<MemberSpec> AllMembers(ObjectSpec model)
    {
        foreach (var member in model.Members)
        {
            yield return member;
        }

        if (model.ExtensionMember is { } extension)
        {
            yield return extension;
        }
    }

    private void EmitMemberConverters(ObjectSpec model)
    {
        var emitted = false;
        foreach (var member in model.Members)
        {
            if (member.ConverterMode != ConverterMode.Explicit)
            {
                continue;
            }

            _writer.WriteLine(
                $"private static readonly global::NbtSharp.NbtConverter __converter{member.Index} = " +
                $"new {member.ExplicitConverterTypeName!}();");
            emitted = true;
        }

        if (emitted)
        {
            _writer.WriteLine();
        }
    }

    // Emits one pre-encoded modified UTF-8 name per member, inside the converter class. The names
    // are written as decimal byte array initializers because U+0000 and supplementary characters
    // have no representable string or u8 literal; the runtime compares them through
    // NbtReader.NameEquals and writes them through the byte-name overloads without transcoding.
    // member.Index is per type, so the fields must live in the converter class, not the context.
    // The table exists only for types with direct (scalar) members, the same predicate that gates
    // the read fast path and the write direct path; types without one never reference __name{Index}
    // on either side, so the fields would be dead.
    private void EmitNameTables(ObjectSpec model)
    {
        var hasDirectMembers = model.Members.Any(
            static member => member.ScalarKind != ScalarKind.None);
        if (!hasDirectMembers)
        {
            return;
        }

        foreach (var member in model.Members)
        {
            var encoded = NbtModifiedUtf8.GetBytes(member.NbtName);
            var literals = new string[encoded.Length];
            for (var i = 0; i < encoded.Length; i++)
            {
                literals[i] = encoded[i].ToString(CultureInfo.InvariantCulture);
            }

            _writer.WriteLine(
                $"private static readonly byte[] __name{member.Index} = " +
                $"new byte[] {{ {string.Join(", ", literals)} }};");
        }

        _writer.WriteLine();
    }

    private void EmitAccessors(ObjectSpec model)
    {
        foreach (var member in AllMembers(model))
        {
            if (!member.NeedsSetterAccessor || member.ConstructorParameterIndex >= 0)
            {
                continue;
            }

            if (member.SetterIsGeneric)
            {
                var receiverParameter = member.SetterReceiverIsValueType
                    ? $"ref {member.SetterReceiverTypeName} instance"
                    : $"{member.SetterReceiverTypeName} instance";

                _writer.WriteLine(
                    $"private static class Accessors_{member.Index}" +
                    member.SetterReceiverTypeParameterList);
                foreach (var constraint in member.SetterReceiverConstraintLines)
                {
                    _writer.WriteLine(constraint);
                }

                _writer.WriteLine("{");
                _writer.Indent();
                _writer.WriteLine(
                    "[global::System.Runtime.CompilerServices.UnsafeAccessor(" +
                    "global::System.Runtime.CompilerServices.UnsafeAccessorKind.Method, " +
                    $"Name = \"set_{member.ClrName}\")]");
                _writer.WriteLine(
                    $"internal static extern void Set_{member.Index}(" +
                    $"{receiverParameter}, {member.SetterValueTypeName} value);");
                _writer.Outdent();
                _writer.WriteLine("}");
                _writer.WriteLine();
                continue;
            }

            var declaringType = member.SetterReceiverTypeName;
            var directReceiver = member.SetterReceiverIsValueType
                ? $"ref {declaringType} instance"
                : $"{declaringType} instance";
            _writer.WriteLine(
                "[global::System.Runtime.CompilerServices.UnsafeAccessor(" +
                "global::System.Runtime.CompilerServices.UnsafeAccessorKind.Method, " +
                $"Name = \"set_{member.ClrName}\")]");
            _writer.WriteLine(
                $"private static extern void Set_{member.Index}({directReceiver}, {member.TypeName} value);");
            _writer.WriteLine();
        }

        if (model is { UseConstructorAccessor: true, Constructor: not null })
        {
            if (model.IsGenericType)
            {
                _writer.WriteLine(
                    $"private static class AccessorsCtor_0" +
                    model.TypeParameterList);
                foreach (var constraint in model.ConstraintLines)
                {
                    _writer.WriteLine(constraint);
                }

                _writer.WriteLine("{");
                _writer.Indent();
                _writer.WriteLine(
                    "[global::System.Runtime.CompilerServices.UnsafeAccessor(" +
                    "global::System.Runtime.CompilerServices.UnsafeAccessorKind.Constructor)]");
                _writer.WriteLine(
                    $"internal static extern {model.Constructor.DefinitionTypeName} Create({model.Constructor.ParameterList});");
                _writer.Outdent();
                _writer.WriteLine("}");
                _writer.WriteLine();
            }
            else
            {
                _writer.WriteLine(
                    "[global::System.Runtime.CompilerServices.UnsafeAccessor(" +
                    "global::System.Runtime.CompilerServices.UnsafeAccessorKind.Constructor)]");
                _writer.WriteLine(
                    $"private static extern {model.TypeName} Create_{model.Name}({model.Constructor.ParameterList});");
                _writer.WriteLine();
            }
        }
    }

    private void EmitRead(ObjectSpec model, string typeName)
    {
        _writer.WriteLine(
            $"public override {typeName} Read(" +
            "ref global::NbtSharp.NbtReader reader, " +
            "global::NbtSharp.NbtSerializerOptions options)");
        _writer.WriteLine("{");
        _writer.Indent();

        _writer.WriteLine(
            "if (reader.TokenType != global::NbtSharp.NbtTokenType.StartCompound)");
        _writer.WriteLine("{");
        _writer.Indent();
        _writer.WriteLine(
            "throw global::NbtSharp.NbtMetadataServices.ExpectedTag(" +
            "reader, global::NbtSharp.NbtTagType.Compound);");
        _writer.Outdent();
        _writer.WriteLine("}");
        _writer.WriteLine();

        foreach (var member in model.Members)
        {
            _writer.WriteLine($"var __v{member.Index} = default({member.TypeName})!;");
            _writer.WriteLine($"var __p{member.Index} = false;");
        }

        // The extension member has no presence flag: it is never required and is only assigned
        // once the first unknown member materializes it.
        if (model.ExtensionMember is { } extension)
        {
            _writer.WriteLine($"var __v{extension.Index} = default({extension.TypeName})!;");
        }

        var hasDirectMembers = model.Members.Any(
            static member => member.ScalarKind != ScalarKind.None);
        if (hasDirectMembers)
        {
            _writer.WriteLine("var __direct = options.Converters.Count == 0;");
            _writer.WriteLine("string? __path = null;");
        }

        _writer.WriteLine();
        _writer.WriteLine("while (reader.Read())");
        _writer.WriteLine("{");
        _writer.Indent();
        if (hasDirectMembers)
        {
            _writer.WriteLine("__path = null;");
        }

        _writer.WriteLine("if (reader.TokenType == global::NbtSharp.NbtTokenType.EndCompound)");
        _writer.WriteLine("{");
        _writer.Indent();
        foreach (var member in model.Members)
        {
            if (!member.IsRequired)
            {
                continue;
            }

            var requiredName = SymbolDisplay.FormatLiteral(member.NbtName, quote: true);
            _writer.WriteLine($"if (!__p{member.Index})");
            _writer.WriteLine("{");
            _writer.Indent();
            _writer.WriteLine(
                $"throw global::NbtSharp.NbtMetadataServices.MissingRequiredMember(" +
                $"reader, {requiredName});");
            _writer.Outdent();
            _writer.WriteLine("}");
        }

        EmitConstruction(model, typeName);
        _writer.Outdent();
        _writer.WriteLine("}");
        _writer.WriteLine();

        if (hasDirectMembers)
        {
            // The direct fast path dispatches on pre-encoded name bytes without decoding the wire
            // name; the slow path keeps the decoded-string switch. Both resolve to a member index
            // so the member body itself exists only once.
            _writer.WriteLine("try");
            _writer.WriteLine("{");
            _writer.Indent();
            _writer.WriteLine("int __matched;");
            _writer.WriteLine("if (__direct)");
            _writer.WriteLine("{");
            _writer.Indent();
            _writer.WriteLine("__matched = -1;");
            for (var i = 0; i < model.Members.Length; i++)
            {
                var member = model.Members[i];
                _writer.WriteLine(
                    i == 0
                        ? $"if (reader.NameEquals(__name{member.Index}))"
                        : $"else if (reader.NameEquals(__name{member.Index}))");
                _writer.WriteLine("{");
                _writer.Indent();
                _writer.WriteLine($"__matched = {member.Index};");
                _writer.Outdent();
                _writer.WriteLine("}");
            }

            _writer.Outdent();
            _writer.WriteLine("}");
            _writer.WriteLine("else");
            _writer.WriteLine("{");
            _writer.Indent();
            _writer.WriteLine("var __name = reader.Name;");
            _writer.WriteLine("if (__name is null)");
            _writer.WriteLine("{");
            _writer.Indent();
            _writer.WriteLine(
                "throw global::NbtSharp.NbtMetadataServices.MissingMemberName(reader);");
            _writer.Outdent();
            _writer.WriteLine("}");
            _writer.WriteLine();
            _writer.WriteLine("switch (__name)");
            _writer.WriteLine("{");
            _writer.Indent();
            foreach (var member in model.Members)
            {
                var nbtName = SymbolDisplay.FormatLiteral(member.NbtName, quote: true);
                _writer.WriteLine($"case {nbtName}:");
                _writer.Indent();
                _writer.WriteLine($"__matched = {member.Index};");
                _writer.WriteLine("break;");
                _writer.Outdent();
            }

            _writer.WriteLine("default:");
            _writer.Indent();
            _writer.WriteLine("__matched = -1;");
            _writer.WriteLine("break;");
            _writer.Outdent();
            _writer.Outdent();
            _writer.WriteLine("}");
            _writer.Outdent();
            _writer.WriteLine("}");

            _writer.WriteLine();
            _writer.WriteLine("if (__matched < 0)");
            _writer.WriteLine("{");
            _writer.Indent();
            EmitUnknownMemberBody(model);
            _writer.Outdent();
            _writer.WriteLine("}");
            _writer.WriteLine("else");
            _writer.WriteLine("{");
            _writer.Indent();
            _writer.WriteLine("switch (__matched)");
            _writer.WriteLine("{");
            _writer.Indent();
            foreach (var member in model.Members)
            {
                _writer.WriteLine($"case {member.Index}:");
                _writer.Indent();
                EmitMemberBody(member);
                _writer.WriteLine("break;");
                _writer.Outdent();
            }

            _writer.Outdent();
            _writer.WriteLine("}");
            _writer.Outdent();
            _writer.WriteLine("}");

            _writer.Outdent();
            _writer.WriteLine("}");
            _writer.WriteLine("catch (global::NbtSharp.NbtException __ex)");
            _writer.WriteLine("{");
            _writer.Indent();
            _writer.WriteLine(
                "global::NbtSharp.NbtMetadataServices.PrependPath(__ex, __path);");
            _writer.WriteLine("throw;");
            _writer.Outdent();
            _writer.WriteLine("}");
        }
        else
        {
            _writer.WriteLine("var __name = reader.Name;");
            _writer.WriteLine("if (__name is null)");
            _writer.WriteLine("{");
            _writer.Indent();
            _writer.WriteLine(
                "throw global::NbtSharp.NbtMetadataServices.MissingMemberName(reader);");
            _writer.Outdent();
            _writer.WriteLine("}");
            _writer.WriteLine();

            _writer.WriteLine("switch (__name)");
            _writer.WriteLine("{");
            _writer.Indent();
            foreach (var member in model.Members)
            {
                var nbtName = SymbolDisplay.FormatLiteral(member.NbtName, quote: true);
                _writer.WriteLine($"case {nbtName}:");
                _writer.Indent();
                EmitMemberBody(member);
                _writer.WriteLine("break;");
                _writer.Outdent();
            }

            _writer.WriteLine("default:");
            _writer.Indent();
            EmitUnknownMemberBody(model);
            _writer.WriteLine("break;");
            _writer.Outdent();
            _writer.Outdent();
            _writer.WriteLine("}");
        }

        _writer.Outdent();
        _writer.WriteLine("}");
        _writer.WriteLine();
        _writer.WriteLine(
            "throw global::NbtSharp.NbtMetadataServices.CompoundNotTerminated(reader);");

        _writer.Outdent();
        _writer.WriteLine("}");
    }

    // Emits the body for a member that matched no mapped name. Without an extension member the
    // token is skipped (the fast path keeps the established silent skip); with one the name is
    // decoded, checked for duplicates, and captured as DOM data. ReadDomValue is the only Path
    // source for this branch, so __path must not be assigned here: the fast path resets it at the
    // loop top and the outer catch prepend becomes a no-op.
    private void EmitUnknownMemberBody(ObjectSpec model)
    {
        if (model.ExtensionMember is not { } extension)
        {
            _writer.WriteLine("reader.Skip();");
            return;
        }

        _writer.WriteLine("var __n = reader.Name;");
        _writer.WriteLine("if (__n is null)");
        _writer.WriteLine("{");
        _writer.Indent();
        _writer.WriteLine(
            "throw global::NbtSharp.NbtMetadataServices.MissingMemberName(reader);");
        _writer.Outdent();
        _writer.WriteLine("}");
        _writer.WriteLine();
        _writer.WriteLine(
            $"if (__v{extension.Index} is not null && " +
            $"__v{extension.Index}.ContainsKey(__n) && options.RejectDuplicateCompoundNames)");
        _writer.WriteLine("{");
        _writer.Indent();
        _writer.WriteLine(
            "throw global::NbtSharp.NbtMetadataServices.DuplicateMember(reader, __n);");
        _writer.Outdent();
        _writer.WriteLine("}");
        _writer.WriteLine();
        _writer.WriteLine($"__v{extension.Index} ??= new {extension.TypeName}();");
        _writer.WriteLine(
            $"__v{extension.Index}[__n] = global::NbtSharp.NbtMetadataServices.ReadDomValue(" +
            "ref reader, options, \".\" + __n);");
    }

    // Emits the body shared by every dispatch shape: duplicate-name detection, the direct or
    // converter-based read, and the presence flag.
    private void EmitMemberBody(MemberSpec member)
    {
        var nbtName = SymbolDisplay.FormatLiteral(member.NbtName, quote: true);
        var path = SymbolDisplay.FormatLiteral("." + member.NbtName, quote: true);
        var scalarKind = member.ScalarKind;
        _writer.WriteLine(
            $"if (__p{member.Index} && options.RejectDuplicateCompoundNames)");
        _writer.WriteLine("{");
        _writer.Indent();
        _writer.WriteLine(
            $"throw global::NbtSharp.NbtMetadataServices.DuplicateMember(reader, {nbtName});");
        _writer.Outdent();
        _writer.WriteLine("}");

        if (scalarKind != ScalarKind.None)
        {
            _writer.WriteLine("if (__direct)");
            _writer.WriteLine("{");
            _writer.Indent();
            _writer.WriteLine($"__path = {path};");
            _writer.WriteLine($"__v{member.Index} = {GetReadExpression(scalarKind)};");
            _writer.WriteLine("__path = null;");
            _writer.Outdent();
            _writer.WriteLine("}");
            _writer.WriteLine("else");
            _writer.WriteLine("{");
            _writer.Indent();
        }

        if (member.ConverterMode == ConverterMode.Explicit)
        {
            _writer.WriteLine(
                $"__v{member.Index} = global::NbtSharp.NbtMetadataServices.ReadValueWithConverter<{member.TypeName}>(" +
                $"ref reader, __converter{member.Index}, options, {path});");
        }
        else if (GetStaticConverterEntry(member) is { } staticEntry)
        {
            _writer.WriteLine(
                $"if (options.Converters.Count == 0 && " +
                $"options.TypeInfoResolver?.GetType() == typeof({ContextTypeName}))");
            _writer.WriteLine("{");
            _writer.Indent();
            _writer.WriteLine(
                $"__v{member.Index} = global::NbtSharp.NbtMetadataServices.ReadValueWithConverter<{member.TypeName}>(" +
                $"ref reader, {ContextTypeName}.{staticEntry.StaticConverterName}.Value, options, {path});");
            _writer.Outdent();
            _writer.WriteLine("}");
            _writer.WriteLine("else");
            _writer.WriteLine("{");
            _writer.Indent();
            _writer.WriteLine(
                $"__v{member.Index} = global::NbtSharp.NbtMetadataServices.ReadValue<{member.TypeName}>(" +
                $"ref reader, options, {path});");
            _writer.Outdent();
            _writer.WriteLine("}");
        }
        else
        {
            _writer.WriteLine(
                $"__v{member.Index} = global::NbtSharp.NbtMetadataServices.ReadValue<{member.TypeName}>(" +
                $"ref reader, options, {path});");
        }

        if (scalarKind != ScalarKind.None)
        {
            _writer.Outdent();
            _writer.WriteLine("}");
        }

        _writer.WriteLine($"__p{member.Index} = true;");
    }

    private void EmitConstruction(ObjectSpec model, string typeName)
    {
        _writer.WriteLine($"var __result = {BuildConstruction(model, typeName)};");
        foreach (var member in model.Members)
        {
            if (!member.CanWrite || member.ConstructorParameterIndex >= 0)
            {
                continue;
            }

            if (member.NeedsSetterAccessor)
            {
                if (member.SetterIsGeneric)
                {
                    var receiverPrefix = member.SetterReceiverIsValueType
                        ? "ref __result, "
                        : "__result, ";
                    _writer.WriteLine(
                        $"if (__p{member.Index}) Accessors_{member.Index}" +
                        $"<{member.SetterTypeArguments}>" +
                        $".Set_{member.Index}({receiverPrefix}__v{member.Index});");
                }
                else
                {
                    var receiverPrefix = model.IsValueType ? "ref __result, " : "__result, ";
                    _writer.WriteLine(
                        $"if (__p{member.Index}) Set_{member.Index}({receiverPrefix}__v{member.Index});");
                }
            }
            else
            {
                _writer.WriteLine(
                    $"if (__p{member.Index}) __result.{member.EscapedClrName} = __v{member.Index};");
            }
        }

        if (model.ExtensionMember is { } extension)
        {
            if (extension.NeedsSetterAccessor)
            {
                if (extension.SetterIsGeneric)
                {
                    var receiverPrefix = extension.SetterReceiverIsValueType
                        ? "ref __result, "
                        : "__result, ";
                    _writer.WriteLine(
                        $"if (__v{extension.Index} is not null) Accessors_{extension.Index}" +
                        $"<{extension.SetterTypeArguments}>" +
                        $".Set_{extension.Index}({receiverPrefix}__v{extension.Index});");
                }
                else
                {
                    var receiverPrefix = model.IsValueType ? "ref __result, " : "__result, ";
                    _writer.WriteLine(
                        $"if (__v{extension.Index} is not null) " +
                        $"Set_{extension.Index}({receiverPrefix}__v{extension.Index});");
                }
            }
            else
            {
                _writer.WriteLine(
                    $"if (__v{extension.Index} is not null) " +
                    $"__result.{extension.EscapedClrName} = __v{extension.Index};");
            }
        }

        _writer.WriteLine("return __result;");
    }

    private string BuildConstruction(ObjectSpec model, string typeName)
    {
        if (model.Constructor is null)
        {
            return $"new {typeName}()";
        }

        var parameterMemberIndexes = model.Constructor.ParameterMemberIndexes;
        var parameterDefaults = model.Constructor.ParameterDefaults;
        var arguments = new string[parameterMemberIndexes.Length];
        for (var i = 0; i < parameterMemberIndexes.Length; i++)
        {
            var memberIndex = parameterMemberIndexes[i];
            var memberType = model.Members[memberIndex].TypeName;
            var fallback = i < parameterDefaults.Length && parameterDefaults[i] is { } parameterDefault
                ? parameterDefault
                : $"default({memberType})!";
            arguments[i] = $"__p{memberIndex} ? __v{memberIndex} : {fallback}";
        }

        var argumentList = string.Join(", ", arguments);
        if (!model.UseConstructorAccessor)
        {
            return $"new {typeName}({argumentList})";
        }

        return model.IsGenericType
            ? $"AccessorsCtor_0<{model.TypeArguments}>.Create({argumentList})"
            : $"{model.Constructor.CreateMethodName}({argumentList})";
    }

    private void EmitWrite(ObjectSpec model, string typeName)
    {
        _writer.WriteLine(
            $"public override void Write(" +
            "ref global::NbtSharp.NbtWriter writer, " +
            $"{typeName} value, " +
            "global::NbtSharp.NbtSerializerOptions options)");
        _writer.WriteLine("{");
        _writer.Indent();
        var hasDirectMembers = model.Members.Any(
            static member => member.ScalarKind != ScalarKind.None);
        _writer.WriteLine("writer.WriteStartCompound();");
        if (hasDirectMembers)
        {
            _writer.WriteLine("var __direct = options.Converters.Count == 0;");
            _writer.WriteLine("string? __path = null;");
            _writer.WriteLine("try");
            _writer.WriteLine("{");
            _writer.Indent();
        }

        foreach (var member in model.Members)
        {
            var nbtName = SymbolDisplay.FormatLiteral(member.NbtName, quote: true);
            var scalarKind = member.ScalarKind;
            var hasNullCheck = member.CanBeNull &&
                (member.IsRequired ||
                 IsNullableScalar(scalarKind) ||
                 scalarKind == ScalarKind.String);
            var value = hasNullCheck
                ? $"__m{member.Index}"
                : $"value.{member.EscapedClrName}";
            if (hasNullCheck)
            {
                _writer.WriteLine($"var __m{member.Index} = value.{member.EscapedClrName};");
            }

            if (member is { IsRequired: true, CanBeNull: true })
            {
                _writer.WriteLine($"if ({value} is null)");
                _writer.WriteLine("{");
                _writer.Indent();
                _writer.WriteLine(
                    $"throw {MetadataServices}.NullRequiredMember(writer, {nbtName});");
                _writer.Outdent();
                _writer.WriteLine("}");
            }

            if (scalarKind != ScalarKind.None)
            {
                var path = SymbolDisplay.FormatLiteral("." + member.NbtName, quote: true);
                _writer.WriteLine("if (__direct)");
                _writer.WriteLine("{");
                _writer.Indent();
                var needsNullFallback = !member.IsRequired &&
                    (IsNullableScalar(scalarKind) || scalarKind == ScalarKind.String);
                if (needsNullFallback)
                {
                    _writer.WriteLine($"if ({value} is null)");
                    _writer.WriteLine("{");
                    _writer.Indent();
                    _writer.WriteLine("__path = null;");
                    _writer.WriteLine(GetWriteMemberStatement(member, nbtName, value));
                    _writer.WriteLine("__path = null;");
                    _writer.Outdent();
                    _writer.WriteLine("}");
                    _writer.WriteLine("else");
                    _writer.WriteLine("{");
                    _writer.Indent();
                }

                _writer.WriteLine($"__path = {path};");
                _writer.WriteLine(GetWriteStatement(scalarKind, $"__name{member.Index}", value));
                _writer.WriteLine("__path = null;");

                if (needsNullFallback)
                {
                    _writer.Outdent();
                    _writer.WriteLine("}");
                }

                _writer.Outdent();
                _writer.WriteLine("}");
                _writer.WriteLine("else");
                _writer.WriteLine("{");
                _writer.Indent();
                _writer.WriteLine("__path = null;");
                _writer.WriteLine(GetWriteMemberStatement(member, nbtName, value));
                _writer.Outdent();
                _writer.WriteLine("}");
            }
            else
            {
                if (hasDirectMembers)
                {
                    _writer.WriteLine("__path = null;");
                }

                EmitWriteMemberStatement(member, nbtName, value);
            }
        }

        if (hasDirectMembers)
        {
            _writer.Outdent();
            _writer.WriteLine("}");
            _writer.WriteLine("catch (global::NbtSharp.NbtException __ex)");
            _writer.WriteLine("{");
            _writer.Indent();
            _writer.WriteLine(
                "global::NbtSharp.NbtMetadataServices.PrependPath(__ex, __path);");
            _writer.WriteLine("throw;");
            _writer.Outdent();
            _writer.WriteLine("}");
        }

        if (model.ExtensionMember is { } extension)
        {
            // Appended after every mapped member. The conflict chain covers the final NBT names of
            // all mapped members, including ones omitted by null handling, so an extension entry
            // can never shadow a name the mapping owns. Dictionary order is not preserved.
            _writer.WriteLine($"var __ext = value.{extension.EscapedClrName};");
            _writer.WriteLine("if (__ext is not null)");
            _writer.WriteLine("{");
            _writer.Indent();
            _writer.WriteLine("foreach (var __pair in __ext)");
            _writer.WriteLine("{");
            _writer.Indent();
            var mappedNames = model.Members
                .Select(static member => SymbolDisplay.FormatLiteral(member.NbtName, quote: true))
                .ToArray();
            if (mappedNames.Length > 0)
            {
                var chain = string.Join(
                    " || ",
                    mappedNames.Select(static name => $"__pair.Key == {name}"));
                _writer.WriteLine($"if ({chain})");
                _writer.WriteLine("{");
                _writer.Indent();
                _writer.WriteLine(
                    "throw global::NbtSharp.NbtMetadataServices.ExtensionMemberConflict(" +
                    "writer, __pair.Key);");
                _writer.Outdent();
                _writer.WriteLine("}");
                _writer.WriteLine();
            }

            _writer.WriteLine(
                "global::NbtSharp.NbtMetadataServices.WriteDomMember(" +
                "ref writer, __pair.Key, __pair.Value, options);");
            _writer.Outdent();
            _writer.WriteLine("}");
            _writer.Outdent();
            _writer.WriteLine("}");
        }

        _writer.WriteLine("writer.WriteEndCompound();");
        _writer.Outdent();
        _writer.WriteLine("}");
    }

    private void EmitWriteMemberStatement(MemberSpec member, string nbtName, string value)
    {
        if (member.ConverterMode == ConverterMode.Explicit)
        {
            _writer.WriteLine(
                $"{MetadataServices}.WriteMemberWithConverter(ref writer, {nbtName}, {value}, " +
                $"__converter{member.Index}, options);");
            return;
        }

        if (GetStaticConverterEntry(member) is { } staticEntry)
        {
            _writer.WriteLine(
                $"if (options.Converters.Count == 0 && " +
                $"options.TypeInfoResolver?.GetType() == typeof({ContextTypeName}))");
            _writer.WriteLine("{");
            _writer.Indent();
            _writer.WriteLine(
                $"{MetadataServices}.WriteMemberWithConverter(ref writer, {nbtName}, {value}, " +
                $"{ContextTypeName}.{staticEntry.StaticConverterName}.Value, options);");
            _writer.Outdent();
            _writer.WriteLine("}");
            _writer.WriteLine("else");
            _writer.WriteLine("{");
            _writer.Indent();
            _writer.WriteLine(
                $"{MetadataServices}.WriteMember(ref writer, {nbtName}, {value}, options);");
            _writer.Outdent();
            _writer.WriteLine("}");
            return;
        }

        _writer.WriteLine(
            $"{MetadataServices}.WriteMember(ref writer, {nbtName}, {value}, options);");
    }

    private static string GetWriteMemberStatement(MemberSpec member, string nbtName, string value) =>
        member.ConverterMode == ConverterMode.Explicit
            ? $"{MetadataServices}.WriteMemberWithConverter(ref writer, {nbtName}, {value}, __converter{member.Index}, options);"
            : $"{MetadataServices}.WriteMember(ref writer, {nbtName}, {value}, options);";

    private void EmitTryGetTagType(string typeName)
    {
        _writer.WriteLine(
            $"public override bool TryGetTagType(" +
            $"{typeName} value, " +
            "global::NbtSharp.NbtSerializerOptions options, " +
            "out global::NbtSharp.NbtTagType tagType)");
        _writer.WriteLine("{");
        _writer.Indent();
        _writer.WriteLine("tagType = global::NbtSharp.NbtTagType.Compound;");
        _writer.WriteLine("return true;");
        _writer.Outdent();
        _writer.WriteLine("}");
    }

    private void Report(DiagnosticDescriptor descriptor, Location? location, params object[] arguments) =>
        _reportDiagnostic(DiagnosticSpec.Create(descriptor, location, arguments));

    private void EmitGeneratedBuilder(TypeSpec entry, GeneratedBuilderSpec builder)
    {
        var interfaceName = builder.IsMap
            ? $"global::NbtSharp.INbtMapBuilder<{entry.TypeName}, {entry.ValueTypeName!}>"
            : $"global::NbtSharp.INbtSequenceBuilder<{entry.TypeName}, {entry.ElementTypeName!}>";
        _writer.WriteLine($"private struct {entry.BuilderName} : {interfaceName}");
        _writer.WriteLine("{");
        _writer.Indent();
        _writer.WriteLine($"private {builder.StorageTypeName} __state;");
        _writer.WriteLine();

        if (builder.IsMap)
        {
            _writer.WriteLine("public void Create()");
            _writer.WriteLine("{");
            _writer.Indent();
            foreach (var line in builder.CreateLines)
            {
                _writer.WriteLine(line);
            }

            _writer.Outdent();
            _writer.WriteLine("}");
            _writer.WriteLine();
            _writer.WriteLine(
                $"public bool ContainsKey(string key) => {builder.ContainsKeyExpression!};");
            _writer.WriteLine();
            _writer.WriteLine($"public void Add(string key, {entry.ValueTypeName!} value)");
            _writer.WriteLine("{");
            _writer.Indent();
            foreach (var line in builder.AddLines)
            {
                _writer.WriteLine(line);
            }

            _writer.Outdent();
            _writer.WriteLine("}");
        }
        else
        {
            _writer.WriteLine("public void Create(int count)");
            _writer.WriteLine("{");
            _writer.Indent();
            foreach (var line in builder.CreateLines)
            {
                _writer.WriteLine(line);
            }

            _writer.Outdent();
            _writer.WriteLine("}");
            _writer.WriteLine();
            _writer.WriteLine($"public void Add({entry.ElementTypeName!} item)");
            _writer.WriteLine("{");
            _writer.Indent();
            foreach (var line in builder.AddLines)
            {
                _writer.WriteLine(line);
            }

            _writer.Outdent();
            _writer.WriteLine("}");
        }

        _writer.WriteLine();
        _writer.WriteLine($"public {entry.TypeName} Complete()");
        _writer.WriteLine("{");
        _writer.Indent();
        foreach (var line in builder.CompleteLines)
        {
            _writer.WriteLine(line);
        }

        _writer.Outdent();
        _writer.WriteLine("}");
        _writer.Outdent();
        _writer.WriteLine("}");
    }

    private void EmitEnumConverter(TypeSpec entry)
    {
        var typeName = entry.TypeName;
        var mapping = entry.EnumMapping!;

        _writer.WriteLine(
            $"private sealed class {entry.ConverterName} : " +
            $"global::NbtSharp.NbtConverter<{typeName}>");
        _writer.WriteLine("{");
        _writer.Indent();
        _writer.WriteLine(
            $"public override {typeName} Read(" +
            "ref global::NbtSharp.NbtReader reader, " +
            "global::NbtSharp.NbtSerializerOptions options) =>");
        _writer.Indent();
        _writer.WriteLine(mapping.ReadExpression);
        _writer.Outdent();
        _writer.WriteLine();
        _writer.WriteLine(
            "public override void Write(" +
            "ref global::NbtSharp.NbtWriter writer, " +
            $"{typeName} value, " +
            "global::NbtSharp.NbtSerializerOptions options) =>");
        _writer.Indent();
        _writer.WriteLine(mapping.WriteStatement);
        _writer.Outdent();
        _writer.WriteLine();
        _writer.WriteLine(
            "public override bool TryGetTagType(" +
            $"{typeName} value, " +
            "global::NbtSharp.NbtSerializerOptions options, " +
            "out global::NbtSharp.NbtTagType tagType)");
        _writer.WriteLine("{");
        _writer.Indent();
        _writer.WriteLine($"tagType = global::NbtSharp.NbtTagType.{mapping.Tag};");
        _writer.WriteLine("return true;");
        _writer.Outdent();
        _writer.WriteLine("}");
        _writer.Outdent();
        _writer.WriteLine("}");
    }

    private static EnumMapping GetEnumMapping(string typeName, ITypeSymbol underlying) =>
        underlying.SpecialType switch
        {
            SpecialType.System_SByte => new EnumMapping(
                "Byte",
                $"({typeName})reader.GetSByte();",
                "writer.WriteSByte((sbyte)value);"),
            SpecialType.System_Byte => new EnumMapping(
                "Byte",
                $"({typeName})unchecked((byte)reader.GetSByte());",
                "writer.WriteSByte(unchecked((sbyte)(byte)value));"),
            SpecialType.System_Int16 => new EnumMapping(
                "Short",
                $"({typeName})reader.GetInt16();",
                "writer.WriteInt16((short)value);"),
            SpecialType.System_UInt16 => new EnumMapping(
                "Short",
                $"({typeName})unchecked((ushort)reader.GetInt16());",
                "writer.WriteInt16(unchecked((short)(ushort)value));"),
            SpecialType.System_Int32 => new EnumMapping(
                "Int",
                $"({typeName})reader.GetInt32();",
                "writer.WriteInt32((int)value);"),
            SpecialType.System_UInt32 => new EnumMapping(
                "Int",
                $"({typeName})unchecked((uint)reader.GetInt32());",
                "writer.WriteInt32(unchecked((int)(uint)value));"),
            SpecialType.System_Int64 => new EnumMapping(
                "Long",
                $"({typeName})reader.GetInt64();",
                "writer.WriteInt64((long)value);"),
            SpecialType.System_UInt64 => new EnumMapping(
                "Long",
                $"({typeName})unchecked((ulong)reader.GetInt64());",
                "writer.WriteInt64(unchecked((long)(ulong)value));"),
            _ => throw new InvalidOperationException(
                $"Unsupported enum underlying type {underlying}."),
        };

    private readonly struct EnumMapping
    {
        public EnumMapping(string tag, string readExpression, string writeStatement)
        {
            Tag = tag;
            ReadExpression = readExpression;
            WriteStatement = writeStatement;
        }

        public string Tag { get; }

        public string ReadExpression { get; }

        public string WriteStatement { get; }
    }

    private static ScalarKind GetScalarKind(ITypeSymbol type)
    {
        var kind = GetNonNullableScalarKind(type.SpecialType);
        if (kind != ScalarKind.None)
        {
            return kind;
        }

        if (type is INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Nullable_T, TypeArguments.Length: 1,
            } nullable)
        {
            return GetNonNullableScalarKind(nullable.TypeArguments[0].SpecialType) switch
            {
                ScalarKind.SByte => ScalarKind.NullableSByte,
                ScalarKind.Byte => ScalarKind.NullableByte,
                ScalarKind.Int16 => ScalarKind.NullableInt16,
                ScalarKind.Int32 => ScalarKind.NullableInt32,
                ScalarKind.Int64 => ScalarKind.NullableInt64,
                ScalarKind.UInt16 => ScalarKind.NullableUInt16,
                ScalarKind.UInt32 => ScalarKind.NullableUInt32,
                ScalarKind.UInt64 => ScalarKind.NullableUInt64,
                ScalarKind.Char => ScalarKind.NullableChar,
                ScalarKind.Single => ScalarKind.NullableSingle,
                ScalarKind.Double => ScalarKind.NullableDouble,
                ScalarKind.Boolean => ScalarKind.NullableBoolean,
                _ => ScalarKind.None,
            };
        }

        return ScalarKind.None;
    }

    private static ScalarKind GetNonNullableScalarKind(SpecialType type) => type switch
    {
        SpecialType.System_SByte => ScalarKind.SByte,
        SpecialType.System_Byte => ScalarKind.Byte,
        SpecialType.System_Int16 => ScalarKind.Int16,
        SpecialType.System_Int32 => ScalarKind.Int32,
        SpecialType.System_Int64 => ScalarKind.Int64,
        SpecialType.System_UInt16 => ScalarKind.UInt16,
        SpecialType.System_UInt32 => ScalarKind.UInt32,
        SpecialType.System_UInt64 => ScalarKind.UInt64,
        SpecialType.System_Char => ScalarKind.Char,
        SpecialType.System_Single => ScalarKind.Single,
        SpecialType.System_Double => ScalarKind.Double,
        SpecialType.System_Boolean => ScalarKind.Boolean,
        SpecialType.System_String => ScalarKind.String,
        _ => ScalarKind.None,
    };

    private static bool IsNullableScalar(ScalarKind kind) =>
        kind is >= ScalarKind.NullableSByte and <= ScalarKind.NullableChar;

    private static ScalarKind GetUnderlyingScalarKind(ScalarKind kind) => kind switch
    {
        ScalarKind.NullableSByte => ScalarKind.SByte,
        ScalarKind.NullableByte => ScalarKind.Byte,
        ScalarKind.NullableInt16 => ScalarKind.Int16,
        ScalarKind.NullableInt32 => ScalarKind.Int32,
        ScalarKind.NullableInt64 => ScalarKind.Int64,
        ScalarKind.NullableUInt16 => ScalarKind.UInt16,
        ScalarKind.NullableUInt32 => ScalarKind.UInt32,
        ScalarKind.NullableUInt64 => ScalarKind.UInt64,
        ScalarKind.NullableChar => ScalarKind.Char,
        ScalarKind.NullableSingle => ScalarKind.Single,
        ScalarKind.NullableDouble => ScalarKind.Double,
        ScalarKind.NullableBoolean => ScalarKind.Boolean,
        _ => kind,
    };

    private static string GetReadExpression(ScalarKind kind) =>
        GetUnderlyingScalarKind(kind) switch
        {
            ScalarKind.SByte => "reader.GetSByte()",
            ScalarKind.Byte => "unchecked((byte)reader.GetSByte())",
            ScalarKind.Int16 => "reader.GetInt16()",
            ScalarKind.Int32 => "reader.GetInt32()",
            ScalarKind.Int64 => "reader.GetInt64()",
            ScalarKind.UInt16 => "unchecked((ushort)reader.GetInt16())",
            ScalarKind.UInt32 => "unchecked((uint)reader.GetInt32())",
            ScalarKind.UInt64 => "unchecked((ulong)reader.GetInt64())",
            ScalarKind.Char => "unchecked((char)reader.GetInt16())",
            ScalarKind.Single => "reader.GetSingle()",
            ScalarKind.Double => "reader.GetDouble()",
            ScalarKind.Boolean => $"{MetadataServices}.ReadBoolean(ref reader)",
            ScalarKind.String => "reader.GetString()",
            _ => throw new InvalidOperationException($"Unsupported scalar kind {kind}."),
        };

    // nameExpression is the pre-encoded name field (__name{Index}) for the direct path; slow paths
    // keep the formatted string literal.
    private static string GetWriteStatement(ScalarKind kind, string nameExpression, string value)
    {
        var underlying = GetUnderlyingScalarKind(kind);
        var operand = IsNullableScalar(kind) ? $"{value}.Value" : value;
        return underlying switch
        {
            ScalarKind.SByte => $"writer.WriteSByte({nameExpression}, {operand});",
            ScalarKind.Byte => $"writer.WriteSByte({nameExpression}, unchecked((sbyte){operand}));",
            ScalarKind.Int16 => $"writer.WriteInt16({nameExpression}, {operand});",
            ScalarKind.Int32 => $"writer.WriteInt32({nameExpression}, {operand});",
            ScalarKind.Int64 => $"writer.WriteInt64({nameExpression}, {operand});",
            ScalarKind.UInt16 => $"writer.WriteInt16({nameExpression}, unchecked((short){operand}));",
            ScalarKind.UInt32 => $"writer.WriteInt32({nameExpression}, unchecked((int){operand}));",
            ScalarKind.UInt64 => $"writer.WriteInt64({nameExpression}, unchecked((long){operand}));",
            ScalarKind.Char => $"writer.WriteInt16({nameExpression}, unchecked((short){operand}));",
            ScalarKind.Single => $"writer.WriteSingle({nameExpression}, {operand});",
            ScalarKind.Double => $"writer.WriteDouble({nameExpression}, {operand});",
            ScalarKind.Boolean => $"writer.WriteSByte({nameExpression}, {operand} ? (sbyte)1 : (sbyte)0);",
            ScalarKind.String => $"writer.WriteString({nameExpression}, {operand});",
            _ => throw new InvalidOperationException($"Unsupported scalar kind {kind}."),
        };
    }

    private static bool CanBeNull(ITypeSymbol type) =>
        type.IsReferenceType ||
        type is INamedTypeSymbol
        {
            OriginalDefinition.SpecialType: SpecialType.System_Nullable_T,
        };

    private static string Fq(ITypeSymbol type) => type.ToDisplayString(FullyQualified);

    private static bool IsPubliclyAccessible(ITypeSymbol type) => type switch
    {
        IArrayTypeSymbol array => IsPubliclyAccessible(array.ElementType),
        INamedTypeSymbol named => IsPubliclyAccessible(named),
        _ => false,
    };

    private static bool IsPubliclyAccessible(INamedTypeSymbol type)
    {
        if (type.DeclaredAccessibility != Accessibility.Public)
        {
            return false;
        }

        if (type.ContainingType is not null && !IsPubliclyAccessible(type.ContainingType))
        {
            return false;
        }

        foreach (var argument in type.TypeArguments)
        {
            if (!IsPubliclyAccessible(argument))
            {
                return false;
            }
        }

        return true;
    }

    private static string EscapeIdentifier(string name) =>
        SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None ? name : "@" + name;

    private static string FormatTypeParameterList(ImmutableArray<ITypeParameterSymbol> parameters) =>
        parameters.Length == 0
            ? ""
            : "<" + string.Join(", ", parameters.Select(static parameter => parameter.Name)) + ">";

    private static string FormatTypeArguments(ImmutableArray<ITypeSymbol> arguments) =>
        string.Join(", ", arguments.Select(static argument => Fq(argument)));

    private static string FormatParameters(ImmutableArray<IParameterSymbol> parameters) =>
        string.Join(
            ", ",
            parameters.Select(static parameter =>
                $"{Fq(parameter.Type)} arg{parameter.Ordinal}"));

    private static IEnumerable<string> FormatConstraints(
        ImmutableArray<ITypeParameterSymbol> parameters)
    {
        foreach (var parameter in parameters)
        {
            var parts = new List<string>();
            if (parameter.HasUnmanagedTypeConstraint)
            {
                parts.Add("unmanaged");
            }
            else if (parameter.HasValueTypeConstraint)
            {
                parts.Add("struct");
            }
            else if (parameter.HasReferenceTypeConstraint)
            {
                parts.Add("class");
            }
            else if (parameter.HasNotNullConstraint)
            {
                parts.Add("notnull");
            }

            foreach (var constraint in parameter.ConstraintTypes)
            {
                if (constraint.SpecialType is SpecialType.System_Object or SpecialType.System_ValueType)
                {
                    continue;
                }

                parts.Add(Fq(constraint));
            }

            if (parameter is { HasConstructorConstraint: true, HasValueTypeConstraint: false, HasUnmanagedTypeConstraint: false })
            {
                parts.Add("new()");
            }

            if (parts.Count > 0)
            {
                yield return $"where {parameter.Name} : {string.Join(", ", parts)}";
            }
        }
    }

    private static string CreateDefaultPropertyName(ITypeSymbol type)
    {
        var name = type switch
        {
            IArrayTypeSymbol array => "ArrayOf" + CreateDefaultPropertyName(array.ElementType),
            INamedTypeSymbol { IsGenericType: true } generic =>
                generic.Name + "Of" + string.Join(
                    "And",
                    generic.TypeArguments.Select(CreateDefaultPropertyName)),
            _ => type.Name,
        };

        var builder = new StringBuilder(name.Length);
        foreach (var character in name)
        {
            builder.Append(char.IsLetterOrDigit(character) || character == '_' ? character : '_');
        }

        if (builder.Length == 0)
        {
            return "TypeInfo";
        }

        if (!char.IsLetter(builder[0]) && builder[0] != '_')
        {
            builder.Insert(0, '_');
        }

        var result = builder.ToString();
        return SyntaxFacts.IsValidIdentifier(result) ? result : "_" + result;
    }

    private static string MakeUnique(string name, HashSet<string> used)
    {
        if (!used.Contains(name))
        {
            return name;
        }

        for (var suffix = 2; ; suffix++)
        {
            var candidate = name + "_" + suffix;
            if (!used.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    internal static string GetHintName(INamedTypeSymbol contextType)
    {
        var builder = new StringBuilder();
        foreach (var character in contextType.ToDisplayString())
        {
            builder.Append(char.IsLetterOrDigit(character) || character == '_' ? character : '_');
        }

        builder.Append(".g.cs");
        return builder.ToString();
    }
}

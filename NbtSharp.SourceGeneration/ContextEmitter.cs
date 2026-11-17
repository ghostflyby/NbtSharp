using System.Collections.Immutable;
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
    }

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

        emitter._graph = new TypeGraph(contextType.ContainingAssembly, emitter._reportDiagnostic);
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

        return true;
    }

    private void AssignNames(INamedTypeSymbol contextType)
    {
        var location = _model.Location;
        var used = new HashSet<string>(StringComparer.Ordinal)
        {
            "Default",
            "Options",
            "GetTypeInfo",
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

        _writer.WriteLine($"public static {spec.ContextName} Default {{ get; }} = new();");
        _writer.WriteLine();

        foreach (var entry in spec.Entries)
        {
            EmitTypeInfoProperty(entry);
        }

        EmitGetTypeInfo();

        foreach (var entry in spec.Entries)
        {
            if (entry.Kind == TypeEntryKind.Object && entry.Object is not null)
            {
                _writer.WriteLine();
                EmitObjectConverter(entry, entry.Object);
            }
            else if (entry.Kind == TypeEntryKind.Enum)
            {
                _writer.WriteLine();
                EmitEnumConverter(entry);
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
        TypeEntryKind.Enum => $"new {entry.ConverterName}()",
        TypeEntryKind.Array =>
            $"{MetadataServices}.CreateArrayConverter<{entry.ElementTypeName!}>()",
        TypeEntryKind.List =>
            $"{MetadataServices}.CreateListConverter<{entry.ElementTypeName!}>()",
        TypeEntryKind.ReadOnlyList =>
            $"{MetadataServices}.CreateReadOnlyListConverter<{entry.ElementTypeName!}>()",
        TypeEntryKind.ReadOnlyCollection =>
            $"{MetadataServices}.CreateReadOnlyCollectionConverter<{entry.ElementTypeName!}>()",
        TypeEntryKind.Nullable =>
            $"{MetadataServices}.CreateNullableConverter<{entry.UnderlyingTypeName!}>()",
        TypeEntryKind.Dictionary =>
            $"{MetadataServices}.CreateDictionaryConverter<{entry.ValueTypeName!}>()",
        TypeEntryKind.IDictionary =>
            $"{MetadataServices}.CreateIDictionaryConverter<{entry.ValueTypeName!}>()",
        TypeEntryKind.IReadOnlyDictionary =>
            $"{MetadataServices}.CreateIReadOnlyDictionaryConverter<{entry.ValueTypeName!}>()",
        _ => throw new InvalidOperationException($"Unknown entry kind {entry.Kind}."),
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
        EmitAccessors(model);

        EmitRead(model, typeName);
        _writer.WriteLine();
        EmitWrite(model, typeName);
        _writer.WriteLine();
        EmitTryGetTagType(typeName);

        _writer.Outdent();
        _writer.WriteLine("}");
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

    private void EmitAccessors(ObjectSpec model)
    {
        foreach (var member in model.Members)
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

        if (model.UseConstructorAccessor && model.Constructor is not null)
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

        _writer.WriteLine("var __name = reader.Name;");
        _writer.WriteLine("if (__name is null)");
        _writer.WriteLine("{");
        _writer.Indent();
        _writer.WriteLine(
            "throw global::NbtSharp.NbtMetadataServices.MissingMemberName(reader);");
        _writer.Outdent();
        _writer.WriteLine("}");
        _writer.WriteLine();

        if (hasDirectMembers)
        {
            _writer.WriteLine("try");
            _writer.WriteLine("{");
            _writer.Indent();
        }

        _writer.WriteLine("switch (__name)");
        _writer.WriteLine("{");
        _writer.Indent();
        foreach (var member in model.Members)
        {
            var nbtName = SymbolDisplay.FormatLiteral(member.NbtName, quote: true);
            var path = SymbolDisplay.FormatLiteral("." + member.NbtName, quote: true);
            var scalarKind = member.ScalarKind;
            _writer.WriteLine($"case {nbtName}:");
            _writer.Indent();
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
            _writer.WriteLine("break;");
            _writer.Outdent();
        }

        _writer.WriteLine("default:");
        _writer.Indent();
        _writer.WriteLine("reader.Skip();");
        _writer.WriteLine("break;");
        _writer.Outdent();
        _writer.Outdent();
        _writer.WriteLine("}");

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

        _writer.Outdent();
        _writer.WriteLine("}");
        _writer.WriteLine();
        _writer.WriteLine(
            "throw global::NbtSharp.NbtMetadataServices.CompoundNotTerminated(reader);");

        _writer.Outdent();
        _writer.WriteLine("}");
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

        _writer.WriteLine("return __result;");
    }

    private string BuildConstruction(ObjectSpec model, string typeName)
    {
        if (model.Constructor is null)
        {
            return $"new {typeName}()";
        }

        var parameterMemberIndexes = model.Constructor.ParameterMemberIndexes;
        var arguments = new string[parameterMemberIndexes.Length];
        for (var i = 0; i < parameterMemberIndexes.Length; i++)
        {
            var memberIndex = parameterMemberIndexes[i];
            var memberType = model.Members[memberIndex].TypeName;
            arguments[i] =
                $"__p{memberIndex} ? __v{memberIndex} : default({memberType})!";
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
            var value = $"value.{member.EscapedClrName}";
            if (member.IsRequired && member.CanBeNull)
            {
                _writer.WriteLine($"if ({value} is null)");
                _writer.WriteLine("{");
                _writer.Indent();
                _writer.WriteLine(
                    $"throw {MetadataServices}.NullRequiredMember(writer, {nbtName});");
                _writer.Outdent();
                _writer.WriteLine("}");
            }

            var scalarKind = member.ScalarKind;
            if (scalarKind != ScalarKind.None)
            {
                var path = SymbolDisplay.FormatLiteral("." + member.NbtName, quote: true);
                _writer.WriteLine("if (__direct)");
                _writer.WriteLine("{");
                _writer.Indent();
                _writer.WriteLine($"__path = {path};");
                _writer.WriteLine(GetWriteStatement(scalarKind, nbtName, value));
                _writer.WriteLine("__path = null;");
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

                _writer.WriteLine(GetWriteMemberStatement(member, nbtName, value));
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

        _writer.WriteLine("writer.WriteEndCompound();");
        _writer.Outdent();
        _writer.WriteLine("}");
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

    private static ScalarKind GetScalarKind(ITypeSymbol type) => type.SpecialType switch
    {
        SpecialType.System_SByte => ScalarKind.SByte,
        SpecialType.System_Byte => ScalarKind.Byte,
        SpecialType.System_Int16 => ScalarKind.Int16,
        SpecialType.System_Int32 => ScalarKind.Int32,
        SpecialType.System_Int64 => ScalarKind.Int64,
        SpecialType.System_Single => ScalarKind.Single,
        SpecialType.System_Double => ScalarKind.Double,
        _ => ScalarKind.None,
    };

    private static string GetReadExpression(ScalarKind kind) => kind switch
    {
        ScalarKind.SByte => "reader.GetSByte()",
        ScalarKind.Byte => "unchecked((byte)reader.GetSByte())",
        ScalarKind.Int16 => "reader.GetInt16()",
        ScalarKind.Int32 => "reader.GetInt32()",
        ScalarKind.Int64 => "reader.GetInt64()",
        ScalarKind.Single => "reader.GetSingle()",
        ScalarKind.Double => "reader.GetDouble()",
        _ => throw new InvalidOperationException($"Unsupported scalar kind {kind}."),
    };

    private static string GetWriteStatement(ScalarKind kind, string name, string value) => kind switch
    {
        ScalarKind.SByte => $"writer.WriteSByte({name}, {value});",
        ScalarKind.Byte => $"writer.WriteSByte({name}, unchecked((sbyte){value}));",
        ScalarKind.Int16 => $"writer.WriteInt16({name}, {value});",
        ScalarKind.Int32 => $"writer.WriteInt32({name}, {value});",
        ScalarKind.Int64 => $"writer.WriteInt64({name}, {value});",
        ScalarKind.Single => $"writer.WriteSingle({name}, {value});",
        ScalarKind.Double => $"writer.WriteDouble({name}, {value});",
        _ => throw new InvalidOperationException($"Unsupported scalar kind {kind}."),
    };

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

            if (parameter.HasConstructorConstraint &&
                !parameter.HasValueTypeConstraint &&
                !parameter.HasUnmanagedTypeConstraint)
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

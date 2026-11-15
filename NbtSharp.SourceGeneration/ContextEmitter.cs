using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NbtSharp.SourceGeneration;

internal sealed class ContextEmitter
{
    private const string ContextBaseType = "global::NbtSharp.NbtSerializerContext";
    private const string MetadataServices = "global::NbtSharp.NbtMetadataServices";

    private static readonly SymbolDisplayFormat FullyQualified = SymbolDisplayFormat.FullyQualifiedFormat;

    private readonly Action<Diagnostic> _reportDiagnostic;
    private readonly ContextModel _model;
    private readonly CodeWriter _writer = new();
    private TypeGraph _graph = null!;
    private bool _hasNameErrors;

    public ContextEmitter(ContextModel model, Action<Diagnostic> reportDiagnostic)
    {
        _model = model;
        _reportDiagnostic = reportDiagnostic;
    }

    public string Emit(out string hintName)
    {
        var contextType = _model.ContextType;
        hintName = GetHintName(contextType);
        if (!Validate(contextType))
        {
            return "";
        }

        _graph = new TypeGraph(contextType.ContainingAssembly, _reportDiagnostic);
        foreach (var root in _model.Roots)
        {
            var entry = _graph.AddRoot(root.Type, root.PropertyName, root.Location);
            if (entry is null && !_graph.HasErrors)
            {
                _reportDiagnostic(Diagnostic.Create(
                    Diagnostics.BuiltInRoot,
                    root.Location,
                    root.Type.ToDisplayString(FullyQualified)));
            }
        }

        if (_graph.HasErrors)
        {
            return "";
        }

        AssignNames(contextType);
        if (_hasNameErrors || _graph.HasErrors)
        {
            return "";
        }

        WriteSource(contextType);
        return _writer.ToString();
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
        };

        foreach (var member in contextType.GetMembers())
        {
            if (!member.IsImplicitlyDeclared)
            {
                used.Add(member.Name);
            }
        }

        if (contextType.GetMembers("Default").Any(static member => !member.IsImplicitlyDeclared))
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
            entry.BackingFieldName = $"__typeInfo{index}";
            entry.ConverterName = $"Converter_{index}";

            if (!entry.IsRoot)
            {
                entry.AccessorName = MakeUnique($"TypeInfo{index}", used);
                used.Add(entry.AccessorName);
                continue;
            }

            var explicitName = entry.PublicPropertyName;
            var name = explicitName ?? CreateDefaultPropertyName(entry.Type);
            if (explicitName is not null)
            {
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
            }
            else
            {
                name = MakeUnique(name, used);
            }

            entry.PublicPropertyName = name;
            entry.AccessorName = name;
            used.Add(name);
        }
    }

    private void WriteSource(INamedTypeSymbol contextType)
    {
        _writer.WriteLine("// <auto-generated/>");
        _writer.WriteLine("#nullable enable");
        _writer.WriteLine("#pragma warning disable CS1591");
        _writer.WriteLine();

        var containingNamespace = contextType.ContainingNamespace;
        var hasNamespace = containingNamespace is { IsGlobalNamespace: false };
        if (hasNamespace)
        {
            _writer.WriteLine($"namespace {containingNamespace.ToDisplayString()}");
            _writer.WriteLine("{");
            _writer.Indent();
        }

        var accessibility = contextType.DeclaredAccessibility == Accessibility.Public
            ? "public"
            : "internal";
        var declarationKind = contextType.IsRecord ? "record class" : "class";
        _writer.WriteLine($"{accessibility} partial {declarationKind} {contextType.Name}");
        _writer.WriteLine("{");
        _writer.Indent();

        _writer.WriteLine($"public static {contextType.Name} Default {{ get; }} = new();");
        _writer.WriteLine();

        for (var index = 0; index < _graph.Entries.Count; index++)
        {
            EmitTypeInfoProperty(_graph.Entries[index], index);
        }

        EmitGetTypeInfo();

        foreach (var entry in _graph.Entries)
        {
            if (entry.Kind == TypeEntryKind.Object && entry.Object is not null)
            {
                _writer.WriteLine();
                EmitObjectConverter(entry, entry.Object);
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

    private void EmitTypeInfoProperty(TypeEntry entry, int index)
    {
        var typeName = Fq(entry.Type);
        var visibility = entry.IsRoot ? "public" : "private";
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

        foreach (var entry in _graph.Entries)
        {
            _writer.WriteLine($"if (type == typeof({Fq(entry.Type)}))");
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

    private string GetFactory(TypeEntry entry) => entry.Kind switch
    {
        TypeEntryKind.Object => $"new {entry.ConverterName}()",
        TypeEntryKind.Array =>
            $"{MetadataServices}.CreateArrayConverter<{Fq(entry.ElementType!)}>()",
        TypeEntryKind.List =>
            $"{MetadataServices}.CreateListConverter<{Fq(entry.ElementType!)}>()",
        TypeEntryKind.ReadOnlyList =>
            $"{MetadataServices}.CreateReadOnlyListConverter<{Fq(entry.ElementType!)}>()",
        TypeEntryKind.ReadOnlyCollection =>
            $"{MetadataServices}.CreateReadOnlyCollectionConverter<{Fq(entry.ElementType!)}>()",
        TypeEntryKind.Nullable =>
            $"{MetadataServices}.CreateNullableConverter<{Fq(entry.UnderlyingType!)}>()",
        TypeEntryKind.Dictionary =>
            $"{MetadataServices}.CreateDictionaryConverter<{Fq(entry.ValueType!)}>()",
        TypeEntryKind.IDictionary =>
            $"{MetadataServices}.CreateIDictionaryConverter<{Fq(entry.ValueType!)}>()",
        TypeEntryKind.IReadOnlyDictionary =>
            $"{MetadataServices}.CreateIReadOnlyDictionaryConverter<{Fq(entry.ValueType!)}>()",
        _ => throw new InvalidOperationException($"Unknown entry kind {entry.Kind}."),
    };

    private void EmitObjectConverter(TypeEntry entry, ObjectModel model)
    {
        var typeName = Fq(model.Type);
        _writer.WriteLine(
            "[global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(" +
            "\"Trimming\", \"IL2026\", " +
            "Justification = \"The generated type graph covers every reachable type.\")]");
        _writer.WriteLine(
            "[global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(" +
            "\"AOT\", \"IL3050\", " +
            "Justification = \"The generated type graph covers every reachable type.\")]");
        _writer.WriteLine($"private sealed class {entry.ConverterName} : global::NbtSharp.NbtConverter<{typeName}>");
        _writer.WriteLine("{");
        _writer.Indent();

        EmitAccessors(model);

        EmitRead(model, typeName);
        _writer.WriteLine();
        EmitWrite(model, typeName);
        _writer.WriteLine();
        EmitTryGetTagType(typeName);

        _writer.Outdent();
        _writer.WriteLine("}");
    }

    private void EmitAccessors(ObjectModel model)
    {
        foreach (var member in model.Members)
        {
            if (!member.NeedsSetterAccessor || member.ConstructorParameterIndex >= 0)
            {
                continue;
            }

            if (member.SetterContainingType is INamedTypeSymbol { IsGenericType: true } receiver)
            {
                var definition = receiver.OriginalDefinition;
                var property = (IPropertySymbol)member.Symbol;
                var openMemberType = property.OriginalDefinition.Type;
                var receiverParameter = definition.IsValueType
                    ? $"ref {Fq(definition)} instance"
                    : $"{Fq(definition)} instance";

                _writer.WriteLine(
                    $"private static class Accessors_{member.Index}" +
                    FormatTypeParameterList(definition.TypeParameters));
                foreach (var constraint in FormatConstraints(definition.TypeParameters))
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
                    $"{receiverParameter}, {Fq(openMemberType)} value);");
                _writer.Outdent();
                _writer.WriteLine("}");
                _writer.WriteLine();
                continue;
            }

            var declaringType = Fq(member.SetterContainingType ?? model.Type);
            var directReceiver = member.SetterContainingType?.IsValueType == true
                ? $"ref {declaringType} instance"
                : $"{declaringType} instance";
            _writer.WriteLine(
                "[global::System.Runtime.CompilerServices.UnsafeAccessor(" +
                "global::System.Runtime.CompilerServices.UnsafeAccessorKind.Method, " +
                $"Name = \"set_{member.ClrName}\")]");
            _writer.WriteLine(
                $"private static extern void Set_{member.Index}({directReceiver}, {Fq(member.Type)} value);");
            _writer.WriteLine();
        }

        if (model.UseConstructorAccessor && model.Constructor is not null)
        {
            if (model.Type.IsGenericType)
            {
                var definition = model.Type.OriginalDefinition;
                var constructor = model.Constructor.Symbol.OriginalDefinition;
                var parameterList = FormatParameters(constructor.Parameters);

                _writer.WriteLine(
                    $"private static class AccessorsCtor_0" +
                    FormatTypeParameterList(definition.TypeParameters));
                foreach (var constraint in FormatConstraints(definition.TypeParameters))
                {
                    _writer.WriteLine(constraint);
                }

                _writer.WriteLine("{");
                _writer.Indent();
                _writer.WriteLine(
                    "[global::System.Runtime.CompilerServices.UnsafeAccessor(" +
                    "global::System.Runtime.CompilerServices.UnsafeAccessorKind.Constructor)]");
                _writer.WriteLine(
                    $"internal static extern {Fq(definition)} Create({parameterList});");
                _writer.Outdent();
                _writer.WriteLine("}");
                _writer.WriteLine();
            }
            else
            {
                var parameterList = FormatParameters(model.Constructor.Symbol.Parameters);
                _writer.WriteLine(
                    "[global::System.Runtime.CompilerServices.UnsafeAccessor(" +
                    "global::System.Runtime.CompilerServices.UnsafeAccessorKind.Constructor)]");
                _writer.WriteLine(
                    $"private static extern {Fq(model.Type)} Create_{model.Type.Name}({parameterList});");
                _writer.WriteLine();
            }
        }
    }

    private void EmitRead(ObjectModel model, string typeName)
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
            _writer.WriteLine($"var __v{member.Index} = default({Fq(member.Type)})!;");
            _writer.WriteLine($"var __p{member.Index} = false;");
        }

        var hasDirectMembers = model.Members.Any(
            static member => GetScalarKind(member.Type) != ScalarKind.None);
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
            var scalarKind = GetScalarKind(member.Type);
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

            _writer.WriteLine(
                $"__v{member.Index} = global::NbtSharp.NbtMetadataServices.ReadValue<{Fq(member.Type)}>(" +
                $"ref reader, options, {path});");

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

    private void EmitConstruction(ObjectModel model, string typeName)
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
                if (member.SetterContainingType is INamedTypeSymbol { IsGenericType: true } receiver)
                {
                    var receiverPrefix = receiver.IsValueType ? "ref __result, " : "__result, ";
                    _writer.WriteLine(
                        $"if (__p{member.Index}) Accessors_{member.Index}" +
                        $"<{FormatTypeArguments(receiver.TypeArguments)}>" +
                        $".Set_{member.Index}({receiverPrefix}__v{member.Index});");
                }
                else
                {
                    var receiverPrefix = model.Type.IsValueType ? "ref __result, " : "__result, ";
                    _writer.WriteLine(
                        $"if (__p{member.Index}) Set_{member.Index}({receiverPrefix}__v{member.Index});");
                }
            }
            else
            {
                _writer.WriteLine(
                    $"if (__p{member.Index}) __result.{EscapeIdentifier(member.ClrName)} = __v{member.Index};");
            }
        }

        _writer.WriteLine("return __result;");
    }

    private string BuildConstruction(ObjectModel model, string typeName)
    {
        if (model.Constructor is null)
        {
            return $"new {typeName}()";
        }

        var parameters = model.Constructor.Symbol.Parameters;
        var arguments = new string[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            var memberIndex = model.Constructor.ParameterMemberIndexes[i];
            var memberType = Fq(model.Members[memberIndex].Type);
            arguments[i] =
                $"__p{memberIndex} ? __v{memberIndex} : default({memberType})!";
        }

        var argumentList = string.Join(", ", arguments);
        if (!model.UseConstructorAccessor)
        {
            return $"new {typeName}({argumentList})";
        }

        return model.Type.IsGenericType
            ? $"AccessorsCtor_0<{FormatTypeArguments(model.Type.TypeArguments)}>.Create({argumentList})"
            : $"Create_{model.Type.Name}({argumentList})";
    }

    private void EmitWrite(ObjectModel model, string typeName)
    {
        _writer.WriteLine(
            $"public override void Write(" +
            "ref global::NbtSharp.NbtWriter writer, " +
            $"{typeName} value, " +
            "global::NbtSharp.NbtSerializerOptions options)");
        _writer.WriteLine("{");
        _writer.Indent();
        var hasDirectMembers = model.Members.Any(
            static member => GetScalarKind(member.Type) != ScalarKind.None);
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
            var value = $"value.{EscapeIdentifier(member.ClrName)}";
            var scalarKind = GetScalarKind(member.Type);
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
                _writer.WriteLine(
                    $"{MetadataServices}.WriteMember(ref writer, {nbtName}, {value}, options);");
                _writer.Outdent();
                _writer.WriteLine("}");
            }
            else
            {
                if (hasDirectMembers)
                {
                    _writer.WriteLine("__path = null;");
                }

                _writer.WriteLine(
                    $"{MetadataServices}.WriteMember(ref writer, {nbtName}, {value}, options);");
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
        _reportDiagnostic(Diagnostic.Create(descriptor, location, arguments));

    private enum ScalarKind
    {
        None,
        SByte,
        Byte,
        Int16,
        Int32,
        Int64,
        Single,
        Double,
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

    private static string Fq(ITypeSymbol type) => type.ToDisplayString(FullyQualified);

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

        var builder = new System.Text.StringBuilder(name.Length);
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

    private static string GetHintName(INamedTypeSymbol contextType)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var character in contextType.ToDisplayString())
        {
            builder.Append(char.IsLetterOrDigit(character) || character == '_' ? character : '_');
        }

        builder.Append(".g.cs");
        return builder.ToString();
    }
}

using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace NbtSharp.SourceGeneration;

internal sealed partial class ContextEmitter
{
    /// <summary>
    /// Converts the symbol-based graph into a symbol-free <see cref="ContextSpec"/> and records
    /// graph construction for incrementality tests.
    /// </summary>
    internal static class ContextSpecBuilder
    {
        private static int _graphBuildCount;

        internal static int GraphBuildCount => Volatile.Read(ref _graphBuildCount);

        internal static void RecordGraphBuild() =>
            Interlocked.Increment(ref _graphBuildCount);

        internal static void ResetGraphBuildCountForTests() =>
            Volatile.Write(ref _graphBuildCount, 0);

        internal static ContextSpec Build(
            ContextModel model,
            TypeGraph? graph,
            string hintName,
            ImmutableArray<DiagnosticSpec> diagnostics)
        {
            var contextType = model.ContextType;
            var hasNamespace = contextType.ContainingNamespace is { IsGlobalNamespace: false };
            var entries = ImmutableArray.CreateBuilder<TypeSpec>();
            if (graph is not null)
            {
                foreach (var entry in graph.Entries)
                {
                    entries.Add(BuildTypeSpec(entry));
                }
            }

            var (namingPolicyExpression, nullValueHandlingExpression) =
                GetContextOptions(contextType);
            return new ContextSpec(
                hintName,
                hasNamespace ? contextType.ContainingNamespace.ToDisplayString() : "",
                contextType.Name,
                contextType.DeclaredAccessibility == Accessibility.Public ? "public" : "internal",
                contextType.IsRecord,
                hasSource: graph is not null,
                entries.ToImmutable(),
                namingPolicyExpression,
                nullValueHandlingExpression,
                diagnostics);
        }

        private static (string? NamingPolicy, string? NullValueHandling) GetContextOptions(
            INamedTypeSymbol contextType)
        {
            var attribute = ContextEmitter.GetGenerationOptionsAttribute(contextType);
            if (attribute is null)
            {
                return (null, null);
            }

            string? namingPolicy = null;
            var nullValueHandling = "global::NbtSharp.NbtNullValueHandling.Omit";
            foreach (var argument in attribute.NamedArguments)
            {
                switch (argument.Key)
                {
                    case "NamingPolicy" when argument.Value.Value is int kind:
                        namingPolicy = kind switch
                        {
                            1 => "global::NbtSharp.NbtNamingPolicy.CamelCase",
                            2 => "global::NbtSharp.NbtNamingPolicy.SnakeCaseLower",
                            3 => "global::NbtSharp.NbtNamingPolicy.SnakeCaseUpper",
                            4 => "global::NbtSharp.NbtNamingPolicy.KebabCaseLower",
                            5 => "global::NbtSharp.NbtNamingPolicy.KebabCaseUpper",
                            _ => null,
                        };
                        break;
                    case "NullValueHandling" when argument.Value.Value is int value:
                        nullValueHandling = value == 1
                            ? "global::NbtSharp.NbtNullValueHandling.Throw"
                            : "global::NbtSharp.NbtNullValueHandling.Omit";
                        break;
                }
            }

            return (namingPolicy, nullValueHandling);
        }

        private static TypeSpec BuildTypeSpec(TypeEntry entry)
        {
            var typeName = Fq(entry.Type);
            var spec = new TypeSpec(entry.Kind, typeName, entry.Type.IsValueType)
            {
                AccessorName = entry.AccessorName,
                BackingFieldName = entry.BackingFieldName,
                ConverterName = entry.ConverterName,
                StaticConverterName = entry.StaticConverterName,
                BuilderName = entry.BuilderName,
                GeneratedBuilder = entry.GeneratedBuilder,
                PublicPropertyName = entry.PublicPropertyName,
                IsRoot = entry.IsRoot,
                IsPubliclyAccessible = IsPubliclyAccessible(entry.Type),
                ElementTypeName = entry.ElementType is null ? null : Fq(entry.ElementType),
                CollectionShape = entry.CollectionShape,
                ValueTypeName = entry.ValueType is null ? null : Fq(entry.ValueType),
                MapShape = entry.MapShape,
                UnderlyingTypeName = entry.UnderlyingType is null ? null : Fq(entry.UnderlyingType),
                EnumIsString = entry.EnumIsString,
            };

            // String enums have no numeric mapping; their factory instantiates the runtime
            // NbtStringEnumConverter<TEnum> instead of a generated converter class.
            if (entry.Kind == TypeEntryKind.Enum && !entry.EnumIsString)
            {
                var mapping = GetEnumMapping(typeName, entry.UnderlyingType!);
                spec.EnumMapping = new EnumMappingSpec(
                    mapping.Tag,
                    mapping.ReadExpression,
                    mapping.WriteStatement);
            }

            if (entry.Kind == TypeEntryKind.Polymorphic)
            {
                spec.Polymorphic = new PolymorphicSpec(
                    entry.DerivedTypes
                        .Select(static derivedType =>
                            new PolymorphicDerivedSpec(
                                Fq(derivedType.Type),
                                derivedType.Discriminator))
                        .ToImmutableArray());
            }

            if (entry.Object is not null)
            {
                spec.Object = BuildObjectSpec(entry.Object);
            }

            return spec;
        }

        private static ObjectSpec BuildObjectSpec(ObjectModel model)
        {
            var definition = model.Type.OriginalDefinition;
            var members = model.Members
                .Select(member => BuildMemberSpec(member, model))
                .ToImmutableArray();
            return new ObjectSpec(
                Fq(model.Type),
                model.Type.Name,
                model.Type.IsValueType,
                model.Type.IsGenericType,
                FormatTypeArguments(model.Type.TypeArguments),
                FormatTypeParameterList(definition.TypeParameters),
                FormatConstraints(definition.TypeParameters).ToImmutableArray(),
                members,
                BuildConstructorSpec(model),
                model.UseConstructorAccessor,
                members.Any(static member =>
                    member.ConverterMode == ConverterMode.Runtime ||
                    member is { ConverterMode: ConverterMode.Explicit, ExplicitConverterIsFactory: true }));
        }

        private static ConstructorSpec? BuildConstructorSpec(ObjectModel model)
        {
            if (model.Constructor is null)
            {
                return null;
            }

            var definition = model.Type.OriginalDefinition;
            var constructor = model.Constructor.Symbol.OriginalDefinition;
            return new ConstructorSpec(
                model.Constructor.ParameterMemberIndexes,
                FormatParameters(constructor.Parameters),
                constructor.Parameters
                    .Select(static parameter => FormatParameterDefault(parameter))
                    .ToImmutableArray(),
                FormatTypeParameterList(definition.TypeParameters),
                FormatConstraints(definition.TypeParameters).ToImmutableArray(),
                Fq(model.Type.IsGenericType ? definition : model.Type),
                "Create_" + model.Type.Name);
        }

        private static string? FormatParameterDefault(IParameterSymbol parameter)
        {
            if (!parameter.HasExplicitDefaultValue)
            {
                return null;
            }

            var value = parameter.ExplicitDefaultValue;
            if (value is null)
            {
                return $"default({Fq(parameter.Type)})!";
            }

            var type = parameter.Type;
            if (type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
                type is INamedTypeSymbol { TypeArguments.Length: 1 } nullableType)
            {
                type = nullableType.TypeArguments[0];
            }

            if (type.TypeKind == TypeKind.Enum &&
                type is INamedTypeSymbol { EnumUnderlyingType: { } enumUnderlyingType })
            {
                var enumLiteral = FormatPrimitive(value, enumUnderlyingType.SpecialType);
                return enumLiteral is null
                    ? null
                    : $"({Fq(parameter.Type)})({enumLiteral})";
            }

            return FormatPrimitive(value, type.SpecialType);
        }

        private static string? FormatPrimitive(object value, SpecialType type) => type switch
        {
            SpecialType.System_Boolean => Convert.ToBoolean(value, CultureInfo.InvariantCulture)
                ? "true"
                : "false",
            SpecialType.System_SByte =>
                $"(sbyte){Convert.ToSByte(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)}",
            SpecialType.System_Byte =>
                $"(byte){Convert.ToByte(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)}",
            SpecialType.System_Int16 =>
                $"(short){Convert.ToInt16(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)}",
            SpecialType.System_UInt16 =>
                $"(ushort){Convert.ToUInt16(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)}",
            SpecialType.System_Int32 =>
                Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            SpecialType.System_UInt32 =>
                Convert.ToUInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) + "U",
            SpecialType.System_Int64 =>
                Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) + "L",
            SpecialType.System_UInt64 =>
                Convert.ToUInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) + "UL",
            SpecialType.System_Single => FormatSingle(Convert.ToSingle(value, CultureInfo.InvariantCulture)),
            SpecialType.System_Double => FormatDouble(Convert.ToDouble(value, CultureInfo.InvariantCulture)),
            SpecialType.System_Decimal =>
                Convert.ToDecimal(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) + "M",
            SpecialType.System_String => SymbolDisplay.FormatLiteral((string)value, quote: true),
            SpecialType.System_Char => SymbolDisplay.FormatLiteral((char)value, quote: true),
            _ => null,
        };

        private static string FormatSingle(float value) =>
            float.IsNaN(value)
                ? "global::System.Single.NaN"
                : float.IsPositiveInfinity(value)
                    ? "global::System.Single.PositiveInfinity"
                    : float.IsNegativeInfinity(value)
                        ? "global::System.Single.NegativeInfinity"
                        : value.ToString("R", CultureInfo.InvariantCulture) + "F";

        private static string FormatDouble(double value) =>
            double.IsNaN(value)
                ? "global::System.Double.NaN"
                : double.IsPositiveInfinity(value)
                    ? "global::System.Double.PositiveInfinity"
                    : double.IsNegativeInfinity(value)
                        ? "global::System.Double.NegativeInfinity"
                        : value.ToString("R", CultureInfo.InvariantCulture) + "D";

        private static MemberSpec BuildMemberSpec(MemberModel member, ObjectModel model)
        {
            string setterReceiverTypeName;
            bool setterReceiverIsValueType;
            string setterReceiverTypeParameterList;
            ImmutableArray<string> setterReceiverConstraintLines;
            string setterTypeArguments;
            string setterValueTypeName;
            var setterIsGeneric = member.SetterContainingType is INamedTypeSymbol { IsGenericType: true };

            if (member.SetterContainingType is INamedTypeSymbol { IsGenericType: true } receiver)
            {
                var definition = receiver.OriginalDefinition;
                var property = (IPropertySymbol)member.Symbol;
                setterReceiverTypeName = Fq(definition);
                setterReceiverIsValueType = definition.IsValueType;
                setterReceiverTypeParameterList = FormatTypeParameterList(definition.TypeParameters);
                setterReceiverConstraintLines = FormatConstraints(definition.TypeParameters).ToImmutableArray();
                setterTypeArguments = FormatTypeArguments(receiver.TypeArguments);
                setterValueTypeName = Fq(property.OriginalDefinition.Type);
            }
            else
            {
                setterReceiverTypeName = Fq(member.SetterContainingType ?? model.Type);
                setterReceiverIsValueType = member.SetterContainingType?.IsValueType == true;
                setterReceiverTypeParameterList = "";
                setterReceiverConstraintLines = ImmutableArray<string>.Empty;
                setterTypeArguments = "";
                setterValueTypeName = Fq(member.Type);
            }

            return new MemberSpec(
                member.Index,
                member.ClrName,
                EscapeIdentifier(member.ClrName),
                member.NbtName,
                Fq(member.Type),
                member.IsField,
                member.CanWrite,
                member.IsInitOnly,
                member.NeedsSetterAccessor,
                CanBeNull(member.Type),
                member.ConverterMode == ConverterMode.Generated
                    ? GetScalarKind(member.Type)
                    : ScalarKind.None,
                member.ConstructorParameterIndex,
                member.IsRequired,
                setterIsGeneric,
                setterReceiverTypeName,
                setterReceiverIsValueType,
                setterReceiverTypeParameterList,
                setterReceiverConstraintLines,
                setterTypeArguments,
                setterValueTypeName,
                member.ConverterMode,
                member.ExplicitConverterType is null ? null : Fq(member.ExplicitConverterType),
                member.ExplicitConverterIsFactory);
        }
    }
}

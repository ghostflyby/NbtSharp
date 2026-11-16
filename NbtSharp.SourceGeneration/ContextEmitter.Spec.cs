using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;

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

            return new ContextSpec(
                hintName,
                hasNamespace ? contextType.ContainingNamespace.ToDisplayString() : "",
                contextType.Name,
                contextType.DeclaredAccessibility == Accessibility.Public ? "public" : "internal",
                contextType.IsRecord,
                hasSource: graph is not null,
                entries.ToImmutable(),
                diagnostics);
        }

        private static TypeSpec BuildTypeSpec(TypeEntry entry)
        {
            var typeName = Fq(entry.Type);
            var spec = new TypeSpec(entry.Kind, typeName, entry.Type.IsValueType)
            {
                AccessorName = entry.AccessorName,
                BackingFieldName = entry.BackingFieldName,
                ConverterName = entry.ConverterName,
                PublicPropertyName = entry.PublicPropertyName,
                IsRoot = entry.IsRoot,
                IsPubliclyAccessible = IsPubliclyAccessible(entry.Type),
                ElementTypeName = entry.ElementType is null ? null : Fq(entry.ElementType),
                ValueTypeName = entry.ValueType is null ? null : Fq(entry.ValueType),
                UnderlyingTypeName = entry.UnderlyingType is null ? null : Fq(entry.UnderlyingType),
            };

            if (entry.Kind == TypeEntryKind.Enum)
            {
                var mapping = GetEnumMapping(typeName, entry.UnderlyingType!);
                spec.EnumMapping = new EnumMappingSpec(
                    mapping.Tag,
                    mapping.ReadExpression,
                    mapping.WriteStatement);
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
                FormatTypeParameterList(definition.TypeParameters),
                FormatConstraints(definition.TypeParameters).ToImmutableArray(),
                Fq(model.Type.IsGenericType ? definition : model.Type),
                "Create_" + model.Type.Name);
        }

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

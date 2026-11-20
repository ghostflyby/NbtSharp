using System.Collections;
using System.Diagnostics.CodeAnalysis;

// The public NbtSharp namespace is intentional; the folder groups internal implementation.
// ReSharper disable once CheckNamespace
namespace NbtSharp;

/// <summary>
/// Dispatches a base-typed value to a registered derived type using an envelope compound.
/// </summary>
[RequiresUnreferencedCode("Polymorphic reflection mapping is not trimming-safe.")]
[RequiresDynamicCode("Polymorphic reflection mapping may require runtime code generation.")]
internal sealed class NbtPolymorphicReflectionConverter : NbtConverter
{
    private const string TypeMemberName = "$type";
    private const string ValueMemberName = "$value";

    private readonly Type _baseType;
    private readonly Dictionary<string, Type> _typesByDiscriminator =
        new(StringComparer.Ordinal);
    private readonly Dictionary<Type, string> _discriminatorsByType = new();

    public NbtPolymorphicReflectionConverter(Type baseType)
    {
        ArgumentNullException.ThrowIfNull(baseType);
        if (baseType.IsValueType)
        {
            throw new NotSupportedException(
                $"The polymorphic base type '{baseType}' must be a class or interface.");
        }

        if (!baseType.IsAbstract && !baseType.IsInterface)
        {
            throw new NotSupportedException(
                $"The polymorphic base type '{baseType}' must be abstract or an interface.");
        }

        if (baseType.IsGenericType)
        {
            throw new NotSupportedException(
                $"Generic polymorphic base types are not supported ('{baseType}').");
        }

        _baseType = baseType;
        foreach (var attribute in baseType.GetCustomAttributes(
                     typeof(NbtDerivedAttribute),
                     inherit: false).Cast<NbtDerivedAttribute>())
        {
            var derivedType = attribute.DerivedType;
            if (derivedType == baseType ||
                derivedType.IsAbstract ||
                derivedType.IsInterface ||
                derivedType.ContainsGenericParameters)
            {
                throw new NotSupportedException(
                    $"The [NbtDerived] type '{derivedType}' on '{baseType}' must be a closed " +
                    "concrete class or struct.");
            }

            if (!baseType.IsAssignableFrom(derivedType))
            {
                throw new NotSupportedException(
                    $"The [NbtDerived] type '{derivedType}' is not assignable to '{baseType}'.");
            }

            if (typeof(IEnumerable).IsAssignableFrom(derivedType))
            {
                throw new NotSupportedException(
                    $"The [NbtDerived] type '{derivedType}' on '{baseType}' must not be a " +
                    "collection shape.");
            }

            if (derivedType.GetCustomAttributes(typeof(NbtDerivedAttribute), inherit: false).Length > 0)
            {
                throw new NotSupportedException(
                    $"The [NbtDerived] type '{derivedType}' is itself polymorphic and cannot be " +
                    $"registered on '{baseType}'.");
            }

            var discriminator = attribute.TypeDiscriminator ?? derivedType.Name;
            if (string.IsNullOrEmpty(discriminator))
            {
                throw new NotSupportedException(
                    $"The [NbtDerived] type '{derivedType}' on '{baseType}' has an empty discriminator.");
            }

            if (!_typesByDiscriminator.TryAdd(discriminator, derivedType))
            {
                throw new NotSupportedException(
                    $"The polymorphic discriminator '{discriminator}' is registered more than once " +
                    $"on '{baseType}'.");
            }

            if (!_discriminatorsByType.TryAdd(derivedType, discriminator))
            {
                throw new NotSupportedException(
                    $"The [NbtDerived] type '{derivedType}' is registered more than once on " +
                    $"'{baseType}'.");
            }
        }

        if (_typesByDiscriminator.Count == 0)
        {
            throw new NotSupportedException(
                $"The polymorphic base type '{baseType}' has no [NbtDerived] registrations.");
        }
    }

    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert) => typeToConvert == _baseType;

    internal override object? ReadValue(
        ref NbtReader reader,
        Type typeToConvert,
        NbtSerializerOptions options)
    {
        if (typeToConvert != _baseType)
        {
            throw new InvalidOperationException(
                $"The converter handles {_baseType} but was asked for {typeToConvert}.");
        }

        if (reader.TokenType != NbtTokenType.StartCompound)
        {
            throw NbtMetadataServices.ExpectedTag(reader, NbtTagType.Compound);
        }

        string? discriminator = null;
        object? result = null;
        while (reader.Read())
        {
            if (reader.TokenType == NbtTokenType.EndCompound)
            {
                break;
            }

            var name = reader.Name ??
                throw NbtMetadataServices.MissingMemberName(reader);
            switch (name)
            {
                case TypeMemberName:
                    if (discriminator is not null && options.RejectDuplicateCompoundNames)
                    {
                        throw NbtMetadataServices.DuplicateMember(reader, TypeMemberName);
                    }

                    discriminator = reader.GetString();
                    break;
                case ValueMemberName:
                    if (discriminator is null)
                    {
                        throw NbtMetadataServices.MissingPolymorphicType(reader, _baseType);
                    }

                    if (!_typesByDiscriminator.TryGetValue(discriminator, out var derivedType))
                    {
                        throw NbtMetadataServices.UnknownPolymorphicType(
                            reader,
                            discriminator,
                            _baseType);
                    }

                    if (result is not null && options.RejectDuplicateCompoundNames)
                    {
                        throw NbtMetadataServices.DuplicateMember(reader, ValueMemberName);
                    }

                    result = NbtSerializer.DeserializeValueInternal(
                        ref reader,
                        derivedType,
                        options,
                        pathSegment: null);
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        if (discriminator is null)
        {
            throw NbtMetadataServices.MissingPolymorphicType(reader, _baseType);
        }

        return result ?? throw NbtMetadataServices.MissingPolymorphicValue(reader, _baseType);
    }

    internal override void WriteValue(
        ref NbtWriter writer,
        object? value,
        Type typeToConvert,
        NbtSerializerOptions options)
    {
        if (typeToConvert != _baseType)
        {
            throw new InvalidOperationException(
                $"The converter handles {_baseType} but was asked for {typeToConvert}.");
        }

        var runtimeType = value?.GetType();
        if (runtimeType is null ||
            !_discriminatorsByType.TryGetValue(runtimeType, out var discriminator))
        {
            throw NbtMetadataServices.UnknownPolymorphicRuntimeType(
                writer,
                runtimeType,
                _baseType);
        }

        writer.WriteStartCompound();
        writer.WriteString(TypeMemberName, discriminator);
        NbtSerializer.SerializeMemberInternal(
            ref writer,
            ValueMemberName,
            value,
            runtimeType,
            options);
        writer.WriteEndCompound();
    }

    internal override bool TryGetTagType(
        object? value,
        Type typeToConvert,
        NbtSerializerOptions options,
        out NbtTagType tagType)
    {
        tagType = typeToConvert == _baseType ? NbtTagType.Compound : default;
        return typeToConvert == _baseType;
    }
}

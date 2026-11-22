using System.Diagnostics.CodeAnalysis;
using System.Reflection;

// The public NbtSharp namespace is intentional; the folder groups internal implementation.
// ReSharper disable once CheckNamespace
namespace NbtSharp;

/// <summary>
/// Maps an ordinary class or struct to a compound using reflection.
/// </summary>
[RequiresUnreferencedCode("Reflection-based object mapping is not trimming-safe.")]
[RequiresDynamicCode("Reflection-based object mapping may require runtime code generation.")]
internal sealed class NbtReflectionObjectConverter : NbtConverter
{
    private readonly Type _type;
    private readonly NbtReflectionMember[] _members;
    private readonly NbtReflectionMember? _extensionMember;
    private readonly Dictionary<string, int> _indexByName;
    private readonly ConstructorInfo? _constructor;
    private readonly int[] _constructorMemberIndexes;
    private readonly int[] _settableMemberIndexes;
    private readonly int[] _requiredMemberIndexes;
    private readonly bool[] _isRequiredMember;

    public NbtReflectionObjectConverter(Type type, NbtNamingPolicy? namingPolicy = null)
    {
        _type = type;
        var hiddenMember = FindHiddenMember(type);
        if (hiddenMember is not null)
        {
            throw new NotSupportedException(
                $"The member '{hiddenMember}' on {type} hides a base member with the same name. " +
                "Rename the derived member or exclude one of the members with [NbtIgnore].");
        }

        _members = DiscoverMembers(type, namingPolicy, out _extensionMember);
        _indexByName = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < _members.Length; i++)
        {
            if (!_indexByName.TryAdd(_members[i].Name, i))
            {
                throw new NotSupportedException(
                    $"The type {type} maps more than one member to the NBT name '{_members[i].Name}'.");
            }
        }

        (_constructor, _constructorMemberIndexes) = SelectConstructor(type, _members);
        _requiredMemberIndexes = DiscoverRequiredMemberIndexes(type, _constructor);
        _isRequiredMember = new bool[_members.Length];
        foreach (var requiredIndex in _requiredMemberIndexes)
        {
            _isRequiredMember[requiredIndex] = true;
        }

        var constructorBound = _constructorMemberIndexes.ToHashSet();
        _settableMemberIndexes = Enumerable.Range(0, _members.Length)
            .Where(index => !constructorBound.Contains(index) && _members[index].CanWrite)
            .ToArray();
    }

    public override bool CanConvert(Type typeToConvert) => typeToConvert == _type;

    internal override bool TryGetTagType(
        object? value,
        Type typeToConvert,
        NbtSerializerOptions options,
        out NbtTagType tagType)
    {
        tagType = NbtTagType.Compound;
        return true;
    }

    internal override object ReadValue(ref NbtReader reader, Type typeToConvert, NbtSerializerOptions options)
    {
        if (reader.TokenType != NbtTokenType.StartCompound)
        {
            throw new NbtException(
                $"Expected TAG_Compound but the current token is {reader.TokenType} ({reader.TagType}).")
            {
                BytePosition = reader.BytesConsumed,
                ExpectedTagType = NbtTagType.Compound,
                ActualTagType = reader.TagType,
            };
        }

        var values = new object?[_members.Length];
        var present = new bool[_members.Length];
        Dictionary<string, NbtValue>? extension = null;
        while (reader.Read())
        {
            if (reader.TokenType == NbtTokenType.EndCompound)
            {
                foreach (var requiredIndex in _requiredMemberIndexes)
                {
                    if (!present[requiredIndex])
                    {
                        var requiredName = _members[requiredIndex].Name;
                        throw new NbtException(
                            $"The compound does not contain the required member '{requiredName}'.")
                        {
                            BytePosition = reader.BytesConsumed,
                            Path = "." + requiredName,
                        };
                    }
                }

                var instance = CreateInstance(values, present);
                if (extension is not null)
                {
                    _extensionMember!.SetValue(instance, extension);
                }

                return instance;
            }

            var name = reader.Name;
            if (name is null)
            {
                throw new NbtException("A compound member has no name.")
                {
                    BytePosition = reader.BytesConsumed,
                };
            }

            if (!_indexByName.TryGetValue(name, out var index))
            {
                if (_extensionMember is null)
                {
                    reader.Skip();
                    continue;
                }

                if (extension is not null &&
                    extension.ContainsKey(name) &&
                    options.RejectDuplicateCompoundNames)
                {
                    throw NbtMetadataServices.DuplicateMember(reader, name);
                }

                extension ??= (Dictionary<string, NbtValue>)Activator.CreateInstance(_extensionMember.Type)!;
                extension[name] = NbtMetadataServices.ReadDomValue(ref reader, options, "." + name);
                continue;
            }

            if (present[index] && options.RejectDuplicateCompoundNames)
            {
                throw new NbtException($"The compound contains the duplicate member '{name}'.")
                {
                    BytePosition = reader.BytesConsumed,
                    Path = "." + name,
                };
            }

            try
            {
                var member = _members[index];
                if (member.ExplicitConverter is { } converter)
                {
                    converter = NbtSerializer.ResolveConverterForType(
                        converter,
                        member.Type,
                        options);
                    values[index] = NbtSerializer.DeserializeValueCore(
                        ref reader,
                        member.Type,
                        converter,
                        options,
                        pathSegment: null);
                }
                else
                {
                    values[index] = NbtSerializer.DeserializeValueInternal(
                        ref reader,
                        member.Type,
                        options,
                        pathSegment: null);
                }
            }
            catch (NbtException ex)
            {
                NbtSerializer.PrependPath(ex, "." + name);
                throw;
            }

            present[index] = true;
        }

        throw new NbtException("The compound was not terminated.")
        {
            IsTruncated = true,
            BytePosition = reader.BytesConsumed,
        };
    }

    internal override void WriteValue(ref NbtWriter writer, object? value, Type typeToConvert, NbtSerializerOptions options)
    {
        if (value is null)
        {
            throw new NbtException("A null value cannot be written at this position.");
        }

        writer.WriteStartCompound();
        for (var i = 0; i < _members.Length; i++)
        {
            var member = _members[i];
            var memberValue = member.GetValue(value);
            if (_isRequiredMember[i] && memberValue is null)
            {
                throw new NbtException(
                    $"The required member '{member.Name}' cannot be null.")
                {
                    BytePosition = writer.BytesWritten,
                    Path = "." + member.Name,
                };
            }

            if (member.ExplicitConverter is { } converter)
            {
                NbtSerializer.SerializeMemberWithConverterInternal(
                    ref writer,
                    member.Name,
                    memberValue,
                    member.Type,
                    converter,
                    options);
            }
            else
            {
                NbtSerializer.SerializeMemberInternal(
                    ref writer,
                    member.Name,
                    memberValue,
                    member.Type,
                    options);
            }
        }

        if (_extensionMember is not null)
        {
            var extension = (Dictionary<string, NbtValue>?)_extensionMember.GetValue(value);
            if (extension is not null)
            {
                foreach (var pair in extension)
                {
                    if (_indexByName.ContainsKey(pair.Key))
                    {
                        throw NbtMetadataServices.ExtensionMemberConflict(writer, pair.Key);
                    }

                    NbtMetadataServices.WriteDomMember(ref writer, pair.Key, pair.Value, options);
                }
            }
        }

        writer.WriteEndCompound();
    }

    private object CreateInstance(object?[] values, bool[] present)
    {
        object instance;
        if (_constructor is null)
        {
            instance = Activator.CreateInstance(_type)!;
        }
        else if (_constructorMemberIndexes.Length == 0)
        {
            instance = _constructor.Invoke(null);
        }
        else
        {
            var parameters = _constructor.GetParameters();
            var arguments = new object?[_constructorMemberIndexes.Length];
            for (var i = 0; i < arguments.Length; i++)
            {
                var memberIndex = _constructorMemberIndexes[i];
                arguments[i] = present[memberIndex]
                    ? values[memberIndex]
                    : GetParameterDefault(parameters[i], _members[memberIndex].Type);
            }

            instance = _constructor.Invoke(arguments);
        }

        foreach (var index in _settableMemberIndexes)
        {
            if (present[index])
            {
                _members[index].SetValue(instance, values[index]);
            }
        }

        return instance;
    }

    private static object? GetParameterDefault(ParameterInfo parameter, Type memberType)
    {
        if (!parameter.HasDefaultValue)
        {
            return GetDefault(memberType);
        }

        var value = parameter.DefaultValue;
        if (value is null || value is DBNull || ReferenceEquals(value, Missing.Value))
        {
            return GetDefault(memberType);
        }

        var targetType = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;
        return targetType.IsEnum && !value.GetType().IsEnum
            ? Enum.ToObject(targetType, value)
            : value;
    }

    private static object? GetDefault(Type type) => type.IsValueType ? Activator.CreateInstance(type) : null;

    private int[] DiscoverRequiredMemberIndexes(Type type, ConstructorInfo? constructor)
    {
        var mappedIndexes = new HashSet<int>();
        List<MemberInfo>? unmapped = null;
        foreach (var requiredMember in DiscoverRequiredMembers(type))
        {
            var index = Array.FindIndex(_members, member => member.Matches(requiredMember));
            if (index >= 0)
            {
                mappedIndexes.Add(index);
            }
            else
            {
                unmapped ??= [];
                unmapped.Add(requiredMember);
            }
        }

        if (unmapped is not null &&
            (constructor is null || !SetsRequiredMembers(constructor)))
        {
            throw new NotSupportedException(
                $"The required member '{unmapped[0].Name}' on {type} is not serialized and the " +
                "selected constructor is not marked with SetsRequiredMembers.");
        }

        return Enumerable.Range(0, _members.Length)
            .Where(mappedIndexes.Contains)
            .ToArray();
    }

    private static List<MemberInfo> DiscoverRequiredMembers(Type type)
    {
        const BindingFlags flags =
            BindingFlags.Public |
            BindingFlags.NonPublic |
            BindingFlags.Instance |
            BindingFlags.DeclaredOnly;

        var required = new List<MemberInfo>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var property in current.GetProperties(flags))
            {
                if (!property.IsDefined(
                        typeof(System.Runtime.CompilerServices.RequiredMemberAttribute),
                        inherit: false))
                {
                    continue;
                }

                var key = "P:" + property.Name + ":" + property.PropertyType;
                if (seen.Add(key))
                {
                    required.Add(property);
                }
            }

            foreach (var field in current.GetFields(flags))
            {
                if (!field.IsDefined(
                        typeof(System.Runtime.CompilerServices.RequiredMemberAttribute),
                        inherit: false))
                {
                    continue;
                }

                var key = "F:" + field.Name + ":" + field.FieldType;
                if (seen.Add(key))
                {
                    required.Add(field);
                }
            }
        }

        return required;
    }

    private static bool SetsRequiredMembers(ConstructorInfo constructor) =>
        constructor.IsDefined(typeof(SetsRequiredMembersAttribute), inherit: false);

    private static string? FindHiddenMember(Type type)
    {
        var declaredTypes = new List<Type>();
        for (var current = type; current is not null; current = current.BaseType)
        {
            declaredTypes.Add(current);
        }

        declaredTypes.Reverse();

        var mappedProperties = new Dictionary<MethodInfo, (PropertyInfo Property, int TypeIndex)>();
        var mappedFields = new List<(string Name, int TypeIndex)>();
        for (var i = 0; i < declaredTypes.Count; i++)
        {
            foreach (var property in declaredTypes[i].GetProperties(
                         BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var getter = property.GetMethod;
                if (getter is null ||
                    !getter.IsPublic ||
                    property.GetIndexParameters().Length != 0)
                {
                    continue;
                }

                if (property.GetCustomAttribute<NbtIgnoreAttribute>(inherit: true) is not null)
                {
                    mappedProperties.Remove(getter.GetBaseDefinition());
                    continue;
                }

                mappedProperties[getter.GetBaseDefinition()] = (property, i);
            }

            foreach (var field in declaredTypes[i].GetFields(
                         BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (field.IsStatic ||
                    field.IsLiteral ||
                    field.IsInitOnly ||
                    !field.IsDefined(typeof(NbtIncludeAttribute), inherit: true) ||
                    field.IsDefined(typeof(NbtIgnoreAttribute), inherit: true))
                {
                    continue;
                }

                mappedFields.Add((field.Name, i));
            }
        }

        var mapped = mappedProperties.Values
            .Select(static entry => (entry.Property.Name, entry.TypeIndex, (PropertyInfo?)entry.Property))
            .Concat(mappedFields.Select(static entry => (entry.Name, entry.TypeIndex, (PropertyInfo?)null)))
            .ToArray();

        foreach (var (name, typeIndex, property) in mapped)
        {
            for (var i = typeIndex + 1; i < declaredTypes.Count; i++)
            {
                foreach (var candidate in declaredTypes[i].GetMembers(
                             BindingFlags.Public | BindingFlags.NonPublic |
                             BindingFlags.Instance | BindingFlags.Static |
                             BindingFlags.DeclaredOnly))
                {
                    if (candidate.Name != name)
                    {
                        continue;
                    }

                    if (property?.GetMethod is { } mappedGetter &&
                        candidate is PropertyInfo candidateProperty &&
                        candidateProperty.GetMethod is { } candidateGetter &&
                        candidateGetter.GetBaseDefinition() == mappedGetter.GetBaseDefinition())
                    {
                        continue;
                    }

                    return name;
                }
            }
        }

        return null;
    }

    private static NbtReflectionMember[] DiscoverMembers(
        Type type,
        NbtNamingPolicy? namingPolicy,
        out NbtReflectionMember? extensionMember)
    {
        var members = new List<(int Token, NbtReflectionMember Member)>();
        extensionMember = null;

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetMethod is not { IsPublic: true } ||
                property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            if (property.GetCustomAttribute<NbtIgnoreAttribute>(inherit: true) is not null)
            {
                continue;
            }

            if (property.GetCustomAttribute<NbtExtensionDataAttribute>(inherit: true) is not null)
            {
                extensionMember = ClaimExtensionMember(extensionMember, property);
                continue;
            }

            var name = property.GetCustomAttribute<NbtPropertyNameAttribute>(inherit: true)?.Name ??
                       namingPolicy?.ConvertName(property.Name) ??
                       property.Name;
            var useConverter = property.GetCustomAttribute<NbtUseConverterAttribute>(inherit: true);
            var explicitConverter = useConverter is null
                ? null
                : ResolveMemberConverter(property.PropertyType, useConverter, property.Name);
            members.Add((
                property.MetadataToken,
                new NbtReflectionMember(name, property, explicitConverter)));
        }

        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (field.IsStatic || field.IsLiteral || field.IsInitOnly)
            {
                continue;
            }

            if (field.GetCustomAttribute<NbtIgnoreAttribute>(inherit: true) is not null)
            {
                continue;
            }

            if (field.GetCustomAttribute<NbtExtensionDataAttribute>(inherit: true) is not null)
            {
                if (field.GetCustomAttribute<NbtIncludeAttribute>(inherit: true) is null)
                {
                    throw new NotSupportedException(
                        $"The [NbtExtensionData] declaration on member '{field.Name}' is invalid: " +
                        "fields require [NbtInclude] in addition to [NbtExtensionData].");
                }

                extensionMember = ClaimExtensionMember(extensionMember, field);
                continue;
            }

            var useConverter = field.GetCustomAttribute<NbtUseConverterAttribute>(inherit: true);
            if (field.GetCustomAttribute<NbtIncludeAttribute>(inherit: true) is null)
            {
                if (useConverter is not null)
                {
                    throw new NotSupportedException(
                        $"The [NbtUseConverter] declaration on member '{field.Name}' is invalid: " +
                        "fields require [NbtInclude] in addition to [NbtUseConverter].");
                }

                continue;
            }

            var name = field.GetCustomAttribute<NbtPropertyNameAttribute>(inherit: true)?.Name ??
                       namingPolicy?.ConvertName(field.Name) ??
                       field.Name;
            var explicitConverter = useConverter is null
                ? null
                : ResolveMemberConverter(field.FieldType, useConverter, field.Name);
            members.Add((
                field.MetadataToken,
                new NbtReflectionMember(name, field, explicitConverter)));
        }

        return members.OrderBy(static member => member.Token).Select(static member => member.Member).ToArray();
    }

    private static NbtReflectionMember ClaimExtensionMember(
        NbtReflectionMember? existing,
        PropertyInfo property)
    {
        ValidateExtensionMember(
            existing,
            property.Name,
            property.PropertyType,
            canWrite: property.CanWrite,
            isRequired: property.IsDefined(
                typeof(System.Runtime.CompilerServices.RequiredMemberAttribute),
                inherit: false),
            hasPropertyName: property.GetCustomAttribute<NbtPropertyNameAttribute>(inherit: true) is not null,
            hasUseConverter: property.GetCustomAttribute<NbtUseConverterAttribute>(inherit: true) is not null);
        return new NbtReflectionMember(property.Name, property);
    }

    private static NbtReflectionMember ClaimExtensionMember(
        NbtReflectionMember? existing,
        FieldInfo field)
    {
        ValidateExtensionMember(
            existing,
            field.Name,
            field.FieldType,
            canWrite: !field.IsInitOnly && !field.IsLiteral,
            isRequired: field.IsDefined(
                typeof(System.Runtime.CompilerServices.RequiredMemberAttribute),
                inherit: false),
            hasPropertyName: field.GetCustomAttribute<NbtPropertyNameAttribute>(inherit: true) is not null,
            hasUseConverter: field.GetCustomAttribute<NbtUseConverterAttribute>(inherit: true) is not null);
        return new NbtReflectionMember(field.Name, field);
    }

    private static void ValidateExtensionMember(
        NbtReflectionMember? existing,
        string memberName,
        Type memberType,
        bool canWrite,
        bool isRequired,
        bool hasPropertyName,
        bool hasUseConverter)
    {
        if (existing is not null)
        {
            throw new NotSupportedException(
                $"The [NbtExtensionData] declaration on member '{memberName}' is invalid: " +
                "the type maps more than one extension member.");
        }

        if (memberType != typeof(NbtCompound) &&
            !(memberType.IsGenericType &&
              memberType.GetGenericTypeDefinition() == typeof(Dictionary<,>) &&
              memberType.GetGenericArguments()[0] == typeof(string) &&
              memberType.GetGenericArguments()[1] == typeof(NbtValue)))
        {
            throw new NotSupportedException(
                $"The [NbtExtensionData] declaration on member '{memberName}' is invalid: " +
                "the member type must be NbtSharp.NbtCompound or Dictionary<string, NbtSharp.NbtValue>.");
        }

        if (hasPropertyName || hasUseConverter)
        {
            throw new NotSupportedException(
                $"The [NbtExtensionData] declaration on member '{memberName}' is invalid: " +
                "it cannot be combined with [NbtPropertyName] or [NbtUseConverter].");
        }

        if (!canWrite)
        {
            throw new NotSupportedException(
                $"The [NbtExtensionData] declaration on member '{memberName}' is invalid: " +
                "the member must have a setter.");
        }

        if (isRequired)
        {
            throw new NotSupportedException(
                $"The [NbtExtensionData] declaration on member '{memberName}' is invalid: " +
                "required members are not supported.");
        }
    }

    private static NbtConverter? ResolveMemberConverter(
        Type memberType,
        NbtUseConverterAttribute attribute,
        string memberName)
    {
        if (memberType == typeof(void) ||
            memberType.IsPointer ||
            memberType.IsFunctionPointer ||
            memberType.IsByRefLike)
        {
            throw new NotSupportedException(
                $"The [NbtUseConverter] declaration on member '{memberName}' is invalid: " +
                "the member type cannot be used as a generic argument.");
        }

        var converterType = attribute.ConverterType;
        if (converterType is null)
        {
            return null;
        }

        if (converterType.IsGenericTypeDefinition || converterType.ContainsGenericParameters)
        {
            return Invalid("the converter type must be a closed generic type");
        }

        if (converterType.IsAbstract)
        {
            return Invalid("the converter type must not be abstract");
        }

        if (!typeof(NbtConverter).IsAssignableFrom(converterType))
        {
            return Invalid("the converter type must derive from NbtSharp.NbtConverter");
        }

        var constructor = converterType.GetConstructor(Type.EmptyTypes);
        if (constructor is null || !constructor.IsPublic)
        {
            return Invalid("the converter type must have a public parameterless constructor");
        }

        if (!typeof(NbtConverterFactory).IsAssignableFrom(converterType))
        {
            var handledType = FindConverterType(converterType);
            if (handledType is null)
            {
                return Invalid(
                    "the converter type must derive from NbtConverter<T> or NbtConverterFactory");
            }

            if (handledType != memberType)
            {
                return Invalid(
                    $"the converter handles '{handledType}' but the member type is '{memberType}'");
            }
        }

        return (NbtConverter)constructor.Invoke(null);

        NbtConverter Invalid(string reason) =>
            throw new NotSupportedException(
                $"The [NbtUseConverter] declaration on member '{memberName}' is invalid: {reason}.");
    }

    private static Type? FindConverterType(Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType &&
                current.GetGenericTypeDefinition() == typeof(NbtConverter<>))
            {
                return current.GetGenericArguments()[0];
            }
        }

        return null;
    }

    private static (ConstructorInfo? Constructor, int[] MemberIndexes) SelectConstructor(
        Type type,
        NbtReflectionMember[] members)
    {
        var constructors = type
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Where(static constructor =>
                constructor.GetParameters().All(static parameter =>
                    !parameter.ParameterType.IsByRef))
            .ToArray();
        var parameterless = constructors.FirstOrDefault(static constructor => constructor.GetParameters().Length == 0);
        if (parameterless is not null)
        {
            return (parameterless, []);
        }

        ConstructorInfo? bestConstructor = null;
        int[] bestIndexes = [];
        foreach (var constructor in constructors)
        {
            var parameters = constructor.GetParameters();
            var indexes = new int[parameters.Length];
            var matches = true;
            for (var i = 0; i < parameters.Length; i++)
            {
                var parameter = parameters[i];
                var index = Array.FindIndex(
                    members,
                    member => member.ClrName.Equals(parameter.Name, StringComparison.OrdinalIgnoreCase) &&
                              member.Type == parameter.ParameterType);
                if (index < 0)
                {
                    matches = false;
                    break;
                }

                indexes[i] = index;
            }

            if (matches && indexes.Length > bestIndexes.Length)
            {
                bestConstructor = constructor;
                bestIndexes = indexes;
            }
        }

        if (bestConstructor is not null)
        {
            return (bestConstructor, bestIndexes);
        }

        if (type.IsValueType)
        {
            return (null, []);
        }

        throw new NotSupportedException(
            $"The type {type} needs a public parameterless constructor or a public constructor whose " +
            "parameters match its NBT members.");
    }
}

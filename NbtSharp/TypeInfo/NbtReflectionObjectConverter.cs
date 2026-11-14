using System.Diagnostics.CodeAnalysis;
using System.Reflection;

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
    private readonly Dictionary<string, int> _indexByName;
    private readonly ConstructorInfo? _constructor;
    private readonly int[] _constructorMemberIndexes;
    private readonly int[] _settableMemberIndexes;

    public NbtReflectionObjectConverter(Type type)
    {
        _type = type;
        _members = DiscoverMembers(type);
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
        while (reader.Read())
        {
            if (reader.TokenType == NbtTokenType.EndCompound)
            {
                return CreateInstance(values, present);
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
                reader.Skip();
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

            values[index] = NbtSerializer.DeserializeValueInternal(
                ref reader,
                _members[index].Type,
                options,
                "." + name);
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
        foreach (var member in _members)
        {
            NbtSerializer.SerializeMemberInternal(
                ref writer,
                member.Name,
                member.GetValue(value),
                member.Type,
                options);
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
            var arguments = new object?[_constructorMemberIndexes.Length];
            for (var i = 0; i < arguments.Length; i++)
            {
                var memberIndex = _constructorMemberIndexes[i];
                arguments[i] = present[memberIndex]
                    ? values[memberIndex]
                    : GetDefault(_members[memberIndex].Type);
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

    private static object? GetDefault(Type type) => type.IsValueType ? Activator.CreateInstance(type) : null;

    private static NbtReflectionMember[] DiscoverMembers(Type type)
    {
        var members = new List<(int Token, NbtReflectionMember Member)>();

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetMethod is null || property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            if (property.IsDefined(typeof(NbtIgnoreAttribute), inherit: true))
            {
                continue;
            }

            var name = property.GetCustomAttribute<NbtPropertyNameAttribute>(inherit: true)?.Name ?? property.Name;
            members.Add((property.MetadataToken, new NbtReflectionMember(name, property)));
        }

        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (field.IsStatic || field.IsLiteral || field.IsInitOnly)
            {
                continue;
            }

            if (!field.IsDefined(typeof(NbtIncludeAttribute), inherit: true) ||
                field.IsDefined(typeof(NbtIgnoreAttribute), inherit: true))
            {
                continue;
            }

            var name = field.GetCustomAttribute<NbtPropertyNameAttribute>(inherit: true)?.Name ?? field.Name;
            members.Add((field.MetadataToken, new NbtReflectionMember(name, field)));
        }

        return members.OrderBy(static member => member.Token).Select(static member => member.Member).ToArray();
    }

    private static (ConstructorInfo? Constructor, int[] MemberIndexes) SelectConstructor(
        Type type,
        NbtReflectionMember[] members)
    {
        var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
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

using System.Diagnostics.CodeAnalysis;
using NbtSharp.Converters;

namespace NbtSharp;

internal static class NbtBuiltInConverters
{
    private static readonly Dictionary<Type, NbtConverter> ExactConverters = new()
    {
        [typeof(sbyte)] = NbtPrimitiveConverters.SByte,
        [typeof(byte)] = NbtPrimitiveConverters.Byte,
        [typeof(short)] = NbtPrimitiveConverters.Int16,
        [typeof(int)] = NbtPrimitiveConverters.Int32,
        [typeof(long)] = NbtPrimitiveConverters.Int64,
        [typeof(float)] = NbtPrimitiveConverters.Single,
        [typeof(double)] = NbtPrimitiveConverters.Double,
        [typeof(string)] = NbtPrimitiveConverters.String,
        [typeof(bool)] = NbtPrimitiveConverters.Boolean,
        [typeof(byte[])] = NbtArrayConverters.ByteArray,
        [typeof(sbyte[])] = NbtArrayConverters.SByteArray,
        [typeof(int[])] = NbtArrayConverters.Int32Array,
        [typeof(long[])] = NbtArrayConverters.Int64Array,
        [typeof(NbtValue)] = NbtDomConverters.Value,
        [typeof(NbtList)] = NbtDomConverters.List,
        [typeof(NbtCompound)] = NbtDomConverters.Compound,
    };

    [RequiresUnreferencedCode("Building closed collection converters from runtime types is not trimming-safe.")]
    [RequiresDynamicCode("Building closed collection converters from runtime types requires runtime code generation.")]
    public static bool TryCreate(Type type, out NbtConverter converter)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (ExactConverters.TryGetValue(type, out converter!))
        {
            return true;
        }

        if (type.IsArray)
        {
            if (type.GetArrayRank() != 1)
            {
                converter = null!;
                return false;
            }

            var elementType = type.GetElementType()!;
            if (elementType.IsPointer || elementType.IsByRef || elementType.IsByRefLike)
            {
                converter = null!;
                return false;
            }

            converter = CreateGeneric(typeof(NbtArrayConverter<>), elementType);
            return true;
        }

        if (!type.IsGenericType || type.IsGenericTypeDefinition)
        {
            converter = null!;
            return false;
        }

        var definition = type.GetGenericTypeDefinition();
        var arguments = type.GetGenericArguments();
        if (definition == typeof(Nullable<>))
        {
            converter = CreateGeneric(typeof(NbtNullableConverter<>), arguments[0]);
            return true;
        }

        if (definition == typeof(List<>) ||
            definition == typeof(IReadOnlyList<>) ||
            definition == typeof(IReadOnlyCollection<>))
        {
            converter = CreateGeneric(typeof(NbtReadOnlyCollectionConverter<,>), type, arguments[0]);
            return true;
        }

        if ((definition == typeof(Dictionary<,>) ||
             definition == typeof(IDictionary<,>) ||
             definition == typeof(IReadOnlyDictionary<,>)) &&
            arguments[0] == typeof(string))
        {
            converter = CreateGeneric(typeof(NbtDictionaryConverter<,>), type, arguments[1]);
            return true;
        }

        converter = null!;
        return false;
    }

    [RequiresUnreferencedCode("Building closed collection converters from runtime types is not trimming-safe.")]
    [RequiresDynamicCode("Building closed collection converters from runtime types requires runtime code generation.")]
    private static NbtConverter CreateGeneric(Type definition, params Type[] arguments) =>
        (NbtConverter)Activator.CreateInstance(definition.MakeGenericType(arguments))!;
}

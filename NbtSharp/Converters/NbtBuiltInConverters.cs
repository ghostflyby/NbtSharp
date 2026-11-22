using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using NbtSharp.Converters;

// The public NbtSharp namespace is intentional; the folder groups internal implementation.
// ReSharper disable once CheckNamespace
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
        [typeof(ushort)] = NbtPrimitiveConverters.UInt16,
        [typeof(uint)] = NbtPrimitiveConverters.UInt32,
        [typeof(ulong)] = NbtPrimitiveConverters.UInt64,
        [typeof(char)] = NbtPrimitiveConverters.Char,
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

        if (type.IsEnum)
        {
            converter = CreateGeneric(
                type.IsDefined(typeof(NbtStringEnumAttribute), inherit: false)
                    ? typeof(NbtStringEnumConverter<>)
                    : typeof(NbtEnumConverter<>),
                type);
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
            if (TryCreateReflectionFallback(type, out converter))
            {
                return true;
            }

            converter = null!;
            return false;
        }

        var definition = type.GetGenericTypeDefinition();
        var arguments = type.GetGenericArguments();
        if (definition == typeof(PriorityQueue<,>))
        {
            throw new NotSupportedException(
                "PriorityQueue<TElement, TPriority> cannot be represented as an NBT list or " +
                "compound because it exposes element/priority pairs rather than a sequence. " +
                "Register a custom converter to define its NBT representation.");
        }

        if (definition == typeof(Nullable<>))
        {
            converter = CreateGeneric(typeof(NbtNullableConverter<>), arguments[0]);
            return true;
        }

        if (IsListBacked(definition))
        {
            converter = CreateGeneric(
                typeof(NbtListBackedCollectionConverter<,>),
                type,
                arguments[0]);
            return true;
        }

        if (IsSetBacked(definition))
        {
            converter = CreateSequence(
                type,
                arguments[0],
                MakeGeneric(typeof(NbtSetBuilder<,>), type, arguments[0]));
            return true;
        }

        if (definition == typeof(Queue<>))
        {
            converter = CreateSequence(
                type,
                arguments[0],
                MakeGeneric(typeof(NbtQueueBuilder<>), arguments[0]));
            return true;
        }

        if (definition == typeof(Stack<>))
        {
            converter = CreateSequence(
                type,
                arguments[0],
                MakeGeneric(typeof(NbtStackBuilder<>), arguments[0]));
            return true;
        }

        if (definition == typeof(LinkedList<>))
        {
            converter = CreateSequence(
                type,
                arguments[0],
                MakeGeneric(typeof(NbtLinkedListBuilder<>), arguments[0]));
            return true;
        }

        if (definition == typeof(Collection<>))
        {
            converter = CreateSequence(
                type,
                arguments[0],
                MakeGeneric(typeof(NbtCollectionBuilder<>), arguments[0]));
            return true;
        }

        if (definition == typeof(ObservableCollection<>))
        {
            converter = CreateSequence(
                type,
                arguments[0],
                MakeGeneric(typeof(NbtObservableCollectionBuilder<>), arguments[0]));
            return true;
        }

        if (definition == typeof(ReadOnlyCollection<>))
        {
            converter = CreateSequence(
                type,
                arguments[0],
                MakeGeneric(typeof(NbtReadOnlyCollectionBuilder<>), arguments[0]));
            return true;
        }

        if (definition == typeof(ReadOnlyObservableCollection<>))
        {
            converter = CreateSequence(
                type,
                arguments[0],
                MakeGeneric(typeof(NbtReadOnlyObservableCollectionBuilder<>), arguments[0]));
            return true;
        }

        if (definition == typeof(ImmutableArray<>))
        {
            converter = CreateGeneric(typeof(NbtImmutableArrayConverter<>), arguments[0]);
            return true;
        }

        if (definition == typeof(ImmutableList<>) || definition == typeof(IImmutableList<>))
        {
            converter = CreateSequence(
                type,
                arguments[0],
                MakeGeneric(typeof(NbtImmutableListBuilder<,>), type, arguments[0]));
            return true;
        }

        if (definition == typeof(ImmutableHashSet<>) || definition == typeof(IImmutableSet<>))
        {
            converter = CreateSequence(
                type,
                arguments[0],
                MakeGeneric(typeof(NbtImmutableHashSetBuilder<,>), type, arguments[0]));
            return true;
        }

        if (definition == typeof(FrozenSet<>))
        {
            converter = CreateSequence(
                type,
                arguments[0],
                MakeGeneric(typeof(NbtFrozenSetBuilder<>), arguments[0]));
            return true;
        }

        if (definition == typeof(ConcurrentBag<>))
        {
            converter = CreateSequence(
                type,
                arguments[0],
                MakeGeneric(typeof(NbtConcurrentBagBuilder<>), arguments[0]));
            return true;
        }

        if (definition == typeof(ConcurrentQueue<>))
        {
            converter = CreateSequence(
                type,
                arguments[0],
                MakeGeneric(typeof(NbtConcurrentQueueBuilder<>), arguments[0]));
            return true;
        }

        if (definition == typeof(ConcurrentStack<>))
        {
            converter = CreateSequence(
                type,
                arguments[0],
                MakeGeneric(typeof(NbtConcurrentStackBuilder<>), arguments[0]));
            return true;
        }

        if (definition == typeof(SortedSet<>))
        {
            converter = CreateSequence(
                type,
                arguments[0],
                MakeGeneric(typeof(NbtSortedSetBuilder<>), arguments[0]));
            return true;
        }

        if (definition == typeof(BlockingCollection<>))
        {
            converter = CreateSequence(
                type,
                arguments[0],
                MakeGeneric(typeof(NbtBlockingCollectionBuilder<>), arguments[0]));
            return true;
        }

        if (definition == typeof(ImmutableQueue<>) || definition == typeof(IImmutableQueue<>))
        {
            converter = CreateSequence(
                type,
                arguments[0],
                MakeGeneric(typeof(NbtImmutableQueueBuilder<,>), type, arguments[0]));
            return true;
        }

        if (definition == typeof(ImmutableStack<>) || definition == typeof(IImmutableStack<>))
        {
            converter = CreateSequence(
                type,
                arguments[0],
                MakeGeneric(typeof(NbtImmutableStackBuilder<,>), type, arguments[0]));
            return true;
        }

        if (definition == typeof(ImmutableSortedSet<>))
        {
            converter = CreateSequence(
                type,
                arguments[0],
                MakeGeneric(typeof(NbtImmutableSortedSetBuilder<>), arguments[0]));
            return true;
        }

        if (IsDictionaryBacked(definition) && arguments[0] == typeof(string))
        {
            converter = CreateMap(
                type,
                arguments[1],
                MakeGeneric(typeof(NbtDictionaryBuilder<,>), type, arguments[1]));
            return true;
        }

        if (definition == typeof(ReadOnlyDictionary<,>) && arguments[0] == typeof(string))
        {
            converter = CreateMap(
                type,
                arguments[1],
                MakeGeneric(typeof(NbtReadOnlyDictionaryBuilder<>), arguments[1]));
            return true;
        }

        if ((definition == typeof(ImmutableDictionary<,>) ||
             definition == typeof(IImmutableDictionary<,>)) &&
            arguments[0] == typeof(string))
        {
            converter = CreateMap(
                type,
                arguments[1],
                MakeGeneric(typeof(NbtImmutableDictionaryBuilder<,>), type, arguments[1]));
            return true;
        }

        if (definition == typeof(FrozenDictionary<,>) && arguments[0] == typeof(string))
        {
            converter = CreateMap(
                type,
                arguments[1],
                MakeGeneric(typeof(NbtFrozenDictionaryBuilder<>), arguments[1]));
            return true;
        }

        if (definition == typeof(ConcurrentDictionary<,>) && arguments[0] == typeof(string))
        {
            converter = CreateMap(
                type,
                arguments[1],
                MakeGeneric(typeof(NbtConcurrentDictionaryBuilder<>), arguments[1]));
            return true;
        }

        if (definition == typeof(SortedDictionary<,>) && arguments[0] == typeof(string))
        {
            converter = CreateMap(
                type,
                arguments[1],
                MakeGeneric(typeof(NbtSortedDictionaryBuilder<>), arguments[1]));
            return true;
        }

        if (definition == typeof(SortedList<,>) && arguments[0] == typeof(string))
        {
            converter = CreateMap(
                type,
                arguments[1],
                MakeGeneric(typeof(NbtSortedListBuilder<>), arguments[1]));
            return true;
        }

        if (definition == typeof(ImmutableSortedDictionary<,>) &&
            arguments[0] == typeof(string))
        {
            converter = CreateMap(
                type,
                arguments[1],
                MakeGeneric(
                    typeof(NbtImmutableSortedDictionaryBuilder<,>),
                    type,
                    arguments[1]));
            return true;
        }

        if (TryCreateReflectionFallback(type, out converter))
        {
            return true;
        }

        converter = null!;
        return false;
    }

    [RequiresUnreferencedCode("Building closed collection converters from runtime types is not trimming-safe.")]
    [RequiresDynamicCode("Building closed collection converters from runtime types requires runtime code generation.")]
    private static bool TryCreateReflectionFallback(Type type, out NbtConverter converter)
    {
        converter = null!;
        if (type.IsInterface || type.IsAbstract || type.IsGenericTypeDefinition ||
            type.ContainsGenericParameters)
        {
            return false;
        }

        if (TryGetMapTypes(type, out var keyType, out var valueType))
        {
            if (keyType != typeof(string))
            {
                return false;
            }

            try
            {
                converter = CreateGeneric(typeof(NbtReflectionMapConverter<,>), type, valueType);
                return true;
            }
            catch (Exception ex) when (ex is System.Reflection.TargetInvocationException or ArgumentException)
            {
                return false;
            }
        }

        if (TryGetSequenceElementType(type, out var elementType))
        {
            try
            {
                converter = CreateGeneric(
                    typeof(NbtReflectionSequenceConverter<,>),
                    type,
                    elementType);
                return true;
            }
            catch (Exception ex) when (ex is System.Reflection.TargetInvocationException or ArgumentException)
            {
                return false;
            }
        }

        return false;
    }

    [UnconditionalSuppressMessage("Aot", "IL2070", Justification = "Reflection is confined to the annotated reflection resolver path.")]
    private static bool TryGetMapTypes(Type type, out Type keyType, out Type valueType)
    {
        foreach (var candidate in type.GetInterfaces())
        {
            if (!candidate.IsGenericType)
            {
                continue;
            }

            var definition = candidate.GetGenericTypeDefinition();
            if (definition == typeof(IDictionary<,>) ||
                definition == typeof(IReadOnlyDictionary<,>))
            {
                keyType = candidate.GetGenericArguments()[0];
                valueType = candidate.GetGenericArguments()[1];
                return true;
            }
        }

        keyType = null!;
        valueType = null!;
        return false;
    }

    [UnconditionalSuppressMessage("Aot", "IL2070", Justification = "Reflection is confined to the annotated reflection resolver path.")]
    private static bool TryGetSequenceElementType(Type type, out Type elementType)
    {
        foreach (var candidate in type.GetInterfaces())
        {
            if (candidate.IsGenericType &&
                candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                elementType = candidate.GetGenericArguments()[0];
                return true;
            }
        }

        elementType = null!;
        return false;
    }

    private static bool IsListBacked(Type definition) =>
        definition == typeof(List<>) ||
        definition == typeof(IEnumerable<>) ||
        definition == typeof(ICollection<>) ||
        definition == typeof(IList<>) ||
        definition == typeof(IReadOnlyList<>) ||
        definition == typeof(IReadOnlyCollection<>);

    private static bool IsSetBacked(Type definition) =>
        definition == typeof(HashSet<>) ||
        definition == typeof(ISet<>) ||
        definition == typeof(IReadOnlySet<>);

    private static bool IsDictionaryBacked(Type definition) =>
        definition == typeof(Dictionary<,>) ||
        definition == typeof(IDictionary<,>) ||
        definition == typeof(IReadOnlyDictionary<,>);

    [RequiresUnreferencedCode("Building closed collection converters from runtime types is not trimming-safe.")]
    [RequiresDynamicCode("Building closed collection converters from runtime types requires runtime code generation.")]
    private static NbtConverter CreateSequence(
        Type collectionType,
        Type elementType,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
        Type builderType)
    {
        var converterType = typeof(NbtSequenceConverter<,,>)
            .MakeGenericType(collectionType, elementType, builderType);
        return (NbtConverter)Activator.CreateInstance(converterType)!;
    }

    [RequiresUnreferencedCode("Building closed collection converters from runtime types is not trimming-safe.")]
    [RequiresDynamicCode("Building closed collection converters from runtime types requires runtime code generation.")]
    private static NbtConverter CreateMap(
        Type collectionType,
        Type valueType,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
        Type builderType)
    {
        var converterType = typeof(NbtMapConverter<,,>)
            .MakeGenericType(builderType, collectionType, valueType);
        return (NbtConverter)Activator.CreateInstance(converterType)!;
    }

    [RequiresUnreferencedCode("Building closed collection converters from runtime types is not trimming-safe.")]
    [RequiresDynamicCode("Building closed collection converters from runtime types requires runtime code generation.")]
    private static Type MakeGeneric(Type definition, params Type[] arguments) =>
        definition.MakeGenericType(arguments);

    [RequiresUnreferencedCode("Building closed collection converters from runtime types is not trimming-safe.")]
    [RequiresDynamicCode("Building closed collection converters from runtime types requires runtime code generation.")]
    private static NbtConverter CreateGeneric(Type definition, params Type[] arguments) =>
        (NbtConverter)Activator.CreateInstance(definition.MakeGenericType(arguments))!;
}

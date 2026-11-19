using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace NbtSharp.Converters;

[UnconditionalSuppressMessage("Aot", "IL2026", Justification = "Reached only through annotated serializer entry points.")]
[UnconditionalSuppressMessage("Aot", "IL3050", Justification = "Reached only through annotated serializer entry points.")]
[UnconditionalSuppressMessage("Aot", "IL2060", Justification = "Reflection is confined to the annotated reflection resolver path.")]
[UnconditionalSuppressMessage("Aot", "IL2070", Justification = "Reflection is confined to the annotated reflection resolver path.")]
internal sealed class NbtReflectionMapConverter<TCollection, TValue> :
    NbtConverter<TCollection>
    where TCollection : IEnumerable<KeyValuePair<string, TValue>>
{
    private readonly MapPlan _plan = MapPlan.Create(typeof(TCollection), typeof(TValue));

    public override TCollection Read(ref NbtReader reader, NbtSerializerOptions options)
    {
        var values = NbtCollectionReader.ReadMap<TValue>(ref reader, options);
        return (TCollection)_plan.Create(values);
    }

    public override void Write(
        ref NbtWriter writer,
        TCollection value,
        NbtSerializerOptions options) =>
        NbtCollectionWriter.WriteMap<TCollection, TValue>(ref writer, value, options);

    public override bool TryGetTagType(
        TCollection value,
        NbtSerializerOptions options,
        out NbtTagType tagType)
    {
        tagType = NbtTagType.Compound;
        return true;
    }

    [UnconditionalSuppressMessage("Aot", "IL2060", Justification = "Reflection is confined to the annotated reflection resolver path.")]
    [UnconditionalSuppressMessage("Aot", "IL2070", Justification = "Reflection is confined to the annotated reflection resolver path.")]
    [UnconditionalSuppressMessage("Aot", "IL2087", Justification = "Reflection is confined to the annotated reflection resolver path.")]
    private sealed class MapPlan
    {
        private readonly ConstructorInfo? _constructor;
        private readonly bool _constructorTakesArray;
        private readonly MethodInfo? _factory;
        private readonly bool _factoryTakesArray;
        private readonly NbtReadOnlySpanFactory<TCollection, KeyValuePair<string, TValue>>?
            _readOnlySpanFactory;
        private readonly NbtSpanFactory<TCollection, KeyValuePair<string, TValue>>? _spanFactory;
        private readonly ConstructorInfo? _defaultConstructor;
        private readonly PropertyInfo? _indexer;
        private readonly MethodInfo? _add;
        private readonly bool _constructorTakesComparer;

        private MapPlan(
            ConstructorInfo? constructor,
            bool constructorTakesArray,
            MethodInfo? factory,
            bool factoryTakesArray,
            NbtReadOnlySpanFactory<TCollection, KeyValuePair<string, TValue>>? readOnlySpanFactory,
            NbtSpanFactory<TCollection, KeyValuePair<string, TValue>>? spanFactory,
            ConstructorInfo? defaultConstructor,
            PropertyInfo? indexer,
            MethodInfo? add,
            bool constructorTakesComparer)
        {
            _constructor = constructor;
            _constructorTakesArray = constructorTakesArray;
            _factory = factory;
            _factoryTakesArray = factoryTakesArray;
            _readOnlySpanFactory = readOnlySpanFactory;
            _spanFactory = spanFactory;
            _defaultConstructor = defaultConstructor;
            _indexer = indexer;
            _add = add;
            _constructorTakesComparer = constructorTakesComparer;
        }

        public static MapPlan Create(Type collectionType, Type valueType)
        {
            var pairType = typeof(KeyValuePair<,>).MakeGenericType(typeof(string), valueType);
            var attribute = collectionType.GetCustomAttribute<CollectionBuilderAttribute>();
            if (attribute is not null &&
                TryFindFactory(
                    attribute.BuilderType,
                    attribute.MethodName,
                    collectionType,
                    valueType,
                    pairType,
                    out var factory,
                    out var factoryTakesArray,
                    out var readOnlySpanFactory,
                    out var spanFactory))
            {
                return new MapPlan(
                    null,
                    false,
                    factory,
                    factoryTakesArray,
                    readOnlySpanFactory,
                    spanFactory,
                    null,
                    null,
                    null,
                    false);
            }

            var companion = FindCompanionType(collectionType);
            if (companion is not null &&
                (TryFindFactory(
                     companion,
                     "CreateRange",
                     collectionType,
                     valueType,
                     pairType,
                     out factory,
                     out factoryTakesArray,
                     out readOnlySpanFactory,
                     out spanFactory) ||
                 TryFindFactory(
                     companion,
                     "Create",
                     collectionType,
                     valueType,
                     pairType,
                     out factory,
                     out factoryTakesArray,
                     out readOnlySpanFactory,
                     out spanFactory)))
            {
                return new MapPlan(
                    null,
                    false,
                    factory,
                    factoryTakesArray,
                    readOnlySpanFactory,
                    spanFactory,
                    null,
                    null,
                    null,
                    false);
            }

            var indexer = collectionType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(property =>
                    property.GetIndexParameters() is [{ ParameterType: var keyType }] &&
                    keyType == typeof(string) &&
                    property.PropertyType == valueType &&
                    property.SetMethod is not null);
            var add = collectionType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(method =>
                    method.Name == "Add" &&
                    !method.IsStatic &&
                    method.GetParameters() is
                        [{ ParameterType: var keyType }, { ParameterType: var valueParameterType }] &&
                    keyType == typeof(string) &&
                    valueParameterType == valueType);
            var comparerConstructor = FindComparerConstructor(collectionType);
            if (comparerConstructor is not null && (indexer is not null || add is not null))
            {
                return new MapPlan(
                    null,
                    false,
                    null,
                    false,
                    null,
                    null,
                    comparerConstructor,
                    indexer,
                    add,
                    true);
            }

            var defaultConstructor = collectionType.GetConstructor(Type.EmptyTypes);
            if ((defaultConstructor is not null || collectionType.IsValueType) &&
                (indexer is not null || add is not null))
            {
                return new MapPlan(
                    null,
                    false,
                    null,
                    false,
                    null,
                    null,
                    defaultConstructor,
                    indexer,
                    add,
                    false);
            }

            var constructors = collectionType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            foreach (var constructor in constructors)
            {
                if (constructor.GetParameters().Length != 1)
                {
                    continue;
                }

                var parameterType = constructor.GetParameters()[0].ParameterType;
                if (parameterType == pairType.MakeArrayType())
                {
                    return new MapPlan(
                        constructor,
                        true,
                        null,
                        false,
                        null,
                        null,
                        null,
                        null,
                        null,
                        false);
                }

                if (parameterType.IsAssignableFrom(typeof(Dictionary<string, TValue>)))
                {
                    return new MapPlan(
                        constructor,
                        false,
                        null,
                        false,
                        null,
                        null,
                        null,
                        null,
                        null,
                        false);
                }
            }

            throw new NotSupportedException(
                $"No supported construction pattern was found for {collectionType}.");
        }

        private static Type? FindCompanionType(Type collectionType)
        {
            var name = collectionType.Name;
            var arityIndex = name.IndexOf('`');
            if (arityIndex >= 0)
            {
                name = name[..arityIndex];
            }

            return collectionType.Assembly.GetType($"{collectionType.Namespace}.{name}");
        }

        private static ConstructorInfo? FindComparerConstructor(Type collectionType)
        {
            var comparerType = typeof(IComparer<>).MakeGenericType(typeof(string));
            var equalityComparerType = typeof(IEqualityComparer<>).MakeGenericType(typeof(string));
            return collectionType.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(constructor =>
                    constructor.GetParameters() is [{ ParameterType: var parameterType }] &&
                    (parameterType == comparerType || parameterType == equalityComparerType));
        }

        public object Create(Dictionary<string, TValue> values)
        {
            if (_readOnlySpanFactory is not null)
            {
                return _readOnlySpanFactory(values.ToArray());
            }

            if (_spanFactory is not null)
            {
                return _spanFactory(values.ToArray());
            }

            if (_factory is not null)
            {
                return NbtReflectionInvoke.Invoke(
                    _factory,
                    null,
                    [_factoryTakesArray ? values.ToArray() : values])!;
            }

            if (_constructor is not null)
            {
                return NbtReflectionInvoke.Invoke(
                    _constructor,
                    null,
                    [_constructorTakesArray ? values.ToArray() : values])!;
            }

            var result = _constructorTakesComparer
                ? NbtReflectionInvoke.Invoke(_defaultConstructor, null, [StringComparer.Ordinal])!
                : _defaultConstructor is not null
                    ? NbtReflectionInvoke.Invoke(_defaultConstructor, null, [])!
                    : Activator.CreateInstance(typeof(TCollection))!;
            foreach (var pair in values)
            {
                if (_indexer is not null)
                {
                    NbtReflectionInvoke.Invoke(
                        _indexer.SetMethod,
                        result,
                        [pair.Key, pair.Value]);
                }
                else
                {
                    NbtReflectionInvoke.Invoke(
                        _add,
                        result,
                        [pair.Key, pair.Value]);
                }
            }

            return result;
        }
    }

    [UnconditionalSuppressMessage("Aot", "IL2060", Justification = "Reflection is confined to the annotated reflection resolver path.")]
    [UnconditionalSuppressMessage("Aot", "IL2070", Justification = "Reflection is confined to the annotated reflection resolver path.")]
    private static bool TryCloseFactory(
        MethodInfo candidate,
        Type valueType,
        [NotNullWhen(true)] out MethodInfo? closed)
    {
        if (!candidate.IsGenericMethodDefinition)
        {
            closed = candidate;
            return true;
        }

        try
        {
            closed = candidate.GetGenericArguments().Length switch
            {
                1 => candidate.MakeGenericMethod(valueType),
                2 => candidate.MakeGenericMethod(typeof(string), valueType),
                _ => null,
            };
        }
        catch (ArgumentException)
        {
            closed = null;
            return false;
        }

        return closed is not null;
    }

    private static bool TryFindFactory(
        Type builderType,
        string methodName,
        Type collectionType,
        Type valueType,
        Type pairType,
        out MethodInfo method,
        out bool takesArray,
        out NbtReadOnlySpanFactory<TCollection, KeyValuePair<string, TValue>>? readOnlySpanFactory,
        out NbtSpanFactory<TCollection, KeyValuePair<string, TValue>>? spanFactory)
    {
        method = null!;
        takesArray = false;
        readOnlySpanFactory = null;
        spanFactory = null;
        MethodInfo? enumerableMethod = null;
        MethodInfo? arrayMethod = null;
        MethodInfo? readOnlySpanMethod = null;
        MethodInfo? writableSpanMethod = null;
        foreach (var candidate in builderType.GetMethods(
                     BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
        {
            if (candidate.Name != methodName || candidate.GetParameters().Length != 1)
            {
                continue;
            }

            if (!TryCloseFactory(candidate, valueType, out var methodToCheck) ||
                !collectionType.IsAssignableFrom(methodToCheck.ReturnType))
            {
                continue;
            }

            var parameterType = methodToCheck.GetParameters()[0].ParameterType;
            if (parameterType == pairType.MakeArrayType())
            {
                arrayMethod ??= methodToCheck;
            }
            else if (parameterType.IsAssignableFrom(typeof(Dictionary<string, TValue>)))
            {
                enumerableMethod ??= methodToCheck;
            }
            else if (parameterType.IsGenericType &&
                     parameterType.GetGenericArguments()[0] == pairType)
            {
                if (parameterType.GetGenericTypeDefinition() == typeof(ReadOnlySpan<>))
                {
                    readOnlySpanMethod ??= methodToCheck;
                }
                else if (parameterType.GetGenericTypeDefinition() == typeof(Span<>))
                {
                    writableSpanMethod ??= methodToCheck;
                }
            }
        }

        if (enumerableMethod is not null)
        {
            method = enumerableMethod;
            return true;
        }

        if (TryCreateSpanFactory(
                readOnlySpanMethod,
                writableSpanMethod,
                collectionType,
                pairType,
                out readOnlySpanFactory,
                out spanFactory))
        {
            return true;
        }

        if (arrayMethod is not null)
        {
            method = arrayMethod;
            takesArray = true;
            return true;
        }

        return false;
    }

    private static bool TryCreateSpanFactory(
        MethodInfo? readOnlySpanMethod,
        MethodInfo? writableSpanMethod,
        Type collectionType,
        Type pairType,
        out NbtReadOnlySpanFactory<TCollection, KeyValuePair<string, TValue>>? readOnlySpanFactory,
        out NbtSpanFactory<TCollection, KeyValuePair<string, TValue>>? spanFactory)
    {
        readOnlySpanFactory = null;
        spanFactory = null;
        if (readOnlySpanMethod is not null)
        {
            try
            {
                readOnlySpanFactory =
                    (NbtReadOnlySpanFactory<TCollection, KeyValuePair<string, TValue>>)
                    readOnlySpanMethod.CreateDelegate(
                        typeof(NbtReadOnlySpanFactory<,>).MakeGenericType(collectionType, pairType));
                return true;
            }
            catch (Exception ex) when (IsSpanFactoryBindingFailure(ex))
            {
            }
        }

        if (writableSpanMethod is null)
        {
            return false;
        }

        try
        {
            spanFactory =
                (NbtSpanFactory<TCollection, KeyValuePair<string, TValue>>)writableSpanMethod.CreateDelegate(
                    typeof(NbtSpanFactory<,>).MakeGenericType(collectionType, pairType));
            return true;
        }
        catch (Exception ex) when (IsSpanFactoryBindingFailure(ex))
        {
            return false;
        }
    }

    private static bool IsSpanFactoryBindingFailure(Exception exception) =>
        exception is ArgumentException or NotSupportedException or
            MethodAccessException or InvalidOperationException;
}

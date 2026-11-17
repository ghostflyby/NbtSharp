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
    private sealed class MapPlan
    {
        private readonly ConstructorInfo? _constructor;
        private readonly bool _constructorTakesArray;
        private readonly MethodInfo? _factory;
        private readonly bool _factoryTakesArray;
        private readonly ConstructorInfo? _defaultConstructor;
        private readonly PropertyInfo? _indexer;
        private readonly MethodInfo? _add;

        private MapPlan(
            ConstructorInfo? constructor,
            bool constructorTakesArray,
            MethodInfo? factory,
            bool factoryTakesArray,
            ConstructorInfo? defaultConstructor,
            PropertyInfo? indexer,
            MethodInfo? add)
        {
            _constructor = constructor;
            _constructorTakesArray = constructorTakesArray;
            _factory = factory;
            _factoryTakesArray = factoryTakesArray;
            _defaultConstructor = defaultConstructor;
            _indexer = indexer;
            _add = add;
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
                    out var factoryTakesArray))
            {
                return new MapPlan(null, false, factory, factoryTakesArray, null, null, null);
            }

            var companion = collectionType.Assembly.GetType(
                $"{collectionType.Namespace}.{collectionType.Name}");
            if (companion is not null &&
                (TryFindFactory(
                     companion,
                     "CreateRange",
                     collectionType,
                     valueType,
                     pairType,
                     out factory,
                     out factoryTakesArray) ||
                 TryFindFactory(
                     companion,
                     "Create",
                     collectionType,
                     valueType,
                     pairType,
                     out factory,
                     out factoryTakesArray)))
            {
                return new MapPlan(null, false, factory, factoryTakesArray, null, null, null);
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
                    return new MapPlan(constructor, true, null, false, null, null, null);
                }

                if (parameterType.IsAssignableFrom(typeof(Dictionary<string, TValue>)))
                {
                    return new MapPlan(constructor, false, null, false, null, null, null);
                }
            }

            var defaultConstructor = collectionType.GetConstructor(Type.EmptyTypes);
            if (defaultConstructor is not null)
            {
                var indexer = collectionType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(property =>
                        property.GetIndexParameters() is [{ ParameterType: var keyType }] &&
                        keyType == typeof(string) &&
                        property.PropertyType == valueType &&
                        property.SetMethod is not null);
                if (indexer is not null)
                {
                    return new MapPlan(null, false, null, false, defaultConstructor, indexer, null);
                }

                var add = collectionType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(method =>
                        method.Name == "Add" &&
                        !method.IsStatic &&
                        method.GetParameters() is
                            [{ ParameterType: var keyType }, { ParameterType: var valueParameterType }] &&
                        keyType == typeof(string) &&
                        valueParameterType == valueType);
                if (add is not null)
                {
                    return new MapPlan(null, false, null, false, defaultConstructor, null, add);
                }
            }

            throw new NotSupportedException(
                $"No supported construction pattern was found for {collectionType}.");
        }

        public object Create(Dictionary<string, TValue> values)
        {
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

            var result = NbtReflectionInvoke.Invoke(_defaultConstructor, null, [])!;
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
    private static bool TryFindFactory(
        Type builderType,
        string methodName,
        Type collectionType,
        Type valueType,
        Type pairType,
        out MethodInfo method,
        out bool takesArray)
    {
        takesArray = false;
        foreach (var candidate in builderType.GetMethods(
                     BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
        {
            if (candidate.Name != methodName || candidate.GetParameters().Length != 1)
            {
                continue;
            }

            var methodToCheck = candidate;
            if (candidate.IsGenericMethodDefinition)
            {
                try
                {
                    methodToCheck = candidate.MakeGenericMethod(typeof(string), valueType);
                }
                catch (ArgumentException)
                {
                    continue;
                }
            }

            if (!collectionType.IsAssignableFrom(methodToCheck.ReturnType))
            {
                continue;
            }

            var parameterType = methodToCheck.GetParameters()[0].ParameterType;
            if (parameterType == pairType.MakeArrayType())
            {
                method = methodToCheck;
                takesArray = true;
                return true;
            }

            if (parameterType.IsAssignableFrom(typeof(Dictionary<string, TValue>)))
            {
                method = methodToCheck;
                return true;
            }
        }

        method = null!;
        return false;
    }
}

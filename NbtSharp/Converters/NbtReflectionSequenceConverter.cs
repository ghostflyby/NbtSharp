using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace NbtSharp.Converters;

[UnconditionalSuppressMessage("Aot", "IL2026", Justification = "Reached only through annotated serializer entry points.")]
[UnconditionalSuppressMessage("Aot", "IL3050", Justification = "Reached only through annotated serializer entry points.")]
[UnconditionalSuppressMessage("Aot", "IL2060", Justification = "Reflection is confined to the annotated reflection resolver path.")]
[UnconditionalSuppressMessage("Aot", "IL2070", Justification = "Reflection is confined to the annotated reflection resolver path.")]
internal sealed class NbtReflectionSequenceConverter<TCollection, TElement> :
    NbtConverter<TCollection>
    where TCollection : IEnumerable<TElement>
{
    private readonly SequencePlan _plan = SequencePlan.Create(typeof(TCollection), typeof(TElement));

    public override TCollection Read(ref NbtReader reader, NbtSerializerOptions options)
    {
        var elements = NbtCollectionReader.ReadList<TElement>(ref reader, options);
        return (TCollection)_plan.Create(elements);
    }

    public override void Write(
        ref NbtWriter writer,
        TCollection value,
        NbtSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);
        NbtCollectionWriter.WriteEnumerable(ref writer, value, options);
    }

    public override bool TryGetTagType(
        TCollection value,
        NbtSerializerOptions options,
        out NbtTagType tagType)
    {
        tagType = NbtTagType.List;
        return true;
    }

    [UnconditionalSuppressMessage("Aot", "IL2060", Justification = "Reflection is confined to the annotated reflection resolver path.")]
    [UnconditionalSuppressMessage("Aot", "IL2070", Justification = "Reflection is confined to the annotated reflection resolver path.")]
    private sealed class SequencePlan
    {
        private readonly ConstructorInfo? _constructor;
        private readonly bool _constructorTakesArray;
        private readonly MethodInfo? _factory;
        private readonly bool _factoryTakesArray;
        private readonly ConstructorInfo? _defaultConstructor;
        private readonly MethodInfo? _add;
        private readonly bool _reverse;

        private SequencePlan(
            ConstructorInfo? constructor,
            bool constructorTakesArray,
            MethodInfo? factory,
            bool factoryTakesArray,
            ConstructorInfo? defaultConstructor,
            MethodInfo? add,
            bool reverse)
        {
            _constructor = constructor;
            _constructorTakesArray = constructorTakesArray;
            _factory = factory;
            _factoryTakesArray = factoryTakesArray;
            _defaultConstructor = defaultConstructor;
            _add = add;
            _reverse = reverse;
        }

        public static SequencePlan Create(Type collectionType, Type elementType)
        {
            var attribute = collectionType.GetCustomAttribute<CollectionBuilderAttribute>();
            if (attribute is not null &&
                TryFindFactory(
                    attribute.BuilderType,
                    attribute.MethodName,
                    collectionType,
                    elementType,
                    out var factory,
                    out var factoryTakesArray))
            {
                return new SequencePlan(null, false, factory, factoryTakesArray, null, null, false);
            }

            var companion = collectionType.Assembly.GetType(
                $"{collectionType.Namespace}.{collectionType.Name}");
            if (companion is not null &&
                (TryFindFactory(
                     companion,
                     "CreateRange",
                     collectionType,
                     elementType,
                     out factory,
                     out factoryTakesArray) ||
                 TryFindFactory(
                     companion,
                     "Create",
                     collectionType,
                     elementType,
                     out factory,
                     out factoryTakesArray)))
            {
                return new SequencePlan(null, false, factory, factoryTakesArray, null, null, false);
            }

            var constructors = collectionType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            foreach (var constructor in constructors)
            {
                if (constructor.GetParameters().Length != 1)
                {
                    continue;
                }

                var parameterType = constructor.GetParameters()[0].ParameterType;
                if (parameterType == elementType.MakeArrayType())
                {
                    return new SequencePlan(constructor, true, null, false, null, null, false);
                }

                if (parameterType.IsAssignableFrom(typeof(List<TElement>)))
                {
                    return new SequencePlan(constructor, false, null, false, null, null, false);
                }
            }

            var defaultConstructor = collectionType.GetConstructor(Type.EmptyTypes);
            if (defaultConstructor is not null)
            {
                foreach (var name in new[] { "Add", "Enqueue", "Push" })
                {
                    var add = collectionType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(method =>
                            method.Name == name &&
                            !method.IsStatic &&
                            method.GetParameters().Length == 1 &&
                            method.GetParameters()[0].ParameterType == elementType);
                    if (add is not null)
                    {
                        return new SequencePlan(
                            null,
                            false,
                            null,
                            false,
                            defaultConstructor,
                            add,
                            name == "Push");
                    }
                }
            }

            throw new NotSupportedException(
                $"No supported construction pattern was found for {collectionType}.");
        }

        public object Create(List<TElement> elements)
        {
            if (_factory is not null)
            {
                return NbtReflectionInvoke.Invoke(
                    _factory,
                    null,
                    [_factoryTakesArray ? elements.ToArray() : elements])!;
            }

            if (_constructor is not null)
            {
                return NbtReflectionInvoke.Invoke(
                    _constructor,
                    null,
                    [_constructorTakesArray ? elements.ToArray() : elements])!;
            }

            var result = NbtReflectionInvoke.Invoke(_defaultConstructor, null, [])!;
            if (_reverse)
            {
                for (var index = elements.Count - 1; index >= 0; index--)
                {
                    NbtReflectionInvoke.Invoke(_add, result, [elements[index]]);
                }
            }
            else
            {
                foreach (var element in elements)
                {
                    NbtReflectionInvoke.Invoke(_add, result, [element]);
                }
            }

            return result;
        }
    }

    [UnconditionalSuppressMessage("Aot", "IL2060", Justification = "Reflection is confined to the annotated reflection resolver path.")]
    [UnconditionalSuppressMessage("Aot", "IL2070", Justification = "Reflection is confined to the annotated reflection resolver path.")]
    internal static bool TryFindFactory(
        Type builderType,
        string methodName,
        Type collectionType,
        Type elementType,
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
                    methodToCheck = candidate.MakeGenericMethod(elementType);
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
            if (parameterType == elementType.MakeArrayType())
            {
                method = methodToCheck;
                takesArray = true;
                return true;
            }

            if (parameterType.IsAssignableFrom(typeof(List<TElement>)))
            {
                method = methodToCheck;
                return true;
            }
        }

        method = null!;
        return false;
    }
}

internal static class NbtReflectionInvoke
{
    public static object? Invoke(MethodBase? method, object? instance, object?[] arguments)
    {
        if (method is null)
        {
            throw new InvalidOperationException("The reflection collection plan is incomplete.");
        }

        try
        {
            return method is ConstructorInfo constructor
                ? constructor.Invoke(arguments)
                : ((MethodInfo)method).Invoke(instance, arguments);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }
}

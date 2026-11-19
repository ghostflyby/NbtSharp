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
    [UnconditionalSuppressMessage("Aot", "IL2087", Justification = "Reflection is confined to the annotated reflection resolver path.")]
    private sealed class SequencePlan
    {
        private readonly ConstructorInfo? _constructor;
        private readonly bool _constructorTakesArray;
        private readonly MethodInfo? _factory;
        private readonly bool _factoryTakesArray;
        private readonly NbtReadOnlySpanFactory<TCollection, TElement>? _readOnlySpanFactory;
        private readonly NbtSpanFactory<TCollection, TElement>? _spanFactory;
        private readonly ConstructorInfo? _defaultConstructor;
        private readonly MethodInfo? _add;
        private readonly bool _reverse;

        private SequencePlan(
            ConstructorInfo? constructor,
            bool constructorTakesArray,
            MethodInfo? factory,
            bool factoryTakesArray,
            NbtReadOnlySpanFactory<TCollection, TElement>? readOnlySpanFactory,
            NbtSpanFactory<TCollection, TElement>? spanFactory,
            ConstructorInfo? defaultConstructor,
            MethodInfo? add,
            bool reverse)
        {
            _constructor = constructor;
            _constructorTakesArray = constructorTakesArray;
            _factory = factory;
            _factoryTakesArray = factoryTakesArray;
            _readOnlySpanFactory = readOnlySpanFactory;
            _spanFactory = spanFactory;
            _defaultConstructor = defaultConstructor;
            _add = add;
            _reverse = reverse;
        }

        public static SequencePlan Create(Type collectionType, Type elementType)
        {
            var addMethods = new[] { "Add", "Enqueue", "Push" }
                .Select(name => (Name: name, Method: FindMutator(collectionType, name, elementType)))
                .Where(static pair => pair.Method is not null)
                .ToArray();
            var stackLike = addMethods.Any(static pair => pair.Name == "Push") &&
                            addMethods.All(static pair => pair.Name is not ("Add" or "Enqueue"));
            var attribute = collectionType.GetCustomAttribute<CollectionBuilderAttribute>();
            if (attribute is not null &&
                TryFindFactory(
                    attribute.BuilderType,
                    attribute.MethodName,
                    collectionType,
                    elementType,
                    out var factory,
                    out var factoryTakesArray,
                    out var readOnlySpanFactory,
                    out var spanFactory))
            {
                return new SequencePlan(
                    null,
                    false,
                    factory,
                    factoryTakesArray,
                    readOnlySpanFactory,
                    spanFactory,
                    null,
                    null,
                    stackLike);
            }

            var companion = FindCompanionType(collectionType);
            if (companion is not null &&
                (TryFindFactory(
                     companion,
                     "CreateRange",
                     collectionType,
                     elementType,
                     out factory,
                     out factoryTakesArray,
                     out readOnlySpanFactory,
                     out spanFactory) ||
                 TryFindFactory(
                     companion,
                     "Create",
                     collectionType,
                     elementType,
                     out factory,
                     out factoryTakesArray,
                     out readOnlySpanFactory,
                     out spanFactory)))
            {
                return new SequencePlan(
                    null,
                    false,
                    factory,
                    factoryTakesArray,
                    readOnlySpanFactory,
                    spanFactory,
                    null,
                    null,
                    stackLike);
            }

            var defaultConstructor = collectionType.GetConstructor(Type.EmptyTypes);
            if ((defaultConstructor is not null || collectionType.IsValueType) &&
                addMethods.Length != 0)
            {
                var add = addMethods.FirstOrDefault(static pair => pair.Name == "Add").Method ??
                          addMethods.FirstOrDefault(static pair => pair.Name == "Enqueue").Method ??
                          addMethods.FirstOrDefault(static pair => pair.Name == "Push").Method;
                return new SequencePlan(
                    null,
                    false,
                    null,
                    false,
                    null,
                    null,
                    defaultConstructor,
                    add,
                    stackLike);
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
                    return new SequencePlan(
                        constructor,
                        true,
                        null,
                        false,
                        null,
                        null,
                        null,
                        null,
                        stackLike);
                }

                if (parameterType.IsAssignableFrom(typeof(List<TElement>)))
                {
                    return new SequencePlan(
                        constructor,
                        false,
                        null,
                        false,
                        null,
                        null,
                        null,
                        null,
                        stackLike);
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

        private static MethodInfo? FindMutator(Type collectionType, string name, Type elementType) =>
            collectionType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(method =>
                    method.Name == name &&
                    !method.IsStatic &&
                    method.GetParameters() is [{ ParameterType: var parameterType }] &&
                    parameterType == elementType);

        public object Create(List<TElement> elements)
        {
            if (_readOnlySpanFactory is not null)
            {
                if (_reverse)
                {
                    elements.Reverse();
                }

                return _readOnlySpanFactory(elements.ToArray());
            }

            if (_spanFactory is not null)
            {
                if (_reverse)
                {
                    elements.Reverse();
                }

                return _spanFactory(elements.ToArray());
            }

            if (_factory is not null)
            {
                if (_reverse)
                {
                    elements.Reverse();
                }

                return NbtReflectionInvoke.Invoke(
                    _factory,
                    null,
                    [_factoryTakesArray ? elements.ToArray() : elements])!;
            }

            if (_constructor is not null)
            {
                if (_reverse)
                {
                    elements.Reverse();
                }

                return NbtReflectionInvoke.Invoke(
                    _constructor,
                    null,
                    [_constructorTakesArray ? elements.ToArray() : elements])!;
            }

            var result = _defaultConstructor is not null
                ? NbtReflectionInvoke.Invoke(_defaultConstructor, null, [])!
                : Activator.CreateInstance(typeof(TCollection))!;
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
        out bool takesArray,
        out NbtReadOnlySpanFactory<TCollection, TElement>? readOnlySpanFactory,
        out NbtSpanFactory<TCollection, TElement>? spanFactory)
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
                arrayMethod ??= methodToCheck;
            }
            else if (parameterType.IsAssignableFrom(typeof(List<TElement>)))
            {
                enumerableMethod ??= methodToCheck;
            }
            else if (parameterType.IsGenericType &&
                     parameterType.GetGenericArguments()[0] == elementType)
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
                elementType,
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
        Type elementType,
        out NbtReadOnlySpanFactory<TCollection, TElement>? readOnlySpanFactory,
        out NbtSpanFactory<TCollection, TElement>? spanFactory)
    {
        readOnlySpanFactory = null;
        spanFactory = null;
        if (readOnlySpanMethod is not null)
        {
            try
            {
                readOnlySpanFactory =
                    (NbtReadOnlySpanFactory<TCollection, TElement>)readOnlySpanMethod.CreateDelegate(
                        typeof(NbtReadOnlySpanFactory<,>).MakeGenericType(
                            collectionType,
                            elementType));
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
            spanFactory = (NbtSpanFactory<TCollection, TElement>)writableSpanMethod.CreateDelegate(
                typeof(NbtSpanFactory<,>).MakeGenericType(collectionType, elementType));
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

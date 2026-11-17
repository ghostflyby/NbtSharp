using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using NbtSharp.Converters;

namespace NbtSharp;

/// <summary>
/// Supplies converter factories and helpers for source-generated converters.
/// </summary>
/// <remarks>
/// This type is public so generated code can reference it. It is not intended for direct use.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class NbtMetadataServices
{
    /// <summary>Creates a converter for an array without a dedicated NBT array tag.</summary>
    /// <typeparam name="TElement">The array element type.</typeparam>
    /// <returns>The converter.</returns>
    public static NbtConverter<TElement[]> CreateArrayConverter<TElement>() =>
        new NbtArrayConverter<TElement>();

    /// <summary>Creates a converter backed by a generated sequence builder.</summary>
    /// <typeparam name="TCollection">The declared collection type.</typeparam>
    /// <typeparam name="TElement">The element type.</typeparam>
    /// <typeparam name="TBuilder">The builder type implemented by generated code.</typeparam>
    /// <returns>The converter.</returns>
    public static NbtConverter<TCollection> CreateSequenceConverter<TCollection, TElement, TBuilder>()
        where TCollection : IEnumerable<TElement>
        where TBuilder : struct, INbtSequenceBuilder<TCollection, TElement> =>
        new NbtSequenceConverter<TCollection, TElement, TBuilder>();

    /// <summary>Creates a converter backed by a generated map builder.</summary>
    /// <typeparam name="TCollection">The declared dictionary type.</typeparam>
    /// <typeparam name="TValue">The value type.</typeparam>
    /// <typeparam name="TBuilder">The builder type implemented by generated code.</typeparam>
    /// <returns>The converter.</returns>
    public static NbtConverter<TCollection> CreateMapConverter<TCollection, TValue, TBuilder>()
        where TCollection : IEnumerable<KeyValuePair<string, TValue>>
        where TBuilder : struct, INbtMapBuilder<TCollection, TValue> =>
        new NbtMapConverter<TBuilder, TCollection, TValue>();

    /// <summary>Creates a converter for a list-backed collection or collection interface.</summary>
    /// <typeparam name="TCollection">The declared collection type.</typeparam>
    /// <typeparam name="TElement">The element type.</typeparam>
    /// <returns>The converter.</returns>
    public static NbtConverter<TCollection> CreateListBackedCollectionConverter<TCollection, TElement>()
        where TCollection : IEnumerable<TElement> =>
        new NbtListBackedCollectionConverter<TCollection, TElement>();

    /// <summary>Creates a converter for <see cref="List{T}"/>.</summary>
    /// <typeparam name="TElement">The element type.</typeparam>
    /// <returns>The converter.</returns>
    public static NbtConverter<List<TElement>> CreateListConverter<TElement>() =>
        new NbtListBackedCollectionConverter<List<TElement>, TElement>();

    /// <summary>Creates a converter for <see cref="IReadOnlyList{T}"/>.</summary>
    /// <typeparam name="TElement">The element type.</typeparam>
    /// <returns>The converter.</returns>
    public static NbtConverter<IReadOnlyList<TElement>> CreateReadOnlyListConverter<TElement>() =>
        new NbtListBackedCollectionConverter<IReadOnlyList<TElement>, TElement>();

    /// <summary>Creates a converter for <see cref="IReadOnlyCollection{T}"/>.</summary>
    /// <typeparam name="TElement">The element type.</typeparam>
    /// <returns>The converter.</returns>
    public static NbtConverter<IReadOnlyCollection<TElement>> CreateReadOnlyCollectionConverter<TElement>() =>
        new NbtListBackedCollectionConverter<IReadOnlyCollection<TElement>, TElement>();

    /// <summary>Creates a converter for a hash-set-backed collection or set interface.</summary>
    /// <typeparam name="TCollection">The declared collection type.</typeparam>
    /// <typeparam name="TElement">The element type.</typeparam>
    /// <returns>The converter.</returns>
    public static NbtConverter<TCollection> CreateSetBackedCollectionConverter<TCollection, TElement>()
        where TCollection : IEnumerable<TElement> =>
        new NbtSequenceConverter<TCollection, TElement, NbtSetBuilder<TCollection, TElement>>();

    /// <summary>Creates a converter for <see cref="Queue{T}"/>.</summary>
    /// <typeparam name="TElement">The element type.</typeparam>
    /// <returns>The converter.</returns>
    public static NbtConverter<Queue<TElement>> CreateQueueConverter<TElement>() =>
        new NbtSequenceConverter<Queue<TElement>, TElement, NbtQueueBuilder<TElement>>();

    /// <summary>Creates a converter for <see cref="Stack{T}"/>.</summary>
    /// <typeparam name="TElement">The element type.</typeparam>
    /// <returns>The converter.</returns>
    public static NbtConverter<Stack<TElement>> CreateStackConverter<TElement>() =>
        new NbtSequenceConverter<Stack<TElement>, TElement, NbtStackBuilder<TElement>>();

    /// <summary>Creates a converter for <see cref="LinkedList{T}"/>.</summary>
    /// <typeparam name="TElement">The element type.</typeparam>
    /// <returns>The converter.</returns>
    public static NbtConverter<LinkedList<TElement>> CreateLinkedListConverter<TElement>() =>
        new NbtSequenceConverter<LinkedList<TElement>, TElement, NbtLinkedListBuilder<TElement>>();

    /// <summary>Creates a converter for <see cref="Collection{T}"/>.</summary>
    /// <typeparam name="TElement">The element type.</typeparam>
    /// <returns>The converter.</returns>
    public static NbtConverter<Collection<TElement>> CreateCollectionConverter<TElement>() =>
        new NbtSequenceConverter<Collection<TElement>, TElement, NbtCollectionBuilder<TElement>>();

    /// <summary>Creates a converter for <see cref="ObservableCollection{T}"/>.</summary>
    /// <typeparam name="TElement">The element type.</typeparam>
    /// <returns>The converter.</returns>
    public static NbtConverter<ObservableCollection<TElement>> CreateObservableCollectionConverter<TElement>() =>
        new NbtSequenceConverter<
            ObservableCollection<TElement>,
            TElement,
            NbtObservableCollectionBuilder<TElement>>();

    /// <summary>Creates a converter for <see cref="ReadOnlyCollection{T}"/>.</summary>
    /// <typeparam name="TElement">The element type.</typeparam>
    /// <returns>The converter.</returns>
    public static NbtConverter<ReadOnlyCollection<TElement>> CreateReadOnlyCollectionWrapperConverter<TElement>() =>
        new NbtSequenceConverter<
            ReadOnlyCollection<TElement>,
            TElement,
            NbtReadOnlyCollectionBuilder<TElement>>();

    /// <summary>Creates a converter for <see cref="ImmutableArray{T}"/>.</summary>
    /// <typeparam name="TElement">The element type.</typeparam>
    /// <returns>The converter.</returns>
    public static NbtConverter<ImmutableArray<TElement>> CreateImmutableArrayConverter<TElement>() =>
        new NbtImmutableArrayConverter<TElement>();

    /// <summary>Creates a converter for an immutable list or immutable-list interface.</summary>
    /// <typeparam name="TCollection">The declared collection type.</typeparam>
    /// <typeparam name="TElement">The element type.</typeparam>
    /// <returns>The converter.</returns>
    public static NbtConverter<TCollection> CreateImmutableListConverter<TCollection, TElement>()
        where TCollection : IEnumerable<TElement> =>
        new NbtSequenceConverter<
            TCollection,
            TElement,
            NbtImmutableListBuilder<TCollection, TElement>>();

    /// <summary>Creates a converter for an immutable hash set or immutable-set interface.</summary>
    /// <typeparam name="TCollection">The declared collection type.</typeparam>
    /// <typeparam name="TElement">The element type.</typeparam>
    /// <returns>The converter.</returns>
    public static NbtConverter<TCollection> CreateImmutableHashSetConverter<TCollection, TElement>()
        where TCollection : IEnumerable<TElement> =>
        new NbtSequenceConverter<
            TCollection,
            TElement,
            NbtImmutableHashSetBuilder<TCollection, TElement>>();

    /// <summary>Creates a converter for an immutable queue or immutable-queue interface.</summary>
    /// <typeparam name="TCollection">The declared collection type.</typeparam>
    /// <typeparam name="TElement">The element type.</typeparam>
    /// <returns>The converter.</returns>
    public static NbtConverter<TCollection> CreateImmutableQueueConverter<TCollection, TElement>()
        where TCollection : IEnumerable<TElement> =>
        new NbtSequenceConverter<
            TCollection,
            TElement,
            NbtImmutableQueueBuilder<TCollection, TElement>>();

    /// <summary>Creates a converter for an immutable stack or immutable-stack interface.</summary>
    /// <typeparam name="TCollection">The declared collection type.</typeparam>
    /// <typeparam name="TElement">The element type.</typeparam>
    /// <returns>The converter.</returns>
    public static NbtConverter<TCollection> CreateImmutableStackConverter<TCollection, TElement>()
        where TCollection : IEnumerable<TElement> =>
        new NbtSequenceConverter<
            TCollection,
            TElement,
            NbtImmutableStackBuilder<TCollection, TElement>>();

    /// <summary>Creates a converter for <see cref="FrozenSet{T}"/>.</summary>
    /// <typeparam name="TElement">The element type.</typeparam>
    /// <returns>The converter.</returns>
    public static NbtConverter<FrozenSet<TElement>> CreateFrozenSetConverter<TElement>() =>
        new NbtSequenceConverter<FrozenSet<TElement>, TElement, NbtFrozenSetBuilder<TElement>>();

    /// <summary>Creates a converter for a nullable value type.</summary>
    /// <typeparam name="T">The underlying value type.</typeparam>
    /// <returns>The converter.</returns>
    public static NbtConverter<T?> CreateNullableConverter<T>()
        where T : struct =>
        new NbtNullableConverter<T>();

    /// <summary>Creates a converter for a dictionary-backed map or dictionary interface.</summary>
    /// <typeparam name="TCollection">The declared dictionary type.</typeparam>
    /// <typeparam name="TValue">The value type.</typeparam>
    /// <returns>The converter.</returns>
    public static NbtConverter<TCollection> CreateDictionaryBackedConverter<TCollection, TValue>()
        where TCollection : IEnumerable<KeyValuePair<string, TValue>> =>
        new NbtMapConverter<
            NbtDictionaryBuilder<TCollection, TValue>,
            TCollection,
            TValue>();

    /// <summary>Creates a converter for <see cref="Dictionary{TKey,TValue}"/> with string keys.</summary>
    /// <typeparam name="TValue">The dictionary value type.</typeparam>
    /// <returns>The converter.</returns>
    public static NbtConverter<Dictionary<string, TValue>> CreateDictionaryConverter<TValue>() =>
        new NbtMapConverter<
            NbtDictionaryBuilder<Dictionary<string, TValue>, TValue>,
            Dictionary<string, TValue>,
            TValue>();

    /// <summary>Creates a converter for <see cref="IDictionary{TKey,TValue}"/> with string keys.</summary>
    /// <typeparam name="TValue">The dictionary value type.</typeparam>
    /// <returns>The converter.</returns>
    public static NbtConverter<IDictionary<string, TValue>> CreateIDictionaryConverter<TValue>() =>
        new NbtMapConverter<
            NbtDictionaryBuilder<IDictionary<string, TValue>, TValue>,
            IDictionary<string, TValue>,
            TValue>();

    /// <summary>
    /// Creates a converter for <see cref="IReadOnlyDictionary{TKey,TValue}"/> with string keys.
    /// </summary>
    /// <typeparam name="TValue">The dictionary value type.</typeparam>
    /// <returns>The converter.</returns>
    public static NbtConverter<IReadOnlyDictionary<string, TValue>> CreateIReadOnlyDictionaryConverter<TValue>() =>
        new NbtMapConverter<
            NbtDictionaryBuilder<IReadOnlyDictionary<string, TValue>, TValue>,
            IReadOnlyDictionary<string, TValue>,
            TValue>();

    /// <summary>Creates a converter for <see cref="ReadOnlyDictionary{TKey,TValue}"/>.</summary>
    /// <typeparam name="TValue">The dictionary value type.</typeparam>
    /// <returns>The converter.</returns>
    public static NbtConverter<ReadOnlyDictionary<string, TValue>> CreateReadOnlyDictionaryConverter<TValue>() =>
        new NbtMapConverter<
            NbtReadOnlyDictionaryBuilder<TValue>,
            ReadOnlyDictionary<string, TValue>,
            TValue>();

    /// <summary>Creates a converter for an immutable dictionary or dictionary interface.</summary>
    /// <typeparam name="TCollection">The declared dictionary type.</typeparam>
    /// <typeparam name="TValue">The dictionary value type.</typeparam>
    /// <returns>The converter.</returns>
    public static NbtConverter<TCollection> CreateImmutableDictionaryConverter<TCollection, TValue>()
        where TCollection : IEnumerable<KeyValuePair<string, TValue>> =>
        new NbtMapConverter<
            NbtImmutableDictionaryBuilder<TCollection, TValue>,
            TCollection,
            TValue>();

    /// <summary>Creates a converter for <see cref="FrozenDictionary{TKey,TValue}"/>.</summary>
    /// <typeparam name="TValue">The dictionary value type.</typeparam>
    /// <returns>The converter.</returns>
    public static NbtConverter<FrozenDictionary<string, TValue>> CreateFrozenDictionaryConverter<TValue>() =>
        new NbtMapConverter<
            NbtFrozenDictionaryBuilder<TValue>,
            FrozenDictionary<string, TValue>,
            TValue>();

    /// <summary>Reads one value and attaches <paramref name="pathSegment"/> to failures.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="reader">The reader positioned at the value's first token.</param>
    /// <param name="options">The serializer options.</param>
    /// <param name="pathSegment">The path segment for error reporting.</param>
    /// <returns>The decoded value.</returns>
    [RequiresUnreferencedCode("Converter resolution may use reflection or runtime generic construction.")]
    [RequiresDynamicCode("Converter resolution may use runtime generic construction.")]
    public static T ReadValue<T>(
        ref NbtReader reader,
        NbtSerializerOptions options,
        string? pathSegment)
    {
        ArgumentNullException.ThrowIfNull(options);
        var converter = options.GetConverter(typeof(T));
        return converter is NbtConverter<T> typed
            ? NbtSerializer.DeserializeValueCore(ref reader, typed, options, pathSegment)
            : (T)NbtSerializer.DeserializeValueCore(
                ref reader,
                typeof(T),
                converter,
                options,
                pathSegment)!;
    }

    /// <summary>Reads one boolean value encoded as a TAG_Byte with a value of 0 or 1.</summary>
    /// <param name="reader">The reader positioned at the value's first token.</param>
    /// <returns>The decoded boolean.</returns>
    public static bool ReadBoolean(ref NbtReader reader)
    {
        var value = reader.GetSByte();
        return value switch
        {
            0 => false,
            1 => true,
            _ => throw new NbtException($"A TAG_Byte boolean must be 0 or 1 but was {value}.")
            {
                BytePosition = reader.BytesConsumed - sizeof(byte),
                ActualTagType = NbtTagType.Byte,
            },
        };
    }

    /// <summary>Writes one named compound member.</summary>
    /// <typeparam name="T">The member type.</typeparam>
    /// <param name="writer">The writer that receives the member.</param>
    /// <param name="name">The member name.</param>
    /// <param name="value">The member value.</param>
    /// <param name="options">The serializer options.</param>
    [RequiresUnreferencedCode("Converter resolution may use reflection or runtime generic construction.")]
    [RequiresDynamicCode("Converter resolution may use runtime generic construction.")]
    public static void WriteMember<T>(
        ref NbtWriter writer,
        string name,
        T value,
        NbtSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(options);
        if (value is null)
        {
            NbtSerializer.SerializeMemberInternal(ref writer, name, value, typeof(T), options);
            return;
        }

        var converter = options.GetConverter(typeof(T));
        if (converter is NbtConverter<T> typed)
        {
            NbtSerializer.SerializeMemberCore(ref writer, name, value, typed, options);
            return;
        }

        NbtSerializer.SerializeMemberInternal(ref writer, name, value, typeof(T), options);
    }

    /// <summary>Reads one value with an explicitly supplied converter.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="reader">The reader positioned at the value's first token.</param>
    /// <param name="converter">The converter or converter factory for <typeparamref name="T"/>.</param>
    /// <param name="options">The serializer options.</param>
    /// <param name="pathSegment">The path segment for error reporting.</param>
    /// <returns>The decoded value.</returns>
    /// <remarks>The converter must be an <see cref="NbtConverter{T}"/> or a factory that creates one.</remarks>
    [RequiresUnreferencedCode("Converter factories may use reflection or runtime generic construction.")]
    [RequiresDynamicCode("Converter factories may use runtime generic construction.")]
    public static T ReadValueWithConverter<T>(
        ref NbtReader reader,
        NbtConverter converter,
        NbtSerializerOptions options,
        string? pathSegment)
    {
        ArgumentNullException.ThrowIfNull(converter);
        ArgumentNullException.ThrowIfNull(options);
        NbtConverter resolved = converter;
        if (resolved is NbtConverterFactory factory)
        {
            resolved = factory.CreateConverter(typeof(T), options);
        }

        if (resolved is not NbtConverter<T> typed)
        {
            var converterName = GetConverterName(resolved);
            throw new NotSupportedException(
                $"The converter {converterName} does not support {typeof(T)}.");
        }

        return NbtSerializer.DeserializeValueCore(ref reader, typed, options, pathSegment);
    }

    /// <summary>Writes one named compound member with an explicitly supplied converter.</summary>
    /// <typeparam name="T">The member type.</typeparam>
    /// <param name="writer">The writer that receives the member.</param>
    /// <param name="name">The member name.</param>
    /// <param name="value">The member value.</param>
    /// <param name="converter">The converter or converter factory for <typeparamref name="T"/>.</param>
    /// <param name="options">The serializer options.</param>
    /// <remarks>The converter must be an <see cref="NbtConverter{T}"/> or a factory that creates one.</remarks>
    [RequiresUnreferencedCode("Converter factories may use reflection or runtime generic construction.")]
    [RequiresDynamicCode("Converter factories may use runtime generic construction.")]
    public static void WriteMemberWithConverter<T>(
        ref NbtWriter writer,
        string name,
        T value,
        NbtConverter converter,
        NbtSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(converter);
        ArgumentNullException.ThrowIfNull(options);
        if (value is null)
        {
            NbtSerializer.SerializeMemberInternal(ref writer, name, value, typeof(T), options);
            return;
        }

        NbtConverter resolved = converter;
        if (resolved is NbtConverterFactory factory)
        {
            resolved = factory.CreateConverter(typeof(T), options);
        }

        if (resolved is not NbtConverter<T> typed)
        {
            var converterName = GetConverterName(resolved);
            throw new NotSupportedException(
                $"The converter {converterName} does not support {typeof(T)}.");
        }

        NbtSerializer.SerializeMemberCore(ref writer, name, value, typed, options);
    }

    /// <summary>Prepends a path segment to an exception.</summary>
    /// <param name="exception">The exception to update.</param>
    /// <param name="segment">The path segment to prepend, or <see langword="null"/>.</param>
    public static void PrependPath(NbtException exception, string? segment) =>
        NbtSerializer.PrependPath(exception, segment);

    /// <summary>Creates the exception for a required compound member whose value is null.</summary>
    /// <param name="writer">The writer at the current compound member.</param>
    /// <param name="name">The required member name.</param>
    /// <returns>The exception.</returns>
    public static NbtException NullRequiredMember(NbtWriter writer, string name) =>
        new($"The required member '{name}' cannot be null.")
        {
            BytePosition = writer.BytesWritten,
            Path = "." + name,
        };

    /// <summary>Creates the exception for an unexpected tag.</summary>
    /// <param name="reader">The reader at the unexpected token.</param>
    /// <param name="expected">The expected tag type.</param>
    /// <returns>The exception.</returns>
    public static NbtException ExpectedTag(NbtReader reader, NbtTagType expected) =>
        new($"Expected TAG_{expected} but the current token is {reader.TokenType} ({reader.TagType}).")
        {
            BytePosition = reader.BytesConsumed,
            ExpectedTagType = expected,
            ActualTagType = reader.TagType,
        };

    /// <summary>Creates the exception for a compound member without a name.</summary>
    /// <param name="reader">The reader at the unnamed member.</param>
    /// <returns>The exception.</returns>
    public static NbtException MissingMemberName(NbtReader reader) =>
        new("A compound member has no name.")
        {
            BytePosition = reader.BytesConsumed,
        };

    /// <summary>Creates the exception for a duplicate compound member.</summary>
    /// <param name="reader">The reader at the duplicate member.</param>
    /// <param name="name">The duplicate member name.</param>
    /// <returns>The exception.</returns>
    public static NbtException DuplicateMember(NbtReader reader, string name) =>
        new($"The compound contains the duplicate member '{name}'.")
        {
            BytePosition = reader.BytesConsumed,
            Path = "." + name,
        };

    /// <summary>Creates the exception for a missing required compound member.</summary>
    /// <param name="reader">The reader at the end of the compound.</param>
    /// <param name="name">The missing member name.</param>
    /// <returns>The exception.</returns>
    public static NbtException MissingRequiredMember(NbtReader reader, string name) =>
        new($"The compound does not contain the required member '{name}'.")
        {
            BytePosition = reader.BytesConsumed,
            Path = "." + name,
        };

    private static string GetConverterName(NbtConverter? converter) =>
        converter is null ? "null" : converter.GetType().ToString();

    /// <summary>Creates the exception for a compound that was not terminated.</summary>
    /// <param name="reader">The reader at the end of the input.</param>
    /// <returns>The exception.</returns>
    public static NbtException CompoundNotTerminated(NbtReader reader) =>
        new("The compound was not terminated.")
        {
            IsTruncated = true,
            BytePosition = reader.BytesConsumed,
        };
}

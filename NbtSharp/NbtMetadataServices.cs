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

    /// <summary>Creates a converter for <see cref="List{T}"/>.</summary>
    /// <typeparam name="TElement">The element type.</typeparam>
    /// <returns>The converter.</returns>
    public static NbtConverter<List<TElement>> CreateListConverter<TElement>() =>
        new NbtReadOnlyCollectionConverter<List<TElement>, TElement>();

    /// <summary>Creates a converter for <see cref="IReadOnlyList{T}"/>.</summary>
    /// <typeparam name="TElement">The element type.</typeparam>
    /// <returns>The converter.</returns>
    public static NbtConverter<IReadOnlyList<TElement>> CreateReadOnlyListConverter<TElement>() =>
        new NbtReadOnlyCollectionConverter<IReadOnlyList<TElement>, TElement>();

    /// <summary>Creates a converter for <see cref="IReadOnlyCollection{T}"/>.</summary>
    /// <typeparam name="TElement">The element type.</typeparam>
    /// <returns>The converter.</returns>
    public static NbtConverter<IReadOnlyCollection<TElement>> CreateReadOnlyCollectionConverter<TElement>() =>
        new NbtReadOnlyCollectionConverter<IReadOnlyCollection<TElement>, TElement>();

    /// <summary>Creates a converter for a nullable value type.</summary>
    /// <typeparam name="T">The underlying value type.</typeparam>
    /// <returns>The converter.</returns>
    public static NbtConverter<T?> CreateNullableConverter<T>()
        where T : struct =>
        new NbtNullableConverter<T>();

    /// <summary>Creates a converter for <see cref="Dictionary{TKey,TValue}"/> with string keys.</summary>
    /// <typeparam name="TValue">The dictionary value type.</typeparam>
    /// <returns>The converter.</returns>
    public static NbtConverter<Dictionary<string, TValue>> CreateDictionaryConverter<TValue>() =>
        new NbtDictionaryConverter<Dictionary<string, TValue>, TValue>();

    /// <summary>Creates a converter for <see cref="IDictionary{TKey,TValue}"/> with string keys.</summary>
    /// <typeparam name="TValue">The dictionary value type.</typeparam>
    /// <returns>The converter.</returns>
    public static NbtConverter<IDictionary<string, TValue>> CreateIDictionaryConverter<TValue>() =>
        new NbtDictionaryConverter<IDictionary<string, TValue>, TValue>();

    /// <summary>
    /// Creates a converter for <see cref="IReadOnlyDictionary{TKey,TValue}"/> with string keys.
    /// </summary>
    /// <typeparam name="TValue">The dictionary value type.</typeparam>
    /// <returns>The converter.</returns>
    public static NbtConverter<IReadOnlyDictionary<string, TValue>> CreateIReadOnlyDictionaryConverter<TValue>() =>
        new NbtDictionaryConverter<IReadOnlyDictionary<string, TValue>, TValue>();

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
        if (converter is NbtConverterFactory factory)
        {
            converter = factory.CreateConverter(typeof(T), options);
        }

        return converter is NbtConverter<T> typed
            ? NbtSerializer.DeserializeValueCore(ref reader, typed, options, pathSegment)
            : (T)NbtSerializer.DeserializeValueCore(
                ref reader,
                typeof(T),
                converter,
                options,
                pathSegment)!;
    }

    /// <summary>Writes one named compound member with an explicitly supplied converter.</summary>
    /// <typeparam name="T">The member type.</typeparam>
    /// <param name="writer">The writer that receives the member.</param>
    /// <param name="name">The member name.</param>
    /// <param name="value">The member value.</param>
    /// <param name="converter">The converter or converter factory for <typeparamref name="T"/>.</param>
    /// <param name="options">The serializer options.</param>
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

        if (converter is NbtConverterFactory factory)
        {
            converter = factory.CreateConverter(typeof(T), options);
        }

        if (converter is NbtConverter<T> typed)
        {
            NbtSerializer.SerializeMemberCore(ref writer, name, value, typed, options);
            return;
        }

        NbtSerializer.SerializeMemberInternal(ref writer, name, value, typeof(T), options);
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

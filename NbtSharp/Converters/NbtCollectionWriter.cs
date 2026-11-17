using System.Diagnostics.CodeAnalysis;

namespace NbtSharp.Converters;

[UnconditionalSuppressMessage("Aot", "IL2026", Justification = "Reached only through annotated serializer entry points.")]
[UnconditionalSuppressMessage("Aot", "IL3050", Justification = "Reached only through annotated serializer entry points.")]
internal static class NbtCollectionWriter
{
    public static void WriteList<TElement>(
        ref NbtWriter writer,
        IReadOnlyCollection<TElement> value,
        NbtSerializerOptions options)
    {
        var count = value.Count;
        if (count == 0)
        {
            writer.WriteStartList(NbtTagType.End, 0);
            writer.WriteEndList();
            return;
        }

        using var enumerator = value.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            throw new NbtException("The collection reported a nonzero count but produced no elements.");
        }

        var first = enumerator.Current;
        if (first is null)
        {
            throw new NbtException("A list element cannot be null.");
        }

        var converter = options.GetConverter(typeof(TElement));
        var typedConverter = converter as NbtConverter<TElement>;
        var hasTagType = typedConverter is not null
            ? typedConverter.TryGetTagType(first, options, out var elementType)
            : converter.TryGetTagType(first, typeof(TElement), options, out elementType);
        if (!hasTagType)
        {
            throw new NotSupportedException(
                $"The converter for {typeof(TElement)} must implement TryGetTagType to write a non-empty list.");
        }

        writer.WriteStartList(elementType, count);
        try
        {
            if (typedConverter is not null)
            {
                NbtSerializer.SerializeValueCore(
                    ref writer,
                    first,
                    typedConverter,
                    options,
                    pathSegment: null,
                    headerWritten: false);
            }
            else
            {
                NbtSerializer.SerializeValueInternal(
                    ref writer,
                    first,
                    typeof(TElement),
                    options,
                    pathSegment: null,
                    headerWritten: false);
            }
        }
        catch (NbtException ex)
        {
            NbtSerializer.PrependPath(ex, "[0]");
            throw;
        }

        var written = 1;
        while (enumerator.MoveNext())
        {
            try
            {
                if (typedConverter is not null)
                {
                    NbtSerializer.SerializeValueCore(
                        ref writer,
                        enumerator.Current,
                        typedConverter,
                        options,
                        pathSegment: null,
                        headerWritten: false);
                }
                else
                {
                    NbtSerializer.SerializeValueInternal(
                        ref writer,
                        enumerator.Current,
                        typeof(TElement),
                        options,
                        pathSegment: null,
                        headerWritten: false);
                }
            }
            catch (NbtException ex)
            {
                NbtSerializer.PrependPath(ex, $"[{written}]");
                throw;
            }

            written++;
        }

        if (written != count)
        {
            throw new NbtException($"The collection reported {count} elements but produced {written}.")
            {
                BytePosition = writer.BytesWritten,
            };
        }

        writer.WriteEndList();
    }

    public static void WriteEnumerable<TElement>(
        ref NbtWriter writer,
        IEnumerable<TElement> value,
        NbtSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value is IReadOnlyCollection<TElement> collection)
        {
            WriteList(ref writer, collection, options);
            return;
        }

        var materialized = new List<TElement>();
        foreach (var element in value)
        {
            materialized.Add(element);
        }

        WriteList(ref writer, materialized, options);
    }

    public static void WriteMap<TDictionary, TValue>(
        ref NbtWriter writer,
        TDictionary value,
        NbtSerializerOptions options)
        where TDictionary : IEnumerable<KeyValuePair<string, TValue>>
    {
        ArgumentNullException.ThrowIfNull(value);

        writer.WriteStartCompound();
        NbtConverter? converter = null;
        NbtConverter<TValue>? typedConverter = null;
        foreach (var pair in value)
        {
            if (converter is null)
            {
                converter = options.GetConverter(typeof(TValue));
                typedConverter = converter as NbtConverter<TValue>;
            }

            if (typedConverter is not null)
            {
                NbtSerializer.SerializeMemberCore(
                    ref writer,
                    pair.Key,
                    pair.Value,
                    typedConverter,
                    options);
            }
            else
            {
                NbtSerializer.SerializeMemberInternal(
                    ref writer,
                    pair.Key,
                    pair.Value,
                    typeof(TValue),
                    options);
            }
        }

        writer.WriteEndCompound();
    }

    public static void WriteList<TElement>(
        ref NbtWriter writer,
        ReadOnlySpan<TElement> value,
        NbtSerializerOptions options)
    {
        var count = value.Length;
        if (count == 0)
        {
            writer.WriteStartList(NbtTagType.End, 0);
            writer.WriteEndList();
            return;
        }

        var first = value[0];
        if (first is null)
        {
            throw new NbtException("A list element cannot be null.");
        }

        var converter = options.GetConverter(typeof(TElement));
        var typedConverter = converter as NbtConverter<TElement>;
        var hasTagType = typedConverter is not null
            ? typedConverter.TryGetTagType(first, options, out var elementType)
            : converter.TryGetTagType(first, typeof(TElement), options, out elementType);
        if (!hasTagType)
        {
            throw new NotSupportedException(
                $"The converter for {typeof(TElement)} must implement TryGetTagType to write a non-empty list.");
        }

        writer.WriteStartList(elementType, count);
        for (var index = 0; index < count; index++)
        {
            try
            {
                if (typedConverter is not null)
                {
                    NbtSerializer.SerializeValueCore(
                        ref writer,
                        value[index],
                        typedConverter,
                        options,
                        pathSegment: null,
                        headerWritten: false);
                }
                else
                {
                    NbtSerializer.SerializeValueInternal(
                        ref writer,
                        value[index],
                        typeof(TElement),
                        options,
                        pathSegment: null,
                        headerWritten: false);
                }
            }
            catch (NbtException ex)
            {
                NbtSerializer.PrependPath(ex, $"[{index}]");
                throw;
            }
        }

        writer.WriteEndList();
    }
}

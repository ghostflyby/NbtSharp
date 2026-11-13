using System.Buffers;
using System.Diagnostics.CodeAnalysis;

namespace NbtSharp.Converters;

[UnconditionalSuppressMessage("Aot", "IL2026", Justification = "Reached only through annotated serializer entry points.")]
[UnconditionalSuppressMessage("Aot", "IL3050", Justification = "Reached only through annotated serializer entry points.")]
internal static class NbtCollectionWriter
{
    public static int TryGetCount<TElement>(IEnumerable<TElement> value)
    {
        return value switch
        {
            ICollection<TElement> collection => collection.Count,
            IReadOnlyCollection<TElement> collection => collection.Count,
            _ => -1,
        };
    }

    public static void WriteList<TElement>(ref NbtWriter writer, IEnumerable<TElement> value, int count, NbtSerializerOptions options)
    {
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
        if (converter is NbtConverter<TElement> typed && typed.TryGetTagType(first, options, out var elementType))
        {
            writer.WriteStartList(elementType, count);
            NbtSerializer.SerializeValueInternal(ref writer, first, typeof(TElement), options, "[0]");
            var written = 1;
            while (enumerator.MoveNext())
            {
                NbtSerializer.SerializeValueInternal(
                    ref writer,
                    enumerator.Current,
                    typeof(TElement),
                    options,
                    $"[{written}]");
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
            return;
        }

        var buffer = new ArrayBufferWriter<byte>();
        var probe = new NbtWriter(buffer, options.CreateWriterOptions(), payloadOnly: true);
        NbtSerializer.SerializeValueInternal(ref probe, first, typeof(TElement), options, "[0]");
        probe.Complete();

        var probedType = probe.RootTagType;
        writer.WriteStartList(probedType, count);
        writer.WriteBufferedListElement(probedType, buffer.WrittenSpan);

        var total = 1;
        while (enumerator.MoveNext())
        {
            NbtSerializer.SerializeValueInternal(
                ref writer,
                enumerator.Current,
                typeof(TElement),
                options,
                $"[{total}]");
            total++;
        }

        if (total != count)
        {
            throw new NbtException($"The collection reported {count} elements but produced {total}.")
            {
                BytePosition = writer.BytesWritten,
            };
        }

        writer.WriteEndList();
    }
}

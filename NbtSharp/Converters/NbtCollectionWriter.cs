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
        if (!converter.TryGetTagType(first, typeof(TElement), options, out var elementType))
        {
            throw new NotSupportedException(
                $"The converter for {typeof(TElement)} must implement TryGetTagType to write a non-empty list.");
        }

        writer.WriteStartList(elementType, count);
        NbtSerializer.SerializeValueInternal(ref writer, first, typeof(TElement), options, "[0]", headerWritten: false);
        var written = 1;
        while (enumerator.MoveNext())
        {
            NbtSerializer.SerializeValueInternal(
                ref writer,
                enumerator.Current,
                typeof(TElement),
                options,
                $"[{written}]",
                headerWritten: false);
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
}

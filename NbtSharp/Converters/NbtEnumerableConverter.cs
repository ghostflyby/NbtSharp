using System.Diagnostics.CodeAnalysis;

namespace NbtSharp.Converters;

/// <summary>
/// Converts generic collections and collection interfaces into NBT lists.
/// </summary>
[UnconditionalSuppressMessage("Aot", "IL2026", Justification = "Reached only through annotated serializer entry points.")]
[UnconditionalSuppressMessage("Aot", "IL3050", Justification = "Reached only through annotated serializer entry points.")]
internal sealed class NbtEnumerableConverter<TCollection, TElement> : NbtConverter<TCollection>
    where TCollection : IEnumerable<TElement>
{
    public override TCollection Read(ref NbtReader reader, NbtSerializerOptions options) =>
        (TCollection)(object)NbtCollectionReader.ReadList<TElement>(ref reader, options);

    public override void Write(ref NbtWriter writer, TCollection value, NbtSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);

        var count = NbtCollectionWriter.TryGetCount(value);
        if (count >= 0)
        {
            NbtCollectionWriter.WriteList(ref writer, value, count, options);
            return;
        }

        var buffered = new List<TElement>();
        foreach (var item in value)
        {
            if (buffered.Count >= options.EffectiveMaxCollectionLength)
            {
                throw new NbtException(
                    $"The collection exceeds the configured limit of {options.EffectiveMaxCollectionLength} elements.");
            }

            buffered.Add(item);
        }

        NbtCollectionWriter.WriteList(ref writer, buffered, buffered.Count, options);
    }

    public override bool TryGetTagType(TCollection value, NbtSerializerOptions options, out NbtTagType tagType)
    {
        tagType = NbtTagType.List;
        return true;
    }
}

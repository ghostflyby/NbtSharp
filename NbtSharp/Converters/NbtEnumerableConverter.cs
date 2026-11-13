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

        if (value is not IReadOnlyCollection<TElement> collection)
        {
            throw new NotSupportedException(
                $"Writing {typeof(TCollection)} requires the runtime value to implement " +
                $"IReadOnlyCollection<{typeof(TElement)}> so the list length is known. " +
                "Materialize the sequence or provide a custom converter.");
        }

        NbtCollectionWriter.WriteList(ref writer, collection, options);
    }

    public override bool TryGetTagType(TCollection value, NbtSerializerOptions options, out NbtTagType tagType)
    {
        tagType = NbtTagType.List;
        return true;
    }
}

namespace NbtSharp.Converters;

/// <summary>
/// Converts a sized read-only collection or list into an NBT list.
/// </summary>
internal sealed class NbtReadOnlyCollectionConverter<TCollection, TElement> : NbtConverter<TCollection>
    where TCollection : IReadOnlyCollection<TElement>
{
    public override TCollection Read(ref NbtReader reader, NbtSerializerOptions options) =>
        (TCollection)(object)NbtCollectionReader.ReadList<TElement>(ref reader, options);

    public override void Write(ref NbtWriter writer, TCollection value, NbtSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);
        NbtCollectionWriter.WriteList(ref writer, value, options);
    }

    public override bool TryGetTagType(TCollection value, NbtSerializerOptions options, out NbtTagType tagType)
    {
        tagType = NbtTagType.List;
        return true;
    }
}

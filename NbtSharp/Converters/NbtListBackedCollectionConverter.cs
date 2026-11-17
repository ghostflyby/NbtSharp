namespace NbtSharp.Converters;

/// <summary>
/// Converts a sequence collection into a list-backed target. The target may be <see cref="List{T}"/>
/// or any interface it implements.
/// </summary>
internal sealed class NbtListBackedCollectionConverter<TCollection, TElement> : NbtConverter<TCollection>
    where TCollection : IEnumerable<TElement>
{
    public override TCollection Read(ref NbtReader reader, NbtSerializerOptions options) =>
        (TCollection)(object)NbtCollectionReader.ReadList<TElement>(ref reader, options);

    public override void Write(ref NbtWriter writer, TCollection value, NbtSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);
        NbtCollectionWriter.WriteEnumerable(ref writer, value, options);
    }

    public override bool TryGetTagType(TCollection value, NbtSerializerOptions options, out NbtTagType tagType)
    {
        tagType = NbtTagType.List;
        return true;
    }
}

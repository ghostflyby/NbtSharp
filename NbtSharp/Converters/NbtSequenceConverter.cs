using System.Collections.Immutable;

namespace NbtSharp.Converters;

/// <summary>
/// Reads a list into a collection produced by <typeparamref name="TBuilder"/> and writes the
/// collection as a list.
/// </summary>
internal sealed class NbtSequenceConverter<TCollection, TElement, TBuilder> :
    NbtConverter<TCollection>
    where TCollection : IEnumerable<TElement>
    where TBuilder : struct, INbtSequenceBuilder<TCollection, TElement>
{
    public override TCollection Read(ref NbtReader reader, NbtSerializerOptions options) =>
        NbtCollectionReader.ReadCollection<TBuilder, TCollection, TElement>(ref reader, options);

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
}

/// <summary>
/// Reads a list into <see cref="ImmutableArray{T}"/> and writes it without boxing the array.
/// </summary>
internal sealed class NbtImmutableArrayConverter<TElement> :
    NbtConverter<ImmutableArray<TElement>>
{
    public override ImmutableArray<TElement> Read(
        ref NbtReader reader,
        NbtSerializerOptions options) =>
        NbtCollectionReader.ReadCollection<
            NbtImmutableArrayBuilder<TElement>,
            ImmutableArray<TElement>,
            TElement>(ref reader, options);

    public override void Write(
        ref NbtWriter writer,
        ImmutableArray<TElement> value,
        NbtSerializerOptions options) =>
        NbtCollectionWriter.WriteList(ref writer, value.AsSpan(), options);

    public override bool TryGetTagType(
        ImmutableArray<TElement> value,
        NbtSerializerOptions options,
        out NbtTagType tagType)
    {
        tagType = NbtTagType.List;
        return true;
    }
}

namespace NbtSharp.Converters;

/// <summary>
/// Signals that a collection type's only usable construction factory accepts a span, which runtime
/// reflection cannot invoke. The message points callers at source generation or a custom converter.
/// </summary>
internal sealed class NbtSpanCollectionBuilderException : NotSupportedException
{
    public NbtSpanCollectionBuilderException(string factory, Type collectionType)
        : base(
            $"The CollectionBuilder factory '{factory}' for {collectionType} accepts a span, " +
            "which cannot be invoked through runtime reflection. Use the NbtSharp source " +
            "generator or register a custom converter for this type.")
    {
    }
}

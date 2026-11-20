namespace NbtSharp.Converters;

internal delegate TCollection NbtReadOnlySpanFactory<TCollection, TItem>(ReadOnlySpan<TItem> items);

internal delegate TCollection NbtSpanFactory<TCollection, TItem>(Span<TItem> items);

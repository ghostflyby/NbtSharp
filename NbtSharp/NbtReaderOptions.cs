namespace NbtSharp;

/// <summary>
/// Options that control a standalone <see cref="NbtReader"/>.
/// </summary>
/// <remarks>
/// A property value of 0 selects the reader default, except <see cref="MaxDocumentBytes"/> where 0
/// means that the document size is not limited.
/// </remarks>
public readonly struct NbtReaderOptions
{
    /// <summary>
    /// Gets whether the root tag carries a name. The default is <see cref="NbtRootFormat.Named"/>.
    /// </summary>
    public NbtRootFormat RootFormat { get; init; }

    /// <summary>
    /// Gets the maximum nesting depth. 0 selects the default of 512.
    /// </summary>
    public int MaxDepth { get; init; }

    /// <summary>
    /// Gets the maximum number of elements in a list or array. 0 selects the default of 16,777,216.
    /// </summary>
    public int MaxCollectionLength { get; init; }

    /// <summary>
    /// Gets the maximum document size in bytes. 0 means that the size is not limited.
    /// </summary>
    public long MaxDocumentBytes { get; init; }
}

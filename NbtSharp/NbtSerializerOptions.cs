namespace NbtSharp;

/// <summary>
/// Options that control how NBT documents are read and written.
/// </summary>
public sealed class NbtSerializerOptions
{
    /// <summary>
    /// Gets or sets whether the root tag carries a name. The default is <see cref="NbtRootFormat.Named"/>.
    /// </summary>
    public NbtRootFormat RootFormat { get; init; } = NbtRootFormat.Named;
}

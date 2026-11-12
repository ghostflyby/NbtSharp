namespace NbtSharp;

/// <summary>
/// Describes whether an NBT root tag carries a name.
/// </summary>
public enum NbtRootFormat
{
    /// <summary>The root tag is encoded as tag type, name length, name, then payload.</summary>
    Named,

    /// <summary>The root tag is encoded as tag type followed directly by its payload.</summary>
    Unnamed,
}

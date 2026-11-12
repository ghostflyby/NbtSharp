namespace NbtSharp;

/// <summary>
/// Identifies the kind of token produced by <see cref="NbtReader"/>.
/// </summary>
public enum NbtTokenType
{
    /// <summary>The start of a compound tag.</summary>
    StartCompound,

    /// <summary>The end of a compound tag.</summary>
    EndCompound,

    /// <summary>The start of a list tag. The element type and count are available before reading elements.</summary>
    StartList,

    /// <summary>The end of a list tag.</summary>
    EndList,

    /// <summary>A scalar, array, or string tag whose value is available through the typed getters.</summary>
    Value,
}

namespace NbtSharp;

/// <summary>
/// Represents a complete NBT document: a root tag name and its value.
/// </summary>
/// <remarks>
/// A <see langword="null"/> <see cref="Name"/> means the document uses the unnamed root format, where
/// no name is encoded at all. An empty string is a valid named root whose name has zero length.
/// </remarks>
/// <param name="Name">The root tag name, or <see langword="null"/> for an unnamed root.</param>
/// <param name="Value">The root tag value.</param>
public readonly record struct NbtRoot(string? Name, NbtValue Value);

namespace NbtSharp;

/// <summary>
/// Controls how a <see langword="null"/> value is handled when it appears as a compound member.
/// </summary>
/// <remarks>
/// NBT has no null tag. A null root value or a null list element is always an error; this option
/// only applies to named compound members.
/// </remarks>
public enum NbtNullValueHandling
{
    /// <summary>Omit a compound member whose value is <see langword="null"/>.</summary>
    Omit,

    /// <summary>Throw <see cref="NbtException"/> when a compound member value is <see langword="null"/>.</summary>
    Throw,
}

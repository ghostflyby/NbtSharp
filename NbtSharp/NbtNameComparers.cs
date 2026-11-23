namespace NbtSharp;

/// <summary>
/// An equality comparer for modified UTF-8 name bytes.
/// </summary>
/// <remarks>
/// <para>
/// Byte sequences are compared by the decoded name, so raw nulls, overlong forms, and canonical
/// encodings of the same name are equal. The hash is computed from the decoded characters with
/// <see cref="string.GetHashCode(ReadOnlySpan{char})"/>.
/// </para>
/// <para>
/// Invalid modified UTF-8 throws <see cref="NbtException"/>.
/// </para>
/// </remarks>
public readonly struct NbtNameByteComparer : IEqualityComparer<ReadOnlySpan<byte>>
{
    /// <summary>The shared comparer instance.</summary>
    public static readonly NbtNameByteComparer Instance = default;

    /// <inheritdoc/>
    public bool Equals(ReadOnlySpan<byte> x, ReadOnlySpan<byte> y) => NbtText.NameEquals(x, y, 0);

    /// <inheritdoc/>
    public int GetHashCode(ReadOnlySpan<byte> obj) => NbtText.GetNameHashCode(obj);
}

/// <summary>
/// An alternate equality comparer that compares decoded name characters with modified UTF-8 name
/// bytes.
/// </summary>
/// <remarks>
/// <para>
/// The alternate side is a character span; the key side is the wire name bytes. Both sides are
/// compared by decoded value, and the character hash matches the byte hash because both use
/// <see cref="string.GetHashCode(ReadOnlySpan{char})"/>.
/// </para>
/// <para>
/// Invalid modified UTF-8 throws <see cref="NbtException"/>. A byte span cannot be created from a
/// character span, so <see cref="Create(ReadOnlySpan{char})"/> throws
/// <see cref="NotSupportedException"/>; this comparer is intended for lookups.
/// </para>
/// </remarks>
public readonly struct NbtNameCharComparer :
    IAlternateEqualityComparer<ReadOnlySpan<char>, ReadOnlySpan<byte>>
{
    /// <summary>The shared comparer instance.</summary>
    public static readonly NbtNameCharComparer Instance = default;

    /// <inheritdoc/>
    public bool Equals(ReadOnlySpan<char> alternate, ReadOnlySpan<byte> other) =>
        NbtText.NameEquals(other, alternate, 0);

    /// <inheritdoc/>
    public int GetHashCode(ReadOnlySpan<char> alternate) => string.GetHashCode(alternate);

    /// <inheritdoc/>
    public ReadOnlySpan<byte> Create(ReadOnlySpan<char> alternate) =>
        throw new NotSupportedException(
            "A modified UTF-8 byte span cannot be created from a character span.");
}

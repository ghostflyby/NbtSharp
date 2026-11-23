using System.Collections.Generic;

namespace NbtSharp;

/// <summary>
/// An ordinal name comparer for string-keyed dictionaries that also supports alternate lookup by
/// decoded name characters.
/// </summary>
/// <remarks>
/// <para>
/// The comparer implements both <see cref="IEqualityComparer{T}"/> over <see cref="string"/> and
/// <see cref="IAlternateEqualityComparer{TAlternate,T}"/> over <see cref="ReadOnlySpan{T}"/> of
/// characters, so a dictionary created with it accepts
/// <c>GetAlternateLookup&lt;ReadOnlySpan&lt;char&gt;&gt;()</c>.
/// </para>
/// <para>
/// Names are compared ordinally, and hashes use <see cref="string.GetHashCode(ReadOnlySpan{char})"/>
/// so the string and span forms produce identical hash codes.
/// </para>
/// </remarks>
public readonly struct NbtNameComparer :
    IEqualityComparer<string>,
    IAlternateEqualityComparer<ReadOnlySpan<char>, string>
{
    /// <summary>The shared comparer instance.</summary>
    public static readonly NbtNameComparer Instance = default;

    /// <inheritdoc/>
    public bool Equals(string? x, string? y) => string.Equals(x, y, StringComparison.Ordinal);

    /// <inheritdoc/>
    public int GetHashCode(string obj) => string.GetHashCode(obj.AsSpan());

    /// <inheritdoc/>
    public bool Equals(ReadOnlySpan<char> alternate, string other) =>
        alternate.SequenceEqual(other.AsSpan());

    /// <inheritdoc/>
    public int GetHashCode(ReadOnlySpan<char> alternate) => string.GetHashCode(alternate);

    /// <inheritdoc/>
    public string Create(ReadOnlySpan<char> alternate) => new(alternate);
}

/// <summary>
/// An ordinal name comparer for string-keyed dictionaries that also supports alternate lookup by
/// modified UTF-8 name bytes.
/// </summary>
/// <remarks>
/// <para>
/// The comparer implements both <see cref="IEqualityComparer{T}"/> over <see cref="string"/> and
/// <see cref="IAlternateEqualityComparer{TAlternate,T}"/> over <see cref="ReadOnlySpan{T}"/> of
/// bytes, so a dictionary created with it accepts
/// <c>GetAlternateLookup&lt;ReadOnlySpan&lt;byte&gt;&gt;()</c>. Alternate lookups compare the
/// decoded name, so raw nulls, overlong forms, and canonical encodings of the same name match.
/// </para>
/// <para>
/// Hashes are computed from the decoded characters with
/// <see cref="string.GetHashCode(ReadOnlySpan{char})"/>, so the byte and string forms produce
/// identical hash codes. Invalid modified UTF-8 throws <see cref="NbtException"/>.
/// </para>
/// </remarks>
public readonly struct NbtUtf8NameComparer :
    IEqualityComparer<string>,
    IAlternateEqualityComparer<ReadOnlySpan<byte>, string>
{
    /// <summary>The shared comparer instance.</summary>
    public static readonly NbtUtf8NameComparer Instance = default;

    /// <inheritdoc/>
    public bool Equals(string? x, string? y) => string.Equals(x, y, StringComparison.Ordinal);

    /// <inheritdoc/>
    public int GetHashCode(string obj) => string.GetHashCode(obj.AsSpan());

    /// <inheritdoc/>
    public bool Equals(ReadOnlySpan<byte> alternate, string other) =>
        NbtText.NameEquals(alternate, other.AsSpan(), 0);

    /// <inheritdoc/>
    public int GetHashCode(ReadOnlySpan<byte> alternate) => NbtText.GetNameHashCode(alternate);

    /// <inheritdoc/>
    public string Create(ReadOnlySpan<byte> alternate) => NbtText.DecodeAt(alternate, 0);
}

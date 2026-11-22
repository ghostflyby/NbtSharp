namespace NbtSharp;

/// <summary>
/// Marks an enum whose values are serialized as a TAG_String of the member name instead of an
/// integer tag.
/// </summary>
/// <remarks>
/// <para>
/// Both the reflection resolver and the source generator route enums marked with this attribute
/// through <see cref="NbtStringEnumConverter{TEnum}"/>, so nullable enums, collection elements, map
/// values, object members, and root values all inherit the string representation. Enums without the
/// attribute keep the numeric representation.
/// </para>
/// <para>
/// In trimmed applications, applying the attribute to an enum used only through the reflection path
/// may not preserve the attribute through trimming, in which case the enum silently falls back to
/// the numeric representation; the generated path sees the attribute at compile time and is
/// unaffected.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Enum)]
public sealed class NbtStringEnumAttribute : Attribute;

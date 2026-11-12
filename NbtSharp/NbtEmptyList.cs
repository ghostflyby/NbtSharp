namespace NbtSharp;

/// <summary>
/// Represents an NBT list whose element type is <see cref="NbtTagType.End"/> and whose length is zero.
/// </summary>
/// <remarks>
/// <para>
/// A list normally records its element type even when it is empty, so an empty <c>List&lt;int&gt;</c>
/// encodes as <c>TAG_List&lt;TAG_Int&gt;</c> with length 0. This type covers the distinct
/// <c>TAG_List&lt;TAG_End&gt;</c> encoding, which carries no element type.
/// </para>
/// <para>
/// The only valid length for this representation is zero; <see cref="NbtList.Count"/> always
/// returns 0 for it.
/// </para>
/// </remarks>
public readonly struct NbtEmptyList;

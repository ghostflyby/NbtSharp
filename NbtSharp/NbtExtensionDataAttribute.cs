namespace NbtSharp;

/// <summary>
/// Designates the member that captures compound members unknown to the mapping.
/// </summary>
/// <remarks>
/// <para>
/// The marked member receives every compound member that no mapped member claims on read, and its
/// entries are appended after the mapped members on write. The member has no NBT name of its own:
/// it is never written as a named member, and it does not participate in constructor binding.
/// </para>
/// <para>
/// The member type must be exactly <see cref="NbtCompound"/> or
/// <see cref="Dictionary{TKey,TValue}"/> of <see cref="string"/> and <see cref="NbtValue"/>, and a
/// type may declare only one such member (including inherited declarations). The member must have
/// a setter and must not be <see langword="required"/>. It cannot be combined with
/// <see cref="NbtPropertyNameAttribute"/> or <see cref="NbtUseConverterAttribute"/>. Fields also
/// require <see cref="NbtIncludeAttribute"/> to participate in the mapping.
/// </para>
/// <para>
/// Invalid declarations report <c>NBTSG010</c> in generated contexts and throw
/// <see cref="NotSupportedException"/> from the reflection resolver.
/// </para>
/// </remarks>
[AttributeUsage(
    AttributeTargets.Property | AttributeTargets.Field,
    AllowMultiple = false,
    Inherited = true)]
public sealed class NbtExtensionDataAttribute : Attribute;

// The public NbtSharp namespace is intentional; the folder groups internal implementation.
// ReSharper disable once CheckNamespace
namespace NbtSharp;

/// <summary>
/// The mutable contract of one CLR type, passed to <see cref="NbtReflectionTypeInfoResolver"/>
/// modifier callbacks before the mapping is cached.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Members"/> lists the discovered properties and included fields in declaration order,
/// with their NBT names already reflecting <see cref="NbtPropertyNameAttribute"/> and
/// <see cref="NbtSerializerOptions.PropertyNamingPolicy"/>. Members cannot be added or removed;
/// use <see cref="NbtMemberInfo.Ignore"/> to exclude one. Extension-data members are not part of
/// the list.
/// </para>
/// <para>
/// After the modifier callbacks of a resolution run, the contract is frozen and every setter of
/// every member throws <see cref="InvalidOperationException"/>.
/// </para>
/// </remarks>
public sealed class NbtContractInfo
{
    private readonly NbtMemberInfo[] _members;
    private readonly IReadOnlyList<NbtMemberInfo> _memberView;

    internal NbtContractInfo(Type type, NbtMemberInfo[] members)
    {
        Type = type;
        _members = members;
        _memberView = Array.AsReadOnly(members);
    }

    /// <summary>Gets the CLR type this contract maps.</summary>
    public Type Type { get; }

    /// <summary>
    /// Gets the mapped members in declaration order. The list is read-only; adding, removing, or
    /// replacing entries throws <see cref="NotSupportedException"/>.
    /// </summary>
    public IReadOnlyList<NbtMemberInfo> Members => _memberView;

    /// <summary>
    /// Gets the member whose <see cref="NbtMemberInfo.ClrName"/> equals
    /// <paramref name="clrName"/> using an ordinal comparison.
    /// </summary>
    /// <param name="clrName">The CLR name of the property or field.</param>
    /// <returns>The matching member, or <see langword="null"/> when the name is unknown.</returns>
    public NbtMemberInfo? GetMember(string clrName)
    {
        ArgumentNullException.ThrowIfNull(clrName);
        foreach (var member in _members)
        {
            if (string.Equals(member.ClrName, clrName, StringComparison.Ordinal))
            {
                return member;
            }
        }

        return null;
    }

    internal void Freeze()
    {
        foreach (var member in _members)
        {
            member.Freeze();
        }
    }
}

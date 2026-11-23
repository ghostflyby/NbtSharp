using System.Collections;

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
public sealed class NbtContractInfo : IReadOnlyDictionary<string, NbtMemberInfo>
{
    private readonly NbtMemberInfo[] _members;
    private readonly Dictionary<string, NbtMemberInfo> _byClrName;

    internal NbtContractInfo(Type type, NbtMemberInfo[] members)
    {
        Type = type;
        _members = members;
        Members = Array.AsReadOnly(members);
        _byClrName = new Dictionary<string, NbtMemberInfo>(members.Length, StringComparer.Ordinal);
        foreach (var member in members)
        {
            _byClrName.TryAdd(member.ClrName, member);
        }
    }

    /// <summary>Gets the CLR type this contract maps.</summary>
    public Type Type { get; }

    /// <summary>
    /// Gets the mapped members in declaration order. The list is read-only; adding, removing, or
    /// replacing entries throws <see cref="NotSupportedException"/>.
    /// </summary>
    public IReadOnlyList<NbtMemberInfo> Members { get; }

    /// <summary>Gets the number of distinct CLR member names.</summary>
    public int Count => _byClrName.Count;

    /// <summary>Gets the member with the specified CLR name.</summary>
    /// <param name="key">The CLR name of the property or field.</param>
    /// <returns>The matching member.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="KeyNotFoundException">No member has the specified CLR name.</exception>
    public NbtMemberInfo this[string key] => _byClrName[key];

    /// <summary>Gets the member with the specified CLR name, when present.</summary>
    /// <param name="key">The CLR name of the property or field.</param>
    /// <param name="value">The matching member, or a default value when absent.</param>
    /// <returns><see langword="true"/> when the member was found.</returns>
    public bool TryGetValue(string key, out NbtMemberInfo value) =>
        _byClrName.TryGetValue(key, out value!);

    IEnumerable<string> IReadOnlyDictionary<string, NbtMemberInfo>.Keys => _byClrName.Keys;

    IEnumerable<NbtMemberInfo> IReadOnlyDictionary<string, NbtMemberInfo>.Values => _byClrName.Values;

    bool IReadOnlyDictionary<string, NbtMemberInfo>.ContainsKey(string key) =>
        _byClrName.ContainsKey(key);

    IEnumerator<KeyValuePair<string, NbtMemberInfo>> IEnumerable<KeyValuePair<string, NbtMemberInfo>>.GetEnumerator() =>
        _byClrName.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() =>
        ((IEnumerable<KeyValuePair<string, NbtMemberInfo>>)this).GetEnumerator();

    internal void Freeze()
    {
        foreach (var member in _members)
        {
            member.Freeze();
        }
    }
}

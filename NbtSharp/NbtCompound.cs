namespace NbtSharp;

/// <summary>
/// Represents an NBT compound tab: a heterogeneous collection of named values.
/// </summary>
/// <remarks>
/// <para>
/// Members are stored in a dictionary keyed by name. Enumeration follows insertion order as an
/// implementation detail of <see cref="Dictionary{TKey,TValue}"/>; the NBT format does not define
/// a member order. When a binary compound contains a duplicate name, the last occurrence wins.
/// </para>
/// </remarks>
public sealed class NbtCompound : Dictionary<string, NbtValue>
{
    /// <summary>Initializes an empty compound.</summary>
    public NbtCompound()
    {
    }

    /// <summary>Initializes an empty compound with the specified initial capacity.</summary>
    /// <param name="capacity">The initial number of members the compound can hold without resizing.</param>
    public NbtCompound(int capacity)
        : base(capacity)
    {
    }

    /// <summary>Initializes a compound that copies the members of another collection.</summary>
    /// <param name="collection">The members to copy.</param>
    public NbtCompound(IEnumerable<KeyValuePair<string, NbtValue>> collection)
        : base(collection)
    {
    }
}

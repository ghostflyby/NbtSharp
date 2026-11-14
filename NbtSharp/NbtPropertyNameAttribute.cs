namespace NbtSharp;

/// <summary>
/// Overrides the NBT member name used by reflection-based object mapping.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class NbtPropertyNameAttribute : Attribute
{
    /// <summary>Initializes the attribute with the NBT member name.</summary>
    /// <param name="name">The name written to and read from the compound.</param>
    public NbtPropertyNameAttribute(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
    }

    /// <summary>Gets the NBT member name.</summary>
    public string Name { get; }
}

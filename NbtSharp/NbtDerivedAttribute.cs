namespace NbtSharp;

/// <summary>
/// Registers a concrete derived type for polymorphic serialization of an abstract base class or
/// interface.
/// </summary>
/// <remarks>
/// <para>
/// Apply one or more instances to the base type. A base-typed value is written as a compound with a
/// <c>$type</c> discriminator and a <c>$value</c> compound containing the derived members:
/// <c>{ "$type": "Cat", "$value": { ... } }</c>.
/// </para>
/// <para>
/// The discriminator defaults to the derived type's short name. Set
/// <see cref="TypeDiscriminator"/> when short names would collide. The base must be a non-generic
/// abstract class or interface. Derived registrations must be closed concrete classes or structs
/// that are assignable to the base, are not collection shapes, and are not themselves polymorphic.
/// Discriminators and derived types must be unique within one base.
/// </para>
/// </remarks>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Interface,
    AllowMultiple = true,
    Inherited = false)]
public sealed class NbtDerivedAttribute : Attribute
{
    /// <summary>Initializes the registration for <paramref name="derivedType"/>.</summary>
    /// <param name="derivedType">A concrete type assignable to the attributed base type.</param>
    public NbtDerivedAttribute(Type derivedType)
    {
        ArgumentNullException.ThrowIfNull(derivedType);
        DerivedType = derivedType;
    }

    /// <summary>Gets the registered concrete type.</summary>
    public Type DerivedType { get; }

    /// <summary>Gets or initializes the discriminator written to <c>$type</c>.</summary>
    /// <remarks>When <see langword="null"/>, the derived type's short name is used.</remarks>
    public string? TypeDiscriminator { get; init; }
}

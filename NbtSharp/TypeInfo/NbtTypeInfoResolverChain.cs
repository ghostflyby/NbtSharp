// The public NbtSharp namespace is intentional; the folder groups internal implementation.
// ReSharper disable once CheckNamespace
namespace NbtSharp;

/// <summary>
/// Combines multiple <see cref="INbtTypeInfoResolver"/> instances and consults them in order.
/// </summary>
/// <remarks>
/// <para>
/// The first resolver that returns a non-null <see cref="NbtTypeInfo"/> wins. This makes it possible
/// to use a generated context as the primary resolver and a reflection resolver as a migration
/// fallback for types that are not part of the generated context.
/// </para>
/// <para>
/// A chain affects converter resolution through <see cref="NbtSerializerOptions"/>; the type-info
/// overloads use the options bound to the returned <see cref="NbtTypeInfo"/>, which for a generated
/// context is the context's own <see cref="NbtSerializerContext.Options"/>.
/// </para>
/// <para>
/// The chain is immutable after construction. At least one resolver is required.
/// </para>
/// </remarks>
public sealed class NbtTypeInfoResolverChain : INbtTypeInfoResolver
{
    private readonly INbtTypeInfoResolver[] _resolvers;
    private readonly IReadOnlyList<INbtTypeInfoResolver> _resolverView;

    /// <summary>Initializes a resolver chain consulted in the given order.</summary>
    /// <param name="resolvers">The resolvers, from highest to lowest precedence.</param>
    /// <exception cref="ArgumentException"><paramref name="resolvers"/> is empty.</exception>
    /// <exception cref="ArgumentNullException">The array or one of its elements is null.</exception>
    public NbtTypeInfoResolverChain(params INbtTypeInfoResolver[] resolvers)
    {
        ArgumentNullException.ThrowIfNull(resolvers);
        if (resolvers.Length == 0)
        {
            throw new ArgumentException("At least one resolver is required.", nameof(resolvers));
        }

        _resolvers = (INbtTypeInfoResolver[])resolvers.Clone();
        foreach (var resolver in _resolvers)
        {
            ArgumentNullException.ThrowIfNull(resolver);
        }

        _resolverView = Array.AsReadOnly(_resolvers);
    }

    /// <summary>Gets a read-only view of the resolvers in precedence order.</summary>
    public IReadOnlyList<INbtTypeInfoResolver> Resolvers => _resolverView;

    /// <inheritdoc/>
    public NbtTypeInfo? GetTypeInfo(Type type, NbtSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(options);

        foreach (var resolver in _resolvers)
        {
            var typeInfo = resolver.GetTypeInfo(type, options);
            if (typeInfo is null)
            {
                continue;
            }

            if (typeInfo.Type != type)
            {
                throw new InvalidOperationException(
                    $"The resolver '{resolver.GetType()}' returned type information for " +
                    $"'{typeInfo.Type}' when '{type}' was requested.");
            }

            return typeInfo;
        }

        return null;
    }
}

namespace NbtSharp;

/// <summary>
/// Supplies source-generated type information to an <see cref="NbtSerializerOptions"/> instance.
/// </summary>
/// <remarks>
/// <para>
/// Derive a partial class from this type and mark it with one or more
/// <see cref="NbtSerializableAttribute"/> instances. The source generator emits the type
/// information, converters, and a <c>Default</c> singleton.
/// </para>
/// <para>
/// The inherited <see cref="Options"/> property binds the context as the type information resolver
/// of a copy of the options passed to the constructor. When no options are passed, the context
/// creates default options.
/// </para>
/// </remarks>
public abstract class NbtSerializerContext : INbtTypeInfoResolver
{
    /// <summary>Initializes the context with default options.</summary>
    protected NbtSerializerContext()
        : this(options: null)
    {
    }

    /// <summary>Initializes the context with a copy of <paramref name="options"/>.</summary>
    /// <param name="options">The options to copy, or <see langword="null"/> for default options.</param>
    protected NbtSerializerContext(NbtSerializerOptions? options)
    {
        Options = options is null
            ? CreateDefaultOptions()
            : options.WithTypeInfoResolver(this);
    }

    /// <summary>Gets the options bound to this context.</summary>
    public NbtSerializerOptions Options { get; }

    /// <summary>Creates the options used when the constructor receives no explicit options.</summary>
    /// <returns>The default options bound to this context.</returns>
    /// <remarks>
    /// The base constructor calls this method, so an override must not depend on derived instance
    /// state that is initialized after the base constructor runs.
    /// </remarks>
    protected virtual NbtSerializerOptions CreateDefaultOptions() =>
        new() { TypeInfoResolver = this };

    /// <inheritdoc/>
    public abstract NbtTypeInfo? GetTypeInfo(Type type, NbtSerializerOptions options);
}

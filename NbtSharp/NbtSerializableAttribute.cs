namespace NbtSharp;

/// <summary>
/// Requests source-generated NBT type information for <see cref="Type"/> on a partial
/// <see cref="NbtSerializerContext"/>.
/// </summary>
/// <remarks>
/// The type and every type reachable from it must be supported by the source generator. The
/// generator emits converters that access members directly and do not use reflection.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class NbtSerializableAttribute : Attribute
{
    /// <summary>Initializes the attribute for <paramref name="type"/>.</summary>
    /// <param name="type">The CLR type to generate type information for.</param>
    public NbtSerializableAttribute(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        Type = type;
    }

    /// <summary>Gets the CLR type to generate type information for.</summary>
    // ReSharper disable once UnusedAutoPropertyAccessor.Global
    public Type Type { get; }

    /// <summary>
    /// Gets or initializes the property name used for this type on the generated context.
    /// </summary>
    /// <remarks>
    /// When this value is <see langword="null"/>, the generator derives a name from the type.
    /// </remarks>
    // ReSharper disable once UnusedAutoPropertyAccessor.Global
    public string? TypeInfoPropertyName { get; init; }
}

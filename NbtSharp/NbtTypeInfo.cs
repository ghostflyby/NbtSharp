namespace NbtSharp;

/// <summary>
/// Describes the converter and contract for a CLR type.
/// </summary>
public abstract class NbtTypeInfo
{
    /// <summary>Initializes a type descriptor for <paramref name="type"/>.</summary>
    /// <param name="type">The described CLR type.</param>
    protected NbtTypeInfo(Type type)
        : this(type, options: null)
    {
    }

    /// <summary>Initializes a type descriptor for <paramref name="type"/>.</summary>
    /// <param name="type">The described CLR type.</param>
    /// <param name="options">
    /// The options used by type-information serializer overloads. When <see langword="null"/>, a
    /// fresh default options instance is used. Provide options with a resolver when the converter
    /// resolves nested types.
    /// </param>
    protected NbtTypeInfo(Type type, NbtSerializerOptions? options)
    {
        ArgumentNullException.ThrowIfNull(type);
        Type = type;
        Options = options ?? new NbtSerializerOptions();
    }

    /// <summary>Gets the described CLR type.</summary>
    public Type Type { get; }

    /// <summary>Gets the options used by type-information serializer overloads.</summary>
    public NbtSerializerOptions Options { get; }

    /// <summary>Gets the converter that handles this type.</summary>
    public abstract NbtConverter Converter { get; }
}

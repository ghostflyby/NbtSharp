namespace NbtSharp;

/// <summary>
/// Describes the converter and contract for a CLR type.
/// </summary>
public abstract class NbtTypeInfo
{
    /// <summary>Initializes a type descriptor for <paramref name="type"/>.</summary>
    /// <param name="type">The described CLR type.</param>
    protected NbtTypeInfo(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        Type = type;
    }

    /// <summary>Gets the described CLR type.</summary>
    public Type Type { get; }

    /// <summary>Gets the converter that handles this type.</summary>
    public abstract NbtConverter Converter { get; }
}

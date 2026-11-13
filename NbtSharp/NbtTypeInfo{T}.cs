namespace NbtSharp;

/// <summary>
/// Describes the converter and contract for <typeparamref name="T"/>.
/// </summary>
/// <typeparam name="T">The described CLR type.</typeparam>
public sealed class NbtTypeInfo<T> : NbtTypeInfo
{
    /// <summary>Initializes a type descriptor that uses <paramref name="converter"/>.</summary>
    /// <param name="converter">The converter for <typeparamref name="T"/>.</param>
    public NbtTypeInfo(NbtConverter<T> converter)
        : base(typeof(T))
    {
        ArgumentNullException.ThrowIfNull(converter);
        TypedConverter = converter;
    }

    /// <inheritdoc/>
    public override NbtConverter Converter => TypedConverter;

    /// <summary>Gets the strongly typed converter for <typeparamref name="T"/>.</summary>
    public NbtConverter<T> TypedConverter { get; }
}

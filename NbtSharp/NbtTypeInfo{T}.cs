namespace NbtSharp;

/// <summary>
/// Describes the converter and contract for <typeparamref name="T"/>.
/// </summary>
/// <typeparam name="T">The described CLR type.</typeparam>
public sealed class NbtTypeInfo<T> : NbtTypeInfo
{
    private readonly NbtConverter<T> _converter;

    /// <summary>Initializes a type descriptor that uses <paramref name="converter"/>.</summary>
    /// <param name="converter">The converter for <typeparamref name="T"/>.</param>
    public NbtTypeInfo(NbtConverter<T> converter)
        : base(typeof(T))
    {
        ArgumentNullException.ThrowIfNull(converter);
        _converter = converter;
    }

    /// <inheritdoc/>
    public override NbtConverter Converter => _converter;

    /// <summary>Gets the strongly typed converter for <typeparamref name="T"/>.</summary>
    public NbtConverter<T> TypedConverter => _converter;
}

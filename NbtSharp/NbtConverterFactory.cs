namespace NbtSharp;

/// <summary>
/// Creates converters for a family of CLR types, such as generic collections or nullable values.
/// </summary>
public abstract class NbtConverterFactory : NbtConverter
{
    /// <summary>Creates a converter for <paramref name="typeToConvert"/>.</summary>
    /// <param name="typeToConvert">The CLR type to convert.</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>The converter for <paramref name="typeToConvert"/>.</returns>
    public abstract NbtConverter CreateConverter(Type typeToConvert, NbtSerializerOptions options);
}

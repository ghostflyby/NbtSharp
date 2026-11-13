namespace NbtSharp;

/// <summary>
/// Base type for NBT converters. A converter decides whether it applies to a CLR type and, through
/// its derived generic form, reads and writes values of that type.
/// </summary>
public abstract class NbtConverter
{
    /// <summary>Gets whether this converter can convert <paramref name="typeToConvert"/>.</summary>
    /// <param name="typeToConvert">The CLR type to test.</param>
    /// <returns><see langword="true"/> if the converter applies.</returns>
    public abstract bool CanConvert(Type typeToConvert);

    internal virtual object? ReadValue(ref NbtReader reader, Type typeToConvert, NbtSerializerOptions options) =>
        throw new NotSupportedException(
            $"The converter {GetType()} must derive from NbtConverter<T> to read values.");

    internal virtual void WriteValue(ref NbtWriter writer, object? value, Type typeToConvert, NbtSerializerOptions options) =>
        throw new NotSupportedException(
            $"The converter {GetType()} must derive from NbtConverter<T> to write values.");
}

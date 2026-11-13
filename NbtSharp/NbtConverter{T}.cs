namespace NbtSharp;

/// <summary>
/// Converts values of <typeparamref name="T"/> to and from NBT.
/// </summary>
/// <typeparam name="T">The CLR type handled by the converter.</typeparam>
/// <remarks>
/// A converter writes exactly one complete NBT value. It does not write the member name; the caller
/// writes the name with <see cref="NbtWriter.WritePropertyName"/> before delegating the value.
/// Different values of the same CLR type may produce different NBT tag types.
/// </remarks>
public abstract class NbtConverter<T> : NbtConverter
{
    /// <inheritdoc/>
    public sealed override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(T);

    /// <summary>Reads one value of type <typeparamref name="T"/>.</summary>
    /// <param name="reader">The reader positioned at the value's first token.</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>The decoded value.</returns>
    public abstract T Read(ref NbtReader reader, NbtSerializerOptions options);

    /// <summary>Writes one complete value of type <typeparamref name="T"/>.</summary>
    /// <param name="writer">The writer that receives the value.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="options">The serializer options.</param>
    public abstract void Write(ref NbtWriter writer, T value, NbtSerializerOptions options);

    /// <summary>
    /// Tries to predict the NBT tag type that <see cref="Write"/> will produce for
    /// <paramref name="value"/>. Returning <see langword="false"/> means the type is unknown, not
    /// that the value cannot be serialized.
    /// </summary>
    /// <param name="value">The value that will be written.</param>
    /// <param name="options">The serializer options.</param>
    /// <param name="tagType">The predicted tag type when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> if the tag type could be predicted.</returns>
    public virtual bool TryGetTagType(T value, NbtSerializerOptions options, out NbtTagType tagType)
    {
        tagType = default;
        return false;
    }

    internal sealed override object? ReadValue(ref NbtReader reader, Type typeToConvert, NbtSerializerOptions options)
    {
        if (typeToConvert != typeof(T))
        {
            throw new InvalidOperationException($"The converter handles {typeof(T)} but was asked for {typeToConvert}.");
        }

        return Read(ref reader, options);
    }

    internal sealed override void WriteValue(ref NbtWriter writer, object? value, Type typeToConvert, NbtSerializerOptions options)
    {
        if (typeToConvert != typeof(T))
        {
            throw new InvalidOperationException($"The converter handles {typeof(T)} but was asked for {typeToConvert}.");
        }

        Write(ref writer, (T)value!, options);
    }
}

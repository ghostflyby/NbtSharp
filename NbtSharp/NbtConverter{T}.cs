namespace NbtSharp;

/// <summary>
/// Converts values of <typeparamref name="T"/> to and from NBT.
/// </summary>
/// <typeparam name="T">The CLR type handled by the converter.</typeparam>
/// <remarks>
/// A converter writes exactly one anonymous NBT value payload. Named members and root values are
/// written by the serializer, which asks the converter for a tag type prediction before writing the
/// header. Different values of the same CLR type may produce different NBT tag types.
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
    /// Predicts the NBT tag type that <see cref="Write"/> will produce for <paramref name="value"/>.
    /// </summary>
    /// <remarks>
    /// A named member, a root value, and the first element of a non-empty list need this
    /// prediction before their header can be written. Returning <see langword="false"/> makes
    /// those operations throw <see cref="NotSupportedException"/>; implement this method to use
    /// the converter in those positions.
    /// </remarks>
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

    internal sealed override bool TryGetTagType(
        object? value,
        Type typeToConvert,
        NbtSerializerOptions options,
        out NbtTagType tagType)
    {
        if (typeToConvert != typeof(T))
        {
            tagType = default;
            return false;
        }

        return TryGetTagType((T)value!, options, out tagType);
    }
}

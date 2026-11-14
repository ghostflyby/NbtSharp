using System.Diagnostics.CodeAnalysis;

namespace NbtSharp.Converters;

/// <summary>
/// Converts <see cref="Nullable{T}"/> values by reading and writing the underlying value.
/// </summary>
[UnconditionalSuppressMessage("Aot", "IL2026", Justification = "Reached only through annotated serializer entry points.")]
[UnconditionalSuppressMessage("Aot", "IL3050", Justification = "Reached only through annotated serializer entry points.")]
internal sealed class NbtNullableConverter<T> : NbtConverter<T?>
    where T : struct
{
    public override T? Read(ref NbtReader reader, NbtSerializerOptions options) =>
        NbtSerializer.DeserializeValue<T>(ref reader, options);

    public override void Write(ref NbtWriter writer, T? value, NbtSerializerOptions options)
    {
        if (!value.HasValue)
        {
            throw new NbtException("A null value cannot be written at this position.");
        }

        NbtSerializer.SerializeValue(ref writer, value.Value, options);
    }

    public override bool TryGetTagType(T? value, NbtSerializerOptions options, out NbtTagType tagType)
    {
        if (value.HasValue &&
            options.GetConverter(typeof(T)) is NbtConverter<T> converter &&
            converter.TryGetTagType(value.Value, options, out tagType))
        {
            return true;
        }

        tagType = default;
        return false;
    }
}

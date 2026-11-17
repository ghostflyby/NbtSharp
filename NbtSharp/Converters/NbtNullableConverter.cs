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
    public override T? Read(ref NbtReader reader, NbtSerializerOptions options)
    {
        var converter = options.GetConverter(typeof(T));
        return converter is NbtConverter<T> typed
            ? NbtSerializer.DeserializeValueCore(ref reader, typed, options, pathSegment: null)
            : (T?)NbtSerializer.DeserializeValueCore(
                ref reader,
                typeof(T),
                converter,
                options,
                pathSegment: null);
    }

    public override void Write(ref NbtWriter writer, T? value, NbtSerializerOptions options)
    {
        if (!value.HasValue)
        {
            throw new NbtException("A null value cannot be written at this position.");
        }

        var converter = options.GetConverter(typeof(T));
        if (converter is NbtConverter<T> typed)
        {
            NbtSerializer.SerializeValueCore(
                ref writer,
                value.Value,
                typed,
                options,
                pathSegment: null,
                headerWritten: false);
            return;
        }

        NbtSerializer.SerializeValueInternal(
            ref writer,
            value.Value,
            typeof(T),
            options,
            pathSegment: null,
            headerWritten: false);
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

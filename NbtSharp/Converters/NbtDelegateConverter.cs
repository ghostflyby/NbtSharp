namespace NbtSharp.Converters;

internal delegate T NbtReadDelegate<out T>(ref NbtReader reader);

internal delegate void NbtWriteDelegate<in T>(ref NbtWriter writer, T value);

/// <summary>
/// Adapts a pair of delegates into a converter with a fixed tag type.
/// </summary>
internal sealed class NbtDelegateConverter<T>(NbtTagType type, NbtReadDelegate<T> read, NbtWriteDelegate<T> write) : NbtConverter<T>
{
    public override T Read(ref NbtReader reader, NbtSerializerOptions options) => read(ref reader);

    public override void Write(ref NbtWriter writer, T value, NbtSerializerOptions options) => write(ref writer, value);

    public override bool TryGetTagType(T value, NbtSerializerOptions options, out NbtTagType tagType)
    {
        tagType = type;
        return true;
    }
}

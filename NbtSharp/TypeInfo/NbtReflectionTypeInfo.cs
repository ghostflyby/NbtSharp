namespace NbtSharp;

internal sealed class NbtReflectionTypeInfo : NbtTypeInfo
{
    public NbtReflectionTypeInfo(Type type, NbtConverter converter)
        : base(type)
    {
        ArgumentNullException.ThrowIfNull(converter);
        Converter = converter;
    }

    public override NbtConverter Converter { get; }
}

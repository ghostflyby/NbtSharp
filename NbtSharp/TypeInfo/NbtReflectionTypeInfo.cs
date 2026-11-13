namespace NbtSharp;

internal sealed class NbtReflectionTypeInfo : NbtTypeInfo
{
    private readonly NbtConverter _converter;

    public NbtReflectionTypeInfo(Type type, NbtConverter converter)
        : base(type)
    {
        ArgumentNullException.ThrowIfNull(converter);
        _converter = converter;
    }

    public override NbtConverter Converter => _converter;
}

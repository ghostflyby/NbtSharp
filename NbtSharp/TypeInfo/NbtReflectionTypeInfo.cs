// The public NbtSharp namespace is intentional; the folder groups internal implementation.
// ReSharper disable once CheckNamespace
namespace NbtSharp;

internal sealed class NbtReflectionTypeInfo : NbtTypeInfo
{
    public NbtReflectionTypeInfo(Type type, NbtConverter converter, NbtSerializerOptions options)
        : base(type, options)
    {
        ArgumentNullException.ThrowIfNull(converter);
        Converter = converter;
    }

    public override NbtConverter Converter { get; }
}

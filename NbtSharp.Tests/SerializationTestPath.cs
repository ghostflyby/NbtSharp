namespace NbtSharp.Tests;

public enum SerializationMode
{
    Generated,
    Reflection,
}

/// <summary>
/// Runs one serializer call through either a generated context or the reflection resolver.
/// </summary>
internal sealed class SerializationTestPath
{
    private readonly NbtSerializerOptions _options;
    private readonly NbtSerializerContext? _context;

    private SerializationTestPath(
        NbtSerializerOptions options,
        NbtSerializerContext? context)
    {
        _options = options;
        _context = context;
    }

    public static SerializationTestPath Generated(NbtSerializerContext context) =>
        new(context.Options, context);

    public static SerializationTestPath Reflection(NbtSerializerOptions options) =>
        new(options, context: null);

    public byte[] Serialize<T>(T value, string rootName = "root")
    {
        if (_context is null)
        {
            return NbtSerializer.Serialize(value, rootName, _options);
        }

        var typeInfo = (NbtTypeInfo<T>)_context.GetTypeInfo(typeof(T), _options)!;
        return NbtSerializer.Serialize(value, typeInfo, rootName);
    }

    public T Deserialize<T>(ReadOnlySpan<byte> data)
    {
        if (_context is null)
        {
            return NbtSerializer.Deserialize<T>(data, _options);
        }

        var typeInfo = (NbtTypeInfo<T>)_context.GetTypeInfo(typeof(T), _options)!;
        return NbtSerializer.Deserialize(data, typeInfo);
    }
}

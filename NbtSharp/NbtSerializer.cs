using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using NbtSharp.IO;

namespace NbtSharp;

/// <summary>
/// Serializes CLR values to NBT and deserializes NBT to CLR values.
/// </summary>
/// <remarks>
/// <para>
/// The serializer is built on <see cref="NbtReader"/>, <see cref="NbtWriter"/>, and
/// <see cref="NbtConverter{T}"/>. Built-in converters cover the primitive NBT types, arrays,
/// common collections, dictionaries, and the DOM types. Ordinary objects require an
/// <see cref="INbtTypeInfoResolver"/> such as <see cref="NbtReflectionTypeInfoResolver"/>.
/// </para>
/// <para>
/// The Stream overloads are synchronous convenience adapters. Deserialization reads the remaining
/// stream into memory; use the span or sequence overloads when the document boundary matters.
/// </para>
/// <para>
/// A generic call whose declared type is <see cref="NbtRoot"/> writes only the contained value and
/// uses the <c>rootName</c> argument. The DOM overloads use <see cref="NbtRoot.Name"/>, and the
/// <c>SerializeDocument</c> overloads accept explicit options for a DOM root.
/// </para>
/// </remarks>
[RequiresUnreferencedCode("Converter resolution may use reflection or runtime generic construction.")]
[RequiresDynamicCode("Converter resolution may use runtime generic construction.")]
public static class NbtSerializer
{
    /// <summary>Serializes <paramref name="value"/> into a new byte array.</summary>
    /// <typeparam name="T">The declared value type.</typeparam>
    /// <param name="value">The value to serialize.</param>
    /// <param name="rootName">The root tag name. Empty is valid.</param>
    /// <param name="options">The serializer options, or the defaults.</param>
    /// <returns>The encoded NBT document.</returns>
    public static byte[] Serialize<T>(T value, string rootName = "", NbtSerializerOptions? options = null)
    {
        var buffer = new ArrayBufferWriter<byte>();
        Serialize(buffer, value, rootName, options);
        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>Serializes <paramref name="value"/> into <paramref name="destination"/>.</summary>
    /// <typeparam name="T">The declared value type.</typeparam>
    /// <param name="destination">The buffer writer that receives the document.</param>
    /// <param name="value">The value to serialize.</param>
    /// <param name="rootName">The root tag name. Empty is valid.</param>
    /// <param name="options">The serializer options, or the defaults.</param>
    public static void Serialize<T>(
        IBufferWriter<byte> destination,
        T value,
        string rootName = "",
        NbtSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(rootName);
        options ??= new NbtSerializerOptions();

        if (options.RootFormat == NbtRootFormat.Unnamed && rootName.Length != 0)
        {
            throw new NbtException("An unnamed root cannot carry a name. Use NbtRootFormat.Named.");
        }

        var writer = new NbtWriter(destination, options.CreateWriterOptions());
        try
        {
            if (options.RootFormat == NbtRootFormat.Named)
            {
                writer.WritePropertyName(rootName);
            }

            if (typeof(T) == typeof(NbtRoot))
            {
                var root = (NbtRoot)(object)value!;
                SerializeValueInternal(ref writer, root.Value, typeof(NbtValue), options, pathSegment: null);
            }
            else
            {
                SerializeValueInternal(ref writer, value, typeof(T), options, pathSegment: null);
            }

            writer.Complete();
        }
        catch (NbtException ex)
        {
            EnsureRootPath(ex);
            throw;
        }
    }

    /// <summary>Serializes <paramref name="value"/> into <paramref name="destination"/>.</summary>
    /// <typeparam name="T">The declared value type.</typeparam>
    /// <param name="destination">The stream that receives the document.</param>
    /// <param name="value">The value to serialize.</param>
    /// <param name="rootName">The root tag name. Empty is valid.</param>
    /// <param name="options">The serializer options, or the defaults.</param>
    public static void Serialize<T>(
        Stream destination,
        T value,
        string rootName = "",
        NbtSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(destination);

        var bufferWriter = new NbtStreamBufferWriter(destination);
        Serialize(bufferWriter, value, rootName, options);
        bufferWriter.Flush();
    }

    /// <summary>Deserializes a complete NBT document from a span.</summary>
    /// <typeparam name="T">The declared value type.</typeparam>
    /// <param name="data">The NBT document.</param>
    /// <param name="options">The serializer options, or the defaults.</param>
    /// <returns>The decoded value.</returns>
    public static T Deserialize<T>(ReadOnlySpan<byte> data, NbtSerializerOptions? options = null) =>
        Deserialize<T>(data, out _, options);

    /// <summary>Deserializes a complete NBT document from a span and returns the root name.</summary>
    /// <typeparam name="T">The declared value type.</typeparam>
    /// <param name="data">The NBT document.</param>
    /// <param name="rootName">The root name, or <see langword="null"/> for an unnamed root.</param>
    /// <param name="options">The serializer options, or the defaults.</param>
    /// <returns>The decoded value.</returns>
    public static T Deserialize<T>(
        ReadOnlySpan<byte> data,
        out string? rootName,
        NbtSerializerOptions? options = null)
    {
        options ??= new NbtSerializerOptions();
        var reader = new NbtReader(data, options.CreateReaderOptions());
        return DeserializeCore<T>(ref reader, options, out rootName);
    }

    /// <summary>Deserializes a complete NBT document from a sequence.</summary>
    /// <typeparam name="T">The declared value type.</typeparam>
    /// <param name="data">The NBT document.</param>
    /// <param name="options">The serializer options, or the defaults.</param>
    /// <returns>The decoded value.</returns>
    public static T Deserialize<T>(ReadOnlySequence<byte> data, NbtSerializerOptions? options = null) =>
        Deserialize<T>(data, out _, options);

    /// <summary>Deserializes a complete NBT document from a sequence and returns the root name.</summary>
    /// <typeparam name="T">The declared value type.</typeparam>
    /// <param name="data">The NBT document.</param>
    /// <param name="rootName">The root name, or <see langword="null"/> for an unnamed root.</param>
    /// <param name="options">The serializer options, or the defaults.</param>
    /// <returns>The decoded value.</returns>
    public static T Deserialize<T>(
        ReadOnlySequence<byte> data,
        out string? rootName,
        NbtSerializerOptions? options = null)
    {
        options ??= new NbtSerializerOptions();
        var reader = new NbtReader(data, options.CreateReaderOptions());
        return DeserializeCore<T>(ref reader, options, out rootName);
    }

    /// <summary>
    /// Deserializes a complete NBT document from the remaining bytes of a stream.
    /// </summary>
    /// <typeparam name="T">The declared value type.</typeparam>
    /// <param name="stream">The stream that supplies the document.</param>
    /// <param name="options">The serializer options, or the defaults.</param>
    /// <returns>The decoded value.</returns>
    /// <remarks>This overload reads the stream to its end and buffers the document in memory.</remarks>
    public static T Deserialize<T>(Stream stream, NbtSerializerOptions? options = null) =>
        Deserialize<T>(stream, out _, options);

    /// <summary>
    /// Deserializes a complete NBT document from the remaining bytes of a stream and returns the
    /// root name.
    /// </summary>
    /// <typeparam name="T">The declared value type.</typeparam>
    /// <param name="stream">The stream that supplies the document.</param>
    /// <param name="rootName">The root name, or <see langword="null"/> for an unnamed root.</param>
    /// <param name="options">The serializer options, or the defaults.</param>
    /// <returns>The decoded value.</returns>
    /// <remarks>This overload reads the stream to its end and buffers the document in memory.</remarks>
    public static T Deserialize<T>(
        Stream stream,
        out string? rootName,
        NbtSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        options ??= new NbtSerializerOptions();
        var data = ReadStream(stream, options.MaxDocumentBytes);
        return Deserialize<T>(data.AsSpan(), out rootName, options);
    }

    /// <summary>Writes one value using <paramref name="writer"/>.</summary>
    /// <typeparam name="T">The declared value type.</typeparam>
    /// <param name="writer">The writer that receives the value.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="options">The serializer options.</param>
    public static void SerializeValue<T>(ref NbtWriter writer, T value, NbtSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        SerializeValueInternal(ref writer, value, typeof(T), options, pathSegment: null);
    }

    /// <summary>Reads one value using <paramref name="reader"/>.</summary>
    /// <typeparam name="T">The declared value type.</typeparam>
    /// <param name="reader">The reader positioned at the value's first token.</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>The decoded value.</returns>
    public static T DeserializeValue<T>(ref NbtReader reader, NbtSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return (T)DeserializeValueInternal(ref reader, typeof(T), options, pathSegment: null)!;
    }

    /// <summary>Writes a complete DOM document into a new byte array.</summary>
    /// <param name="root">The document, including its root name.</param>
    /// <returns>The encoded NBT document.</returns>
    public static byte[] Serialize(NbtRoot root) => SerializeDocument(root, options: null);

    /// <summary>Writes a complete DOM document into a new byte array.</summary>
    /// <param name="root">The document, including its root name.</param>
    /// <param name="options">The serializer options, or the defaults.</param>
    /// <returns>The encoded NBT document.</returns>
    public static byte[] SerializeDocument(NbtRoot root, NbtSerializerOptions? options)
    {
        var buffer = new ArrayBufferWriter<byte>();
        SerializeDocument(buffer, root, options);
        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>Writes a complete DOM document with default options.</summary>
    /// <param name="destination">The buffer writer that receives the document.</param>
    /// <param name="root">The document, including its root name.</param>
    public static void Serialize(IBufferWriter<byte> destination, NbtRoot root) =>
        SerializeDocument(destination, root, options: null);

    /// <summary>Writes a complete DOM document.</summary>
    /// <param name="destination">The buffer writer that receives the document.</param>
    /// <param name="root">The document, including its root name.</param>
    /// <param name="options">The serializer options, or the defaults.</param>
    public static void SerializeDocument(
        IBufferWriter<byte> destination,
        NbtRoot root,
        NbtSerializerOptions? options)
    {
        ArgumentNullException.ThrowIfNull(destination);
        options ??= new NbtSerializerOptions();
        ValidateRoot(root, options);
        Serialize(destination, root.Value, root.Name ?? string.Empty, options);
    }

    /// <summary>Writes a complete DOM document to a stream with default options.</summary>
    /// <param name="destination">The stream that receives the document.</param>
    /// <param name="root">The document, including its root name.</param>
    public static void Serialize(Stream destination, NbtRoot root) =>
        SerializeDocument(destination, root, options: null);

    /// <summary>Writes a complete DOM document to a stream.</summary>
    /// <param name="destination">The stream that receives the document.</param>
    /// <param name="root">The document, including its root name.</param>
    /// <param name="options">The serializer options, or the defaults.</param>
    public static void SerializeDocument(Stream destination, NbtRoot root, NbtSerializerOptions? options)
    {
        ArgumentNullException.ThrowIfNull(destination);
        options ??= new NbtSerializerOptions();
        ValidateRoot(root, options);
        Serialize(destination, root.Value, root.Name ?? string.Empty, options);
    }

    /// <summary>Reads a complete DOM document from a span.</summary>
    /// <param name="data">The NBT document.</param>
    /// <param name="options">The serializer options, or the defaults.</param>
    /// <returns>The parsed document.</returns>
    public static NbtRoot Deserialize(ReadOnlySpan<byte> data, NbtSerializerOptions? options = null)
    {
        var value = Deserialize<NbtValue>(data, out var rootName, options);
        return new NbtRoot(rootName, value);
    }

    /// <summary>Reads a complete DOM document from a sequence.</summary>
    /// <param name="data">The NBT document.</param>
    /// <param name="options">The serializer options, or the defaults.</param>
    /// <returns>The parsed document.</returns>
    public static NbtRoot Deserialize(ReadOnlySequence<byte> data, NbtSerializerOptions? options = null)
    {
        var value = Deserialize<NbtValue>(data, out var rootName, options);
        return new NbtRoot(rootName, value);
    }

    /// <summary>Reads a complete DOM document from a stream.</summary>
    /// <param name="stream">The stream that supplies the document.</param>
    /// <param name="options">The serializer options, or the defaults.</param>
    /// <returns>The parsed document.</returns>
    public static NbtRoot Deserialize(Stream stream, NbtSerializerOptions? options = null)
    {
        var value = Deserialize<NbtValue>(stream, out var rootName, options);
        return new NbtRoot(rootName, value);
    }

    internal static object? DeserializeValueInternal(
        ref NbtReader reader,
        Type type,
        NbtSerializerOptions options,
        string? pathSegment)
    {
        try
        {
            var converter = options.GetConverter(type);
            var startToken = reader.TokenType;
            var startDepth = reader.CurrentDepth;
            var parentDepth = startToken is NbtTokenType.StartCompound or NbtTokenType.StartList
                ? startDepth - 1
                : startDepth;
            var beforePosition = reader.BytesConsumed;
            var beforeValues = reader.ValuesReadAtDepth(parentDepth);
            var value = converter.ReadValue(ref reader, type, options);

            var expectedDepth = startToken is NbtTokenType.StartCompound or NbtTokenType.StartList
                ? startDepth - 1
                : startDepth;
            var expectedEndToken = startToken switch
            {
                NbtTokenType.StartCompound => NbtTokenType.EndCompound,
                NbtTokenType.StartList => NbtTokenType.EndList,
                _ => reader.TokenType,
            };

            if (reader.CurrentDepth != expectedDepth ||
                reader.TokenType != expectedEndToken ||
                reader.ValuesReadAtDepth(parentDepth) != beforeValues ||
                reader.HasPendingValue ||
                reader.HasPendingContainer)
            {
                throw new NbtException($"The converter for {type} did not consume exactly one complete value.")
                {
                    BytePosition = beforePosition,
                };
            }

            return value;
        }
        catch (NbtException ex)
        {
            PrependPath(ex, pathSegment);
            throw;
        }
    }

    internal static void SerializeValueInternal(
        ref NbtWriter writer,
        object? value,
        Type type,
        NbtSerializerOptions options,
        string? pathSegment)
    {
        try
        {
            if (value is null)
            {
                HandleNull(ref writer, options);
                return;
            }

            var converter = options.GetConverter(type);
            var beforeDepth = writer.CurrentDepth;
            var beforeValuesAtDepth = writer.ValuesWrittenAtDepth(beforeDepth);
            converter.WriteValue(ref writer, value, type, options);

            if (writer.ValuesWrittenAtDepth(beforeDepth) != beforeValuesAtDepth + 1)
            {
                throw new NbtException($"The converter for {type} must write exactly one value.")
                {
                    BytePosition = writer.BytesWritten,
                };
            }

            if (writer.CurrentDepth != beforeDepth)
            {
                throw new NbtException($"The converter for {type} left a container open.")
                {
                    BytePosition = writer.BytesWritten,
                };
            }

            if (writer.HasPendingName)
            {
                throw new NbtException($"The converter for {type} left a member name pending.")
                {
                    BytePosition = writer.BytesWritten,
                };
            }
        }
        catch (NbtException ex)
        {
            PrependPath(ex, pathSegment);
            throw;
        }
    }

    internal static void PrependPath(NbtException exception, string? segment)
    {
        if (segment is null)
        {
            return;
        }

        if (exception.Path is null)
        {
            exception.Path = segment;
            return;
        }

        exception.Path = exception.Path.StartsWith('$')
            ? "$" + segment + exception.Path[1..]
            : segment + exception.Path;
    }

    private static T DeserializeCore<T>(
        ref NbtReader reader,
        NbtSerializerOptions options,
        out string? rootName)
    {
        rootName = null;
        try
        {
            if (!reader.Read())
            {
                throw new NbtException("The NBT data does not contain a root tag.");
            }

            rootName = reader.Name;

            object? value;
            if (typeof(T) == typeof(NbtRoot))
            {
                var nbtValue = DeserializeValueInternal(ref reader, typeof(NbtValue), options, pathSegment: null);
                value = new NbtRoot(rootName, (NbtValue)nbtValue!);
            }
            else
            {
                value = DeserializeValueInternal(ref reader, typeof(T), options, pathSegment: null);
            }

            if (reader.CurrentDepth != 0)
            {
                throw new NbtException("The root value was not fully consumed.")
                {
                    BytePosition = reader.BytesConsumed,
                };
            }

            return (T)value!;
        }
        catch (NbtException ex)
        {
            EnsureRootPath(ex);
            throw;
        }
    }

    private static void HandleNull(ref NbtWriter writer, NbtSerializerOptions options)
    {
        if (writer.IsInList)
        {
            throw new NbtException("A list element cannot be null.")
            {
                BytePosition = writer.BytesWritten,
            };
        }

        if (writer.CurrentDepth == 0)
        {
            throw new NbtException("The root value cannot be null.")
            {
                BytePosition = writer.BytesWritten,
            };
        }

        if (!writer.IsInCompound)
        {
            throw new InvalidOperationException("A null value can only appear as a compound member.");
        }

        if (!writer.HasPendingName)
        {
            throw new InvalidOperationException("A compound member name is required.");
        }

        if (options.NullValueHandling == NbtNullValueHandling.Throw)
        {
            throw new NbtException("A compound member value cannot be null.")
            {
                BytePosition = writer.BytesWritten,
            };
        }

        writer.SkipPendingPropertyName();
    }

    private static void ValidateRoot(NbtRoot root, NbtSerializerOptions options)
    {
        if (options.RootFormat == NbtRootFormat.Named && root.Name is null)
        {
            throw new NbtException("A named root requires a name. Use an empty name or NbtRootFormat.Unnamed.");
        }

        if (options.RootFormat == NbtRootFormat.Unnamed && root.Name is not null)
        {
            throw new NbtException("An unnamed root cannot carry a name. Use NbtRootFormat.Named.");
        }
    }

    private static void EnsureRootPath(NbtException exception)
    {
        if (exception.Path is null)
        {
            exception.Path = "$";
        }
        else if (!exception.Path.StartsWith('$'))
        {
            exception.Path = "$" + exception.Path;
        }
    }

    private static byte[] ReadStream(Stream stream, long maxBytes)
    {
        var buffer = new ArrayBufferWriter<byte>();
        while (true)
        {
            var span = buffer.GetSpan(8192);
            var read = stream.Read(span);
            if (read == 0)
            {
                return buffer.WrittenSpan.ToArray();
            }

            buffer.Advance(read);
            if (maxBytes > 0 && buffer.WrittenCount > maxBytes)
            {
                throw new NbtException($"The NBT document exceeds the configured limit of {maxBytes} bytes.");
            }
        }
    }
}

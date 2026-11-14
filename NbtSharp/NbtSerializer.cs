using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;

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
/// Synchronous deserialization reads a span, a sequence of spans, or a stream through a fixed-size
/// buffer. Asynchronous deserialization reads through a <see cref="PipeReader"/>. Serialization
/// into a <see cref="Stream"/> is backed by <see cref="PipeWriter"/>. Documents are consumed and
/// produced incrementally, so a stream does not need to be seekable. Trailing bytes after the root
/// value are ignored; a stream-backed source may read some of them into its buffer.
/// </para>
/// <para>
/// A generic call whose declared type is <see cref="NbtRoot"/> writes only the contained value and
/// uses the <c>rootName</c> argument. The DOM overloads use <see cref="NbtRoot.Name"/>, and the
/// <c>SerializeDocument</c> overloads accept explicit options for a DOM root.
/// </para>
/// </remarks>
public static class NbtSerializer
{
    /// <summary>Serializes <paramref name="value"/> into a new byte array.</summary>
    /// <typeparam name="T">The declared value type.</typeparam>
    /// <param name="value">The value to serialize.</param>
    /// <param name="rootName">The root tag name. Empty is valid.</param>
    /// <param name="options">The serializer options, or the defaults.</param>
    /// <returns>The encoded NBT document.</returns>
    [RequiresUnreferencedCode("Converter resolution may use reflection or runtime generic construction.")]
    [RequiresDynamicCode("Converter resolution may use runtime generic construction.")]
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
    [RequiresUnreferencedCode("Converter resolution may use reflection or runtime generic construction.")]
    [RequiresDynamicCode("Converter resolution may use runtime generic construction.")]
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
            if (typeof(T) == typeof(NbtRoot))
            {
                var root = (NbtRoot)(object)value!;
                SerializeRootValue(ref writer, root.Value, typeof(NbtValue), rootName, options);
            }
            else
            {
                SerializeRootValue(ref writer, value, typeof(T), rootName, options);
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
    /// <remarks>
    /// The document is written through a <see cref="PipeWriter"/> over the stream and flushed when
    /// the document is complete. The stream stays open.
    /// </remarks>
    [RequiresUnreferencedCode("Converter resolution may use reflection or runtime generic construction.")]
    [RequiresDynamicCode("Converter resolution may use runtime generic construction.")]
    public static void Serialize<T>(
        Stream destination,
        T value,
        string rootName = "",
        NbtSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(destination);

        var pipeWriter = PipeWriter.Create(
            destination,
            new StreamPipeWriterOptions(leaveOpen: true));
        try
        {
            Serialize(pipeWriter, value, rootName, options);
        }
        catch (Exception ex)
        {
            try
            {
                pipeWriter.Complete(ex);
            }
            catch
            {
                // Preserve the original serialization failure.
            }

            throw;
        }

        pipeWriter.Complete();
    }

    /// <summary>Deserializes a complete NBT document from a span.</summary>
    /// <typeparam name="T">The declared value type.</typeparam>
    /// <param name="data">The NBT document.</param>
    /// <param name="options">The serializer options, or the defaults.</param>
    /// <returns>The decoded value.</returns>
    [RequiresUnreferencedCode("Converter resolution may use reflection or runtime generic construction.")]
    [RequiresDynamicCode("Converter resolution may use runtime generic construction.")]
    public static T Deserialize<T>(ReadOnlySpan<byte> data, NbtSerializerOptions? options = null) =>
        Deserialize<T>(data, out _, options);

    /// <summary>Deserializes a complete NBT document from a span and returns the root name.</summary>
    /// <typeparam name="T">The declared value type.</typeparam>
    /// <param name="data">The NBT document.</param>
    /// <param name="rootName">The root name, or <see langword="null"/> for an unnamed root.</param>
    /// <param name="options">The serializer options, or the defaults.</param>
    /// <returns>The decoded value.</returns>
    [RequiresUnreferencedCode("Converter resolution may use reflection or runtime generic construction.")]
    [RequiresDynamicCode("Converter resolution may use runtime generic construction.")]
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
    [RequiresUnreferencedCode("Converter resolution may use reflection or runtime generic construction.")]
    [RequiresDynamicCode("Converter resolution may use runtime generic construction.")]
    public static T Deserialize<T>(ReadOnlySequence<byte> data, NbtSerializerOptions? options = null) =>
        Deserialize<T>(data, out _, options);

    /// <summary>Deserializes a complete NBT document from a sequence and returns the root name.</summary>
    /// <typeparam name="T">The declared value type.</typeparam>
    /// <param name="data">The NBT document.</param>
    /// <param name="rootName">The root name, or <see langword="null"/> for an unnamed root.</param>
    /// <param name="options">The serializer options, or the defaults.</param>
    /// <returns>The decoded value.</returns>
    [RequiresUnreferencedCode("Converter resolution may use reflection or runtime generic construction.")]
    [RequiresDynamicCode("Converter resolution may use runtime generic construction.")]
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
    /// <remarks>
    /// This overload reads the stream synchronously through a fixed-size pooled buffer. The stream
    /// does not need to be seekable. Bytes after the root value are ignored, but may already have
    /// been read from the stream.
    /// </remarks>
    [RequiresUnreferencedCode("Converter resolution may use reflection or runtime generic construction.")]
    [RequiresDynamicCode("Converter resolution may use runtime generic construction.")]
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
    /// <remarks>
    /// This overload reads the stream synchronously through a fixed-size pooled buffer. The stream
    /// does not need to be seekable. Bytes after the root value are ignored, but may already have
    /// been read from the stream.
    /// </remarks>
    [RequiresUnreferencedCode("Converter resolution may use reflection or runtime generic construction.")]
    [RequiresDynamicCode("Converter resolution may use runtime generic construction.")]
    public static T Deserialize<T>(
        Stream stream,
        out string? rootName,
        NbtSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        options ??= new NbtSerializerOptions();
        var reader = new NbtReader(stream, options.CreateReaderOptions());
        try
        {
            return DeserializeCore<T>(ref reader, options, out rootName);
        }
        finally
        {
            reader.Dispose();
        }
    }

    /// <summary>Deserializes a complete NBT document from a pipe reader asynchronously.</summary>
    /// <typeparam name="T">The declared value type.</typeparam>
    /// <param name="reader">The pipe reader that supplies the document.</param>
    /// <param name="options">The serializer options, or the defaults.</param>
    /// <param name="cancellationToken">The token used to cancel the read.</param>
    /// <returns>A task that yields the decoded value.</returns>
    /// <remarks>
    /// <para>
    /// The caller owns <paramref name="reader"/>; this method does not complete it. One document is
    /// consumed, and bytes after the root remain available to the next call.
    /// </para>
    /// <para>
    /// A truncated parse can invoke converters again for the same bytes, so converters should not
    /// depend on being called exactly once.
    /// </para>
    /// </remarks>
    [RequiresUnreferencedCode("Converter resolution may use reflection or runtime generic construction.")]
    [RequiresDynamicCode("Converter resolution may use runtime generic construction.")]
    public static async Task<T> DeserializeAsync<T>(
        PipeReader reader,
        NbtSerializerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        options ??= new NbtSerializerOptions();
        var readerOptions = options.CreateReaderOptions();

        while (true)
        {
            var result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (result.IsCanceled)
            {
                reader.AdvanceTo(result.Buffer.Start, result.Buffer.End);
                throw new OperationCanceledException("The pipe read was canceled.");
            }

            try
            {
                try
                {
                    var nbtReader = new NbtReader(result.Buffer, readerOptions);
                    var value = DeserializeCore<T>(ref nbtReader, options, out _);
                    var consumed = nbtReader.BytesConsumed;
                    if (options.MaxDocumentBytes > 0 && consumed > options.MaxDocumentBytes)
                    {
                        throw new NbtException(
                            $"The NBT document exceeds the configured limit of {options.MaxDocumentBytes} bytes.");
                    }

                    reader.AdvanceTo(result.Buffer.GetPosition(consumed), result.Buffer.End);
                    return value;
                }
                catch (NbtException ex) when (ex.IsTruncated && !result.IsCompleted)
                {
                    if (options.MaxDocumentBytes > 0 &&
                        result.Buffer.Length >= options.MaxDocumentBytes)
                    {
                        throw new NbtException(
                            $"The NBT document exceeds the configured limit of {options.MaxDocumentBytes} bytes.");
                    }

                    reader.AdvanceTo(result.Buffer.Start, result.Buffer.End);
                    continue;
                }
            }
            catch
            {
                reader.AdvanceTo(result.Buffer.Start, result.Buffer.End);
                throw;
            }
        }
    }

    /// <summary>Deserializes a complete NBT document from a stream asynchronously.</summary>
    /// <typeparam name="T">The declared value type.</typeparam>
    /// <param name="stream">The stream that supplies the document.</param>
    /// <param name="options">The serializer options, or the defaults.</param>
    /// <param name="cancellationToken">The token used to cancel the read.</param>
    /// <returns>A task that yields the decoded value.</returns>
    /// <remarks>
    /// The stream is read through a <see cref="PipeReader"/>, one document is consumed, and the
    /// stream is left open.
    /// </remarks>
    [RequiresUnreferencedCode("Converter resolution may use reflection or runtime generic construction.")]
    [RequiresDynamicCode("Converter resolution may use runtime generic construction.")]
    public static async Task<T> DeserializeAsync<T>(
        Stream stream,
        NbtSerializerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var pipe = PipeReader.Create(stream, new StreamPipeReaderOptions(leaveOpen: true));
        try
        {
            return await DeserializeAsync<T>(pipe, options, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await pipe.CompleteAsync().ConfigureAwait(false);
            }
            catch
            {
                // Preserve the original result or exception.
            }
        }
    }

    /// <summary>Deserializes a complete NBT document from a sequence asynchronously.</summary>
    /// <typeparam name="T">The declared value type.</typeparam>
    /// <param name="data">The NBT document.</param>
    /// <param name="options">The serializer options, or the defaults.</param>
    /// <param name="cancellationToken">The token used to cancel the read.</param>
    /// <returns>A task that yields the decoded value.</returns>
    /// <remarks>One document is read through a <see cref="PipeReader"/> and then parsed.</remarks>
    [RequiresUnreferencedCode("Converter resolution may use reflection or runtime generic construction.")]
    [RequiresDynamicCode("Converter resolution may use runtime generic construction.")]
    public static async Task<T> DeserializeAsync<T>(
        ReadOnlySequence<byte> data,
        NbtSerializerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var pipe = PipeReader.Create(data);
        try
        {
            return await DeserializeAsync<T>(pipe, options, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await pipe.CompleteAsync().ConfigureAwait(false);
            }
            catch
            {
                // Preserve the original result or exception.
            }
        }
    }

    /// <summary>Writes one unnamed value using <paramref name="writer"/>.</summary>
    /// <typeparam name="T">The declared value type.</typeparam>
    /// <param name="writer">The writer that receives the value.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="options">The serializer options.</param>
    /// <remarks>
    /// This overload is for list elements and for converter payloads whose member or root header
    /// was already written. Compound members use <see cref="SerializeMember{T}"/>.
    /// </remarks>
    [RequiresUnreferencedCode("Converter resolution may use reflection or runtime generic construction.")]
    [RequiresDynamicCode("Converter resolution may use runtime generic construction.")]
    public static void SerializeValue<T>(ref NbtWriter writer, T value, NbtSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        SerializeValueInternal(ref writer, value, typeof(T), options, pathSegment: null, headerWritten: false);
    }

    /// <summary>Writes one named compound member using <paramref name="writer"/>.</summary>
    /// <typeparam name="T">The declared value type.</typeparam>
    /// <param name="writer">The writer that receives the member.</param>
    /// <param name="name">The member name.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="options">The serializer options.</param>
    /// <remarks>
    /// The converter must predict the member's tag type with
    /// <see cref="NbtConverter{T}.TryGetTagType(T, NbtSerializerOptions, out NbtTagType)"/> before
    /// the header can be written.
    /// </remarks>
    [RequiresUnreferencedCode("Converter resolution may use reflection or runtime generic construction.")]
    [RequiresDynamicCode("Converter resolution may use runtime generic construction.")]
    public static void SerializeMember<T>(
        ref NbtWriter writer,
        string name,
        T value,
        NbtSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(options);
        SerializeMemberInternal(ref writer, name, value, typeof(T), options);
    }

    /// <summary>Reads one value using <paramref name="reader"/>.</summary>
    /// <typeparam name="T">The declared value type.</typeparam>
    /// <param name="reader">The reader positioned at the value's first token.</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>The decoded value.</returns>
    [RequiresUnreferencedCode("Converter resolution may use reflection or runtime generic construction.")]
    [RequiresDynamicCode("Converter resolution may use runtime generic construction.")]
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
    [UnconditionalSuppressMessage("Aot", "IL2026", Justification = "DOM values resolve to built-in converters.")]
    [UnconditionalSuppressMessage("Aot", "IL3050", Justification = "DOM values resolve to built-in converters.")]
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
    [UnconditionalSuppressMessage("Aot", "IL2026", Justification = "DOM values resolve to built-in converters.")]
    [UnconditionalSuppressMessage("Aot", "IL3050", Justification = "DOM values resolve to built-in converters.")]
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
    [UnconditionalSuppressMessage("Aot", "IL2026", Justification = "DOM values resolve to built-in converters.")]
    [UnconditionalSuppressMessage("Aot", "IL3050", Justification = "DOM values resolve to built-in converters.")]
    public static NbtRoot Deserialize(ReadOnlySpan<byte> data, NbtSerializerOptions? options = null)
    {
        var value = Deserialize<NbtValue>(data, out var rootName, options);
        return new NbtRoot(rootName, value);
    }

    /// <summary>Reads a complete DOM document from a sequence.</summary>
    /// <param name="data">The NBT document.</param>
    /// <param name="options">The serializer options, or the defaults.</param>
    /// <returns>The parsed document.</returns>
    [UnconditionalSuppressMessage("Aot", "IL2026", Justification = "DOM values resolve to built-in converters.")]
    [UnconditionalSuppressMessage("Aot", "IL3050", Justification = "DOM values resolve to built-in converters.")]
    public static NbtRoot Deserialize(ReadOnlySequence<byte> data, NbtSerializerOptions? options = null)
    {
        var value = Deserialize<NbtValue>(data, out var rootName, options);
        return new NbtRoot(rootName, value);
    }

    /// <summary>Reads a complete DOM document from a stream.</summary>
    /// <param name="stream">The stream that supplies the document.</param>
    /// <param name="options">The serializer options, or the defaults.</param>
    /// <returns>The parsed document.</returns>
    /// <remarks>
    /// The stream is consumed synchronously through a fixed-size buffer and does not need to be
    /// seekable.
    /// </remarks>
    [UnconditionalSuppressMessage("Aot", "IL2026", Justification = "DOM values resolve to built-in converters.")]
    [UnconditionalSuppressMessage("Aot", "IL3050", Justification = "DOM values resolve to built-in converters.")]
    public static NbtRoot Deserialize(Stream stream, NbtSerializerOptions? options = null)
    {
        var value = Deserialize<NbtValue>(stream, out var rootName, options);
        return new NbtRoot(rootName, value);
    }

    [RequiresUnreferencedCode("Converter resolution may use reflection or runtime generic construction.")]
    [RequiresDynamicCode("Converter resolution may use runtime generic construction.")]
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

    [RequiresUnreferencedCode("Converter resolution may use reflection or runtime generic construction.")]
    [RequiresDynamicCode("Converter resolution may use runtime generic construction.")]
    internal static void SerializeMemberInternal(
        ref NbtWriter writer,
        string name,
        object? value,
        Type type,
        NbtSerializerOptions options)
    {
        if (writer.IsPayloadExpected)
        {
            throw new InvalidOperationException("A value payload is already expected.");
        }

        try
        {
            if (value is null)
            {
                HandleNullMember(ref writer, options);
                return;
            }

            var converter = options.GetConverter(type);
            if (!converter.TryGetTagType(value, type, options, out var tagType))
            {
                throw new NotSupportedException(
                    $"The converter for {type} must implement TryGetTagType to write a named member.");
            }

            writer.WriteMemberHeader(tagType, name);
        }
        catch (NbtException ex)
        {
            PrependPath(ex, "." + name);
            throw;
        }

        SerializeValueInternal(ref writer, value, type, options, "." + name, headerWritten: true);
    }

    [RequiresUnreferencedCode("Converter resolution may use reflection or runtime generic construction.")]
    [RequiresDynamicCode("Converter resolution may use runtime generic construction.")]
    internal static void SerializeValueInternal(
        ref NbtWriter writer,
        object? value,
        Type type,
        NbtSerializerOptions options,
        string? pathSegment,
        bool headerWritten)
    {
        try
        {
            if (value is null)
            {
                HandleNullValue(ref writer);
                return;
            }

            var converter = options.GetConverter(type);
            var beforeDepth = writer.CurrentDepth;
            var beforeValuesAtDepth = writer.ValuesWrittenAtDepth(beforeDepth);
            var payloadWasPrepared = headerWritten || writer.IsPayloadExpected;
            converter.WriteValue(ref writer, value, type, options);

            if (payloadWasPrepared)
            {
                if (writer.IsPayloadExpected ||
                    writer.CurrentDepth != beforeDepth ||
                    writer.ValuesWrittenAtDepth(beforeDepth) != beforeValuesAtDepth)
                {
                    throw new NbtException(
                        $"The converter for {type} did not complete the prepared value.")
                    {
                        BytePosition = writer.BytesWritten,
                    };
                }
            }
            else if (writer.CurrentDepth != beforeDepth ||
                     writer.ValuesWrittenAtDepth(beforeDepth) != beforeValuesAtDepth + 1)
            {
                throw new NbtException($"The converter for {type} must write exactly one value.")
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

    [RequiresUnreferencedCode("Converter resolution may use reflection or runtime generic construction.")]
    [RequiresDynamicCode("Converter resolution may use runtime generic construction.")]
    private static void SerializeRootValue(
        ref NbtWriter writer,
        object? value,
        Type type,
        string rootName,
        NbtSerializerOptions options)
    {
        if (value is null)
        {
            throw new NbtException("The root value cannot be null.");
        }

        var converter = options.GetConverter(type);
        if (!converter.TryGetTagType(value, type, options, out var tagType))
        {
            throw new NotSupportedException(
                $"The converter for {type} must implement TryGetTagType to write a root value.");
        }

        writer.WriteRootHeader(tagType, rootName);
        SerializeValueInternal(ref writer, value, type, options, pathSegment: null, headerWritten: true);
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

    [RequiresUnreferencedCode("Converter resolution may use reflection or runtime generic construction.")]
    [RequiresDynamicCode("Converter resolution may use runtime generic construction.")]
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
                throw new NbtException("The NBT data does not contain a root tag.")
                {
                    IsTruncated = true,
                };
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

    private static void HandleNullMember(ref NbtWriter writer, NbtSerializerOptions options)
    {
        if (!writer.IsInCompound)
        {
            throw new InvalidOperationException("SerializeMember requires an open compound.");
        }

        if (options.NullValueHandling == NbtNullValueHandling.Throw)
        {
            throw new NbtException("A compound member value cannot be null.")
            {
                BytePosition = writer.BytesWritten,
            };
        }
    }

    private static void HandleNullValue(ref NbtWriter writer)
    {
        if (writer.IsPayloadExpected)
        {
            throw new NbtException("A null value cannot complete a prepared member or root payload.")
            {
                BytePosition = writer.BytesWritten,
            };
        }

        if (writer.IsInList)
        {
            throw new NbtException("A list element cannot be null.")
            {
                BytePosition = writer.BytesWritten,
            };
        }

        throw new InvalidOperationException(
            "Anonymous values cannot be null; use SerializeMember for a compound member.");
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
}

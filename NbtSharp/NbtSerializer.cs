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
/// buffer. Asynchronous deserialization reads through a <see cref="PipeReader"/>; asynchronous
/// serialization writes into a <see cref="Stream"/> or <see cref="PipeWriter"/> and flushes
/// asynchronously. Serialization into a <see cref="Stream"/> is backed by <see cref="PipeWriter"/>.
/// Documents are consumed and
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

    /// <summary>Serializes <paramref name="value"/> into a pipe writer asynchronously.</summary>
    /// <typeparam name="T">The declared value type.</typeparam>
    /// <param name="destination">The pipe writer that receives the document.</param>
    /// <param name="value">The value to serialize.</param>
    /// <param name="rootName">The root tag name. Empty is valid.</param>
    /// <param name="options">The serializer options, or the defaults.</param>
    /// <param name="cancellationToken">The token used to cancel the flush.</param>
    /// <returns>A task that completes when the document has been flushed.</returns>
    /// <remarks>
    /// The caller owns <paramref name="destination"/>; this method flushes but does not complete it.
    /// Writing is synchronous; only the flush observes cancellation. When <typeparamref name="T"/>
    /// is <see cref="NbtRoot"/>, only the contained value is written and <paramref name="rootName"/>
    /// is used; call <see cref="SerializeDocumentAsync(PipeWriter,NbtRoot,NbtSerializerOptions?,CancellationToken)"/>
    /// to preserve <see cref="NbtRoot.Name"/>.
    /// </remarks>
    [RequiresUnreferencedCode("Converter resolution may use reflection or runtime generic construction.")]
    [RequiresDynamicCode("Converter resolution may use runtime generic construction.")]
    public static async Task SerializeAsync<T>(
        PipeWriter destination,
        T value,
        string rootName = "",
        NbtSerializerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        cancellationToken.ThrowIfCancellationRequested();
        Serialize(destination, value, rootName, options);
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Serializes <paramref name="value"/> into a stream asynchronously.</summary>
    /// <typeparam name="T">The declared value type.</typeparam>
    /// <param name="destination">The stream that receives the document.</param>
    /// <param name="value">The value to serialize.</param>
    /// <param name="rootName">The root tag name. Empty is valid.</param>
    /// <param name="options">The serializer options, or the defaults.</param>
    /// <param name="cancellationToken">The token used to cancel the flush.</param>
    /// <returns>A task that completes when the document has been flushed.</returns>
    /// <remarks>
    /// The document is written through a <see cref="PipeWriter"/> over the stream; the stream stays
    /// open. Writing is synchronous; only the flush observes cancellation. When
    /// <typeparamref name="T"/> is <see cref="NbtRoot"/>, only the contained value is written and
    /// <paramref name="rootName"/> is used; call
    /// <see cref="SerializeDocumentAsync(Stream,NbtRoot,NbtSerializerOptions?,CancellationToken)"/>
    /// to preserve <see cref="NbtRoot.Name"/>.
    /// </remarks>
    [RequiresUnreferencedCode("Converter resolution may use reflection or runtime generic construction.")]
    [RequiresDynamicCode("Converter resolution may use runtime generic construction.")]
    public static async Task SerializeAsync<T>(
        Stream destination,
        T value,
        string rootName = "",
        NbtSerializerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        cancellationToken.ThrowIfCancellationRequested();
        var pipeWriter = PipeWriter.Create(
            destination,
            new StreamPipeWriterOptions(leaveOpen: true));
        try
        {
            Serialize(pipeWriter, value, rootName, options);
            await pipeWriter.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            try
            {
                await pipeWriter.CompleteAsync(ex).ConfigureAwait(false);
            }
            catch
            {
                // Preserve the original serialization failure.
            }

            throw;
        }

        await pipeWriter.CompleteAsync().ConfigureAwait(false);
    }

    /// <summary>Serializes a value into a pipe writer asynchronously using explicit type information.</summary>
    /// <typeparam name="T">The declared value type.</typeparam>
    /// <param name="destination">The pipe writer that receives the document.</param>
    /// <param name="value">The value to serialize.</param>
    /// <param name="typeInfo">The type information that supplies the root converter and options.</param>
    /// <param name="rootName">The root tag name. Empty is valid.</param>
    /// <param name="cancellationToken">The token used to cancel the flush.</param>
    /// <returns>A task that completes when the document has been flushed.</returns>
    /// <remarks>
    /// The caller owns <paramref name="destination"/>; this method flushes but does not complete it.
    /// Writing is synchronous; only the flush observes cancellation.
    /// </remarks>
    public static async Task SerializeAsync<T>(
        PipeWriter destination,
        T value,
        NbtTypeInfo<T> typeInfo,
        string rootName = "",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(typeInfo);
        cancellationToken.ThrowIfCancellationRequested();
        Serialize(destination, value, typeInfo, rootName);
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Serializes a value into a stream asynchronously using explicit type information.</summary>
    /// <typeparam name="T">The declared value type.</typeparam>
    /// <param name="destination">The stream that receives the document.</param>
    /// <param name="value">The value to serialize.</param>
    /// <param name="typeInfo">The type information that supplies the root converter and options.</param>
    /// <param name="rootName">The root tag name. Empty is valid.</param>
    /// <param name="cancellationToken">The token used to cancel the flush.</param>
    /// <returns>A task that completes when the document has been flushed.</returns>
    /// <remarks>
    /// The document is written through a <see cref="PipeWriter"/> over the stream; the stream stays
    /// open. Writing is synchronous; only the flush observes cancellation.
    /// </remarks>
    public static async Task SerializeAsync<T>(
        Stream destination,
        T value,
        NbtTypeInfo<T> typeInfo,
        string rootName = "",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(typeInfo);
        cancellationToken.ThrowIfCancellationRequested();
        var pipeWriter = PipeWriter.Create(
            destination,
            new StreamPipeWriterOptions(leaveOpen: true));
        try
        {
            Serialize(pipeWriter, value, typeInfo, rootName);
            await pipeWriter.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            try
            {
                await pipeWriter.CompleteAsync(ex).ConfigureAwait(false);
            }
            catch
            {
                // Preserve the original serialization failure.
            }

            throw;
        }

        await pipeWriter.CompleteAsync().ConfigureAwait(false);
    }

    /// <summary>Writes a complete DOM document to a pipe writer asynchronously.</summary>
    /// <param name="destination">The pipe writer that receives the document.</param>
    /// <param name="root">The document, including its root name.</param>
    /// <param name="options">The serializer options, or the defaults.</param>
    /// <param name="cancellationToken">The token used to cancel the flush.</param>
    /// <returns>A task that completes when the document has been flushed.</returns>
    /// <remarks>
    /// The caller owns <paramref name="destination"/>; this method flushes but does not complete it.
    /// Writing is synchronous; only the flush observes cancellation. <paramref name="root"/>'s
    /// name is preserved.
    /// </remarks>
    [UnconditionalSuppressMessage("Aot", "IL2026", Justification = "DOM values resolve to built-in converters.")]
    [UnconditionalSuppressMessage("Aot", "IL3050", Justification = "DOM values resolve to built-in converters.")]
    public static async Task SerializeDocumentAsync(
        PipeWriter destination,
        NbtRoot root,
        NbtSerializerOptions? options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        cancellationToken.ThrowIfCancellationRequested();
        options ??= new NbtSerializerOptions();
        ValidateRoot(root, options);
        await SerializeAsync(
            destination,
            root.Value,
            root.Name ?? string.Empty,
            options,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes a complete DOM document to a stream asynchronously.</summary>
    /// <param name="destination">The stream that receives the document.</param>
    /// <param name="root">The document, including its root name.</param>
    /// <param name="options">The serializer options, or the defaults.</param>
    /// <param name="cancellationToken">The token used to cancel the flush.</param>
    /// <returns>A task that completes when the document has been flushed.</returns>
    /// <remarks>
    /// The document is written through a <see cref="PipeWriter"/> over the stream; the stream stays
    /// open. Writing is synchronous; only the flush observes cancellation. <paramref name="root"/>'s
    /// name is preserved.
    /// </remarks>
    [UnconditionalSuppressMessage("Aot", "IL2026", Justification = "DOM values resolve to built-in converters.")]
    [UnconditionalSuppressMessage("Aot", "IL3050", Justification = "DOM values resolve to built-in converters.")]
    public static async Task SerializeDocumentAsync(
        Stream destination,
        NbtRoot root,
        NbtSerializerOptions? options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        cancellationToken.ThrowIfCancellationRequested();
        options ??= new NbtSerializerOptions();
        ValidateRoot(root, options);
        await SerializeAsync(
            destination,
            root.Value,
            root.Name ?? string.Empty,
            options,
            cancellationToken).ConfigureAwait(false);
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
        var converter = typeof(T) == typeof(NbtRoot)
            ? options.GetConverter(typeof(NbtValue))
            : options.GetConverter(typeof(T));
        return await DeserializeAsyncCore<T>(
            reader,
            converter,
            options,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deserializes a complete NBT document from a pipe reader asynchronously using explicit type
    /// information.
    /// </summary>
    /// <typeparam name="T">The declared value type.</typeparam>
    /// <param name="reader">The pipe reader that supplies the document.</param>
    /// <param name="typeInfo">The type information that supplies the root converter and options.</param>
    /// <param name="cancellationToken">The token used to cancel the read.</param>
    /// <returns>A task that yields the decoded value.</returns>
    public static async Task<T> DeserializeAsync<T>(
        PipeReader reader,
        NbtTypeInfo<T> typeInfo,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(typeInfo);
        ValidateTypeInfo(typeInfo);
        return await DeserializeAsyncCore<T>(
            reader,
            GetEffectiveConverter(typeInfo),
            typeInfo.Options,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<T> DeserializeAsyncCore<T>(
        PipeReader reader,
        NbtConverter converter,
        NbtSerializerOptions options,
        CancellationToken cancellationToken)
    {
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
                    var value = DeserializeCore<T>(ref nbtReader, converter, options, out _);
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

    /// <summary>
    /// Deserializes a complete NBT document from a stream asynchronously using explicit type
    /// information.
    /// </summary>
    /// <typeparam name="T">The declared value type.</typeparam>
    /// <param name="stream">The stream that supplies the document.</param>
    /// <param name="typeInfo">The type information that supplies the root converter and options.</param>
    /// <param name="cancellationToken">The token used to cancel the read.</param>
    /// <returns>A task that yields the decoded value.</returns>
    public static async Task<T> DeserializeAsync<T>(
        Stream stream,
        NbtTypeInfo<T> typeInfo,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(typeInfo);

        var pipe = PipeReader.Create(stream, new StreamPipeReaderOptions(leaveOpen: true));
        try
        {
            return await DeserializeAsync(pipe, typeInfo, cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// Deserializes a complete NBT document from a sequence asynchronously using explicit type
    /// information.
    /// </summary>
    /// <typeparam name="T">The declared value type.</typeparam>
    /// <param name="data">The NBT document.</param>
    /// <param name="typeInfo">The type information that supplies the root converter and options.</param>
    /// <param name="cancellationToken">The token used to cancel the read.</param>
    /// <returns>A task that yields the decoded value.</returns>
    public static async Task<T> DeserializeAsync<T>(
        ReadOnlySequence<byte> data,
        NbtTypeInfo<T> typeInfo,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);

        var pipe = PipeReader.Create(data);
        try
        {
            return await DeserializeAsync(pipe, typeInfo, cancellationToken).ConfigureAwait(false);
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

    /// <summary>Serializes a value using explicit type information.</summary>
    /// <typeparam name="T">The declared value type.</typeparam>
    /// <param name="value">The value to serialize.</param>
    /// <param name="typeInfo">The type information that supplies the root converter and options.</param>
    /// <param name="rootName">The root tag name. Empty is valid.</param>
    /// <returns>The encoded NBT document.</returns>
    /// <remarks>
    /// Unlike the generic overloads, this entry point does not carry trimming or AOT annotations.
    /// Safety depends on <paramref name="typeInfo"/> covering every type reachable from
    /// <typeparamref name="T"/>.
    /// </remarks>
    public static byte[] Serialize<T>(T value, NbtTypeInfo<T> typeInfo, string rootName = "")
    {
        var buffer = new ArrayBufferWriter<byte>();
        Serialize(buffer, value, typeInfo, rootName);
        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>Serializes a value using explicit type information.</summary>
    /// <typeparam name="T">The declared value type.</typeparam>
    /// <param name="destination">The buffer writer that receives the document.</param>
    /// <param name="value">The value to serialize.</param>
    /// <param name="typeInfo">The type information that supplies the root converter and options.</param>
    /// <param name="rootName">The root tag name. Empty is valid.</param>
    public static void Serialize<T>(
        IBufferWriter<byte> destination,
        T value,
        NbtTypeInfo<T> typeInfo,
        string rootName = "")
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(typeInfo);
        ArgumentNullException.ThrowIfNull(rootName);
        ValidateTypeInfo(typeInfo);

        var options = typeInfo.Options;
        if (options.RootFormat == NbtRootFormat.Unnamed && rootName.Length != 0)
        {
            throw new NbtException("An unnamed root cannot carry a name. Use NbtRootFormat.Named.");
        }

        var writer = new NbtWriter(destination, options.CreateWriterOptions());
        try
        {
            SerializeRootValue(
            ref writer,
            value,
            typeof(T),
            rootName,
            options,
            GetEffectiveConverter(typeInfo));
            writer.Complete();
        }
        catch (NbtException ex)
        {
            EnsureRootPath(ex);
            throw;
        }
    }

    /// <summary>Serializes a value to a stream using explicit type information.</summary>
    /// <typeparam name="T">The declared value type.</typeparam>
    /// <param name="destination">The stream that receives the document.</param>
    /// <param name="value">The value to serialize.</param>
    /// <param name="typeInfo">The type information that supplies the root converter and options.</param>
    /// <param name="rootName">The root tag name. Empty is valid.</param>
    public static void Serialize<T>(
        Stream destination,
        T value,
        NbtTypeInfo<T> typeInfo,
        string rootName = "")
    {
        ArgumentNullException.ThrowIfNull(destination);

        var pipeWriter = PipeWriter.Create(
            destination,
            new StreamPipeWriterOptions(leaveOpen: true));
        try
        {
            Serialize(pipeWriter, value, typeInfo, rootName);
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

    /// <summary>Deserializes a document from a span using explicit type information.</summary>
    /// <typeparam name="T">The declared value type.</typeparam>
    /// <param name="data">The NBT document.</param>
    /// <param name="typeInfo">The type information that supplies the root converter and options.</param>
    /// <returns>The decoded value.</returns>
    public static T Deserialize<T>(ReadOnlySpan<byte> data, NbtTypeInfo<T> typeInfo) =>
        Deserialize(data, typeInfo, out _);

    /// <summary>Deserializes a document from a span using explicit type information.</summary>
    /// <typeparam name="T">The declared value type.</typeparam>
    /// <param name="data">The NBT document.</param>
    /// <param name="typeInfo">The type information that supplies the root converter and options.</param>
    /// <param name="rootName">The root name, or <see langword="null"/> for an unnamed root.</param>
    /// <returns>The decoded value.</returns>
    public static T Deserialize<T>(
        ReadOnlySpan<byte> data,
        NbtTypeInfo<T> typeInfo,
        out string? rootName)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        ValidateTypeInfo(typeInfo);

        var options = typeInfo.Options;
        var reader = new NbtReader(data, options.CreateReaderOptions());
        return DeserializeCore<T>(ref reader, GetEffectiveConverter(typeInfo), options, out rootName);
    }

    /// <summary>Deserializes a document from a sequence using explicit type information.</summary>
    /// <typeparam name="T">The declared value type.</typeparam>
    /// <param name="data">The NBT document.</param>
    /// <param name="typeInfo">The type information that supplies the root converter and options.</param>
    /// <returns>The decoded value.</returns>
    public static T Deserialize<T>(ReadOnlySequence<byte> data, NbtTypeInfo<T> typeInfo) =>
        Deserialize(data, typeInfo, out _);

    /// <summary>Deserializes a document from a sequence using explicit type information.</summary>
    /// <typeparam name="T">The declared value type.</typeparam>
    /// <param name="data">The NBT document.</param>
    /// <param name="typeInfo">The type information that supplies the root converter and options.</param>
    /// <param name="rootName">The root name, or <see langword="null"/> for an unnamed root.</param>
    /// <returns>The decoded value.</returns>
    public static T Deserialize<T>(
        ReadOnlySequence<byte> data,
        NbtTypeInfo<T> typeInfo,
        out string? rootName)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        ValidateTypeInfo(typeInfo);

        var options = typeInfo.Options;
        var reader = new NbtReader(data, options.CreateReaderOptions());
        return DeserializeCore<T>(ref reader, GetEffectiveConverter(typeInfo), options, out rootName);
    }

    /// <summary>Deserializes a document from a stream using explicit type information.</summary>
    /// <typeparam name="T">The declared value type.</typeparam>
    /// <param name="stream">The stream that supplies the document.</param>
    /// <param name="typeInfo">The type information that supplies the root converter and options.</param>
    /// <returns>The decoded value.</returns>
    public static T Deserialize<T>(Stream stream, NbtTypeInfo<T> typeInfo) =>
        Deserialize(stream, typeInfo, out _);

    /// <summary>Deserializes a document from a stream using explicit type information.</summary>
    /// <typeparam name="T">The declared value type.</typeparam>
    /// <param name="stream">The stream that supplies the document.</param>
    /// <param name="typeInfo">The type information that supplies the root converter and options.</param>
    /// <param name="rootName">The root name, or <see langword="null"/> for an unnamed root.</param>
    /// <returns>The decoded value.</returns>
    public static T Deserialize<T>(
        Stream stream,
        NbtTypeInfo<T> typeInfo,
        out string? rootName)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(typeInfo);
        ValidateTypeInfo(typeInfo);

        var options = typeInfo.Options;
        var reader = new NbtReader(stream, options.CreateReaderOptions());
        try
        {
            return DeserializeCore<T>(ref reader, GetEffectiveConverter(typeInfo), options, out rootName);
        }
        finally
        {
            reader.Dispose();
        }
    }

    private static NbtConverter GetEffectiveConverter<T>(NbtTypeInfo<T> typeInfo) =>
        typeInfo.Options.GetCustomConverter(typeof(T)) ?? typeInfo.Converter;

    private static void ValidateTypeInfo<T>(NbtTypeInfo<T> typeInfo)
    {
        if (typeof(T) == typeof(NbtRoot))
        {
            throw new NotSupportedException(
                $"{typeInfo.Type} is handled by the DOM overloads, not by type information.");
        }
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
        return DeserializeValueCore(ref reader, type, options.GetConverter(type), options, pathSegment);
    }

    internal static object? DeserializeValueCore(
        ref NbtReader reader,
        Type type,
        NbtConverter converter,
        NbtSerializerOptions options,
        string? pathSegment)
    {
        try
        {
            var frame = new ReadFrame(ref reader);
            var value = converter.ReadValue(ref reader, type, options);
            ValidateRead(ref reader, type, in frame);
            return value;
        }
        catch (NbtException ex)
        {
            PrependPath(ex, pathSegment);
            throw;
        }
    }

    internal static T DeserializeValueCore<T>(
        ref NbtReader reader,
        NbtConverter<T> converter,
        NbtSerializerOptions options,
        string? pathSegment)
    {
        try
        {
            var frame = new ReadFrame(ref reader);
            var value = converter.Read(ref reader, options);
            ValidateRead(ref reader, typeof(T), in frame);
            return value;
        }
        catch (NbtException ex)
        {
            PrependPath(ex, pathSegment);
            throw;
        }
    }

    private readonly struct ReadFrame
    {
        public ReadFrame(ref NbtReader reader)
        {
            StartToken = reader.TokenType;
            StartDepth = reader.CurrentDepth;
            ParentDepth = StartToken is NbtTokenType.StartCompound or NbtTokenType.StartList
                ? StartDepth - 1
                : StartDepth;
            BeforePosition = reader.BytesConsumed;
            BeforeValues = reader.ValuesReadAtDepth(ParentDepth);
        }

        public readonly NbtTokenType StartToken;

        public readonly int StartDepth;

        public readonly int ParentDepth;

        public readonly long BeforePosition;

        public readonly long BeforeValues;
    }

    private static void ValidateRead(ref NbtReader reader, Type type, in ReadFrame frame)
    {
        var expectedDepth = frame.StartToken is NbtTokenType.StartCompound or NbtTokenType.StartList
            ? frame.StartDepth - 1
            : frame.StartDepth;
        var expectedEndToken = frame.StartToken switch
        {
            NbtTokenType.StartCompound => NbtTokenType.EndCompound,
            NbtTokenType.StartList => NbtTokenType.EndList,
            _ => reader.TokenType,
        };

        if (reader.CurrentDepth != expectedDepth ||
            reader.TokenType != expectedEndToken ||
            reader.ValuesReadAtDepth(frame.ParentDepth) != frame.BeforeValues ||
            reader.HasPendingValue ||
            reader.HasPendingContainer)
        {
            throw new NbtException($"The converter for {type} did not consume exactly one complete value.")
            {
                BytePosition = frame.BeforePosition,
            };
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

        NbtConverter converter;
        try
        {
            if (value is null)
            {
                HandleNullMember(ref writer, options);
                return;
            }

            converter = options.GetConverter(type);
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

        try
        {
            SerializeValueCore(
                ref writer,
                value,
                type,
                converter,
                options,
                pathSegment: null,
                headerWritten: true);
        }
        catch (NbtException ex)
        {
            PrependPath(ex, "." + name);
            throw;
        }
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
        if (value is null)
        {
            HandleNullValue(ref writer);
            return;
        }

        SerializeValueCore(
            ref writer,
            value,
            type,
            options.GetConverter(type),
            options,
            pathSegment,
            headerWritten);
    }

    internal static void SerializeValueCore(
        ref NbtWriter writer,
        object? value,
        Type type,
        NbtConverter converter,
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

            var frame = new WriteFrame(ref writer, headerWritten);
            converter.WriteValue(ref writer, value, type, options);
            ValidateWrite(ref writer, type, in frame);
        }
        catch (NbtException ex)
        {
            PrependPath(ex, pathSegment);
            throw;
        }
    }

    internal static void SerializeMemberCore<T>(
        ref NbtWriter writer,
        string name,
        T value,
        NbtConverter<T> converter,
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

            if (!converter.TryGetTagType(value, options, out var tagType))
            {
                throw new NotSupportedException(
                    $"The converter for {typeof(T)} must implement TryGetTagType to write a named member.");
            }

            writer.WriteMemberHeader(tagType, name);
        }
        catch (NbtException ex)
        {
            PrependPath(ex, "." + name);
            throw;
        }

        try
        {
            SerializeValueCore(
                ref writer,
                value,
                converter,
                options,
                pathSegment: null,
                headerWritten: true);
        }
        catch (NbtException ex)
        {
            PrependPath(ex, "." + name);
            throw;
        }
    }

    internal static void SerializeValueCore<T>(
        ref NbtWriter writer,
        T value,
        NbtConverter<T> converter,
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

            var frame = new WriteFrame(ref writer, headerWritten);
            converter.Write(ref writer, value, options);
            ValidateWrite(ref writer, typeof(T), in frame);
        }
        catch (NbtException ex)
        {
            PrependPath(ex, pathSegment);
            throw;
        }
    }

    private readonly struct WriteFrame
    {
        public WriteFrame(ref NbtWriter writer, bool headerWritten)
        {
            Depth = writer.CurrentDepth;
            ValuesAtDepth = writer.ValuesWrittenAtDepth(Depth);
            PayloadWasPrepared = headerWritten || writer.IsPayloadExpected;
        }

        public readonly int Depth;

        public readonly long ValuesAtDepth;

        public readonly bool PayloadWasPrepared;
    }

    private static void ValidateWrite(ref NbtWriter writer, Type type, in WriteFrame frame)
    {
        if (frame.PayloadWasPrepared)
        {
            if (writer.IsPayloadExpected ||
                writer.CurrentDepth != frame.Depth ||
                writer.ValuesWrittenAtDepth(frame.Depth) != frame.ValuesAtDepth)
            {
                throw new NbtException(
                    $"The converter for {type} did not complete the prepared value.")
                {
                    BytePosition = writer.BytesWritten,
                };
            }
        }
        else if (writer.CurrentDepth != frame.Depth ||
                 writer.ValuesWrittenAtDepth(frame.Depth) != frame.ValuesAtDepth + 1)
        {
            throw new NbtException($"The converter for {type} must write exactly one value.")
            {
                BytePosition = writer.BytesWritten,
            };
        }
    }

    private static void SerializeRootValue(
        ref NbtWriter writer,
        object? value,
        Type type,
        string rootName,
        NbtSerializerOptions options,
        NbtConverter converter)
    {
        if (value is null)
        {
            throw new NbtException("The root value cannot be null.");
        }

        if (!converter.TryGetTagType(value, type, options, out var tagType))
        {
            throw new NotSupportedException(
                $"The converter for {type} must implement TryGetTagType to write a root value.");
        }

        writer.WriteRootHeader(tagType, rootName);
        SerializeValueCore(ref writer, value, type, converter, options, pathSegment: null, headerWritten: true);
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
        SerializeRootValue(ref writer, value, type, rootName, options, options.GetConverter(type));
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
        var converter = typeof(T) == typeof(NbtRoot)
            ? options.GetConverter(typeof(NbtValue))
            : options.GetConverter(typeof(T));
        return DeserializeCore<T>(ref reader, converter, options, out rootName);
    }

    private static T DeserializeCore<T>(
        ref NbtReader reader,
        NbtConverter converter,
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
                var nbtValue = DeserializeValueCore(
                    ref reader,
                    typeof(NbtValue),
                    converter,
                    options,
                    pathSegment: null);
                value = new NbtRoot(rootName, (NbtValue)nbtValue!);
            }
            else
            {
                value = DeserializeValueCore(
                    ref reader,
                    typeof(T),
                    converter,
                    options,
                    pathSegment: null);
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

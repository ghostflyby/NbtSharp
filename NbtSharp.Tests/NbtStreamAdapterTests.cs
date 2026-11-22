using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;
using Xunit.Sdk;

namespace NbtSharp.Tests;

public sealed class NbtStreamAdapterTests
{
    [Fact]
    public void GenericStreamRoundTrips()
    {
        using var stream = new MemoryStream();
        NbtSerializer.Serialize(stream, 42, "answer");
        stream.Position = 0;

        Assert.Equal(42, NbtSerializer.Deserialize<int>(stream));
    }

    [Fact]
    public void DomStreamRoundTrips()
    {
        using var stream = new MemoryStream();
        var root = new NbtRoot("root", new NbtCompound { ["X"] = 1 });
        NbtSerializer.Serialize(stream, root);
        stream.Position = 0;

        var actual = NbtSerializer.Deserialize(stream);

        Assert.Equal("root", actual.Name);
        var compound = (NbtCompound)actual.Value.Value!;
        Assert.Equal(1, compound["X"] is int value ? value : 0);
    }

    [Fact]
    public void LargeStreamRoundTrips()
    {
        var payload = new byte[1024 * 1024];
        Random.Shared.NextBytes(payload);
        using var stream = new MemoryStream();
        NbtSerializer.Serialize(stream, payload, "root");
        stream.Position = 0;

        Assert.Equal(payload, NbtSerializer.Deserialize<byte[]>(stream));
    }

    [Fact]
    public void LargeIntArrayStreamRoundTrips()
    {
        var payload = Enumerable.Range(0, 200_000).ToArray();
        using var stream = new MemoryStream();
        NbtSerializer.Serialize(stream, payload, "root");
        stream.Position = 0;

        Assert.Equal(payload, NbtSerializer.Deserialize<int[]>(stream));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(64)]
    public void StreamRoundTripsAcrossChunkBoundaries(int chunkSize)
    {
        var data = CreateDocumentForStreaming();
        using var inner = new MemoryStream(data);
        using var stream = new ChunkedStream(inner, chunkSize);

        var actual = NbtSerializer.Deserialize(stream);

        AssertDocumentFromStreaming(actual);
    }

    [Fact]
    public void StreamSkipsUnknownMembersAcrossSegments()
    {
        var data = NbtSerializer.Serialize(new NbtRoot("root", new NbtCompound
        {
            ["Known"] = 42,
            ["Unknown"] = new NbtCompound
            {
                ["Text"] = "a value that spans several pipe segments",
                ["Numbers"] = new[] { 1, 2, 3 },
            },
        }));
        using var inner = new MemoryStream(data);
        using var stream = new ChunkedStream(inner, 3);
        var options = new NbtSerializerOptions { TypeInfoResolver = new NbtReflectionTypeInfoResolver() };

        var actual = NbtSerializer.Deserialize<KnownMemberDto>(stream, options);

        Assert.Equal(42, actual.Known);
    }

    [Fact]
    public async Task SerializeAsyncRoundTripsAStream()
    {
        using var stream = new MemoryStream();
        var value = new NbtRoot("root", 42);

        await NbtSerializer.SerializeDocumentAsync(
            stream,
            value,
            options: null,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(stream.CanWrite);
        stream.Position = 0;
        var actual = NbtSerializer.Deserialize(stream);
        Assert.Equal("root", actual.Name);
        Assert.Equal(42, actual.Value.Value is int stored ? stored : 0);
    }

    [Fact]
    public async Task SerializeAsyncRoundTripsAPipeWriterWithoutCompletingIt()
    {
        var pipe = new Pipe();
        var value = new NbtRoot("root", 43);

        await NbtSerializer.SerializeDocumentAsync(
            pipe.Writer,
            value,
            options: null,
            cancellationToken: TestContext.Current.CancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var read = await pipe.Reader.ReadAsync(timeout.Token);
        var data = read.Buffer.ToArray();
        pipe.Reader.AdvanceTo(read.Buffer.End);
        await pipe.Writer.CompleteAsync();
        await pipe.Reader.CompleteAsync();

        var actual = NbtSerializer.Deserialize(data);
        Assert.Equal("root", actual.Name);
        Assert.Equal(43, actual.Value.Value is int stored ? stored : 0);
    }

    [Fact]
    public async Task SerializeAsyncValidatesTheRootFormatBeforeWriting()
    {
        using var stream = new MemoryStream();
        var options = new NbtSerializerOptions { RootFormat = NbtRootFormat.Unnamed };

        await Assert.ThrowsAsync<NbtException>(() =>
            NbtSerializer.SerializeDocumentAsync(
                stream,
                new NbtRoot("root", 1),
                options,
                TestContext.Current.CancellationToken));

        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public async Task SerializeAsyncDoesNotFlushAfterMidWriteCancellation()
    {
        using var stream = new MemoryStream();
        using var cancellation = new CancellationTokenSource();
        var options = new NbtSerializerOptions();
        options.Converters.Add(new CancelingIntConverter(cancellation));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            NbtSerializer.SerializeAsync(
                stream,
                1,
                options: options,
                cancellationToken: cancellation.Token));

        Assert.Equal(0, stream.Length);
        Assert.True(stream.CanWrite);
    }

    [Fact]
    public async Task SerializeAsyncHonorsACanceledPipeToken()
    {
        var pipe = new Pipe();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            NbtSerializer.SerializeAsync(
                pipe.Writer,
                new NbtRoot("root", 1),
                cancellationToken: cancellation.Token));

        Assert.False(pipe.Reader.TryRead(out _));
        await pipe.Writer.CompleteAsync();
        await pipe.Reader.CompleteAsync();
    }

    [Fact]
    public async Task SerializeAsyncRejectsANamedRootInUnnamedFormat()
    {
        using var stream = new MemoryStream();
        var options = new NbtSerializerOptions { RootFormat = NbtRootFormat.Unnamed };

        await Assert.ThrowsAsync<NbtException>(() =>
            NbtSerializer.SerializeAsync(
                stream,
                1,
                rootName: "root",
                options: options,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public async Task SerializeAsyncPropagatesConverterFailure()
    {
        using var stream = new MemoryStream();
        var options = new NbtSerializerOptions();
        options.Converters.Add(new ThrowingIntConverter());

        var exception = await Assert.ThrowsAsync<NbtException>(() =>
            NbtSerializer.SerializeAsync(
                stream,
                1,
                options: options,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("intentional", exception.Message, StringComparison.Ordinal);
        Assert.True(stream.CanWrite);
    }

    [Fact]
    public async Task SerializeAsyncHonorsACanceledToken()
    {
        using var stream = new MemoryStream();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            NbtSerializer.SerializeAsync(
                stream,
                new NbtRoot("root", 1),
                cancellationToken: cancellation.Token));

        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public async Task DeserializeAsyncRoundTripsAStream()
    {
        var data = CreateDocumentForStreaming();
        using var inner = new MemoryStream(data);
        using var stream = new ChunkedStream(inner, 3);

        var actual = await NbtSerializer.DeserializeAsync<NbtRoot>(
            stream,
            cancellationToken: TestContext.Current.CancellationToken);

        AssertDocumentFromStreaming(actual);
    }

    [Fact]
    public async Task DeserializeAsyncRoundTripsASequence()
    {
        var data = CreateDocumentForStreaming();
        var sequence = new ReadOnlySequence<byte>(data);

        var actual = await NbtSerializer.DeserializeAsync<NbtRoot>(
            sequence,
            cancellationToken: TestContext.Current.CancellationToken);

        AssertDocumentFromStreaming(actual);
    }

    [Fact]
    public async Task DeserializeAsyncWaitsForAConcurrentProducer()
    {
        var data = NbtSerializer.Serialize(42, "root");
        var pipe = new Pipe();
        var producer = Task.Run(async () =>
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
            await pipe.Writer.WriteAsync(data, TestContext.Current.CancellationToken);
            await pipe.Writer.CompleteAsync();
        }, TestContext.Current.CancellationToken);

        var actual = await NbtSerializer.DeserializeAsync<int>(
            pipe.Reader,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(42, actual);
        await producer;
        await pipe.Reader.CompleteAsync();
    }

    [Fact]
    public void TruncatedStreamThrows()
    {
        var data = NbtSerializer.Serialize(new string('x', 100), "root");
        using var stream = new MemoryStream(data[..^5]);

        Assert.Throws<NbtException>(() => NbtSerializer.Deserialize<string>(stream));
    }

    [Fact]
    public void TruncatedIntArrayStreamThrows()
    {
        var data = NbtSerializer.Serialize(new[] { 1, 2, 3 }, "root");
        using var stream = new MemoryStream(data[..^4]);

        Assert.Throws<NbtException>(() => NbtSerializer.Deserialize<int[]>(stream));
    }

    [Fact]
    public void StreamReaderCannotBeUsedAfterDispose()
    {
        var data = NbtSerializer.Serialize(42, "root");
        using var stream = new MemoryStream(data);
        var reader = new NbtReader(stream);

        Assert.True(reader.Read());
        Assert.Equal(42, reader.GetInt32());
        reader.Dispose();

        Throws<ObjectDisposedException>(ref reader, static (ref value) => value.Read());
    }

    [Fact]
    public void StreamReaderGetterCannotBeUsedAfterDispose()
    {
        var data = NbtSerializer.Serialize(42, "root");
        using var stream = new MemoryStream(data);
        var reader = new NbtReader(stream);

        Assert.True(reader.Read());
        reader.Dispose();
        reader.Dispose();

        Throws<ObjectDisposedException>(ref reader, static (ref value) => value.GetInt32());
    }

    [Fact]
    public async Task DeserializeAsyncStopsAtTheRootOfAnOpenPipe()
    {
        var data = NbtSerializer.Serialize(42, "root");
        var pipe = new Pipe();
        await pipe.Writer.WriteAsync(data, TestContext.Current.CancellationToken);

        var actual = await NbtSerializer.DeserializeAsync<int>(
            pipe.Reader,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(42, actual);
        await pipe.Reader.CompleteAsync();
        await pipe.Writer.CompleteAsync();
    }

    [Fact]
    public async Task DeserializeAsyncConsumesOneDocumentFromAPipe()
    {
        var first = NbtSerializer.Serialize(1, "a");
        var second = NbtSerializer.Serialize(2, "b");
        var pipe = new Pipe();
        var buffer = pipe.Writer.GetSpan(first.Length + second.Length);
        first.CopyTo(buffer);
        second.CopyTo(buffer[first.Length..]);
        pipe.Writer.Advance(first.Length + second.Length);
        pipe.Writer.Complete();

        var firstValue = await NbtSerializer.DeserializeAsync<int>(
            pipe.Reader,
            cancellationToken: TestContext.Current.CancellationToken);
        var secondValue = await NbtSerializer.DeserializeAsync<int>(
            pipe.Reader,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, firstValue);
        Assert.Equal(2, secondValue);
        await pipe.Reader.CompleteAsync();
    }

    [Fact]
    public async Task DeserializeAsyncCompletesFromAnOpenPipeAcrossReads()
    {
        var text = new string('x', 4000);
        var data = NbtSerializer.Serialize(text, "root");
        var firstLength = data.Length * 3 / 5;
        var pipe = new Pipe();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        await pipe.Writer.WriteAsync(data.AsMemory(0, firstLength), cts.Token);
        var task = NbtSerializer.DeserializeAsync<string>(pipe.Reader, cancellationToken: cts.Token);
        await pipe.Writer.WriteAsync(data.AsMemory(firstLength), cts.Token);

        var actual = await task;

        Assert.Equal(text, actual);
        await pipe.Reader.CompleteAsync();
        await pipe.Writer.CompleteAsync();
    }

    [Fact]
    public async Task DeserializeAsyncLeavesThePipeUsableAfterAFailure()
    {
        var data = NbtSerializer.Serialize(42, "root");
        var pipe = new Pipe();
        data.CopyTo(pipe.Writer.GetSpan(data.Length));
        pipe.Writer.Advance(data.Length);
        pipe.Writer.Complete();
        var strict = new NbtSerializerOptions { MaxDocumentBytes = 4 };

        await Assert.ThrowsAsync<NbtException>(() =>
            NbtSerializer.DeserializeAsync<int>(
                pipe.Reader,
                strict,
                TestContext.Current.CancellationToken));

        var relaxed = new NbtSerializerOptions { MaxDocumentBytes = 100 };
        Assert.Equal(42, await NbtSerializer.DeserializeAsync<int>(
            pipe.Reader,
            relaxed,
            TestContext.Current.CancellationToken));
        await pipe.Reader.CompleteAsync();
    }

    [Fact]
    public async Task DeserializeAsyncHonorsCancelPendingRead()
    {
        var pipe = new Pipe();
        var task = NbtSerializer.DeserializeAsync<int>(
            pipe.Reader,
            cancellationToken: TestContext.Current.CancellationToken);

        pipe.Reader.CancelPendingRead();

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await task);
        await pipe.Reader.CompleteAsync();
        await pipe.Writer.CompleteAsync();
    }

    [Fact]
    public async Task DeserializeAsyncRejectsDeclaredPayloadBeyondTheLimit()
    {
        // Named root TAG_IntArray "root" declaring 1000 elements with no payload.
        var data = new byte[]
        {
            0x0B, 0x00, 0x04, (byte)'r', (byte)'o', (byte)'o', (byte)'t',
            0x00, 0x00, 0x03, 0xE8,
        };
        var pipe = new Pipe();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(5));
        await pipe.Writer.WriteAsync(data, cts.Token);
        var options = new NbtSerializerOptions { MaxDocumentBytes = 32 };

        await Assert.ThrowsAsync<NbtException>(() =>
            NbtSerializer.DeserializeAsync<int[]>(pipe.Reader, options, cts.Token));

        await pipe.Reader.CompleteAsync();
        await pipe.Writer.CompleteAsync();
    }

    [Fact]
    public async Task DeserializeAsyncThrowsForATruncatedSequence()
    {
        var data = NbtSerializer.Serialize(new string('x', 100), "root");
        var sequence = new ReadOnlySequence<byte>(data[..^5]);

        await Assert.ThrowsAsync<NbtException>(() =>
            NbtSerializer.DeserializeAsync<string>(
                sequence,
                cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeserializeAsyncHonorsMaxDocumentBytes()
    {
        var data = NbtSerializer.Serialize(42, "root");
        var pipe = new Pipe();
        data.CopyTo(pipe.Writer.GetSpan(data.Length));
        pipe.Writer.Advance(data.Length);
        pipe.Writer.Complete();
        var options = new NbtSerializerOptions { MaxDocumentBytes = 4 };

        await Assert.ThrowsAsync<NbtException>(async () =>
            await NbtSerializer.DeserializeAsync<int>(
                pipe.Reader,
                options,
                TestContext.Current.CancellationToken));

        await pipe.Reader.CompleteAsync();
    }

    [Fact]
    public void NonSeekableStreamRoundTrips()
    {
        var payload = new byte[100_000];
        Random.Shared.NextBytes(payload);
        var data = NbtSerializer.Serialize(payload, "root");
        using var inner = new MemoryStream(data);
        using var stream = new NonSeekableStream(inner);

        Assert.Equal(payload, NbtSerializer.Deserialize<byte[]>(stream));
    }

    [Fact]
    public void StreamDeserializationReadsTheDocument()
    {
        using var stream = new MemoryStream(NbtSerializer.Serialize(42, "root"));

        var actual = NbtSerializer.Deserialize<int>(stream);

        Assert.Equal(42, actual);
        Assert.True(stream.Position > 0);
    }

    [Fact]
    public void StreamDeserializationIgnoresTrailingBytes()
    {
        var data = NbtSerializer.Serialize(42, "root");
        var padded = new byte[data.Length + 32];
        data.CopyTo(padded, 0);
        using var stream = new MemoryStream(padded);

        Assert.Equal(42, NbtSerializer.Deserialize<int>(stream));
    }

    [Fact]
    public void StreamDeserializationReportsTheRootName()
    {
        using var stream = new MemoryStream(NbtSerializer.Serialize(42, "answer"));

        var actual = NbtSerializer.Deserialize<int>(stream, out var rootName);

        Assert.Equal(42, actual);
        Assert.Equal("answer", rootName);
    }

    [Fact]
    public void StreamDeserializationHonorsMaxDocumentBytes()
    {
        var data = NbtSerializer.Serialize(42, "root");
        using var stream = new MemoryStream(data);
        var options = new NbtSerializerOptions { MaxDocumentBytes = 4 };

        Assert.Throws<NbtException>(() => NbtSerializer.Deserialize<int>(stream, options));
    }

    [Fact]
    public void StreamDeserializationHonorsMaxDocumentBytesInsideAPayload()
    {
        var data = NbtSerializer.Serialize(new string('x', 100), "root");
        using var stream = new MemoryStream(data);
        var options = new NbtSerializerOptions { MaxDocumentBytes = 20 };

        Assert.Throws<NbtException>(() => NbtSerializer.Deserialize<string>(stream, options));
    }

    [Fact]
    public void StreamDeserializationRejectsDeclaredArrayBeyondTheDocumentLimit()
    {
        // Named root TAG_IntArray "root" with length 1000 and no payload.
        var data = new byte[]
        {
            0x0B, 0x00, 0x04, (byte)'r', (byte)'o', (byte)'o', (byte)'t',
            0x00, 0x00, 0x03, 0xE8,
        };
        using var stream = new MemoryStream(data);
        var options = new NbtSerializerOptions { MaxDocumentBytes = 32 };

        Assert.Throws<NbtException>(() => NbtSerializer.Deserialize<int[]>(stream, options));
    }

    [Fact]
    public void StreamSerializationHonorsMaxDocumentBytes()
    {
        using var stream = new MemoryStream();
        var options = new NbtSerializerOptions { MaxDocumentBytes = 2 };

        Assert.Throws<NbtException>(() => NbtSerializer.Serialize(stream, 42, "root", options));
    }

    [Fact]
    public void UnnamedRootRejectsANameInTheGenericApi()
    {
        var options = new NbtSerializerOptions { RootFormat = NbtRootFormat.Unnamed };

        Assert.Throws<NbtException>(() => NbtSerializer.Serialize(42, "named", options));
    }

    private static byte[] CreateDocumentForStreaming()
    {
        var root = new NbtRoot("root", new NbtCompound
        {
            ["Name"] = "Hello, NBT! 你好",
            ["Values"] = new[] { 1, -2, 3, 4000 },
            ["Bytes"] = new sbyte[] { 0, 1, 2, 3, 4, 5, 6, 7 },
            ["Nested"] = new NbtCompound
            {
                ["Long"] = 1_234_567_890_123_456_789L,
                ["Float"] = 1.5f,
            },
            ["Strings"] = new NbtList(new List<string> { "", "a", "bb", "ccc" }),
            ["Ints"] = new NbtList(new List<int> { int.MinValue, 0, int.MaxValue }),
        });

        return NbtSerializer.Serialize(root);
    }

    private static void AssertDocumentFromStreaming(NbtRoot actual)
    {
        Assert.Equal("root", actual.Name);
        var compound = (NbtCompound)actual.Value.Value!;
        Assert.Equal("Hello, NBT! 你好", compound["Name"] is string name ? name : string.Empty);
        Assert.Equal(new[] { 1, -2, 3, 4000 }, compound["Values"] is int[] values ? values : []);
        Assert.Equal(
            new sbyte[] { 0, 1, 2, 3, 4, 5, 6, 7 },
            compound["Bytes"] is sbyte[] bytes ? bytes : []);

        var nested = (NbtCompound)compound["Nested"].Value!;
        Assert.Equal(1_234_567_890_123_456_789L, nested["Long"] is long longValue ? longValue : 0L);
        Assert.Equal(1.5f, nested["Float"] is float floatValue ? floatValue : float.NaN);

        var strings = compound["Strings"] is NbtList stringList ? stringList : default;
        Assert.Equal(new[] { "", "a", "bb", "ccc" }, ((List<string>)strings.Value).ToArray());

        var ints = compound["Ints"] is NbtList intList ? intList : default;
        Assert.Equal(
            new[] { int.MinValue, 0, int.MaxValue },
            ((List<int>)ints.Value).ToArray());
    }

    private delegate void NbtReaderAction(ref NbtReader reader);

    private static void Throws<TException>(ref NbtReader reader, NbtReaderAction action)
        where TException : Exception
    {
        try
        {
            action(ref reader);
        }
        catch (TException)
        {
            return;
        }
        catch (Exception exception)
        {
            throw new XunitException(
                $"Expected {typeof(TException).Name} but caught {exception.GetType().Name}.",
                exception);
        }

        throw new XunitException($"Expected {typeof(TException).Name} but nothing was thrown.");
    }

    private sealed class CancelingIntConverter(CancellationTokenSource cancellation)
        : NbtConverter<int>
    {
        public override int Read(ref NbtReader reader, NbtSerializerOptions options) => 0;

        public override void Write(
            ref NbtWriter writer,
            int value,
            NbtSerializerOptions options)
        {
            writer.WriteInt32(value);
            cancellation.Cancel();
        }

        public override bool TryGetTagType(
            int value,
            NbtSerializerOptions options,
            out NbtTagType tagType)
        {
            tagType = NbtTagType.Int;
            return true;
        }
    }

    private sealed class ThrowingIntConverter : NbtConverter<int>
    {
        public override int Read(ref NbtReader reader, NbtSerializerOptions options) => 0;

        public override void Write(
            ref NbtWriter writer,
            int value,
            NbtSerializerOptions options)
        {
            writer.WriteInt32(value);
            throw new NbtException("intentional converter failure");
        }

        public override bool TryGetTagType(
            int value,
            NbtSerializerOptions options,
            out NbtTagType tagType)
        {
            tagType = NbtTagType.Int;
            return true;
        }
    }

    private sealed class KnownMemberDto
    {
        [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Local")]
        public int Known { get; set; }
    }

    private sealed class NonSeekableStream(Stream inner) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer) => inner.Read(buffer);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class ChunkedStream(Stream inner, int chunkSize) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            inner.Read(buffer, offset, Math.Min(count, chunkSize));

        public override int Read(Span<byte> buffer) =>
            inner.Read(buffer[..Math.Min(buffer.Length, chunkSize)]);

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer[..Math.Min(buffer.Length, chunkSize)], cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public void UnnamedRootRoundTripsThroughTheGenericApi()
    {
        var options = new NbtSerializerOptions { RootFormat = NbtRootFormat.Unnamed };
        var data = NbtSerializer.Serialize(42, options: options);

        Assert.Equal(42, NbtSerializer.Deserialize<int>(data, out var rootName, options));
        Assert.Null(rootName);
    }

    private sealed class CountingConverter : NbtConverter<string>
    {
        public int ReadCount;

        public override string Read(ref NbtReader reader, NbtSerializerOptions _) =>
            ReadCount++ is var count && reader.TagType == NbtTagType.String
                ? reader.GetString()
                : throw new NbtException($"Unexpected tag {reader.TagType} (attempt {count}).");

        public override void Write(ref NbtWriter writer, string value, NbtSerializerOptions _) =>
            writer.WriteString(value);

        public override bool TryGetTagType(string value, NbtSerializerOptions _, out NbtTagType tagType)
        {
            tagType = NbtTagType.String;
            return true;
        }
    }

    private static NbtSerializerOptions OptionsWithCounter(CountingConverter converter)
    {
        var options = new NbtSerializerOptions();
        options.Converters.Add(converter);
        return options;
    }

    private static async Task PushInChunksAsync(
        PipeWriter writer,
        ReadOnlyMemory<byte> data,
        int chunkSize,
        bool complete,
        int skipFirstBytes = 0)
    {
        // The first push covers at least the full root header (tag type + name + payload length
        // prefix) so the first parse attempt reaches the root converter and is counted.
        var firstPush = Math.Max(skipFirstBytes, 9);
        await writer.WriteAsync(data[..Math.Min(firstPush, data.Length)], TestContext.Current.CancellationToken);
        for (var offset = firstPush; offset < data.Length; offset += chunkSize)
        {
            await writer.WriteAsync(
                data[offset..Math.Min(offset + chunkSize, data.Length)],
                TestContext.Current.CancellationToken);
        }

        if (complete)
        {
            await writer.FlushAsync(TestContext.Current.CancellationToken);
            await writer.CompleteAsync();
        }
    }

    [Fact]
    public async Task DeserializeAsyncParsesASingleBlockDocumentOnce()
    {
        var data = NbtSerializer.Serialize("hello", "root");
        var pipe = new Pipe();
        var converter = new CountingConverter();
        var producer = PushInChunksAsync(pipe.Writer, data, chunkSize: data.Length, complete: true);

        var actual = await NbtSerializer.DeserializeAsync<string>(
            pipe.Reader,
            OptionsWithCounter(converter),
            TestContext.Current.CancellationToken);
        await producer;

        Assert.Equal("hello", actual);
        Assert.Equal(1, converter.ReadCount);
        await pipe.Reader.CompleteAsync();
    }

    [Fact]
    public async Task DeserializeAsyncReparsesAnOpenPipeOnlyAfterTheDeficitArrives()
    {
        var text = new string('x', 4000);
        var data = NbtSerializer.Serialize(text, "root");
        var firstLength = data.Length * 3 / 5;
        var pipe = new Pipe();
        var converter = new CountingConverter();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        await pipe.Writer.WriteAsync(data.AsMemory(0, firstLength), cts.Token);
        var task = NbtSerializer.DeserializeAsync<string>(
            pipe.Reader,
            OptionsWithCounter(converter),
            cts.Token);
        await pipe.Writer.WriteAsync(data.AsMemory(firstLength), cts.Token);

        Assert.Equal(text, await task);
        // The truncation reports the exact payload deficit, so the second write satisfies the
        // growth requirement and the document completes without the writer finishing the pipe.
        Assert.True(converter.ReadCount <= 2, $"ReadCount was {converter.ReadCount}.");
        await pipe.Reader.CompleteAsync();
        await pipe.Writer.CompleteAsync();
    }

    [Fact]
    public async Task DeserializeAsyncBoundsReparseAttemptsForTrickledPayloads()
    {
        var text = new string('y', 32 * 1024);
        var data = NbtSerializer.Serialize(text, "root");
        var pipe = new Pipe();
        var converter = new CountingConverter();

        var producer = PushInChunksAsync(pipe.Writer, data, chunkSize: 1, complete: true);
        var actual = await NbtSerializer.DeserializeAsync<string>(
            pipe.Reader,
            OptionsWithCounter(converter),
            TestContext.Current.CancellationToken);
        await producer;

        Assert.Equal(text, actual);
        Assert.True(converter.ReadCount <= 4, $"ReadCount was {converter.ReadCount}.");
        await pipe.Reader.CompleteAsync();
    }

    [Fact]
    public async Task DeserializeAsyncBoundsReparseAttemptsForDeepDocumentsWithLargeChunks()
    {
        var compound = new NbtCompound();
        var current = compound;
        for (var depth = 0; depth < 250; depth++)
        {
            var nested = new NbtCompound();
            current[$"N{depth}"] = nested;
            current["V"] = depth;
            current = nested;
        }

        current["Leaf"] = "end";
        var data = NbtSerializer.Serialize(new NbtRoot("root", compound));
        var pipe = new Pipe();
        var options = new NbtSerializerOptions
        {
            TypeInfoResolver = new NbtReflectionTypeInfoResolver(),
        };
        var expected = NbtSerializer.Deserialize<NbtRoot>(data, options);

        var producer = PushInChunksAsync(pipe.Writer, data, chunkSize: 16 * 1024, complete: true);
        var actual = await NbtSerializer.DeserializeAsync<NbtRoot>(
            pipe.Reader,
            options,
            TestContext.Current.CancellationToken);
        await producer;

        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(((NbtCompound)expected.Value.Value!).Count, ((NbtCompound)actual.Value.Value!).Count);
        await pipe.Reader.CompleteAsync();
    }

    [Fact]
    public async Task DeserializeAsyncSucceedsWithoutCompletionWhenTheLastChunkMatchesTheDeficit()
    {
        var text = new string('z', 1000);
        var data = NbtSerializer.Serialize(text, "root");
        // Truncate just before the payload's last byte: the deficit is exactly one byte, so the
        // final single-byte push satisfies the growth requirement while the writer stays open.
        var firstLength = data.Length - 1;
        var pipe = new Pipe();
        var converter = new CountingConverter();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        await pipe.Writer.WriteAsync(data.AsMemory(0, firstLength), cts.Token);
        var task = NbtSerializer.DeserializeAsync<string>(
            pipe.Reader,
            OptionsWithCounter(converter),
            cts.Token);
        await pipe.Writer.WriteAsync(data.AsMemory(firstLength, 1), cts.Token);

        Assert.Equal(text, await task);
        await pipe.Reader.CompleteAsync();
        await pipe.Writer.CompleteAsync();
    }

    [Fact]
    public async Task DeserializeAsyncRejectsAnOverLimitDocumentWhileWaitingForMoreData()
    {
        var text = new string('w', 200);
        var data = NbtSerializer.Serialize(text, "root");
        var pipe = new Pipe();
        var strict = new NbtSerializerOptions { MaxDocumentBytes = 40 };
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        await pipe.Writer.WriteAsync(data, cts.Token);
        await Assert.ThrowsAsync<NbtException>(() =>
            NbtSerializer.DeserializeAsync<string>(pipe.Reader, strict, cts.Token));

        // The pipe stays usable after the failure.
        var relaxed = new NbtSerializerOptions { MaxDocumentBytes = data.Length + 16 };
        await pipe.Writer.FlushAsync(cts.Token);
        await pipe.Writer.CompleteAsync();
        Assert.Equal(text, await NbtSerializer.DeserializeAsync<string>(pipe.Reader, relaxed, cts.Token));
        await pipe.Reader.CompleteAsync();
    }

    [Fact]
    public async Task DeserializeAsyncThrowsWhenTheWriterCompletesOnTruncatedData()
    {
        var text = new string('v', 500);
        var data = NbtSerializer.Serialize(text, "root");
        var pipe = new Pipe();
        await pipe.Writer.WriteAsync(data.AsMemory(0, 64), TestContext.Current.CancellationToken);
        await pipe.Writer.CompleteAsync();

        await Assert.ThrowsAsync<NbtException>(() =>
            NbtSerializer.DeserializeAsync<string>(
                pipe.Reader,
                cancellationToken: TestContext.Current.CancellationToken));
        await pipe.Reader.CompleteAsync();
    }

    [Fact]
    public async Task DeserializeAsyncAcceptsAMultiDocumentBufferAboveTheLimit()
    {
        // A pipeline producer may push the rest of the current document together with the next
        // document; the buffered length then exceeds the limit while the current document is
        // within it, and the throttled reparse must succeed instead of rejecting by buffer size.
        var first = NbtSerializer.Serialize("first!", "a");
        var second = NbtSerializer.Serialize(2, "b");
        var pipe = new Pipe();
        var strict = new NbtSerializerOptions { MaxDocumentBytes = first.Length + second.Length - 1 };
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        await pipe.Writer.WriteAsync(first.AsMemory(0, 9), cts.Token);
        var task = NbtSerializer.DeserializeAsync<string>(pipe.Reader, strict, cts.Token);
        await pipe.Writer.WriteAsync(first.AsMemory(9), cts.Token);
        await pipe.Writer.WriteAsync(second, cts.Token);

        Assert.Equal("first!", await task);
        await pipe.Reader.CompleteAsync();
        await pipe.Writer.CompleteAsync();
    }
}

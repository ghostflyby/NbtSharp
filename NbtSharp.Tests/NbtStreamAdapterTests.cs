using System.Buffers;
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

        Throws<ObjectDisposedException>(ref reader, static (ref NbtReader value) => value.Read());
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

    private static TException Throws<TException>(ref NbtReader reader, NbtReaderAction action)
        where TException : Exception
    {
        try
        {
            action(ref reader);
        }
        catch (TException exception)
        {
            return exception;
        }
        catch (Exception exception)
        {
            throw new XunitException(
                $"Expected {typeof(TException).Name} but caught {exception.GetType().Name}.",
                exception);
        }

        throw new XunitException($"Expected {typeof(TException).Name} but nothing was thrown.");
    }

    private sealed class KnownMemberDto
    {
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
}

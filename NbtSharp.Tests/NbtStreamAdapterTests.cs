using System.IO.Pipelines;

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
    public void PipeReaderRoundTrips()
    {
        var data = CreateDocumentForStreaming();
        var pipe = new Pipe();
        data.CopyTo(pipe.Writer.GetSpan(data.Length));
        pipe.Writer.Advance(data.Length);
        pipe.Writer.Complete();

        var actual = NbtSerializer.Deserialize(pipe.Reader);

        AssertDocumentFromStreaming(actual);
        pipe.Reader.Complete();
    }

    [Fact]
    public async Task PipeReaderWaitsForAConcurrentProducer()
    {
        var data = NbtSerializer.Serialize(42, "root");
        var pipe = new Pipe();
        var producer = Task.Run(async () =>
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
            await pipe.Writer.WriteAsync(data, TestContext.Current.CancellationToken);
            await pipe.Writer.CompleteAsync();
        }, TestContext.Current.CancellationToken);

        Assert.Equal(42, NbtSerializer.Deserialize<int>(pipe.Reader));
        await producer;
        pipe.Reader.Complete();
    }

    [Fact]
    public void TruncatedStreamThrows()
    {
        var data = NbtSerializer.Serialize(new string('x', 100), "root");
        using var stream = new MemoryStream(data[..^5]);

        Assert.Throws<NbtException>(() => NbtSerializer.Deserialize<string>(stream));
    }

    [Fact]
    public void PipeReaderCanDeserializeConsecutiveDocuments()
    {
        var first = NbtSerializer.Serialize(1, "a");
        var second = NbtSerializer.Serialize(2, "b");
        var pipe = new Pipe();
        var span = pipe.Writer.GetSpan(first.Length + second.Length);
        first.CopyTo(span);
        second.CopyTo(span[first.Length..]);
        pipe.Writer.Advance(first.Length + second.Length);
        pipe.Writer.Complete();

        Assert.Equal(1, NbtSerializer.Deserialize<int>(pipe.Reader));
        Assert.Equal(2, NbtSerializer.Deserialize<int>(pipe.Reader));
        pipe.Reader.Complete();
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
    public void StreamDeserializationReadsToTheEnd()
    {
        using var stream = new MemoryStream(NbtSerializer.Serialize(42, "root"));

        var actual = NbtSerializer.Deserialize<int>(stream);

        Assert.Equal(42, actual);
        Assert.Equal(stream.Length, stream.Position);
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

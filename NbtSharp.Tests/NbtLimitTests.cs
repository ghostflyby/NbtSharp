using System.Buffers;

namespace NbtSharp.Tests;

public sealed class NbtLimitTests
{
    [Fact]
    public void ReaderRejectsExcessiveDepth()
    {
        var root = new NbtRoot("root", new NbtCompound
        {
            ["Inner"] = new NbtCompound { ["X"] = 1 },
        });
        var data = NbtSerializer.Serialize(root);
        var options = new NbtSerializerOptions { MaxDepth = 1 };

        Assert.Throws<NbtException>(() => NbtSerializer.Deserialize(data, options));
    }

    [Fact]
    public void WriterRejectsExcessiveDepth()
    {
        var root = new NbtRoot("root", new NbtCompound
        {
            ["Inner"] = new NbtCompound { ["X"] = 1 },
        });
        var options = new NbtSerializerOptions { MaxDepth = 1 };

        Assert.Throws<NbtException>(() => NbtSerializer.Serialize(root.Value, "root", options));
    }

    [Fact]
    public void ReaderRejectsExcessiveCollectionLength()
    {
        var data = NbtSerializer.Serialize(new List<int> { 1, 2, 3 }, "root");
        var options = new NbtSerializerOptions { MaxCollectionLength = 2 };

        Assert.Throws<NbtException>(() => NbtSerializer.Deserialize<List<int>>(data, options));
    }

    [Fact]
    public void WriterRejectsExcessiveListLength()
    {
        var options = new NbtSerializerOptions { MaxCollectionLength = 2 };

        Assert.Throws<NbtException>(() => NbtSerializer.Serialize(new List<int> { 1, 2, 3 }, "root", options));
    }

    [Fact]
    public void WriterRejectsExcessiveArrayLength()
    {
        var options = new NbtSerializerOptions { MaxCollectionLength = 2 };

        Assert.Throws<NbtException>(() => NbtSerializer.Serialize(new int[3], "root", options));
    }

    [Fact]
    public void ReaderRejectsExcessiveDocumentSize()
    {
        var data = NbtSerializer.Serialize(42, "root");
        var options = new NbtSerializerOptions { MaxDocumentBytes = data.Length - 1 };

        Assert.Throws<NbtException>(() => NbtSerializer.Deserialize<int>(data, options));
    }

    [Fact]
    public void WriterRejectsExcessiveDocumentSize()
    {
        var options = new NbtSerializerOptions { MaxDocumentBytes = 2 };

        Assert.Throws<NbtException>(() => NbtSerializer.Serialize(42, "root", options));
    }

    [Fact]
    public void InvalidEnumOptionsAreRejected()
    {
        var options = new NbtSerializerOptions();
        Assert.Throws<ArgumentOutOfRangeException>(() => options.RootFormat = (NbtRootFormat)42);
        Assert.Throws<ArgumentOutOfRangeException>(() => options.NullValueHandling = (NbtNullValueHandling)42);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new NbtReader([0x00], new NbtReaderOptions { RootFormat = (NbtRootFormat)42 }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new NbtWriter(
                new ArrayBufferWriter<byte>(),
                new NbtWriterOptions { RootFormat = (NbtRootFormat)42 }));
    }

    [Fact]
    public void NegativeLimitsAreRejected()
    {
        var options = new NbtSerializerOptions();

        Assert.Throws<ArgumentOutOfRangeException>(() => options.MaxDepth = -1);
        Assert.Throws<ArgumentOutOfRangeException>(() => options.MaxCollectionLength = -1);
        Assert.Throws<ArgumentOutOfRangeException>(() => options.MaxDocumentBytes = -1);
    }

    [Fact]
    public void WriterEnforcesTheDocumentLimitAcrossLargeBuffers()
    {
        var writer = new HugeSpanBufferWriter();
        var options = new NbtSerializerOptions { MaxDocumentBytes = 10_000 };

        Assert.Throws<NbtException>(() =>
            NbtSerializer.Serialize(writer, new byte[20_000], "root", options));
    }

    [Fact]
    public void ZeroLimitsSelectTheDefaults()
    {
        var options = new NbtSerializerOptions
        {
            MaxDepth = 0,
            MaxCollectionLength = 0,
            MaxDocumentBytes = 0,
        };

        var data = NbtSerializer.Serialize(new List<int> { 1, 2, 3 }, "root", options);

        Assert.Equal(new[] { 1, 2, 3 }, NbtSerializer.Deserialize<List<int>>(data, options));
    }

    private sealed class HugeSpanBufferWriter : IBufferWriter<byte>
    {
        private readonly byte[] _buffer = new byte[64 * 1024];
        private int _written;

        public void Advance(int count)
        {
            if (count < 0 || _written + count > _buffer.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            _written += count;
        }

        public Memory<byte> GetMemory(int sizeHint = 0) => _buffer.AsMemory(_written);

        public Span<byte> GetSpan(int sizeHint = 0) => _buffer.AsSpan(_written);
    }
}

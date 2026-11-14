using System.Buffers;

namespace NbtSharp.Tests;

public sealed class NbtSequenceTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(17)]
    [InlineData(41)]
    [InlineData(50)]
    public void DeserializesFromSegmentedSequences(int split)
    {
        var data = BuildDocument();
        var sequence = CreateSequence(data, split, data.Length - split);

        var actual = NbtSerializer.Deserialize(sequence);

        Assert.Equal("root", actual.Name);
        var compound = (NbtCompound)actual.Value.Value!;
        Assert.Equal("a long string value that crosses segments", compound["Text"] is string text ? text : "");
        Assert.Equal(new[] { 1, 2, 3 }, compound["Numbers"] is int[] numbers ? numbers : []);
    }

    [Fact]
    public void SequenceReaderReadsTheRootHeader()
    {
        var data = BuildDocument();
        var sequence = CreateSequence(data, 10, data.Length - 10);
        var reader = new NbtReader(sequence);

        Assert.Equal(data.Length, (int)sequence.Length);
        Assert.Equal((byte)0x0A, sequence.First.Span[0]);
        Assert.Equal(new byte[] { 0x0A, 0x00, 0x04 }, sequence.Slice(0, 3).ToArray());

        Assert.True(reader.Read());
        Assert.Equal(NbtTokenType.StartCompound, reader.TokenType);
        Assert.Equal("root", reader.Name);
        Assert.Equal(7, reader.BytesConsumed);
        reader.Dispose();
    }

    [Fact]
    public void TruncatedSequenceThrows()
    {
        var data = NbtSerializer.Serialize(new string('x', 100), "root");
        var sequence = new ReadOnlySequence<byte>(data[..^5]);

        Assert.Throws<NbtException>(() => NbtSerializer.Deserialize<string>(sequence));
    }

    [Fact]
    public void DeserializesFromAThreeSegmentSequence()
    {
        var data = BuildDocument();
        var third = data.Length / 3;
        var sequence = CreateSequence(
            data,
            third,
            third,
            data.Length - (third * 2));

        var actual = NbtSerializer.Deserialize<NbtCompound>(sequence);

        Assert.Equal("a long string value that crosses segments", actual["Text"] is string text ? text : "");
    }

    [Fact]
    public void DeserializesAcrossEmptySegments()
    {
        var data = BuildDocument();
        var first = new Segment(ReadOnlyMemory<byte>.Empty);
        var second = first.Append(data.AsMemory(0, 5));
        var third = second.Append(ReadOnlyMemory<byte>.Empty);
        var fourth = third.Append(data.AsMemory(5));
        var fifth = fourth.Append(ReadOnlyMemory<byte>.Empty);
        var sequence = new ReadOnlySequence<byte>(first, 0, fifth, fifth.Memory.Length);

        var actual = NbtSerializer.Deserialize(sequence);

        Assert.Equal("root", actual.Name);
        var compound = (NbtCompound)actual.Value.Value!;
        Assert.Equal("a long string value that crosses segments", compound["Text"] is string text ? text : "");
    }

    [Fact]
    public void GenericDeserializationWorksOverSegments()
    {
        var data = NbtSerializer.Serialize(new List<int> { 1, 2, 3 }, "root");
        var sequence = CreateSequence(data, 1, data.Length - 1);

        Assert.Equal(new[] { 1, 2, 3 }, NbtSerializer.Deserialize<List<int>>(sequence));
    }

    [Fact]
    public void BufferWriterOverloadWritesTheSameDocument()
    {
        var expected = NbtSerializer.Serialize(new List<int> { 1, 2, 3 }, "root");
        var buffer = new ArrayBufferWriter<byte>();

        NbtSerializer.Serialize(buffer, new List<int> { 1, 2, 3 }, "root");

        Assert.Equal(expected, buffer.WrittenSpan.ToArray());
    }

    private static byte[] BuildDocument()
    {
        var root = new NbtRoot("root", new NbtCompound
        {
            ["Text"] = "a long string value that crosses segments",
            ["Numbers"] = new[] { 1, 2, 3 },
        });
        return NbtSerializer.Serialize(root);
    }

    private static ReadOnlySequence<byte> CreateSequence(byte[] data, int firstLength, int restLength)
    {
        var first = new Segment(data.AsMemory(0, firstLength));
        var last = first.Append(data.AsMemory(firstLength, restLength));
        return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    }

    private static ReadOnlySequence<byte> CreateSequence(
        byte[] data,
        int firstLength,
        int secondLength,
        int thirdLength)
    {
        var first = new Segment(data.AsMemory(0, firstLength));
        var second = first.Append(data.AsMemory(firstLength, secondLength));
        var third = second.Append(data.AsMemory(firstLength + secondLength, thirdLength));
        return new ReadOnlySequence<byte>(first, 0, third, third.Memory.Length);
    }

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(ReadOnlyMemory<byte> memory)
        {
            Memory = memory;
        }

        public Segment Append(ReadOnlyMemory<byte> memory)
        {
            var segment = new Segment(memory)
            {
                RunningIndex = RunningIndex + Memory.Length,
            };
            Next = segment;
            return segment;
        }
    }
}

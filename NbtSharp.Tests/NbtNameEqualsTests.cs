using System.Buffers;
using Myitian.Text;

namespace NbtSharp.Tests;

public sealed class NbtNameEqualsTests
{
    private static readonly string[] NameMatrix =
    [
        "Health",
        "\0",
        "é",
        "€",
        "中文",
        "\U0001F600",
        new string('a', ushort.MaxValue),
    ];

    [Fact]
    public void MatchesPreEncodedNamesOnASpan()
    {
        var reader = new NbtReader(BuildMatrixDocument());
        RunTruthMatrix(ref reader);
    }

    [Fact]
    public void MatchesPreEncodedNamesOnAStream()
    {
        using var stream = new ChunkedStream(BuildMatrixDocument(), 1, 3);
        var reader = new NbtReader(stream);
        RunTruthMatrix(ref reader);
        reader.Dispose();
    }

    [Fact]
    public void EmptyNameMatchesTheEmptyNameBytes()
    {
        var reader = new NbtReader(BuildDocument(""));
        Assert.True(reader.Read()); // the named root
        Assert.Equal("root", reader.Name);

        Assert.True(reader.Read()); // the empty-named member
        Assert.Equal(string.Empty, reader.Name);
        Assert.True(reader.NameEquals([]));
        Assert.False(reader.NameEquals("x"u8));
    }

    [Fact]
    public void NamelessTokensNeverMatch()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer, new NbtWriterOptions { RootFormat = NbtRootFormat.Unnamed });
        writer.WriteStartCompound("ignored");
        writer.WriteInt32("root", 7);
        writer.WriteEndCompound();
        writer.Complete();

        var reader = new NbtReader(
            buffer.WrittenSpan,
            new NbtReaderOptions { RootFormat = NbtRootFormat.Unnamed });
        Assert.True(reader.Read());
        Assert.False(reader.NameEquals("root"u8));
        Assert.Null(reader.Name);

        Assert.True(reader.Read()); // the named member inside the root
        Assert.True(reader.NameEquals("root"u8));
        reader.Skip();

        Assert.True(reader.Read()); // EndCompound
        Assert.False(reader.NameEquals("root"u8));
        Assert.False(reader.NameEquals([]));
        Assert.False(reader.Read());
    }

    [Fact]
    public void ListElementsAndEndTokensNeverMatch()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer);
        writer.WriteStartCompound("root");
        writer.WriteStartList("Values", NbtTagType.Int, 2);
        writer.WriteInt32(1);
        writer.WriteInt32(2);
        writer.WriteEndList();
        writer.WriteEndCompound();
        writer.Complete();

        var reader = new NbtReader(buffer.WrittenSpan);
        Assert.True(reader.Read()); // StartCompound "root"
        Assert.True(reader.Read()); // StartList "Values"
        Assert.True(reader.NameEquals("Values"u8));
        Assert.True(reader.Read()); // element 1
        Assert.Equal(NbtTokenType.Value, reader.TokenType);
        Assert.False(reader.NameEquals("Values"u8));
        Assert.Null(reader.Name);
        reader.Skip();
        Assert.True(reader.Read()); // element 2
        Assert.False(reader.NameEquals("Values"u8));
        reader.Skip();
        Assert.True(reader.Read()); // EndList
        Assert.False(reader.NameEquals("Values"u8));
        Assert.True(reader.Read()); // EndCompound
        Assert.False(reader.NameEquals("Values"u8));
        Assert.False(reader.Read());
    }

    [Theory]
    [InlineData(new byte[] { 0x00 }, new byte[] { 0xC0, 0x80 })] // wire raw null vs canonical input
    [InlineData(new byte[] { 0xC0, 0x80 }, new byte[] { 0xC0, 0x80 })] // byte fast path
    [InlineData(new byte[] { 0xC0, 0x80 }, new byte[] { 0x00 })] // wire two-byte null vs raw null input
    [InlineData(new byte[] { 0xE0, 0x81, 0x81 }, new byte[] { 0x41 })] // overlong three-byte vs 'A'
    [InlineData(new byte[] { 0xC1, 0x81 }, new byte[] { 0x41 })] // overlong two-byte vs 'A'
    [InlineData(new byte[] { 0xE0, 0x81, 0x81 }, new byte[] { 0xC1, 0x81 })] // both non-canonical
    public void NonCanonicalWireNamesMatchByDecodedValue(byte[] wireName, byte[] inputName)
    {
        var data = BuildDocument(wireName);
        var reader = new NbtReader(data);
        Assert.True(reader.Read());
        Assert.True(reader.Read());
        Assert.True(reader.NameEquals(inputName));
    }

    [Theory]
    [InlineData(new byte[] { 0x00 })]
    [InlineData(new byte[] { 0xC0, 0x80 })]
    [InlineData(new byte[] { 0xE0, 0x81, 0x81 })]
    [InlineData(new byte[] { 0xC1, 0x81 })]
    public void NonCanonicalWireNamesDontMatchADifferentName(byte[] wireName)
    {
        var data = BuildDocument(wireName);
        var reader = new NbtReader(data);
        Assert.True(reader.Read());
        Assert.True(reader.Read());
        Assert.False(reader.NameEquals("B"u8));
    }

    [Fact]
    public void CanonicalWireNamesDoNotDecodeOnMismatch()
    {
        // E1 followed by an ASCII byte is a truncated multi-byte sequence; it carries no
        // non-canonical marker, so a mismatching comparison must return false without decoding
        // (decoding would throw).
        var data = BuildDocument(new byte[] { 0xE1, 0x42 });
        var reader = new NbtReader(data);
        Assert.True(reader.Read());
        Assert.True(reader.Read());
        Assert.False(reader.NameEquals("A"u8));
    }

    [Fact]
    public void CanonicalWireNamesDoNotFallBackForNonCanonicalInput()
    {
        // The decoded fallback is keyed on the wire name's encoding: a canonical wire name that
        // mismatches simply returns false, even when the input would decode to the same value.
        var data = BuildDocument(new byte[] { 0x41 });
        var reader = new NbtReader(data);
        Assert.True(reader.Read());
        Assert.True(reader.Read());
        Assert.False(reader.NameEquals(new byte[] { 0xE0, 0x81, 0x81 }));
        Assert.False(reader.NameEquals(new byte[] { 0x00 }));
    }

    [Fact]
    public void InvalidInputNameBytesThrowOnTheDecodedComparison()
    {
        var data = BuildDocument(new byte[] { 0x00, 0x41 }); // wire raw "\0A" is non-canonical
        var reader = new NbtReader(data);
        Assert.True(reader.Read());
        Assert.True(reader.Read());
        NbtException? thrown = null;
        try
        {
            _ = reader.NameEquals(new byte[] { 0xC0 });
        }
        catch (NbtException exception)
        {
            thrown = exception;
        }

        Assert.NotNull(thrown);
        reader.Dispose();
    }

    public static IEnumerable<object[]> SegmentSizes =>
        Enumerable.Range(1, 34).Select(size => new object[] { size });

    [Theory]
    [MemberData(nameof(SegmentSizes))]
    public void MatchesAcrossEverySegmentBoundary(int split)
    {
        // Fixed-size segments of `split` bytes; size 1 makes every byte offset inside the names a
        // cross-segment boundary, and larger sizes cross the boundaries at each multiple.
        var data = BuildMatrixDocument();
        var sequence = CreateSegmentedSequence(data, split);
        var reader = new NbtReader(sequence);
        RunTruthMatrix(ref reader);
        reader.Dispose();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void MatchesOnAChunkedStream(int maxChunk)
    {
        using var stream = new ChunkedStream(BuildMatrixDocument(), 1, maxChunk);
        var reader = new NbtReader(stream);
        RunTruthMatrix(ref reader);
        reader.Dispose();
    }

    [Fact]
    public void NamedStartListRootSurvivesTheListHeaderBufferRoll()
    {
        // The root name is followed by the 5-byte list header; a stream that returns at most
        // 3 bytes per read refills the pooled buffer while the name is still addressable, so the
        // name must have been copied.
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer);
        writer.WriteStartList("Numbers", NbtTagType.Int, 2);
        writer.WriteInt32(1);
        writer.WriteInt32(2);
        writer.WriteEndList();
        writer.Complete();

        using var stream = new ChunkedStream(buffer.WrittenSpan.ToArray(), 1, 3);
        var reader = new NbtReader(stream);
        Assert.True(reader.Read());
        Assert.Equal("Numbers", reader.Name);
        Assert.True(reader.NameEquals("Numbers"u8));
        Assert.Equal(NbtTagType.Int, reader.ListElementType);
        Assert.Equal(2, reader.ListLength);
        reader.Dispose();
    }

    [Fact]
    public void NameStaysConsistentAfterPayloadAndBufferAdvancement()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer);
        writer.WriteStartCompound("root");
        writer.WriteInt32("Health", 20);
        writer.WriteStartList("Position", NbtTagType.Double, 1);
        writer.WriteDouble(1.0);
        writer.WriteEndList();
        writer.WriteEndCompound();
        writer.Complete();

        using var stream = new ChunkedStream(buffer.WrittenSpan.ToArray(), 1, 2);
        var reader = new NbtReader(stream);
        Assert.True(reader.Read());

        Assert.True(reader.Read());
        Assert.Equal("Health", reader.Name);
        Assert.Equal(20, reader.GetInt32());
        Assert.Equal("Health", reader.Name); // after the payload advanced the buffer
        Assert.True(reader.NameEquals("Health"u8));

        Assert.True(reader.Read());
        // ReadListHeader already consumed the 5-byte header past the name, scrolling the buffer.
        var startListName = reader.Name;
        Assert.Equal("Position", reader.Name);
        Assert.Equal(startListName, reader.Name);
        Assert.True(reader.NameEquals("Position"u8));
        reader.Skip();

        Assert.True(reader.Read());
        Assert.Equal(NbtTokenType.EndCompound, reader.TokenType);
        Assert.Null(reader.Name);
        reader.Dispose();
    }

    [Fact]
    public void LeadingF0BytesStillThrowDuringRead()
    {
        var data = new byte[]
        {
            0x0A, 0x00, 0x04, (byte)'r', (byte)'o', (byte)'o', (byte)'t',
            0x01, 0x00, 0x02, 0xF0, 0x41,
        };

        Assert.Throws<NbtException>(() =>
        {
            var reader = new NbtReader(data);
            try
            {
                while (reader.Read())
                {
                }
            }
            finally
            {
                reader.Dispose();
            }
        });
    }

    [Fact]
    public void TruncatedNameSequenceThrowsOnTheFirstNameAccess()
    {
        // TAG_Compound "root" { TAG_Byte with a one-byte name 0xC2, which is a lead byte whose
        // continuation is missing. The name length prefix sits at offset 8.
        var data = new byte[]
        {
            0x0A, 0x00, 0x04, (byte)'r', (byte)'o', (byte)'o', (byte)'t',
            0x01, 0x00, 0x01, 0xC2,
        };

        var reader = new NbtReader(data);
        Assert.True(reader.Read());
        Assert.True(reader.Read()); // the member token is produced without decoding the name
        NbtException? thrown = null;
        try
        {
            _ = reader.Name;
        }
        catch (NbtException exception)
        {
            thrown = exception;
        }

        Assert.NotNull(thrown);
        Assert.Equal(8, thrown.BytePosition);
        reader.Dispose();
    }

    [Fact]
    public void TruncatedNameSequenceThrowsOnTheDecodedComparison()
    {
        // The same truncated wire name is non-canonical (overlong three-byte lead), so the
        // NameEquals fallback decodes it and throws.
        var data = new byte[]
        {
            0x0A, 0x00, 0x04, (byte)'r', (byte)'o', (byte)'o', (byte)'t',
            0x01, 0x00, 0x02, 0xE0, 0x80,
        };

        var reader = new NbtReader(data);
        Assert.True(reader.Read());
        Assert.True(reader.Read());
        NbtException? thrown = null;
        try
        {
            _ = reader.NameEquals("A"u8);
        }
        catch (NbtException exception)
        {
            thrown = exception;
        }

        Assert.NotNull(thrown);
        reader.Dispose();
    }

    [Fact]
    public void SkippedMembersWithInvalidNamesAreNotValidated()
    {
        var data = new byte[]
        {
            0x0A, 0x00, 0x04, (byte)'r', (byte)'o', (byte)'o', (byte)'t',
            0x01, 0x00, 0x01, 0xC2, 0x2A, // truncated-name member; deep validation is skipped
            0x00,
        };

        var reader = new NbtReader(data);
        Assert.True(reader.Read());
        Assert.True(reader.Read());
        reader.Skip(); // must not throw: the skipped name is never materialized
        Assert.True(reader.Read());
        Assert.Equal(NbtTokenType.EndCompound, reader.TokenType);
        Assert.False(reader.Read());
        reader.Dispose();
    }

    private static void RunTruthMatrix(ref NbtReader reader)
    {
        Assert.True(reader.Read());
        Assert.Equal(NbtTokenType.StartCompound, reader.TokenType);
        Assert.True(reader.NameEquals("root"u8));
        Assert.False(reader.NameEquals("roo"u8));
        Assert.False(reader.NameEquals("roots"u8));

        foreach (var name in NameMatrix)
        {
            Assert.True(reader.Read());
            Assert.Equal(NbtTokenType.Value, reader.TokenType);
            var encoded = Encode(name);
            Assert.True(reader.NameEquals(encoded), name);
            foreach (var other in NameMatrix)
            {
                if (!ReferenceEquals(other, name))
                {
                    Assert.False(reader.NameEquals(Encode(other)), $"{name} vs {other}");
                }
            }

            Assert.Equal(name, reader.Name);
            reader.Skip();
        }

        Assert.True(reader.Read());
        Assert.Equal(NbtTokenType.EndCompound, reader.TokenType);
        Assert.False(reader.NameEquals("root"u8));
        Assert.False(reader.Read());
        reader.Dispose();
    }

    private static byte[] Encode(string name) => ModifiedUTF8Encoding.Instance.GetBytes(name);

    private static byte[] BuildDocument(string memberName)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer);
        writer.WriteStartCompound("root");
        writer.WriteInt32(memberName, 7);
        writer.WriteEndCompound();
        writer.Complete();
        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] BuildDocument(byte[] memberName)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer);
        writer.WriteStartCompound("root");
        writer.WriteInt32(memberName, 7);
        writer.WriteEndCompound();
        writer.Complete();
        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] BuildMatrixDocument()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer);
        writer.WriteStartCompound("root");
        foreach (var name in NameMatrix)
        {
            writer.WriteInt32(name, 1);
        }

        writer.WriteEndCompound();
        writer.Complete();
        return buffer.WrittenSpan.ToArray();
    }

    private static ReadOnlySequence<byte> CreateSegmentedSequence(byte[] data, int segmentLength)
    {
        Segment? first = null;
        Segment? current = null;
        for (var offset = 0; offset < data.Length; offset += segmentLength)
        {
            var take = Math.Min(segmentLength, data.Length - offset);
            current = current is null
                ? new Segment(data.AsMemory(offset, take))
                : current.Append(data.AsMemory(offset, take));
            first ??= current;
        }

        if (first is null)
        {
            return ReadOnlySequence<byte>.Empty;
        }

        return new ReadOnlySequence<byte>(first, 0, current!, current!.Memory.Length);
    }

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(ReadOnlyMemory<byte> memory)
        {
            Memory = memory;
        }

        public Segment Append(ReadOnlyMemory<byte> memory)
        {
            var next = new Segment(memory) { RunningIndex = RunningIndex + Memory.Length };
            Next = next;
            return next;
        }
    }

    private sealed class ChunkedStream(byte[] data, int minChunk, int maxChunk) : Stream
    {
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var remaining = data.Length - _position;
            if (remaining == 0)
            {
                return 0;
            }

            var take = Math.Min(Math.Min(count, remaining), Random.Shared.Next(minChunk, maxChunk + 1));
            Buffer.BlockCopy(data, _position, buffer, offset, take);
            _position += take;
            return take;
        }

        public override void Flush() => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

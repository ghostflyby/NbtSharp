namespace NbtSharp.Tests;

public sealed class JavaStringTests
{
    [Theory]
    [InlineData("")]
    [InlineData("Steve")]
    [InlineData("中文字符")]
    [InlineData("Ünïcödé")]
    [InlineData("with\ttab")]
    public void RoundTripsPlainStrings(string value)
    {
        Assert.Equal(value, RoundTrip(value));
    }

    [Fact]
    public void EncodesNullCharacterAsTwoBytes()
    {
        var root = SerializeString("a\0b");
        var payload = StringPayload(root);

        Assert.Equal(4, payload.Length);
        Assert.Equal(new byte[] { (byte)'a', 0xC0, 0x80, (byte)'b' }, payload);
        Assert.Equal("a\0b", RoundTrip("a\0b"));
    }

    [Fact]
    public void EncodesSupplementaryCharacterAsSixBytes()
    {
        const string emoji = "\U0001F600";
        var root = SerializeString(emoji);
        var payload = StringPayload(root);

        Assert.Equal(6, payload.Length);
        Assert.Equal(emoji, RoundTrip(emoji));
    }

    [Fact]
    public void PreservesLengthInBytesRatherThanCharacters()
    {
        const string value = "中文";
        var root = SerializeString(value);
        var payload = StringPayload(root);

        Assert.Equal(6, payload.Length);
        Assert.Equal(value, RoundTrip(value));
    }

    [Theory]
    [InlineData(32767)]
    [InlineData(32768)]
    [InlineData(65535)]
    public void RoundTripsStringsAtLengthPrefixBoundaries(int length)
    {
        var value = new string('a', length);

        Assert.Equal(value, RoundTrip(value));
    }

    [Fact]
    public void SupplementaryCharacterUsesSurrogateEncodedBytes()
    {
        var root = SerializeString("\U0001F600");
        var payload = StringPayload(root);

        Assert.Equal(new byte[] { 0xED, 0xA0, 0xBD, 0xED, 0xB8, 0x80 }, payload);
    }

    [Theory]
    [InlineData(new byte[] { 0xC1 })]
    [InlineData(new byte[] { 0xC0, 0x41 })]
    [InlineData(new byte[] { 0xE0, 0x80 })]
    [InlineData(new byte[] { 0xED, 0xA0 })]
    [InlineData(new byte[] { 0xF0, 0x9F })]
    [InlineData(new byte[] { 0xF0, 0x80, 0x80, 0x80 })]
    [InlineData(new byte[] { 0xF4, 0x90, 0x80, 0x80 })]
    [InlineData(new byte[] { 0xF5, 0x80, 0x80, 0x80 })]
    [InlineData(new byte[] { 0xF8, 0x88, 0x80, 0x80, 0x80 })]
    [InlineData(new byte[] { 0xFE })]
    [InlineData(new byte[] { 0xFF })]
    [InlineData(new byte[] { 0xF0, 0x9F, 0x98, 0x80 })]
    public void RejectsMalformedModifiedUtf8(byte[] payload)
    {
        using var stream = new MemoryStream(BuildRootString(payload));

        Assert.Throws<NbtException>(() => NbtSerializer.Deserialize(stream));
    }

    [Fact]
    public void AcceptsRawNullLikeJavaReadUtf()
    {
        using var stream = new MemoryStream(BuildRootString([0x00]));

        Assert.True(NbtSerializer.Deserialize(stream).Value is string zero && zero == "\0");
    }

    [Theory]
    [InlineData(new byte[] { 0xED, 0xA0, 0x80 }, 0xD800)]
    [InlineData(new byte[] { 0xED, 0xB0, 0x80 }, 0xDC00)]
    public void DecodesLoneSurrogatesLikeJavaReadUtf(byte[] payload, int expectedCodeUnit)
    {
        using var stream = new MemoryStream(BuildRootString(payload));
        var expected = ((char)expectedCodeUnit).ToString();

        Assert.True(NbtSerializer.Deserialize(stream).Value is string text && text == expected);
    }

    [Fact]
    public void EncodesLoneSurrogateAsThreeBytes()
    {
        var value = ((char)0xD800).ToString();
        var root = SerializeString(value);
        var payload = StringPayload(root);

        Assert.Equal(new byte[] { 0xED, 0xA0, 0x80 }, payload);
        Assert.Equal(value, RoundTrip(value));
    }

    [Fact]
    public void RoundTripsMultibyteStringsAtLengthPrefixBoundaries()
    {
        Assert.Equal(new string('\0', 32767), RoundTrip(new string('\0', 32767))); // 65534 bytes
        Assert.Equal(new string('中', 21845), RoundTrip(new string('中', 21845))); // 65535 bytes
        var emoji = string.Concat(Enumerable.Repeat("\U0001F600", 10922)); // 65532 bytes
        Assert.Equal(emoji, RoundTrip(emoji));
    }

    private static byte[] BuildRootString(byte[] payload)
    {
        var bytes = new List<byte>
        {
            (byte)NbtTagType.String, 0x00, 0x01, (byte)'v',
            (byte)(payload.Length >> 8), (byte)payload.Length,
        };
        bytes.AddRange(payload);
        return bytes.ToArray();
    }

    private static byte[] StringPayload(NbtRoot root)
    {
        using var stream = new MemoryStream();
        NbtSerializer.Serialize(stream, root);
        var bytes = stream.ToArray();

        // tag type (1) + name length (2) + name (1) + payload length (2)
        var payloadLength = (bytes[4] << 8) | bytes[5];
        return bytes[6..(6 + payloadLength)];
    }

    private static NbtRoot SerializeString(string value) => new("v", value);

    private static string RoundTrip(string value)
    {
        using var stream = new MemoryStream();
        NbtSerializer.Serialize(stream, new NbtRoot("v", value));
        stream.Position = 0;
        var actual = NbtSerializer.Deserialize(stream);
        return actual.Value is string text ? text : string.Empty;
    }
}

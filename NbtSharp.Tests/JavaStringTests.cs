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
    public void RejectsMalformedModifiedUtf8(byte[] payload)
    {
        using var stream = new MemoryStream(BuildRootString(payload));

        Assert.Throws<NbtException>(() => NbtSerializer.Deserialize(stream));
    }

    [Fact]
    public void DecodesStandardUtf8AsALenientExtension()
    {
        using var emoji = new MemoryStream(BuildRootString([0xF0, 0x9F, 0x98, 0x80]));
        using var nul = new MemoryStream(BuildRootString([0x00]));

        Assert.True(NbtSerializer.Deserialize(emoji).Value is string text && text == "\U0001F600");
        Assert.True(NbtSerializer.Deserialize(nul).Value is string zero && zero == "\0");
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

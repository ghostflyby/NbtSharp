namespace NbtSharp.Tests;

public sealed class NbtModifiedUtf8EncodingTests
{
    private static readonly NbtModifiedUtf8Encoding Codec = NbtModifiedUtf8Encoding.Instance;

    [Theory]
    [InlineData(new byte[] { 0xF0, 0x9F, 0x98, 0x80 })]
    [InlineData(new byte[] { 0xF0, 0x41 })]
    [InlineData(new byte[] { 0xFF })]
    public void StringCountAndCharsEntryPointsRejectLeadBytesOfF0OrGreater(byte[] bytes)
    {
        Assert.Throws<NbtException>(() => Codec.GetString(bytes));
        Assert.Throws<NbtException>(() => Codec.GetString(bytes, 0, bytes.Length));
        Assert.Throws<NbtException>(() => Codec.GetString(bytes.AsSpan()));
        Assert.Throws<NbtException>(() => Codec.GetCharCount(bytes, 0, bytes.Length));
        Assert.Throws<NbtException>(() => Codec.GetCharCount(bytes.AsSpan()));
        Assert.Throws<NbtException>(() => Codec.GetChars(bytes, 0, bytes.Length, new char[8], 0));
        Assert.Throws<NbtException>(() => Codec.GetChars(bytes.AsSpan(), new char[8]));
    }

    [Theory]
    [InlineData(new byte[] { 0x00 }, "\0")]
    [InlineData(new byte[] { 0xC0, 0x80 }, "\0")]
    [InlineData(new byte[] { 0xC1, 0x81 }, "A")]
    [InlineData(new byte[] { 0xE0, 0x80, 0x80 }, "\0")]
    [InlineData(new byte[] { 0xED, 0xA0, 0xBD, 0xED, 0xB8, 0x80 }, "\uD83D\uDE00")]
    public void DecodesTheAcceptedForms(byte[] bytes, string expected)
    {
        Assert.Equal(expected, Codec.GetString(bytes));
        Assert.Equal(expected, Codec.GetString(bytes, 0, bytes.Length));
        Assert.Equal(expected, Codec.GetString(bytes.AsSpan()));
        Assert.Equal(expected.Length, Codec.GetCharCount(bytes, 0, bytes.Length));
        Assert.Equal(expected.Length, Codec.GetCharCount(bytes.AsSpan()));

        var chars = new char[expected.Length];
        Assert.Equal(expected.Length, Codec.GetChars(bytes, 0, bytes.Length, chars, 0));
        Assert.Equal(expected, new string(chars));

        chars = new char[expected.Length];
        Assert.Equal(expected.Length, Codec.GetChars(bytes.AsSpan(), chars));
        Assert.Equal(expected, new string(chars));
    }

    [Fact]
    public void UnpairedSurrogateDecodesToTheLoneCodeUnit()
    {
        // Java's readUTF returns the lone U+D800 code unit for this sequence, and the forwarded
        // Myitian decoder agrees.
        byte[] bytes = [0xED, 0xA0, 0x80];
        Assert.Equal("\uD800", Codec.GetString(bytes));
        Assert.Equal("\uD800", Codec.GetString(bytes.AsSpan()));
        Assert.Equal(1, Codec.GetCharCount(bytes.AsSpan()));

        var chars = new char[1];
        Assert.Equal(1, Codec.GetChars(bytes.AsSpan(), chars));
        Assert.Equal('\uD800', chars[0]);
    }

    [Theory]
    [InlineData(new byte[] { 0x80 })]
    [InlineData(new byte[] { 0xC2 })]
    [InlineData(new byte[] { 0xC2, 0x41 })]
    [InlineData(new byte[] { 0xE0, 0x80 })]
    [InlineData(new byte[] { 0xE0, 0x80, 0x41 })]
    public void RejectsMalformedSequences(byte[] bytes)
    {
        Assert.Throws<NbtException>(() => Codec.GetString(bytes));
        Assert.Throws<NbtException>(() => Codec.GetString(bytes.AsSpan()));
        Assert.Throws<NbtException>(() => Codec.GetCharCount(bytes.AsSpan()));
        Assert.Throws<NbtException>(() => Codec.GetChars(bytes.AsSpan(), new char[8]));
    }
}

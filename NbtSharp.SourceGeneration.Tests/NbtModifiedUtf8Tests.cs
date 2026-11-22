using Myitian.Text;

namespace NbtSharp.SourceGeneration.Tests;

public sealed class NbtModifiedUtf8Tests
{
    [Theory]
    [InlineData("")]
    [InlineData("Steve")]
    [InlineData("中文字符")]
    [InlineData("Ünïcödé")]
    [InlineData("with\ttab")]
    [InlineData("a\0b")]
    [InlineData("\0")]
    [InlineData("\u007F")]
    [InlineData("\u0080")]
    [InlineData("\u07FF")]
    [InlineData("\u0800")]
    [InlineData("\uFFFF")]
    [InlineData("\U0001F600")]
    [InlineData("\uD800\uDC00")]
    public void MatchesTheRuntimeModifiedUtf8Encoder(string value)
    {
        Assert.Equal(
            ModifiedUTF8Encoding.Instance.GetBytes(value),
            NbtModifiedUtf8.GetBytes(value));
    }

    [Fact]
    public void MatchesTheRuntimeEncoderForLoneSurrogatesAndMixedInput()
    {
        // Lone surrogates cannot travel through InlineData (attribute string blobs are UTF-8, which
        // replaces them), so the corpus is built at runtime like in JavaStringTests.
        var high = ((char)0xD800).ToString();
        var low = ((char)0xDC00).ToString();
        var mixed = "a" + high + "b\U0001F600c" + low + "d\0";

        Assert.Equal(ModifiedUTF8Encoding.Instance.GetBytes(high), NbtModifiedUtf8.GetBytes(high));
        Assert.Equal(ModifiedUTF8Encoding.Instance.GetBytes(low), NbtModifiedUtf8.GetBytes(low));
        Assert.Equal(ModifiedUTF8Encoding.Instance.GetBytes(mixed), NbtModifiedUtf8.GetBytes(mixed));
    }

    [Fact]
    public void EncodesNullAsTheTwoByteForm()
    {
        Assert.Equal(new byte[] { 0xC0, 0x80 }, NbtModifiedUtf8.GetBytes("\0"));
    }

    [Fact]
    public void EncodesSupplementaryCharactersAsSixByteSurrogatePairs()
    {
        Assert.Equal(
            new byte[] { 0xED, 0xA0, 0xBD, 0xED, 0xB8, 0x80 },
            NbtModifiedUtf8.GetBytes("\U0001F600"));
    }

    [Fact]
    public void EncodesLoneSurrogatesAsThreeBytesWithoutReplacement()
    {
        Assert.Equal(new byte[] { 0xED, 0xA0, 0x80 }, NbtModifiedUtf8.GetBytes(((char)0xD800).ToString()));
        Assert.Equal(new byte[] { 0xED, 0xB0, 0x80 }, NbtModifiedUtf8.GetBytes(((char)0xDC00).ToString()));
    }

    [Theory]
    [InlineData("\u007F", new byte[] { 0x7F })]
    [InlineData("\u0080", new byte[] { 0xC2, 0x80 })]
    [InlineData("\u07FF", new byte[] { 0xDF, 0xBF })]
    [InlineData("\u0800", new byte[] { 0xE0, 0xA0, 0x80 })]
    [InlineData("\uFFFF", new byte[] { 0xEF, 0xBF, 0xBF })]
    public void EncodesRangeBoundariesPerModifiedUtf8(string value, byte[] expected)
    {
        Assert.Equal(expected, NbtModifiedUtf8.GetBytes(value));
    }
}

namespace NbtSharp.Tests;

public sealed class NbtNameComparerTests
{
    [Theory]
    [InlineData(new byte[] { 0x00 }, new byte[] { 0xC0, 0x80 }, true)]
    [InlineData(new byte[] { 0xC1, 0x81 }, new byte[] { 0x41 }, true)]
    [InlineData(new byte[] { 0xE0, 0x81, 0x81 }, new byte[] { 0x41 }, true)]
    [InlineData(new byte[] { 0x41 }, new byte[] { 0x42 }, false)]
    public void ByteComparerUsesDecodedValue(byte[] x, byte[] y, bool expected)
    {
        Assert.Equal(expected, NbtNameByteComparer.Instance.Equals(x, y));
        Assert.Equal(expected, NbtNameByteComparer.Instance.Equals(y, x));
    }

    [Theory]
    [InlineData(new byte[] { 0x00 }, "\0", true)]
    [InlineData(new byte[] { 0xC0, 0x80 }, "\0", true)]
    [InlineData(new byte[] { 0xC1, 0x81 }, "A", true)]
    [InlineData(new byte[] { 0xE0, 0x81, 0x81 }, "A", true)]
    [InlineData(new byte[] { 0x41 }, "B", false)]
    public void CharComparerUsesDecodedValue(byte[] bytes, string chars, bool expected)
    {
        Assert.Equal(expected, NbtNameCharComparer.Instance.Equals(chars.AsSpan(), bytes));
    }

    [Theory]
    [InlineData(new byte[] { 0x00 }, "\0")]
    [InlineData(new byte[] { 0xC1, 0x81 }, "A")]
    [InlineData(new byte[] { 0xE0, 0x81, 0x81 }, "A")]
    [InlineData(new byte[] { 0xED, 0xA0, 0xBD, 0xED, 0xB8, 0x80 }, "\U0001F600")]
    public void HashCodesAgreeAcrossEncodings(byte[] bytes, string name)
    {
        Assert.Equal(string.GetHashCode(name.AsSpan()), NbtNameByteComparer.Instance.GetHashCode(bytes));
        Assert.Equal(string.GetHashCode(name.AsSpan()), NbtNameCharComparer.Instance.GetHashCode(name.AsSpan()));
    }

    [Fact]
    public void LongNameHashMatchesTheDecodedString()
    {
        var name = new string('a', 300);
        var bytes = NbtModifiedUtf8Encoding.Instance.GetBytes(name);

        Assert.Equal(string.GetHashCode(name.AsSpan()), NbtNameByteComparer.Instance.GetHashCode(bytes));
        Assert.Equal(string.GetHashCode(name.AsSpan()), NbtNameCharComparer.Instance.GetHashCode(name.AsSpan()));
    }

    [Fact]
    public void InvalidUtf8Throws()
    {
        Assert.Throws<NbtException>(() =>
            NbtNameByteComparer.Instance.Equals(new byte[] { 0xC2 }, new byte[] { 0x41 }));
        Assert.Throws<NbtException>(() =>
            NbtNameByteComparer.Instance.GetHashCode(new byte[] { 0xC2 }));
        Assert.Throws<NbtException>(() =>
            NbtNameCharComparer.Instance.Equals("A".AsSpan(), new byte[] { 0xC2 }));
    }

    [Fact]
    public void CreatingAByteSpanFromCharsIsNotSupported()
    {
        Assert.Throws<NotSupportedException>(() =>
        {
            ReadOnlySpan<byte> ignored = NbtNameCharComparer.Instance.Create("A".AsSpan());
            _ = ignored.Length;
        });
    }
}

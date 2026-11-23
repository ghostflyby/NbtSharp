namespace NbtSharp.Tests;

public sealed class NbtNameComparerTests
{
    [Theory]
    [InlineData("")]
    [InlineData("Health")]
    [InlineData("\0")]
    [InlineData("é")]
    [InlineData("中文")]
    [InlineData("\U0001F600")]
    public void HashCodesAgreeAcrossNameForms(string name)
    {
        var bytes = Encode(name);

        Assert.True(NbtNameComparer.Instance.Equals(name, name));
        Assert.True(NbtNameComparer.Instance.Equals(name.AsSpan(), name));
        Assert.True(NbtUtf8NameComparer.Instance.Equals(bytes, name));
        Assert.Equal(string.GetHashCode(name.AsSpan()), NbtNameComparer.Instance.GetHashCode(name));
        Assert.Equal(string.GetHashCode(name.AsSpan()), NbtNameComparer.Instance.GetHashCode(name.AsSpan()));
        Assert.Equal(string.GetHashCode(name.AsSpan()), NbtUtf8NameComparer.Instance.GetHashCode(name));
        Assert.Equal(string.GetHashCode(name.AsSpan()), NbtUtf8NameComparer.Instance.GetHashCode(bytes));
        Assert.Equal(name, NbtNameComparer.Instance.Create(name.AsSpan()));
        Assert.Equal(name, NbtUtf8NameComparer.Instance.Create(bytes));
    }

    [Fact]
    public void LongNameHashesAgreeAcrossNameForms()
    {
        var name = new string('a', 300);
        var bytes = Encode(name);

        Assert.Equal(string.GetHashCode(name.AsSpan()), NbtUtf8NameComparer.Instance.GetHashCode(bytes));
        Assert.True(NbtUtf8NameComparer.Instance.Equals(bytes, name));
        Assert.Equal(name, NbtUtf8NameComparer.Instance.Create(bytes));
    }

    [Fact]
    public void Utf8AlternateLookupFindsDecodedNames()
    {
        var names = new Dictionary<string, int>(NbtUtf8NameComparer.Instance)
        {
            ["Health"] = 1,
            ["\0"] = 2,
            ["A"] = 3,
            ["\U0001F600"] = 4,
        };
        var lookup = names.GetAlternateLookup<ReadOnlySpan<byte>>();

        Assert.True(lookup.TryGetValue("Health"u8, out var health));
        Assert.Equal(1, health);
        Assert.True(lookup.TryGetValue(new byte[] { 0x00 }, out var rawNull));
        Assert.Equal(2, rawNull);
        Assert.True(lookup.TryGetValue(new byte[] { 0xC1, 0x81 }, out var overlongA));
        Assert.Equal(3, overlongA);
        Assert.True(lookup.TryGetValue(Encode("\U0001F600"), out var emoji));
        Assert.Equal(4, emoji);
        Assert.False(lookup.ContainsKey("B"u8));
    }

    [Fact]
    public void CharAlternateLookupFindsDecodedNames()
    {
        var names = new Dictionary<string, int>(NbtNameComparer.Instance)
        {
            ["Health"] = 1,
            ["é"] = 2,
        };
        var lookup = names.GetAlternateLookup<ReadOnlySpan<char>>();

        Assert.True(lookup.TryGetValue("Health".AsSpan(), out var health));
        Assert.Equal(1, health);
        Assert.True(lookup.TryGetValue("é".AsSpan(), out var accent));
        Assert.Equal(2, accent);
        Assert.False(lookup.ContainsKey("health".AsSpan()));
    }

    [Fact]
    public void InvalidUtf8Throws()
    {
        Assert.Throws<NbtException>(() =>
            NbtUtf8NameComparer.Instance.GetHashCode(new byte[] { 0xC2 }));
        Assert.Throws<NbtException>(() =>
            NbtUtf8NameComparer.Instance.Equals(new byte[] { 0xC2 }, "x"));
    }

    private static byte[] Encode(string name) => NbtModifiedUtf8Encoding.Instance.GetBytes(name);
}

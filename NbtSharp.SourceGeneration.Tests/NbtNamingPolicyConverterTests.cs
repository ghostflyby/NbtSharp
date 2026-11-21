namespace NbtSharp.SourceGeneration.Tests;

public sealed class NbtNamingPolicyConverterTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void GeneratorPolicyMatchesTheRuntimePolicy(int kind)
    {
        var generatorPolicy = NbtNamingPolicyConverter.Create(kind);
        var runtimePolicy = NbtNamingPolicy.FromKind((NbtNamingPolicyKind)kind);

        Assert.NotNull(generatorPolicy);
        Assert.NotNull(runtimePolicy);
        foreach (var name in Names)
        {
            Assert.Equal(runtimePolicy.ConvertName(name), generatorPolicy(name));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public void UnspecifiedKindsHaveNoPolicy(int kind)
    {
        Assert.Null(NbtNamingPolicyConverter.Create(kind));
    }

    private static readonly string[] Names =
    [
        "",
        "A",
        "AB",
        "ABc",
        "ID",
        "PlayerName",
        "URLValue",
        "XMLHttpRequest",
        "HTTP2Server",
        "Health2Points",
        "Version2ID",
        "Already_Snake",
        "already-kebab",
        "lowercase",
    ];
}

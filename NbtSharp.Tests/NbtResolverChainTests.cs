namespace NbtSharp.Tests;

public sealed class NbtResolverChainTests
{
    [Fact]
    public void ResolversAreConsultedInOrder()
    {
        var first = new NbtTypeInfo<ChainProbe>(new ChainProbeConverter());
        var second = new NbtTypeInfo<ChainProbe>(new ChainProbeConverter());
        var chain = new NbtTypeInfoResolverChain(
            new FixedResolver(typeof(ChainProbe), first),
            new FixedResolver(typeof(ChainProbe), second));

        var resolved = chain.GetTypeInfo(typeof(ChainProbe), new NbtSerializerOptions());

        Assert.Same(first, resolved);
        Assert.Equal(2, chain.Resolvers.Count);
    }

    [Fact]
    public void NullResultsFallThroughToTheNextResolver()
    {
        var info = new NbtTypeInfo<ChainProbe>(new ChainProbeConverter());
        var chain = new NbtTypeInfoResolverChain(
            new FixedResolver(type: null, info: null),
            new FixedResolver(typeof(ChainProbe), info));

        var resolved = chain.GetTypeInfo(typeof(ChainProbe), new NbtSerializerOptions());

        Assert.Same(info, resolved);
    }

    [Fact]
    public void RejectsAnEmptyResolverList()
    {
        Assert.Throws<ArgumentException>(() => new NbtTypeInfoResolverChain());
        Assert.Throws<ArgumentNullException>(() => new NbtTypeInfoResolverChain(null!));
        Assert.Throws<ArgumentNullException>(() =>
            new NbtTypeInfoResolverChain(new FixedResolver(type: null, info: null), null!));
    }

    [Fact]
    public void GeneratedContextFallsBackToReflectionForUnknownTypes()
    {
        var options = new NbtSerializerOptions
        {
            TypeInfoResolver = new NbtTypeInfoResolverChain(
                SourceGenerationContext.Default,
                new NbtReflectionTypeInfoResolver()),
        };
        var value = new UnannotatedDto { Name = "fallback", Value = 5 };

        var data = NbtSerializer.Serialize(value, "root", options);
        var copy = NbtSerializer.Deserialize<UnannotatedDto>(data, options);

        Assert.Equal("fallback", copy.Name);
        Assert.Equal(5, copy.Value);
    }

    [Fact]
    public void GeneratedContextStillHandlesItsOwnTypesInAChain()
    {
        var options = new NbtSerializerOptions
        {
            TypeInfoResolver = new NbtTypeInfoResolverChain(
                SourceGenerationContext.Default,
                new NbtReflectionTypeInfoResolver()),
        };
        var value = new SourceGenItem { Id = "chain", Count = 7 };

        var data = NbtSerializer.Serialize(value, "root", options);
        var copy = NbtSerializer.Deserialize<SourceGenItem>(data, options);

        Assert.Equal("chain", copy.Id);
        Assert.Equal(7, copy.Count);
    }

    private sealed class FixedResolver(Type? type, NbtTypeInfo? info) : INbtTypeInfoResolver
    {
        public NbtTypeInfo? GetTypeInfo(Type requestedType, NbtSerializerOptions options) =>
            requestedType == type ? info : null;
    }

    private sealed class ChainProbe
    {
    }

    private sealed class ChainProbeConverter : NbtConverter<ChainProbe>
    {
        public override ChainProbe Read(ref NbtReader reader, NbtSerializerOptions options) => new();

        public override void Write(
            ref NbtWriter writer,
            ChainProbe value,
            NbtSerializerOptions options)
        {
        }
    }
}

internal sealed class UnannotatedDto
{
    public string Name { get; set; } = "";

    public int Value { get; set; }
}

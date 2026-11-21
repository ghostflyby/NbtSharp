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
    public void ConstructorSnapshotsTheInputArray()
    {
        var info = new NbtTypeInfo<ChainProbe>(new ChainProbeConverter());
        var replacementInfo = new NbtTypeInfo<ChainProbe>(new ChainProbeConverter());
        var first = new FixedResolver(type: null, info: null);
        var resolvers = new INbtTypeInfoResolver[]
        {
            first,
            new FixedResolver(typeof(ChainProbe), info),
        };
        var chain = new NbtTypeInfoResolverChain(resolvers);

        resolvers[0] = new FixedResolver(typeof(ChainProbe), replacementInfo);

        Assert.Same(first, chain.Resolvers[0]);
        Assert.Same(
            info,
            chain.GetTypeInfo(typeof(ChainProbe), new NbtSerializerOptions()));
    }

    [Fact]
    public void ResolversCannotBeMutatedThroughTheExposedView()
    {
        var chain = new NbtTypeInfoResolverChain(new FixedResolver(type: null, info: null));

        Assert.False(chain.Resolvers is INbtTypeInfoResolver[]);
        var view = Assert.IsAssignableFrom<IList<INbtTypeInfoResolver>>(chain.Resolvers);
        Assert.Throws<NotSupportedException>(() =>
            view[0] = new FixedResolver(type: null, info: null));
    }

    [Fact]
    public void ForwardsTheSameOptionsInstanceToResolvers()
    {
        var resolver = new CapturingResolver();
        var chain = new NbtTypeInfoResolverChain(resolver);
        var options = new NbtSerializerOptions();

        Assert.Null(chain.GetTypeInfo(typeof(ChainProbe), options));
        Assert.Same(options, resolver.Options);
    }

    [Fact]
    public void RejectsTypeInfoForAnotherType()
    {
        var info = new NbtTypeInfo<ChainProbe>(new ChainProbeConverter());
        var chain = new NbtTypeInfoResolverChain(
            new FixedResolver(typeof(UnannotatedDto), info));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            chain.GetTypeInfo(typeof(UnannotatedDto), new NbtSerializerOptions()));

        Assert.Contains("ChainProbe", exception.Message, StringComparison.Ordinal);
        Assert.Contains("UnannotatedDto", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomConvertersOutrankTheChain()
    {
        var options = new NbtSerializerOptions
        {
            TypeInfoResolver = new NbtTypeInfoResolverChain(
                new FixedResolver(
                    typeof(ChainProbe),
                    new NbtTypeInfo<ChainProbe>(new ChainProbeConverter()))),
        };
        options.Converters.Add(new MarkerChainProbeConverter());

        var data = NbtSerializer.Serialize(new ChainProbe(), "root", options);
        var root = (NbtCompound)NbtSerializer.Deserialize(data).Value.Value!;

        Assert.True(root.ContainsKey("marker"));
    }

    [Fact]
    public void GeneratedContextWinsForItsOwnTypes()
    {
        var chain = new NbtTypeInfoResolverChain(
            SourceGenerationContext.Default,
            new NbtReflectionTypeInfoResolver());

        var info = chain.GetTypeInfo(typeof(SourceGenItem), new NbtSerializerOptions());

        Assert.Same(SourceGenerationContext.Default.SourceGenItem, info);
    }

    [Fact]
    public void ChainResolvesRuntimeMembersAcrossGeneratedAndReflectionResolvers()
    {
        var options = new NbtSerializerOptions
        {
            TypeInfoResolver = new NbtTypeInfoResolverChain(
                ChainResolverContext.Default,
                new NbtReflectionTypeInfoResolver()),
        };
        var value = new ChainGeneratedRoot
        {
            Id = 9,
            Child = new UnannotatedDto { Name = "nested", Value = 4 },
        };

        var data = NbtSerializer.Serialize(value, "root", options);
        var copy = NbtSerializer.Deserialize<ChainGeneratedRoot>(data, options);

        Assert.Equal(9, copy.Id);
        Assert.Equal("nested", copy.Child.Name);
        Assert.Equal(4, copy.Child.Value);
    }

    [Fact]
    public void ReflectionRootResolvesGeneratedMembersThroughTheChain()
    {
        var options = new NbtSerializerOptions
        {
            TypeInfoResolver = new NbtTypeInfoResolverChain(
                ChainResolverContext.Default,
                new NbtReflectionTypeInfoResolver()),
        };
        var value = new ChainReflectionRoot
        {
            Item = new SourceGenItem { Id = "mixed", Count = 3 },
        };

        var data = NbtSerializer.Serialize(value, "root", options);
        var copy = NbtSerializer.Deserialize<ChainReflectionRoot>(data, options);

        Assert.Equal("mixed", copy.Item.Id);
        Assert.Equal(3, copy.Item.Count);
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

    private sealed class CapturingResolver : INbtTypeInfoResolver
    {
        public NbtSerializerOptions? Options { get; private set; }

        public NbtTypeInfo? GetTypeInfo(Type requestedType, NbtSerializerOptions options)
        {
            Options = options;
            return null;
        }
    }

    private sealed class MarkerChainProbeConverter : NbtConverter<ChainProbe>
    {
        public override ChainProbe Read(ref NbtReader reader, NbtSerializerOptions options)
        {
            reader.Skip();
            return new ChainProbe();
        }

        public override void Write(
            ref NbtWriter writer,
            ChainProbe value,
            NbtSerializerOptions options)
        {
            writer.WriteStartCompound();
            writer.WriteInt32("marker", 1);
            writer.WriteEndCompound();
        }

        public override bool TryGetTagType(
            ChainProbe value,
            NbtSerializerOptions options,
            out NbtTagType tagType)
        {
            tagType = NbtTagType.Compound;
            return true;
        }
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

internal sealed class ChainGeneratedRoot
{
    public int Id { get; set; }

    [NbtUseConverter]
    public UnannotatedDto Child { get; set; } = new();
}

internal sealed class ChainReflectionRoot
{
    public SourceGenItem Item { get; set; } = new();
}

[NbtSerializable(typeof(ChainGeneratedRoot))]
internal partial class ChainResolverContext : NbtSerializerContext
{
}

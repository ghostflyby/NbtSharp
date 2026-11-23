using System.Runtime.CompilerServices;

namespace NbtSharp.Tests;

// These tests verify the reflection resolver's contract-modifier API (callback lifecycle, caching,
// freezing, cycle guard). Serialization behavior is covered by the dual-mode contract suites.
public sealed class NbtReflectionResolverApiTests
{
    [Fact]
    public void WithAddedModifierRunsCallbacksInOrderAndStacks()
    {
        var calls = new List<string>();
        var first = new NbtReflectionTypeInfoResolver(contract =>
        {
            calls.Add("first");
            contract["Name"].NbtName = "from-first";
        });
        var second = first.WithAddedModifier(contract =>
        {
            calls.Add("second:" + contract["Name"].NbtName);
            contract["Name"].NbtName = "from-second";
        });

        var options = new NbtSerializerOptions { TypeInfoResolver = second };
        var data = NbtSerializer.Serialize(new ResolverRenameDto { Name = "x" }, "root", options);

        Assert.Equal(["first", "second:from-first"], calls);
        Assert.True(AsCompound(data).ContainsKey("from-second"));
        Assert.Equal("x", NbtSerializer.Deserialize<ResolverRenameDto>(data, options).Name);
    }

    [Fact]
    public void WithAddedModifierLeavesTheOriginalResolverCacheIntact()
    {
        var first = new NbtReflectionTypeInfoResolver(contract =>
            contract["Name"].NbtName = "original-name");
        var options = new NbtSerializerOptions { TypeInfoResolver = first };
        var before = NbtSerializer.Serialize(new ResolverRenameDto { Name = "x" }, "root", options);

        var derived = first.WithAddedModifier(contract => contract["Name"].NbtName = "derived-name");
        var derivedOptions = new NbtSerializerOptions { TypeInfoResolver = derived };
        var derivedData = NbtSerializer.Serialize(new ResolverRenameDto { Name = "x" }, "root", derivedOptions);

        var after = NbtSerializer.Serialize(new ResolverRenameDto { Name = "x" }, "root", options);

        Assert.True(AsCompound(derivedData).ContainsKey("derived-name"));
        Assert.True(AsCompound(after).ContainsKey("original-name"));
        Assert.Equal(before, after);
        Assert.Equal("x", NbtSerializer.Deserialize<ResolverRenameDto>(after, options).Name);
    }

    [Fact]
    public void FrozenContractRejectsStashedMutationsAndKeepsSerializing()
    {
        NbtMemberInfo? stash = null;
        var options = ModifierOptions(contract => stash = contract["Name"]);
        var value = new ResolverRenameDto { Name = "x", Health = 1 };
        var first = NbtSerializer.Serialize(value, "root", options);

        var copy = NbtSerializer.Deserialize<ResolverRenameDto>(first, options);
        Assert.Equal("x", copy.Name);
        Assert.Equal(1, copy.Health);

        Assert.NotNull(stash);
        Assert.Throws<InvalidOperationException>(() => stash!.NbtName = "other");
        Assert.Throws<InvalidOperationException>(() => stash!.Ignore = true);
        Assert.Throws<InvalidOperationException>(() => stash!.Converter = new ResolverOffsetIntConverter());
        Assert.Throws<InvalidOperationException>(() => stash!.Order = 9);

        Assert.Equal(first, NbtSerializer.Serialize(value, "root", options));
    }

    [Fact]
    public void ReentrantResolutionOfTheSameTypeThrows()
    {
        var value = new ResolverSelfRefDto { Name = "x" };
        Assert.Equal("x", value.Name);

        var optionsSlot = new OptionsSlot();
        var resolver = new NbtReflectionTypeInfoResolver(contract =>
        {
            if (contract.Type == typeof(ResolverSelfRefDto))
            {
                NbtSerializer.Serialize(new ResolverSelfRefDto { Name = "nested" }, "nested", optionsSlot.Value);
            }
        });
        optionsSlot.Value = optionsSlot.Value.WithTypeInfoResolver(resolver);
        var options = optionsSlot.Value;

        var exception = Assert.Throws<InvalidOperationException>(() =>
            NbtSerializer.Serialize(value, "root", options));

        Assert.Contains("re-entered", exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(ResolverSelfRefDto).ToString(), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NestedResolutionOfASiblingTypeSucceedsExactlyOnce()
    {
        var siblingRuns = 0;
        var optionsSlot = new OptionsSlot();
        var resolver = new NbtReflectionTypeInfoResolver(contract =>
        {
            if (contract.Type == typeof(ResolverSiblingADto))
            {
                NbtSerializer.Serialize(new ResolverSiblingBDto { Value = 3 }, "sibling", optionsSlot.Value);
            }
            else if (contract.Type == typeof(ResolverSiblingBDto))
            {
                siblingRuns++;
            }
        });
        optionsSlot.Value = optionsSlot.Value.WithTypeInfoResolver(resolver);
        var options = optionsSlot.Value;

        var data = NbtSerializer.Serialize(new ResolverSiblingADto { Name = "a" }, "root", options);

        Assert.Equal(1, siblingRuns);
        Assert.Equal("a", NbtSerializer.Deserialize<ResolverSiblingADto>(data, options).Name);

        var siblingData = NbtSerializer.Serialize(new ResolverSiblingBDto { Value = 4 }, "root", options);
        Assert.Equal(4, NbtSerializer.Deserialize<ResolverSiblingBDto>(siblingData, options).Value);
        Assert.Equal(1, siblingRuns);
    }

    [Fact]
    public void CyclicNestedResolutionThrows()
    {
        var cycleA = new ResolverCycleADto { Name = "a" };
        var cycleB = new ResolverCycleBDto { Value = 1 };
        Assert.Equal("a", cycleA.Name);
        Assert.Equal(1, cycleB.Value);

        var optionsSlot = new OptionsSlot();
        var resolver = new NbtReflectionTypeInfoResolver(contract =>
        {
            if (contract.Type == typeof(ResolverCycleADto))
            {
                NbtSerializer.Serialize(cycleB, "b", optionsSlot.Value);
            }
            else if (contract.Type == typeof(ResolverCycleBDto))
            {
                NbtSerializer.Serialize(cycleA, "a", optionsSlot.Value);
            }
        });
        optionsSlot.Value = optionsSlot.Value.WithTypeInfoResolver(resolver);

        Assert.Throws<InvalidOperationException>(() =>
            NbtSerializer.Serialize(cycleA, "root", optionsSlot.Value));
    }

    [Fact]
    public void NestedResolutionOfCachedAndResolverlessTypesSucceeds()
    {
        var optionsSlot = new OptionsSlot();
        var resolver = new NbtReflectionTypeInfoResolver(contract =>
        {
            if (contract.Type != typeof(ResolverUsesPreResolvedDto))
            {
                return;
            }

            NbtSerializer.Serialize(new ResolverCachedSiblingDto { Value = 2 }, "cached", optionsSlot.Value);
            NbtSerializer.Serialize(42, "plain");
        });
        optionsSlot.Value = optionsSlot.Value.WithTypeInfoResolver(resolver);
        var options = optionsSlot.Value;

        var warm = NbtSerializer.Serialize(new ResolverCachedSiblingDto { Value = 1 }, "warm", options);
        Assert.Equal(1, NbtSerializer.Deserialize<ResolverCachedSiblingDto>(warm, options).Value);

        var data = NbtSerializer.Serialize(new ResolverUsesPreResolvedDto { Name = "ok" }, "root", options);

        Assert.Equal("ok", NbtSerializer.Deserialize<ResolverUsesPreResolvedDto>(data, options).Name);
    }

    [Fact]
    public async Task ConcurrentFirstTouchRunsTheModifierOnce()
    {
        var runs = new StrongBox<int>();
        var resolver = new NbtReflectionTypeInfoResolver(_ => Interlocked.Increment(ref runs.Value));
        var options = new NbtSerializerOptions { TypeInfoResolver = resolver };
        const int threads = 4;
        var barrier = new Barrier(threads);
        var results = new byte[threads][];

        var tasks = Enumerable.Range(0, threads)
            .Select(i => Task.Run(
                () =>
                {
                    barrier.SignalAndWait();
                    results[i] = NbtSerializer.Serialize(new ResolverCountDto { Value = 7 }, "root", options);
                },
                TestContext.Current.CancellationToken))
            .ToArray();
        await Task.WhenAll(tasks);

        Assert.Equal(1, Volatile.Read(ref runs.Value));
        Assert.All(results, result => Assert.Equal(results[0], result));
        Assert.Equal(7, NbtSerializer.Deserialize<ResolverCountDto>(results[0], options).Value);
    }

    [Fact]
    public void ContractExposesMemberMetadata()
    {
        NbtContractInfo? contract = null;
        var options = ModifierOptions(c => contract = c);
        NbtSerializer.Serialize(new ResolverRenameDto(), "root", options);

        Assert.NotNull(contract);
        Assert.Equal(typeof(ResolverRenameDto), contract!.Type);
        Assert.Equal(2, contract.Members.Count);
        Assert.True(contract.TryGetValue("Name", out _));
        Assert.False(contract.TryGetValue("name", out _));
        Assert.Equal(typeof(string), contract["Name"].Type);
        Assert.True(contract["Name"].CanWrite);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<NbtMemberInfo>)contract.Members)[0] = contract.Members[0]);
    }

    [Fact]
    public void NullNbtNameThrowsArgumentNull()
    {
        var options = ModifierOptions(contract => contract["Name"].NbtName = null!);

        Assert.Throws<ArgumentNullException>(() =>
            NbtSerializer.Serialize(new ResolverRenameDto(), "root", options));
    }

    [Fact]
    public void IgnoringAConstructorBoundMemberFallsBackToTheParameterlessConstructor()
    {
        var empty = new ResolverBoundWithParameterlessDto();
        Assert.Equal(0, empty.A);
        Assert.Equal(0, empty.B);

        var options = ModifierOptions(contract => contract["A"].Ignore = true);

        var copy = NbtSerializer.Deserialize<ResolverBoundWithParameterlessDto>(
            NbtSerializer.Serialize(new ResolverBoundWithParameterlessDto(5, 7), "root", options),
            options);

        Assert.Equal(0, copy.A);
        Assert.Equal(7, copy.B);
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void ModifierRunsOncePerTypeAndNamingPolicy(SerializationMode mode)
    {
        if (mode == SerializationMode.Generated)
        {
            var context = ResolverApiContext.Default;
            var first = context.GetTypeInfo(typeof(ResolverCountDto), context.Options);
            var second = context.GetTypeInfo(typeof(ResolverCountDto), context.Options);
            AssertSameConverter(first, second);
            Assert.Same(first, second);

            var camel = ResolverApiCamelContext.Default;
            var camelFirst = camel.GetTypeInfo(typeof(ResolverCountDto), camel.Options);
            var camelSecond = camel.GetTypeInfo(typeof(ResolverCountDto), camel.Options);
            AssertSameConverter(camelFirst, camelSecond);
            Assert.Same(camelFirst, camelSecond);

            AssertResolvedCount(context.Options);
            return;
        }

        var runs = 0;
        var resolver = new NbtReflectionTypeInfoResolver(_ => runs++);
        var plain = new NbtSerializerOptions { TypeInfoResolver = resolver };
        var camelOptions = new NbtSerializerOptions
        {
            TypeInfoResolver = resolver,
            PropertyNamingPolicy = NbtNamingPolicy.CamelCase,
        };

        AssertSameConverter(
            resolver.GetTypeInfo(typeof(ResolverCountDto), plain),
            resolver.GetTypeInfo(typeof(ResolverCountDto), plain));
        Assert.Equal(1, runs);

        AssertResolvedCount(plain);
        Assert.Equal(1, runs);

        NbtSerializer.Serialize(new ResolverCountDto { Value = 7 }, "root", camelOptions);
        Assert.Equal(2, runs);

        var plainAgain = new NbtSerializerOptions { TypeInfoResolver = resolver };
        NbtSerializer.Serialize(new ResolverCountDto { Value = 7 }, "root", plainAgain);
        Assert.Equal(2, runs);
    }

    private static void AssertSameConverter(NbtTypeInfo? first, NbtTypeInfo? second)
    {
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(typeof(ResolverCountDto), first.Type);
        Assert.Same(first.Converter, second.Converter);
    }

    private static void AssertResolvedCount(NbtSerializerOptions options)
    {
        var data = NbtSerializer.Serialize(new ResolverCountDto { Value = 7 }, "root", options);
        Assert.Equal(7, NbtSerializer.Deserialize<ResolverCountDto>(data, options).Value);
    }

    private static NbtSerializerOptions ModifierOptions(Action<NbtContractInfo> modifier) => new()
    {
        TypeInfoResolver = new NbtReflectionTypeInfoResolver(modifier),
    };

    private static NbtCompound AsCompound(byte[] data) =>
        (NbtCompound)NbtSerializer.Deserialize(data).Value.Value!;

    private sealed class OptionsSlot
    {
        public NbtSerializerOptions Value = new();
    }

    private sealed class ResolverRenameDto
    {
        public string Name { get; set; } = "";

        public int Health { get; set; }
    }

    internal sealed class ResolverCountDto
    {
        public int Value { get; set; }
    }

    private sealed class ResolverSelfRefDto
    {
        public string Name { get; set; } = "";
    }

    private sealed class ResolverSiblingADto
    {
        public string Name { get; set; } = "";
    }

    private sealed class ResolverSiblingBDto
    {
        public int Value { get; set; }
    }

    private sealed class ResolverCycleADto
    {
        public string Name { get; set; } = "";
    }

    private sealed class ResolverCycleBDto
    {
        public int Value { get; set; }
    }

    private sealed class ResolverCachedSiblingDto
    {
        public int Value { get; set; }
    }

    private sealed class ResolverUsesPreResolvedDto
    {
        public string Name { get; set; } = "";
    }

    private sealed class ResolverBoundWithParameterlessDto
    {
        public ResolverBoundWithParameterlessDto()
        {
        }

        public ResolverBoundWithParameterlessDto(int a, int b)
        {
            A = a;
            B = b;
        }

        public int A { get; set; }

        public int B { get; set; }
    }
}

[NbtSerializable(typeof(NbtReflectionResolverApiTests.ResolverCountDto))]
internal partial class ResolverApiContext : NbtSerializerContext
{
}

[NbtSerializable(typeof(NbtReflectionResolverApiTests.ResolverCountDto))]
[NbtSourceGenerationOptions(NamingPolicy = NbtNamingPolicyKind.CamelCase)]
internal partial class ResolverApiCamelContext : NbtSerializerContext
{
}

internal sealed class ResolverOffsetIntConverter : NbtConverter<int>
{
    public override int Read(ref NbtReader reader, NbtSerializerOptions options) =>
        reader.GetInt32();

    public override void Write(ref NbtWriter writer, int value, NbtSerializerOptions options) =>
        writer.WriteInt32(value);

    public override bool TryGetTagType(
        int value,
        NbtSerializerOptions options,
        out NbtTagType tagType)
    {
        tagType = NbtTagType.Int;
        return true;
    }
}

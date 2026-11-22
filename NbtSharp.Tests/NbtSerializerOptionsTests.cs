namespace NbtSharp.Tests;

// The shared NbtSerializerOptions.Default is process-wide state; these tests must not interleave
// with other options-less calls made by the rest of the assembly.
[CollectionDefinition("DefaultOptions", DisableParallelization = true)]
public sealed class DefaultOptionsCollectionDefinition
{
}

[Collection("DefaultOptions")]
public sealed class NbtSerializerOptionsTests
{
    [Fact]
    public void DefaultReturnsTheSameInstance()
    {
        Assert.Same(NbtSerializerOptions.Default, NbtSerializerOptions.Default);
    }

    [Fact]
    public void FrozenDefaultRejectsEveryConverterListMutationButStaysReadable()
    {
        ForceFreeze();
        var converters = NbtSerializerOptions.Default.Converters;
        var converter = new IntAsShortConverter();

        Assert.Throws<InvalidOperationException>(() => converters.Add(converter));
        Assert.Throws<InvalidOperationException>(() => converters.Clear());
        Assert.Throws<InvalidOperationException>(() => converters.Insert(0, converter));
        Assert.Throws<InvalidOperationException>(() => converters.Remove(converter));
        Assert.Throws<InvalidOperationException>(() => converters.RemoveAt(0));
        Assert.Throws<InvalidOperationException>(() => converters[0] = converter);

        var snapshot = new NbtConverter[converters.Count];
        converters.CopyTo(snapshot, 0);
        Assert.False(converters.Contains(converter));
        Assert.True(converters.IndexOf(converter) < 0);
        Assert.Equal(snapshot.Length, converters.Count);

        var enumerated = 0;
        foreach (var _ in converters)
        {
            enumerated++;
        }

        Assert.Equal(converters.Count, enumerated);
    }

    [Fact]
    public void OptionsLessCallFreezesTheSharedDefaultConverters()
    {
        ForceFreeze();

        // If options-less calls built a fresh options instance per call, the shared instance would
        // never freeze and this Add would succeed.
        Assert.Throws<InvalidOperationException>(
            () => NbtSerializerOptions.Default.Converters.Add(new IntAsShortConverter()));
    }

    [Fact]
    public void FrozenDefaultStillRoundTripsGenericAndDomValues()
    {
        ForceFreeze();

        var intData = NbtSerializer.Serialize(42, "root");
        Assert.Equal(42, NbtSerializer.Deserialize<int>(intData));

        var stringData = NbtSerializer.Serialize("hello", "root");
        Assert.Equal("hello", NbtSerializer.Deserialize<string>(stringData));

        var rootData = NbtSerializer.Serialize(
            new NbtRoot("Player", new NbtCompound { ["Health"] = 20 }));
        var root = NbtSerializer.Deserialize(rootData);

        Assert.Equal("Player", root.Name);
        Assert.True(root.Value.Value is NbtCompound compound && compound.ContainsKey("Health"));
    }

    [Fact]
    public void DefaultCarriesTheLibraryDefaultSettings()
    {
        ForceFreeze();
        var options = NbtSerializerOptions.Default;

        Assert.Equal(NbtRootFormat.Named, options.RootFormat);
        Assert.Equal(NbtNullValueHandling.Omit, options.NullValueHandling);
        Assert.False(options.RejectDuplicateCompoundNames);
        Assert.Equal(512, options.MaxDepth);
        Assert.Equal(16 * 1024 * 1024, options.MaxCollectionLength);
        Assert.Equal(0, options.MaxDocumentBytes);
        Assert.Null(options.TypeInfoResolver);
        Assert.Null(options.PropertyNamingPolicy);
    }

    [Fact]
    public async Task ConcurrentOptionsLessCallsAllSucceedAndLeaveTheDefaultFrozen()
    {
        ForceFreeze();
        var tasks = new List<Task>();

        for (var worker = 0; worker < 8; worker++)
        {
            tasks.Add(Task.Run(() =>
            {
                for (var i = 0; i < 25; i++)
                {
                    var data = NbtSerializer.Serialize(i, "root");
                    Assert.Equal(i, NbtSerializer.Deserialize<int>(data));

                    var root = NbtSerializer.Deserialize(NbtSerializer.Serialize(new NbtRoot("r", i)));
                    Assert.Equal("r", root.Name);
                    Assert.True(root.Value.Value is int value && value == i);
                }
            }, TestContext.Current.CancellationToken));
        }

        await Task.WhenAll(tasks);

        Assert.Throws<InvalidOperationException>(
            () => NbtSerializerOptions.Default.Converters.Add(new IntAsShortConverter()));
    }

    [Fact]
    public void WithTypeInfoResolverCopiesTheFrozenDefault()
    {
        ForceFreeze();
        var resolver = new NbtReflectionTypeInfoResolver();
        var copy = NbtSerializerOptions.Default.WithTypeInfoResolver(resolver);

        Assert.NotSame(NbtSerializerOptions.Default, copy);
        Assert.Same(resolver, copy.TypeInfoResolver);

        var converter = new IntAsShortConverter();
        copy.Converters.Add(converter);
        Assert.Contains(converter, copy.Converters);
        Assert.DoesNotContain(converter, NbtSerializerOptions.Default.Converters);
    }

    [Fact]
    public void GeneratedContextOptionsAreIndependentOfTheSharedDefault()
    {
        ForceFreeze();
        var contextOptions = SourceGenerationContext.Default.Options;

        Assert.NotSame(NbtSerializerOptions.Default, contextOptions);

        // The context options are never frozen, so registering a converter always works.
        var converter = new InertConverter();
        contextOptions.Converters.Add(converter);
        Assert.Contains(converter, contextOptions.Converters);
        Assert.True(contextOptions.Converters.Remove(converter));
        Assert.DoesNotContain(converter, contextOptions.Converters);
    }

    private static void ForceFreeze()
    {
        // Idempotent: any options-less call freezes the shared default on first use.
        _ = NbtSerializer.Serialize(1, "frozen");
    }

    private sealed class IntAsShortConverter : NbtConverter<int>
    {
        public override int Read(ref NbtReader reader, NbtSerializerOptions options) => reader.GetInt16();

        public override void Write(ref NbtWriter writer, int value, NbtSerializerOptions options) =>
            writer.WriteInt16((short)value);

        public override bool TryGetTagType(int value, NbtSerializerOptions options, out NbtTagType tagType)
        {
            tagType = NbtTagType.Short;
            return true;
        }
    }

    private sealed class InertConverter : NbtConverter
    {
        public override bool CanConvert(Type typeToConvert) => false;
    }
}

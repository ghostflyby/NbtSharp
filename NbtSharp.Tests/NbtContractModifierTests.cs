using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace NbtSharp.Tests;

public sealed class NbtContractModifierTests
{
    [Fact]
    public void RenamedMemberRoundTripsUnderTheNewName()
    {
        var options = ModifierOptions(c => c.GetMember("Name")!.NbtName = "label");

        var data = NbtSerializer.Serialize(new RenameDto { Name = "steve" }, "root", options);

        var compound = AsCompound(data);
        Assert.True(compound.ContainsKey("label"));
        Assert.False(compound.ContainsKey("Name"));
        Assert.Equal("steve", NbtSerializer.Deserialize<RenameDto>(data, options).Name);
    }

    [Fact]
    public void TheOldNameIsNoLongerRecognizedOnRead()
    {
        var options = ModifierOptions(c => c.GetMember("Name")!.NbtName = "label");
        var legacy = NbtSerializer.Serialize(new NbtRoot("root", new NbtCompound
        {
            ["Name"] = "legacy",
            ["Health"] = 7,
        }));

        var actual = NbtSerializer.Deserialize<RenameDto>(legacy, options);

        Assert.Equal("", actual.Name);
        Assert.Equal(7, actual.Health);
    }

    [Fact]
    public void ModifierSeesNamesAfterAttributesAndNamingPolicy()
    {
        string? policySeen = null;
        string? attributeSeen = null;
        var options = ModifierOptions(
            c =>
            {
                policySeen = c.GetMember("UserName")!.NbtName;
                attributeSeen = c.GetMember("Value")!.NbtName;
            },
            NbtNamingPolicy.CamelCase);

        NbtSerializer.Serialize(new PolicyViewDto { UserName = "u", Value = 1 }, "root", options);

        Assert.Equal("userName", policySeen);
        Assert.Equal("wire", attributeSeen);
    }

    [Fact]
    public void ModifierRunsOncePerTypeAndNamingPolicy()
    {
        var runs = 0;
        var resolver = new NbtReflectionTypeInfoResolver(_ => runs++);
        var plain = new NbtSerializerOptions { TypeInfoResolver = resolver };
        var camel = new NbtSerializerOptions
        {
            TypeInfoResolver = resolver,
            PropertyNamingPolicy = NbtNamingPolicy.CamelCase,
        };

        NbtSerializer.Serialize(new CountDto(), "root", plain);
        NbtSerializer.Serialize(new CountDto(), "root", plain);
        Assert.Equal(1, runs);

        NbtSerializer.Serialize(new CountDto(), "root", camel);
        Assert.Equal(2, runs);

        var plainAgain = new NbtSerializerOptions { TypeInfoResolver = resolver };
        NbtSerializer.Serialize(new CountDto(), "root", plainAgain);
        Assert.Equal(2, runs);
    }

    [Fact]
    public void ContractExposesMemberMetadata()
    {
        NbtContractInfo? contract = null;
        var options = ModifierOptions(c => contract = c);
        NbtSerializer.Serialize(new RenameDto(), "root", options);

        Assert.NotNull(contract);
        Assert.Equal(typeof(RenameDto), contract!.Type);
        Assert.Equal(2, contract.Members.Count);
        Assert.NotNull(contract.GetMember("Name"));
        Assert.Null(contract.GetMember("name"));
        Assert.Equal(typeof(string), contract.GetMember("Name")!.Type);
        Assert.True(contract.GetMember("Name")!.CanWrite);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<NbtMemberInfo>)contract.Members)[0] = contract.Members[0]);
    }

    [Fact]
    public void IgnoredMemberIsOmittedOnWriteAndSkippedOnRead()
    {
        var options = ModifierOptions(c => c.GetMember("Health")!.Ignore = true);

        var data = NbtSerializer.Serialize(new RenameDto { Name = "kept", Health = 20 }, "root", options);

        var compound = AsCompound(data);
        Assert.False(compound.ContainsKey("Health"));
        Assert.True(compound.ContainsKey("Name"));

        var incoming = NbtSerializer.Serialize(new NbtRoot("root", new NbtCompound
        {
            ["Name"] = "kept",
            ["Health"] = 99,
        }));
        Assert.Equal(0, NbtSerializer.Deserialize<RenameDto>(incoming, options).Health);
    }

    [Fact]
    public void IgnoredRequiredMemberThrowsNotSupportedException()
    {
        var options = ModifierOptions(c => c.GetMember("Name")!.Ignore = true);

        var exception = Assert.Throws<NotSupportedException>(() =>
            NbtSerializer.Serialize(new IgnoredRequiredDto { Name = "x", Value = 1 }, "root", options));

        Assert.Equal(
            $"The required member 'Name' on {typeof(IgnoredRequiredDto)} is not serialized and the " +
            "selected constructor is not marked with SetsRequiredMembers.",
            exception.Message);
    }

    [Fact]
    public void IgnoringAConstructorBoundMemberWithoutOtherConstructorsThrows()
    {
        var options = ModifierOptions(c => c.GetMember("A")!.Ignore = true);

        var exception = Assert.Throws<NotSupportedException>(() =>
            NbtSerializer.Serialize(new BoundOnlyDto(1, 2), "root", options));

        Assert.Equal(
            $"The type {typeof(BoundOnlyDto)} needs a public parameterless constructor or a public " +
            "constructor whose parameters match its NBT members.",
            exception.Message);
    }

    [Fact]
    public void IgnoringAConstructorBoundMemberFallsBackToTheParameterlessConstructor()
    {
        var options = ModifierOptions(c => c.GetMember("A")!.Ignore = true);

        var copy = NbtSerializer.Deserialize<BoundWithParameterlessDto>(
            NbtSerializer.Serialize(new BoundWithParameterlessDto(5, 7), "root", options),
            options);

        Assert.Equal(0, copy.A);
        Assert.Equal(7, copy.B);
    }

    [Fact]
    public void ModifierCreatedDuplicateNameThrows()
    {
        var options = ModifierOptions(c => c.GetMember("Health")!.NbtName = "Name");

        var exception = Assert.Throws<NotSupportedException>(() =>
            NbtSerializer.Serialize(new RenameDto(), "root", options));

        Assert.Equal(
            $"The type {typeof(RenameDto)} maps more than one member to the NBT name 'Name'.",
            exception.Message);
    }

    [Fact]
    public void ModifierRenameResolvesAnAttributeNameCollision()
    {
        var plain = new NbtSerializerOptions
        {
            TypeInfoResolver = new NbtReflectionTypeInfoResolver(),
        };
        Assert.Throws<NotSupportedException>(() =>
            NbtSerializer.Serialize(new AttributeCollisionDto(), "root", plain));

        var options = ModifierOptions(c => c.GetMember("B")!.NbtName = "b");
        var copy = NbtSerializer.Deserialize<AttributeCollisionDto>(
            NbtSerializer.Serialize(new AttributeCollisionDto { A = 1, B = 2 }, "root", options),
            options);

        Assert.Equal(1, copy.A);
        Assert.Equal(2, copy.B);
    }

    [Fact]
    public void NullNbtNameThrowsArgumentNull()
    {
        var options = ModifierOptions(c => c.GetMember("Name")!.NbtName = null!);

        Assert.Throws<ArgumentNullException>(() =>
            NbtSerializer.Serialize(new RenameDto(), "root", options));
    }

    [Fact]
    public void EmptyNbtNameIsAllowed()
    {
        var options = ModifierOptions(c => c.GetMember("Name")!.NbtName = "");

        var data = NbtSerializer.Serialize(new RenameDto { Name = "anon" }, "root", options);

        var compound = AsCompound(data);
        Assert.True(compound.ContainsKey(""));
        Assert.Equal("anon", NbtSerializer.Deserialize<RenameDto>(data, options).Name);
    }

    [Fact]
    public void InjectedConverterOverridesTheAttributeConverter()
    {
        var plain = new NbtSerializerOptions
        {
            TypeInfoResolver = new NbtReflectionTypeInfoResolver(),
        };
        var options = ModifierOptions(c => c.GetMember("Value")!.Converter = new ModifierOffsetIntConverter(200));

        var attributeData = NbtSerializer.Serialize(
            new AttributeConverterDto { Value = 5 }, "root", plain);
        var overriddenData = NbtSerializer.Serialize(
            new AttributeConverterDto { Value = 5 }, "root", options);

        Assert.Equal(15, (int)AsCompound(attributeData)["Value"].Value!);
        Assert.Equal(205, (int)AsCompound(overriddenData)["Value"].Value!);
        Assert.Equal(5, NbtSerializer.Deserialize<AttributeConverterDto>(overriddenData, options).Value);
    }

    [Fact]
    public void InjectedFactoryConverterRoundTrips()
    {
        var options = ModifierOptions(c => c.GetMember("Value")!.Converter = new ModifierIntFactory());

        var copy = NbtSerializer.Deserialize<PlainIntDto>(
            NbtSerializer.Serialize(new PlainIntDto { Value = 5 }, "root", options),
            options);

        Assert.Equal(5, copy.Value);
        Assert.Equal(15, (int)AsCompound(
            NbtSerializer.Serialize(new PlainIntDto { Value = 5 }, "root", options))["Value"].Value!);
    }

    [Fact]
    public void InjectedConverterWithMismatchedCanConvertThrows()
    {
        var options = ModifierOptions(c => c.GetMember("Value")!.Converter = new ModifierStringConverter());

        var exception = Assert.Throws<NotSupportedException>(() =>
            NbtSerializer.Serialize(new PlainIntDto { Value = 5 }, "root", options));

        Assert.Equal(
            $"The converter {typeof(ModifierStringConverter)} assigned to member 'Value' does not " +
            $"support the member type '{typeof(int)}'.",
            exception.Message);
    }

    [Fact]
    public void NullConverterFallsBackToOptionsResolution()
    {
        var options = ModifierOptions(c =>
        {
            var member = c.GetMember("Value")!;
            if (c.Type == typeof(PlainIntDto))
            {
                member.Converter = new ModifierOffsetIntConverter(100);
            }
        });

        var shifted = NbtSerializer.Serialize(new PlainIntDto { Value = 5 }, "root", options);
        Assert.Equal(105, (int)AsCompound(shifted)["Value"].Value!);

        var plain = new NbtSerializerOptions
        {
            TypeInfoResolver = new NbtReflectionTypeInfoResolver(),
        };
        var direct = NbtSerializer.Serialize(new PlainIntDto { Value = 5 }, "root", plain);
        Assert.Equal(5, (int)AsCompound(direct)["Value"].Value!);
    }

    [Fact]
    public void WithAddedModifierRunsCallbacksInOrderAndStacks()
    {
        var calls = new List<string>();
        var first = new NbtReflectionTypeInfoResolver(c =>
        {
            calls.Add("first");
            c.GetMember("Name")!.NbtName = "from-first";
        });
        var second = first.WithAddedModifier(c =>
        {
            calls.Add("second:" + c.GetMember("Name")!.NbtName);
            c.GetMember("Name")!.NbtName = "from-second";
        });

        var options = new NbtSerializerOptions { TypeInfoResolver = second };
        var data = NbtSerializer.Serialize(new RenameDto { Name = "x" }, "root", options);

        Assert.Equal(["first", "second:from-first"], calls);
        Assert.True(AsCompound(data).ContainsKey("from-second"));
    }

    [Fact]
    public void WithAddedModifierLeavesTheOriginalResolverCacheIntact()
    {
        var first = new NbtReflectionTypeInfoResolver(c => c.GetMember("Name")!.NbtName = "original-name");
        var options = new NbtSerializerOptions { TypeInfoResolver = first };
        var before = NbtSerializer.Serialize(new RenameDto { Name = "x" }, "root", options);

        var derived = first.WithAddedModifier(c => c.GetMember("Name")!.NbtName = "derived-name");
        var derivedOptions = new NbtSerializerOptions { TypeInfoResolver = derived };
        var derivedData = NbtSerializer.Serialize(new RenameDto { Name = "x" }, "root", derivedOptions);

        var after = NbtSerializer.Serialize(new RenameDto { Name = "x" }, "root", options);

        Assert.True(AsCompound(derivedData).ContainsKey("derived-name"));
        Assert.True(AsCompound(after).ContainsKey("original-name"));
        Assert.Equal(before, after);
    }

    [Fact]
    public void FrozenContractRejectsStashedMutationsAndKeepsSerializing()
    {
        NbtMemberInfo? stash = null;
        var options = ModifierOptions(c => stash = c.GetMember("Name"));
        var value = new RenameDto { Name = "x", Health = 1 };
        var first = NbtSerializer.Serialize(value, "root", options);

        Assert.NotNull(stash);
        Assert.Throws<InvalidOperationException>(() => stash!.NbtName = "other");
        Assert.Throws<InvalidOperationException>(() => stash!.Ignore = true);
        Assert.Throws<InvalidOperationException>(() => stash!.Converter = new ModifierOffsetIntConverter());
        Assert.Throws<InvalidOperationException>(() => stash!.Order = 9);

        Assert.Equal(first, NbtSerializer.Serialize(value, "root", options));
    }

    [Fact]
    public void ReentrantResolutionOfTheSameTypeThrows()
    {
        var optionsSlot = new OptionsSlot();
        var resolver = new NbtReflectionTypeInfoResolver(c =>
        {
            if (c.Type == typeof(SelfRefDto))
            {
                NbtSerializer.Serialize(new SelfRefDto(), "nested", optionsSlot.Value);
            }
        });
        optionsSlot.Value = optionsSlot.Value.WithTypeInfoResolver(resolver);
        var options = optionsSlot.Value;

        var exception = Assert.Throws<InvalidOperationException>(() =>
            NbtSerializer.Serialize(new SelfRefDto(), "root", options));

        Assert.Contains("re-entered", exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(SelfRefDto).ToString(), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NestedResolutionOfASiblingTypeSucceedsExactlyOnce()
    {
        var siblingRuns = 0;
        var optionsSlot = new OptionsSlot();
        var resolver = new NbtReflectionTypeInfoResolver(c =>
        {
            if (c.Type == typeof(SiblingADto))
            {
                NbtSerializer.Serialize(new SiblingBDto { Value = 3 }, "sibling", optionsSlot.Value);
            }
            else if (c.Type == typeof(SiblingBDto))
            {
                siblingRuns++;
            }
        });
        optionsSlot.Value = optionsSlot.Value.WithTypeInfoResolver(resolver);
        var options = optionsSlot.Value;

        var data = NbtSerializer.Serialize(new SiblingADto { Name = "a" }, "root", options);

        Assert.Equal(1, siblingRuns);
        Assert.Equal("a", NbtSerializer.Deserialize<SiblingADto>(data, options).Name);

        var siblingData = NbtSerializer.Serialize(new SiblingBDto { Value = 4 }, "root", options);
        Assert.Equal(4, NbtSerializer.Deserialize<SiblingBDto>(siblingData, options).Value);
        Assert.Equal(1, siblingRuns);
    }

    [Fact]
    public void CyclicNestedResolutionThrows()
    {
        var optionsSlot = new OptionsSlot();
        var resolver = new NbtReflectionTypeInfoResolver(c =>
        {
            if (c.Type == typeof(CycleADto))
            {
                NbtSerializer.Serialize(new CycleBDto(), "b", optionsSlot.Value);
            }
            else if (c.Type == typeof(CycleBDto))
            {
                NbtSerializer.Serialize(new CycleADto(), "a", optionsSlot.Value);
            }
        });
        optionsSlot.Value = optionsSlot.Value.WithTypeInfoResolver(resolver);
        var options = optionsSlot.Value;

        Assert.Throws<InvalidOperationException>(() =>
            NbtSerializer.Serialize(new CycleADto(), "root", options));
    }

    [Fact]
    public void NestedResolutionOfCachedAndResolverlessTypesSucceeds()
    {
        var optionsSlot = new OptionsSlot();
        var resolver = new NbtReflectionTypeInfoResolver(c =>
        {
            if (c.Type != typeof(UsesPreResolvedDto))
            {
                return;
            }

            NbtSerializer.Serialize(new CachedSiblingDto { Value = 2 }, "cached", optionsSlot.Value);
            NbtSerializer.Serialize(42, "plain");
        });
        optionsSlot.Value = optionsSlot.Value.WithTypeInfoResolver(resolver);
        var options = optionsSlot.Value;
        NbtSerializer.Serialize(new CachedSiblingDto { Value = 1 }, "warm", options);

        var data = NbtSerializer.Serialize(new UsesPreResolvedDto { Name = "ok" }, "root", options);

        Assert.Equal("ok", NbtSerializer.Deserialize<UsesPreResolvedDto>(data, options).Name);
    }

    [Fact]
    public void OrderChangesTheWrittenMemberSequence()
    {
        var options = ModifierOptions(c =>
        {
            c.GetMember("Third")!.Order = -1;
            c.GetMember("Second")!.Order = 10;
        });

        var data = NbtSerializer.Serialize(
            new OrderDto { First = 1, Second = 2, Third = 3 },
            "root",
            options);

        Assert.True(IndexOfNameBytes(data, "Third"u8) < IndexOfNameBytes(data, "First"u8));
        Assert.True(IndexOfNameBytes(data, "First"u8) < IndexOfNameBytes(data, "Second"u8));
    }

    [Fact]
    public void EqualOrderKeepsDeclarationOrder()
    {
        var options = ModifierOptions(c =>
        {
            c.GetMember("First")!.Order = 5;
            c.GetMember("Second")!.Order = 5;
        });

        var data = NbtSerializer.Serialize(
            new OrderDto { First = 1, Second = 2, Third = 3 },
            "root",
            options);

        Assert.True(IndexOfNameBytes(data, "First"u8) < IndexOfNameBytes(data, "Second"u8));
    }

    [Fact]
    public void OrderDoesNotAffectReading()
    {
        var options = ModifierOptions(c => c.GetMember("Third")!.Order = -1);

        var data = NbtSerializer.Serialize(
            new OrderDto { First = 1, Second = 2, Third = 3 },
            "root",
            options);
        var copy = NbtSerializer.Deserialize<OrderDto>(data, options);

        Assert.Equal(1, copy.First);
        Assert.Equal(2, copy.Second);
        Assert.Equal(3, copy.Third);
    }

    [Fact]
    public void PolymorphicEnvelopeSkipsModifiersForTheBaseContract()
    {
        var baseRuns = 0;
        var options = ModifierOptions(c =>
        {
            if (c.Type == typeof(ModifierPolyBase))
            {
                baseRuns++;
            }
        });

        ModifierPolyBase value = new ModifierPolyDerived { Value = 4 };
        var data = NbtSerializer.Serialize(value, "root", options);
        NbtSerializer.Deserialize<ModifierPolyBase>(data, options);

        Assert.Equal(0, baseRuns);
    }

    [Fact]
    public void DerivedPolymorphicTypeRunsModifiers()
    {
        var derivedRuns = 0;
        var options = ModifierOptions(c =>
        {
            if (c.Type != typeof(ModifierPolyDerived))
            {
                return;
            }

            derivedRuns++;
            c.GetMember("Value")!.NbtName = "v";
        });

        ModifierPolyBase value = new ModifierPolyDerived { Value = 4 };
        var data = NbtSerializer.Serialize(value, "root", options);

        var envelope = AsCompound(data);
        var payload = (NbtCompound)envelope["$value"].Value!;
        Assert.True(payload.ContainsKey("v"));

        var copy = (ModifierPolyDerived)NbtSerializer.Deserialize<ModifierPolyBase>(data, options);
        Assert.Equal(4, copy.Value);
        Assert.Equal(1, derivedRuns);
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
                    results[i] = NbtSerializer.Serialize(new CountDto { Value = 7 }, "root", options);
                },
                TestContext.Current.CancellationToken))
            .ToArray();
        await Task.WhenAll(tasks);

        Assert.Equal(1, Volatile.Read(ref runs.Value));
        Assert.All(results, result => Assert.Equal(results[0], result));
    }

    [Fact]
    public void HiddenMembersStillThrowWithoutModifiers()
    {
        var options = ReflectionOptions();

        var exception = Assert.Throws<NotSupportedException>(() =>
            NbtSerializer.Serialize(new HiddenMemberDerived(), "root", options));

        Assert.Equal(
            $"The member 'Value' on {typeof(HiddenMemberDerived)} hides a base member with the same " +
            "name. Rename the derived member or exclude one of the members with [NbtIgnore].",
            exception.Message);
    }

    [Fact]
    public void CallbackIgnoreExemptsAHiddenBaseChain()
    {
        var plain = ReflectionOptions();
        var value = new ChainHiddenLeaf { Value = 2L };
        ((ChainHiddenBase)value).Value = 1;
        Assert.Throws<NotSupportedException>(() => NbtSerializer.Serialize(value, "root", plain));

        var options = ModifierOptions(c =>
            c.Members.First(member => member.Type == typeof(int)).Ignore = true);

        var data = NbtSerializer.Serialize(value, "root", options);
        var compound = AsCompound(data);

        Assert.Equal(2L, (long)compound["Value"].Value!);
        var copy = NbtSerializer.Deserialize<ChainHiddenLeaf>(data, options);
        Assert.Equal(0, ((ChainHiddenBase)copy).Value);
        Assert.Equal(2L, copy.Value);
    }

    [Fact]
    public void DuplicateAttributeNamesStillThrowWithoutModifiers()
    {
        var options = ReflectionOptions();

        var exception = Assert.Throws<NotSupportedException>(() =>
            NbtSerializer.Serialize(new AttributeCollisionDto(), "root", options));

        Assert.Equal(
            $"The type {typeof(AttributeCollisionDto)} maps more than one member to the NBT name 'x'.",
            exception.Message);
    }

    [Fact]
    public void MissingConstructorsStillThrowWithoutModifiers()
    {
        var options = ReflectionOptions();

        var exception = Assert.Throws<NotSupportedException>(() =>
            NbtSerializer.Serialize(new NoUsableCtorDto(1), "root", options));

        Assert.Equal(
            $"The type {typeof(NoUsableCtorDto)} needs a public parameterless constructor or a public " +
            "constructor whose parameters match its NBT members.",
            exception.Message);
    }

    [Fact]
    public void UnmappedRequiredMembersStillThrowWithoutModifiers()
    {
        var options = ReflectionOptions();

        var exception = Assert.Throws<NotSupportedException>(() =>
            NbtSerializer.Serialize(new SnapshotUnmappedRequiredDto { Ignored = "", Value = 1 }, "root", options));

        Assert.Equal(
            $"The required member 'Ignored' on {typeof(SnapshotUnmappedRequiredDto)} is not serialized " +
            "and the selected constructor is not marked with SetsRequiredMembers.",
            exception.Message);
    }

    [Fact]
    public void FieldUseConverterWithoutIncludeStillThrowsWithoutModifiers()
    {
        var options = ReflectionOptions();

        var exception = Assert.Throws<NotSupportedException>(() =>
            NbtSerializer.Serialize(new FieldUseConverterWithoutIncludeDto(), "root", options));

        Assert.Equal(
            "The [NbtUseConverter] declaration on member 'Value' is invalid: fields require " +
            "[NbtInclude] in addition to [NbtUseConverter].",
            exception.Message);
    }

    [Fact]
    public void FieldExtensionDataWithoutIncludeStillThrowsWithoutModifiers()
    {
        var options = ReflectionOptions();

        var exception = Assert.Throws<NotSupportedException>(() =>
            NbtSerializer.Serialize(new ExtensionFieldWithoutIncludeDto(), "root", options));

        Assert.Equal(
            "The [NbtExtensionData] declaration on member 'Extra' is invalid: fields require " +
            "[NbtInclude] in addition to [NbtExtensionData].",
            exception.Message);
    }

    [Theory]
    [InlineData(typeof(AbstractConverterDto), "the converter type must not be abstract")]
    [InlineData(typeof(NoDefaultCtorConverterDto), "the converter type must have a public parameterless constructor")]
    [InlineData(typeof(WrongTypeConverterDto), "the converter handles 'System.String' but the member type is 'System.Int32'")]
    public void InvalidAttributeConverterDeclarationsStillThrowWithoutModifiers(Type dtoType, string reason)
    {
        var options = ReflectionOptions();

        var exception = Assert.Throws<NotSupportedException>(() => options.GetConverter(dtoType));

        Assert.Equal(
            $"The [NbtUseConverter] declaration on member 'Value' is invalid: {reason}.",
            exception.Message);
    }

    [Fact]
    public void IgnoredMembersSkipConverterValidationWithoutModifiers()
    {
        var options = ReflectionOptions();

        var data = NbtSerializer.Serialize(new IgnoredInvalidConverterDto { Kept = 8 }, "root", options);

        var compound = AsCompound(data);
        Assert.True(compound.ContainsKey("Kept"));
        Assert.False(compound.ContainsKey("Property"));
        Assert.False(compound.ContainsKey("Field"));
    }

    [Fact]
    public void MixedFieldsAndPropertiesKeepTheTokenOrderWithoutModifiers()
    {
        var options = ReflectionOptions();

        var data = NbtSerializer.Serialize(new MixedMemberOrderDto(), "root", options);

        Assert.True(IndexOfNameBytes(data, "FieldTwo"u8) < IndexOfNameBytes(data, "PropOne"u8));
        Assert.True(IndexOfNameBytes(data, "PropOne"u8) < IndexOfNameBytes(data, "PropThree"u8));
    }

    private static NbtSerializerOptions ModifierOptions(
        Action<NbtContractInfo> modifier,
        NbtNamingPolicy? policy = null)
    {
        return new NbtSerializerOptions
        {
            TypeInfoResolver = new NbtReflectionTypeInfoResolver(modifier),
            PropertyNamingPolicy = policy,
        };
    }

    private static NbtSerializerOptions ReflectionOptions() => new()
    {
        TypeInfoResolver = new NbtReflectionTypeInfoResolver(),
    };

    private static NbtCompound AsCompound(byte[] data) =>
        (NbtCompound)NbtSerializer.Deserialize(data).Value.Value!;

    private static int IndexOfNameBytes(byte[] data, ReadOnlySpan<byte> needle) =>
        data.AsSpan().IndexOf(needle);

    private sealed class OptionsSlot
    {
        public NbtSerializerOptions Value = new();
    }

    private sealed class RenameDto
    {
        public string Name { get; set; } = "";

        public int Health { get; set; }
    }

    private sealed class PolicyViewDto
    {
        public string UserName { get; set; } = "";

        [NbtPropertyName("wire")]
        public int Value { get; set; }
    }

    private sealed class CountDto
    {
        public int Value { get; set; }
    }

    private sealed class IgnoredRequiredDto
    {
        public required string Name { get; set; }

        public int Value { get; set; }
    }

    private sealed class BoundOnlyDto
    {
        public BoundOnlyDto(int a, int b)
        {
            A = a;
            B = b;
        }

        public int A { get; }

        public int B { get; }
    }

    private sealed class BoundWithParameterlessDto
    {
        public BoundWithParameterlessDto()
        {
        }

        public BoundWithParameterlessDto(int a, int b)
        {
            A = a;
            B = b;
        }

        public int A { get; set; }

        public int B { get; set; }
    }

    private sealed class AttributeCollisionDto
    {
        [NbtPropertyName("x")]
        public int A { get; set; }

        [NbtPropertyName("x")]
        public int B { get; set; }
    }

    private sealed class AttributeConverterDto
    {
        [NbtUseConverter(typeof(ModifierOffsetIntConverter))]
        public int Value { get; set; }
    }

    private sealed class PlainIntDto
    {
        public int Value { get; set; }
    }

    private sealed class SelfRefDto
    {
        public string Name { get; set; } = "";
    }

    private sealed class SiblingADto
    {
        public string Name { get; set; } = "";
    }

    private sealed class SiblingBDto
    {
        public int Value { get; set; }
    }

    private sealed class CycleADto
    {
        public string Name { get; set; } = "";
    }

    private sealed class CycleBDto
    {
        public int Value { get; set; }
    }

    private sealed class CachedSiblingDto
    {
        public int Value { get; set; }
    }

    private sealed class UsesPreResolvedDto
    {
        public string Name { get; set; } = "";
    }

    private sealed class OrderDto
    {
        public int First { get; set; }

        public int Second { get; set; }

        public int Third { get; set; }
    }

    [NbtDerived(typeof(ModifierPolyDerived))]
    private abstract class ModifierPolyBase;

    private sealed class ModifierPolyDerived : ModifierPolyBase
    {
        public int Value { get; set; }
    }

    private abstract class HiddenMemberBase
    {
        public virtual int Value { get; set; }
    }

    private sealed class HiddenMemberDerived : HiddenMemberBase
    {
        public new int Value { get; set; }
    }

    private class ChainHiddenBase
    {
        public virtual int Value { get; set; }
    }

    private sealed class ChainHiddenLeaf : ChainHiddenBase
    {
        public new long Value { get; set; }
    }

    private sealed class NoUsableCtorDto
    {
        public NoUsableCtorDto(int unrelated)
        {
            _ = unrelated;
        }
    }

    private sealed class SnapshotUnmappedRequiredDto
    {
        [NbtIgnore]
        public required string Ignored { get; set; }

        public int Value { get; set; }
    }

    [SuppressMessage("ReSharper", "NotAccessedField.Compiler")]
    private sealed class FieldUseConverterWithoutIncludeDto
    {
        [NbtUseConverter(typeof(ModifierOffsetIntConverter))]
        public int Value = 1;
    }

    private sealed class ExtensionFieldWithoutIncludeDto
    {
        [NbtExtensionData]
        public NbtCompound Extra = new();
    }

    private sealed class AbstractConverterDto
    {
        [NbtUseConverter(typeof(ModifierAbstractIntConverter))]
        public int Value { get; set; }
    }

    private sealed class NoDefaultCtorConverterDto
    {
        [NbtUseConverter(typeof(ModifierNoDefaultCtorConverter))]
        public int Value { get; set; }
    }

    private sealed class WrongTypeConverterDto
    {
        [NbtUseConverter(typeof(ModifierStringConverter))]
        public int Value { get; set; }
    }

    [SuppressMessage("ReSharper", "NotAccessedField.Compiler")]
    private sealed class IgnoredInvalidConverterDto
    {
        [NbtIgnore]
        [NbtUseConverter(typeof(ModifierAbstractIntConverter))]
        public int Property { get; set; }

        [NbtIgnore]
        [NbtInclude]
        [NbtUseConverter(typeof(ModifierAbstractIntConverter))]
        public int Field = 2;

        public int Kept { get; set; }
    }

    [SuppressMessage("ReSharper", "NotAccessedField.Compiler")]
    private sealed class MixedMemberOrderDto
    {
        public int PropOne { get; set; }

        [NbtInclude]
        public int FieldTwo = 2;

        public int PropThree { get; set; }
    }
}

internal sealed class ModifierOffsetIntConverter : NbtConverter<int>
{
    private readonly int _offset;

    public ModifierOffsetIntConverter()
        : this(10)
    {
    }

    public ModifierOffsetIntConverter(int offset) => _offset = offset;

    public override int Read(ref NbtReader reader, NbtSerializerOptions options) =>
        reader.GetInt32() - _offset;

    public override void Write(ref NbtWriter writer, int value, NbtSerializerOptions options) =>
        writer.WriteInt32(value + _offset);

    public override bool TryGetTagType(
        int value,
        NbtSerializerOptions options,
        out NbtTagType tagType)
    {
        tagType = NbtTagType.Int;
        return true;
    }
}

internal abstract class ModifierAbstractIntConverter : NbtConverter<int>
{
    public override int Read(ref NbtReader reader, NbtSerializerOptions options) => reader.GetInt32();

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

internal sealed class ModifierNoDefaultCtorConverter : NbtConverter<int>
{
    public ModifierNoDefaultCtorConverter(int unused)
    {
        _ = unused;
    }

    public override int Read(ref NbtReader reader, NbtSerializerOptions options) => reader.GetInt32();

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

internal sealed class ModifierStringConverter : NbtConverter<string>
{
    public override string Read(ref NbtReader reader, NbtSerializerOptions options) =>
        reader.GetString();

    public override void Write(ref NbtWriter writer, string value, NbtSerializerOptions options) =>
        writer.WriteString(value);

    public override bool TryGetTagType(
        string value,
        NbtSerializerOptions options,
        out NbtTagType tagType)
    {
        tagType = NbtTagType.String;
        return true;
    }
}

internal sealed class ModifierIntFactory : NbtConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(int);

    public override NbtConverter CreateConverter(Type typeToConvert, NbtSerializerOptions options) =>
        new ModifierOffsetIntConverter(10);
}

namespace NbtSharp.Tests;

/// <summary>
/// Contract-modifier behavior, checked through both serialization paths. The generated branch uses
/// twin types whose compile-time attributes express the same final contract the reflection branch
/// produces with a modifier callback; both branches assert the same wire facts.
/// </summary>
public sealed class NbtContractModifierTests
{
    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void RenamedMemberRoundTripsUnderTheNewName(SerializationMode mode)
    {
        if (mode == SerializationMode.Generated)
        {
            AssertRenamed(
                GeneratedPath(),
                new TwinRenameDto { Name = "steve" },
                static value => value.Name,
                static value => value.Health);
            return;
        }

        AssertRenamed(
            ReflectionPath(static contract => contract["Name"].NbtName = "label"),
            new RenameDto { Name = "steve" },
            static value => value.Name,
            static value => value.Health);
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void TheOldNameIsNoLongerRecognizedOnRead(SerializationMode mode)
    {
        if (mode == SerializationMode.Generated)
        {
            AssertLegacyNameIsIgnored(
                GeneratedPath(),
                new TwinRenameDto { Name = "steve", Health = 3 },
                static value => value.Name,
                static value => value.Health);
            return;
        }

        AssertLegacyNameIsIgnored(
            ReflectionPath(static contract => contract["Name"].NbtName = "label"),
            new RenameDto { Name = "steve", Health = 3 },
            static value => value.Name,
            static value => value.Health);
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void ModifierSeesNamesAfterAttributesAndNamingPolicy(SerializationMode mode)
    {
        if (mode == SerializationMode.Generated)
        {
            AssertPolicyNames(
                SerializationTestPath.Generated(ContractModifierCamelTwinContext.Default),
                new TwinPolicyViewDto { UserName = "u", Value = 1 },
                static value => value.UserName,
                static value => value.Value);
            return;
        }

        string? policySeen = null;
        string? attributeSeen = null;
        var path = ReflectionPath(
            contract =>
            {
                policySeen = contract["UserName"].NbtName;
                attributeSeen = contract["Value"].NbtName;
            },
            NbtNamingPolicy.CamelCase);

        AssertPolicyNames(
            path,
            new PolicyViewDto { UserName = "u", Value = 1 },
            static value => value.UserName,
            static value => value.Value);

        Assert.Equal("userName", policySeen);
        Assert.Equal("wire", attributeSeen);
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void IgnoredMemberIsOmittedOnWriteAndSkippedOnRead(SerializationMode mode)
    {
        if (mode == SerializationMode.Generated)
        {
            AssertIgnoredHealth(
                GeneratedPath(),
                new TwinIgnoredHealthDto { Name = "kept", Health = 20 },
                static value => value.Name,
                static value => value.Health);
            return;
        }

        AssertIgnoredHealth(
            ReflectionPath(static contract => contract["Health"].Ignore = true),
            new RenameDto { Name = "kept", Health = 20 },
            static value => value.Name,
            static value => value.Health);
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void EmptyNbtNameIsAllowed(SerializationMode mode)
    {
        if (mode == SerializationMode.Generated)
        {
            AssertEmptyName(
                GeneratedPath(),
                new TwinEmptyNameDto { Name = "anon" },
                static value => value.Name,
                static value => value.Health);
            return;
        }

        AssertEmptyName(
            ReflectionPath(static contract => contract["Name"].NbtName = ""),
            new RenameDto { Name = "anon" },
            static value => value.Name,
            static value => value.Health);
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void InjectedConverterOverridesTheAttributeConverter(SerializationMode mode)
    {
        if (mode == SerializationMode.Generated)
        {
            AssertOffsetConverter(
                GeneratedPath(),
                new TwinInjectedConverterDto { Value = 5 },
                static value => value.Value,
                expectedWire: 205);
            return;
        }

        var attributeData = NbtSerializer.Serialize(
            new AttributeConverterDto { Value = 5 },
            "root",
            ReflectionOptions());
        Assert.Equal(15, (int)AsCompound(attributeData)["Value"].Value!);

        AssertOffsetConverter(
            ReflectionPath(static contract =>
                contract["Value"].Converter = new ModifierOffsetIntConverter(200)),
            new AttributeConverterDto { Value = 5 },
            static value => value.Value,
            expectedWire: 205);
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void InjectedFactoryConverterRoundTrips(SerializationMode mode)
    {
        if (mode == SerializationMode.Generated)
        {
            AssertOffsetConverter(
                GeneratedPath(),
                new TwinFactoryOffsetDto { Value = 5 },
                static value => value.Value,
                expectedWire: 15);
            return;
        }

        AssertOffsetConverter(
            ReflectionPath(static contract => contract["Value"].Converter = new ModifierIntFactory()),
            new PlainIntDto { Value = 5 },
            static value => value.Value,
            expectedWire: 15);
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void NullConverterFallsBackToOptionsResolution(SerializationMode mode)
    {
        if (mode == SerializationMode.Generated)
        {
            AssertPlainInt(
                GeneratedPath(),
                new PlainIntDto { Value = 5 },
                static value => value.Value);
            return;
        }

        var options = ModifierOptions(static contract =>
        {
            if (contract.Type == typeof(NullConverterTargetDto))
            {
                contract["Value"].Converter = new ModifierOffsetIntConverter(100);
            }
        });

        var shifted = NbtSerializer.Serialize(new NullConverterTargetDto { Value = 5 }, "root", options);
        Assert.Equal(105, (int)AsCompound(shifted)["Value"].Value!);
        Assert.Equal(5, NbtSerializer.Deserialize<NullConverterTargetDto>(shifted, options).Value);

        AssertPlainInt(
            SerializationTestPath.Reflection(options),
            new PlainIntDto { Value = 5 },
            static value => value.Value);
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void OrderChangesTheWrittenMemberSequence(SerializationMode mode)
    {
        if (mode == SerializationMode.Generated)
        {
            AssertReorderedMembers(
                GeneratedPath(),
                new TwinOrderSequenceDto { First = 1, Second = 2, Third = 3 },
                static value => value.First,
                static value => value.Second,
                static value => value.Third);
            return;
        }

        AssertReorderedMembers(
            ReflectionPath(static contract =>
            {
                contract["Third"].Order = -1;
                contract["Second"].Order = 10;
            }),
            new OrderDto { First = 1, Second = 2, Third = 3 },
            static value => value.First,
            static value => value.Second,
            static value => value.Third);
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void EqualOrderKeepsDeclarationOrder(SerializationMode mode)
    {
        if (mode == SerializationMode.Generated)
        {
            AssertDeclarationOrder(
                GeneratedPath(),
                new TwinDeclarationOrderDto { First = 1, Second = 2, Third = 3 },
                static value => value.First,
                static value => value.Second,
                static value => value.Third);
            return;
        }

        AssertDeclarationOrder(
            ReflectionPath(static contract =>
            {
                contract["First"].Order = 5;
                contract["Second"].Order = 5;
            }),
            new OrderDto { First = 1, Second = 2, Third = 3 },
            static value => value.First,
            static value => value.Second,
            static value => value.Third);
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void OrderDoesNotAffectReading(SerializationMode mode)
    {
        if (mode == SerializationMode.Generated)
        {
            AssertOrderedRoundTrip(
                GeneratedPath(),
                new TwinOrderSequenceDto { First = 1, Second = 2, Third = 3 },
                static value => value.First,
                static value => value.Second,
                static value => value.Third);
            return;
        }

        AssertOrderedRoundTrip(
            ReflectionPath(static contract => contract["Third"].Order = -1),
            new OrderDto { First = 1, Second = 2, Third = 3 },
            static value => value.First,
            static value => value.Second,
            static value => value.Third);
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void PolymorphicEnvelopeSkipsModifiersForTheBaseContract(SerializationMode mode)
    {
        if (mode == SerializationMode.Generated)
        {
            AssertPlainPolymorphicEnvelope<TwinPolyBase, TwinPlainPolyDerived>(
                GeneratedPath(),
                new TwinPlainPolyDerived { Name = "d", Value = 4 },
                static value => value.Name,
                static value => value.Value);
            return;
        }

        var baseRuns = 0;
        var path = ReflectionPath(contract =>
        {
            if (contract.Type != typeof(ModifierPolyBase))
            {
                return;
            }

            baseRuns++;
            contract["Name"].NbtName = "renamed";
        });

        AssertPlainPolymorphicEnvelope<ModifierPolyBase, ModifierPlainPolyDerived>(
            path,
            new ModifierPlainPolyDerived { Name = "d", Value = 4 },
            static value => value.Name,
            static value => value.Value);

        Assert.Equal(0, baseRuns);
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void DerivedPolymorphicTypeRunsModifiers(SerializationMode mode)
    {
        if (mode == SerializationMode.Generated)
        {
            AssertRenamedPolymorphicPayload<TwinPolyBase, TwinPolyDerived>(
                GeneratedPath(),
                new TwinPolyDerived { Name = "d", Value = 4 },
                static value => value.Name,
                static value => value.Value);
            return;
        }

        var derivedRuns = 0;
        var path = ReflectionPath(contract =>
        {
            if (contract.Type != typeof(ModifierPolyDerived))
            {
                return;
            }

            derivedRuns++;
            contract["Value"].NbtName = "v";
        });

        AssertRenamedPolymorphicPayload<ModifierPolyBase, ModifierPolyDerived>(
            path,
            new ModifierPolyDerived { Name = "d", Value = 4 },
            static value => value.Name,
            static value => value.Value);

        Assert.Equal(1, derivedRuns);
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void CallbackIgnoreExemptsAHiddenBaseChain(SerializationMode mode)
    {
        if (mode == SerializationMode.Generated)
        {
            var twin = new TwinHiddenLeaf { Value = 2L };
            ((TwinHiddenBase)twin).Value = 1;
            AssertHiddenChain(
                GeneratedPath(),
                twin,
                static value => ((TwinHiddenBase)value).Value,
                static value => value.Value);
            return;
        }

        var leaf = new ChainHiddenLeaf { Value = 2L };
        ((ChainHiddenBase)leaf).Value = 1;
        Assert.Throws<NotSupportedException>(() =>
            NbtSerializer.Serialize(leaf, "root", ReflectionOptions()));

        AssertHiddenChain(
            ReflectionPath(static contract =>
                contract.Members.First(member => member.Type == typeof(int)).Ignore = true),
            leaf,
            static value => ((ChainHiddenBase)value).Value,
            static value => value.Value);
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void MixedFieldsAndPropertiesKeepTheTokenOrderWithoutModifiers(SerializationMode mode)
    {
        var path = mode == SerializationMode.Generated
            ? GeneratedPath()
            : SerializationTestPath.Reflection(ReflectionOptions());

        AssertMixedMemberOrder(path);
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void ModifierRenameResolvesAnAttributeNameCollision(SerializationMode mode)
    {
        if (mode == SerializationMode.Generated)
        {
            AssertResolvedCollision(
                GeneratedPath(),
                new TwinResolvedCollisionDto { A = 1, B = 2 },
                static value => value.A,
                static value => value.B);
            return;
        }

        Assert.Throws<NotSupportedException>(() =>
            NbtSerializer.Serialize(new AttributeCollisionDto { A = 1, B = 2 }, "root", ReflectionOptions()));

        AssertResolvedCollision(
            ReflectionPath(static contract => contract["B"].NbtName = "b"),
            new AttributeCollisionDto { A = 1, B = 2 },
            static value => value.A,
            static value => value.B);
    }

    [Fact]
    public void RenameModifierAndTwinProduceIdenticalBytes()
    {
        var reflection = ReflectionPath(static contract => contract["Name"].NbtName = "label");
        var generated = GeneratedPath();

        var reflectedData = reflection.Serialize(new RenameDto { Name = "steve" });
        var generatedData = generated.Serialize(new TwinRenameDto { Name = "steve" });

        Assert.Equal(reflectedData, generatedData);

        var fromGenerated = reflection.Deserialize<RenameDto>(generatedData);
        Assert.Equal("steve", fromGenerated.Name);

        var fromReflected = generated.Deserialize<TwinRenameDto>(reflectedData);
        Assert.Equal("steve", fromReflected.Name);
    }

    [Fact]
    public void IgnoreModifierAndTwinProduceIdenticalBytes()
    {
        var reflection = ReflectionPath(static contract => contract["Health"].Ignore = true);
        var generated = GeneratedPath();

        var reflectedData = reflection.Serialize(new RenameDto { Name = "kept", Health = 20 });
        var generatedData = generated.Serialize(new TwinIgnoredHealthDto { Name = "kept", Health = 20 });

        Assert.Equal(reflectedData, generatedData);

        var fromGenerated = reflection.Deserialize<RenameDto>(generatedData);
        Assert.Equal("kept", fromGenerated.Name);
        Assert.Equal(0, fromGenerated.Health);

        var fromReflected = generated.Deserialize<TwinIgnoredHealthDto>(reflectedData);
        Assert.Equal("kept", fromReflected.Name);
        Assert.Equal(0, fromReflected.Health);
    }

    [Fact]
    public void ConverterModifierAndTwinProduceIdenticalBytes()
    {
        var reflection = ReflectionPath(static contract =>
            contract["Value"].Converter = new ModifierOffsetIntConverter(200));
        var generated = GeneratedPath();

        var reflectedData = reflection.Serialize(new AttributeConverterDto { Value = 5 });
        var generatedData = generated.Serialize(new TwinInjectedConverterDto { Value = 5 });

        Assert.Equal(reflectedData, generatedData);

        var fromGenerated = reflection.Deserialize<AttributeConverterDto>(generatedData);
        Assert.Equal(5, fromGenerated.Value);

        var fromReflected = generated.Deserialize<TwinInjectedConverterDto>(reflectedData);
        Assert.Equal(5, fromReflected.Value);
    }

    [Fact]
    public void OrderModifierAndTwinProduceIdenticalBytes()
    {
        var reflection = ReflectionPath(static contract =>
        {
            contract["Third"].Order = -1;
            contract["Second"].Order = 10;
        });
        var generated = GeneratedPath();

        var reflectedData = reflection.Serialize(new OrderDto { First = 1, Second = 2, Third = 3 });
        var generatedData = generated.Serialize(new TwinOrderSequenceDto { First = 1, Second = 2, Third = 3 });

        Assert.Equal(reflectedData, generatedData);

        var fromGenerated = reflection.Deserialize<OrderDto>(generatedData);
        Assert.Equal(1, fromGenerated.First);
        Assert.Equal(2, fromGenerated.Second);
        Assert.Equal(3, fromGenerated.Third);

        var fromReflected = generated.Deserialize<TwinOrderSequenceDto>(reflectedData);
        Assert.Equal(1, fromReflected.First);
        Assert.Equal(2, fromReflected.Second);
        Assert.Equal(3, fromReflected.Third);
    }

    [Fact]
    public void NamingPolicyAndTwinProduceIdenticalBytes()
    {
        var reflection = SerializationTestPath.Reflection(new NbtSerializerOptions
        {
            TypeInfoResolver = new NbtReflectionTypeInfoResolver(),
            PropertyNamingPolicy = NbtNamingPolicy.CamelCase,
        });
        var generated = SerializationTestPath.Generated(ContractModifierCamelTwinContext.Default);

        var reflectedData = reflection.Serialize(new PolicyViewDto { UserName = "u", Value = 1 });
        var generatedData = generated.Serialize(new TwinPolicyViewDto { UserName = "u", Value = 1 });

        Assert.Equal(reflectedData, generatedData);

        var fromGenerated = reflection.Deserialize<PolicyViewDto>(generatedData);
        Assert.Equal("u", fromGenerated.UserName);
        Assert.Equal(1, fromGenerated.Value);

        var fromReflected = generated.Deserialize<TwinPolicyViewDto>(reflectedData);
        Assert.Equal("u", fromReflected.UserName);
        Assert.Equal(1, fromReflected.Value);
    }

    private static SerializationTestPath GeneratedPath() =>
        SerializationTestPath.Generated(ContractModifierTwinContext.Default);

    private static SerializationTestPath ReflectionPath(
        Action<NbtContractInfo> modifier,
        NbtNamingPolicy? policy = null) =>
        SerializationTestPath.Reflection(ModifierOptions(modifier, policy));

    private static NbtSerializerOptions ModifierOptions(
        Action<NbtContractInfo> modifier,
        NbtNamingPolicy? policy = null) => new()
        {
            TypeInfoResolver = new NbtReflectionTypeInfoResolver(modifier),
            PropertyNamingPolicy = policy,
        };

    private static NbtSerializerOptions ReflectionOptions() => new()
    {
        TypeInfoResolver = new NbtReflectionTypeInfoResolver(),
    };

    private static void AssertRenamed<T>(
        SerializationTestPath path,
        T value,
        Func<T, string> readName,
        Func<T, int> readHealth)
    {
        var data = path.Serialize(value);
        var compound = AsCompound(data);
        Assert.True(compound.ContainsKey("label"));
        Assert.False(compound.ContainsKey("Name"));

        var copy = path.Deserialize<T>(data);
        Assert.Equal("steve", readName(copy));
        Assert.Equal(0, readHealth(copy));
    }

    private static void AssertLegacyNameIsIgnored<T>(
        SerializationTestPath path,
        T value,
        Func<T, string> readName,
        Func<T, int> readHealth)
    {
        var legacy = NbtSerializer.Serialize(new NbtRoot("root", new NbtCompound
        {
            ["Name"] = "legacy",
            ["Health"] = 7,
        }));

        var copy = path.Deserialize<T>(legacy);

        Assert.Equal("", readName(copy));
        Assert.Equal(7, readHealth(copy));

        var roundTrip = path.Deserialize<T>(path.Serialize(value));
        Assert.Equal("steve", readName(roundTrip));
        Assert.Equal(3, readHealth(roundTrip));
    }

    private static void AssertPolicyNames<T>(
        SerializationTestPath path,
        T value,
        Func<T, string> readUserName,
        Func<T, int> readValue)
    {
        var data = path.Serialize(value);
        var compound = AsCompound(data);
        Assert.True(compound.ContainsKey("userName"));
        Assert.True(compound.ContainsKey("wire"));
        Assert.False(compound.ContainsKey("UserName"));
        Assert.False(compound.ContainsKey("Value"));

        var copy = path.Deserialize<T>(data);
        Assert.Equal("u", readUserName(copy));
        Assert.Equal(1, readValue(copy));
    }

    private static void AssertIgnoredHealth<T>(
        SerializationTestPath path,
        T value,
        Func<T, string> readName,
        Func<T, int> readHealth)
    {
        var data = path.Serialize(value);
        var compound = AsCompound(data);
        Assert.False(compound.ContainsKey("Health"));
        Assert.True(compound.ContainsKey("Name"));

        var roundTrip = path.Deserialize<T>(data);
        Assert.Equal("kept", readName(roundTrip));
        Assert.Equal(0, readHealth(roundTrip));

        var incoming = NbtSerializer.Serialize(new NbtRoot("root", new NbtCompound
        {
            ["Name"] = "kept",
            ["Health"] = 99,
        }));

        var copy = path.Deserialize<T>(incoming);
        Assert.Equal("kept", readName(copy));
        Assert.Equal(0, readHealth(copy));
    }

    private static void AssertEmptyName<T>(
        SerializationTestPath path,
        T value,
        Func<T, string> readName,
        Func<T, int> readHealth)
    {
        var data = path.Serialize(value);
        var compound = AsCompound(data);
        Assert.True(compound.ContainsKey(""));

        var copy = path.Deserialize<T>(data);
        Assert.Equal("anon", readName(copy));
        Assert.Equal(0, readHealth(copy));
    }

    private static void AssertOffsetConverter<T>(
        SerializationTestPath path,
        T value,
        Func<T, int> readValue,
        int expectedWire)
    {
        var data = path.Serialize(value);
        Assert.Equal(expectedWire, (int)AsCompound(data)["Value"].Value!);

        var copy = path.Deserialize<T>(data);
        Assert.Equal(5, readValue(copy));
    }

    private static void AssertPlainInt<T>(
        SerializationTestPath path,
        T value,
        Func<T, int> readValue)
    {
        var data = path.Serialize(value);
        Assert.Equal(5, (int)AsCompound(data)["Value"].Value!);

        var copy = path.Deserialize<T>(data);
        Assert.Equal(5, readValue(copy));
    }

    private static void AssertReorderedMembers<T>(
        SerializationTestPath path,
        T value,
        Func<T, int> readFirst,
        Func<T, int> readSecond,
        Func<T, int> readThird)
    {
        var data = path.Serialize(value);
        Assert.True(IndexOfNameBytes(data, "Third"u8) < IndexOfNameBytes(data, "First"u8));
        Assert.True(IndexOfNameBytes(data, "First"u8) < IndexOfNameBytes(data, "Second"u8));

        var copy = path.Deserialize<T>(data);
        Assert.Equal(1, readFirst(copy));
        Assert.Equal(2, readSecond(copy));
        Assert.Equal(3, readThird(copy));
    }

    private static void AssertDeclarationOrder<T>(
        SerializationTestPath path,
        T value,
        Func<T, int> readFirst,
        Func<T, int> readSecond,
        Func<T, int> readThird)
    {
        var data = path.Serialize(value);
        Assert.True(IndexOfNameBytes(data, "First"u8) < IndexOfNameBytes(data, "Second"u8));

        var copy = path.Deserialize<T>(data);
        Assert.Equal(1, readFirst(copy));
        Assert.Equal(2, readSecond(copy));
        Assert.Equal(3, readThird(copy));
    }

    private static void AssertOrderedRoundTrip<T>(
        SerializationTestPath path,
        T value,
        Func<T, int> readFirst,
        Func<T, int> readSecond,
        Func<T, int> readThird)
    {
        var data = path.Serialize(value);
        Assert.True(IndexOfNameBytes(data, "Third"u8) < IndexOfNameBytes(data, "First"u8));

        var copy = path.Deserialize<T>(data);
        Assert.Equal(1, readFirst(copy));
        Assert.Equal(2, readSecond(copy));
        Assert.Equal(3, readThird(copy));
    }

    private static void AssertPlainPolymorphicEnvelope<TBase, TDerived>(
        SerializationTestPath path,
        TBase value,
        Func<TDerived, string> readName,
        Func<TDerived, int> readValue)
        where TDerived : TBase
    {
        var data = path.Serialize(value);
        var envelope = AsCompound(data);
        Assert.Equal(typeof(TDerived).Name, (string)envelope["$type"].Value!);
        Assert.False(envelope.ContainsKey("Name"));

        var payload = (NbtCompound)envelope["$value"].Value!;
        Assert.True(payload.ContainsKey("Name"));
        Assert.True(payload.ContainsKey("Value"));
        Assert.False(payload.ContainsKey("v"));

        var copy = Assert.IsType<TDerived>(path.Deserialize<TBase>(data));
        Assert.Equal("d", readName(copy));
        Assert.Equal(4, readValue(copy));
    }

    private static void AssertRenamedPolymorphicPayload<TBase, TDerived>(
        SerializationTestPath path,
        TBase value,
        Func<TDerived, string> readName,
        Func<TDerived, int> readValue)
        where TDerived : TBase
    {
        var data = path.Serialize(value);
        var envelope = AsCompound(data);
        Assert.Equal(typeof(TDerived).Name, (string)envelope["$type"].Value!);

        var payload = (NbtCompound)envelope["$value"].Value!;
        Assert.True(payload.ContainsKey("Name"));
        Assert.True(payload.ContainsKey("v"));
        Assert.False(payload.ContainsKey("Value"));

        var copy = Assert.IsType<TDerived>(path.Deserialize<TBase>(data));
        Assert.Equal("d", readName(copy));
        Assert.Equal(4, readValue(copy));
    }

    private static void AssertHiddenChain<T>(
        SerializationTestPath path,
        T value,
        Func<T, int> readBaseValue,
        Func<T, long> readLeafValue)
    {
        var data = path.Serialize(value);
        var compound = AsCompound(data);
        Assert.Equal(2L, (long)compound["Value"].Value!);

        var copy = path.Deserialize<T>(data);
        Assert.Equal(0, readBaseValue(copy));
        Assert.Equal(2L, readLeafValue(copy));
    }

    private static void AssertMixedMemberOrder(SerializationTestPath path)
    {
        var value = new MixedMemberOrderDto
        {
            PropOne = 1,
            FieldTwo = 4,
            PropThree = 3,
        };

        var data = path.Serialize(value);
        Assert.True(IndexOfNameBytes(data, "FieldTwo"u8) < IndexOfNameBytes(data, "PropOne"u8));
        Assert.True(IndexOfNameBytes(data, "PropOne"u8) < IndexOfNameBytes(data, "PropThree"u8));

        var copy = path.Deserialize<MixedMemberOrderDto>(data);
        Assert.Equal(1, copy.PropOne);
        Assert.Equal(4, copy.FieldTwo);
        Assert.Equal(3, copy.PropThree);
    }

    private static void AssertResolvedCollision<T>(
        SerializationTestPath path,
        T value,
        Func<T, int> readA,
        Func<T, int> readB)
    {
        var data = path.Serialize(value);
        var compound = AsCompound(data);
        Assert.True(compound.ContainsKey("x"));
        Assert.True(compound.ContainsKey("b"));

        var copy = path.Deserialize<T>(data);
        Assert.Equal(1, readA(copy));
        Assert.Equal(2, readB(copy));
    }

    private static NbtCompound AsCompound(byte[] data) =>
        (NbtCompound)NbtSerializer.Deserialize(data).Value.Value!;

    private static int IndexOfNameBytes(byte[] data, ReadOnlySpan<byte> needle) =>
        data.AsSpan().IndexOf(needle);

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

    private sealed class OrderDto
    {
        public int First { get; set; }

        public int Second { get; set; }

        public int Third { get; set; }
    }

    private sealed class AttributeConverterDto
    {
        [NbtUseConverter(typeof(ModifierOffsetIntConverter))]
        public int Value { get; set; }
    }

    internal sealed class PlainIntDto
    {
        public int Value { get; set; }
    }

    private sealed class NullConverterTargetDto
    {
        public int Value { get; set; }
    }

    [NbtDerived(typeof(ModifierPolyDerived))]
    [NbtDerived(typeof(ModifierPlainPolyDerived))]
    private abstract class ModifierPolyBase
    {
        public string Name { get; set; } = "";
    }

    private sealed class ModifierPolyDerived : ModifierPolyBase
    {
        public int Value { get; set; }
    }

    private sealed class ModifierPlainPolyDerived : ModifierPolyBase
    {
        public int Value { get; set; }
    }

    private class ChainHiddenBase
    {
        public virtual int Value { get; set; }
    }

    private sealed class ChainHiddenLeaf : ChainHiddenBase
    {
        public new long Value { get; set; }
    }

    private sealed class AttributeCollisionDto
    {
        [NbtPropertyName("x")]
        public int A { get; set; }

        [NbtPropertyName("x")]
        public int B { get; set; }
    }

    internal sealed class MixedMemberOrderDto
    {
        public int PropOne { get; set; }

        [NbtInclude]
        public int FieldTwo = 2;

        public int PropThree { get; set; }
    }

    internal sealed class TwinRenameDto
    {
        [NbtPropertyName("label")]
        public string Name { get; set; } = "";

        public int Health { get; set; }
    }

    internal sealed class TwinPolicyViewDto
    {
        public string UserName { get; set; } = "";

        [NbtPropertyName("wire")]
        public int Value { get; set; }
    }

    internal sealed class TwinIgnoredHealthDto
    {
        public string Name { get; set; } = "";

        [NbtIgnore]
        public int Health { get; set; }
    }

    internal sealed class TwinEmptyNameDto
    {
        [NbtPropertyName("")]
        public string Name { get; set; } = "";

        public int Health { get; set; }
    }

    internal sealed class TwinInjectedConverterDto
    {
        [NbtUseConverter(typeof(ModifierOffset200IntConverter))]
        public int Value { get; set; }
    }

    internal sealed class TwinFactoryOffsetDto
    {
        [NbtUseConverter(typeof(ModifierOffsetIntConverter))]
        public int Value { get; set; }
    }

    internal sealed class TwinOrderSequenceDto
    {
        public int Third { get; set; }

        public int First { get; set; }

        public int Second { get; set; }
    }

    internal sealed class TwinDeclarationOrderDto
    {
        public int Third { get; set; }

        public int First { get; set; }

        public int Second { get; set; }
    }

    [NbtDerived(typeof(TwinPolyDerived))]
    [NbtDerived(typeof(TwinPlainPolyDerived))]
    internal abstract class TwinPolyBase
    {
        public string Name { get; set; } = "";
    }

    internal sealed class TwinPolyDerived : TwinPolyBase
    {
        [NbtPropertyName("v")]
        public int Value { get; set; }
    }

    internal sealed class TwinPlainPolyDerived : TwinPolyBase
    {
        public int Value { get; set; }
    }

    internal class TwinHiddenBase
    {
        [NbtIgnore]
        public virtual int Value { get; set; }
    }

    internal sealed class TwinHiddenLeaf : TwinHiddenBase
    {
        public new long Value { get; set; }
    }

    internal sealed class TwinResolvedCollisionDto
    {
        [NbtPropertyName("x")]
        public int A { get; set; }

        [NbtPropertyName("b")]
        public int B { get; set; }
    }
}

[NbtSerializable(typeof(NbtContractModifierTests.TwinRenameDto))]
[NbtSerializable(typeof(NbtContractModifierTests.TwinIgnoredHealthDto))]
[NbtSerializable(typeof(NbtContractModifierTests.TwinEmptyNameDto))]
[NbtSerializable(typeof(NbtContractModifierTests.TwinInjectedConverterDto))]
[NbtSerializable(typeof(NbtContractModifierTests.TwinFactoryOffsetDto))]
[NbtSerializable(typeof(NbtContractModifierTests.TwinOrderSequenceDto))]
[NbtSerializable(typeof(NbtContractModifierTests.TwinDeclarationOrderDto))]
[NbtSerializable(typeof(NbtContractModifierTests.TwinResolvedCollisionDto))]
[NbtSerializable(typeof(NbtContractModifierTests.TwinPolyBase))]
[NbtSerializable(typeof(NbtContractModifierTests.TwinHiddenLeaf))]
[NbtSerializable(typeof(NbtContractModifierTests.PlainIntDto))]
[NbtSerializable(typeof(NbtContractModifierTests.MixedMemberOrderDto))]
internal partial class ContractModifierTwinContext : NbtSerializerContext
{
}

[NbtSerializable(typeof(NbtContractModifierTests.TwinPolicyViewDto))]
[NbtSourceGenerationOptions(NamingPolicy = NbtNamingPolicyKind.CamelCase)]
internal partial class ContractModifierCamelTwinContext : NbtSerializerContext
{
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

internal sealed class ModifierOffset200IntConverter : NbtConverter<int>
{
    public override int Read(ref NbtReader reader, NbtSerializerOptions options) =>
        reader.GetInt32() - 200;

    public override void Write(ref NbtWriter writer, int value, NbtSerializerOptions options) =>
        writer.WriteInt32(value + 200);

    public override bool TryGetTagType(
        int value,
        NbtSerializerOptions options,
        out NbtTagType tagType)
    {
        tagType = NbtTagType.Int;
        return true;
    }
}

internal sealed class ModifierIntFactory : NbtConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(int);

    public override NbtConverter CreateConverter(Type typeToConvert, NbtSerializerOptions options) =>
        new ModifierOffsetIntConverter(10);
}

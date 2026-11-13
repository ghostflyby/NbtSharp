namespace NbtSharp.Tests;

public sealed class NbtConverterTests
{
    [Fact]
    public void RoundTripsPrimitiveValues()
    {
        Assert.Equal((sbyte)-8, RoundTrip((sbyte)-8));
        Assert.Equal((byte)250, RoundTrip((byte)250));
        Assert.Equal((short)-300, RoundTrip((short)-300));
        Assert.Equal(-70000, RoundTrip(-70000));
        Assert.Equal(-5_000_000_000L, RoundTrip(-5_000_000_000L));
        Assert.Equal(0.25f, RoundTrip(0.25f));
        Assert.Equal(0.125, RoundTrip(0.125));
        Assert.True(RoundTrip(true));
        Assert.False(RoundTrip(false));
        Assert.Equal("hello", RoundTrip("hello"));
    }

    [Fact]
    public void UnsupportedScalarsRequireAConverter()
    {
        Assert.Throws<NotSupportedException>(() => NbtSerializer.Serialize((ushort)1, "root"));
        Assert.Throws<NotSupportedException>(() => NbtSerializer.Serialize(1.5m, "root"));
        Assert.Throws<NotSupportedException>(() => NbtSerializer.Serialize(Guid.Empty, "root"));
    }

    [Fact]
    public void BooleanReaderRejectsValuesOtherThanZeroAndOne()
    {
        var root = new NbtRoot("root", (sbyte)2);
        var data = NbtSerializer.Serialize(root);

        Assert.Throws<NbtException>(() => NbtSerializer.Deserialize<bool>(data));
    }

    [Fact]
    public void ArraysAndListsUseDifferentTags()
    {
        var intArray = NbtSerializer.Deserialize(NbtSerializer.Serialize(new[] { 1, 2 }, "root"));
        Assert.Equal(NbtTagType.IntArray, intArray.Value.TagType);

        var longArray = NbtSerializer.Deserialize(NbtSerializer.Serialize(new[] { 1L, 2L }, "root"));
        Assert.Equal(NbtTagType.LongArray, longArray.Value.TagType);

        var intList = NbtSerializer.Deserialize(NbtSerializer.Serialize(new List<int> { 1, 2 }, "root"));
        Assert.Equal(NbtTagType.List, intList.Value.TagType);
        Assert.Equal(NbtTagType.Int, ((NbtList)intList.Value.Value!).ElementType);
    }

    [Fact]
    public void ByteArraysRoundTripWithUnsignedAndSignedViews()
    {
        var unsigned = new byte[] { 0, 1, 255 };
        Assert.Equal(unsigned, RoundTrip(unsigned));

        var signed = new sbyte[] { -1, 0, 1 };
        Assert.Equal(signed, RoundTrip(signed));

        Assert.Equal(NbtTagType.ByteArray, NbtSerializer.Deserialize(NbtSerializer.Serialize(unsigned, "root")).Value.TagType);
        Assert.Equal(NbtTagType.ByteArray, NbtSerializer.Deserialize(NbtSerializer.Serialize(signed, "root")).Value.TagType);
    }

    [Fact]
    public void EmptyTypedCollectionWritesAnEndList()
    {
        var data = NbtSerializer.Serialize(new List<int>(), "root");
        var root = NbtSerializer.Deserialize(data);

        Assert.True(root.Value is NbtList);
        var list = (NbtList)root.Value.Value!;
        Assert.Equal(NbtTagType.End, list.ElementType);
        Assert.Equal(0, list.Count);
        Assert.Empty(NbtSerializer.Deserialize<List<int>>(data));
    }

    [Fact]
    public void EmptyTypedListMustMatchTheRequestedElementType()
    {
        var root = new NbtRoot("root", new NbtList(new List<int>()));
        var data = NbtSerializer.Serialize(root);

        Assert.Throws<NbtException>(() => NbtSerializer.Deserialize<List<string>>(data));
        Assert.Empty(NbtSerializer.Deserialize<List<int>>(data));
    }

    [Fact]
    public void MultidimensionalArraysAreNotSupported()
    {
        Assert.Throws<NotSupportedException>(() => NbtSerializer.Serialize(new int[1, 1], "root"));
        Assert.Throws<NotSupportedException>(() =>
            NbtSerializer.Deserialize<int[,]>(NbtSerializer.Serialize(1, "root")));
    }

    [Fact]
    public void EmptyIntArrayStaysAnIntArray()
    {
        var data = NbtSerializer.Serialize(Array.Empty<int>(), "root");
        var root = NbtSerializer.Deserialize(data);

        Assert.Equal(NbtTagType.IntArray, root.Value.TagType);
        Assert.Empty(NbtSerializer.Deserialize<int[]>(data));
    }

    [Fact]
    public void GenericCollectionsAndDictionaryInterfacesRoundTrip()
    {
        IList<int> list = new List<int> { 1, 2, 3 };
        Assert.Equal(new[] { 1, 2, 3 }, RoundTrip(list));

        IEnumerable<string> sequence = new[] { "a", "b" };
        Assert.Equal(new[] { "a", "b" }, RoundTrip(sequence));

        IDictionary<string, int> dictionary = new Dictionary<string, int> { ["A"] = 1, ["B"] = 2 };
        var actual = RoundTrip(dictionary);
        Assert.Equal(1, actual["A"]);
        Assert.Equal(2, actual["B"]);
    }

    [Fact]
    public void CustomConverterOverridesBuiltInConverter()
    {
        var options = new NbtSerializerOptions();
        options.Converters.Add(new IntAsShortConverter());

        var data = NbtSerializer.Serialize(7, "root", options);
        var root = NbtSerializer.Deserialize(data);
        Assert.Equal(NbtTagType.Short, root.Value.TagType);
        Assert.Equal(7, NbtSerializer.Deserialize<int>(data, options));

        var listData = NbtSerializer.Serialize(new List<int> { 1, 2 }, "root", options);
        var listRoot = NbtSerializer.Deserialize(listData);
        Assert.Equal(NbtTagType.Short, ((NbtList)listRoot.Value.Value!).ElementType);
        Assert.Equal(new[] { 1, 2 }, NbtSerializer.Deserialize<List<int>>(listData, options));
    }

    [Fact]
    public void ConverterFactoryCreatesConverters()
    {
        var options = new NbtSerializerOptions();
        options.Converters.Add(new IntFactory());

        var data = NbtSerializer.Serialize(7, "root", options);
        Assert.Equal(NbtTagType.Short, NbtSerializer.Deserialize(data).Value.TagType);
        Assert.Equal(7, NbtSerializer.Deserialize<int>(data, options));
    }

    [Fact]
    public void PredictedDynamicTagsMustBeHomogeneousInAList()
    {
        var options = new NbtSerializerOptions();
        options.Converters.Add(new CompactIntConverter());

        var data = NbtSerializer.Serialize(new List<int> { 1, 2, 3 }, "root", options);
        var root = NbtSerializer.Deserialize(data);
        Assert.Equal(NbtTagType.Byte, ((NbtList)root.Value.Value!).ElementType);
        Assert.Equal(new[] { 1, 2, 3 }, NbtSerializer.Deserialize<List<int>>(data, options));

        Assert.Throws<NbtException>(() =>
            NbtSerializer.Serialize(new List<int> { 1, 300 }, "root", options));
    }

    [Fact]
    public void UnpredictedDynamicTagsAreRejected()
    {
        var options = new NbtSerializerOptions();
        options.Converters.Add(new CompactIntWithoutPredictionConverter());

        Assert.Throws<NotSupportedException>(() =>
            NbtSerializer.Serialize(new List<int> { 1, 2, 3 }, "root", options));
        Assert.Throws<NotSupportedException>(() =>
            NbtSerializer.Serialize(1, "root", options));
    }

    [Fact]
    public void MembersRequireTagTypePrediction()
    {
        var options = new NbtSerializerOptions
        {
            TypeInfoResolver = new NbtReflectionTypeInfoResolver(),
        };
        options.Converters.Add(new CompactIntWithoutPredictionConverter());

        Assert.Throws<NotSupportedException>(() =>
            NbtSerializer.Serialize(new IntHolder { Value = 1 }, "root", options));
    }

    [Fact]
    public void CustomCompoundConverterRoundTrips()
    {
        var options = new NbtSerializerOptions();
        options.Converters.Add(new PointConverter());

        var point = new Point { X = 3, Y = 4 };
        var data = NbtSerializer.Serialize(point, "root", options);
        var root = NbtSerializer.Deserialize(data);

        Assert.Equal(NbtTagType.Compound, root.Value.TagType);
        var actual = NbtSerializer.Deserialize<Point>(data, options);
        Assert.Equal(3, actual.X);
        Assert.Equal(4, actual.Y);
    }

    [Fact]
    public void RecursiveConvertersCanUseThePublicValueApi()
    {
        var options = new NbtSerializerOptions();
        options.Converters.Add(new EnvelopeConverter());
        var value = new Envelope { Value = 42 };

        var data = NbtSerializer.Serialize(value, "root", options);
        var actual = NbtSerializer.Deserialize<Envelope>(data, options);

        Assert.Equal(42, actual.Value);
    }

    [Fact]
    public void ConverterListChangesInvalidateTheResolutionCache()
    {
        var options = new NbtSerializerOptions();
        var first = NbtSerializer.Serialize(7, "root", options);
        Assert.Equal(NbtTagType.Int, NbtSerializer.Deserialize(first).Value.TagType);

        options.Converters.Add(new IntAsShortConverter());
        var second = NbtSerializer.Serialize(7, "root", options);
        Assert.Equal(NbtTagType.Short, NbtSerializer.Deserialize(second).Value.TagType);
    }

    [Fact]
    public void ConvertersThatReadExtraValuesAreRejected()
    {
        var options = new NbtSerializerOptions();
        options.Converters.Add(new OverReadingIntConverter());
        var data = NbtSerializer.Serialize(new List<int> { 1, 2 }, "root");

        var exception = Assert.Throws<NbtException>(() =>
            NbtSerializer.Deserialize<List<int>>(data, options));

        Assert.Equal("$[0]", exception.Path);
    }

    [Fact]
    public void TypeInfoExposesTheTypedConverter()
    {
        var converter = new IntAsShortConverter();
        var typeInfo = new NbtTypeInfo<int>(converter);

        Assert.Equal(typeof(int), typeInfo.Type);
        Assert.Same(converter, typeInfo.Converter);
        Assert.Same(converter, typeInfo.TypedConverter);
    }

    [Fact]
    public void NullRootAndNullListElementThrow()
    {
        string? nullValue = null;
        Assert.Throws<NbtException>(() => NbtSerializer.Serialize(nullValue, "root"));
        Assert.Throws<NbtException>(() =>
            NbtSerializer.Serialize(new List<string?> { "a", null }, "root"));
    }

    [Fact]
    public void NullDictionaryMemberIsOmittedByDefault()
    {
        var value = new Dictionary<string, string?> { ["Present"] = "yes", ["Missing"] = null };
        var data = NbtSerializer.Serialize(value, "root");
        var root = (NbtCompound)NbtSerializer.Deserialize(data).Value.Value!;

        Assert.True(root.ContainsKey("Present"));
        Assert.False(root.ContainsKey("Missing"));

        var actual = NbtSerializer.Deserialize<Dictionary<string, string?>>(data);
        Assert.Equal("yes", actual["Present"]);
        Assert.False(actual.ContainsKey("Missing"));
    }

    [Fact]
    public void NullDictionaryMemberCanBeRejected()
    {
        var options = new NbtSerializerOptions { NullValueHandling = NbtNullValueHandling.Throw };
        var value = new Dictionary<string, string?> { ["Missing"] = null };

        Assert.Throws<NbtException>(() => NbtSerializer.Serialize(value, "root", options));
    }

    private static T RoundTrip<T>(T value, NbtSerializerOptions? options = null)
    {
        var data = NbtSerializer.Serialize(value, "root", options);
        return NbtSerializer.Deserialize<T>(data, options);
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

    private sealed class IntFactory : NbtConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(int);

        public override NbtConverter CreateConverter(Type typeToConvert, NbtSerializerOptions options) =>
            new IntAsShortConverter();
    }

    private sealed class CompactIntConverter : NbtConverter<int>
    {
        public override int Read(ref NbtReader reader, NbtSerializerOptions options) => reader.TagType switch
        {
            NbtTagType.Byte => reader.GetSByte(),
            NbtTagType.Short => reader.GetInt16(),
            _ => reader.GetInt32(),
        };

        public override void Write(ref NbtWriter writer, int value, NbtSerializerOptions options)
        {
            if (value is >= sbyte.MinValue and <= sbyte.MaxValue)
            {
                writer.WriteSByte((sbyte)value);
            }
            else if (value is >= short.MinValue and <= short.MaxValue)
            {
                writer.WriteInt16((short)value);
            }
            else
            {
                writer.WriteInt32(value);
            }
        }

        public override bool TryGetTagType(int value, NbtSerializerOptions options, out NbtTagType tagType)
        {
            tagType = value is >= sbyte.MinValue and <= sbyte.MaxValue
                ? NbtTagType.Byte
                : value is >= short.MinValue and <= short.MaxValue
                    ? NbtTagType.Short
                    : NbtTagType.Int;
            return true;
        }
    }

    private sealed class CompactIntWithoutPredictionConverter : NbtConverter<int>
    {
        public override int Read(ref NbtReader reader, NbtSerializerOptions options) => reader.TagType switch
        {
            NbtTagType.Byte => reader.GetSByte(),
            NbtTagType.Short => reader.GetInt16(),
            _ => reader.GetInt32(),
        };

        public override void Write(ref NbtWriter writer, int value, NbtSerializerOptions options)
        {
            if (value is >= sbyte.MinValue and <= sbyte.MaxValue)
            {
                writer.WriteSByte((sbyte)value);
            }
            else if (value is >= short.MinValue and <= short.MaxValue)
            {
                writer.WriteInt16((short)value);
            }
            else
            {
                writer.WriteInt32(value);
            }
        }
    }

    private sealed class OverReadingIntConverter : NbtConverter<int>
    {
        public override int Read(ref NbtReader reader, NbtSerializerOptions options)
        {
            var value = reader.GetInt32();
            reader.Read();
            _ = reader.GetInt32();
            return value;
        }

        public override void Write(ref NbtWriter writer, int value, NbtSerializerOptions options) =>
            writer.WriteInt32(value);

        public override bool TryGetTagType(int value, NbtSerializerOptions options, out NbtTagType tagType)
        {
            tagType = NbtTagType.Int;
            return true;
        }
    }

    private sealed class IntHolder
    {
        public int Value { get; set; }
    }

    private sealed class Envelope
    {
        public int Value { get; set; }
    }

    private sealed class EnvelopeConverter : NbtConverter<Envelope>
    {
        public override Envelope Read(ref NbtReader reader, NbtSerializerOptions options)
        {
            if (reader.TokenType != NbtTokenType.StartCompound)
            {
                throw new NbtException("Expected TAG_Compound.");
            }

            var envelope = new Envelope();
            while (reader.Read())
            {
                if (reader.TokenType == NbtTokenType.EndCompound)
                {
                    return envelope;
                }

                switch (reader.Name)
                {
                    case "Value":
                        envelope.Value = NbtSerializer.DeserializeValue<int>(ref reader, options);
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            throw new NbtException("The compound was not terminated.");
        }

        public override void Write(ref NbtWriter writer, Envelope value, NbtSerializerOptions options)
        {
            writer.WriteStartCompound();
            NbtSerializer.SerializeMember(ref writer, "Value", value.Value, options);
            writer.WriteEndCompound();
        }

        public override bool TryGetTagType(Envelope value, NbtSerializerOptions options, out NbtTagType tagType)
        {
            tagType = NbtTagType.Compound;
            return true;
        }
    }

    private sealed class Point
    {
        public int X { get; set; }

        public int Y { get; set; }
    }

    private sealed class PointConverter : NbtConverter<Point>
    {
        public override Point Read(ref NbtReader reader, NbtSerializerOptions options)
        {
            if (reader.TokenType != NbtTokenType.StartCompound)
            {
                throw new NbtException("Expected TAG_Compound.");
            }

            var point = new Point();
            while (reader.Read())
            {
                if (reader.TokenType == NbtTokenType.EndCompound)
                {
                    return point;
                }

                switch (reader.Name)
                {
                    case "X":
                        point.X = reader.GetInt32();
                        break;
                    case "Y":
                        point.Y = reader.GetInt32();
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            throw new NbtException("The compound was not terminated.");
        }

        public override void Write(ref NbtWriter writer, Point value, NbtSerializerOptions options)
        {
            writer.WriteStartCompound();
            writer.WriteInt32("X", value.X);
            writer.WriteInt32("Y", value.Y);
            writer.WriteEndCompound();
        }

        public override bool TryGetTagType(Point value, NbtSerializerOptions options, out NbtTagType tagType)
        {
            tagType = NbtTagType.Compound;
            return true;
        }
    }
}

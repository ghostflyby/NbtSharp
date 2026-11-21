namespace NbtSharp.Tests;

public sealed class NbtConverterPrecedenceMatrixTests
{
    [Fact]
    public void FirstRegisteredOptionsConverterWins()
    {
        var options = new NbtSerializerOptions();
        options.Converters.Add(new OffsetIntConverter(100));
        options.Converters.Add(new OffsetIntConverter(200));

        var data = NbtSerializer.Serialize(1, "root", options);

        Assert.Equal(101, (int)NbtSerializer.Deserialize(data).Value.Value!);
        Assert.Equal(1, NbtSerializer.Deserialize<int>(data, options));
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void ExplicitMemberConverterOverridesTheBuiltInScalarPath(SerializationMode mode)
    {
        var value = new ExplicitPrecedenceDto { Value = 5 };
        var path = mode == SerializationMode.Reflection
            ? SerializationTestPath.Reflection(new NbtSerializerOptions
            {
                TypeInfoResolver = new NbtReflectionTypeInfoResolver(),
            })
            : SerializationTestPath.Generated(ExplicitPrecedenceContext.Default);

        var data = path.Serialize(value);
        var root = (NbtCompound)NbtSerializer.Deserialize(data).Value.Value!;
        var copy = path.Deserialize<ExplicitPrecedenceDto>(data);

        Assert.Equal(105, (int)root["Value"].Value!);
        Assert.Equal(5, copy.Value);
    }
}

internal sealed class OffsetIntConverter : NbtConverter<int>
{
    private readonly int _offset;

    public OffsetIntConverter()
        : this(100)
    {
    }

    public OffsetIntConverter(int offset) => _offset = offset;

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

internal sealed class ExplicitPrecedenceDto
{
    [NbtUseConverter(typeof(OffsetIntConverter))]
    public int Value { get; set; }
}

[NbtSerializable(typeof(ExplicitPrecedenceDto))]
internal partial class ExplicitPrecedenceContext : NbtSerializerContext
{
}

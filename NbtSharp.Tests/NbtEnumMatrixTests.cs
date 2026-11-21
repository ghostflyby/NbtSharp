namespace NbtSharp.Tests;

public sealed class NbtEnumMatrixTests
{
    [Fact]
    public void FlagsAndUndefinedValuesRoundTripThroughBothPaths()
    {
        var value = new EnumMatrixDto
        {
            Flags = MatrixFlags.Alpha | MatrixFlags.Gamma,
            Optional = MatrixFlags.Beta,
            Undefined = (MatrixByteEnum)200,
            Values = [MatrixByteEnum.One, (MatrixByteEnum)255],
            Map = new Dictionary<string, MatrixByteEnum>
            {
                ["defined"] = MatrixByteEnum.One,
                ["undefined"] = (MatrixByteEnum)200,
            },
        };
        var reflection = new NbtSerializerOptions
        {
            TypeInfoResolver = new NbtReflectionTypeInfoResolver(),
        };

        var generatedData = NbtSerializer.Serialize(
            value,
            EnumMatrixContext.Default.EnumMatrixDto,
            "root");
        var reflectedData = NbtSerializer.Serialize(value, "root", reflection);

        Assert.Equal(reflectedData, generatedData);

        var generatedCopy = NbtSerializer.Deserialize(
            generatedData,
            EnumMatrixContext.Default.EnumMatrixDto);
        var reflectedCopy = NbtSerializer.Deserialize<EnumMatrixDto>(reflectedData, reflection);

        Assert.Equal(value.Flags, generatedCopy.Flags);
        Assert.Equal(value.Optional, generatedCopy.Optional);
        Assert.Equal(value.Undefined, generatedCopy.Undefined);
        Assert.Equal(value.Values, generatedCopy.Values);
        Assert.Equal(value.Map, generatedCopy.Map);
        Assert.Equal(value.Flags, reflectedCopy.Flags);
        Assert.Equal(value.Optional, reflectedCopy.Optional);
        Assert.Equal(value.Undefined, reflectedCopy.Undefined);
        Assert.Equal(value.Values, reflectedCopy.Values);
        Assert.Equal(value.Map, reflectedCopy.Map);
    }

    [Fact]
    public void UndefinedRootEnumValueRoundTrips()
    {
        var value = (MatrixByteEnum)200;
        var reflection = new NbtSerializerOptions
        {
            TypeInfoResolver = new NbtReflectionTypeInfoResolver(),
        };

        var generatedData = NbtSerializer.Serialize(
            value,
            EnumMatrixContext.Default.MatrixByteEnum,
            "root");
        var reflectedData = NbtSerializer.Serialize(value, "root", reflection);

        Assert.Equal(reflectedData, generatedData);
        Assert.Equal(
            value,
            NbtSerializer.Deserialize(generatedData, EnumMatrixContext.Default.MatrixByteEnum));
        Assert.Equal(value, NbtSerializer.Deserialize<MatrixByteEnum>(reflectedData, reflection));
    }
}

[Flags]
internal enum MatrixFlags : ushort
{
    None = 0,
    Alpha = 1,
    Beta = 0x0100,
    Gamma = 0x8000,
}

internal enum MatrixByteEnum : byte
{
    Zero = 0,
    One = 1,
}

internal sealed class EnumMatrixDto
{
    public MatrixFlags Flags { get; set; }

    public MatrixFlags? Optional { get; set; }

    public MatrixByteEnum Undefined { get; set; }

    public List<MatrixByteEnum> Values { get; set; } = [];

    public Dictionary<string, MatrixByteEnum> Map { get; set; } = [];
}

[NbtSerializable(typeof(EnumMatrixDto))]
[NbtSerializable(typeof(MatrixByteEnum))]
internal partial class EnumMatrixContext : NbtSerializerContext
{
}

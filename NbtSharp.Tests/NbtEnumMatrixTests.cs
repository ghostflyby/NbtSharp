namespace NbtSharp.Tests;

public sealed class NbtEnumMatrixTests
{
    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void FlagsAndUndefinedValuesRoundTrip(SerializationMode mode)
    {
        var path = CreatePath(mode);
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

        var data = path.Serialize(value);
        var copy = path.Deserialize<EnumMatrixDto>(data);

        Assert.Equal(value.Flags, copy.Flags);
        Assert.Equal(value.Optional, copy.Optional);
        Assert.Equal(value.Undefined, copy.Undefined);
        Assert.Equal(value.Values, copy.Values);
        Assert.Equal(value.Map, copy.Map);
    }

    [Theory]
    [InlineData(SerializationMode.Generated)]
    [InlineData(SerializationMode.Reflection)]
    public void UndefinedRootEnumValueRoundTrips(SerializationMode mode)
    {
        var path = CreatePath(mode);
        var value = (MatrixByteEnum)200;

        var data = path.Serialize(value);

        Assert.Equal(value, path.Deserialize<MatrixByteEnum>(data));
    }

    [Fact]
    public void BothPathsProduceTheSameBytes()
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

        Assert.Equal(
            CreatePath(SerializationMode.Reflection).Serialize(value),
            CreatePath(SerializationMode.Generated).Serialize(value));
    }

    private static SerializationTestPath CreatePath(SerializationMode mode) =>
        mode == SerializationMode.Reflection
            ? SerializationTestPath.Reflection(new NbtSerializerOptions
            {
                TypeInfoResolver = new NbtReflectionTypeInfoResolver(),
            })
            : SerializationTestPath.Generated(EnumMatrixContext.Default);
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

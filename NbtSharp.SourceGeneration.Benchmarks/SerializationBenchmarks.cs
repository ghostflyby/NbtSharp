using BenchmarkDotNet.Attributes;

namespace NbtSharp.SourceGeneration.Benchmarks;

[MemoryDiagnoser]
public class SerializationBenchmarks
{
    private ScalarDto _scalars = null!;
    private byte[] _scalarData = null!;
    private NullableScalarDto _nullableScalars = null!;
    private byte[] _nullableScalarData = null!;
    private ArrayDto _arrays = null!;
    private byte[] _arrayData = null!;
    private ArrayAllocationDto _allocationArrays = null!;
    private byte[] _allocationArrayData = null!;

    [GlobalSetup]
    public void Setup()
    {
        _scalars = new ScalarDto
        {
            SByte = -12,
            Byte = 250,
            Int16 = -32000,
            Int32 = -123456,
            Int64 = long.MinValue + 1,
            Single = 1.25f,
            Double = -2.5,
            Flag = true,
            OptionalFlag = false,
            Text = "a scalar-heavy DTO",
            OptionalText = "optional",
            OptionalNumber = 42,
        };
        _scalarData = NbtSerializer.Serialize(_scalars, SerializationContext.Default.ScalarDto, "root");

        _nullableScalars = new NullableScalarDto
        {
            Int0 = 0,
            Int1 = 1,
            Int2 = 2,
            Int3 = 3,
            Int4 = 4,
            Int5 = 5,
            Int6 = 6,
            Int7 = 7,
            Bool0 = true,
            Bool1 = false,
            Bool2 = true,
            Bool3 = false,
            Bool4 = true,
            Bool5 = false,
            Bool6 = true,
            Bool7 = false,
        };
        _nullableScalarData = NbtSerializer.Serialize(
            _nullableScalars,
            SerializationContext.Default.NullableScalarDto,
            "root");

        _arrays = new ArrayDto
        {
            Labels = Enumerable.Range(0, 64).Select(static index => "label" + index).ToArray(),
            Items = Enumerable.Range(0, 64)
                .Select(static index => new ArrayItem { Id = index, Name = "item" + index })
                .ToArray(),
            Bytes = Enumerable.Range(0, 256).Select(static value => (sbyte)value).ToArray(),
            Names = Enumerable.Range(0, 64).Select(static index => "name" + index).ToList(),
            Numbers = Enumerable.Range(0, 256).ToArray(),
        };
        _arrayData = NbtSerializer.Serialize(_arrays, SerializationContext.Default.ArrayDto, "root");

        _allocationArrays = new ArrayAllocationDto
        {
            Values = Enumerable.Range(0, 1024)
                .Select(static index => new ArrayValue { Id = index, Value = index * 2L })
                .ToArray(),
            Bytes = Enumerable.Range(0, 1024).Select(static value => (sbyte)value).ToArray(),
        };
        _allocationArrayData = NbtSerializer.Serialize(
            _allocationArrays,
            SerializationContext.Default.ArrayAllocationDto,
            "root");
    }

    [Benchmark(Baseline = true)]
    public byte[] ScalarSerialize() =>
        NbtSerializer.Serialize(_scalars, SerializationContext.Default.ScalarDto, "root");

    [Benchmark]
    public ScalarDto ScalarDeserialize() =>
        NbtSerializer.Deserialize(_scalarData, SerializationContext.Default.ScalarDto);

    [Benchmark]
    public byte[] NullableScalarSerialize() =>
        NbtSerializer.Serialize(
            _nullableScalars,
            SerializationContext.Default.NullableScalarDto,
            "root");

    [Benchmark]
    public NullableScalarDto NullableScalarDeserialize() =>
        NbtSerializer.Deserialize(_nullableScalarData, SerializationContext.Default.NullableScalarDto);

    [Benchmark]
    public byte[] ArraySerialize() =>
        NbtSerializer.Serialize(_arrays, SerializationContext.Default.ArrayDto, "root");

    [Benchmark]
    public ArrayDto ArrayDeserialize() =>
        NbtSerializer.Deserialize(_arrayData, SerializationContext.Default.ArrayDto);

    [Benchmark]
    public ArrayAllocationDto ArrayAllocationDeserialize() =>
        NbtSerializer.Deserialize(
            _allocationArrayData,
            SerializationContext.Default.ArrayAllocationDto);
}

[NbtSerializable(typeof(ScalarDto))]
[NbtSerializable(typeof(NullableScalarDto))]
[NbtSerializable(typeof(ArrayDto))]
[NbtSerializable(typeof(ArrayAllocationDto))]
internal partial class SerializationContext : NbtSerializerContext
{
}

public sealed class ScalarDto
{
    public sbyte SByte { get; set; }

    public byte Byte { get; set; }

    public short Int16 { get; set; }

    public int Int32 { get; set; }

    public long Int64 { get; set; }

    public float Single { get; set; }

    public double Double { get; set; }

    public bool Flag { get; set; }

    public bool? OptionalFlag { get; set; }

    public string Text { get; set; } = "";

    public string? OptionalText { get; set; }

    public int? OptionalNumber { get; set; }
}

public sealed class ArrayDto
{
    public string[] Labels { get; set; } = [];

    public ArrayItem[] Items { get; set; } = [];

    public sbyte[] Bytes { get; set; } = [];

    public List<string> Names { get; set; } = [];

    public int[] Numbers { get; set; } = [];
}

public sealed class ArrayItem
{
    public int Id { get; set; }

    public string Name { get; set; } = "";
}

public sealed class NullableScalarDto
{
    public int? Int0 { get; set; }

    public int? Int1 { get; set; }

    public int? Int2 { get; set; }

    public int? Int3 { get; set; }

    public int? Int4 { get; set; }

    public int? Int5 { get; set; }

    public int? Int6 { get; set; }

    public int? Int7 { get; set; }

    public bool? Bool0 { get; set; }

    public bool? Bool1 { get; set; }

    public bool? Bool2 { get; set; }

    public bool? Bool3 { get; set; }

    public bool? Bool4 { get; set; }

    public bool? Bool5 { get; set; }

    public bool? Bool6 { get; set; }

    public bool? Bool7 { get; set; }
}

public sealed class ArrayAllocationDto
{
    public ArrayValue[] Values { get; set; } = [];

    public sbyte[] Bytes { get; set; } = [];
}

public sealed class ArrayValue
{
    public int Id { get; set; }

    public long Value { get; set; }
}

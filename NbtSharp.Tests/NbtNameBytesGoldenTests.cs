using System.Buffers;

namespace NbtSharp.Tests;

[NbtSerializable(typeof(AlternateNameBytesDto))]
internal partial class AlternateNameBytesContext : NbtSerializerContext
{
    public AlternateNameBytesContext()
    {
    }

    public AlternateNameBytesContext(NbtSerializerOptions options)
        : base(options)
    {
    }
}

internal sealed class AlternateNameBytesDto
{
    [NbtPropertyName("\0A")]
    public int NullPrefixed { get; set; }

    [NbtPropertyName("A")]
    public int Overlong { get; set; }

    [NbtPropertyName("\0a\U0001F600z")]
    public string Weird { get; set; } = "";
}

public sealed class NbtNameBytesGoldenTests
{
    [Fact]
    public void AlternateNameBytesBindOnTheGeneratedFastPath()
    {
        var actual = NbtSerializer.Deserialize(
            BuildAlternateNameBytesDocument(),
            AlternateNameBytesContext.Default.AlternateNameBytesDto);

        AssertDto(actual);
    }

    [Fact]
    public void AlternateNameBytesBindOnTheGeneratedSlowPath()
    {
        var options = new NbtSerializerOptions();
        options.Converters.Add(new MarkerConverter());
        var context = new AlternateNameBytesContext(options);

        var actual = NbtSerializer.Deserialize(BuildAlternateNameBytesDocument(), context.AlternateNameBytesDto);

        AssertDto(actual);
    }

    [Fact]
    public void AlternateNameBytesBindThroughReflection()
    {
        var options = new NbtSerializerOptions
        {
            TypeInfoResolver = new NbtReflectionTypeInfoResolver(),
        };

        var actual = NbtSerializer.Deserialize<AlternateNameBytesDto>(BuildAlternateNameBytesDocument(), options);

        AssertDto(actual);
    }

    [Fact]
    public void GeneratedFastPathOutputMatchesReflectionByteForByte()
    {
        var value = new AlternateNameBytesDto { NullPrefixed = 1, Overlong = 2, Weird = "\0é\U0001F600" };
        var generated = NbtSerializer.Serialize(
            value,
            AlternateNameBytesContext.Default.AlternateNameBytesDto,
            "root");
        var reflectionOptions = new NbtSerializerOptions
        {
            TypeInfoResolver = new NbtReflectionTypeInfoResolver(),
        };
        var reflected = NbtSerializer.Serialize(value, "root", reflectionOptions);

        Assert.Equal(reflected, generated);

        var roundTripped = NbtSerializer.Deserialize(generated, AlternateNameBytesContext.Default.AlternateNameBytesDto);
        Assert.Equal(value.NullPrefixed, roundTripped.NullPrefixed);
        Assert.Equal(value.Overlong, roundTripped.Overlong);
        Assert.Equal(value.Weird, roundTripped.Weird);
    }

    private static void AssertDto(AlternateNameBytesDto actual)
    {
        Assert.Equal(7, actual.NullPrefixed);
        Assert.Equal(11, actual.Overlong);
        Assert.Equal("bound", actual.Weird);
    }

    // A compound whose member names use alternate modified UTF-8 encodings: a raw 0x00 for
    // "\0A", the overlong three-byte form of "A", and a raw 0x00 prefix on "\0a\U0001F600z". Each
    // must bind to the DTO member whose declared name has the same decoded value.
    private static byte[] BuildAlternateNameBytesDocument()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer);
        writer.WriteStartCompound("root");
        writer.WriteInt32(new byte[] { 0x00, 0x41 }, 7);
        writer.WriteInt32(new byte[] { 0xE0, 0x81, 0x81 }, 11);
        writer.WriteString(
            new byte[] { 0x00, 0x61, 0xED, 0xA0, 0xBD, 0xED, 0xB8, 0x80, 0x7A },
            "bound");
        writer.WriteEndCompound();
        writer.Complete();
        return buffer.WrittenSpan.ToArray();
    }

    // Registered only to make options.Converters non-empty, which disables the direct fast path.
    private sealed class MarkerConverter : NbtConverter<Guid>
    {
        public override Guid Read(ref NbtReader reader, NbtSerializerOptions options) => default;

        public override void Write(ref NbtWriter writer, Guid value, NbtSerializerOptions options) =>
            writer.WriteString(value.ToString());

        public override bool TryGetTagType(
            Guid value,
            NbtSerializerOptions options,
            out NbtTagType tagType)
        {
            tagType = NbtTagType.String;
            return true;
        }
    }
}

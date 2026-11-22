using System.Buffers;

namespace NbtSharp.Tests;

[NbtSerializable(typeof(NonCanonicalNamesDto))]
internal partial class NonCanonicalNameContext : NbtSerializerContext
{
    public NonCanonicalNameContext()
    {
    }

    public NonCanonicalNameContext(NbtSerializerOptions options)
        : base(options)
    {
    }
}

internal sealed class NonCanonicalNamesDto
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
    public void NonCanonicalNamesBindOnTheGeneratedFastPath()
    {
        var actual = NbtSerializer.Deserialize(
            BuildNonCanonicalDocument(),
            NonCanonicalNameContext.Default.NonCanonicalNamesDto);

        AssertDto(actual);
    }

    [Fact]
    public void NonCanonicalNamesBindOnTheGeneratedSlowPath()
    {
        var options = new NbtSerializerOptions();
        options.Converters.Add(new MarkerConverter());
        var context = new NonCanonicalNameContext(options);

        var actual = NbtSerializer.Deserialize(BuildNonCanonicalDocument(), context.NonCanonicalNamesDto);

        AssertDto(actual);
    }

    [Fact]
    public void NonCanonicalNamesBindThroughReflection()
    {
        var options = new NbtSerializerOptions
        {
            TypeInfoResolver = new NbtReflectionTypeInfoResolver(),
        };

        var actual = NbtSerializer.Deserialize<NonCanonicalNamesDto>(BuildNonCanonicalDocument(), options);

        AssertDto(actual);
    }

    [Fact]
    public void GeneratedFastPathOutputMatchesReflectionByteForByte()
    {
        var value = new NonCanonicalNamesDto { NullPrefixed = 1, Overlong = 2, Weird = "\0é\U0001F600" };
        var generated = NbtSerializer.Serialize(
            value,
            NonCanonicalNameContext.Default.NonCanonicalNamesDto,
            "root");
        var reflectionOptions = new NbtSerializerOptions
        {
            TypeInfoResolver = new NbtReflectionTypeInfoResolver(),
        };
        var reflected = NbtSerializer.Serialize(value, "root", reflectionOptions);

        Assert.Equal(reflected, generated);

        var roundTripped = NbtSerializer.Deserialize(generated, NonCanonicalNameContext.Default.NonCanonicalNamesDto);
        Assert.Equal(value.NullPrefixed, roundTripped.NullPrefixed);
        Assert.Equal(value.Overlong, roundTripped.Overlong);
        Assert.Equal(value.Weird, roundTripped.Weird);
    }

    private static void AssertDto(NonCanonicalNamesDto actual)
    {
        Assert.Equal(7, actual.NullPrefixed);
        Assert.Equal(11, actual.Overlong);
        Assert.Equal("bound", actual.Weird);
    }

    // A compound whose member names use non-canonical modified UTF-8 encodings: a raw 0x00 for
    // "\0A", the overlong three-byte form of "A", and a raw 0x00 prefix on "\0a\U0001F600z". Each
    // must bind to the DTO member whose declared name is the canonical encoding of the same value.
    private static byte[] BuildNonCanonicalDocument()
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

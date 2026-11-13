using System.Buffers;

namespace NbtSharp.Tests;

public sealed class NbtErrorPathTests
{
    [Fact]
    public void NestedMemberErrorsCarryTheFullPath()
    {
        var root = new NbtRoot("root", new NbtCompound
        {
            ["Name"] = "A",
            ["Items"] = new NbtList(new List<NbtCompound>
            {
                new()
                {
                    ["Id"] = "x",
                    ["Count"] = "not an int",
                },
            }),
        });
        var data = NbtSerializer.Serialize(root);
        var options = ReflectionOptions();

        var exception = Assert.Throws<NbtException>(() =>
            NbtSerializer.Deserialize<PlayerDto>(data, options));

        Assert.Equal("$.Items[0].Count", exception.Path);
        Assert.Equal(NbtTagType.Int, exception.ExpectedTagType);
        Assert.Equal(NbtTagType.String, exception.ActualTagType);
        Assert.NotNull(exception.BytePosition);
    }

    [Fact]
    public void ScalarGetterOnAContainerReportsTheActualTagType()
    {
        var data = NbtSerializer.Serialize(new NbtRoot("root", new NbtCompound { ["X"] = 1 }));

        var exception = Assert.Throws<NbtException>(() => NbtSerializer.Deserialize<int>(data));

        Assert.Equal("$", exception.Path);
        Assert.Equal(NbtTagType.Int, exception.ExpectedTagType);
        Assert.Equal(NbtTagType.Compound, exception.ActualTagType);
        Assert.Equal(0, exception.BytePosition);
    }

    [Fact]
    public void RootedPathsFromNestedSerializersAreNotPrefixedTwice()
    {
        var options = ReflectionOptions();
        options.Converters.Add(new RootedFailureConverter());
        var data = NbtSerializer.Serialize(new SimpleDto { X = 1 }, "root", options);

        var exception = Assert.Throws<NbtException>(() =>
            NbtSerializer.Deserialize<SimpleDto>(data, options));

        Assert.Equal("$.X", exception.Path);
    }

    [Fact]
    public void MemberPayloadTagMismatchCarriesTheMemberPath()
    {
        var options = ReflectionOptions();
        options.Converters.Add(new IntAsStringConverter());

        var exception = Assert.Throws<NbtException>(() =>
            NbtSerializer.Serialize(new SimpleDto { X = 1 }, "root", options));

        Assert.Equal("$.X", exception.Path);
        Assert.Equal(NbtTagType.Int, exception.ExpectedTagType);
        Assert.Equal(NbtTagType.String, exception.ActualTagType);
    }

    [Fact]
    public void ListTypeMismatchCarriesTheElementIndex()
    {
        var options = new NbtSerializerOptions();
        options.Converters.Add(new IntAsStringConverter());

        var exception = Assert.Throws<NbtException>(() =>
            NbtSerializer.Serialize(new List<int> { 1 }, "root", options));

        Assert.Equal("$[0]", exception.Path);
        Assert.Equal(NbtTagType.Int, exception.ExpectedTagType);
        Assert.Equal(NbtTagType.String, exception.ActualTagType);
        Assert.NotNull(exception.BytePosition);
    }

    [Fact]
    public void DuplicateMemberErrorsCarryTheMemberName()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer);
        writer.WriteStartCompound("root");
        writer.WriteString("Name", "first");
        writer.WriteString("Name", "second");
        writer.WriteEndCompound();
        writer.Complete();

        var options = ReflectionOptions();
        options.RejectDuplicateCompoundNames = true;

        var exception = Assert.Throws<NbtException>(() =>
            NbtSerializer.Deserialize<PlayerDto>(buffer.WrittenSpan, options));

        Assert.Equal("$.Name", exception.Path);
    }

    private static NbtSerializerOptions ReflectionOptions() => new()
    {
        TypeInfoResolver = new NbtReflectionTypeInfoResolver(),
    };

    private sealed class PlayerDto
    {
        public string Name { get; set; } = "";

        public List<ItemDto> Items { get; set; } = [];
    }

    private sealed class ItemDto
    {
        public string Id { get; set; } = "";

        public int Count { get; set; }
    }

    private sealed class SimpleDto
    {
        public int X { get; set; }
    }

    private sealed class RootedFailureConverter : NbtConverter<int>
    {
        public override int Read(ref NbtReader reader, NbtSerializerOptions options)
        {
            var bad = NbtSerializer.Serialize(new NbtRoot("bad", new NbtCompound()));
            return NbtSerializer.Deserialize<int>(bad);
        }

        public override void Write(ref NbtWriter writer, int value, NbtSerializerOptions options) =>
            writer.WriteInt32(value);

        public override bool TryGetTagType(int value, NbtSerializerOptions options, out NbtTagType tagType)
        {
            tagType = NbtTagType.Int;
            return true;
        }
    }

    private sealed class IntAsStringConverter : NbtConverter<int>
    {
        public override int Read(ref NbtReader reader, NbtSerializerOptions options) => reader.GetInt32();

        public override void Write(ref NbtWriter writer, int value, NbtSerializerOptions options) =>
            writer.WriteString(value.ToString());

        public override bool TryGetTagType(int value, NbtSerializerOptions options, out NbtTagType tagType)
        {
            tagType = NbtTagType.Int;
            return true;
        }
    }
}

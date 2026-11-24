using System.Buffers;
using System.Buffers.Binary;
using System.Collections;

namespace NbtSharp.Tests;

public sealed class NbtDocumentTests
{
    private static readonly NbtReaderOptions UnnamedOptions = new()
    {
        RootFormat = NbtRootFormat.Unnamed,
    };

    private static readonly NbtTagType[] AllTags =
    [
        NbtTagType.Byte,
        NbtTagType.Short,
        NbtTagType.Int,
        NbtTagType.Long,
        NbtTagType.Float,
        NbtTagType.Double,
        NbtTagType.ByteArray,
        NbtTagType.String,
        NbtTagType.List,
        NbtTagType.Compound,
        NbtTagType.IntArray,
        NbtTagType.LongArray,
    ];

    private static readonly byte[] SampleDocument = BuildSampleDocument();

    public static IEnumerable<object[]> CasePropertyPairs =>
        AllTags.SelectMany(actual => AllTags.Select(property => new object[] { actual, property }));

    public static IEnumerable<object[]> TruncationLengths =>
        Enumerable.Range(0, SampleDocument.Length).Select(length => new object[] { length });

    private delegate void RootWriter(ref NbtWriter writer);

    // --------------------------------------------------------------------------------------------
    // Parse overloads and the root surface
    // --------------------------------------------------------------------------------------------

    [Fact]
    public void ParsesFromMemory()
    {
        var data = BuildSampleDocument();
        using var document = NbtDocument.Parse(data.AsMemory());

        Assert.Equal("root", document.RootName);
        Assert.Equal(NbtTagType.Compound, document.RootElement.TagType);
        Assert.Equal(20, document.RootElement.Compound["Health"].Int32);
    }

    [Fact]
    public void ParsesFromByteArrayThroughTheMemoryOverload()
    {
        var data = BuildSampleDocument();
        using var document = NbtDocument.Parse(data);

        var root = document.RootElement.Compound;
        Assert.True(root.TryGetValue("Health", out var health));
        Assert.Equal(20, health.Int32);
        Assert.Equal("a long string value", root["Name"].String.GetString());
    }

    [Fact]
    public void ParsesFromSingleSegmentSequence()
    {
        var data = BuildSampleDocument();
        var sequence = new ReadOnlySequence<byte>(data);

        using var document = NbtDocument.Parse(sequence);

        Assert.Equal(20, document.RootElement.Compound["Health"].Int32);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(17)]
    public void ParsesFromMultiSegmentSequenceWithNamesAndScalarsCrossingSegments(int segmentLength)
    {
        var data = BuildSampleDocument();
        var sequence = CreateSegmentedSequence(data, segmentLength);

        using var document = NbtDocument.Parse(sequence);

        var root = document.RootElement.Compound;
        Assert.Equal(20, RequireMember(root, "Health").Int32);
        Assert.Equal(-3.5, RequireMember(root, "Temperature").Double);
        Assert.Equal("a long string value", RequireMember(root, "Name").String.GetString());
        Assert.Equal([1, 2, 3], ReadIntElements(RequireMember(root, "Numbers").List));
    }

    [Fact]
    public void ParsesFromStream()
    {
        using var stream = new MemoryStream(BuildSampleDocument());

        using var document = NbtDocument.Parse(stream);

        Assert.Equal(20, document.RootElement.Compound["Health"].Int32);
    }

    [Fact]
    public void EmptyInputsAreTruncatedAtParse()
    {
        var memoryException = Assert.Throws<NbtException>(() => NbtDocument.Parse(ReadOnlyMemory<byte>.Empty));
        Assert.True(memoryException.IsTruncated);

        var sequenceException = Assert.Throws<NbtException>(() => NbtDocument.Parse(ReadOnlySequence<byte>.Empty));
        Assert.True(sequenceException.IsTruncated);

        using var stream = new MemoryStream();
        var streamException = Assert.Throws<NbtException>(() => NbtDocument.Parse(stream));
        Assert.True(streamException.IsTruncated);
    }

    [Fact]
    public void RootNameDistinguishesNamedEmptyAndUnnamedRoots()
    {
        using var named = NbtDocument.Parse(BuildSampleDocument());
        using var emptyNamed = NbtDocument.Parse(BuildEmptyNamedRoot());
        using var unnamed = NbtDocument.Parse(BuildUnnamedDocument(), UnnamedOptions);

        Assert.Equal("root", named.RootName);
        Assert.Equal(string.Empty, emptyNamed.RootName);
        Assert.Null(unnamed.RootName);
        Assert.Equal(NbtTagType.Compound, emptyNamed.RootElement.TagType);
        Assert.Equal(NbtTagType.Compound, unnamed.RootElement.TagType);
        Assert.Equal(7, unnamed.RootElement.Compound["Value"].Int32);
    }

    [Fact]
    public void RootNameIsDecodedOnceAndCached()
    {
        using var document = NbtDocument.Parse(BuildSampleDocument());

        var first = document.RootName;
        var second = document.RootName;

        Assert.Equal("root", first);
        Assert.Same(first, second);
    }

    // --------------------------------------------------------------------------------------------
    // Root element cases
    // --------------------------------------------------------------------------------------------

    [Fact]
    public void NumericRootsExposeTheirInlineCase()
    {
        using var byteDocument = ParseUnnamedRootDocument(
            static (ref writer) => writer.WriteSByte("ignored", -5),
            out var byteRoot);
        Assert.Equal(NbtTagType.Byte, byteRoot.TagType);
        Assert.Equal(-5, byteRoot.SByte);

        using var shortDocument = ParseUnnamedRootDocument(
            static (ref writer) => writer.WriteInt16("ignored", 12345),
            out var shortRoot);
        Assert.Equal(NbtTagType.Short, shortRoot.TagType);
        Assert.Equal((short)12345, shortRoot.Int16);

        using var intDocument = ParseUnnamedRootDocument(
            static (ref writer) => writer.WriteInt32("ignored", -123456789),
            out var intRoot);
        Assert.Equal(NbtTagType.Int, intRoot.TagType);
        Assert.Equal(-123456789, intRoot.Int32);

        using var longDocument = ParseUnnamedRootDocument(
            static (ref writer) => writer.WriteInt64("ignored", 1234567890123L),
            out var longRoot);
        Assert.Equal(NbtTagType.Long, longRoot.TagType);
        Assert.Equal(1234567890123L, longRoot.Int64);

        using var floatDocument = ParseUnnamedRootDocument(
            static (ref writer) => writer.WriteSingle("ignored", 0.5f),
            out var floatRoot);
        Assert.Equal(NbtTagType.Float, floatRoot.TagType);
        Assert.Equal(0.5f, floatRoot.Single);

        using var doubleDocument = ParseUnnamedRootDocument(
            static (ref writer) => writer.WriteDouble("ignored", -0.25),
            out var doubleRoot);
        Assert.Equal(NbtTagType.Double, doubleRoot.TagType);
        Assert.Equal(-0.25, doubleRoot.Double);
    }

    [Fact]
    public void StringRootExposesTheEncodedView()
    {
        using var document = ParseUnnamedRootDocument(
            static (ref writer) => writer.WriteString("ignored", "中文 \0 x"),
            out var root);

        Assert.Equal(NbtTagType.String, root.TagType);
        var view = root.String;
        Assert.False(view.IsEmpty);
        Assert.Equal("中文 \0 x", view.GetString());
        Assert.Equal(11, view.ByteLength);
        Assert.Equal(view.ByteLength, view.RawSpan.Length);
        Assert.Equal(view.ByteLength, view.RawMemory.Length);
        Assert.Equal(view.RawSpan.ToArray(), view.RawMemory.ToArray());
    }

    [Fact]
    public void ArrayRootsExposeZeroCopyViews()
    {
        using var byteDocument = ParseUnnamedRootDocument(
            static (ref writer) => writer.WriteByteArray("ignored", [1, 2, 3]),
            out var byteRoot);
        Assert.Equal(NbtTagType.ByteArray, byteRoot.TagType);
        var byteArray = byteRoot.ByteArray;
        Assert.Equal(3, byteArray.Length);
        Assert.Equal([1, 2, 3], byteArray.ToArray());
        Assert.Equal([1, 2, 3], byteArray.Memory.ToArray());

        using var intDocument = ParseUnnamedRootDocument(
            static (ref writer) => writer.WriteInt32Array("ignored", [1, -2, 3]),
            out var intRoot);
        Assert.Equal(NbtTagType.IntArray, intRoot.TagType);
        var intArray = intRoot.IntArray;
        Assert.Equal(3, intArray.Length);
        Assert.Equal([1, -2, 3], intArray.ToArray());
        Assert.Equal(intArray.Length * sizeof(int), intArray.RawSpan.Length);

        using var longDocument = ParseUnnamedRootDocument(
            static (ref writer) => writer.WriteInt64Array("ignored", [1L, -2L]),
            out var longRoot);
        Assert.Equal(NbtTagType.LongArray, longRoot.TagType);
        var longArray = longRoot.LongArray;
        Assert.Equal(2, longArray.Length);
        Assert.Equal([1L, -2L], longArray.ToArray());
        Assert.Equal(longArray.Length * sizeof(long), longArray.RawSpan.Length);
    }

    [Fact]
    public void ListRootExposesTheViewInWireOrder()
    {
        using var document = ParseUnnamedRootDocument(
            static (ref writer) =>
            {
                writer.WriteStartList("ignored", NbtTagType.Int, 3);
                writer.WriteInt32(1);
                writer.WriteInt32(2);
                writer.WriteInt32(3);
                writer.WriteEndList();
            },
            out var root);

        Assert.Equal(NbtTagType.List, root.TagType);
        var list = root.List;
        Assert.Equal(NbtTagType.Int, list.ElementType);
        Assert.Equal(3, list.Count);
        Assert.Equal(1, list[0].Int32);
        Assert.Equal(2, list[1].Int32);
        Assert.Equal(3, list[2].Int32);
        Assert.Equal([1, 2, 3], ReadIntElements(list));
    }

    [Fact]
    public void CompoundRootExposesMembersAndCount()
    {
        using var document = NbtDocument.Parse(BuildSampleDocument());
        var root = document.RootElement.Compound;

        Assert.Equal(4, root.Count);
        Assert.Equal(20, root["Health"].Int32);
        Assert.Equal(-3.5, root["Temperature"].Double);
        Assert.Equal("a long string value", root["Name"].String.GetString());
        var numbers = root["Numbers"].List;
        Assert.Equal(NbtTagType.Int, numbers.ElementType);
        Assert.Equal(3, numbers.Count);
        Assert.Equal([1, 2, 3], ReadIntElements(numbers));
    }

    // --------------------------------------------------------------------------------------------
    // The twelve tag types
    // --------------------------------------------------------------------------------------------

    [Fact]
    public void ReadsAllTwelveTagTypesThroughCompoundMembers()
    {
        using var document = NbtDocument.Parse(BuildAllTagsDocument());
        var root = document.RootElement.Compound;

        Assert.Equal(12, root.Count);

        Assert.Equal(-5, RequireMember(root, "Byte").SByte);
        Assert.Equal((short)12345, RequireMember(root, "Short").Int16);
        Assert.Equal(-123456789, RequireMember(root, "Int").Int32);
        Assert.Equal(1234567890123L, RequireMember(root, "Long").Int64);
        Assert.Equal(0.5f, RequireMember(root, "Float").Single);
        Assert.Equal(-0.25, RequireMember(root, "Double").Double);

        var stringView = RequireMember(root, "String").String;
        Assert.Equal("中文 \0 x", stringView.GetString());
        Assert.Equal(11, stringView.ByteLength);

        var byteArray = RequireMember(root, "ByteArray").ByteArray;
        Assert.Equal(3, byteArray.Length);
        Assert.Equal([1, 2, 3], byteArray.Span.ToArray());
        Assert.Equal(new sbyte[] { 1, 2, 3 }, byteArray.ToSByteArray());

        Assert.Equal(
            [int.MinValue, -1, int.MaxValue],
            RequireMember(root, "IntArray").IntArray.ToArray());
        Assert.Equal(
            [long.MinValue, long.MaxValue],
            RequireMember(root, "LongArray").LongArray.ToArray());

        var list = RequireMember(root, "List").List;
        Assert.Equal(NbtTagType.String, list.ElementType);
        Assert.Equal(2, list.Count);
        Assert.Equal("a", list[0].String.GetString());
        Assert.Equal("b", list[1].String.GetString());

        var compound = RequireMember(root, "Compound").Compound;
        Assert.Single(compound);
        Assert.Equal(1, compound["Nested"].Int32);
    }

    [Fact]
    public void RepeatedAccessReturnsEqualValues()
    {
        using var document = NbtDocument.Parse(BuildAllTagsDocument());
        var root = document.RootElement.Compound;
        var intMember = root["Int"];
        var stringMember = root["String"];
        var arrayMember = root["ByteArray"];

        Assert.Equal(-123456789, intMember.Int32);
        Assert.Equal(-123456789, intMember.Int32);
        Assert.Equal("中文 \0 x", stringMember.String.GetString());
        Assert.Equal("中文 \0 x", stringMember.String.GetString());
        Assert.Equal(new sbyte[] { 1, 2, 3 }, arrayMember.ByteArray.ToSByteArray());
        Assert.Equal(new sbyte[] { 1, 2, 3 }, arrayMember.ByteArray.ToSByteArray());
    }

    [Theory]
    [MemberData(nameof(CasePropertyPairs))]
    public void TagTypeSelectsExactlyOneCaseProperty(NbtTagType actualTag, NbtTagType propertyTag)
    {
        using var document = NbtDocument.Parse(BuildUnnamedRoot(actualTag), UnnamedOptions);
        var element = document.RootElement;

        Assert.Equal(actualTag, element.TagType);

        if (actualTag == propertyTag)
        {
            Assert.NotNull(ReadCaseProperty(element, propertyTag));
            return;
        }

        var exception = Assert.Throws<NbtException>(() => ReadCaseProperty(element, propertyTag));
        Assert.Equal(propertyTag, exception.ExpectedTagType);
        Assert.Equal(actualTag, exception.ActualTagType);
    }

    // --------------------------------------------------------------------------------------------
    // Union mismatch behavior
    // --------------------------------------------------------------------------------------------

    [Fact]
    public void MismatchedCasePropertyReportsExpectedAndActualTagTypes()
    {
        using var document = ParseUnnamedRootDocument(
            static (ref writer) => writer.WriteInt32("ignored", 7),
            out var root);

        var exception = Assert.Throws<NbtException>(() => root.Double);
        Assert.Equal(NbtTagType.Double, exception.ExpectedTagType);
        Assert.Equal(NbtTagType.Int, exception.ActualTagType);
        Assert.Contains("Expected a Double value", exception.Message, StringComparison.Ordinal);
        Assert.Contains("current value is Int", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ContainerCaseMismatchReportsExpectedAndActualTagTypes()
    {
        using var compoundDocument = ParseUnnamedRootDocument(
            static (ref writer) =>
            {
                writer.WriteStartCompound("ignored");
                writer.WriteInt32("x", 1);
                writer.WriteEndCompound();
            },
            out var compoundRoot);

        var listException = Assert.Throws<NbtException>(() => compoundRoot.List);
        Assert.Equal(NbtTagType.List, listException.ExpectedTagType);
        Assert.Equal(NbtTagType.Compound, listException.ActualTagType);

        using var listDocument = ParseUnnamedRootDocument(
            static (ref writer) =>
            {
                writer.WriteStartList("ignored", NbtTagType.Int, 0);
                writer.WriteEndList();
            },
            out var listRoot);

        var compoundException = Assert.Throws<NbtException>(() => listRoot.Compound);
        Assert.Equal(NbtTagType.Compound, compoundException.ExpectedTagType);
        Assert.Equal(NbtTagType.List, compoundException.ActualTagType);

        // Access-time mismatches carry the two tag types but no byte position: the element was
        // indexed successfully, and nothing was read to produce the failure.
        Assert.Null(listException.BytePosition);
        Assert.Null(compoundException.BytePosition);
    }

    [Fact]
    public void DefaultElementHasNoCase()
    {
        var element = default(NbtElement);

        Assert.Null(element.Value);
        Assert.False(element is int);

        var tagException = Assert.Throws<NbtException>(() => element.TagType);
        Assert.Null(tagException.ExpectedTagType);
        Assert.Null(tagException.ActualTagType);
        Assert.Contains("uninitialized", tagException.Message, StringComparison.Ordinal);

        AssertDefaultCaseFailure(() => element.SByte, NbtTagType.Byte);
        AssertDefaultCaseFailure(() => element.Int16, NbtTagType.Short);
        AssertDefaultCaseFailure(() => element.Int32, NbtTagType.Int);
        AssertDefaultCaseFailure(() => element.Int64, NbtTagType.Long);
        AssertDefaultCaseFailure(() => element.Single, NbtTagType.Float);
        AssertDefaultCaseFailure(() => element.Double, NbtTagType.Double);
        AssertDefaultCaseFailure(() => element.String, NbtTagType.String);
        AssertDefaultCaseFailure(() => element.ByteArray, NbtTagType.ByteArray);
        AssertDefaultCaseFailure(() => element.IntArray, NbtTagType.IntArray);
        AssertDefaultCaseFailure(() => element.LongArray, NbtTagType.LongArray);
        AssertDefaultCaseFailure(() => element.List, NbtTagType.List);
        AssertDefaultCaseFailure(() => element.Compound, NbtTagType.Compound);
    }

    [Fact]
    public void ElementValueAndPatternMatchingExposeTheUnionCase()
    {
        using var document = ParseUnnamedRootDocument(
            static (ref writer) => writer.WriteInt32("ignored", 42),
            out var root);

        Assert.Equal(42, Assert.IsType<int>(root.Value));
        Assert.True(root is int);
        Assert.Equal(42, root.Int32);
    }

    [Fact]
    public void ElementValueBoxesEveryCase()
    {
        using var document = NbtDocument.Parse(BuildAllTagsDocument());
        var root = document.RootElement.Compound;

        Assert.IsType<sbyte>(root["Byte"].Value);
        Assert.IsType<short>(root["Short"].Value);
        Assert.IsType<int>(root["Int"].Value);
        Assert.IsType<long>(root["Long"].Value);
        Assert.IsType<float>(root["Float"].Value);
        Assert.IsType<double>(root["Double"].Value);
        Assert.IsType<NbtStringElement>(root["String"].Value);
        Assert.IsType<NbtByteArrayElement>(root["ByteArray"].Value);
        Assert.IsType<NbtIntArrayElement>(root["IntArray"].Value);
        Assert.IsType<NbtLongArrayElement>(root["LongArray"].Value);
        Assert.IsType<NbtListElement>(root["List"].Value);
        Assert.IsType<NbtCompoundElement>(root["Compound"].Value);
    }

    [Fact]
    public void ParsingBeyondTheInitialSlotCapacityPreservesEveryValue()
    {
        const int memberCount = 100;
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer);
        writer.WriteStartCompound("root");
        for (var i = 0; i < memberCount; i++)
        {
            writer.WriteInt32($"m{i}", i);
        }

        writer.WriteEndCompound();
        writer.Complete();

        using var document = NbtDocument.Parse(buffer.WrittenSpan.ToArray());
        var root = document.RootElement.Compound;

        Assert.Equal(memberCount, root.Count);
        for (var i = 0; i < memberCount; i++)
        {
            Assert.Equal(i, root[$"m{i}"].Int32);
        }
    }

    // --------------------------------------------------------------------------------------------
    // Strings
    // --------------------------------------------------------------------------------------------

    [Fact]
    public void GetStringDecodesModifiedUtf8AtAccessTime()
    {
        // An unnamed TAG_String root: tag, length prefix, then payload.
        Assert.Equal(string.Empty, ParseString([0x08, 0x00, 0x00]));
        Assert.Equal("\0", ParseString([0x08, 0x00, 0x02, 0xC0, 0x80]));
        Assert.Equal("\U0001F600", ParseString([0x08, 0x00, 0x06, 0xED, 0xA0, 0xBD, 0xED, 0xB8, 0x80]));
    }

    [Fact]
    public void StringCaseRejectsF0LeadBytesAtAccessTimeWithThePayloadPosition()
    {
        var data = new byte[] { 0x08, 0x00, 0x02, 0xF0, 0x41 };
        using var document = NbtDocument.Parse(data, UnnamedOptions);

        // Indexing accepted the payload because it only checks the length prefix; the lead byte is
        // rejected when the view is decoded, reported at the payload's length prefix.
        var view = document.RootElement.String;
        Assert.Equal(2, view.ByteLength);
        Assert.Equal(new byte[] { 0xF0, 0x41 }, view.RawSpan.ToArray());

        var exception = Assert.Throws<NbtException>(() => view.GetString());
        Assert.Equal(1, exception.BytePosition);
    }

    [Fact]
    public void StringRawBytesExposeThePayloadWithoutTheLengthPrefix()
    {
        var data = new byte[] { 0x08, 0x00, 0x06, 0xED, 0xA0, 0xBD, 0xED, 0xB8, 0x80 };
        using var document = NbtDocument.Parse(data, UnnamedOptions);
        var view = document.RootElement.String;

        Assert.Equal(6, view.ByteLength);
        Assert.Equal(new byte[] { 0xED, 0xA0, 0xBD, 0xED, 0xB8, 0x80 }, view.RawSpan.ToArray());
        Assert.Equal(new byte[] { 0xED, 0xA0, 0xBD, 0xED, 0xB8, 0x80 }, view.RawMemory.ToArray());
        Assert.Equal("\U0001F600", view.GetString());
    }

    [Fact]
    public void StringRoundTripsNullAndSupplementaryCharacters()
    {
        const string value = "a\0b\U0001F600c";
        using var document = ParseUnnamedRootDocument(
            static (ref writer) => writer.WriteString("ignored", value),
            out var root);

        Assert.Equal(value, root.String.GetString());
    }

    [Fact]
    public void EmptyStringRootExposesAnEmptyView()
    {
        using var document = ParseUnnamedRootDocument(
            static (ref writer) => writer.WriteString("ignored", string.Empty),
            out var root);

        var view = root.String;
        Assert.True(view.IsEmpty);
        Assert.Equal(0, view.ByteLength);
        Assert.Empty(view.RawSpan.ToArray());
        Assert.Equal(string.Empty, view.GetString());
    }

    // --------------------------------------------------------------------------------------------
    // Arrays
    // --------------------------------------------------------------------------------------------

    [Fact]
    public void ByteArrayViewMatchesTheWirePayload()
    {
        byte[] payload = [0x00, 0x7F, 0x80, 0xFF];
        var data = new byte[1 + sizeof(int) + payload.Length];
        data[0] = (byte)NbtTagType.ByteArray;
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(1, sizeof(int)), payload.Length);
        payload.AsSpan().CopyTo(data.AsSpan(1 + sizeof(int)));

        using var document = NbtDocument.Parse(data, UnnamedOptions);
        var view = document.RootElement.ByteArray;

        Assert.Equal(4, view.Length);
        Assert.Equal(payload, view.Span.ToArray());
        Assert.Equal(payload, view.Memory.ToArray());
        Assert.Equal(new sbyte[] { 0, 127, -128, -1 }, view.SByteSpan.ToArray());
        Assert.Equal(new sbyte[] { 0, 127, -128, -1 }, view.ToSByteArray());

        var bytes = new byte[view.Length];
        view.CopyTo(bytes);
        Assert.Equal(payload, bytes);

        var signed = new sbyte[view.Length];
        view.CopyTo(signed);
        Assert.Equal(new sbyte[] { 0, 127, -128, -1 }, signed);
    }

    [Fact]
    public void IntArrayViewMatchesTheWirePayloadAndConvertsToHostOrder()
    {
        int[] values = [1, -2, int.MaxValue, int.MinValue];
        var payload = new byte[values.Length * sizeof(int)];
        for (var i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(i * sizeof(int), sizeof(int)), values[i]);
        }

        var data = new byte[1 + sizeof(int) + payload.Length];
        data[0] = (byte)NbtTagType.IntArray;
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(1, sizeof(int)), values.Length);
        payload.AsSpan().CopyTo(data.AsSpan(1 + sizeof(int)));

        using var document = NbtDocument.Parse(data, UnnamedOptions);
        var view = document.RootElement.IntArray;

        Assert.Equal(values.Length, view.Length);
        Assert.Equal(view.RawSpan.Length / sizeof(int), view.Length);
        Assert.Equal(payload.Length / sizeof(int), view.Length);
        Assert.Equal(payload, view.RawSpan.ToArray());
        Assert.Equal(payload, view.RawMemory.ToArray());
        Assert.Equal(values, view.ToArray());

        var copied = new int[values.Length];
        view.CopyTo(copied);
        Assert.Equal(values, copied);
    }

    [Fact]
    public void LongArrayViewMatchesTheWirePayloadAndConvertsToHostOrder()
    {
        long[] values = [1, -2, long.MaxValue, long.MinValue];
        var payload = new byte[values.Length * sizeof(long)];
        for (var i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(i * sizeof(long), sizeof(long)), values[i]);
        }

        var data = new byte[1 + sizeof(int) + payload.Length];
        data[0] = (byte)NbtTagType.LongArray;
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(1, sizeof(int)), values.Length);
        payload.AsSpan().CopyTo(data.AsSpan(1 + sizeof(int)));

        using var document = NbtDocument.Parse(data, UnnamedOptions);
        var view = document.RootElement.LongArray;

        Assert.Equal(values.Length, view.Length);
        Assert.Equal(view.RawSpan.Length / sizeof(long), view.Length);
        Assert.Equal(payload.Length / sizeof(long), view.Length);
        Assert.Equal(payload, view.RawSpan.ToArray());
        Assert.Equal(payload, view.RawMemory.ToArray());
        Assert.Equal(values, view.ToArray());

        var copied = new long[values.Length];
        view.CopyTo(copied);
        Assert.Equal(values, copied);
    }

    // --------------------------------------------------------------------------------------------
    // Compound members
    // --------------------------------------------------------------------------------------------

    [Fact]
    public void TryGetValueFindsAndMissesMembers()
    {
        using var document = NbtDocument.Parse(BuildSampleDocument());
        var root = document.RootElement.Compound;

        Assert.True(root.TryGetValue("Health", out var health));
        Assert.Equal(20, health.Int32);
        Assert.False(root.TryGetValue("Missing", out var missing));
        Assert.Throws<NbtException>(() => missing.Int32);
    }

    [Fact]
    public void MissingMemberThrowsKeyNotFound()
    {
        using var document = NbtDocument.Parse(BuildSampleDocument());

        var exception = Assert.Throws<KeyNotFoundException>(() => document.RootElement.Compound["Missing"]);
        Assert.Contains("Missing", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateMemberNamesResolveLastWins()
    {
        using var document = NbtDocument.Parse(BuildDuplicateMemberDocument());
        var root = document.RootElement.Compound;

        Assert.Equal(3, root.Count);
        Assert.Equal(2, root["Dup"].Int32);
        Assert.True(root.TryGetValue("Dup", out var duplicate));
        Assert.Equal(2, duplicate.Int32);
        Assert.Equal(3, root["Other"].Int32);
    }

    [Fact]
    public void EnumerationYieldsEveryDuplicateMember()
    {
        using var document = NbtDocument.Parse(BuildDuplicateMemberDocument());

        Assert.Equal(["Dup", "Dup", "Other"], ReadMemberNames(document.RootElement.Compound));
    }

    [Fact]
    public void CompoundImplementsPairCollection()
    {
        using var document = NbtDocument.Parse(BuildDuplicateMemberDocument());
        IReadOnlyCollection<KeyValuePair<string, NbtElement>> pairs = document.RootElement.Compound;

        Assert.Equal(3, pairs.Count);
        Assert.Equal(["Dup", "Dup", "Other"], pairs.Select(static pair => pair.Key).ToArray());
        Assert.Equal(2, document.RootElement.Compound["Dup"].Int32);
    }

    [Fact]
    public void DuplicateMemberPairsPreserveWireOrderAndValues()
    {
        using var document = NbtDocument.Parse(BuildDuplicateMemberDocument());

        var pairs = document.RootElement.Compound
            .Select(static pair => (pair.Key, pair.Value.Int32))
            .ToArray();

        Assert.Equal([("Dup", 1), ("Dup", 2), ("Other", 3)], pairs);
    }

    [Fact]
    public void CompoundEnumeratorDecodesOverlongWireName()
    {
        using var document = NbtDocument.Parse(
            BuildDocumentWithByteMemberName(new byte[] { 0xE0, 0x81, 0x81 }));
        using var enumerator = document.RootElement.Compound.GetEnumerator();

        Assert.True(enumerator.MoveNext());
        Assert.Equal("A", enumerator.Current.Key);
        Assert.Equal(7, enumerator.Current.Value.SByte);
    }

    [Fact]
    public void CompoundEnumeratorReportsInvalidWireName()
    {
        using var document = NbtDocument.Parse(BuildDocumentWithByteMemberName(new byte[] { 0x80 }));
        using var enumerator = document.RootElement.Compound.GetEnumerator();

        Assert.True(enumerator.MoveNext());
        Assert.Throws<NbtException>(() => _ = enumerator.Current);
    }

    [Fact]
    public void DisposedCompoundCollectionThrowsOnEnumeration()
    {
        var document = NbtDocument.Parse(BuildSampleDocument());
        IEnumerable<KeyValuePair<string, NbtElement>> pairs = document.RootElement.Compound;
        document.Dispose();

        Assert.Throws<ObjectDisposedException>(() => pairs.GetEnumerator());
    }

    [Theory]
    [InlineData(new byte[] { 0x00 }, "\0", "\0", true)] // wire raw null
    [InlineData(new byte[] { 0xC0, 0x80 }, "\0", "\0", true)] // wire two-byte null
    [InlineData(new byte[] { 0x41 }, "A", "A", true)] // wire single-byte 'A'
    [InlineData(new byte[] { 0xE0, 0x81, 0x81 }, "A", "A", true)] // overlong three-byte wire name
    [InlineData(new byte[] { 0xC1, 0x81 }, "A", "A", true)] // overlong two-byte wire name
    [InlineData(new byte[] { 0x00 }, "\0", "A", false)] // decoded values differ
    [InlineData(new byte[] { 0x41 }, "A", "\0", false)]
    [InlineData(new byte[] { 0xE0, 0x81, 0x81 }, "A", "\0", false)]
    public void WireNamesResolveByDecodedValue(
        byte[] wireName,
        string lookupName,
        string queryName,
        bool expected)
    {
        using var document = NbtDocument.Parse(BuildDocumentWithByteMemberName(wireName));
        var root = document.RootElement.Compound;

        Assert.True(root.TryGetValue(lookupName, out var member));
        Assert.Equal(7, member.SByte);

        Assert.Equal(expected, root.TryGetValue(queryName, out var queried));
        if (expected)
        {
            Assert.Equal(7, queried.SByte);
        }

        Assert.False(root.TryGetValue("B", out _));
    }

    [Fact]
    public void TryGetValueEncodesLongNamesThroughThePool()
    {
        var longName = new string('a', 300);
        using var document = NbtDocument.Parse(BuildLongMemberNameDocument(longName));
        var root = document.RootElement.Compound;

        Assert.True(root.TryGetValue(longName, out var member));
        Assert.Equal(7, member.Int32);
        Assert.False(root.TryGetValue(new string('a', 299), out _));
        Assert.False(root.TryGetValue(new string('a', 301), out _));
    }

    [Fact]
    public void CompoundEnumeratorExposesNameAndCurrentPerMember()
    {
        using var document = NbtDocument.Parse(BuildSampleDocument());
        var root = document.RootElement.Compound;

        var names = new List<string>(root.Count);
        var types = new List<NbtTagType>(root.Count);
        using var enumerator = root.GetEnumerator();
        while (enumerator.MoveNext())
        {
            names.Add(enumerator.Current.Key);
            types.Add(enumerator.Current.Value.TagType);
        }

        Assert.Equal(["Health", "Temperature", "Name", "Numbers"], names.ToArray());
        Assert.Equal([NbtTagType.Int, NbtTagType.Double, NbtTagType.String, NbtTagType.List], types.ToArray());

        enumerator.Reset();
        Assert.True(enumerator.MoveNext());
        Assert.Equal("Health", enumerator.Current.Key);
        Assert.Equal("Health", enumerator.Current.Key);
        Assert.Equal(NbtTagType.Int, enumerator.Current.Value.TagType);
    }

    [Fact]
    public void CompoundEnumeratorCurrentRequiresPosition()
    {
        using var document = NbtDocument.Parse(BuildSampleDocument());
        using var enumerator = document.RootElement.Compound.GetEnumerator();

        Assert.Throws<InvalidOperationException>(() => _ = enumerator.Current);

        Assert.True(enumerator.MoveNext());
        Assert.Equal("Health", enumerator.Current.Key);

        for (var i = 0; i < 3; i++)
        {
            Assert.True(enumerator.MoveNext());
        }

        Assert.False(enumerator.MoveNext());
        Assert.Throws<InvalidOperationException>(() => _ = enumerator.Current);

        enumerator.Reset();
        Assert.Throws<InvalidOperationException>(() => _ = enumerator.Current);

        Assert.Throws<InvalidOperationException>(() => _ = default(NbtCompoundElement.Enumerator).Current);
    }

    [Fact]
    public void CompoundEnumeratorSupportsNonGenericCurrent()
    {
        using var document = NbtDocument.Parse(BuildSampleDocument());
        using var enumerator = document.RootElement.Compound.GetEnumerator();
        IEnumerator boxed = enumerator;

        Assert.True(boxed.MoveNext());
        var pair = Assert.IsType<KeyValuePair<string, NbtElement>>(boxed.Current);
        Assert.Equal("Health", pair.Key);
        Assert.Equal(20, pair.Value.Int32);
    }

    [Fact]
    public void LayoutCounterexampleReportsCountsAndOrderCorrectly()
    {
        using var document = NbtDocument.Parse(BuildLayoutDocument());
        var root = document.RootElement.Compound;

        Assert.Equal(3, root.Count);
        Assert.Equal(["A", "Items", "B"], ReadMemberNames(root));

        var nested = root["A"].Compound;
        Assert.Single(nested);
        Assert.Equal(["x"], ReadMemberNames(nested));
        Assert.Equal(1, nested["x"].Int32);
        Assert.Equal(9, root["B"].Int32);
    }

    // --------------------------------------------------------------------------------------------
    // Lists
    // --------------------------------------------------------------------------------------------

    [Fact]
    public void ListViewReportsTypeCountAndOrder()
    {
        using var document = NbtDocument.Parse(BuildSampleDocument());
        var numbers = document.RootElement.Compound["Numbers"].List;

        Assert.Equal(NbtTagType.Int, numbers.ElementType);
        Assert.Equal(3, numbers.Count);
        Assert.Equal(1, numbers[0].Int32);
        Assert.Equal(2, numbers[1].Int32);
        Assert.Equal(3, numbers[2].Int32);
        Assert.Equal([1, 2, 3], ReadIntElements(numbers));
    }

    [Fact]
    public void IndexerThrowsForOutOfRangeIndices()
    {
        using var document = NbtDocument.Parse(BuildSampleDocument());
        var numbers = document.RootElement.Compound["Numbers"].List;

        Assert.Throws<ArgumentOutOfRangeException>(() => numbers[-1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => numbers[3]);
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = numbers[3]);
    }

    [Fact]
    public void EmptyListEncodingsKeepTheirElementType()
    {
        using var document = NbtDocument.Parse(BuildEmptyListDocument());
        var root = document.RootElement.Compound;

        var emptyEnd = root["EmptyEnd"].List;
        Assert.Equal(NbtTagType.End, emptyEnd.ElementType);
        Assert.Empty(emptyEnd);
        using var endEnumerator = emptyEnd.GetEnumerator();
        Assert.False(endEnumerator.MoveNext());


        var emptyInts = root["EmptyInts"].List;
        Assert.Equal(NbtTagType.Int, emptyInts.ElementType);
        Assert.Empty(emptyInts);
        using var intEnumerator = emptyInts.GetEnumerator();
        Assert.False(intEnumerator.MoveNext());
    }

    [Fact]
    public void ListEnumeratorResetsAndTerminates()
    {
        using var document = NbtDocument.Parse(BuildSampleDocument());
        var numbers = document.RootElement.Compound["Numbers"].List;

        using var enumerator = numbers.GetEnumerator();
        Assert.True(enumerator.MoveNext());
        Assert.Equal(1, enumerator.Current.Int32);
        enumerator.Reset();
        Assert.True(enumerator.MoveNext());
        Assert.Equal(1, enumerator.Current.Int32);
        Assert.True(enumerator.MoveNext());
        Assert.Equal(2, enumerator.Current.Int32);
        Assert.True(enumerator.MoveNext());
        Assert.Equal(3, enumerator.Current.Int32);
        Assert.False(enumerator.MoveNext());
        Assert.False(enumerator.MoveNext());
    }

    [Fact]
    public void ListEnumeratorSupportsIEnumeratorUse()
    {
        using var document = NbtDocument.Parse(BuildSampleDocument());
        var numbers = document.RootElement.Compound["Numbers"].List;

        using var enumerator = numbers.GetEnumerator();
        IEnumerator boxed = enumerator;
        var values = new List<int>();
        while (boxed.MoveNext())
        {
            values.Add(((NbtElement)boxed.Current!).Int32);
        }

        Assert.Equal([1, 2, 3], values.ToArray());
    }

    [Fact]
    public void ListElementImplementsIReadOnlyList()
    {
        using var document = NbtDocument.Parse(BuildSampleDocument());
        IReadOnlyList<NbtElement> numbers = document.RootElement.Compound["Numbers"].List;

        Assert.Equal(3, numbers.Count);
        Assert.Equal(1, numbers[0].Int32);
        Assert.Equal([1, 2, 3], numbers.Select(static element => element.Int32).ToArray());
    }

    // --------------------------------------------------------------------------------------------
    // Nesting
    // --------------------------------------------------------------------------------------------

    [Fact]
    public void NestedCompoundInListInCompoundNavigatesLayerByLayer()
    {
        using var document = NbtDocument.Parse(BuildLayoutDocument());
        var root = document.RootElement.Compound;

        Assert.Equal(1, root["A"].Compound["x"].Int32);
        Assert.Equal(9, root["B"].Int32);

        var items = root["Items"].List;
        Assert.Equal(NbtTagType.Compound, items.ElementType);
        Assert.Equal(3, items.Count);

        var first = items[0].Compound;
        Assert.Single(first);
        Assert.Equal(10, first["x"].Int32);
        Assert.Equal(20, items[1].Compound["y"].Int32);
        Assert.Equal(30, items[2].Compound["x"].Int32);

        Assert.Equal(["x", "y", "x"], ReadNestedMemberNames(items));
    }

    [Fact]
    public void UnnamedListRootExposesElements()
    {
        using var document = NbtDocument.Parse(BuildListDocument(2), UnnamedOptions);

        Assert.Null(document.RootName);
        Assert.Equal(NbtTagType.List, document.RootElement.TagType);
        var list = document.RootElement.List;
        Assert.Equal(2, list.Count);
        Assert.Equal(0, list[0].Int32);
        Assert.Equal(1, list[1].Int32);
    }

    // --------------------------------------------------------------------------------------------
    // Access behavior
    // --------------------------------------------------------------------------------------------

    [Fact]
    public void NumericRootAccess()
    {
        using var document = ParseUnnamedRootDocument(
            static (ref writer) => writer.WriteInt32("ignored", 42),
            out var root);

        var sum = 0;
        for (var i = 0; i < 1_000; i++)
        {
            sum += document.RootElement.Int32;
            sum += root.Int32;
        }

        Assert.Equal(84_000, sum);
    }

    [Fact]
    public void StringRawSpanAccess()
    {
        using var document = ParseUnnamedRootDocument(
            static (ref writer) => writer.WriteString("ignored", "中文 \0 x"),
            out var root);

        var length = 0;
        for (var i = 0; i < 1_000; i++)
        {
            length += document.RootElement.String.RawSpan.Length;
            length += root.String.RawSpan.Length;
        }

        Assert.Equal(22_000, length);
    }

    [Fact]
    public void ArrayRawSpanAccess()
    {
        using var byteDocument = ParseUnnamedRootDocument(
            static (ref writer) => writer.WriteByteArray("ignored", [1, 2, 3]),
            out var byteRoot);
        using var intDocument = ParseUnnamedRootDocument(
            static (ref writer) => writer.WriteInt32Array("ignored", [1, 2, 3]),
            out var intRoot);
        using var longDocument = ParseUnnamedRootDocument(
            static (ref writer) => writer.WriteInt64Array("ignored", [1L, 2L, 3L]),
            out var longRoot);

        var length = 0;
        for (var i = 0; i < 1_000; i++)
        {
            length += byteRoot.ByteArray.Span.Length;
            length += intRoot.IntArray.RawSpan.Length;
            length += longRoot.LongArray.RawSpan.Length;
        }

        Assert.Equal(39_000, length);
    }

    [Fact]
    public void ListElementAccessAndEnumeration()
    {
        using var document = NbtDocument.Parse(BuildSampleDocument());
        var numbers = document.RootElement.Compound["Numbers"].List;

        var sum = 0;
        for (var i = 0; i < 1_000; i++)
        {
            sum += numbers[i % numbers.Count].Int32;
            foreach (var element in numbers)
            {
                sum += element.Int32;
            }
        }

        Assert.Equal(7_999, sum);
    }

    [Fact]
    public void CompoundEnumeratorResetRestartsEnumeration()
    {
        using var document = NbtDocument.Parse(BuildSampleDocument());
        var root = document.RootElement.Compound;
        using var enumerator = root.GetEnumerator();

        while (enumerator.MoveNext())
        {
        }

        enumerator.Reset();
        var count = 0;
        var nameLength = 0;
        while (enumerator.MoveNext())
        {
            count++;
            nameLength += enumerator.Current.Key.Length;
        }

        Assert.Equal(root.Count, count);
        Assert.Equal(28, nameLength);
    }

    [Fact]
    public void ArrayMaterializationReturnsFreshArrays()
    {
        using var document = ParseUnnamedRootDocument(
            static (ref writer) => writer.WriteInt32Array("ignored", [1, 2, 3]),
            out var root);
        _ = root.IntArray.ToArray();

        var first = root.IntArray.ToArray();
        var second = root.IntArray.ToArray();

        Assert.NotSame(first, second);
        Assert.Equal([1, 2, 3], first);
        Assert.Equal([1, 2, 3], second);
    }

    // --------------------------------------------------------------------------------------------
    // Parse-time validation
    // --------------------------------------------------------------------------------------------

    [Fact]
    public void UnknownTagTypeThrowsAtParse()
    {
        var exception = Assert.Throws<NbtException>(() => NbtDocument.Parse(new byte[] { 0x0D, 0x00, 0x00 }));

        Assert.Contains("Unknown NBT tag type 13", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, exception.BytePosition);
        Assert.False(exception.IsTruncated);
    }

    [Fact]
    public void RootTagEndThrowsAtParse()
    {
        var exception = Assert.Throws<NbtException>(() => NbtDocument.Parse(new byte[] { 0x00 }));

        Assert.Contains("The root tag cannot be TAG_End", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, exception.BytePosition);
        Assert.Equal(NbtTagType.End, exception.ActualTagType);
    }

    [Fact]
    public void NegativeListLengthThrowsAtParse()
    {
        var exception = Assert.Throws<NbtException>(() => NbtDocument.Parse(
            new byte[] { 0x09, 0x03, 0xFF, 0xFF, 0xFF, 0xFF },
            UnnamedOptions));

        Assert.Contains("Invalid NBT list length -1", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, exception.BytePosition);
    }

    [Fact]
    public void ExcessiveListLengthThrowsAtParse()
    {
        var exception = Assert.Throws<NbtException>(() => NbtDocument.Parse(
            BuildListDocument(3),
            new NbtReaderOptions { RootFormat = NbtRootFormat.Unnamed, MaxCollectionLength = 2 }));

        Assert.Contains("exceeds the configured limit of 2", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, exception.BytePosition);
    }

    [Theory]
    [InlineData(NbtTagType.IntArray)]
    [InlineData(NbtTagType.LongArray)]
    public void NegativeArrayLengthThrowsAtParse(NbtTagType tag)
    {
        var exception = Assert.Throws<NbtException>(() => NbtDocument.Parse(
            new byte[] { (byte)tag, 0xFF, 0xFF, 0xFF, 0xFF },
            UnnamedOptions));

        Assert.Contains("Invalid NBT array length -1", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, exception.BytePosition);
    }

    [Theory]
    [InlineData(NbtTagType.IntArray)]
    [InlineData(NbtTagType.LongArray)]
    public void ExcessiveArrayLengthThrowsAtParse(NbtTagType tag)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer, new NbtWriterOptions { RootFormat = NbtRootFormat.Unnamed });
        if (tag == NbtTagType.IntArray)
        {
            writer.WriteInt32Array("ignored", new int[3]);
        }
        else
        {
            writer.WriteInt64Array("ignored", new long[3]);
        }

        writer.Complete();

        var exception = Assert.Throws<NbtException>(() => NbtDocument.Parse(
            buffer.WrittenSpan.ToArray(),
            new NbtReaderOptions { RootFormat = NbtRootFormat.Unnamed, MaxCollectionLength = 2 }));

        Assert.Contains("Invalid NBT array length 3 (limit 2)", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, exception.BytePosition);
    }

    [Fact]
    public void ListOfEndWithNonZeroLengthThrowsAtParse()
    {
        var exception = Assert.Throws<NbtException>(() => NbtDocument.Parse(
            new byte[] { 0x09, 0x00, 0x00, 0x00, 0x00, 0x01 },
            UnnamedOptions));

        Assert.Contains("A list of TAG_End must have length zero", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, exception.BytePosition);
    }

    [Fact]
    public void DeclaredListLengthBeyondAvailableDataThrowsAtParse()
    {
        var exception = Assert.Throws<NbtException>(() => NbtDocument.Parse(
            new byte[] { 0x09, 0x03, 0x00, 0x00, 0x03, 0xE8, 0x00 },
            UnnamedOptions));

        Assert.True(exception.IsTruncated);
        Assert.Contains("declares 1000 elements", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, exception.BytePosition);
    }

    [Theory]
    [InlineData(NbtTagType.IntArray)]
    [InlineData(NbtTagType.LongArray)]
    public void DeclaredArrayLengthBeyondAvailableDataThrowsAtParse(NbtTagType tag)
    {
        var exception = Assert.Throws<NbtException>(() => NbtDocument.Parse(
            new byte[] { (byte)tag, 0x00, 0x00, 0x03, 0xE8, 0x00 },
            UnnamedOptions));

        Assert.True(exception.IsTruncated);
        var declaredBytes = tag == NbtTagType.IntArray ? 4000 : 8000;
        Assert.Contains($"declares {declaredBytes} bytes", exception.Message, StringComparison.Ordinal);
        Assert.Equal(5, exception.BytePosition);
    }

    [Theory]
    [MemberData(nameof(TruncationLengths))]
    public void TruncationAtEveryPositionThrowsAtParse(int length)
    {
        var exception = Assert.Throws<NbtException>(() =>
            NbtDocument.Parse(SampleDocument.AsMemory(0, length)));

        Assert.True(exception.IsTruncated);
    }

    [Fact]
    public void UnterminatedCompoundThrowsAtParse()
    {
        // TAG_Compound "root" with no end tag.
        var exception = Assert.Throws<NbtException>(() => NbtDocument.Parse(
            new byte[] { 0x0A, 0x00, 0x04, (byte)'r', (byte)'o', (byte)'o', (byte)'t' }));

        Assert.True(exception.IsTruncated);
        Assert.Contains("Unexpected end of NBT data", exception.Message, StringComparison.Ordinal);
        Assert.Equal(7, exception.BytePosition);
    }

    [Fact]
    public void ExcessiveDepthThrowsAtParse()
    {
        var data = BuildDeepDocument(3);

        var exception = Assert.Throws<NbtException>(() =>
            NbtDocument.Parse(data, new NbtReaderOptions { MaxDepth = 2 }));

        Assert.Contains("nesting depth exceeds the limit of 2", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidOptionsAreRejected()
    {
        var data = BuildSampleDocument();
        var invalidRootFormat = new NbtReaderOptions { RootFormat = (NbtRootFormat)42 };

        Assert.Throws<ArgumentOutOfRangeException>(() => NbtDocument.Parse(data, invalidRootFormat));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            NbtDocument.Parse(new ReadOnlySequence<byte>(data), invalidRootFormat));
        using (var stream = new MemoryStream(data))
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => NbtDocument.Parse(stream, invalidRootFormat));
        }

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            NbtDocument.Parse(data, new NbtReaderOptions { MaxDepth = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            NbtDocument.Parse(data, new NbtReaderOptions { MaxCollectionLength = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            NbtDocument.Parse(data, new NbtReaderOptions { MaxDocumentBytes = -1 }));
    }

    // --------------------------------------------------------------------------------------------
    // Document limits and input strategy
    // --------------------------------------------------------------------------------------------

    [Fact]
    public void MemoryOverloadEnforcesTheDocumentLimit()
    {
        var data = NbtSerializer.Serialize(42, "root");

        var exception = Assert.Throws<NbtException>(() => NbtDocument.Parse(data, new NbtReaderOptions
        {
            MaxDocumentBytes = data.Length - 1,
        }));

        Assert.Contains("exceeds the configured limit", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TrailingBytesDoNotCountTowardsTheDocumentLimitOnMemory()
    {
        var data = NbtSerializer.Serialize(42, "root");
        var padded = new byte[data.Length + 32];
        data.CopyTo(padded, 0);

        using var document = NbtDocument.Parse(padded, new NbtReaderOptions
        {
            MaxDocumentBytes = data.Length,
        });

        Assert.Equal(42, document.RootElement.Int32);
    }

    [Fact]
    public void SequenceCopyIsClampedToTheDocumentLimitAndPaddingIsIgnored()
    {
        var data = NbtSerializer.Serialize(42, "root");
        var padded = new byte[data.Length + 32];
        data.CopyTo(padded, 0);

        using var document = NbtDocument.Parse(
            new ReadOnlySequence<byte>(padded),
            new NbtReaderOptions { MaxDocumentBytes = data.Length });

        Assert.Equal(42, document.RootElement.Int32);
    }

    [Fact]
    public void SequenceTruncationWithinTheAllowanceIsRealTruncation()
    {
        // A string root that declares two payload bytes but supplies one; the sequence is far
        // below the allowance, so the truncation is genuine.
        var data = new byte[] { 0x08, 0x00, 0x02, 0x41 };

        var exception = Assert.Throws<NbtException>(() => NbtDocument.Parse(
            new ReadOnlySequence<byte>(data),
            new NbtReaderOptions { RootFormat = NbtRootFormat.Unnamed, MaxDocumentBytes = 50 }));

        Assert.True(exception.IsTruncated);
    }

    [Fact]
    public void SequenceTruncationAtTheAllowanceReportsTheDocumentLimit()
    {
        // A list of two compounds whose first member fills exactly the allowance; the second
        // element starves only because the copy stopped at the allowance.
        var data = new byte[]
        {
            0x09, 0x0A, 0x00, 0x00, 0x00, 0x02, // unnamed root: TAG_List of TAG_Compound, 2 elements
            0x01, 0x00, 0x01, (byte)'a', 0x05,  // first compound: TAG_Byte "a" = 5 (no end tag yet)
        };

        var exception = Assert.Throws<NbtException>(() => NbtDocument.Parse(
            new ReadOnlySequence<byte>(data),
            new NbtReaderOptions { RootFormat = NbtRootFormat.Unnamed, MaxDocumentBytes = 11 }));

        Assert.Contains("exceeds the configured limit of 11 bytes", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SequenceLongerThanTheAllowanceThrowsWhenTheDocumentNeedsMore()
    {
        var data = NbtSerializer.Serialize(42, "root");
        var sequence = new ReadOnlySequence<byte>(Pad(data, data.Length + 16));

        using var document = NbtDocument.Parse(sequence, new NbtReaderOptions
        {
            MaxDocumentBytes = data.Length,
        });

        Assert.Equal(42, document.RootElement.Int32);
        Assert.Throws<NbtException>(() => NbtDocument.Parse(
            new ReadOnlySequence<byte>(Pad(data, data.Length + 16)),
            new NbtReaderOptions { MaxDocumentBytes = data.Length - 1 }));
    }

    [Fact]
    public void StreamReadsAreBoundedByTheDocumentLimitAndPaddingIsIgnored()
    {
        var data = NbtSerializer.Serialize(42, "root");
        var padded = Pad(data, data.Length + 32);
        using var stream = new MemoryStream(padded);

        using var document = NbtDocument.Parse(stream, new NbtReaderOptions
        {
            MaxDocumentBytes = data.Length,
        });

        Assert.Equal(42, document.RootElement.Int32);
    }

    [Fact]
    public void StreamTruncationWithinTheAllowanceIsRealTruncation()
    {
        var data = new byte[] { 0x0A, 0x00, 0x04, (byte)'r', (byte)'o', (byte)'o', (byte)'t' };
        using var stream = new MemoryStream(data);

        var exception = Assert.Throws<NbtException>(() =>
            NbtDocument.Parse(stream, new NbtReaderOptions { MaxDocumentBytes = 50 }));

        Assert.True(exception.IsTruncated);
    }

    [Fact]
    public void StreamTruncationAtTheAllowanceReportsTheDocumentLimit()
    {
        var data = new byte[]
        {
            0x09, 0x0A, 0x00, 0x00, 0x00, 0x02,
            0x01, 0x00, 0x01, (byte)'a', 0x05,
        };
        using var stream = new MemoryStream(data);

        var exception = Assert.Throws<NbtException>(() =>
            NbtDocument.Parse(stream, new NbtReaderOptions
            {
                RootFormat = NbtRootFormat.Unnamed,
                MaxDocumentBytes = 11,
            }));

        Assert.Contains("exceeds the configured limit of 11 bytes", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void StreamLimitBelowTheDocumentSizeThrowsWithoutReadingPastTheLimit()
    {
        var data = NbtSerializer.Serialize(42, "root");
        using var stream = new MemoryStream(data);

        Assert.Throws<NbtException>(() =>
            NbtDocument.Parse(stream, new NbtReaderOptions { MaxDocumentBytes = data.Length - 1 }));
    }

    // --------------------------------------------------------------------------------------------
    // Stream buffering behavior
    // --------------------------------------------------------------------------------------------

    [Fact]
    public void StreamOfUnknownLengthGrowsTheBufferUntilTheDocumentFits()
    {
        var data = BuildLargeDocument(); // well past the 64 KiB initial buffer

        using var stream = new NonSeekableStream(data);
        using var document = NbtDocument.Parse(stream);

        var root = document.RootElement.Compound;
        Assert.Equal(100_000, root["Data"].ByteArray.Length);
    }

    [Fact]
    public void StreamThatOverReportsItsLengthParsesTheActualData()
    {
        var data = BuildSampleDocument();
        using var stream = new OverReportingStream(data, 1024);

        using var document = NbtDocument.Parse(stream, new NbtReaderOptions { MaxDocumentBytes = 10_000 });

        Assert.Equal(20, document.RootElement.Compound["Health"].Int32);
    }

    [Fact]
    public void MalformedStreamDataThrowsImmediately()
    {
        using var stream = new NonSeekableStream([0x0D, 0x00, 0x00]);

        var exception = Assert.Throws<NbtException>(() => NbtDocument.Parse(stream));
        Assert.False(exception.IsTruncated);
        Assert.Contains("Unknown NBT tag type 13", exception.Message, StringComparison.Ordinal);
    }

    // --------------------------------------------------------------------------------------------
    // Lifetime
    // --------------------------------------------------------------------------------------------

    [Fact]
    public void DisposedDocumentThrowsOnRootAccess()
    {
        var document = NbtDocument.Parse(BuildSampleDocument());
        document.Dispose();

        Assert.Throws<ObjectDisposedException>(() => document.RootElement);
        Assert.Throws<ObjectDisposedException>(() => _ = document.RootName);
    }

    [Fact]
    public void StringViewThrowsAfterDispose()
    {
        var document = NbtDocument.Parse(BuildSampleDocument());
        var view = document.RootElement.Compound["Name"].String;
        document.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _ = view.ByteLength);
        Assert.Throws<ObjectDisposedException>(() => _ = view.IsEmpty);
        Assert.Throws<ObjectDisposedException>(() => _ = view.RawSpan.Length);
        Assert.Throws<ObjectDisposedException>(() => _ = view.RawMemory.Length);
        Assert.Throws<ObjectDisposedException>(() => view.GetString());
    }

    [Fact]
    public void ArrayViewsThrowAfterDispose()
    {
        var document = NbtDocument.Parse(BuildAllTagsDocument());
        var root = document.RootElement.Compound;
        var bytes = root["ByteArray"].ByteArray;
        var ints = root["IntArray"].IntArray;
        var longs = root["LongArray"].LongArray;
        document.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _ = bytes.Length);
        Assert.Throws<ObjectDisposedException>(() => _ = bytes.Span.Length);
        Assert.Throws<ObjectDisposedException>(() => bytes.ToArray());
        Assert.Throws<ObjectDisposedException>(() => _ = ints.Length);
        Assert.Throws<ObjectDisposedException>(() => _ = ints.RawSpan.Length);
        Assert.Throws<ObjectDisposedException>(() => ints.ToArray());
        Assert.Throws<ObjectDisposedException>(() => _ = longs.Length);
        Assert.Throws<ObjectDisposedException>(() => _ = longs.RawSpan.Length);
        Assert.Throws<ObjectDisposedException>(() => longs.ToArray());
    }

    [Fact]
    public void ListViewThrowsAfterDispose()
    {
        var document = NbtDocument.Parse(BuildSampleDocument());
        var list = document.RootElement.Compound["Numbers"].List;
        using var enumerator = list.GetEnumerator();
        document.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _ = list.Count);
        Assert.Throws<ObjectDisposedException>(() => _ = list.ElementType);
        Assert.Throws<ObjectDisposedException>(() => list[0]);
        Assert.Throws<ObjectDisposedException>(() => _ = list[0]);
        Assert.Throws<ObjectDisposedException>(() => list.GetEnumerator());
        Assert.Throws<ObjectDisposedException>(() => enumerator.MoveNext());
    }

    [Fact]
    public void CompoundViewThrowsAfterDispose()
    {
        var document = NbtDocument.Parse(BuildSampleDocument());
        var compound = document.RootElement.Compound;
        using var enumerator = compound.GetEnumerator();
        document.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _ = compound.Count);
        Assert.Throws<ObjectDisposedException>(() => compound.TryGetValue("Health", out _));
        Assert.Throws<ObjectDisposedException>(() => compound["Health"]);
        Assert.Throws<ObjectDisposedException>(() => compound.GetEnumerator());
        Assert.Throws<ObjectDisposedException>(() => enumerator.MoveNext());
        Assert.Throws<ObjectDisposedException>(() => _ = enumerator.Current.Key);
    }

    [Fact]
    public void NumericValuesAndTagTypeSurviveDispose()
    {
        var document = NbtDocument.Parse(BuildSampleDocument());
        var root = document.RootElement;
        var health = root.Compound["Health"];
        document.Dispose();

        // TagType is inline metadata and a numeric value is self-contained; neither touches the
        // document after it has been indexed.
        Assert.Equal(NbtTagType.Compound, root.TagType);
        Assert.Equal(NbtTagType.Int, health.TagType);
        Assert.Equal(20, health.Int32);

        var numberDocument = NbtDocument.Parse(NbtSerializer.Serialize(42, "root"));
        var number = numberDocument.RootElement;
        numberDocument.Dispose();

        Assert.Equal(NbtTagType.Int, number.TagType);
        Assert.Equal(42, number.Int32);
    }

    [Fact]
    public void DisposeIsIdempotent()
    {
        var document = NbtDocument.Parse(BuildSampleDocument());

        document.Dispose();
        document.Dispose();

        Assert.Throws<ObjectDisposedException>(() => document.RootElement);
    }

    // --------------------------------------------------------------------------------------------
    // Helpers
    // --------------------------------------------------------------------------------------------

    private static string ParseString(byte[] data)
    {
        using var document = NbtDocument.Parse(data, UnnamedOptions);
        return document.RootElement.String.GetString();
    }

    private static NbtDocument ParseUnnamedRootDocument(RootWriter writeValue, out NbtElement root)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer, new NbtWriterOptions { RootFormat = NbtRootFormat.Unnamed });
        writeValue(ref writer);
        writer.Complete();
        var document = NbtDocument.Parse(buffer.WrittenSpan.ToArray(), UnnamedOptions);
        root = document.RootElement;
        return document;
    }

    private static NbtElement RequireMember(NbtCompoundElement compound, string name)
    {
        Assert.True(compound.TryGetValue(name, out var value), $"The compound has no member '{name}'.");
        return value;
    }

    private static int[] ReadIntElements(NbtListElement list)
    {
        var values = new int[list.Count];
        var index = 0;
        foreach (var element in list)
        {
            values[index++] = element.Int32;
        }

        return values;
    }

    private static string[] ReadMemberNames(NbtCompoundElement compound)
    {
        var names = new string[compound.Count];
        var index = 0;
        using var enumerator = compound.GetEnumerator();
        while (enumerator.MoveNext())
        {
            names[index++] = enumerator.Current.Key;
        }

        return names;
    }

    private static string[] ReadNestedMemberNames(NbtListElement list)
    {
        var names = new List<string>();
        foreach (var element in list)
        {
            using var enumerator = element.Compound.GetEnumerator();
            while (enumerator.MoveNext())
            {
                names.Add(enumerator.Current.Key);
            }
        }

        return names.ToArray();
    }

    private static void AssertDefaultCaseFailure(Func<object?> read, NbtTagType expected)
    {
        var exception = Assert.Throws<NbtException>(read);
        Assert.Equal(expected, exception.ExpectedTagType);
        Assert.Null(exception.ActualTagType);
    }

    private static byte[] BuildUnnamedRoot(NbtTagType tag)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer, new NbtWriterOptions { RootFormat = NbtRootFormat.Unnamed });
        WriteMember(ref writer, "value", tag);
        writer.Complete();
        return buffer.WrittenSpan.ToArray();
    }

    private static object? ReadCaseProperty(NbtElement element, NbtTagType tag) => tag switch
    {
        NbtTagType.Byte => element.SByte,
        NbtTagType.Short => element.Int16,
        NbtTagType.Int => element.Int32,
        NbtTagType.Long => element.Int64,
        NbtTagType.Float => element.Single,
        NbtTagType.Double => element.Double,
        NbtTagType.ByteArray => element.ByteArray.ToArray(),
        NbtTagType.String => element.String.GetString(),
        NbtTagType.List => element.List,
        NbtTagType.Compound => element.Compound,
        NbtTagType.IntArray => element.IntArray.ToArray(),
        NbtTagType.LongArray => element.LongArray.ToArray(),
        _ => throw new ArgumentOutOfRangeException(nameof(tag)),
    };

    private static void WriteMember(ref NbtWriter writer, string name, NbtTagType tag)
    {
        switch (tag)
        {
            case NbtTagType.Byte:
                writer.WriteSByte(name, -5);
                break;
            case NbtTagType.Short:
                writer.WriteInt16(name, 12345);
                break;
            case NbtTagType.Int:
                writer.WriteInt32(name, -123456789);
                break;
            case NbtTagType.Long:
                writer.WriteInt64(name, 1234567890123L);
                break;
            case NbtTagType.Float:
                writer.WriteSingle(name, 0.5f);
                break;
            case NbtTagType.Double:
                writer.WriteDouble(name, -0.25);
                break;
            case NbtTagType.ByteArray:
                writer.WriteByteArray(name, [1, 2, 3]);
                break;
            case NbtTagType.String:
                writer.WriteString(name, "中文 \0 x");
                break;
            case NbtTagType.List:
                writer.WriteStartList(name, NbtTagType.String, 2);
                writer.WriteString("a");
                writer.WriteString("b");
                writer.WriteEndList();
                break;
            case NbtTagType.Compound:
                writer.WriteStartCompound(name);
                writer.WriteInt32("Nested", 1);
                writer.WriteEndCompound();
                break;
            case NbtTagType.IntArray:
                writer.WriteInt32Array(name, [int.MinValue, -1, int.MaxValue]);
                break;
            case NbtTagType.LongArray:
                writer.WriteInt64Array(name, [long.MinValue, long.MaxValue]);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(tag));
        }
    }

    private static byte[] BuildSampleDocument()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer);
        writer.WriteStartCompound("root");
        writer.WriteInt32("Health", 20);
        writer.WriteDouble("Temperature", -3.5);
        writer.WriteString("Name", "a long string value");
        writer.WriteStartList("Numbers", NbtTagType.Int, 3);
        writer.WriteInt32(1);
        writer.WriteInt32(2);
        writer.WriteInt32(3);
        writer.WriteEndList();
        writer.WriteEndCompound();
        writer.Complete();
        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] BuildAllTagsDocument()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer);
        writer.WriteStartCompound("root");
        foreach (var tag in AllTags)
        {
            WriteMember(ref writer, tag.ToString(), tag);
        }

        writer.WriteEndCompound();
        writer.Complete();
        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] BuildLayoutDocument()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer);
        writer.WriteStartCompound("root");
        writer.WriteStartCompound("A");
        writer.WriteInt32("x", 1);
        writer.WriteEndCompound();
        writer.WriteStartList("Items", NbtTagType.Compound, 3);
        writer.WriteStartCompound();
        writer.WriteInt32("x", 10);
        writer.WriteEndCompound();
        writer.WriteStartCompound();
        writer.WriteInt32("y", 20);
        writer.WriteEndCompound();
        writer.WriteStartCompound();
        writer.WriteInt32("x", 30);
        writer.WriteEndCompound();
        writer.WriteEndList();
        writer.WriteInt32("B", 9);
        writer.WriteEndCompound();
        writer.Complete();
        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] BuildDuplicateMemberDocument()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer);
        writer.WriteStartCompound("root");
        writer.WriteInt32("Dup", 1);
        writer.WriteInt32("Dup", 2);
        writer.WriteInt32("Other", 3);
        writer.WriteEndCompound();
        writer.Complete();
        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] BuildEmptyListDocument()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer);
        writer.WriteStartCompound("root");
        writer.WriteStartList("EmptyEnd", NbtTagType.End, 0);
        writer.WriteEndList();
        writer.WriteStartList("EmptyInts", NbtTagType.Int, 0);
        writer.WriteEndList();
        writer.WriteEndCompound();
        writer.Complete();
        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] BuildEmptyNamedRoot()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer);
        writer.WriteStartCompound(string.Empty);
        writer.WriteInt32("Value", 7);
        writer.WriteEndCompound();
        writer.Complete();
        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] BuildUnnamedDocument()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer, new NbtWriterOptions { RootFormat = NbtRootFormat.Unnamed });
        writer.WriteStartCompound("ignored");
        writer.WriteInt32("Value", 7);
        writer.WriteEndCompound();
        writer.Complete();
        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] BuildDocumentWithByteMemberName(byte[] memberName)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer);
        writer.WriteStartCompound("root");
        writer.WriteSByte(memberName, 7);
        writer.WriteEndCompound();
        writer.Complete();
        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] BuildLongMemberNameDocument(string memberName)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer);
        writer.WriteStartCompound("root");
        writer.WriteInt32(memberName, 7);
        writer.WriteEndCompound();
        writer.Complete();
        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] BuildListDocument(int count)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer, new NbtWriterOptions { RootFormat = NbtRootFormat.Unnamed });
        writer.WriteStartList("ignored", NbtTagType.Int, count);
        for (var i = 0; i < count; i++)
        {
            writer.WriteInt32(i);
        }

        writer.WriteEndList();
        writer.Complete();
        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] BuildDeepDocument(int depth)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer);
        writer.WriteStartCompound("root");
        for (var i = 1; i < depth; i++)
        {
            writer.WriteStartCompound("N");
        }

        writer.WriteInt32("Value", 1);
        for (var i = 1; i < depth; i++)
        {
            writer.WriteEndCompound();
        }

        writer.WriteEndCompound();
        writer.Complete();
        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] BuildLargeDocument()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer);
        writer.WriteStartCompound("root");
        writer.WriteByteArray("Data", new byte[100_000]);
        writer.WriteEndCompound();
        writer.Complete();
        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] Pad(byte[] data, int length)
    {
        var padded = new byte[length];
        data.CopyTo(padded, 0);
        return padded;
    }

    private static ReadOnlySequence<byte> CreateSegmentedSequence(byte[] data, int segmentLength)
    {
        Segment? first = null;
        Segment? current = null;
        for (var offset = 0; offset < data.Length; offset += segmentLength)
        {
            var take = Math.Min(segmentLength, data.Length - offset);
            current = current is null
                ? new Segment(data.AsMemory(offset, take))
                : current.Append(data.AsMemory(offset, take));
            first ??= current;
        }

        if (first is null)
        {
            return ReadOnlySequence<byte>.Empty;
        }

        return new ReadOnlySequence<byte>(first, 0, current!, current!.Memory.Length);
    }

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(ReadOnlyMemory<byte> memory)
        {
            Memory = memory;
        }

        public Segment Append(ReadOnlyMemory<byte> memory)
        {
            var next = new Segment(memory) { RunningIndex = RunningIndex + Memory.Length };
            Next = next;
            return next;
        }
    }

    private sealed class NonSeekableStream(byte[] data) : Stream
    {
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var remaining = data.Length - _position;
            if (remaining == 0)
            {
                return 0;
            }

            var take = Math.Min(count, remaining);
            Buffer.BlockCopy(data, _position, buffer, offset, take);
            _position += take;
            return take;
        }

        public override void Flush() => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class OverReportingStream(byte[] data, long overReport) : Stream
    {
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => data.Length + overReport;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var remaining = data.Length - _position;
            if (remaining == 0)
            {
                return 0;
            }

            var take = Math.Min(count, remaining);
            Buffer.BlockCopy(data, _position, buffer, offset, take);
            _position += take;
            return take;
        }

        public override void Flush() => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

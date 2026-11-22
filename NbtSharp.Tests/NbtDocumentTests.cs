using System.Buffers;
using System.Collections;

namespace NbtSharp.Tests;

public sealed class NbtDocumentTests
{
    // --------------------------------------------------------------------------------------------
    // Parse overloads and the root surface
    // --------------------------------------------------------------------------------------------

    [Fact]
    public void ParsesFromMemory()
    {
        var data = BuildSampleDocument();
        using var document = NbtDocument.Parse(data);

        Assert.Equal("root", document.RootName);
        Assert.Equal(NbtTagType.Compound, document.RootElement.TagType);
    }

    [Fact]
    public void ParsesFromByteArrayThroughTheMemoryOverload()
    {
        var data = BuildSampleDocument();
        using var document = NbtDocument.Parse(data);

        Assert.Equal(20, document.RootElement.GetMember("Health").GetInt32());
        Assert.Equal("a long string value", document.RootElement.GetMember("Name").GetString());
    }

    [Fact]
    public void ParsesFromSingleSegmentSequence()
    {
        var data = BuildSampleDocument();
        var sequence = new ReadOnlySequence<byte>(data);

        using var document = NbtDocument.Parse(sequence);

        Assert.Equal(20, document.RootElement.GetMember("Health").GetInt32());
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

        var root = document.RootElement;
        Assert.Equal(20, root.GetMember("Health").GetInt32());
        Assert.Equal(-3.5, root.GetMember("Temperature").GetDouble());
        Assert.Equal("a long string value", root.GetMember("Name").GetString());
        Assert.Equal(
            [1, 2, 3],
            root.GetMember("Numbers").EnumerateElements().Select(element => element.GetInt32()).ToArray());
    }

    [Fact]
    public void ParsesFromStream()
    {
        using var stream = new MemoryStream(BuildSampleDocument());

        using var document = NbtDocument.Parse(stream);

        Assert.Equal(20, document.RootElement.GetMember("Health").GetInt32());
    }

    [Fact]
    public void EmptyInputsAreTruncatedAtParse()
    {
        Assert.Throws<NbtException>(() => NbtDocument.Parse(ReadOnlyMemory<byte>.Empty));
        Assert.Throws<NbtException>(() =>
            NbtDocument.Parse(ReadOnlySequence<byte>.Empty));
        using var stream = new MemoryStream();
        Assert.Throws<NbtException>(() => NbtDocument.Parse(stream));
    }

    [Fact]
    public void RootNameDistinguishesNamedEmptyAndUnnamedRoots()
    {
        using var named = NbtDocument.Parse(BuildSampleDocument());
        using var emptyNamed = NbtDocument.Parse(BuildEmptyNamedRoot());
        using var unnamed = NbtDocument.Parse(
            BuildUnnamedDocument(),
            new NbtReaderOptions { RootFormat = NbtRootFormat.Unnamed });

        Assert.Equal("root", named.RootName);
        Assert.Equal(string.Empty, emptyNamed.RootName);
        Assert.Null(unnamed.RootName);
        Assert.Equal(string.Empty, emptyNamed.RootElement.Name);
        Assert.Null(unnamed.RootElement.Name);
    }

    [Fact]
    public void RootElementNameMatchesTheReaderRootTokenName()
    {
        var data = BuildSampleDocument();
        using var document = NbtDocument.Parse(data);

        Assert.Equal(document.RootName, document.RootElement.Name);
    }

    // --------------------------------------------------------------------------------------------
    // Scalar and array getters
    // --------------------------------------------------------------------------------------------

    [Fact]
    public void ReadsAllTwelveTagTypes()
    {
        var data = BuildAllTagsDocument();
        using var document = NbtDocument.Parse(data);
        var root = document.RootElement;

        Assert.Equal(-5, root.GetMember("Byte").GetSByte());
        Assert.Equal((short)12345, root.GetMember("Short").GetInt16());
        Assert.Equal(-123456789, root.GetMember("Int").GetInt32());
        Assert.Equal(1234567890123L, root.GetMember("Long").GetInt64());
        Assert.Equal(0.5f, root.GetMember("Float").GetSingle());
        Assert.Equal(-0.25, root.GetMember("Double").GetDouble());
        Assert.Equal(new byte[] { 1, 2, 3 }, root.GetMember("ByteArray").GetByteArray());
        Assert.Equal(new sbyte[] { 1, 2, 3 }, root.GetMember("ByteArray").GetSByteArray());
        Assert.Equal("中文 \0 x", root.GetMember("String").GetString());
        Assert.Equal(new[] { int.MinValue, -1, int.MaxValue }, root.GetMember("IntArray").GetInt32Array());
        Assert.Equal(new[] { long.MinValue, long.MaxValue }, root.GetMember("LongArray").GetInt64Array());
        Assert.Equal(NbtTagType.List, root.GetMember("List").TagType);
        Assert.Equal(NbtTagType.Compound, root.GetMember("Compound").TagType);
    }

    [Fact]
    public void TypeMismatchReportsExpectedActualAndPosition()
    {
        var data = BuildSampleDocument();
        using var document = NbtDocument.Parse(data);
        var health = document.RootElement.GetMember("Health");

        var exception = Assert.Throws<NbtException>(() => health.GetString());
        Assert.Equal(NbtTagType.String, exception.ExpectedTagType);
        Assert.Equal(NbtTagType.Int, exception.ActualTagType);
        Assert.NotNull(exception.BytePosition);
    }

    [Fact]
    public void ContainerTypeMismatchReportsExpectedActualAndPosition()
    {
        var data = BuildSampleDocument();
        using var document = NbtDocument.Parse(data);

        var exception = Assert.Throws<NbtException>(() => document.RootElement.ElementCount);
        Assert.Equal(NbtTagType.List, exception.ExpectedTagType);
        Assert.Equal(NbtTagType.Compound, exception.ActualTagType);
        Assert.NotNull(exception.BytePosition);

        exception = Assert.Throws<NbtException>(() => document.RootElement.GetMember("Numbers").MemberCount);
        Assert.Equal(NbtTagType.Compound, exception.ExpectedTagType);
        Assert.Equal(NbtTagType.List, exception.ActualTagType);
        Assert.NotNull(exception.BytePosition);
    }

    [Fact]
    public void GettersCanBeCalledRepeatedly()
    {
        var data = BuildAllTagsDocument();
        using var document = NbtDocument.Parse(data);
        var root = document.RootElement;
        var member = root.GetMember("Int");

        Assert.Equal(-123456789, member.GetInt32());
        Assert.Equal(-123456789, member.GetInt32());
        Assert.Equal("中文 \0 x", root.GetMember("String").GetString());
        Assert.Equal("中文 \0 x", root.GetMember("String").GetString());
        Assert.Equal(new byte[] { 1, 2, 3 }, root.GetMember("ByteArray").GetByteArray());
        Assert.Equal(new byte[] { 1, 2, 3 }, root.GetMember("ByteArray").GetByteArray());
    }

    [Fact]
    public void GetStringDecodesModifiedUtf8AtAccessTime()
    {
        // An unnamed TAG_String root: tag, length prefix, then payload.
        Assert.Equal(string.Empty, ParseString([0x08, 0x00, 0x00]));
        Assert.Equal("\0", ParseString([0x08, 0x00, 0x02, 0xC0, 0x80]));
        Assert.Equal("\U0001F600", ParseString([0x08, 0x00, 0x06, 0xED, 0xA0, 0xBD, 0xED, 0xB8, 0x80]));
    }

    [Fact]
    public void GetStringRejectsF0LeadBytesAtAccessTimeWithThePayloadPosition()
    {
        var data = new byte[] { 0x08, 0x00, 0x02, 0xF0, 0x41 };
        using var document = NbtDocument.Parse(data, new NbtReaderOptions { RootFormat = NbtRootFormat.Unnamed });

        // The payload (length prefix included) starts right after the tag byte; indexing accepted
        // the payload because it only checks the length prefix.
        var exception = Assert.Throws<NbtException>(() => document.RootElement.GetString());
        Assert.Equal(1, exception.BytePosition);
    }

    [Fact]
    public void GetRawBytesReturnsTheCompletePayload()
    {
        var data = new byte[] { 0x08, 0x00, 0x06, 0xED, 0xA0, 0xBD, 0xED, 0xB8, 0x80 };
        using var document = NbtDocument.Parse(data, new NbtReaderOptions { RootFormat = NbtRootFormat.Unnamed });

        Assert.Equal(
            new byte[] { 0x00, 0x06, 0xED, 0xA0, 0xBD, 0xED, 0xB8, 0x80 },
            document.RootElement.GetRawBytes());
    }

    // --------------------------------------------------------------------------------------------
    // Names
    // --------------------------------------------------------------------------------------------

    [Fact]
    public void NameIsOnlyPresentOnCompoundMembersAndNamedRoots()
    {
        var data = BuildSampleDocument();
        using var document = NbtDocument.Parse(data);
        var root = document.RootElement;

        Assert.Equal("root", root.Name);
        Assert.Equal("Health", root.GetMember("Health").Name);
        foreach (var element in root.GetMember("Numbers").EnumerateElements())
        {
            Assert.Null(element.Name);
        }
    }

    [Fact]
    public void NameEqualsMatchesCanonicalBytes()
    {
        var data = BuildSampleDocument();
        using var document = NbtDocument.Parse(data);
        var root = document.RootElement;

        Assert.True(root.NameEquals("root"u8));
        Assert.True(root.GetMember("Health").NameEquals("Health"u8));
        Assert.False(root.NameEquals("roo"u8)); // shorter
        Assert.False(root.NameEquals("roots"u8)); // longer
        Assert.False(root.NameEquals("health"u8));
        Assert.False(root.GetMember("Numbers")[0].NameEquals("Numbers"u8));
    }

    [Theory]
    [InlineData(new byte[] { 0x00 }, "\0", new byte[] { 0xC0, 0x80 }, true)] // wire raw null, canonical query
    [InlineData(new byte[] { 0xC0, 0x80 }, "\0", new byte[] { 0xC0, 0x80 }, true)] // byte fast path
    [InlineData(new byte[] { 0xC0, 0x80 }, "\0", new byte[] { 0x00 }, true)] // both encode U+0000
    [InlineData(new byte[] { 0x41 }, "A", new byte[] { 0xE0, 0x81, 0x81 }, false)] // canonical wire never falls back
    [InlineData(new byte[] { 0x41 }, "A", new byte[] { 0x41 }, true)]
    [InlineData(new byte[] { 0xE0, 0x81, 0x81 }, "A", new byte[] { 0x41 }, true)] // overlong three-byte vs 'A'
    [InlineData(new byte[] { 0xC1, 0x81 }, "A", new byte[] { 0x41 }, true)] // overlong two-byte vs 'A'
    [InlineData(new byte[] { 0xE0, 0x81, 0x81 }, "A", new byte[] { 0xC1, 0x81 }, true)] // both non-canonical
    [InlineData(new byte[] { 0x00 }, "\0", new byte[] { 0x41 }, false)] // decoded values differ
    public void NameEqualsComparesNonCanonicalWireNamesByDecodedValue(
        byte[] wireName,
        string lookupName,
        byte[] queryName,
        bool expected)
    {
        var data = BuildDocumentWithByteMemberName(wireName);
        using var document = NbtDocument.Parse(data);

        var member = document.RootElement.GetMember(lookupName);

        Assert.Equal(expected, member.NameEquals(queryName));
        Assert.False(member.NameEquals("B"u8));
    }

    [Fact]
    public void NameIsDecodedEveryTimeWithoutCaching()
    {
        var data = BuildSampleDocument();
        using var document = NbtDocument.Parse(data);
        var member = document.RootElement.GetMember("Health");

        Assert.Equal("Health", member.Name);
        Assert.Equal("Health", member.Name);
    }

    // --------------------------------------------------------------------------------------------
    // Member lookup
    // --------------------------------------------------------------------------------------------

    [Fact]
    public void TryGetMemberFindsAndMissesMembers()
    {
        var data = BuildSampleDocument();
        using var document = NbtDocument.Parse(data);
        var root = document.RootElement;

        Assert.True(root.TryGetMember("Health", out var health));
        Assert.Equal(20, health.GetInt32());
        Assert.False(root.TryGetMember("Missing", out var missing));
        Assert.Throws<NbtException>(() => root.GetMember("Numbers").TryGetMember("Health", out _));
    }

    [Fact]
    public void DuplicateMemberNamesResolveLastWins()
    {
        var data = BuildDuplicateMemberDocument();
        using var document = NbtDocument.Parse(data);
        var root = document.RootElement;

        Assert.Equal(3, root.MemberCount);
        Assert.Equal(2, root.GetMember("Dup").GetInt32());
        Assert.True(root.TryGetMember("Dup", out var dup));
        Assert.Equal(2, dup.GetInt32());
    }

    [Fact]
    public void EnumerationYieldsEveryDuplicateMember()
    {
        var data = BuildDuplicateMemberDocument();
        using var document = NbtDocument.Parse(data);
        var root = document.RootElement;

        var names = root.EnumerateMembers().Select(member => member.Name).ToArray();
        Assert.Equal(["Dup", "Dup", "Other"], names);
    }

    [Fact]
    public void GetMemberThrowsKeyNotFoundForAMissingName()
    {
        var data = BuildSampleDocument();
        using var document = NbtDocument.Parse(data);

        Assert.Throws<KeyNotFoundException>(() => document.RootElement.GetMember("Missing"));
    }

    [Fact]
    public void TryGetMemberEncodesLongNamesThroughThePool()
    {
        var longName = new string('a', 300);
        var data = BuildLongMemberNameDocument(longName);
        using var document = NbtDocument.Parse(data);

        Assert.True(document.RootElement.TryGetMember(longName, out var member));
        Assert.Equal(7, member.GetInt32());
        Assert.False(document.RootElement.TryGetMember(new string('a', 299), out _));
        Assert.False(document.RootElement.TryGetMember(new string('a', 301), out _));
    }

    // --------------------------------------------------------------------------------------------
    // Containers, layout, and enumeration order
    // --------------------------------------------------------------------------------------------

    [Fact]
    public void LayoutCounterexampleReportsCountsAndOrderCorrectly()
    {
        // root { A: { x: Int }, B: Int } — the layout that a flat index could get wrong.
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer);
        writer.WriteStartCompound("root");
        writer.WriteStartCompound("A");
        writer.WriteInt32("x", 1);
        writer.WriteEndCompound();
        writer.WriteInt32("B", 9);
        writer.WriteEndCompound();
        writer.Complete();

        using var document = NbtDocument.Parse(buffer.WrittenSpan.ToArray());
        var root = document.RootElement;

        Assert.Equal(2, root.MemberCount);
        var a = root.GetMember("A");
        Assert.Equal(NbtTagType.Compound, a.TagType);
        Assert.Equal(1, a.MemberCount);
        Assert.Equal(9, root.GetMember("B").GetInt32());

        Assert.Equal(["A", "B"], root.EnumerateMembers().Select(member => member.Name).ToArray());
        Assert.Equal(["x"], a.EnumerateMembers().Select(member => member.Name).ToArray());
        Assert.Equal(1, a.GetMember("x").GetInt32());
    }

    [Fact]
    public void ListOfScalarsAddressesElementsInConstantTime()
    {
        var data = BuildSampleDocument();
        using var document = NbtDocument.Parse(data);
        var numbers = document.RootElement.GetMember("Numbers");

        Assert.Equal(NbtTagType.Int, numbers.ElementType);
        Assert.Equal(3, numbers.ElementCount);
        Assert.Equal(1, numbers.GetElementAt(0).GetInt32());
        Assert.Equal(2, numbers.GetElementAt(1).GetInt32());
        Assert.Equal(3, numbers[2].GetInt32());
    }

    [Fact]
    public void ListOfCompoundsWalksToTheRequestedIndex()
    {
        var data = BuildLayoutDocument();
        using var document = NbtDocument.Parse(data);
        var items = document.RootElement.GetMember("Items");

        Assert.Equal(NbtTagType.Compound, items.ElementType);
        Assert.Equal(3, items.ElementCount);
        Assert.Equal(10, items.GetElementAt(0).GetMember("x").GetInt32());
        Assert.Equal(20, items.GetElementAt(1).GetMember("y").GetInt32());
        Assert.Equal(30, items.GetElementAt(2).GetMember("x").GetInt32());
        Assert.Equal(20, items[1].GetMember("y").GetInt32());
    }

    [Fact]
    public void GetElementAtThrowsForOutOfRangeIndices()
    {
        var data = BuildSampleDocument();
        using var document = NbtDocument.Parse(data);
        var numbers = document.RootElement.GetMember("Numbers");

        Assert.Throws<ArgumentOutOfRangeException>(() => numbers.GetElementAt(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => numbers.GetElementAt(3));
    }

    [Fact]
    public void NestedCompoundInListInCompoundEnumeratesInWireOrder()
    {
        var data = BuildLayoutDocument();
        using var document = NbtDocument.Parse(data);
        var root = document.RootElement;

        Assert.Equal(["A", "Items", "B"], root.EnumerateMembers().Select(member => member.Name).ToArray());

        var items = root.GetMember("Items");
        var names = new List<string>();
        foreach (var item in items.EnumerateElements())
        {
            foreach (var member in item.EnumerateMembers())
            {
                names.Add(member.Name!);
            }
        }

        Assert.Equal(["x", "y", "x"], names);
    }

    [Fact]
    public void UnnamedListRootEnumeratesElements()
    {
        var data = BuildListDocument(2);
        using var document = NbtDocument.Parse(
            data,
            new NbtReaderOptions { RootFormat = NbtRootFormat.Unnamed });

        Assert.Null(document.RootName);
        Assert.Equal(NbtTagType.List, document.RootElement.TagType);
        Assert.Equal([0, 1], document.RootElement.EnumerateElements()
            .Select(element => element.GetInt32())
            .ToArray());
    }

    [Fact]
    public void EndTypedListReportsEndElementTypeAndZeroElements()
    {
        var data = BuildEmptyListDocument();
        using var document = NbtDocument.Parse(data);
        var endList = document.RootElement.GetMember("EmptyEnd");

        Assert.Equal(NbtTagType.End, endList.ElementType);
        Assert.Equal(0, endList.ElementCount);
        Assert.Empty(endList.EnumerateElements().ToArray());

        var intList = document.RootElement.GetMember("EmptyInts");
        Assert.Equal(NbtTagType.Int, intList.ElementType);
        Assert.Equal(0, intList.ElementCount);
        Assert.Empty(intList.EnumerateElements().ToArray());
    }

    [Fact]
    public void EnumeratorResetsToTheFirstChild()
    {
        var data = BuildSampleDocument();
        using var document = NbtDocument.Parse(data);
        var numbers = document.RootElement.GetMember("Numbers");

        var enumerator = numbers.EnumerateElements();
        Assert.True(enumerator.MoveNext());
        Assert.Equal(1, enumerator.Current.GetInt32());
        enumerator.Reset();
        Assert.True(enumerator.MoveNext());
        Assert.Equal(1, enumerator.Current.GetInt32());
        Assert.True(enumerator.MoveNext());
        Assert.True(enumerator.MoveNext());
        Assert.False(enumerator.MoveNext());
        Assert.False(enumerator.MoveNext());
    }

    [Fact]
    public void EnumeratorSupportsIEnumerableUse()
    {
        var data = BuildSampleDocument();
        using var document = NbtDocument.Parse(data);

        IEnumerable elements = document.RootElement.GetMember("Numbers").EnumerateElements();
        var values = elements.Cast<NbtElement>().Select(element => element.GetInt32()).ToArray();
        Assert.Equal([1, 2, 3], values);
    }

    // --------------------------------------------------------------------------------------------
    // Parse-time validation
    // --------------------------------------------------------------------------------------------

    [Fact]
    public void UnknownTagTypeThrowsAtParse()
    {
        Assert.Throws<NbtException>(() => NbtDocument.Parse(new byte[] { 0x0D, 0x00, 0x00 }));
    }

    [Fact]
    public void RootTagEndThrowsAtParse()
    {
        Assert.Throws<NbtException>(() => NbtDocument.Parse(new byte[] { 0x00 }));
    }

    [Fact]
    public void NegativeListLengthThrowsAtParse()
    {
        Assert.Throws<NbtException>(() => NbtDocument.Parse(
            new byte[] { 0x09, 0x03, 0xFF, 0xFF, 0xFF, 0xFF },
            new NbtReaderOptions { RootFormat = NbtRootFormat.Unnamed }));
    }

    [Fact]
    public void ExcessiveListLengthThrowsAtParse()
    {
        Assert.Throws<NbtException>(() => NbtDocument.Parse(
            BuildListDocument(3),
            new NbtReaderOptions { MaxCollectionLength = 2 }));
    }

    [Fact]
    public void NegativeArrayLengthThrowsAtParse()
    {
        Assert.Throws<NbtException>(() => NbtDocument.Parse(
            new byte[] { 0x0B, 0xFF, 0xFF, 0xFF, 0xFF },
            new NbtReaderOptions { RootFormat = NbtRootFormat.Unnamed }));
    }

    [Fact]
    public void ExcessiveArrayLengthThrowsAtParse()
    {
        Assert.Throws<NbtException>(() => NbtDocument.Parse(
            BuildIntArrayDocument(3),
            new NbtReaderOptions { MaxCollectionLength = 2 }));
    }

    [Fact]
    public void ListOfEndWithNonZeroLengthThrowsAtParse()
    {
        Assert.Throws<NbtException>(() => NbtDocument.Parse(
            new byte[] { 0x09, 0x00, 0x00, 0x00, 0x00, 0x01 },
            new NbtReaderOptions { RootFormat = NbtRootFormat.Unnamed }));
    }

    [Fact]
    public void DeclaredListLengthBeyondAvailableDataThrowsAtParse()
    {
        Assert.Throws<NbtException>(() => NbtDocument.Parse(
            new byte[] { 0x09, 0x03, 0x00, 0x00, 0x03, 0xE8, 0x00 },
            new NbtReaderOptions { RootFormat = NbtRootFormat.Unnamed }));
    }

    [Fact]
    public void DeclaredArrayLengthBeyondAvailableDataThrowsAtParse()
    {
        Assert.Throws<NbtException>(() => NbtDocument.Parse(
            new byte[] { 0x0B, 0x00, 0x00, 0x03, 0xE8, 0x00 },
            new NbtReaderOptions { RootFormat = NbtRootFormat.Unnamed }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(11)]
    public void TruncationAtEveryPositionThrowsAtParse(int length)
    {
        var data = BuildSampleDocument();

        Assert.Throws<NbtException>(() =>
            NbtDocument.Parse(data.AsMemory(0, length)));
    }

    [Fact]
    public void UnterminatedCompoundThrowsAtParse()
    {
        // TAG_Compound "root" with no end tag.
        Assert.Throws<NbtException>(() => NbtDocument.Parse(
            new byte[] { 0x0A, 0x00, 0x04, (byte)'r', (byte)'o', (byte)'o', (byte)'t' }));
    }

    [Fact]
    public void ExcessiveDepthThrowsAtParse()
    {
        var data = BuildDeepDocument(3);

        Assert.Throws<NbtException>(() => NbtDocument.Parse(data, new NbtReaderOptions { MaxDepth = 2 }));
    }

    [Fact]
    public void MemoryOverloadEnforcesTheDocumentLimit()
    {
        var data = NbtSerializer.Serialize(42, "root");

        Assert.Throws<NbtException>(() => NbtDocument.Parse(data, new NbtReaderOptions
        {
            MaxDocumentBytes = data.Length - 1,
        }));
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

        Assert.Equal(42, document.RootElement.GetInt32());
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

        Assert.Equal(42, document.RootElement.GetInt32());
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

        Assert.Contains("exceeds the configured limit of 11 bytes", exception.Message);
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

        Assert.Equal(42, document.RootElement.GetInt32());
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

        Assert.Equal(42, document.RootElement.GetInt32());
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

        Assert.Contains("exceeds the configured limit of 11 bytes", exception.Message);
    }

    [Fact]
    public void StreamLimitBelowTheDocumentSizeThrowsWithoutReadingPastTheLimit()
    {
        var data = NbtSerializer.Serialize(42, "root");
        using var stream = new MemoryStream(data);

        Assert.Throws<NbtException>(() =>
            NbtDocument.Parse(stream, new NbtReaderOptions { MaxDocumentBytes = data.Length - 1 }));
    }

    [Fact]
    public void InvalidOptionsAreRejected()
    {
        var data = BuildSampleDocument();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            NbtDocument.Parse(data, new NbtReaderOptions { RootFormat = (NbtRootFormat)42 }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            NbtDocument.Parse(data, new NbtReaderOptions { MaxDepth = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            NbtDocument.Parse(data, new NbtReaderOptions { MaxCollectionLength = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            NbtDocument.Parse(data, new NbtReaderOptions { MaxDocumentBytes = -1 }));
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

        Assert.Equal(100_000, document.RootElement.GetMember("Data").GetByteArray().Length);
    }

    [Fact]
    public void StreamThatOverReportsItsLengthParsesTheActualData()
    {
        var data = BuildSampleDocument();
        using var stream = new OverReportingStream(data, 1024);

        using var document = NbtDocument.Parse(stream, new NbtReaderOptions { MaxDocumentBytes = 10_000 });

        Assert.Equal(20, document.RootElement.GetMember("Health").GetInt32());
    }

    [Fact]
    public void MalformedStreamDataThrowsImmediately()
    {
        using var stream = new NonSeekableStream([0x0D, 0x00, 0x00]);

        var exception = Assert.Throws<NbtException>(() => NbtDocument.Parse(stream));
        Assert.False(exception.IsTruncated);
        Assert.Contains("Unknown NBT tag type 13", exception.Message);
    }

    // --------------------------------------------------------------------------------------------
    // Lifetime
    // --------------------------------------------------------------------------------------------

    [Fact]
    public void DisposedDocumentThrowsOnEveryMember()
    {
        var document = NbtDocument.Parse(BuildSampleDocument());
        var root = document.RootElement;
        var member = root.GetMember("Health");
        document.Dispose();

        Assert.Throws<ObjectDisposedException>(() => document.RootElement);
        Assert.Throws<ObjectDisposedException>(() => _ = document.RootName);
        Assert.Throws<ObjectDisposedException>(() => _ = root.TagType);
        Assert.Throws<ObjectDisposedException>(() => member.GetInt32());
        Assert.Throws<ObjectDisposedException>(() => root.EnumerateMembers().MoveNext());
    }

    [Fact]
    public void DisposeIsIdempotent()
    {
        var document = NbtDocument.Parse(BuildSampleDocument());

        document.Dispose();
        document.Dispose();
    }

    [Fact]
    public void DefaultElementThrowsOnEveryMember()
    {
        var element = default(NbtElement);

        Assert.Throws<InvalidOperationException>(() => _ = element.TagType);
        Assert.Throws<InvalidOperationException>(() => _ = element.Name);
        Assert.Throws<InvalidOperationException>(() => element.GetSByte());
        Assert.Throws<InvalidOperationException>(() => element.GetString());
        Assert.Throws<InvalidOperationException>(() => _ = element.MemberCount);
        Assert.Throws<InvalidOperationException>(() => element.GetElementAt(0));
        Assert.Throws<InvalidOperationException>(() => element.GetMember("x"));
        Assert.Throws<InvalidOperationException>(() => element.TryGetMember("x", out _));
        Assert.Throws<InvalidOperationException>(() => element.ToNbtValue());
        Assert.Throws<InvalidOperationException>(() => element.NameEquals("x"u8));
    }

    // --------------------------------------------------------------------------------------------
    // Materialization
    // --------------------------------------------------------------------------------------------

    [Fact]
    public void ToNbtValueMatchesSerializerDeserialization()
    {
        var data = BuildAllTagsDocument();
        var expected = NbtSerializer.Deserialize(data).Value;

        using var document = NbtDocument.Parse(data);

        Assert.True(ValueEquals(expected, document.RootElement.ToNbtValue()), "materialized value differs");
    }

    [Fact]
    public void ToNbtValueMatchesSerializerDeserializationForDuplicatesAndEmptyLists()
    {
        var data = BuildEmptyListDocument();
        var expected = NbtSerializer.Deserialize(data).Value;

        using var document = NbtDocument.Parse(data);

        Assert.True(ValueEquals(expected, document.RootElement.ToNbtValue()), "materialized value differs");
    }

    [Fact]
    public void ToNbtValueMaterializesBothEmptyListEncodingsDistinctly()
    {
        var data = BuildEmptyListDocument();
        using var document = NbtDocument.Parse(data);
        var root = document.RootElement;

        var emptyEnd = root.GetMember("EmptyEnd").ToNbtValue();
        var emptyInts = root.GetMember("EmptyInts").ToNbtValue();

        Assert.True(emptyEnd.Value is NbtList endList && endList is NbtEndList);
        Assert.True(emptyInts.Value is NbtList intList && intList.ElementType == NbtTagType.Int);
    }

    [Fact]
    public void ToNbtValueMaterializesByteArraysAsSignedBytes()
    {
        var data = BuildAllTagsDocument();
        using var document = NbtDocument.Parse(data);

        var value = document.RootElement.GetMember("ByteArray").ToNbtValue();

        Assert.True(value is sbyte[] { Length: 3 });
    }

    // --------------------------------------------------------------------------------------------
    // Helpers
    // --------------------------------------------------------------------------------------------

    private static string ParseString(byte[] data)
    {
        using var document = NbtDocument.Parse(data, new NbtReaderOptions { RootFormat = NbtRootFormat.Unnamed });
        return document.RootElement.GetString();
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
        writer.WriteSByte("Byte", -5);
        writer.WriteInt16("Short", 12345);
        writer.WriteInt32("Int", -123456789);
        writer.WriteInt64("Long", 1234567890123L);
        writer.WriteSingle("Float", 0.5f);
        writer.WriteDouble("Double", -0.25);
        writer.WriteByteArray("ByteArray", [1, 2, 3]);
        writer.WriteString("String", "中文 \0 x");
        writer.WriteStartList("List", NbtTagType.String, 2);
        writer.WriteString("a");
        writer.WriteString("b");
        writer.WriteEndList();
        writer.WriteStartCompound("Compound");
        writer.WriteInt32("Nested", 1);
        writer.WriteEndCompound();
        writer.WriteInt32Array("IntArray", [int.MinValue, -1, int.MaxValue]);
        writer.WriteInt64Array("LongArray", [long.MinValue, long.MaxValue]);
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
        writer.WriteInt32("Dup", 1);
        writer.WriteInt32("Dup", 2);
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

    private static byte[] BuildIntArrayDocument(int count)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new NbtWriter(buffer, new NbtWriterOptions { RootFormat = NbtRootFormat.Unnamed });
        writer.WriteInt32Array("ignored", new int[count]);
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

    private static bool ValueEquals(object? expected, object? actual)
    {
        if (expected is NbtValue expectedValue)
        {
            expected = expectedValue.Value;
        }

        if (actual is NbtValue actualValue)
        {
            actual = actualValue.Value;
        }

        return (expected, actual) switch
        {
            (null, null) => true,
            (NbtCompound expectedCompound, NbtCompound actualCompound) =>
                CompoundEquals(expectedCompound, actualCompound),
            (NbtList expectedList, NbtList actualList) => ListEquals(expectedList, actualList),
            (sbyte[] expectedArray, sbyte[] actualArray) => expectedArray.SequenceEqual(actualArray),
            (int[] expectedArray, int[] actualArray) => expectedArray.SequenceEqual(actualArray),
            (long[] expectedArray, long[] actualArray) => expectedArray.SequenceEqual(actualArray),
            (string expectedString, string actualString) => expectedString == actualString,
            (IEnumerable expectedSequence, IEnumerable actualSequence) =>
                SequenceEquals(expectedSequence, actualSequence),
            _ => Equals(expected, actual),
        };
    }

    private static bool CompoundEquals(NbtCompound expected, NbtCompound actual)
    {
        if (expected.Count != actual.Count)
        {
            return false;
        }

        foreach (var (name, value) in expected)
        {
            if (!actual.TryGetValue(name, out var actualValue) || !ValueEquals(value, actualValue))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ListEquals(NbtList expected, NbtList actual)
    {
        if (expected.ElementType != actual.ElementType || expected.Count != actual.Count)
        {
            return false;
        }

        if (expected.Count == 0)
        {
            return true; // both empty; the NbtEndList marker is not enumerable
        }

        return SequenceEquals((IEnumerable)expected.Value, (IEnumerable)actual.Value);
    }

    private static bool SequenceEquals(IEnumerable expected, IEnumerable actual)
    {
        var expectedEnumerator = expected.GetEnumerator();
        var actualEnumerator = actual.GetEnumerator();
        while (true)
        {
            var expectedNext = expectedEnumerator.MoveNext();
            var actualNext = actualEnumerator.MoveNext();
            if (expectedNext != actualNext)
            {
                return false;
            }

            if (!expectedNext)
            {
                return true;
            }

            if (!ValueEquals(expectedEnumerator.Current, actualEnumerator.Current))
            {
                return false;
            }
        }
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

namespace NbtSharp.Tests;

public sealed class NbtNestedCollectionMatrixTests
{
    [Fact]
    public void NestedCollectionsRoundTripThroughBothPaths()
    {
        var value = new NestedCollectionMatrixDto
        {
            Lists = [[1, 2], [], [3]],
            MapOfLists = new Dictionary<string, List<int>>
            {
                ["a"] = [4, 5],
                ["b"] = [],
            },
            Arrays = [[6, 7], [8]],
            MapOfMaps = new Dictionary<string, Dictionary<string, int>>
            {
                ["outer"] = new Dictionary<string, int>
                {
                    ["inner"] = 9,
                    ["second"] = 10,
                },
            },
            JaggedArray = [[11], [12, 13]],
        };
        var reflection = new NbtSerializerOptions
        {
            TypeInfoResolver = new NbtReflectionTypeInfoResolver(),
        };

        var generatedData = NbtSerializer.Serialize(
            value,
            NestedCollectionContext.Default.NestedCollectionMatrixDto,
            "root");
        var reflectedData = NbtSerializer.Serialize(value, "root", reflection);

        Assert.Equal(reflectedData, generatedData);

        var generatedCopy = NbtSerializer.Deserialize(
            generatedData,
            NestedCollectionContext.Default.NestedCollectionMatrixDto);
        var reflectedCopy = NbtSerializer.Deserialize<NestedCollectionMatrixDto>(
            reflectedData,
            reflection);

        AssertNestedValues(generatedCopy);
        AssertNestedValues(reflectedCopy);
    }

    private static void AssertNestedValues(NestedCollectionMatrixDto copy)
    {
        Assert.Equal(new[] { 1, 2 }, copy.Lists[0]);
        Assert.Empty(copy.Lists[1]);
        Assert.Equal(new[] { 3 }, copy.Lists[2]);
        Assert.Equal(new[] { 4, 5 }, copy.MapOfLists["a"]);
        Assert.Empty(copy.MapOfLists["b"]);
        Assert.Equal(new[] { 6, 7 }, copy.Arrays[0]);
        Assert.Equal(new[] { 8 }, copy.Arrays[1]);
        Assert.Equal(9, copy.MapOfMaps["outer"]["inner"]);
        Assert.Equal(10, copy.MapOfMaps["outer"]["second"]);
        Assert.Equal(new[] { 11 }, copy.JaggedArray[0]);
        Assert.Equal(new[] { 12, 13 }, copy.JaggedArray[1]);
    }
}

internal sealed class NestedCollectionMatrixDto
{
    public List<List<int>> Lists { get; set; } = [];

    public Dictionary<string, List<int>> MapOfLists { get; set; } = [];

    public List<int[]> Arrays { get; set; } = [];

    public Dictionary<string, Dictionary<string, int>> MapOfMaps { get; set; } = [];

    public int[][] JaggedArray { get; set; } = [];
}

[NbtSerializable(typeof(NestedCollectionMatrixDto))]
internal partial class NestedCollectionContext : NbtSerializerContext
{
}

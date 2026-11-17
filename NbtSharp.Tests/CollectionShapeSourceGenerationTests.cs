using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;

namespace NbtSharp.Tests;

public sealed class CollectionShapeSourceGenerationTests
{
    [Fact]
    public void GeneratedConvertersSupportDiscoveredAndConcurrentShapes()
    {
        var context = CollectionShapeContext.Default;
        var value = new SourceGenCollectionShapes
        {
            ConcurrentMap = new ConcurrentDictionary<string, int>(
                new Dictionary<string, int> { ["a"] = 1 }),
            ConcurrentBag = [1, 2],
            ConcurrentQueue = new ConcurrentQueue<int>([1, 2]),
            ConcurrentStack = new ConcurrentStack<int>([1, 2]),
            SortedSet = [2, 1],
            SortedMap = new SortedDictionary<string, int>(StringComparer.Ordinal)
            {
                ["b"] = 2,
                ["a"] = 1,
            },
            SortedList = new SortedList<string, int>(StringComparer.Ordinal)
            {
                ["d"] = 4,
                ["c"] = 3,
            },
            ImmutableQueue = ImmutableQueue.CreateRange(new[] { 1, 2 }),
            ImmutableStack = ImmutableStack.CreateRange(new[] { 1, 2 }),
            ImmutableSortedSet = ImmutableSortedSet.Create(2, 1),
            ImmutableSortedMap = ImmutableSortedDictionary.CreateRange(
                StringComparer.Ordinal,
                new Dictionary<string, int> { ["f"] = 6, ["e"] = 5 }),
            Custom = new CustomCollection<int>([3, 4]),
        };
        value.Blocking.TryAdd(1);
        value.Blocking.TryAdd(2);

        var data = NbtSerializer.Serialize(value, context.SourceGenCollectionShapes, "root");
        var copy = NbtSerializer.Deserialize(data, context.SourceGenCollectionShapes);

        Assert.Equal(1, copy.ConcurrentMap["a"]);
        Assert.Equal([1, 2], copy.ConcurrentBag.OrderBy(static item => item));
        Assert.Equal([1, 2], copy.ConcurrentQueue.ToArray());
        Assert.Equal([2, 1], copy.ConcurrentStack.ToArray());
        Assert.Equal([1, 2], copy.SortedSet.ToArray());
        Assert.Equal(1, copy.SortedMap["a"]);
        Assert.Equal(2, copy.SortedMap["b"]);
        Assert.Equal(3, copy.SortedList["c"]);
        Assert.Equal(4, copy.SortedList["d"]);
        Assert.Equal([1, 2], copy.ImmutableQueue.ToArray());
        Assert.Equal([2, 1], copy.ImmutableStack.ToArray());
        Assert.Equal([1, 2], copy.ImmutableSortedSet.ToArray());
        Assert.Equal(5, copy.ImmutableSortedMap["e"]);
        Assert.Equal(6, copy.ImmutableSortedMap["f"]);
        Assert.Equal([1, 2], copy.Blocking.OrderBy(static item => item));
        Assert.Equal([3, 4], copy.Custom.ToArray());
    }

    [Fact]
    public void GeneratedConvertersSupportMutableCollectionShapes()
    {
        var context = CollectionShapeContext.Default;
        var value = new SourceGenCollectionShapes
        {
            Queue = new Queue<int>([1, 2, 3]),
            Stack = new Stack<int>([1, 2, 3]),
            Linked = new LinkedList<int>([1, 2, 3]),
            Set = new HashSet<string> { "a", "b" },
            SetInterface = new HashSet<string> { "c", "d" },
            ReadOnlySet = new HashSet<string> { "e", "f" },
            Enumerable = new[] { 4, 5 },
            CollectionInterface = new List<int> { 6, 7 },
            ListInterface = new List<int> { 8, 9 },
            Collection = new Collection<int> { 10, 11 },
            Observable = new ObservableCollection<int> { 12, 13 },
            ReadOnly = new ReadOnlyCollection<int>([14, 15]),
            ImmutableArray = [],
        };

        var data = NbtSerializer.Serialize(value, context.SourceGenCollectionShapes, "root");
        var copy = NbtSerializer.Deserialize(data, context.SourceGenCollectionShapes);

        Assert.Equal([1, 2, 3], copy.Queue.ToArray());
        Assert.Equal([3, 2, 1], copy.Stack.ToArray());
        Assert.Equal([1, 2, 3], copy.Linked.ToArray());
        Assert.True(copy.Set.SetEquals(["a", "b"]));
        Assert.True(copy.SetInterface.SetEquals(["c", "d"]));
        Assert.True(copy.ReadOnlySet.SetEquals(["e", "f"]));
        Assert.Equal([4, 5], copy.Enumerable.ToArray());
        Assert.Equal([6, 7], copy.CollectionInterface.ToArray());
        Assert.Equal([8, 9], copy.ListInterface.ToArray());
        Assert.Equal([10, 11], copy.Collection.ToArray());
        Assert.Equal([12, 13], copy.Observable.ToArray());
        Assert.Equal([14, 15], copy.ReadOnly.ToArray());
    }

    [Fact]
    public void GeneratedConvertersSupportImmutableAndFrozenShapes()
    {
        var context = CollectionShapeContext.Default;
        var value = new SourceGenCollectionShapes
        {
            ImmutableArray = [16, 17],
            ImmutableList = ImmutableList.Create(18, 19),
            ImmutableListInterface = ImmutableList.Create(20, 21),
            ImmutableSet = ImmutableHashSet.Create("g", "h"),
            ImmutableSetInterface = ImmutableHashSet.Create("i", "j"),
            ReadOnlyDictionary = new ReadOnlyDictionary<string, int>(
                new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 }),
            ImmutableDictionary = ImmutableDictionary.CreateRange(
                new Dictionary<string, int> { ["c"] = 3, ["d"] = 4 }),
            ImmutableDictionaryInterface = ImmutableDictionary.CreateRange(
                new Dictionary<string, int> { ["e"] = 5, ["f"] = 6 }),
            FrozenSet = new HashSet<int> { 22, 23 }.ToFrozenSet(),
            FrozenDictionary = new Dictionary<string, int> { ["g"] = 7, ["h"] = 8 }
                .ToFrozenDictionary(),
        };

        var data = NbtSerializer.Serialize(value, context.SourceGenCollectionShapes, "root");
        var copy = NbtSerializer.Deserialize(data, context.SourceGenCollectionShapes);

        Assert.Equal([16, 17], copy.ImmutableArray.ToArray());
        Assert.Equal([18, 19], copy.ImmutableList.ToArray());
        Assert.Equal([20, 21], copy.ImmutableListInterface.ToArray());
        Assert.True(copy.ImmutableSet.SetEquals(["g", "h"]));
        Assert.True(copy.ImmutableSetInterface.SetEquals(["i", "j"]));
        Assert.Equal(1, copy.ReadOnlyDictionary["a"]);
        Assert.Equal(2, copy.ReadOnlyDictionary["b"]);
        Assert.Equal(3, copy.ImmutableDictionary["c"]);
        Assert.Equal(4, copy.ImmutableDictionary["d"]);
        Assert.Equal(5, copy.ImmutableDictionaryInterface["e"]);
        Assert.Equal(6, copy.ImmutableDictionaryInterface["f"]);
        Assert.True(copy.FrozenSet.SetEquals([22, 23]));
        Assert.Equal(7, copy.FrozenDictionary["g"]);
        Assert.Equal(8, copy.FrozenDictionary["h"]);
    }
}

[NbtSerializable(typeof(SourceGenCollectionShapes))]
internal partial class CollectionShapeContext : NbtSerializerContext
{
}

internal sealed class SourceGenCollectionShapes
{
    public Queue<int> Queue { get; set; } = new();
    public Stack<int> Stack { get; set; } = new();

    public LinkedList<int> Linked { get; set; } = new();

    public HashSet<string> Set { get; set; } = [];

    public ISet<string> SetInterface { get; set; } = new HashSet<string>();

    public IReadOnlySet<string> ReadOnlySet { get; set; } = new HashSet<string>();

    public IEnumerable<int> Enumerable { get; set; } = [];

    public ICollection<int> CollectionInterface { get; set; } = new List<int>();

    public IList<int> ListInterface { get; set; } = new List<int>();

    public Collection<int> Collection { get; set; } = [];

    public ObservableCollection<int> Observable { get; set; } = [];

    public ReadOnlyCollection<int> ReadOnly { get; set; } = new(new List<int>());

    public ImmutableArray<int> ImmutableArray { get; set; }

    public ImmutableList<int> ImmutableList { get; set; } = ImmutableList<int>.Empty;

    public IImmutableList<int> ImmutableListInterface { get; set; } = ImmutableList<int>.Empty;

    public ImmutableHashSet<string> ImmutableSet { get; set; } = ImmutableHashSet<string>.Empty;

    public IImmutableSet<string> ImmutableSetInterface { get; set; } =
        ImmutableHashSet<string>.Empty;

    public ReadOnlyDictionary<string, int> ReadOnlyDictionary { get; set; } =
        new(new Dictionary<string, int>());

    public ImmutableDictionary<string, int> ImmutableDictionary { get; set; } =
        ImmutableDictionary<string, int>.Empty;

    public IImmutableDictionary<string, int> ImmutableDictionaryInterface { get; set; } =
        ImmutableDictionary<string, int>.Empty;

    public ConcurrentDictionary<string, int> ConcurrentMap { get; set; } =
        new(StringComparer.Ordinal);

    public ConcurrentBag<int> ConcurrentBag { get; set; } = [];

    public ConcurrentQueue<int> ConcurrentQueue { get; set; } = new();

    public ConcurrentStack<int> ConcurrentStack { get; set; } = new();

    public SortedSet<int> SortedSet { get; set; } = [];

    public SortedDictionary<string, int> SortedMap { get; set; } = [];

    public SortedList<string, int> SortedList { get; set; } = [];

    public ImmutableQueue<int> ImmutableQueue { get; set; } = ImmutableQueue<int>.Empty;

    public ImmutableStack<int> ImmutableStack { get; set; } = ImmutableStack<int>.Empty;

    public ImmutableSortedSet<int> ImmutableSortedSet { get; set; } =
        ImmutableSortedSet<int>.Empty;

    public ImmutableSortedDictionary<string, int> ImmutableSortedMap { get; set; } =
        ImmutableSortedDictionary<string, int>.Empty;

    public BlockingCollection<int> Blocking { get; set; } = [];

    public CustomCollection<int> Custom { get; set; } = new([]);

    public FrozenSet<int> FrozenSet { get; set; } = new HashSet<int>().ToFrozenSet();

    public FrozenDictionary<string, int> FrozenDictionary { get; set; } =
        new Dictionary<string, int>().ToFrozenDictionary();
}

[CollectionBuilder(typeof(CustomCollectionFactory), "Create")]
internal sealed class CustomCollection<T> : IReadOnlyList<T>
{
    private readonly T[] _items;

    public CustomCollection(T[] items) => _items = items;

    public T this[int index] => _items[index];

    public int Count => _items.Length;

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
}

internal static class CustomCollectionFactory
{
    public static CustomCollection<T> Create<T>(ReadOnlySpan<T> items) =>
        new(items.ToArray());
}

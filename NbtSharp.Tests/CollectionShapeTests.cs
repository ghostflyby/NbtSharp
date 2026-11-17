using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;

namespace NbtSharp.Tests;

public sealed class CollectionShapeTests
{
    [Fact]
    public void MutableSequenceShapesRoundTripInEnumerationOrder()
    {
        var queue = new Queue<int>([1, 2, 3]);
        var stack = new Stack<int>([1, 2, 3]);
        var linked = new LinkedList<int>([1, 2, 3]);
        var collection = new Collection<int> { 1, 2, 3 };
        var observable = new ObservableCollection<int> { 1, 2, 3 };
        var readOnly = new ReadOnlyCollection<int>([1, 2, 3]);

        Assert.Equal([1, 2, 3], RoundTrip(queue).ToArray());
        Assert.Equal([3, 2, 1], RoundTrip(stack).ToArray());
        Assert.Equal([1, 2, 3], RoundTrip(linked).ToArray());
        Assert.Equal([1, 2, 3], RoundTrip(collection).ToArray());
        Assert.Equal([1, 2, 3], RoundTrip(observable).ToArray());
        Assert.Equal([1, 2, 3], RoundTrip(readOnly).ToArray());
    }

    [Fact]
    public void SetShapesCollapseDuplicatesAndRoundTrip()
    {
        var set = new HashSet<int>([1, 2, 2, 3]);
        var immutable = ImmutableHashSet.CreateRange(new[] { 1, 2, 2, 3 });
        var frozen = set.ToFrozenSet();

        Assert.True(set.SetEquals(RoundTrip(set)));
        Assert.Equal([1, 2, 3], RoundTrip<ISet<int>>(set).OrderBy(static value => value));
        Assert.True(immutable.SetEquals(RoundTrip(immutable)));
        Assert.Equal(
            [1, 2, 3],
            RoundTrip<IImmutableSet<int>>(immutable).OrderBy(static value => value));
        Assert.True(frozen.SetEquals(RoundTrip(frozen)));
    }

    [Fact]
    public void SetShapesCollapseDuplicatesOnRead()
    {
        var data = NbtSerializer.Serialize(new List<int> { 1, 1, 2, 2, 3 }, "root");

        var set = NbtSerializer.Deserialize<HashSet<int>>(data);
        var interfaceSet = NbtSerializer.Deserialize<IReadOnlySet<int>>(data);

        Assert.Equal([1, 2, 3], set.OrderBy(static value => value));
        Assert.Equal([1, 2, 3], interfaceSet.OrderBy(static value => value));
    }

    [Fact]
    public void EmptyShapesRoundTrip()
    {
        Assert.Empty(RoundTrip(new Queue<int>()));
        Assert.Empty(RoundTrip(new Stack<int>()));
        Assert.Empty(RoundTrip(new HashSet<int>()));
        Assert.Empty(RoundTrip(ImmutableArray<int>.Empty));
        Assert.Empty(RoundTrip(ImmutableList<int>.Empty));
    }

    [Fact]
    public void ImmutableSequenceShapesRoundTrip()
    {
        var array = ImmutableArray.Create(1, 2, 3);
        var list = ImmutableList.Create(1, 2, 3);

        Assert.Equal([1, 2, 3], RoundTrip(array).ToArray());
        Assert.Equal([1, 2, 3], RoundTrip(list).ToArray());
        Assert.Equal([1, 2, 3], RoundTrip<IImmutableList<int>>(list).ToArray());
    }

    [Fact]
    public void DictionaryShapesRoundTrip()
    {
        var readOnly = new ReadOnlyDictionary<string, int>(
            new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 });
        var immutable = ImmutableDictionary.CreateRange(
            new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 });
        var frozen = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 }.ToFrozenDictionary();

        var readOnlyCopy = RoundTrip(readOnly);
        var immutableCopy = RoundTrip(immutable);
        var interfaceCopy = RoundTrip<IImmutableDictionary<string, int>>(immutable);
        var frozenCopy = RoundTrip(frozen);

        Assert.Equal(1, readOnlyCopy["a"]);
        Assert.Equal(2, readOnlyCopy["b"]);
        Assert.Equal(1, immutableCopy["a"]);
        Assert.Equal(2, immutableCopy["b"]);
        Assert.Equal(1, interfaceCopy["a"]);
        Assert.Equal(2, interfaceCopy["b"]);
        Assert.Equal(1, frozenCopy["a"]);
        Assert.Equal(2, frozenCopy["b"]);
    }

    [Fact]
    public void DiscoveredAndConcurrentShapesRoundTrip()
    {
        var concurrentMap = new ConcurrentDictionary<string, int>(
            new Dictionary<string, int> { ["a"] = 1 });
        var bag = new ConcurrentBag<int>([1, 2]);
        var concurrentQueue = new ConcurrentQueue<int>([1, 2]);
        var concurrentStack = new ConcurrentStack<int>([1, 2]);
        var sortedSet = new SortedSet<int>([2, 1, 2]);
        var sortedMap = new SortedDictionary<string, int>(StringComparer.Ordinal)
        {
            ["b"] = 2,
            ["a"] = 1,
        };
        var sortedList = new SortedList<string, int>(StringComparer.Ordinal)
        {
            ["d"] = 4,
            ["c"] = 3,
        };
        var immutableQueue = ImmutableQueue.CreateRange(new[] { 1, 2 });
        var immutableStack = ImmutableStack.CreateRange(new[] { 1, 2 });
        var immutableSortedSet = ImmutableSortedSet.Create(2, 1);
        var immutableSortedMap = ImmutableSortedDictionary.CreateRange(
            StringComparer.Ordinal,
            new Dictionary<string, int> { ["f"] = 6, ["e"] = 5 });
        var blocking = new BlockingCollection<int>();
        blocking.TryAdd(1);
        blocking.TryAdd(2);

        Assert.Equal(1, RoundTrip(concurrentMap)["a"]);
        Assert.Equal([1, 2], RoundTrip(bag).OrderBy(static value => value));
        Assert.Equal([1, 2], RoundTrip(concurrentQueue).ToArray());
        Assert.Equal([2, 1], RoundTrip(concurrentStack).ToArray());
        Assert.Equal([1, 2], RoundTrip(sortedSet).ToArray());
        Assert.Equal(1, RoundTrip(sortedMap)["a"]);
        Assert.Equal(2, RoundTrip(sortedMap)["b"]);
        Assert.Equal(3, RoundTrip(sortedList)["c"]);
        Assert.Equal(4, RoundTrip(sortedList)["d"]);
        Assert.Equal([1, 2], RoundTrip(immutableQueue).ToArray());
        Assert.Equal([1, 2], RoundTrip<IImmutableQueue<int>>(immutableQueue).ToArray());
        Assert.Equal([2, 1], RoundTrip(immutableStack).ToArray());
        Assert.Equal([2, 1], RoundTrip<IImmutableStack<int>>(immutableStack).ToArray());
        Assert.Equal([1, 2], RoundTrip(immutableSortedSet).ToArray());
        Assert.Equal(5, RoundTrip(immutableSortedMap)["e"]);
        Assert.Equal(6, RoundTrip(immutableSortedMap)["f"]);
        Assert.Equal([1, 2], RoundTrip(blocking).OrderBy(static value => value));
    }

    [Fact]
    public void ReflectionSupportsCollectionBuilderAndConstructorPatterns()
    {
        var builder = new ArrayCustomCollection<int>([1, 2]);
        var constructed = new ConstructedCustomCollection<int>([3, 4]);
        var map = new CustomStringMap { ["a"] = 1 };

        Assert.Equal([1, 2], RoundTrip(builder).ToArray());
        Assert.Equal([3, 4], RoundTrip(constructed).ToArray());
        Assert.Equal(1, RoundTrip(map)["a"]);
    }

    private static T RoundTrip<T>(T value)
    {
        var data = NbtSerializer.Serialize(value, "root");
        return NbtSerializer.Deserialize<T>(data);
    }
}

[CollectionBuilder(typeof(ArrayCustomCollectionFactory), "Create")]
internal sealed class ArrayCustomCollection<T> : IReadOnlyList<T>
{
    private readonly T[] _items;

    public ArrayCustomCollection(T[] items) => _items = items;

    public T this[int index] => _items[index];

    public int Count => _items.Length;

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
}

internal static class ArrayCustomCollectionFactory
{
    public static ArrayCustomCollection<T> Create<T>(T[] items) => new(items);
}

internal sealed class ConstructedCustomCollection<T> : IReadOnlyList<T>
{
    private readonly List<T> _items;

    public ConstructedCustomCollection(IEnumerable<T> items) => _items = [.. items];

    public T this[int index] => _items[index];

    public int Count => _items.Count;

    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
}

internal sealed class CustomStringMap : Dictionary<string, int>
{
}

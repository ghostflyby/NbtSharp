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
        var readOnlyObservable = new ReadOnlyObservableCollection<int>(observable);

        Assert.Equal([1, 2, 3], RoundTrip(queue).ToArray());
        Assert.Equal([3, 2, 1], RoundTrip(stack).ToArray());
        Assert.Equal([1, 2, 3], RoundTrip(linked).ToArray());
        Assert.Equal([1, 2, 3], RoundTrip(collection).ToArray());
        Assert.Equal([1, 2, 3], RoundTrip(observable).ToArray());
        Assert.Equal([1, 2, 3], RoundTrip(readOnly).ToArray());
        Assert.Equal([1, 2, 3], RoundTrip(readOnlyObservable).ToArray());
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
    public void SortedSetShapesUseOrdinalOrderingForStrings()
    {
        var sorted = new SortedSet<string>(StringComparer.Ordinal) { "a", "B" };
        var immutable = ImmutableSortedSet.Create(StringComparer.Ordinal, "a", "B");

        var sortedCopy = RoundTrip(sorted);
        var immutableCopy = RoundTrip(immutable);

        Assert.Equal(["B", "a"], sortedCopy.ToArray());
        Assert.Equal(["B", "a"], immutableCopy.ToArray());
        Assert.Same(StringComparer.Ordinal, sortedCopy.Comparer);
        Assert.Same(StringComparer.Ordinal, immutableCopy.KeyComparer);
    }

    [Fact]
    public void EmptyShapesRoundTrip()
    {
        Assert.Empty(RoundTrip(new Queue<int>()));
        Assert.Empty(RoundTrip(new Stack<int>()));
        Assert.Empty(RoundTrip(new HashSet<int>()));
        Assert.Empty(RoundTrip(ImmutableArray<int>.Empty));
        Assert.Empty(RoundTrip(ImmutableList<int>.Empty));

        var sorted = RoundTrip(new SortedSet<string>(StringComparer.Ordinal));
        var immutableSorted = RoundTrip(ImmutableSortedSet.Create<string>(StringComparer.Ordinal));
        Assert.Empty(sorted);
        Assert.Empty(immutableSorted);
        Assert.Same(StringComparer.Ordinal, sorted.Comparer);
        Assert.Same(StringComparer.Ordinal, immutableSorted.KeyComparer);
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

        var immutableSortedSetCopy = RoundTrip(immutableSortedSet);
        var immutableSortedMapCopy = RoundTrip(immutableSortedMap);
        var blockingCopy = RoundTrip(blocking);

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
        Assert.Equal([1, 2], immutableSortedSetCopy.ToArray());
        Assert.Equal(5, immutableSortedMapCopy["e"]);
        Assert.Equal(6, immutableSortedMapCopy["f"]);
        Assert.Same(StringComparer.Ordinal, immutableSortedMapCopy.KeyComparer);
        Assert.Equal([1, 2], blockingCopy.OrderBy(static value => value));
    }

    [Fact]
    public void StructCollectionUsesAddPattern()
    {
        var value = new StructIntCollection();
        value.Add(1);
        value.Add(2);

        Assert.Equal([1, 2], RoundTrip(value).ToArray());
    }

    [Fact]
    public void NonStringKeyMapsAreRejected()
    {
        Assert.Throws<NotSupportedException>(() =>
            NbtSerializer.Serialize(new Dictionary<int, int> { [1] = 2 }, "root"));
    }

    [Fact]
    public void PriorityQueueIsRejectedWithGuidance()
    {
        var queue = new PriorityQueue<int, int>();
        queue.Enqueue(1, 2);

        var exception = Assert.Throws<NotSupportedException>(() =>
            NbtSerializer.Serialize(queue, "root"));

        Assert.Contains("PriorityQueue", exception.Message);
        Assert.Contains("custom converter", exception.Message);

        Assert.Throws<NotSupportedException>(() =>
            NbtSerializer.Deserialize<PriorityQueue<int, int>>(Array.Empty<byte>()));
    }

    [Fact]
    public void ReflectionSupportsSpanOnlySequenceBuilders()
    {
        var readOnlySpan = SpanOnlyCollectionFactory.Create(new[] { 1, 2 });
        var writableSpan = CompanionSpanCollection.Create(new[] { 3, 4 });

        Assert.Equal([1, 2], RoundTrip(readOnlySpan).ToArray());
        Assert.Equal([3, 4], RoundTrip(writableSpan).ToArray());
        Assert.Empty(RoundTrip(SpanOnlyCollectionFactory.Create<int>([])));
    }

    [Fact]
    public void ReflectionSupportsSpanOnlyMapBuilders()
    {
        var value = SpanOnlyMapFactory.Create(
            new[] { new KeyValuePair<string, int>("a", 1) });

        var copy = RoundTrip(value);

        Assert.Equal(1, copy["a"]);
        Assert.Empty(RoundTrip(SpanOnlyMapFactory.Create<int>([])));
    }

    [Fact]
    public void ReflectionReversesStackLikeSpanFactories()
    {
        var value = StackSpanFactory.Create<int>([1, 2, 3]);

        var copy = RoundTrip(value);

        Assert.Equal([3, 2, 1], copy.ToArray());
    }

    [Fact]
    public void ReflectionPrefersEnumerableFactoriesOverSpanAndArray()
    {
        MultiFactory.Reset();
        var options = new NbtSerializerOptions();
        var value = new MultiFactoryCollection<int>([1, 2]);

        var data = NbtSerializer.Serialize(value, "root", options);
        var copy = NbtSerializer.Deserialize<MultiFactoryCollection<int>>(data, options);

        Assert.Equal([1, 2], copy.ToArray());
        Assert.Equal(1, MultiFactory.EnumerableCalls);
        Assert.Equal(0, MultiFactory.ArrayCalls);
        Assert.Equal(0, MultiFactory.ReadOnlySpanCalls);
        Assert.Equal(0, MultiFactory.WritableSpanCalls);
    }

    [Fact]
    public void ReflectionPrefersReadOnlySpanFactories()
    {
        SpanPreferenceFactory.Reset();
        var options = new NbtSerializerOptions();
        var value = new SpanPreferenceCollection<int>([1, 2]);

        var data = NbtSerializer.Serialize(value, "root", options);
        var copy = NbtSerializer.Deserialize<SpanPreferenceCollection<int>>(data, options);

        Assert.Equal([1, 2], copy.ToArray());
        Assert.Equal(1, SpanPreferenceFactory.ReadOnlyCalls);
        Assert.Equal(0, SpanPreferenceFactory.WritableCalls);
    }

    [Fact]
    public void ReflectionSupportsNonPublicSpanFactories()
    {
        var value = new NonPublicSpanCollection<int>([1, 2]);

        var copy = RoundTrip(value);

        Assert.Equal([1, 2], copy.ToArray());
    }

    [Fact]
    public void ReflectionSupportsCompanionWritableSpanMapFactories()
    {
        var value = WritableSpanMap.CreateRange(
            new[] { new KeyValuePair<string, int>("a", 1) });

        var copy = RoundTrip(value);

        Assert.Equal(1, copy["a"]);
    }

    [Fact]
    public void ReflectionSupportsSingleTypeParameterMapFactories()
    {
        var value = EnumerableMapFactory.Create(
            new[] { new KeyValuePair<string, int>("a", 1) });

        var copy = RoundTrip(value);

        Assert.Equal(1, copy["a"]);
    }

    [Fact]
    public void PriorityQueueConverterCanBeSupplied()
    {
        var queue = new PriorityQueue<int, int>();
        queue.Enqueue(1, 2);
        var options = new NbtSerializerOptions();
        options.Converters.Add(new PriorityQueueCountConverter());

        var data = NbtSerializer.Serialize(queue, "root", options);
        var copy = NbtSerializer.Deserialize<PriorityQueue<int, int>>(data, options);

        Assert.Empty(copy.UnorderedItems);
    }

    [Fact]
    public void NestedPriorityQueueIsRejected()
    {
        Assert.Throws<NotSupportedException>(() =>
            NbtSerializer.Serialize(new List<PriorityQueue<int, int>> { new() }, "root"));
    }

    [Fact]
    public void ReflectionKeepsStackOrderForConstructorPushTypes()
    {
        var value = new ConstructorStack<int>([1, 2]);

        Assert.Equal([2, 1], RoundTrip(value).ToArray());
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

internal struct StructIntCollection : IEnumerable<int>
{
    private List<int>? _items;

    public void Add(int item) => (_items ??= []).Add(item);

    public IEnumerator<int> GetEnumerator() => (_items ?? []).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

[CollectionBuilder(typeof(SpanOnlyCollectionFactory), "Create")]
internal sealed class SpanOnlyCollection<T> : IReadOnlyList<T>
{
    private readonly T[] _items;

    internal SpanOnlyCollection(T[] items) => _items = items;

    public T this[int index] => _items[index];

    public int Count => _items.Length;

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
}

internal static class SpanOnlyCollectionFactory
{
    public static SpanOnlyCollection<T> Create<T>(ReadOnlySpan<T> items) =>
        new(items.ToArray());
}

[CollectionBuilder(typeof(EnumerableMapFactory), "Create")]
internal sealed class EnumerableMap<TValue> : IReadOnlyDictionary<string, TValue>
{
    private readonly Dictionary<string, TValue> _items;

    internal EnumerableMap(Dictionary<string, TValue> items) => _items = items;

    public TValue this[string key] => _items[key];

    public IEnumerable<string> Keys => _items.Keys;

    public IEnumerable<TValue> Values => _items.Values;

    public int Count => _items.Count;

    public bool ContainsKey(string key) => _items.ContainsKey(key);

    public bool TryGetValue(string key, out TValue value)
    {
        if (_items.TryGetValue(key, out var stored))
        {
            value = stored;
            return true;
        }

        value = default!;
        return false;
    }

    public IEnumerator<KeyValuePair<string, TValue>> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

internal static class EnumerableMapFactory
{
    public static EnumerableMap<TValue> Create<TValue>(
        IEnumerable<KeyValuePair<string, TValue>> items)
    {
        var map = new Dictionary<string, TValue>(StringComparer.Ordinal);
        foreach (var pair in items)
        {
            map[pair.Key] = pair.Value;
        }

        return new EnumerableMap<TValue>(map);
    }
}

internal sealed class CompanionSpanCollection<T> : IReadOnlyList<T>
{
    private readonly T[] _items;

    internal CompanionSpanCollection(T[] items) => _items = items;

    public T this[int index] => _items[index];

    public int Count => _items.Length;

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
}

internal static class CompanionSpanCollection
{
    public static CompanionSpanCollection<T> Create<T>(Span<T> items) => new(items.ToArray());
}

[CollectionBuilder(typeof(MultiFactory), "Create")]
internal sealed class MultiFactoryCollection<T> : IReadOnlyList<T>
{
    private readonly T[] _items;

    internal MultiFactoryCollection(T[] items) => _items = items;

    public T this[int index] => _items[index];

    public int Count => _items.Length;

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
}

internal static class MultiFactory
{
    public static int EnumerableCalls;
    public static int ArrayCalls;
    public static int ReadOnlySpanCalls;
    public static int WritableSpanCalls;

    public static void Reset()
    {
        EnumerableCalls = 0;
        ArrayCalls = 0;
        ReadOnlySpanCalls = 0;
        WritableSpanCalls = 0;
    }

    public static MultiFactoryCollection<T> Create<T>(IEnumerable<T> items)
    {
        EnumerableCalls++;
        return new([.. items]);
    }

    public static MultiFactoryCollection<T> Create<T>(T[] items)
    {
        ArrayCalls++;
        return new(items);
    }

    public static MultiFactoryCollection<T> Create<T>(ReadOnlySpan<T> items)
    {
        ReadOnlySpanCalls++;
        return new(items.ToArray());
    }

    public static MultiFactoryCollection<T> Create<T>(Span<T> items)
    {
        WritableSpanCalls++;
        return new(items.ToArray());
    }
}

[CollectionBuilder(typeof(SpanPreferenceFactory), "Create")]
internal sealed class SpanPreferenceCollection<T> : IReadOnlyList<T>
{
    private readonly T[] _items;

    internal SpanPreferenceCollection(T[] items) => _items = items;

    public T this[int index] => _items[index];

    public int Count => _items.Length;

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
}

internal static class SpanPreferenceFactory
{
    public static int ReadOnlyCalls;
    public static int WritableCalls;

    public static void Reset()
    {
        ReadOnlyCalls = 0;
        WritableCalls = 0;
    }

    public static SpanPreferenceCollection<T> Create<T>(ReadOnlySpan<T> items)
    {
        ReadOnlyCalls++;
        return new(items.ToArray());
    }

    public static SpanPreferenceCollection<T> Create<T>(Span<T> items)
    {
        WritableCalls++;
        return new(items.ToArray());
    }
}

[CollectionBuilder(typeof(StackSpanFactory), "Create")]
internal sealed class StackSpanCollection<T> : IEnumerable<T>
{
    private readonly List<T> _items = [];

    public void Push(T item) => _items.Insert(0, item);

    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

internal static class StackSpanFactory
{
    public static StackSpanCollection<T> Create<T>(ReadOnlySpan<T> items)
    {
        var stack = new StackSpanCollection<T>();
        foreach (var item in items)
        {
            stack.Push(item);
        }

        return stack;
    }
}

[CollectionBuilder(typeof(NonPublicSpanFactory), "Create")]
internal sealed class NonPublicSpanCollection<T> : IReadOnlyList<T>
{
    private readonly T[] _items;

    internal NonPublicSpanCollection(T[] items) => _items = items;

    public T this[int index] => _items[index];

    public int Count => _items.Length;

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
}

internal static class NonPublicSpanFactory
{
    internal static NonPublicSpanCollection<T> Create<T>(ReadOnlySpan<T> items) =>
        new(items.ToArray());
}

internal sealed class WritableSpanMap<TValue> : IReadOnlyDictionary<string, TValue>
{
    private readonly Dictionary<string, TValue> _items;

    internal WritableSpanMap(Dictionary<string, TValue> items) => _items = items;

    public TValue this[string key] => _items[key];

    public IEnumerable<string> Keys => _items.Keys;

    public IEnumerable<TValue> Values => _items.Values;

    public int Count => _items.Count;

    public bool ContainsKey(string key) => _items.ContainsKey(key);

    public bool TryGetValue(string key, out TValue value)
    {
        if (_items.TryGetValue(key, out var stored))
        {
            value = stored;
            return true;
        }

        value = default!;
        return false;
    }

    public IEnumerator<KeyValuePair<string, TValue>> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

internal static class WritableSpanMap
{
    public static WritableSpanMap<TValue> CreateRange<TValue>(
        Span<KeyValuePair<string, TValue>> items)
    {
        var map = new Dictionary<string, TValue>(StringComparer.Ordinal);
        foreach (var pair in items)
        {
            map[pair.Key] = pair.Value;
        }

        return new WritableSpanMap<TValue>(map);
    }
}

[CollectionBuilder(typeof(SpanOnlyMapFactory), "Create")]
internal sealed class SpanOnlyMap<TValue> : IReadOnlyDictionary<string, TValue>
{
    private readonly Dictionary<string, TValue> _items;

    internal SpanOnlyMap(Dictionary<string, TValue> items) => _items = items;

    public TValue this[string key] => _items[key];

    public IEnumerable<string> Keys => _items.Keys;

    public IEnumerable<TValue> Values => _items.Values;

    public int Count => _items.Count;

    public bool ContainsKey(string key) => _items.ContainsKey(key);

    public bool TryGetValue(string key, out TValue value)
    {
        if (_items.TryGetValue(key, out var stored))
        {
            value = stored;
            return true;
        }

        value = default!;
        return false;
    }

    public IEnumerator<KeyValuePair<string, TValue>> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

internal static class SpanOnlyMapFactory
{
    public static SpanOnlyMap<TValue> Create<TValue>(
        ReadOnlySpan<KeyValuePair<string, TValue>> items)
    {
        var map = new Dictionary<string, TValue>(StringComparer.Ordinal);
        foreach (var pair in items)
        {
            map[pair.Key] = pair.Value;
        }

        return new SpanOnlyMap<TValue>(map);
    }
}

internal sealed class PriorityQueueCountConverter : NbtConverter<PriorityQueue<int, int>>
{
    public override PriorityQueue<int, int> Read(
        ref NbtReader reader,
        NbtSerializerOptions options)
    {
        _ = reader.GetInt32();
        return new PriorityQueue<int, int>();
    }

    public override void Write(
        ref NbtWriter writer,
        PriorityQueue<int, int> value,
        NbtSerializerOptions options) =>
        writer.WriteInt32(value.Count);

    public override bool TryGetTagType(
        PriorityQueue<int, int> value,
        NbtSerializerOptions options,
        out NbtTagType tagType)
    {
        tagType = NbtTagType.Int;
        return true;
    }
}

internal sealed class ConstructorStack<T> : IEnumerable<T>
{
    private readonly List<T> _items = [];

    public ConstructorStack(IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            _items.Insert(0, item);
        }
    }

    public ConstructorStack<T> Push(T item)
    {
        _items.Insert(0, item);
        return this;
    }

    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
}

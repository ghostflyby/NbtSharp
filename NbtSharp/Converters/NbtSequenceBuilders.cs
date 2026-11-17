using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Collections.ObjectModel;

namespace NbtSharp.Converters;

internal struct NbtSetBuilder<TCollection, TElement> : INbtSequenceBuilder<TCollection, TElement>
    where TCollection : IEnumerable<TElement>
{
    private HashSet<TElement> _set;

    public void Create(int count)
    {
        _set = new HashSet<TElement>();
        _set.EnsureCapacity(count);
    }

    public void Add(TElement element) => _set.Add(element);

    public TCollection Complete() => (TCollection)(object)_set;
}

internal struct NbtQueueBuilder<TElement> : INbtSequenceBuilder<Queue<TElement>, TElement>
{
    private Queue<TElement> _queue;

    public void Create(int count) => _queue = new Queue<TElement>(count);

    public void Add(TElement element) => _queue.Enqueue(element);

    public Queue<TElement> Complete() => _queue;
}

internal struct NbtStackBuilder<TElement> : INbtSequenceBuilder<Stack<TElement>, TElement>
{
    private List<TElement> _elements;

    public void Create(int count) => _elements = new List<TElement>(count);

    public void Add(TElement element) => _elements.Add(element);

    public Stack<TElement> Complete()
    {
        var stack = new Stack<TElement>(_elements.Count);
        for (var index = _elements.Count - 1; index >= 0; index--)
        {
            stack.Push(_elements[index]);
        }

        return stack;
    }
}

internal struct NbtLinkedListBuilder<TElement> : INbtSequenceBuilder<LinkedList<TElement>, TElement>
{
    private LinkedList<TElement> _list;

    public void Create(int count) => _list = new LinkedList<TElement>();

    public void Add(TElement element) => _list.AddLast(element);

    public LinkedList<TElement> Complete() => _list;
}

internal struct NbtCollectionBuilder<TElement> :
    INbtSequenceBuilder<Collection<TElement>, TElement>
{
    private Collection<TElement> _collection;

    public void Create(int count) => _collection = new Collection<TElement>();

    public void Add(TElement element) => _collection.Add(element);

    public Collection<TElement> Complete() => _collection;
}

internal struct NbtObservableCollectionBuilder<TElement> :
    INbtSequenceBuilder<ObservableCollection<TElement>, TElement>
{
    private ObservableCollection<TElement> _collection;

    public void Create(int count) => _collection = new ObservableCollection<TElement>();

    public void Add(TElement element) => _collection.Add(element);

    public ObservableCollection<TElement> Complete() => _collection;
}

internal struct NbtReadOnlyCollectionBuilder<TElement> :
    INbtSequenceBuilder<ReadOnlyCollection<TElement>, TElement>
{
    private List<TElement> _elements;

    public void Create(int count) => _elements = new List<TElement>(count);

    public void Add(TElement element) => _elements.Add(element);

    public ReadOnlyCollection<TElement> Complete() => new(_elements);
}

internal struct NbtImmutableArrayBuilder<TElement> :
    INbtSequenceBuilder<ImmutableArray<TElement>, TElement>
{
    private ImmutableArray<TElement>.Builder _builder;

    public void Create(int count) => _builder = ImmutableArray.CreateBuilder<TElement>(count);

    public void Add(TElement element) => _builder.Add(element);

    public ImmutableArray<TElement> Complete() => _builder.MoveToImmutable();
}

internal struct NbtImmutableListBuilder<TCollection, TElement> :
    INbtSequenceBuilder<TCollection, TElement>
    where TCollection : IEnumerable<TElement>
{
    private ImmutableList<TElement>.Builder _builder;

    public void Create(int count) => _builder = ImmutableList.CreateBuilder<TElement>();

    public void Add(TElement element) => _builder.Add(element);

    public TCollection Complete() => (TCollection)(object)_builder.ToImmutable();
}

internal struct NbtImmutableHashSetBuilder<TCollection, TElement> :
    INbtSequenceBuilder<TCollection, TElement>
    where TCollection : IEnumerable<TElement>
{
    private ImmutableHashSet<TElement>.Builder _builder;

    public void Create(int count) => _builder = ImmutableHashSet.CreateBuilder<TElement>();

    public void Add(TElement element) => _builder.Add(element);

    public TCollection Complete() => (TCollection)(object)_builder.ToImmutable();
}

internal struct NbtImmutableQueueBuilder<TCollection, TElement> :
    INbtSequenceBuilder<TCollection, TElement>
    where TCollection : IEnumerable<TElement>
{
    private List<TElement> _elements;

    public void Create(int count) => _elements = new List<TElement>(count);

    public void Add(TElement element) => _elements.Add(element);

    public TCollection Complete() =>
        (TCollection)(object)ImmutableQueue.CreateRange(_elements);
}

internal struct NbtImmutableStackBuilder<TCollection, TElement> :
    INbtSequenceBuilder<TCollection, TElement>
    where TCollection : IEnumerable<TElement>
{
    private List<TElement> _elements;

    public void Create(int count) => _elements = new List<TElement>(count);

    public void Add(TElement element) => _elements.Add(element);

    public TCollection Complete()
    {
        _elements.Reverse();
        return (TCollection)(object)ImmutableStack.CreateRange(_elements);
    }
}

internal struct NbtFrozenSetBuilder<TElement> :
    INbtSequenceBuilder<FrozenSet<TElement>, TElement>
{
    private HashSet<TElement> _set;

    public void Create(int count)
    {
        _set = new HashSet<TElement>();
        _set.EnsureCapacity(count);
    }

    public void Add(TElement element) => _set.Add(element);

    public FrozenSet<TElement> Complete() => _set.ToFrozenSet();
}

internal struct NbtConcurrentBagBuilder<TElement> :
    INbtSequenceBuilder<ConcurrentBag<TElement>, TElement>
{
    private ConcurrentBag<TElement> _bag;

    public void Create(int count) => _bag = new ConcurrentBag<TElement>();

    public void Add(TElement element) => _bag.Add(element);

    public ConcurrentBag<TElement> Complete() => _bag;
}

internal struct NbtConcurrentQueueBuilder<TElement> :
    INbtSequenceBuilder<ConcurrentQueue<TElement>, TElement>
{
    private ConcurrentQueue<TElement> _queue;

    public void Create(int count) => _queue = new ConcurrentQueue<TElement>();

    public void Add(TElement element) => _queue.Enqueue(element);

    public ConcurrentQueue<TElement> Complete() => _queue;
}

internal struct NbtConcurrentStackBuilder<TElement> :
    INbtSequenceBuilder<ConcurrentStack<TElement>, TElement>
{
    private List<TElement> _elements;

    public void Create(int count) => _elements = new List<TElement>(count);

    public void Add(TElement element) => _elements.Add(element);

    public ConcurrentStack<TElement> Complete()
    {
        var stack = new ConcurrentStack<TElement>();
        for (var index = _elements.Count - 1; index >= 0; index--)
        {
            stack.Push(_elements[index]);
        }

        return stack;
    }
}

internal struct NbtSortedSetBuilder<TElement> :
    INbtSequenceBuilder<SortedSet<TElement>, TElement>
{
    private SortedSet<TElement> _set;

    public void Create(int count) => _set = new SortedSet<TElement>();

    public void Add(TElement element) => _set.Add(element);

    public SortedSet<TElement> Complete() => _set;
}

internal struct NbtBlockingCollectionBuilder<TElement> :
    INbtSequenceBuilder<BlockingCollection<TElement>, TElement>
{
    private BlockingCollection<TElement> _collection;

    public void Create(int count) => _collection = new BlockingCollection<TElement>();

    public void Add(TElement element) => _collection.Add(element);

    public BlockingCollection<TElement> Complete() => _collection;
}

internal struct NbtImmutableSortedSetBuilder<TElement> :
    INbtSequenceBuilder<ImmutableSortedSet<TElement>, TElement>
{
    private ImmutableSortedSet<TElement>.Builder _builder;

    public void Create(int count) => _builder = ImmutableSortedSet.CreateBuilder<TElement>();

    public void Add(TElement element) => _builder.Add(element);

    public ImmutableSortedSet<TElement> Complete() => _builder.ToImmutable();
}

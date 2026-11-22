using System.Collections;

// The public NbtSharp namespace is intentional; the folder groups internal implementation.
// ReSharper disable once CheckNamespace
namespace NbtSharp;

/// <summary>
/// A converter list that invalidates the options cache whenever its contents change. Every mutation
/// runs through <see cref="NbtSerializerOptions.MutateConverters{T}"/>, which rejects changes to a
/// frozen options instance and invalidates the cache inside the same critical section.
/// </summary>
internal sealed class NbtConverterCollection(NbtSerializerOptions options, List<NbtConverter> items)
    : IList<NbtConverter>
{
    private readonly List<NbtConverter> _items = items;

    public NbtConverter this[int index]
    {
        get => _items[index];
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            options.MutateConverters(items => items[index] = value);
        }
    }

    public int Count => _items.Count;

    public bool IsReadOnly => false;

    public void Add(NbtConverter item)
    {
        ArgumentNullException.ThrowIfNull(item);
        options.MutateConverters(items => items.Add(item));
    }

    public void Clear()
    {
        options.MutateConverters(static items => items.Clear());
    }

    public bool Contains(NbtConverter item) => _items.Contains(item);

    public void CopyTo(NbtConverter[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

    public IEnumerator<NbtConverter> GetEnumerator() => _items.GetEnumerator();

    public int IndexOf(NbtConverter item) => _items.IndexOf(item);

    public void Insert(int index, NbtConverter item)
    {
        ArgumentNullException.ThrowIfNull(item);
        options.MutateConverters(items => items.Insert(index, item));
    }

    public bool Remove(NbtConverter item) => options.MutateConverters(items => items.Remove(item));

    public void RemoveAt(int index)
    {
        options.MutateConverters(items => items.RemoveAt(index));
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

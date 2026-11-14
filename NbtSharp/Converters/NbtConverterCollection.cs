using System.Collections;

namespace NbtSharp;

/// <summary>
/// A converter list that invalidates the options cache whenever its contents change.
/// </summary>
internal sealed class NbtConverterCollection(NbtSerializerOptions options) : IList<NbtConverter>
{
    private readonly List<NbtConverter> _items = [];

    public NbtConverter this[int index]
    {
        get => _items[index];
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _items[index] = value;
            options.Invalidate();
        }
    }

    public int Count => _items.Count;

    public bool IsReadOnly => false;

    public void Add(NbtConverter item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _items.Add(item);
        options.Invalidate();
    }

    public void Clear()
    {
        _items.Clear();
        options.Invalidate();
    }

    public bool Contains(NbtConverter item) => _items.Contains(item);

    public void CopyTo(NbtConverter[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

    public IEnumerator<NbtConverter> GetEnumerator() => _items.GetEnumerator();

    public int IndexOf(NbtConverter item) => _items.IndexOf(item);

    public void Insert(int index, NbtConverter item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _items.Insert(index, item);
        options.Invalidate();
    }

    public bool Remove(NbtConverter item)
    {
        var removed = _items.Remove(item);
        if (removed)
        {
            options.Invalidate();
        }

        return removed;
    }

    public void RemoveAt(int index)
    {
        _items.RemoveAt(index);
        options.Invalidate();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

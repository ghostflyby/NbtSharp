using System.Collections;

namespace NbtSharp;

/// <summary>
/// A <c>TAG_Compound</c> value: a read-only view over the document's slot index.
/// </summary>
/// <remarks>
/// <para>
/// Members are produced on demand as <see cref="NbtElement"/> values; no member tree is
/// materialized. Count, the indexer, and <see cref="TryGetValue"/> do not allocate. Enumeration yields
/// <see cref="KeyValuePair{TKey,TValue}"/> in wire order and decodes each member name to form the
/// pair key. The view stays valid only while its document is alive and undisposed.
/// </para>
/// <para>
/// Duplicate member names are preserved by <see cref="Count"/> and enumeration, while the indexer
/// and <see cref="TryGetValue"/> resolve the last occurrence. The view is an enumerable collection
/// of pairs, not <see cref="IReadOnlyDictionary{TKey,TValue}"/>.
/// </para>
/// </remarks>
public readonly struct NbtCompoundElement : IReadOnlyCollection<KeyValuePair<string, NbtElement>>
{
    internal NbtCompoundElement(NbtDocument owner, int slotIndex)
    {
        Owner = owner;
        SlotIndex = slotIndex;
    }

    /// <summary>Gets the owning document, when the view was created by one.</summary>
    internal NbtDocument? Owner { get; }

    /// <summary>Gets the document slot index of this view.</summary>
    internal int SlotIndex { get; }

    /// <summary>Gets the number of members.</summary>
    /// <exception cref="InvalidOperationException">The view is default-initialized.</exception>
    /// <exception cref="NbtException">The document slot is not a compound.</exception>
    /// <exception cref="ObjectDisposedException">The owning document has been disposed.</exception>
    public int Count
    {
        get
        {
            ref readonly var slot = ref ExpectSlot();
            return slot.ChildCount;
        }
    }

    /// <summary>Gets the member with the specified key.</summary>
    /// <param name="key">The member name.</param>
    /// <returns>The member.</returns>
    /// <remarks>When the key occurs more than once, the last occurrence wins.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The view is default-initialized.</exception>
    /// <exception cref="KeyNotFoundException">No member has the specified key.</exception>
    /// <exception cref="NbtException">The document slot is not a compound.</exception>
    /// <exception cref="ObjectDisposedException">The owning document has been disposed.</exception>
    public NbtElement this[string key]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(key);
            if (!TryGetValue(key, out var value))
            {
                throw new KeyNotFoundException($"The given key '{key}' was not present in the dictionary.");
            }

            return value;
        }
    }

    /// <summary>Gets the member with the specified key, when present.</summary>
    /// <param name="key">The member name.</param>
    /// <param name="value">The member, or a default value when absent.</param>
    /// <returns><see langword="true"/> when the member was found.</returns>
    /// <remarks>
    /// The query string is compared with each member's wire name by decoded value. Wire names
    /// decode into a stack buffer when at most 256 chars, or a pooled buffer otherwise, so no
    /// strings are allocated. When the key occurs more than once, the last occurrence wins.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The view is default-initialized.</exception>
    /// <exception cref="NbtException">
    /// The document slot is not a compound, or a wire name is not valid modified UTF-8.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The owning document has been disposed.</exception>
    public bool TryGetValue(string key, out NbtElement value)
    {
        ArgumentNullException.ThrowIfNull(key);
        var document = Document;
        ref readonly var slot = ref ExpectSlot();

        var query = key.AsSpan();
        var child = SlotIndex + 1;
        var lastMatch = -1;
        for (var i = 0; i < slot.ChildCount; i++)
        {
            ref readonly var member = ref document.GetSlot(child);
            var recorded = document.DataSpan.Slice(member.NameOffset, member.NameLength);
            if (NbtText.NameEquals(recorded, query, member.NameOffset - sizeof(ushort)))
            {
                lastMatch = child;
            }

            child += 1 + member.DescendantCount;
        }

        if (lastMatch < 0)
        {
            value = default;
            return false;
        }

        value = document.CreateElement(lastMatch);
        return true;
    }

    /// <summary>Gets an enumerator over the members in wire order.</summary>
    /// <returns>The enumerator.</returns>
    /// <exception cref="InvalidOperationException">The view is default-initialized.</exception>
    /// <exception cref="NbtException">The document slot is not a compound.</exception>
    /// <exception cref="ObjectDisposedException">The owning document has been disposed.</exception>
    public Enumerator GetEnumerator()
    {
        _ = ExpectSlot();
        return new Enumerator(Document, SlotIndex);
    }

    IEnumerator<KeyValuePair<string, NbtElement>> IEnumerable<KeyValuePair<string, NbtElement>>.GetEnumerator() =>
        GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private NbtDocument Document => Owner ?? throw new InvalidOperationException(
        "The operation is not valid for a default-initialized NbtCompoundElement.");

    private ref readonly NbtDocumentSlot ExpectSlot()
    {
        var document = Document;
        ref readonly var slot = ref document.GetSlot(SlotIndex);
        if (slot.TagType != NbtTagType.Compound)
        {
            throw new NbtException(
                $"Expected a {NbtTagType.Compound} value but the current value is {slot.TagType}.")
            {
                ExpectedTagType = NbtTagType.Compound,
                ActualTagType = slot.TagType,
            };
        }

        return ref slot;
    }

    /// <summary>Enumerates the members of a compound in wire order.</summary>
    /// <remarks>
    /// <see cref="Current"/> produces a <see cref="KeyValuePair{TKey,TValue}"/> whose key is the
    /// decoded member name.
    /// </remarks>
    public struct Enumerator : IEnumerator<KeyValuePair<string, NbtElement>>
    {
        private readonly NbtDocument _document;
        private readonly int _parentSlot;
        private readonly int _count;
        private int _index;
        private int _childSlot;
        private int _currentSlot;
        private NbtElement _current;

        internal Enumerator(NbtDocument document, int parentSlot)
        {
            _document = document;
            _parentSlot = parentSlot;
            _count = document.GetSlot(parentSlot).ChildCount;
            _index = -1;
            _childSlot = parentSlot + 1;
            _currentSlot = -1;
            _current = default;
        }

        /// <summary>Gets the current member pair.</summary>
        /// <exception cref="InvalidOperationException">
        /// The enumerator is not positioned on a member, or is default-initialized.
        /// </exception>
        /// <exception cref="ObjectDisposedException">The owning document has been disposed.</exception>
        /// <exception cref="NbtException">The current member name is not valid modified UTF-8.</exception>
        public KeyValuePair<string, NbtElement> Current
        {
            get
            {
                if (_document is null)
                {
                    throw new InvalidOperationException("The enumerator is not positioned on an element.");
                }

                _document.ThrowIfDisposed();
                if (_currentSlot < 0)
                {
                    throw new InvalidOperationException("The enumerator is not positioned on an element.");
                }

                return new(_document.NameAt(_currentSlot), _current);
            }
        }

        object IEnumerator.Current => Current;

        /// <summary>Advances to the next member.</summary>
        /// <returns><see langword="false"/> when the enumeration is complete.</returns>
        /// <exception cref="InvalidOperationException">The enumerator is default-initialized.</exception>
        /// <exception cref="ObjectDisposedException">The owning document has been disposed.</exception>
        public bool MoveNext()
        {
            if (_document is null)
            {
                throw new InvalidOperationException("The enumerator is not initialized.");
            }

            if (_index + 1 >= _count)
            {
                _index = _count;
                _currentSlot = -1;
                _current = default;
                return false;
            }

            _index++;
            _currentSlot = _childSlot;
            _current = _document.CreateElement(_currentSlot);
            _childSlot += 1 + _document.GetSlot(_currentSlot).DescendantCount;
            return true;
        }

        /// <summary>Resets the enumerator to the start of the compound.</summary>
        /// <exception cref="InvalidOperationException">The enumerator is default-initialized.</exception>
        public void Reset()
        {
            if (_document is null)
            {
                throw new InvalidOperationException("The enumerator is not initialized.");
            }

            _index = -1;
            _childSlot = _parentSlot + 1;
            _currentSlot = -1;
            _current = default;
        }

        /// <summary>Releases the enumerator. The enumerator holds no resources.</summary>
        public void Dispose()
        {
        }

        /// <summary>Returns this enumerator.</summary>
        /// <returns>This enumerator.</returns>
        public Enumerator GetEnumerator() => this;
    }
}

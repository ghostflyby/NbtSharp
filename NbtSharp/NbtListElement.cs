using System.Collections;

namespace NbtSharp;

/// <summary>
/// A <c>TAG_List</c> value: a zero-allocation view over the document's slot index.
/// </summary>
/// <remarks>
/// <para>
/// Elements are produced on demand as <see cref="NbtElement"/> values; nothing is materialized and
/// the view allocates nothing while navigating. The view stays valid only while its document is
/// alive and undisposed.
/// </para>
/// <para>
/// The view implements <see cref="IReadOnlyList{T}"/>; interface enumeration boxes the struct
/// enumerator, while direct <see langword="foreach"/> over the view stays allocation-free.
/// </para>
/// </remarks>
public readonly struct NbtListElement : IReadOnlyList<NbtElement>
{
    internal NbtListElement(NbtDocument owner, int slotIndex)
    {
        Owner = owner;
        SlotIndex = slotIndex;
    }

    /// <summary>Gets the owning document, when the view was created by one.</summary>
    internal NbtDocument? Owner { get; }

    /// <summary>Gets the document slot index of this view.</summary>
    internal int SlotIndex { get; }

    /// <summary>Gets the tag type declared by the list header.</summary>
    /// <exception cref="NbtException">The document slot is not a list.</exception>
    /// <exception cref="ObjectDisposedException">The owning document has been disposed.</exception>
    public NbtTagType ElementType
    {
        get
        {
            ref readonly var slot = ref ExpectSlot();
            return slot.ElementType;
        }
    }

    /// <summary>Gets the number of elements.</summary>
    /// <exception cref="NbtException">The document slot is not a list.</exception>
    /// <exception cref="ObjectDisposedException">The owning document has been disposed.</exception>
    public int Count
    {
        get
        {
            ref readonly var slot = ref ExpectSlot();
            return slot.ChildCount;
        }
    }

    /// <summary>Gets the element at <paramref name="index"/>.</summary>
    /// <param name="index">The zero-based element index.</param>
    /// <returns>The element.</returns>
    /// <remarks>
    /// Scalar, string, and array elements are addressed in constant time; lists and compounds walk
    /// to the index, which is proportional to the index.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="index"/> is negative or not less than <see cref="Count"/>.
    /// </exception>
    /// <exception cref="NbtException">The document slot is not a list.</exception>
    /// <exception cref="ObjectDisposedException">The owning document has been disposed.</exception>
    public NbtElement this[int index]
    {
        get
        {
            var document = Document;
            ref readonly var slot = ref ExpectSlot();
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, slot.ChildCount);

            var child = SlotIndex + 1;
            if (slot.ElementType is not (NbtTagType.List or NbtTagType.Compound))
            {
                // Scalar, string, and array elements occupy consecutive slots.
                return document.CreateElement(child + index);
            }

            for (var i = 0; i < index; i++)
            {
                child += 1 + document.GetSlot(child).DescendantCount;
            }

            return document.CreateElement(child);
        }
    }


    /// <summary>Gets an enumerator over the elements in wire order.</summary>
    /// <returns>The enumerator.</returns>
    /// <exception cref="NbtException">The document slot is not a list.</exception>
    /// <exception cref="ObjectDisposedException">The owning document has been disposed.</exception>
    public Enumerator GetEnumerator()
    {
        _ = ExpectSlot();
        return new Enumerator(Document, SlotIndex);
    }

    IEnumerator<NbtElement> IEnumerable<NbtElement>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private NbtDocument Document => Owner ?? throw new InvalidOperationException(
        "The operation is not valid for a default-initialized NbtListElement.");

    private ref readonly NbtDocumentSlot ExpectSlot()
    {
        var document = Document;
        ref readonly var slot = ref document.GetSlot(SlotIndex);
        if (slot.TagType != NbtTagType.List)
        {
            throw new NbtException(
                $"Expected a {NbtTagType.List} value but the current value is {slot.TagType}.")
            {
                ExpectedTagType = NbtTagType.List,
                ActualTagType = slot.TagType,
            };
        }

        return ref slot;
    }

    /// <summary>Enumerates the elements of a list in wire order.</summary>
    public struct Enumerator : IEnumerator<NbtElement>
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

        /// <summary>Gets the current element.</summary>
        public NbtElement Current => _current;

        object IEnumerator.Current => _current;

        /// <summary>Advances to the next element.</summary>
        /// <returns><see langword="false"/> when the enumeration is complete.</returns>
        public bool MoveNext()
        {
            if (_index + 1 >= _count)
            {
                return false;
            }

            _index++;
            _currentSlot = _childSlot;
            _current = _document.CreateElement(_currentSlot);
            _childSlot += 1 + _document.GetSlot(_currentSlot).DescendantCount;
            return true;
        }

        /// <summary>Resets the enumerator to the start of the list.</summary>
        public void Reset()
        {
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

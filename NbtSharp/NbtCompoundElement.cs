using System.Collections;

namespace NbtSharp;

/// <summary>
/// A <c>TAG_Compound</c> value: a zero-allocation view over the document's slot index.
/// </summary>
/// <remarks>
/// <para>
/// Members are produced on demand as <see cref="NbtElement"/> values; nothing is materialized and
/// the view allocates nothing while navigating. The view stays valid only while its document is
/// alive and undisposed.
/// </para>
/// </remarks>
public readonly struct NbtCompoundElement
{
    private readonly NbtDocument? _owner;
    private readonly int _slotIndex;

    internal NbtCompoundElement(NbtDocument owner, int slotIndex)
    {
        _owner = owner;
        _slotIndex = slotIndex;
    }

    /// <summary>Gets the owning document, when the view was created by one.</summary>
    internal NbtDocument? Owner => _owner;

    /// <summary>Gets the document slot index of this view.</summary>
    internal int SlotIndex => _slotIndex;

    /// <summary>Gets the number of members.</summary>
    /// <exception cref="NbtException">The document slot is not a compound.</exception>
    /// <exception cref="ObjectDisposedException">The owning document has been disposed.</exception>
    public int MemberCount
    {
        get
        {
            ref readonly var slot = ref ExpectSlot();
            return slot.ChildCount;
        }
    }

    /// <summary>Gets the member with the specified name.</summary>
    /// <param name="name">The member name.</param>
    /// <returns>The member.</returns>
    /// <remarks>When the name occurs more than once, the last occurrence wins.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="KeyNotFoundException">No member has the specified name.</exception>
    /// <exception cref="NbtException">The document slot is not a compound.</exception>
    /// <exception cref="ObjectDisposedException">The owning document has been disposed.</exception>
    public NbtElement GetMember(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!TryGetMember(name, out var value))
        {
            throw new KeyNotFoundException($"The given key '{name}' was not present in the dictionary.");
        }

        return value;
    }

    /// <summary>Gets the member with the specified name, when present.</summary>
    /// <param name="name">The member name.</param>
    /// <param name="value">The member, or a default value when absent.</param>
    /// <returns><see langword="true"/> when the member was found.</returns>
    /// <remarks>
    /// The query string is compared with each member's wire name by decoded value. Wire names
    /// decode into a stack buffer when at most 256 chars, or a pooled buffer otherwise, so no
    /// strings are allocated. When the name occurs more than once, the last occurrence wins.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="NbtException">
    /// The document slot is not a compound, or a wire name is not valid modified UTF-8.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The owning document has been disposed.</exception>
    public bool TryGetMember(string name, out NbtElement value)
    {
        ArgumentNullException.ThrowIfNull(name);
        var document = Document;
        ref readonly var slot = ref ExpectSlot();

        var query = name.AsSpan();
        var child = _slotIndex + 1;
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
    /// <exception cref="NbtException">The document slot is not a compound.</exception>
    /// <exception cref="ObjectDisposedException">The owning document has been disposed.</exception>
    public Enumerator GetEnumerator()
    {
        _ = ExpectSlot();
        return new Enumerator(Document, _slotIndex);
    }

    /// <summary>Gets an enumerator over the members in wire order.</summary>
    /// <returns>The enumerator.</returns>
    public Enumerator EnumerateMembers() => GetEnumerator();

    private NbtDocument Document => _owner ?? throw new InvalidOperationException(
        "The operation is not valid for a default-initialized NbtCompoundElement.");

    private ref readonly NbtDocumentSlot ExpectSlot()
    {
        var document = Document;
        ref readonly var slot = ref document.GetSlot(_slotIndex);
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
    /// <see cref="Current"/> produces the member value; <see cref="Name"/> decodes the member name
    /// on demand, so enumerations that do not read names stay allocation-free.
    /// </remarks>
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

        /// <summary>Gets the current member value.</summary>
        public NbtElement Current => _current;

        /// <summary>Gets the current member name.</summary>
        public string Name => _document.NameAt(_currentSlot);

        object IEnumerator.Current => _current;

        /// <summary>Advances to the next member.</summary>
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

        /// <summary>Resets the enumerator to the start of the compound.</summary>
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

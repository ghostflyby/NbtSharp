using System.ComponentModel;

namespace NbtSharp;

/// <summary>
/// Builds one sequence collection while a generated converter reads elements.
/// </summary>
/// <remarks>
/// This type is public so generated code can implement it. It is not intended for direct use.
/// </remarks>
/// <typeparam name="TCollection">The declared collection type.</typeparam>
/// <typeparam name="TElement">The element type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface INbtSequenceBuilder<TCollection, TElement>
    where TCollection : IEnumerable<TElement>
{
    /// <summary>Initializes the builder for a list that declares <paramref name="count"/> elements.</summary>
    /// <param name="count">The declared element count.</param>
    void Create(int count);

    /// <summary>Adds one element.</summary>
    /// <param name="element">The element to add.</param>
    void Add(TElement element);

    /// <summary>Produces the completed collection.</summary>
    /// <returns>The completed collection.</returns>
    TCollection Complete();
}

/// <summary>
/// Builds one string-keyed map while a generated converter reads compound members.
/// </summary>
/// <remarks>
/// This type is public so generated code can implement it. It is not intended for direct use.
/// </remarks>
/// <typeparam name="TCollection">The declared dictionary type.</typeparam>
/// <typeparam name="TValue">The value type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface INbtMapBuilder<TCollection, TValue>
    where TCollection : IEnumerable<KeyValuePair<string, TValue>>
{
    /// <summary>Initializes the builder.</summary>
    void Create();

    /// <summary>Gets whether the map already contains <paramref name="key"/>.</summary>
    /// <param name="key">The member name.</param>
    /// <returns><see langword="true"/> when the key is already present.</returns>
    bool ContainsKey(string key);

    /// <summary>Adds or replaces one member.</summary>
    /// <param name="key">The member name.</param>
    /// <param name="value">The member value.</param>
    void Add(string key, TValue value);

    /// <summary>Produces the completed map.</summary>
    /// <returns>The completed map.</returns>
    TCollection Complete();
}

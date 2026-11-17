using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace NbtSharp.Converters;

internal struct NbtDictionaryBuilder<TCollection, TValue> :
    INbtMapBuilder<TCollection, TValue>
    where TCollection : IEnumerable<KeyValuePair<string, TValue>>
{
    private Dictionary<string, TValue> _dictionary;

    public void Create() => _dictionary = new Dictionary<string, TValue>(StringComparer.Ordinal);

    public bool ContainsKey(string key) => _dictionary.ContainsKey(key);

    public void Add(string key, TValue value) => _dictionary[key] = value;

    public TCollection Complete() => (TCollection)(object)_dictionary;
}

internal struct NbtReadOnlyDictionaryBuilder<TValue> :
    INbtMapBuilder<ReadOnlyDictionary<string, TValue>, TValue>
{
    private Dictionary<string, TValue> _dictionary;

    public void Create() => _dictionary = new Dictionary<string, TValue>(StringComparer.Ordinal);

    public bool ContainsKey(string key) => _dictionary.ContainsKey(key);

    public void Add(string key, TValue value) => _dictionary[key] = value;

    public ReadOnlyDictionary<string, TValue> Complete() => new(_dictionary);
}

internal struct NbtImmutableDictionaryBuilder<TCollection, TValue> :
    INbtMapBuilder<TCollection, TValue>
    where TCollection : IEnumerable<KeyValuePair<string, TValue>>
{
    private ImmutableDictionary<string, TValue>.Builder _builder;

    public void Create() =>
        _builder = ImmutableDictionary.CreateBuilder<string, TValue>(StringComparer.Ordinal);

    public bool ContainsKey(string key) => _builder.ContainsKey(key);

    public void Add(string key, TValue value) => _builder[key] = value;

    public TCollection Complete() => (TCollection)(object)_builder.ToImmutable();
}

internal struct NbtFrozenDictionaryBuilder<TValue> :
    INbtMapBuilder<FrozenDictionary<string, TValue>, TValue>
{
    private Dictionary<string, TValue> _dictionary;

    public void Create() => _dictionary = new Dictionary<string, TValue>(StringComparer.Ordinal);

    public bool ContainsKey(string key) => _dictionary.ContainsKey(key);

    public void Add(string key, TValue value) => _dictionary[key] = value;

    public FrozenDictionary<string, TValue> Complete() =>
        _dictionary.ToFrozenDictionary(StringComparer.Ordinal);
}

internal struct NbtConcurrentDictionaryBuilder<TValue> :
    INbtMapBuilder<ConcurrentDictionary<string, TValue>, TValue>
{
    private ConcurrentDictionary<string, TValue> _dictionary;

    public void Create() =>
        _dictionary = new ConcurrentDictionary<string, TValue>(StringComparer.Ordinal);

    public bool ContainsKey(string key) => _dictionary.ContainsKey(key);

    public void Add(string key, TValue value) => _dictionary[key] = value;

    public ConcurrentDictionary<string, TValue> Complete() => _dictionary;
}

internal struct NbtSortedDictionaryBuilder<TValue> :
    INbtMapBuilder<SortedDictionary<string, TValue>, TValue>
{
    private SortedDictionary<string, TValue> _dictionary;

    public void Create() =>
        _dictionary = new SortedDictionary<string, TValue>(StringComparer.Ordinal);

    public bool ContainsKey(string key) => _dictionary.ContainsKey(key);

    public void Add(string key, TValue value) => _dictionary[key] = value;

    public SortedDictionary<string, TValue> Complete() => _dictionary;
}

internal struct NbtSortedListBuilder<TValue> :
    INbtMapBuilder<SortedList<string, TValue>, TValue>
{
    private SortedList<string, TValue> _list;

    public void Create() => _list = new SortedList<string, TValue>(StringComparer.Ordinal);

    public bool ContainsKey(string key) => _list.ContainsKey(key);

    public void Add(string key, TValue value) => _list[key] = value;

    public SortedList<string, TValue> Complete() => _list;
}

internal struct NbtImmutableSortedDictionaryBuilder<TCollection, TValue> :
    INbtMapBuilder<TCollection, TValue>
    where TCollection : IEnumerable<KeyValuePair<string, TValue>>
{
    private ImmutableSortedDictionary<string, TValue>.Builder _builder;

    public void Create() =>
        _builder = ImmutableSortedDictionary.CreateBuilder<string, TValue>(
            StringComparer.Ordinal);

    public bool ContainsKey(string key) => _builder.ContainsKey(key);

    public void Add(string key, TValue value) => _builder[key] = value;

    public TCollection Complete() => (TCollection)(object)_builder.ToImmutable();
}

/// <summary>
/// Reads a compound into a string-keyed map produced by <typeparamref name="TBuilder"/>.
/// </summary>
[UnconditionalSuppressMessage("Aot", "IL2026", Justification = "Reached only through annotated serializer entry points.")]
[UnconditionalSuppressMessage("Aot", "IL3050", Justification = "Reached only through annotated serializer entry points.")]
internal sealed class NbtMapConverter<TBuilder, TCollection, TValue> :
    NbtConverter<TCollection>
    where TCollection : IEnumerable<KeyValuePair<string, TValue>>
    where TBuilder : struct, INbtMapBuilder<TCollection, TValue>
{
    public override TCollection Read(ref NbtReader reader, NbtSerializerOptions options)
    {
        if (reader.TokenType != NbtTokenType.StartCompound)
        {
            throw new NbtException(
                $"Expected TAG_Compound but the current token is {reader.TokenType} ({reader.TagType}).")
            {
                BytePosition = reader.BytesConsumed,
                ExpectedTagType = NbtTagType.Compound,
                ActualTagType = reader.TagType,
            };
        }

        var builder = default(TBuilder);
        builder.Create();
        NbtConverter? converter = null;
        NbtConverter<TValue>? typedConverter = null;
        while (reader.Read())
        {
            if (reader.TokenType == NbtTokenType.EndCompound)
            {
                return builder.Complete();
            }

            var name = reader.Name;
            if (name is null)
            {
                throw new NbtException("A compound member has no name.")
                {
                    BytePosition = reader.BytesConsumed,
                };
            }

            if (options.RejectDuplicateCompoundNames && builder.ContainsKey(name))
            {
                throw new NbtException($"The compound contains the duplicate member '{name}'.")
                {
                    BytePosition = reader.BytesConsumed,
                    Path = "." + name,
                };
            }

            if (converter is null)
            {
                converter = options.GetConverter(typeof(TValue));
                typedConverter = converter as NbtConverter<TValue>;
            }

            try
            {
                var value = typedConverter is not null
                    ? NbtSerializer.DeserializeValueCore(
                        ref reader,
                        typedConverter,
                        options,
                        pathSegment: null)
                    : (TValue)NbtSerializer.DeserializeValueCore(
                        ref reader,
                        typeof(TValue),
                        converter,
                        options,
                        pathSegment: null)!;
                builder.Add(name, value);
            }
            catch (NbtException ex)
            {
                NbtSerializer.PrependPath(ex, "." + name);
                throw;
            }
        }

        throw new NbtException("The compound was not terminated.")
        {
            IsTruncated = true,
            BytePosition = reader.BytesConsumed,
        };
    }

    public override void Write(
        ref NbtWriter writer,
        TCollection value,
        NbtSerializerOptions options) =>
        NbtCollectionWriter.WriteMap<TCollection, TValue>(ref writer, value, options);

    public override bool TryGetTagType(
        TCollection value,
        NbtSerializerOptions options,
        out NbtTagType tagType)
    {
        tagType = NbtTagType.Compound;
        return true;
    }
}

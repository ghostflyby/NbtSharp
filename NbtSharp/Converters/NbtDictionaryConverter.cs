using System.Diagnostics.CodeAnalysis;

namespace NbtSharp.Converters;

/// <summary>
/// Converts string-keyed dictionaries and dictionary interfaces into compounds.
/// </summary>
[UnconditionalSuppressMessage("Aot", "IL2026", Justification = "Reached only through annotated serializer entry points.")]
[UnconditionalSuppressMessage("Aot", "IL3050", Justification = "Reached only through annotated serializer entry points.")]
internal sealed class NbtDictionaryConverter<TDictionary, TValue> : NbtConverter<TDictionary>
    where TDictionary : IEnumerable<KeyValuePair<string, TValue>>
{
    public override TDictionary Read(ref NbtReader reader, NbtSerializerOptions options)
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

        var dictionary = new Dictionary<string, TValue>(StringComparer.Ordinal);
        while (reader.Read())
        {
            if (reader.TokenType == NbtTokenType.EndCompound)
            {
                return (TDictionary)(object)dictionary;
            }

            var name = reader.Name;
            if (name is null)
            {
                throw new NbtException("A compound member has no name.")
                {
                    BytePosition = reader.BytesConsumed,
                };
            }

            if (options.RejectDuplicateCompoundNames && dictionary.ContainsKey(name))
            {
                throw new NbtException($"The compound contains the duplicate member '{name}'.")
                {
                    BytePosition = reader.BytesConsumed,
                    Path = "." + name,
                };
            }

            dictionary[name] = (TValue)NbtSerializer.DeserializeValueInternal(
                ref reader,
                typeof(TValue),
                options,
                "." + name)!;
        }

        throw new NbtException("The compound was not terminated.")
        {
            BytePosition = reader.BytesConsumed,
        };
    }

    public override void Write(ref NbtWriter writer, TDictionary value, NbtSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);

        writer.WriteStartCompound();
        foreach (var pair in value)
        {
            NbtSerializer.SerializeMemberInternal(
                ref writer,
                pair.Key,
                pair.Value,
                typeof(TValue),
                options);
        }

        writer.WriteEndCompound();
    }

    public override bool TryGetTagType(TDictionary value, NbtSerializerOptions options, out NbtTagType tagType)
    {
        tagType = NbtTagType.Compound;
        return true;
    }
}

using System.Diagnostics.CodeAnalysis;

namespace NbtSharp.Converters;

[UnconditionalSuppressMessage("Aot", "IL2026", Justification = "Reached only through annotated serializer entry points.")]
[UnconditionalSuppressMessage("Aot", "IL3050", Justification = "Reached only through annotated serializer entry points.")]
internal static class NbtCollectionReader
{
    /// <summary>
    /// Reads a list whose start token is current and returns its elements. The reader must be
    /// positioned on a <see cref="NbtTokenType.StartList"/> token.
    /// </summary>
    public static List<TElement> ReadList<TElement>(ref NbtReader reader, NbtSerializerOptions options)
    {
        if (reader.TokenType != NbtTokenType.StartList)
        {
            throw new NbtException(
                $"Expected TAG_List but the current token is {reader.TokenType} ({reader.TagType}).")
            {
                BytePosition = reader.BytesConsumed,
                ExpectedTagType = NbtTagType.List,
                ActualTagType = reader.TagType,
            };
        }

        var count = reader.ListLength;
        var declaredType = reader.ListElementType;
        if (count == 0 &&
            declaredType != NbtTagType.End &&
            !HasCustomConverter(options, typeof(TElement)) &&
            TryGetKnownElementType(typeof(TElement), out var expectedType) &&
            expectedType != declaredType)
        {
            throw new NbtException(
                $"The list declares TAG_List<{declaredType}> but {typeof(TElement)} expects TAG_List<{expectedType}>.")
            {
                BytePosition = reader.BytesConsumed,
                ExpectedTagType = expectedType,
                ActualTagType = declaredType,
            };
        }

        var list = new List<TElement>(count <= 4096 ? count : 0);
        for (var i = 0; i < count; i++)
        {
            if (!reader.Read())
            {
                throw new NbtException("The list ended before all declared elements were read.")
                {
                    BytePosition = reader.BytesConsumed,
                };
            }

            list.Add((TElement)NbtSerializer.DeserializeValueInternal(
                ref reader,
                typeof(TElement),
                options,
                $"[{i}]")!);
        }

        if (!reader.Read() || reader.TokenType != NbtTokenType.EndList)
        {
            throw new NbtException("The list was not terminated.")
            {
                BytePosition = reader.BytesConsumed,
            };
        }

        return list;
    }

    private static bool HasCustomConverter(NbtSerializerOptions options, Type type)
    {
        foreach (var converter in options.Converters)
        {
            if (converter.CanConvert(type))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetKnownElementType(Type type, out NbtTagType tagType)
    {
        if (type == typeof(sbyte) || type == typeof(byte) || type == typeof(bool))
        {
            tagType = NbtTagType.Byte;
            return true;
        }

        if (type == typeof(short))
        {
            tagType = NbtTagType.Short;
            return true;
        }

        if (type == typeof(int))
        {
            tagType = NbtTagType.Int;
            return true;
        }

        if (type == typeof(long))
        {
            tagType = NbtTagType.Long;
            return true;
        }

        if (type == typeof(float))
        {
            tagType = NbtTagType.Float;
            return true;
        }

        if (type == typeof(double))
        {
            tagType = NbtTagType.Double;
            return true;
        }

        if (type == typeof(string))
        {
            tagType = NbtTagType.String;
            return true;
        }

        if (type == typeof(byte[]) || type == typeof(sbyte[]))
        {
            tagType = NbtTagType.ByteArray;
            return true;
        }

        if (type == typeof(int[]))
        {
            tagType = NbtTagType.IntArray;
            return true;
        }

        if (type == typeof(long[]))
        {
            tagType = NbtTagType.LongArray;
            return true;
        }

        if (type == typeof(NbtCompound))
        {
            tagType = NbtTagType.Compound;
            return true;
        }

        if (type == typeof(NbtList))
        {
            tagType = NbtTagType.List;
            return true;
        }

        if (type == typeof(NbtValue) || type == typeof(object))
        {
            tagType = default;
            return false;
        }

        if (type.IsArray)
        {
            tagType = NbtTagType.List;
            return true;
        }

        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            var arguments = type.GetGenericArguments();
            if (definition == typeof(Nullable<>))
            {
                return TryGetKnownElementType(arguments[0], out tagType);
            }

            if (definition == typeof(List<>) ||
                definition == typeof(IReadOnlyList<>) ||
                definition == typeof(IReadOnlyCollection<>))
            {
                tagType = NbtTagType.List;
                return true;
            }

            if ((definition == typeof(Dictionary<,>) ||
                 definition == typeof(IDictionary<,>) ||
                 definition == typeof(IReadOnlyDictionary<,>)) &&
                arguments[0] == typeof(string))
            {
                tagType = NbtTagType.Compound;
                return true;
            }
        }

        tagType = default;
        return false;
    }
}

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace NbtSharp.Converters;

[UnconditionalSuppressMessage("Aot", "IL2026", Justification = "Reached only through annotated serializer entry points.")]
[UnconditionalSuppressMessage("Aot", "IL3050", Justification = "Reached only through annotated serializer entry points.")]
internal static class NbtCollectionReader
{
    // Avoid committing memory for a declared length that the payload may not actually contain;
    // larger lists grow as elements are read.
    private const int PreallocationLimit = 4096;

    /// <summary>
    /// Reads a list whose start token is current and returns its elements. The reader must be
    /// positioned on a <see cref="NbtTokenType.StartList"/> token.
    /// </summary>
    public static List<TElement> ReadList<TElement>(ref NbtReader reader, NbtSerializerOptions options)
    {
        var count = ValidateListStart<TElement>(ref reader, options);
        if (count == 0)
        {
            ReadListEnd(ref reader);
            return [];
        }

        if (count <= PreallocationLimit)
        {
            var list = new List<TElement>(count);
            CollectionsMarshal.SetCount(list, count);
            ReadElements(ref reader, options, CollectionsMarshal.AsSpan(list));
            ReadListEnd(ref reader);
            return list;
        }

        var growing = new List<TElement>();
        ReadElements(ref reader, options, growing, count);
        ReadListEnd(ref reader);
        return growing;
    }

    /// <summary>
    /// Reads a list whose start token is current into an array. In-memory input is allocated once at
    /// the declared length; stream-backed input grows from a bounded capacity. The reader must be
    /// positioned on a <see cref="NbtTokenType.StartList"/> token.
    /// </summary>
    public static TElement[] ReadArray<TElement>(ref NbtReader reader, NbtSerializerOptions options)
    {
        var count = ValidateListStart<TElement>(ref reader, options);
        if (count == 0)
        {
            ReadListEnd(ref reader);
            return [];
        }

        // Only fixed-width element tags have an exact payload-size check. Variable-size elements
        // (compound, string, list, arrays) are bounded by a minimum size, so grow from a bounded
        // capacity to keep malformed or still-partial input from forcing a large allocation.
        var allocateDeclaredCount = !reader.IsStreamBacked &&
            IsFixedWidthListElement(reader.ListElementType);
        var initialCapacity = allocateDeclaredCount
            ? count
            : Math.Min(count, PreallocationLimit);
        var elements = new TElement[initialCapacity];
        var converter = options.GetConverter(typeof(TElement));
        var typedConverter = converter as NbtConverter<TElement>;
        var index = 0;
        while (index < count)
        {
            if (index == elements.Length)
            {
                var newCapacity = (int)Math.Min(
                    count,
                    Math.Max((long)elements.Length * 2, elements.Length + 1));
                Array.Resize(ref elements, newCapacity);
            }

            elements[index] = ReadElement(
                ref reader,
                options,
                converter,
                typedConverter,
                index);
            index++;
        }

        ReadListEnd(ref reader);
        return elements;
    }

    private static bool IsFixedWidthListElement(NbtTagType elementType) =>
        elementType is
            NbtTagType.Byte or
            NbtTagType.Short or
            NbtTagType.Int or
            NbtTagType.Long or
            NbtTagType.Float or
            NbtTagType.Double;

    private static int ValidateListStart<TElement>(ref NbtReader reader, NbtSerializerOptions options)
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

        return count;
    }

    private static void ReadElements<TElement>(
        ref NbtReader reader,
        NbtSerializerOptions options,
        Span<TElement> destination)
    {
        var converter = options.GetConverter(typeof(TElement));
        var typedConverter = converter as NbtConverter<TElement>;
        for (var i = 0; i < destination.Length; i++)
        {
            destination[i] = ReadElement(
                ref reader,
                options,
                converter,
                typedConverter,
                i);
        }
    }

    private static void ReadElements<TElement>(
        ref NbtReader reader,
        NbtSerializerOptions options,
        List<TElement> destination,
        int count)
    {
        var converter = options.GetConverter(typeof(TElement));
        var typedConverter = converter as NbtConverter<TElement>;
        for (var i = 0; i < count; i++)
        {
            destination.Add(ReadElement(
                ref reader,
                options,
                converter,
                typedConverter,
                i));
        }
    }

    private static TElement ReadElement<TElement>(
        ref NbtReader reader,
        NbtSerializerOptions options,
        NbtConverter converter,
        NbtConverter<TElement>? typedConverter,
        int index)
    {
        if (!reader.Read())
        {
            throw new NbtException("The list ended before all declared elements were read.")
            {
                IsTruncated = true,
                BytePosition = reader.BytesConsumed,
            };
        }

        try
        {
            return typedConverter is not null
                ? NbtSerializer.DeserializeValueCore(
                    ref reader,
                    typedConverter,
                    options,
                    pathSegment: null)
                : (TElement)NbtSerializer.DeserializeValueCore(
                    ref reader,
                    typeof(TElement),
                    converter,
                    options,
                    pathSegment: null)!;
        }
        catch (NbtException ex)
        {
            NbtSerializer.PrependPath(ex, $"[{index}]");
            throw;
        }
    }

    private static void ReadListEnd(ref NbtReader reader)
    {
        if (!reader.Read() || reader.TokenType != NbtTokenType.EndList)
        {
            throw new NbtException("The list was not terminated.")
            {
                IsTruncated = true,
                BytePosition = reader.BytesConsumed,
            };
        }
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
        if (type.IsEnum)
        {
            return TryGetKnownElementType(Enum.GetUnderlyingType(type), out tagType);
        }

        if (type == typeof(sbyte) || type == typeof(byte) || type == typeof(bool))
        {
            tagType = NbtTagType.Byte;
            return true;
        }

        if (type == typeof(short) || type == typeof(ushort))
        {
            tagType = NbtTagType.Short;
            return true;
        }

        if (type == typeof(int) || type == typeof(uint))
        {
            tagType = NbtTagType.Int;
            return true;
        }

        if (type == typeof(long) || type == typeof(ulong))
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

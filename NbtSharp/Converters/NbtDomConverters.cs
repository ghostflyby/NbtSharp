using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace NbtSharp.Converters;

[UnconditionalSuppressMessage("Aot", "IL2026", Justification = "Reached only through annotated serializer entry points.")]
[UnconditionalSuppressMessage("Aot", "IL3050", Justification = "Reached only through annotated serializer entry points.")]
internal static class NbtDomConverters
{
    public static readonly NbtConverter<NbtValue> Value = new NbtValueConverter();

    public static readonly NbtConverter<NbtList> List = new NbtListConverter();

    public static readonly NbtConverter<NbtCompound> Compound = new NbtCompoundConverter();

    private sealed class NbtValueConverter : NbtConverter<NbtValue>
    {
        public override NbtValue Read(ref NbtReader reader, NbtSerializerOptions options)
        {
            switch (reader.TagType)
            {
                case NbtTagType.Byte:
                    return reader.GetSByte();
                case NbtTagType.Short:
                    return reader.GetInt16();
                case NbtTagType.Int:
                    return reader.GetInt32();
                case NbtTagType.Long:
                    return reader.GetInt64();
                case NbtTagType.Float:
                    return reader.GetSingle();
                case NbtTagType.Double:
                    return reader.GetDouble();
                case NbtTagType.ByteArray:
                    return MemoryMarshal.Cast<byte, sbyte>(reader.GetByteArray().AsSpan()).ToArray();
                case NbtTagType.String:
                    return reader.GetString();
                case NbtTagType.List:
                    return NbtSerializer.DeserializeValue<NbtList>(ref reader, options);
                case NbtTagType.Compound:
                    return NbtSerializer.DeserializeValue<NbtCompound>(ref reader, options);
                case NbtTagType.IntArray:
                    return reader.GetInt32Array();
                case NbtTagType.LongArray:
                    return reader.GetInt64Array();
                default:
                    throw new NbtException($"Unexpected NBT tag type {reader.TagType}.")
                    {
                        BytePosition = reader.BytesConsumed,
                        ActualTagType = reader.TagType,
                    };
            }
        }

        public override void Write(ref NbtWriter writer, NbtValue value, NbtSerializerOptions options)
        {
            switch (value)
            {
                case sbyte item:
                    writer.WriteSByte(item);
                    break;
                case short item:
                    writer.WriteInt16(item);
                    break;
                case int item:
                    writer.WriteInt32(item);
                    break;
                case long item:
                    writer.WriteInt64(item);
                    break;
                case float item:
                    writer.WriteSingle(item);
                    break;
                case double item:
                    writer.WriteDouble(item);
                    break;
                case sbyte[] item:
                    writer.WriteByteArray(MemoryMarshal.Cast<sbyte, byte>(item.AsSpan()));
                    break;
                case string item:
                    writer.WriteString(item.AsSpan());
                    break;
                case NbtList item:
                    NbtSerializer.SerializeValue(ref writer, item, options);
                    break;
                case NbtCompound item:
                    NbtSerializer.SerializeValue(ref writer, item, options);
                    break;
                case int[] item:
                    writer.WriteInt32Array(item);
                    break;
                case long[] item:
                    writer.WriteInt64Array(item);
                    break;
                default:
                    throw new NbtException("The NBT value is uninitialized or has an unsupported case.");
            }
        }

        public override bool TryGetTagType(NbtValue value, NbtSerializerOptions options, out NbtTagType tagType)
        {
            tagType = value.TagType;
            return true;
        }
    }

    private sealed class NbtCompoundConverter : NbtConverter<NbtCompound>
    {
        public override NbtCompound Read(ref NbtReader reader, NbtSerializerOptions options)
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

            var compound = new NbtCompound();
            while (reader.Read())
            {
                if (reader.TokenType == NbtTokenType.EndCompound)
                {
                    return compound;
                }

                var name = reader.Name;
                if (name is null)
                {
                    throw new NbtException("A compound member has no name.")
                    {
                        BytePosition = reader.BytesConsumed,
                    };
                }

                if (options.RejectDuplicateCompoundNames && compound.ContainsKey(name))
                {
                    throw new NbtException($"The compound contains the duplicate member '{name}'.")
                    {
                        BytePosition = reader.BytesConsumed,
                        Path = "." + name,
                    };
                }

                compound[name] = (NbtValue)NbtSerializer.DeserializeValueInternal(
                    ref reader,
                    typeof(NbtValue),
                    options,
                    "." + name)!;
            }

            throw new NbtException("The compound was not terminated.")
            {
                BytePosition = reader.BytesConsumed,
            };
        }

        public override void Write(ref NbtWriter writer, NbtCompound value, NbtSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(value);

            writer.WriteStartCompound();
            foreach (var pair in value)
            {
                NbtSerializer.SerializeMemberInternal(
                    ref writer,
                    pair.Key,
                    pair.Value,
                    typeof(NbtValue),
                    options);
            }

            writer.WriteEndCompound();
        }

        public override bool TryGetTagType(NbtCompound value, NbtSerializerOptions options, out NbtTagType tagType)
        {
            tagType = NbtTagType.Compound;
            return true;
        }
    }

    private sealed class NbtListConverter : NbtConverter<NbtList>
    {
        public override NbtList Read(ref NbtReader reader, NbtSerializerOptions options)
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

            return reader.ListElementType switch
            {
                NbtTagType.End => ReadEndList(ref reader),
                NbtTagType.Byte => new NbtList(NbtCollectionReader.ReadList<sbyte>(ref reader, options)),
                NbtTagType.Short => new NbtList(NbtCollectionReader.ReadList<short>(ref reader, options)),
                NbtTagType.Int => new NbtList(NbtCollectionReader.ReadList<int>(ref reader, options)),
                NbtTagType.Long => new NbtList(NbtCollectionReader.ReadList<long>(ref reader, options)),
                NbtTagType.Float => new NbtList(NbtCollectionReader.ReadList<float>(ref reader, options)),
                NbtTagType.Double => new NbtList(NbtCollectionReader.ReadList<double>(ref reader, options)),
                NbtTagType.ByteArray => new NbtList(NbtCollectionReader.ReadList<sbyte[]>(ref reader, options)),
                NbtTagType.String => new NbtList(NbtCollectionReader.ReadList<string>(ref reader, options)),
                NbtTagType.List => new NbtList(NbtCollectionReader.ReadList<NbtList>(ref reader, options)),
                NbtTagType.Compound => new NbtList(NbtCollectionReader.ReadList<NbtCompound>(ref reader, options)),
                NbtTagType.IntArray => new NbtList(NbtCollectionReader.ReadList<int[]>(ref reader, options)),
                NbtTagType.LongArray => new NbtList(NbtCollectionReader.ReadList<long[]>(ref reader, options)),
                _ => throw new NbtException($"Unknown NBT list element type {reader.ListElementType}.")
                {
                    BytePosition = reader.BytesConsumed,
                },
            };
        }

        public override void Write(ref NbtWriter writer, NbtList value, NbtSerializerOptions options)
        {
            var elementType = value.ElementType;
            writer.WriteStartList(elementType, value.Count);
            switch (elementType)
            {
                case NbtTagType.End:
                    break;
                case NbtTagType.Byte:
                    WriteElements(ref writer, (List<sbyte>)value.Value, options);
                    break;
                case NbtTagType.Short:
                    WriteElements(ref writer, (List<short>)value.Value, options);
                    break;
                case NbtTagType.Int:
                    WriteElements(ref writer, (List<int>)value.Value, options);
                    break;
                case NbtTagType.Long:
                    WriteElements(ref writer, (List<long>)value.Value, options);
                    break;
                case NbtTagType.Float:
                    WriteElements(ref writer, (List<float>)value.Value, options);
                    break;
                case NbtTagType.Double:
                    WriteElements(ref writer, (List<double>)value.Value, options);
                    break;
                case NbtTagType.ByteArray:
                    WriteElements(ref writer, (List<sbyte[]>)value.Value, options);
                    break;
                case NbtTagType.String:
                    WriteElements(ref writer, (List<string>)value.Value, options);
                    break;
                case NbtTagType.List:
                    WriteElements(ref writer, (List<NbtList>)value.Value, options);
                    break;
                case NbtTagType.Compound:
                    WriteElements(ref writer, (List<NbtCompound>)value.Value, options);
                    break;
                case NbtTagType.IntArray:
                    WriteElements(ref writer, (List<int[]>)value.Value, options);
                    break;
                case NbtTagType.LongArray:
                    WriteElements(ref writer, (List<long[]>)value.Value, options);
                    break;
                default:
                    throw new NbtException($"Unknown NBT list element type {elementType}.");
            }

            writer.WriteEndList();
        }

        public override bool TryGetTagType(NbtList value, NbtSerializerOptions options, out NbtTagType tagType)
        {
            tagType = NbtTagType.List;
            return true;
        }

        private static NbtList ReadEndList(ref NbtReader reader)
        {
            if (!reader.Read() || reader.TokenType != NbtTokenType.EndList)
            {
                throw new NbtException("The TAG_List<TAG_End> was not terminated.")
                {
                    BytePosition = reader.BytesConsumed,
                };
            }

            return default;
        }

        private static void WriteElements<TElement>(
            ref NbtWriter writer,
            List<TElement> elements,
            NbtSerializerOptions options)
        {
            for (var i = 0; i < elements.Count; i++)
            {
                NbtSerializer.SerializeValueInternal(
                    ref writer,
                    elements[i],
                    typeof(TElement),
                    options,
                    $"[{i}]",
                    headerWritten: false);
            }
        }
    }
}

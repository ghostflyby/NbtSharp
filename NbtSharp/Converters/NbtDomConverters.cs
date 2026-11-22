using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace NbtSharp.Converters;

[UnconditionalSuppressMessage("Aot", "IL2026", Justification = "Reached only through annotated serializer entry points.")]
[UnconditionalSuppressMessage("Aot", "IL3050", Justification = "Reached only through annotated serializer entry points.")]
internal static class NbtDomConverters
{
    // Mirrors NbtCollectionReader's limit: avoid committing memory for a declared length that the
    // payload may not actually contain.
    private const int PreallocationLimit = 4096;

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
                    return reader.GetSByteArray();
                case NbtTagType.String:
                    return reader.GetString();
                case NbtTagType.List:
                    return NbtDomConverters.List.Read(ref reader, options);
                case NbtTagType.Compound:
                    return Compound.Read(ref reader, options);
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
                    NbtDomConverters.List.Write(ref writer, item, options);
                    break;
                case NbtCompound item:
                    Compound.Write(ref writer, item, options);
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

                try
                {
                    compound[name] = Value.Read(ref reader, options);
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

        public override void Write(ref NbtWriter writer, NbtCompound value, NbtSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(value);

            writer.WriteStartCompound();
            foreach (var pair in value)
            {
                NbtMetadataServices.WriteDomMember(ref writer, pair.Key, pair.Value, options);
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
                NbtTagType.Byte => new NbtList(ReadDomElements<sbyte>(ref reader, options)),
                NbtTagType.Short => new NbtList(ReadDomElements<short>(ref reader, options)),
                NbtTagType.Int => new NbtList(ReadDomElements<int>(ref reader, options)),
                NbtTagType.Long => new NbtList(ReadDomElements<long>(ref reader, options)),
                NbtTagType.Float => new NbtList(ReadDomElements<float>(ref reader, options)),
                NbtTagType.Double => new NbtList(ReadDomElements<double>(ref reader, options)),
                NbtTagType.ByteArray => new NbtList(ReadDomElements<sbyte[]>(ref reader, options)),
                NbtTagType.String => new NbtList(ReadDomElements<string>(ref reader, options)),
                NbtTagType.List => new NbtList(ReadDomListElements(ref reader, options)),
                NbtTagType.Compound => new NbtList(ReadDomCompoundElements(ref reader, options)),
                NbtTagType.IntArray => new NbtList(ReadDomElements<int[]>(ref reader, options)),
                NbtTagType.LongArray => new NbtList(ReadDomElements<long[]>(ref reader, options)),
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
                    IsTruncated = true,
                    BytePosition = reader.BytesConsumed,
                };
            }

            return default;
        }

        // Nested list and compound elements recurse through the built-in DOM converters so that
        // converters registered for NbtList/NbtCompound never intercept nested DOM values. Scalar
        // and array element cases go through the same closed family (DomElementConverter), keeping
        // reads symmetric with the write path. Count validation and EndList handling mirror
        // NbtCollectionReader's element loops.
        private static List<TElement> ReadDomElements<TElement>(ref NbtReader reader, NbtSerializerOptions options)
        {
            var count = reader.ListLength;
            var converter = DomElementConverter<TElement>.Converter;
            var elements = new List<TElement>(Math.Min(count, 4096));
            for (var i = 0; i < count; i++)
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
                    elements.Add(converter.Read(ref reader, options));
                }
                catch (NbtException ex)
                {
                    NbtSerializer.PrependPath(ex, $"[{i}]");
                    throw;
                }
            }

            ReadDomListEnd(ref reader);
            return elements;
        }

        private static List<NbtList> ReadDomListElements(ref NbtReader reader, NbtSerializerOptions options)
        {
            var count = reader.ListLength;
            var elements = new List<NbtList>(Math.Min(count, PreallocationLimit));
            for (var i = 0; i < count; i++)
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
                    elements.Add(NbtDomConverters.List.Read(ref reader, options));
                }
                catch (NbtException ex)
                {
                    NbtSerializer.PrependPath(ex, $"[{i}]");
                    throw;
                }
            }

            ReadDomListEnd(ref reader);
            return elements;
        }

        private static List<NbtCompound> ReadDomCompoundElements(ref NbtReader reader, NbtSerializerOptions options)
        {
            var count = reader.ListLength;
            var elements = new List<NbtCompound>(Math.Min(count, PreallocationLimit));
            for (var i = 0; i < count; i++)
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
                    elements.Add(Compound.Read(ref reader, options));
                }
                catch (NbtException ex)
                {
                    NbtSerializer.PrependPath(ex, $"[{i}]");
                    throw;
                }
            }

            ReadDomListEnd(ref reader);
            return elements;
        }

        private static void ReadDomListEnd(ref NbtReader reader)
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

        private static void WriteElements<TElement>(
            ref NbtWriter writer,
            List<TElement> elements,
            NbtSerializerOptions options)
        {
            var converter = DomElementConverter<TElement>.Converter;
            for (var i = 0; i < elements.Count; i++)
            {
                var element = elements[i];
                try
                {
                    if (element is null)
                    {
                        throw new NbtException("A list element cannot be null.")
                        {
                            BytePosition = writer.BytesWritten,
                        };
                    }

                    converter.Write(ref writer, element, options);
                }
                catch (NbtException ex)
                {
                    NbtSerializer.PrependPath(ex, $"[{i}]");
                    throw;
                }
            }
        }
    }

    // Resolves the built-in converter for one legal DOM list element type once per instantiation,
    // so element writes never consult NbtSerializerOptions and registered converters cannot
    // intercept nested DOM values.
    private static class DomElementConverter<TElement>
    {
        public static readonly NbtConverter<TElement> Converter = (NbtConverter<TElement>)Create();

        private static object Create()
        {
            var type = typeof(TElement);
            if (type == typeof(sbyte))
            {
                return NbtPrimitiveConverters.SByte;
            }

            if (type == typeof(short))
            {
                return NbtPrimitiveConverters.Int16;
            }

            if (type == typeof(int))
            {
                return NbtPrimitiveConverters.Int32;
            }

            if (type == typeof(long))
            {
                return NbtPrimitiveConverters.Int64;
            }

            if (type == typeof(float))
            {
                return NbtPrimitiveConverters.Single;
            }

            if (type == typeof(double))
            {
                return NbtPrimitiveConverters.Double;
            }

            if (type == typeof(string))
            {
                return NbtPrimitiveConverters.String;
            }

            if (type == typeof(sbyte[]))
            {
                return NbtArrayConverters.SByteArray;
            }

            if (type == typeof(int[]))
            {
                return NbtArrayConverters.Int32Array;
            }

            if (type == typeof(long[]))
            {
                return NbtArrayConverters.Int64Array;
            }

            if (type == typeof(NbtList))
            {
                return List;
            }

            if (type == typeof(NbtCompound))
            {
                return Compound;
            }

            throw new InvalidOperationException($"Unknown NBT list element type {type}.");
        }
    }
}

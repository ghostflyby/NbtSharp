namespace NbtSharp;

/// <summary>
/// Converts between NBT documents and their Java Edition binary encoding.
/// </summary>
/// <remarks>
/// <para>
/// Both operations are built on <see cref="NbtReader"/> and <see cref="NbtWriter"/>, so the format
/// rules live in one place. The serializer reads and writes the stream from its current position and
/// never takes ownership of it.
/// </para>
/// <para>
/// A compound with no members and a list are both encoded with the type information the format
/// requires; an empty list keeps its element type, and the <see cref="NbtEmptyList"/> case encodes as
/// <c>TAG_List&lt;TAG_End&gt;</c> with length zero.
/// </para>
/// </remarks>
public static class NbtSerializer
{
    /// <summary>Reads a complete NBT document from <paramref name="stream"/>.</summary>
    /// <param name="stream">The stream that supplies the NBT data.</param>
    /// <param name="options">The read options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The parsed document.</returns>
    /// <exception cref="NbtException">The data is malformed.</exception>
    public static NbtRoot Deserialize(Stream stream, NbtSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        options ??= new NbtSerializerOptions();

        using var reader = new NbtReader(stream, options.RootFormat);
        reader.Read();
        var name = reader.Name;
        var value = ReadValue(reader);
        return new NbtRoot(name, value);
    }

    /// <summary>Writes a complete NBT document to <paramref name="stream"/>.</summary>
    /// <param name="stream">The stream that receives the NBT data.</param>
    /// <param name="root">The document to write.</param>
    /// <param name="options">The write options, or <see langword="null"/> for the defaults.</param>
    /// <exception cref="NbtException">The document contains an uninitialized value.</exception>
    public static void Serialize(Stream stream, NbtRoot root, NbtSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        options ??= new NbtSerializerOptions();

        if (options.RootFormat == NbtRootFormat.Named && root.Name is null)
        {
            throw new NbtException("A named root requires a name. Use an empty name or NbtRootFormat.Unnamed.");
        }

        if (options.RootFormat == NbtRootFormat.Unnamed && root.Name is not null)
        {
            throw new NbtException("An unnamed root cannot carry a name. Use NbtRootFormat.Named.");
        }

        var writer = new NbtWriter(stream, options.RootFormat);
        try
        {
            WriteValue(writer, root.Name ?? string.Empty, root.Value);
        }
        catch
        {
            writer.Abort();
            throw;
        }

        writer.Dispose();
    }

    private static NbtValue ReadValue(NbtReader reader)
    {
        if (reader.TokenType == NbtTokenType.StartCompound)
        {
            var compound = new NbtCompound();
            while (reader.Read())
            {
                if (reader.TokenType == NbtTokenType.EndCompound)
                {
                    return compound;
                }

                compound[reader.Name ?? string.Empty] = ReadValue(reader);
            }

            throw new NbtException("The compound was not terminated.");
        }

        if (reader.TokenType == NbtTokenType.StartList)
        {
            return ReadListElements(reader, reader.ListElementType, reader.ListCount);
        }

        return reader.TagType switch
        {
            NbtTagType.Byte => reader.GetSByte(),
            NbtTagType.Short => reader.GetInt16(),
            NbtTagType.Int => reader.GetInt32(),
            NbtTagType.Long => reader.GetInt64(),
            NbtTagType.Float => reader.GetSingle(),
            NbtTagType.Double => reader.GetDouble(),
            NbtTagType.ByteArray => reader.GetByteArray(),
            NbtTagType.String => reader.GetString(),
            NbtTagType.IntArray => reader.GetIntArray(),
            NbtTagType.LongArray => reader.GetLongArray(),
            _ => throw new NbtException($"Unexpected NBT tag type {reader.TagType}."),
        };
    }

    private static NbtList ReadListElements(NbtReader reader, NbtTagType elementType, int count)
    {
        switch (elementType)
        {
            case NbtTagType.End:
                reader.Read();
                return new NbtList(new NbtEmptyList());
            case NbtTagType.Byte:
            {
                var list = new List<sbyte>();
                for (var i = 0; i < count; i++)
                {
                    reader.Read();
                    list.Add(reader.GetSByte());
                }

                reader.Read();
                return list;
            }
            case NbtTagType.Short:
            {
                var list = new List<short>();
                for (var i = 0; i < count; i++)
                {
                    reader.Read();
                    list.Add(reader.GetInt16());
                }

                reader.Read();
                return list;
            }
            case NbtTagType.Int:
            {
                var list = new List<int>();
                for (var i = 0; i < count; i++)
                {
                    reader.Read();
                    list.Add(reader.GetInt32());
                }

                reader.Read();
                return list;
            }
            case NbtTagType.Long:
            {
                var list = new List<long>();
                for (var i = 0; i < count; i++)
                {
                    reader.Read();
                    list.Add(reader.GetInt64());
                }

                reader.Read();
                return list;
            }
            case NbtTagType.Float:
            {
                var list = new List<float>();
                for (var i = 0; i < count; i++)
                {
                    reader.Read();
                    list.Add(reader.GetSingle());
                }

                reader.Read();
                return list;
            }
            case NbtTagType.Double:
            {
                var list = new List<double>();
                for (var i = 0; i < count; i++)
                {
                    reader.Read();
                    list.Add(reader.GetDouble());
                }

                reader.Read();
                return list;
            }
            case NbtTagType.ByteArray:
            {
                var list = new List<sbyte[]>();
                for (var i = 0; i < count; i++)
                {
                    reader.Read();
                    list.Add(reader.GetByteArray());
                }

                reader.Read();
                return list;
            }
            case NbtTagType.String:
            {
                var list = new List<string>();
                for (var i = 0; i < count; i++)
                {
                    reader.Read();
                    list.Add(reader.GetString());
                }

                reader.Read();
                return list;
            }
            case NbtTagType.List:
            {
                var list = new List<NbtList>();
                for (var i = 0; i < count; i++)
                {
                    reader.Read();
                    list.Add(ReadListElements(reader, reader.ListElementType, reader.ListCount));
                }

                reader.Read();
                return list;
            }
            case NbtTagType.Compound:
            {
                var list = new List<NbtCompound>();
                for (var i = 0; i < count; i++)
                {
                    reader.Read();
                    if (ReadValue(reader) is not NbtCompound element)
                    {
                        throw new NbtException("A compound list element was not a compound.");
                    }

                    list.Add(element);
                }

                reader.Read();
                return new NbtList(list);
            }
            case NbtTagType.IntArray:
            {
                var list = new List<int[]>();
                for (var i = 0; i < count; i++)
                {
                    reader.Read();
                    list.Add(reader.GetIntArray());
                }

                reader.Read();
                return list;
            }
            case NbtTagType.LongArray:
            {
                var list = new List<long[]>();
                for (var i = 0; i < count; i++)
                {
                    reader.Read();
                    list.Add(reader.GetLongArray());
                }

                reader.Read();
                return list;
            }
            default:
                throw new NbtException($"Unknown NBT list element type {elementType}.");
        }
    }

    private static void WriteValue(NbtWriter writer, string name, NbtValue value)
    {
        switch (value)
        {
            case sbyte item:
                writer.WriteByte(name, item);
                break;
            case short item:
                writer.WriteInt16(name, item);
                break;
            case int item:
                writer.WriteInt32(name, item);
                break;
            case long item:
                writer.WriteInt64(name, item);
                break;
            case float item:
                writer.WriteSingle(name, item);
                break;
            case double item:
                writer.WriteDouble(name, item);
                break;
            case sbyte[] item:
                writer.WriteByteArray(name, item);
                break;
            case string item:
                writer.WriteString(name, item);
                break;
            case NbtCompound item:
                writer.WriteStartCompound(name);
                foreach ((var memberName, var memberValue) in item)
                {
                    WriteValue(writer, memberName, memberValue);
                }

                writer.WriteEndCompound();
                break;
            case int[] item:
                writer.WriteIntArray(name, item);
                break;
            case long[] item:
                writer.WriteLongArray(name, item);
                break;
            case NbtList item:
                WriteList(writer, name, item);
                break;
            default:
                throw new NbtException("The NBT value is uninitialized or has an unsupported case.");
        }
    }

    private static void WriteList(NbtWriter writer, string name, NbtList list)
    {
        var elementType = list.ElementType;
        switch (elementType)
        {
            case NbtTagType.End:
                writer.WriteStartList(name, NbtTagType.End, 0);
                writer.WriteEndList();
                break;
            case NbtTagType.Byte:
                WriteListElements(writer, name, elementType, (List<sbyte>)list.Value!);
                break;
            case NbtTagType.Short:
                WriteListElements(writer, name, elementType, (List<short>)list.Value!);
                break;
            case NbtTagType.Int:
                WriteListElements(writer, name, elementType, (List<int>)list.Value!);
                break;
            case NbtTagType.Long:
                WriteListElements(writer, name, elementType, (List<long>)list.Value!);
                break;
            case NbtTagType.Float:
                WriteListElements(writer, name, elementType, (List<float>)list.Value!);
                break;
            case NbtTagType.Double:
                WriteListElements(writer, name, elementType, (List<double>)list.Value!);
                break;
            case NbtTagType.ByteArray:
                WriteListElements(writer, name, elementType, (List<sbyte[]>)list.Value!);
                break;
            case NbtTagType.String:
                WriteListElements(writer, name, elementType, (List<string>)list.Value!);
                break;
            case NbtTagType.IntArray:
                WriteListElements(writer, name, elementType, (List<int[]>)list.Value!);
                break;
            case NbtTagType.LongArray:
                WriteListElements(writer, name, elementType, (List<long[]>)list.Value!);
                break;
            case NbtTagType.List:
            {
                var elements = (List<NbtList>)list.Value!;
                writer.WriteStartList(name, elementType, elements.Count);
                foreach (var element in elements)
                {
                    WriteListElement(writer, element);
                }

                writer.WriteEndList();
                break;
            }
            case NbtTagType.Compound:
            {
                var elements = (List<NbtCompound>)list.Value!;
                writer.WriteStartList(name, elementType, elements.Count);
                foreach (var element in elements)
                {
                    writer.WriteStartCompoundElement();
                    foreach ((var memberName, var memberValue) in element)
                    {
                        WriteValue(writer, memberName, memberValue);
                    }

                    writer.WriteEndCompound();
                }

                writer.WriteEndList();
                break;
            }
            default:
                throw new NbtException($"Unknown NBT list element type {elementType}.");
        }
    }

    private static void WriteListElements<T>(NbtWriter writer, string name, NbtTagType elementType, List<T> elements)
    {
        writer.WriteStartList(name, elementType, elements.Count);
        foreach (var element in elements)
        {
            WriteElement(writer, element);
        }

        writer.WriteEndList();
    }

    private static void WriteElement<T>(NbtWriter writer, T element)
    {
        switch (element)
        {
            case sbyte item:
                writer.WriteElement(item);
                break;
            case short item:
                writer.WriteElement(item);
                break;
            case int item:
                writer.WriteElement(item);
                break;
            case long item:
                writer.WriteElement(item);
                break;
            case float item:
                writer.WriteElement(item);
                break;
            case double item:
                writer.WriteElement(item);
                break;
            case sbyte[] item:
                writer.WriteElement(item);
                break;
            case string item:
                writer.WriteElement(item);
                break;
            case int[] item:
                writer.WriteElement(item);
                break;
            case long[] item:
                writer.WriteElement(item);
                break;
            default:
                throw new NbtException($"Unsupported NBT list element type {typeof(T)}.");
        }
    }

    private static void WriteListElement(NbtWriter writer, NbtList element)
    {
        var elementType = element.ElementType;
        switch (elementType)
        {
            case NbtTagType.End:
                writer.WriteStartListElement(NbtTagType.End, 0);
                writer.WriteEndList();
                break;
            case NbtTagType.Byte:
                WriteElementList(writer, elementType, (List<sbyte>)element.Value!);
                break;
            case NbtTagType.Short:
                WriteElementList(writer, elementType, (List<short>)element.Value!);
                break;
            case NbtTagType.Int:
                WriteElementList(writer, elementType, (List<int>)element.Value!);
                break;
            case NbtTagType.Long:
                WriteElementList(writer, elementType, (List<long>)element.Value!);
                break;
            case NbtTagType.Float:
                WriteElementList(writer, elementType, (List<float>)element.Value!);
                break;
            case NbtTagType.Double:
                WriteElementList(writer, elementType, (List<double>)element.Value!);
                break;
            case NbtTagType.ByteArray:
                WriteElementList(writer, elementType, (List<sbyte[]>)element.Value!);
                break;
            case NbtTagType.String:
                WriteElementList(writer, elementType, (List<string>)element.Value!);
                break;
            case NbtTagType.IntArray:
                WriteElementList(writer, elementType, (List<int[]>)element.Value!);
                break;
            case NbtTagType.LongArray:
                WriteElementList(writer, elementType, (List<long[]>)element.Value!);
                break;
            case NbtTagType.List:
            {
                var elements = (List<NbtList>)element.Value!;
                writer.WriteStartListElement(elementType, elements.Count);
                foreach (var item in elements)
                {
                    WriteListElement(writer, item);
                }

                writer.WriteEndList();
                break;
            }
            case NbtTagType.Compound:
            {
                var elements = (List<NbtCompound>)element.Value!;
                writer.WriteStartListElement(elementType, elements.Count);
                foreach (var item in elements)
                {
                    writer.WriteStartCompoundElement();
                    foreach ((var memberName, var memberValue) in item)
                    {
                        WriteValue(writer, memberName, memberValue);
                    }

                    writer.WriteEndCompound();
                }

                writer.WriteEndList();
                break;
            }
            default:
                throw new NbtException($"Unknown NBT list element type {elementType}.");
        }
    }

    private static void WriteElementList<T>(NbtWriter writer, NbtTagType elementType, List<T> elements)
    {
        writer.WriteStartListElement(elementType, elements.Count);
        foreach (var element in elements)
        {
            WriteElement(writer, element);
        }

        writer.WriteEndList();
    }
}

namespace NbtSharp.Converters;

internal static class NbtPrimitiveConverters
{
    public static readonly NbtConverter<sbyte> SByte = new NbtDelegateConverter<sbyte>(
        NbtTagType.Byte,
        static (ref reader) => reader.GetSByte(),
        static (ref writer, value) => writer.WriteSByte(value));

    public static readonly NbtConverter<byte> Byte = new NbtDelegateConverter<byte>(
        NbtTagType.Byte,
        static (ref reader) => unchecked((byte)reader.GetSByte()),
        static (ref writer, value) => writer.WriteSByte(unchecked((sbyte)value)));

    public static readonly NbtConverter<short> Int16 = new NbtDelegateConverter<short>(
        NbtTagType.Short,
        static (ref reader) => reader.GetInt16(),
        static (ref writer, value) => writer.WriteInt16(value));

    public static readonly NbtConverter<int> Int32 = new NbtDelegateConverter<int>(
        NbtTagType.Int,
        static (ref reader) => reader.GetInt32(),
        static (ref writer, value) => writer.WriteInt32(value));

    public static readonly NbtConverter<long> Int64 = new NbtDelegateConverter<long>(
        NbtTagType.Long,
        static (ref reader) => reader.GetInt64(),
        static (ref writer, value) => writer.WriteInt64(value));

    public static readonly NbtConverter<ushort> UInt16 = new NbtDelegateConverter<ushort>(
        NbtTagType.Short,
        static (ref reader) => unchecked((ushort)reader.GetInt16()),
        static (ref writer, value) => writer.WriteInt16(unchecked((short)value)));

    public static readonly NbtConverter<uint> UInt32 = new NbtDelegateConverter<uint>(
        NbtTagType.Int,
        static (ref reader) => unchecked((uint)reader.GetInt32()),
        static (ref writer, value) => writer.WriteInt32(unchecked((int)value)));

    public static readonly NbtConverter<ulong> UInt64 = new NbtDelegateConverter<ulong>(
        NbtTagType.Long,
        static (ref reader) => unchecked((ulong)reader.GetInt64()),
        static (ref writer, value) => writer.WriteInt64(unchecked((long)value)));

    public static readonly NbtConverter<char> Char = new NbtDelegateConverter<char>(
        NbtTagType.Short,
        static (ref reader) => unchecked((char)reader.GetInt16()),
        static (ref writer, value) => writer.WriteInt16(unchecked((short)value)));

    public static readonly NbtConverter<float> Single = new NbtDelegateConverter<float>(
        NbtTagType.Float,
        static (ref reader) => reader.GetSingle(),
        static (ref writer, value) => writer.WriteSingle(value));

    public static readonly NbtConverter<double> Double = new NbtDelegateConverter<double>(
        NbtTagType.Double,
        static (ref reader) => reader.GetDouble(),
        static (ref writer, value) => writer.WriteDouble(value));

    public static readonly NbtConverter<string> String = new NbtDelegateConverter<string>(
        NbtTagType.String,
        static (ref reader) => reader.GetString(),
        static (ref writer, value) => writer.WriteString(value.AsSpan()));

    public static readonly NbtConverter<bool> Boolean = new NbtBooleanConverter();

    private sealed class NbtBooleanConverter : NbtConverter<bool>
    {
        public override bool Read(ref NbtReader reader, NbtSerializerOptions options) =>
            NbtMetadataServices.ReadBoolean(ref reader);

        public override void Write(ref NbtWriter writer, bool value, NbtSerializerOptions options) =>
            writer.WriteSByte(value ? (sbyte)1 : (sbyte)0);

        public override bool TryGetTagType(bool value, NbtSerializerOptions options, out NbtTagType tagType)
        {
            tagType = NbtTagType.Byte;
            return true;
        }
    }
}

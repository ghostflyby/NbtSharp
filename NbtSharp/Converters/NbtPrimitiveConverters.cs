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
        public override bool Read(ref NbtReader reader, NbtSerializerOptions options)
        {
            var value = reader.GetSByte();
            return value switch
            {
                0 => false,
                1 => true,
                _ => throw new NbtException($"A TAG_Byte boolean must be 0 or 1 but was {value}.")
                {
                    BytePosition = reader.BytesConsumed - sizeof(byte),
                    ActualTagType = NbtTagType.Byte,
                },
            };
        }

        public override void Write(ref NbtWriter writer, bool value, NbtSerializerOptions options) =>
            writer.WriteSByte(value ? (sbyte)1 : (sbyte)0);

        public override bool TryGetTagType(bool value, NbtSerializerOptions options, out NbtTagType tagType)
        {
            tagType = NbtTagType.Byte;
            return true;
        }
    }
}

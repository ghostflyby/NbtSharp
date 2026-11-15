using System.Runtime.CompilerServices;

namespace NbtSharp.Converters;

/// <summary>
/// Converts enums using the signed NBT integer tag with the same width as the enum's underlying type.
/// </summary>
internal sealed class NbtEnumConverter<TEnum> : NbtConverter<TEnum>
    where TEnum : struct, Enum
{
    private static readonly EnumWidth Width = GetWidth(typeof(TEnum).GetEnumUnderlyingType());

    public override TEnum Read(ref NbtReader reader, NbtSerializerOptions options)
    {
        switch (Width)
        {
            case EnumWidth.SByte:
            {
                var value = reader.GetSByte();
                return Unsafe.As<sbyte, TEnum>(ref value);
            }
            case EnumWidth.Byte:
            {
                var value = unchecked((byte)reader.GetSByte());
                return Unsafe.As<byte, TEnum>(ref value);
            }
            case EnumWidth.Int16:
            {
                var value = reader.GetInt16();
                return Unsafe.As<short, TEnum>(ref value);
            }
            case EnumWidth.UInt16:
            {
                var value = unchecked((ushort)reader.GetInt16());
                return Unsafe.As<ushort, TEnum>(ref value);
            }
            case EnumWidth.Int32:
            {
                var value = reader.GetInt32();
                return Unsafe.As<int, TEnum>(ref value);
            }
            case EnumWidth.UInt32:
            {
                var value = unchecked((uint)reader.GetInt32());
                return Unsafe.As<uint, TEnum>(ref value);
            }
            case EnumWidth.Int64:
            {
                var value = reader.GetInt64();
                return Unsafe.As<long, TEnum>(ref value);
            }
            case EnumWidth.UInt64:
            {
                var value = unchecked((ulong)reader.GetInt64());
                return Unsafe.As<ulong, TEnum>(ref value);
            }
            default:
                throw new InvalidOperationException($"Unsupported enum width {Width}.");
        }
    }

    public override void Write(ref NbtWriter writer, TEnum value, NbtSerializerOptions options)
    {
        switch (Width)
        {
            case EnumWidth.SByte:
                writer.WriteSByte(Unsafe.As<TEnum, sbyte>(ref value));
                break;
            case EnumWidth.Byte:
                writer.WriteSByte(unchecked((sbyte)Unsafe.As<TEnum, byte>(ref value)));
                break;
            case EnumWidth.Int16:
                writer.WriteInt16(Unsafe.As<TEnum, short>(ref value));
                break;
            case EnumWidth.UInt16:
                writer.WriteInt16(unchecked((short)Unsafe.As<TEnum, ushort>(ref value)));
                break;
            case EnumWidth.Int32:
                writer.WriteInt32(Unsafe.As<TEnum, int>(ref value));
                break;
            case EnumWidth.UInt32:
                writer.WriteInt32(unchecked((int)Unsafe.As<TEnum, uint>(ref value)));
                break;
            case EnumWidth.Int64:
                writer.WriteInt64(Unsafe.As<TEnum, long>(ref value));
                break;
            case EnumWidth.UInt64:
                writer.WriteInt64(unchecked((long)Unsafe.As<TEnum, ulong>(ref value)));
                break;
            default:
                throw new InvalidOperationException($"Unsupported enum width {Width}.");
        }
    }

    public override bool TryGetTagType(
        TEnum value,
        NbtSerializerOptions options,
        out NbtTagType tagType)
    {
        tagType = Width switch
        {
            EnumWidth.SByte or EnumWidth.Byte => NbtTagType.Byte,
            EnumWidth.Int16 or EnumWidth.UInt16 => NbtTagType.Short,
            EnumWidth.Int32 or EnumWidth.UInt32 => NbtTagType.Int,
            EnumWidth.Int64 or EnumWidth.UInt64 => NbtTagType.Long,
            _ => throw new InvalidOperationException($"Unsupported enum width {Width}."),
        };
        return true;
    }

    private static EnumWidth GetWidth(Type underlyingType) => Type.GetTypeCode(underlyingType) switch
    {
        TypeCode.SByte => EnumWidth.SByte,
        TypeCode.Byte => EnumWidth.Byte,
        TypeCode.Int16 => EnumWidth.Int16,
        TypeCode.UInt16 => EnumWidth.UInt16,
        TypeCode.Int32 => EnumWidth.Int32,
        TypeCode.UInt32 => EnumWidth.UInt32,
        TypeCode.Int64 => EnumWidth.Int64,
        TypeCode.UInt64 => EnumWidth.UInt64,
        _ => throw new NotSupportedException(
            $"The enum underlying type '{underlyingType}' is not supported."),
    };

    private enum EnumWidth
    {
        SByte,
        Byte,
        Int16,
        UInt16,
        Int32,
        UInt32,
        Int64,
        UInt64,
    }
}

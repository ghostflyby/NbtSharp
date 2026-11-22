using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;

// The public NbtSharp namespace is intentional; the folder groups internal implementation.
// ReSharper disable once CheckNamespace
namespace NbtSharp;

/// <summary>
/// Converts an enum to and from a TAG_String containing the member name, following the
/// System.Text.Json string-enum semantics.
/// </summary>
/// <typeparam name="TEnum">The enum type handled by the converter.</typeparam>
/// <remarks>
/// <para>
/// Defined values are written as their first declared name; values without a defined name —
/// including undefined flags combinations — are written as the invariant numeric string of the
/// underlying value, so every value round-trips. Name lookup on read is ordinal and
/// case-sensitive.
/// </para>
/// <para>
/// Enums marked <see cref="FlagsAttribute"/> additionally read a comma-separated list: each segment
/// is trimmed, resolved as a name or a numeric string of the underlying width, and combined with a
/// bitwise OR. Numeric segments accept an optional leading sign and no whitespace. Any unresolvable
/// value throws <see cref="NbtException"/> naming the offending string and the enum type.
/// </para>
/// </remarks>
public sealed class NbtStringEnumConverter<TEnum> : NbtConverter<TEnum>
    where TEnum : struct, Enum
{
    private static readonly EnumWidth Width = GetWidth(typeof(TEnum).GetEnumUnderlyingType());
    private static readonly FrozenDictionary<string, TEnum> NameToValue = CreateNameToValue();

    [SuppressMessage(
        "ReSharper",
        "StaticMemberInGenericType",
        Justification = "Per-enum name tables are intentionally static so each closed TEnum builds them once.")]
    private static readonly FrozenDictionary<ulong, string> ValueToName = CreateValueToName();
    private static readonly bool IsFlags = typeof(TEnum).IsDefined(typeof(FlagsAttribute), inherit: false);

    /// <inheritdoc/>
    public override TEnum Read(ref NbtReader reader, NbtSerializerOptions options)
    {
        var text = reader.GetString();
        if (NameToValue.TryGetValue(text, out var value))
        {
            return value;
        }

        if (!IsFlags)
        {
            if (TryParseNumeric(text, out value))
            {
                return value;
            }
        }
        else if (TryReadFlags(text, out value))
        {
            return value;
        }

        throw CreateReadException(reader, text);
    }

    /// <inheritdoc/>
    public override void Write(ref NbtWriter writer, TEnum value, NbtSerializerOptions options) =>
        writer.WriteString(Format(value));

    /// <inheritdoc/>
    public override bool TryGetTagType(TEnum value, NbtSerializerOptions options, out NbtTagType tagType)
    {
        tagType = NbtTagType.String;
        return true;
    }

    private static string Format(TEnum value) =>
        ValueToName.TryGetValue(ToUInt64(value), out var name) ? name : value.ToString();

    private static bool TryReadFlags(string text, out TEnum value)
    {
        var result = 0UL;
        foreach (var rawSegment in text.Split(','))
        {
            var segment = rawSegment.Trim();
            if (segment.Length == 0)
            {
                value = default;
                return false;
            }

            if (NameToValue.TryGetValue(segment, out var segmentValue) ||
                TryParseNumeric(segment, out segmentValue))
            {
                result |= ToUInt64(segmentValue);
                continue;
            }

            value = default;
            return false;
        }

        value = FromUInt64(result);
        return true;
    }

    private static bool TryParseNumeric(string text, out TEnum value)
    {
        value = default;
        ulong raw;
        try
        {
            raw = Width switch
            {
                EnumWidth.SByte or EnumWidth.Int16 or EnumWidth.Int32 or EnumWidth.Int64 =>
                    unchecked((ulong)long.Parse(
                        text,
                        NumberStyles.AllowLeadingSign,
                        CultureInfo.InvariantCulture)),
                _ => ulong.Parse(
                    text,
                    NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture),
            };
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            return false;
        }

        if (!IsInWidth(raw))
        {
            return false;
        }

        value = FromUInt64(raw);
        return true;
    }

    private static NbtException CreateReadException(NbtReader reader, string value) => new(
        $"The string value '{value}' could not be converted to a member of the " +
        $"'{typeof(TEnum)}' enum.")
    {
        BytePosition = reader.BytesConsumed,
    };

    private static bool IsInWidth(ulong value) => Width switch
    {
        EnumWidth.SByte => value <= unchecked((ulong)sbyte.MaxValue) ||
            value >= unchecked((ulong)sbyte.MinValue),
        EnumWidth.Byte => value <= byte.MaxValue,
        EnumWidth.Int16 => value <= unchecked((ulong)short.MaxValue) ||
            value >= unchecked((ulong)short.MinValue),
        EnumWidth.UInt16 => value <= ushort.MaxValue,
        EnumWidth.Int32 => value <= int.MaxValue ||
            value >= unchecked((ulong)int.MinValue),
        EnumWidth.UInt32 => value <= uint.MaxValue,
        _ => true,
    };

    private static ulong ToUInt64(TEnum value) => Width switch
    {
        EnumWidth.SByte => unchecked((ulong)Unsafe.As<TEnum, sbyte>(ref value)),
        EnumWidth.Byte => Unsafe.As<TEnum, byte>(ref value),
        EnumWidth.Int16 => unchecked((ulong)Unsafe.As<TEnum, short>(ref value)),
        EnumWidth.UInt16 => Unsafe.As<TEnum, ushort>(ref value),
        EnumWidth.Int32 => unchecked((ulong)Unsafe.As<TEnum, int>(ref value)),
        EnumWidth.UInt32 => Unsafe.As<TEnum, uint>(ref value),
        EnumWidth.Int64 => unchecked((ulong)Unsafe.As<TEnum, long>(ref value)),
        EnumWidth.UInt64 => Unsafe.As<TEnum, ulong>(ref value),
        _ => throw new InvalidOperationException($"Unsupported enum width {Width}."),
    };

    private static TEnum FromUInt64(ulong value)
    {
        switch (Width)
        {
            case EnumWidth.SByte:
            {
                var converted = unchecked((sbyte)value);
                return Unsafe.As<sbyte, TEnum>(ref converted);
            }
            case EnumWidth.Byte:
            {
                var converted = (byte)value;
                return Unsafe.As<byte, TEnum>(ref converted);
            }
            case EnumWidth.Int16:
            {
                var converted = unchecked((short)value);
                return Unsafe.As<short, TEnum>(ref converted);
            }
            case EnumWidth.UInt16:
            {
                var converted = (ushort)value;
                return Unsafe.As<ushort, TEnum>(ref converted);
            }
            case EnumWidth.Int32:
            {
                var converted = unchecked((int)value);
                return Unsafe.As<int, TEnum>(ref converted);
            }
            case EnumWidth.UInt32:
            {
                var converted = (uint)value;
                return Unsafe.As<uint, TEnum>(ref converted);
            }
            case EnumWidth.Int64:
            {
                var converted = unchecked((long)value);
                return Unsafe.As<long, TEnum>(ref converted);
            }
            case EnumWidth.UInt64:
            {
                return Unsafe.As<ulong, TEnum>(ref value);
            }
            default:
                throw new InvalidOperationException($"Unsupported enum width {Width}.");
        }
    }

    private static FrozenDictionary<string, TEnum> CreateNameToValue()
    {
        var names = Enum.GetNames<TEnum>();
        var values = Enum.GetValues<TEnum>();
        var pairs = new KeyValuePair<string, TEnum>[names.Length];
        for (var i = 0; i < names.Length; i++)
        {
            pairs[i] = new KeyValuePair<string, TEnum>(names[i], values[i]);
        }

        // Enum member names are unique, so the pair list always has distinct keys.
        return pairs.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static FrozenDictionary<ulong, string> CreateValueToName()
    {
        var names = Enum.GetNames<TEnum>();
        var values = Enum.GetValues<TEnum>();
        Dictionary<ulong, string>? pairs = null;
        for (var i = 0; i < names.Length; i++)
        {
            // TryAdd keeps the first declared name for duplicate values, matching
            // System.Text.Json.
            (pairs ??= []).TryAdd(ToUInt64(values[i]), names[i]);
        }

        return (pairs ?? []).ToFrozenDictionary();
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

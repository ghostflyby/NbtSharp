using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace NbtSharp.Converters;

internal static class NbtArrayConverters
{
    public static readonly NbtConverter<byte[]> ByteArray = new NbtByteArrayConverter();

    public static readonly NbtConverter<sbyte[]> SByteArray = new NbtSByteArrayConverter();

    public static readonly NbtConverter<int[]> Int32Array = new NbtInt32ArrayConverter();

    public static readonly NbtConverter<long[]> Int64Array = new NbtInt64ArrayConverter();

    private sealed class NbtByteArrayConverter : NbtConverter<byte[]>
    {
        public override byte[] Read(ref NbtReader reader, NbtSerializerOptions options) => reader.GetByteArray();

        public override void Write(ref NbtWriter writer, byte[] value, NbtSerializerOptions options) =>
            writer.WriteByteArray(value);

        public override bool TryGetTagType(byte[] value, NbtSerializerOptions options, out NbtTagType tagType)
        {
            tagType = NbtTagType.ByteArray;
            return true;
        }
    }

    private sealed class NbtSByteArrayConverter : NbtConverter<sbyte[]>
    {
        public override sbyte[] Read(ref NbtReader reader, NbtSerializerOptions options) =>
            MemoryMarshal.Cast<byte, sbyte>(reader.GetByteArray().AsSpan()).ToArray();

        public override void Write(ref NbtWriter writer, sbyte[] value, NbtSerializerOptions options) =>
            writer.WriteByteArray(MemoryMarshal.Cast<sbyte, byte>(value.AsSpan()));

        public override bool TryGetTagType(sbyte[] value, NbtSerializerOptions options, out NbtTagType tagType)
        {
            tagType = NbtTagType.ByteArray;
            return true;
        }
    }

    private sealed class NbtInt32ArrayConverter : NbtConverter<int[]>
    {
        public override int[] Read(ref NbtReader reader, NbtSerializerOptions options) => reader.GetInt32Array();

        public override void Write(ref NbtWriter writer, int[] value, NbtSerializerOptions options) =>
            writer.WriteInt32Array(value);

        public override bool TryGetTagType(int[] value, NbtSerializerOptions options, out NbtTagType tagType)
        {
            tagType = NbtTagType.IntArray;
            return true;
        }
    }

    private sealed class NbtInt64ArrayConverter : NbtConverter<long[]>
    {
        public override long[] Read(ref NbtReader reader, NbtSerializerOptions options) => reader.GetInt64Array();

        public override void Write(ref NbtWriter writer, long[] value, NbtSerializerOptions options) =>
            writer.WriteInt64Array(value);

        public override bool TryGetTagType(long[] value, NbtSerializerOptions options, out NbtTagType tagType)
        {
            tagType = NbtTagType.LongArray;
            return true;
        }
    }
}

/// <summary>
/// Converts arrays that do not have a dedicated NBT array tag into NBT lists.
/// </summary>
[UnconditionalSuppressMessage("Aot", "IL2026", Justification = "Reached only through annotated serializer entry points.")]
[UnconditionalSuppressMessage("Aot", "IL3050", Justification = "Reached only through annotated serializer entry points.")]
internal sealed class NbtArrayConverter<TElement> : NbtConverter<TElement[]>
{
    public override TElement[] Read(ref NbtReader reader, NbtSerializerOptions options) =>
        NbtCollectionReader.ReadList<TElement>(ref reader, options).ToArray();

    public override void Write(ref NbtWriter writer, TElement[] value, NbtSerializerOptions options) =>
        NbtCollectionWriter.WriteList(ref writer, value, options);

    public override bool TryGetTagType(TElement[] value, NbtSerializerOptions options, out NbtTagType tagType)
    {
        tagType = NbtTagType.List;
        return true;
    }
}

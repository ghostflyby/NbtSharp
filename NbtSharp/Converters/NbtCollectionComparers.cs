namespace NbtSharp.Converters;

/// <summary>
/// Chooses the ordering used when a sorted collection is constructed from NBT values. String
/// elements use ordinal ordering so deserialization does not depend on the current culture,
/// matching the comparers used by the string-keyed map builders.
/// </summary>
internal static class NbtCollectionComparers
{
    public static IComparer<T> Default<T>() =>
        typeof(T) == typeof(string)
            ? (IComparer<T>)(object)StringComparer.Ordinal
            : Comparer<T>.Default;
}

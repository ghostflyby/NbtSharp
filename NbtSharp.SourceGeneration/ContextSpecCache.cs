using System.Collections.Concurrent;

namespace NbtSharp.SourceGeneration;

/// <summary>
/// Caches symbol-free prepared contexts by their input dependency fingerprint. The cache prevents
/// repeated graph construction when the same context is prepared again by another compilation or
/// driver instance.
/// </summary>
internal static class ContextSpecCache
{
    private const int Capacity = 256;
    private static readonly ConcurrentDictionary<string, PreparedContext> Entries =
        new(StringComparer.Ordinal);

    public static bool TryGet(string inputFingerprint, out PreparedContext prepared) =>
        Entries.TryGetValue(inputFingerprint, out prepared!);

    public static void Add(string inputFingerprint, PreparedContext prepared)
    {
        if (Entries.Count >= Capacity)
        {
            Entries.Clear();
        }

        Entries[inputFingerprint] = prepared;
    }

    internal static void ResetForTests() => Entries.Clear();
}

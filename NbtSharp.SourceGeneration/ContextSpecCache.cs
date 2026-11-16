using System.Collections.Concurrent;

namespace NbtSharp.SourceGeneration;

/// <summary>
/// Caches symbol-free prepared contexts by their content fingerprint. The cache prevents repeated
/// graph construction when the same context is prepared again by another compilation or driver
/// instance; positional-only changes deliberately reuse the cached spec unless it contains
/// diagnostics, whose locations must be refreshed.
/// </summary>
internal static class ContextSpecCache
{
    private const int Capacity = 256;
    private static readonly ConcurrentDictionary<string, PreparedContext> Entries =
        new(StringComparer.Ordinal);

    public static bool TryGet(string contentFingerprint, out PreparedContext prepared) =>
        Entries.TryGetValue(contentFingerprint, out prepared!);

    public static void Add(string contentFingerprint, PreparedContext prepared)
    {
        if (Entries.Count >= Capacity)
        {
            foreach (var key in Entries.Keys)
            {
                Entries.TryRemove(key, out _);
                break;
            }
        }

        Entries[contentFingerprint] = prepared;
    }

    internal static void ResetForTests() => Entries.Clear();
}

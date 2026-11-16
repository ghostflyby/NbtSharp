using System.Collections.Concurrent;

namespace NbtSharp.SourceGeneration;

/// <summary>
/// Caches symbol-free prepared contexts by their content fingerprint. The cache prevents repeated
/// graph construction when the same context is prepared again by another compilation or driver
/// instance; positional-only changes deliberately reuse the cached spec. Diagnostic-bearing specs are
/// never cached, so their locations are rebuilt on each transform run.
/// </summary>
internal static class ContextSpecCache
{
    // Keep prepared specs for realistic solution sizes. Eviction is FIFO and exceptional; the cache
    // lets Prepare skip graph construction for unchanged contexts. The budget is process-wide.
    private const int DefaultCapacity = 4096;
    private static readonly ConcurrentDictionary<string, PreparedContext> Entries =
        new(StringComparer.Ordinal);
    private static readonly ConcurrentQueue<string> Order = new();
    private static readonly object Gate = new();
    private static int _count;
    private static int _capacity = DefaultCapacity;

    public static bool TryGet(string contentFingerprint, out PreparedContext prepared) =>
        Entries.TryGetValue(contentFingerprint, out prepared!);

    public static void Add(string contentFingerprint, PreparedContext prepared)
    {
        if (prepared.Diagnostics.Length != 0)
        {
            return;
        }

        lock (Gate)
        {
            if (Entries.ContainsKey(contentFingerprint))
            {
                Entries[contentFingerprint] = prepared;
                return;
            }

            while (_count >= _capacity && Order.TryDequeue(out var oldest))
            {
                if (Entries.TryRemove(oldest, out _))
                {
                    _count--;
                }
            }

            if (_count >= _capacity)
            {
                return;
            }

            Entries[contentFingerprint] = prepared;
            Order.Enqueue(contentFingerprint);
            _count++;
        }
    }

    internal static void ResetForTests()
    {
        lock (Gate)
        {
            Entries.Clear();
            while (Order.TryDequeue(out _))
            {
            }

            _count = 0;
            _capacity = DefaultCapacity;
        }
    }

    internal static void SetCapacityForTests(int capacity)
    {
        if (capacity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        lock (Gate)
        {
            _capacity = capacity;
            while (_count > _capacity && Order.TryDequeue(out var oldest))
            {
                if (Entries.TryRemove(oldest, out _))
                {
                    _count--;
                }
            }
        }
    }
}

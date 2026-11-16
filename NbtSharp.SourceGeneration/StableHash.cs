using System.Globalization;

namespace NbtSharp.SourceGeneration;

/// <summary>
/// A deterministic 128-bit hash used for incremental fingerprints. Unlike
/// <see cref="string.GetHashCode()"/>, the result is stable across processes.
/// </summary>
internal struct StableHash
{
    private ulong _first;
    private ulong _second;

    private StableHash(ulong first, ulong second)
    {
        _first = first;
        _second = second;
    }

    public static StableHash Create() =>
        new(14695981039346656037UL, 0x9E3779B97F4A7C15UL);

    public void Add(bool value) => Add(value ? 1 : 0);

    public void Add(int value)
    {
        unchecked
        {
            _first = (_first ^ (uint)value) * 1099511628211UL;
            _second = (_second ^ (uint)value) * 14029467366897019727UL;
        }
    }

    public void Add(byte value)
    {
        unchecked
        {
            _first = (_first ^ value) * 1099511628211UL;
            _second = (_second ^ value) * 14029467366897019727UL;
        }
    }

    public void Add(string? value)
    {
        if (value is null)
        {
            Add(-1);
            return;
        }

        Add(value.Length);
        foreach (var character in value)
        {
            Add(character);
        }
    }

    public void Add(char value) => Add((int)value);

    public string ToHex() =>
        _first.ToString("x16", CultureInfo.InvariantCulture) +
        _second.ToString("x16", CultureInfo.InvariantCulture);
}

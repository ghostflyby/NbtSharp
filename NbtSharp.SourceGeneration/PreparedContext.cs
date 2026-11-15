using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace NbtSharp.SourceGeneration;

internal sealed class PreparedContext : IEquatable<PreparedContext>
{
    public PreparedContext(
        ContextModel model,
        TypeGraph? graph,
        string hintName,
        ImmutableArray<Diagnostic> diagnostics,
        string fingerprint)
    {
        Model = model;
        Graph = graph;
        HintName = hintName;
        Diagnostics = diagnostics;
        Fingerprint = fingerprint;
    }

    public ContextModel Model { get; }

    public TypeGraph? Graph { get; }

    public string HintName { get; }

    public ImmutableArray<Diagnostic> Diagnostics { get; }

    public string Fingerprint { get; }

    public bool HasSource => Graph is not null;

    public bool Equals(PreparedContext? other) =>
        other is not null && StringComparer.Ordinal.Equals(Fingerprint, other.Fingerprint);

    public override bool Equals(object? obj) => obj is PreparedContext other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Fingerprint);
}

internal sealed class PreparedContextComparer : IEqualityComparer<PreparedContext>
{
    public static readonly PreparedContextComparer Instance = new();

    public bool Equals(PreparedContext? x, PreparedContext? y) => x?.Equals(y) ?? y is null;

    public int GetHashCode(PreparedContext obj) => obj.GetHashCode();
}

using System.Collections.Immutable;

namespace NbtSharp.SourceGeneration;

internal sealed class PreparedContext : IEquatable<PreparedContext>
{
    public PreparedContext(ContextSpec spec)
    {
        Spec = spec;
    }

    public ContextSpec Spec { get; }

    public string HintName => Spec.HintName;

    public ImmutableArray<DiagnosticSpec> Diagnostics => Spec.Diagnostics;

    public string Fingerprint => Spec.Fingerprint;

    public bool HasSource => Spec.HasSource;

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

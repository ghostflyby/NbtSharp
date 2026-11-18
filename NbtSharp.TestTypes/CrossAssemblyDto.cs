namespace NbtSharp.TestTypes;

/// <summary>
/// Exercises cross-assembly access to a non-public setter, an init accessor, and a required member.
/// </summary>
public sealed class CrossAssemblyDto
{
    public string Name { get; internal set; } = "";

    public int Value { get; init; }

    public required string RequiredName { get; init; }

    public static CrossAssemblyDto Create(string name, int value) => new()
    {
        Name = name,
        Value = value,
        RequiredName = name,
    };
}

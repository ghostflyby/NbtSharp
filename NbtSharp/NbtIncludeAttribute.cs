namespace NbtSharp;

/// <summary>
/// Includes a public instance field in reflection-based object mapping.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class NbtIncludeAttribute : Attribute;

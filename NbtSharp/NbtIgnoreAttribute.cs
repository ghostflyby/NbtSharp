namespace NbtSharp;

/// <summary>
/// Excludes a property or field from reflection-based object mapping.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class NbtIgnoreAttribute : Attribute;

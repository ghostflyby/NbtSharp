namespace NbtSharp;

/// <summary>
/// Supplies <see cref="NbtTypeInfo"/> instances for CLR types.
/// </summary>
public interface INbtTypeInfoResolver
{
    /// <summary>Gets the type information for <paramref name="type"/>, or <see langword="null"/>.</summary>
    /// <param name="type">The CLR type to resolve.</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>The type information, or <see langword="null"/> if this resolver does not handle the type.</returns>
    NbtTypeInfo? GetTypeInfo(Type type, NbtSerializerOptions options);
}

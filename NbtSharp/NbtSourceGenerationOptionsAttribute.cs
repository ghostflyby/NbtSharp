namespace NbtSharp;

/// <summary>
/// Configures default member mapping for a source-generated <see cref="NbtSerializerContext"/>.
/// </summary>
/// <remarks>
/// The naming policy is applied at generation time to members without
/// <see cref="NbtPropertyNameAttribute"/>. The generated context also exposes the policy and null
/// handling through its default <see cref="NbtSerializerOptions"/>, so reflection mappings resolved
/// with those options use the same policy; setting
/// <see cref="NbtSerializerOptions.PropertyNamingPolicy"/> directly does not rename members that
/// were already generated.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class NbtSourceGenerationOptionsAttribute : Attribute
{
    /// <summary>Gets or sets the naming policy for members without <see cref="NbtPropertyNameAttribute"/>.</summary>
    // ReSharper disable once UnusedAutoPropertyAccessor.Global
    public NbtNamingPolicyKind NamingPolicy { get; set; }

    /// <summary>Gets or sets the default null handling for the context's default options.</summary>
    // ReSharper disable once UnusedAutoPropertyAccessor.Global
    public NbtNullValueHandling NullValueHandling { get; set; } = NbtNullValueHandling.Omit;
}

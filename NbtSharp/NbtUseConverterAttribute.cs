namespace NbtSharp;

/// <summary>
/// Marks a property or field as using a converter that is resolved outside the generated type graph
/// or reflection's object mapping.
/// </summary>
/// <remarks>
/// <para>
/// With no arguments, the member is read and written through <see cref="NbtSerializerOptions.Converters"/>
/// at runtime. This is useful for types the source generator does not model, such as <see cref="Guid"/>
/// or <see cref="decimal"/>.
/// </para>
/// <para>
/// With a converter type, generated code creates that converter directly and reflection instantiates
/// it. Either way the explicit converter takes precedence over <see cref="NbtSerializerOptions.Converters"/>
/// for the member. The converter type must be a closed, non-abstract type derived from
/// <see cref="NbtConverter"/> with a public parameterless constructor.
/// </para>
/// <para>
/// Fields must also be marked with <see cref="NbtIncludeAttribute"/> to participate in the mapping.
/// </para>
/// </remarks>
[AttributeUsage(
    AttributeTargets.Property | AttributeTargets.Field,
    AllowMultiple = false,
    Inherited = true)]
public sealed class NbtUseConverterAttribute : Attribute
{
    /// <summary>Initializes the attribute for runtime converter resolution.</summary>
    public NbtUseConverterAttribute()
    {
    }

    /// <summary>Initializes the attribute with an explicit converter type.</summary>
    /// <param name="converterType">The converter type used for the marked member.</param>
    public NbtUseConverterAttribute(Type converterType)
    {
        ArgumentNullException.ThrowIfNull(converterType);
        ConverterType = converterType;
    }

    /// <summary>
    /// Gets the explicit converter type, or <see langword="null"/> when the converter is resolved at
    /// runtime.
    /// </summary>
    // ReSharper disable once UnusedAutoPropertyAccessor.Global
    public Type? ConverterType { get; }
}

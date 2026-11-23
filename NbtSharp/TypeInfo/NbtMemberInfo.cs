using System.Reflection;

// The public NbtSharp namespace is intentional; the folder groups internal implementation.
// ReSharper disable once CheckNamespace
namespace NbtSharp;

/// <summary>
/// One mapped member of an <see cref="NbtContractInfo"/>, mutable while contract modifier callbacks
/// run and frozen afterwards.
/// </summary>
/// <remarks>
/// <para>
/// Instances are created by reflection discovery and handed to
/// <see cref="NbtReflectionTypeInfoResolver"/> modifier callbacks. <see cref="ClrName"/>,
/// <see cref="Type"/>, and <see cref="CanWrite"/> reflect the CLR member and cannot be changed;
/// <see cref="NbtName"/>, <see cref="Ignore"/>, <see cref="Converter"/>, and <see cref="Order"/>
/// adjust how the member is serialized.
/// </para>
/// <para>
/// Once the contract is frozen, every setter throws <see cref="InvalidOperationException"/>, so a
/// reference stashed by a callback cannot mutate a cached contract later.
/// </para>
/// </remarks>
public sealed class NbtMemberInfo
{
    private string _nbtName;
    private int _order;
    private bool _frozen;

    internal NbtMemberInfo(string nbtName, PropertyInfo property, int order)
    {
        _nbtName = nbtName;
        Property = property;
        _order = order;
    }

    internal NbtMemberInfo(string nbtName, FieldInfo field, int order)
    {
        _nbtName = nbtName;
        Field = field;
        _order = order;
    }

    /// <summary>Gets the CLR name of the property or field, independent of any renaming.</summary>
    public string ClrName => Property?.Name ?? Field!.Name;

    /// <summary>Gets the CLR type of the property or field.</summary>
    public Type Type => Property?.PropertyType ?? Field!.FieldType;

    /// <summary>
    /// Gets whether the member can be assigned after construction. Ignored members that a
    /// constructor does not bind are skipped on read.
    /// </summary>
    public bool CanWrite => Property?.CanWrite == true || Field is { IsInitOnly: false, IsLiteral: false };

    /// <summary>
    /// Gets or sets the NBT name of the member, already reflecting
    /// <see cref="NbtPropertyNameAttribute"/> and
    /// <see cref="NbtSerializerOptions.PropertyNamingPolicy"/>. The default is the discovered name.
    /// </summary>
    /// <remarks>
    /// An empty string is allowed; a <see langword="null"/> value throws
    /// <see cref="ArgumentNullException"/>. Setting the same value again is allowed.
    /// </remarks>
    /// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The contract is frozen.</exception>
    public string NbtName
    {
        get => _nbtName;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            ThrowIfFrozen();
            _nbtName = value;
        }
    }

    /// <summary>
    /// Gets or sets whether the member is excluded from the contract, the runtime equivalent of
    /// <see cref="NbtIgnoreAttribute"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The contract is frozen.</exception>
    public bool Ignore
    {
        get;
        set
        {
            ThrowIfFrozen();
            field = value;
        }
    }

    /// <summary>
    /// Gets or sets an explicit converter for this member, replacing an
    /// <see cref="NbtUseConverterAttribute"/> converter when set. <see langword="null"/> (the
    /// default) keeps attribute-based resolution or, without an attribute, regular options-based
    /// resolution.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A non-factory instance must report <see langword="true"/> from
    /// <see cref="NbtConverter.CanConvert"/> for <see cref="Type"/>; a mismatch throws
    /// <see cref="NotSupportedException"/> while the contract is built.
    /// <see cref="NbtConverterFactory"/> instances are not checked at build time; the converter
    /// they produce is validated when the member is first read or written.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">The contract is frozen.</exception>
    public NbtConverter? Converter
    {
        get;
        set
        {
            ThrowIfFrozen();
            field = value;
        }
    }

    /// <summary>
    /// Gets or sets the sort key of the member. The default is the declaration ordinal, so members
    /// are written in declaration order; equal keys keep declaration order.
    /// </summary>
    /// <exception cref="InvalidOperationException">The contract is frozen.</exception>
    public int Order
    {
        get => _order;
        set
        {
            ThrowIfFrozen();
            _order = value;
        }
    }

    internal PropertyInfo? Property { get; }

    internal FieldInfo? Field { get; }

    internal int MetadataToken => Property?.MetadataToken ?? Field!.MetadataToken;

    internal bool IsField => Field is not null;

    internal NbtUseConverterAttribute? UseConverter { get; init; }

    internal void Freeze() => _frozen = true;

    private void ThrowIfFrozen()
    {
        if (_frozen)
        {
            throw new InvalidOperationException(
                "This NbtMemberInfo belongs to a frozen contract; modify members only inside a " +
                "contract modifier callback.");
        }
    }
}

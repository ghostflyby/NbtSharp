using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;

// The public NbtSharp namespace is intentional; the folder groups internal implementation.
// ReSharper disable once CheckNamespace
namespace NbtSharp;

/// <summary>
/// Supplies reflection-based type information for ordinary classes and structs, plus polymorphic
/// envelopes for abstract classes and interfaces with <see cref="NbtDerivedAttribute"/> registrations.
/// </summary>
/// <remarks>
/// <para>
/// Properties are mapped by name; public fields are included only when marked with
/// <see cref="NbtIncludeAttribute"/>. <see cref="NbtIgnoreAttribute"/> excludes a member,
/// <see cref="NbtPropertyNameAttribute"/> renames it, and
/// <see cref="NbtSerializerOptions.PropertyNamingPolicy"/> renames members without an explicit
/// name.
/// </para>
/// <para>
/// This resolver uses reflection and runtime generic construction. It is not safe for Native AOT
/// or aggressive trimming; source-generated type information provides the AOT path.
/// </para>
/// </remarks>
[RequiresUnreferencedCode("Reflection-based object mapping is not trimming-safe.")]
[RequiresDynamicCode("Reflection-based object mapping may require runtime code generation.")]
public sealed class NbtReflectionTypeInfoResolver : INbtTypeInfoResolver
{
    private static readonly HashSet<Type> UnsupportedTypes =
    [
        typeof(object),
        typeof(NbtRoot),
        typeof(NbtValue),
        typeof(NbtList),
        typeof(NbtCompound),
        typeof(NbtEndList),
        typeof(nint),
        typeof(nuint),
        typeof(decimal),
        typeof(Guid),
        typeof(DateTime),
        typeof(DateTimeOffset),
        typeof(TimeSpan),
        typeof(DateOnly),
        typeof(TimeOnly),
        typeof(Version),
        typeof(Uri),
        typeof(Type),
        typeof(Int128),
        typeof(UInt128),
        typeof(Half),
        typeof(BigInteger),
    ];

    private readonly Dictionary<(Type Type, NbtNamingPolicy? NamingPolicy), NbtConverter> _cache = [];
    private readonly Lock _cacheLock = new();

    /// <inheritdoc/>
    public NbtTypeInfo? GetTypeInfo(Type type, NbtSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(options);

        if (!IsSupported(type))
        {
            return null;
        }

        var key = (type, options.PropertyNamingPolicy);
        lock (_cacheLock)
        {
            if (!_cache.TryGetValue(key, out var converter))
            {
                converter = HasDerivedRegistrations(type)
                    ? new NbtPolymorphicReflectionConverter(type)
                    : new NbtReflectionObjectConverter(type, options.PropertyNamingPolicy);
                _cache[key] = converter;
            }

            return new NbtReflectionTypeInfo(type, converter, options);
        }
    }

    private static bool IsSupported(Type type)
    {
        if (type.IsPointer || type.IsByRef || type.IsByRefLike)
        {
            return false;
        }

        if (type.IsGenericTypeDefinition || type.IsEnum || type.IsPrimitive)
        {
            return false;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            return false;
        }

        if (UnsupportedTypes.Contains(type))
        {
            return false;
        }

        if (typeof(Delegate).IsAssignableFrom(type) ||
            typeof(IEnumerable).IsAssignableFrom(type))
        {
            return false;
        }

        if (type.IsInterface || type.IsAbstract)
        {
            return HasDerivedRegistrations(type);
        }

        return type.IsClass || type.IsValueType;
    }

    private static bool HasDerivedRegistrations(Type type) =>
        type.GetCustomAttributes(typeof(NbtDerivedAttribute), inherit: false).Length > 0;
}

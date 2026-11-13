using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace NbtSharp;

/// <summary>
/// Supplies reflection-based type information for ordinary classes and structs.
/// </summary>
/// <remarks>
/// <para>
/// Properties are mapped by name; public fields are included only when marked with
/// <see cref="NbtIncludeAttribute"/>. <see cref="NbtIgnoreAttribute"/> excludes a member and
/// <see cref="NbtPropertyNameAttribute"/> renames it.
/// </para>
/// <para>
/// This resolver uses reflection and runtime generic construction. It is not safe for Native AOT
/// or aggressive trimming; source-generated type information will provide the AOT path.
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
        typeof(char),
        typeof(ushort),
        typeof(uint),
        typeof(ulong),
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

    private readonly Dictionary<Type, NbtTypeInfo> _cache = [];
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

        lock (_cacheLock)
        {
            if (_cache.TryGetValue(type, out var cached))
            {
                return cached;
            }

            var converter = new NbtReflectionObjectConverter(type);
            var typeInfo = new NbtReflectionTypeInfo(type, converter);
            _cache[type] = typeInfo;
            return typeInfo;
        }
    }

    private static bool IsSupported(Type type)
    {
        if (type.IsPointer || type.IsByRef || type.IsByRefLike || type.IsInterface || type.IsAbstract)
        {
            return false;
        }

        if (type.IsGenericTypeDefinition || type.IsEnum || type.IsPrimitive)
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

        return type.IsClass || type.IsValueType;
    }
}

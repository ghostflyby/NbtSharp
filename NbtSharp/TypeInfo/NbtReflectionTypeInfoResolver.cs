using System.Collections;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;

// The public NbtSharp namespace is intentional; the folder groups internal implementation.
// ReSharper disable once CheckNamespace
namespace NbtSharp;

/// <summary>
/// Supplies reflection-based type information for ordinary classes and structs, plus polymorphic
/// envelopes for abstract classes and interfaces with <see cref="NbtDerivedAttribute"/> registrations,
/// with optional contract modifier callbacks.
/// </summary>
/// <remarks>
/// <para>
/// Properties are mapped by name; public fields are included only when marked with
/// <see cref="NbtIncludeAttribute"/>. <see cref="NbtIgnoreAttribute"/> excludes a member,
/// <see cref="NbtPropertyNameAttribute"/> renames it,
/// <see cref="NbtSerializerOptions.PropertyNamingPolicy"/> renames members without an explicit
/// name, and <see cref="NbtUseConverterAttribute"/> selects a runtime or explicit member converter.
/// Modifier callbacks supplied to this resolver adjust the discovered contract at runtime; see
/// <see cref="NbtReflectionTypeInfoResolver(Action{NbtContractInfo})"/>.
/// </para>
/// <para>
/// Modifiers must be pure functions: they run at most once per (<see cref="Type"/>,
/// <see cref="NbtSerializerOptions.PropertyNamingPolicy"/>) pair, the resulting converter is cached
/// by this resolver, and an exception thrown from a modifier is not cached, so the next operation
/// retries the resolution. Modifiers run while the resolver's cache lock is held, which serializes
/// type resolution for this resolver instance; they must be short and non-blocking. Unlike
/// <c>System.Text.Json</c>, the callback does not receive the options instance: the contract is
/// fixed per (type, naming policy) on first use, so the same type resolved under options that
/// differ only in other settings shares one contract.
/// </para>
/// <para>
/// Resolving other types inside a modifier is supported when it does not loop back to a type that
/// is currently being built (same-thread lock reentrancy handles nested resolutions; cached types
/// resolve without rebuilding). Looping back throws <see cref="InvalidOperationException"/> instead
/// of overflowing the stack. Blocking inside a modifier on another thread that is resolving a type
/// through the same options deadlocks and is not supported.
/// </para>
/// <para>
/// Polymorphic envelopes built from <see cref="NbtDerivedAttribute"/> registrations are
/// attribute-driven and do not run modifier callbacks; the derived concrete types resolve through
/// this resolver as usual and do run them. In an <see cref="NbtTypeInfoResolverChain"/> a modified
/// reflection resolver only affects types that fall through to the reflection fallback; types
/// served by an earlier resolver (for example a generated context) are untouched. Source-generated
/// contexts bake their contracts in at compile time, so there is no interface-level modifier hook
/// on <see cref="INbtTypeInfoResolver"/>.
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
    private static readonly ImmutableHashSet<Type> UnsupportedTypes =
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

    private readonly Action<NbtContractInfo>[] _modifiers;
    private readonly Dictionary<(Type Type, NbtNamingPolicy? NamingPolicy), NbtConverter> _cache = [];
    private readonly HashSet<(Type Type, NbtNamingPolicy? NamingPolicy)> _inFlight = [];
    private readonly Lock _cacheLock = new();

    /// <summary>Initializes a resolver without contract modifier callbacks.</summary>
    public NbtReflectionTypeInfoResolver()
        : this([])
    {
    }

    /// <summary>
    /// Initializes a resolver that runs <paramref name="modifier"/> against every discovered
    /// contract before it is cached.
    /// </summary>
    /// <param name="modifier">
    /// The callback, or <see langword="null"/> for no modification. The callback receives the
    /// discovered members with their final attribute- and policy-derived names and may rename,
    /// ignore, reorder, or replace the converter of any member.
    /// </param>
    /// <remarks>
    /// <para>
    /// The callback must be a pure function: it runs at most once per
    /// (<see cref="Type"/>, <see cref="NbtSerializerOptions.PropertyNamingPolicy"/>) pair, the
    /// result is cached, and an exception is not cached, so a later operation retries. It runs
    /// under the resolver's cache lock and must be short and non-blocking.
    /// </para>
    /// <para>
    /// The callback does not receive the <see cref="NbtSerializerOptions"/> instance; the contract
    /// is keyed only by type and naming policy, unlike
    /// <c>System.Text.Json.JsonTypeInfo.Options</c>. Do not block inside the callback on another
    /// thread resolving types through the same options.
    /// </para>
    /// </remarks>
    public NbtReflectionTypeInfoResolver(Action<NbtContractInfo>? modifier)
        : this(modifier is null ? [] : [modifier])
    {
    }

    private NbtReflectionTypeInfoResolver(Action<NbtContractInfo>[] modifiers)
    {
        _modifiers = modifiers;
    }

    /// <summary>
    /// Creates a resolver whose callbacks are the callbacks of this instance plus
    /// <paramref name="modifier"/> appended, each carrying its own resolution cache.
    /// </summary>
    /// <param name="modifier">The callback to append; runs after every callback of this instance.</param>
    /// <returns>A new resolver; this instance and its cache are unchanged.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="modifier"/> is <see langword="null"/>.</exception>
    public NbtReflectionTypeInfoResolver WithAddedModifier(Action<NbtContractInfo> modifier)
    {
        ArgumentNullException.ThrowIfNull(modifier);

        return new NbtReflectionTypeInfoResolver([.. _modifiers, modifier]);
    }

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
                if (!_inFlight.Add(key))
                {
                    throw new InvalidOperationException(
                        $"A contract modifier callback re-entered the type '{type}' while its " +
                        "converter is still being built; a modifier must not loop back to the " +
                        "type currently being resolved.");
                }

                try
                {
                    // The polymorphic envelope is attribute-driven and takes no modifiers; the
                    // derived concrete types resolve through this resolver and run them.
                    converter = HasDerivedRegistrations(type)
                        ? new NbtPolymorphicReflectionConverter(type)
                        : new NbtReflectionObjectConverter(type, options.PropertyNamingPolicy, _modifiers);
                    _cache[key] = converter;
                }
                finally
                {
                    // A failed build (including a modifier exception) is not cached; the in-flight
                    // marker is released so the next operation retries.
                    _inFlight.Remove(key);
                }
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

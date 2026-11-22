using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace NbtSharp;

/// <summary>
/// Options that control serialization, deserialization, converter resolution, and NBT limits.
/// </summary>
/// <remarks>
/// <para>
/// An instance may be reused across operations. Mutating an instance while an operation is running
/// is not supported. A value of 0 for a limit selects the library default; for
/// <see cref="MaxDocumentBytes"/>, 0 means that the size is not limited.
/// </para>
/// <para>
/// The shared <see cref="Default"/> instance freezes on its first use by the serializer: once any
/// options-less operation has run, mutating <see cref="Converters"/> through it throws
/// <see cref="InvalidOperationException"/>. Instances created with the constructor never freeze;
/// mutating them between operations stays supported and invalidates the resolution cache.
/// </para>
/// </remarks>
public sealed class NbtSerializerOptions
{
    private static readonly NbtSerializerOptions DefaultInstance = new(isSharedDefault: true);

    private readonly bool _isSharedDefault;
    private volatile bool _isFrozen;
    private readonly List<NbtConverter> _converterItems = [];
    private readonly NbtConverterCollection _converters;
    private volatile ImmutableDictionary<Type, NbtConverter> _cache =
        ImmutableDictionary<Type, NbtConverter>.Empty;
    private readonly Lock _cacheLock = new();

    /// <summary>Initializes the options with the default limits.</summary>
    public NbtSerializerOptions()
        : this(isSharedDefault: false)
    {
    }

    private NbtSerializerOptions(bool isSharedDefault)
    {
        _isSharedDefault = isSharedDefault;
        _converters = new NbtConverterCollection(this, _converterItems);
    }

    /// <summary>
    /// Gets the shared default options used by every overload that receives no explicit options.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The instance is constructed with the library defaults. Unlike
    /// <c>System.Text.Json.JsonSerializerOptions.Default</c>, NbtSharp options are init-only except
    /// for <see cref="Converters"/>, so the limits, policies, and resolver of this instance cannot
    /// be reconfigured; create an <see cref="NbtSerializerOptions"/> instance for custom settings.
    /// Converters may be registered through <see cref="Converters"/> until the first options-less
    /// operation freezes the instance; later mutation attempts throw
    /// <see cref="InvalidOperationException"/>.
    /// </para>
    /// <para>
    /// This property is unrelated to the <c>Default</c> singleton of a source-generated context:
    /// <see cref="NbtSerializerContext.Options"/> of such a context is a separate instance bound to
    /// that context, and it is never frozen.
    /// </para>
    /// </remarks>
    public static NbtSerializerOptions Default => DefaultInstance;

    /// <summary>
    /// Gets the custom converters, checked in order before the built-in converters. Mutating the
    /// list invalidates the resolution cache. Mutating the list of the shared
    /// <see cref="Default"/> instance after its first use throws
    /// <see cref="InvalidOperationException"/>.
    /// </summary>
    public IList<NbtConverter> Converters => _converters;

    /// <summary>
    /// Creates a copy of these options that uses <paramref name="resolver"/> as its type information
    /// resolver. The current instance is not modified.
    /// </summary>
    /// <param name="resolver">The resolver for the copied options.</param>
    /// <returns>A copy of these options bound to <paramref name="resolver"/>.</returns>
    public NbtSerializerOptions WithTypeInfoResolver(INbtTypeInfoResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        var copy = new NbtSerializerOptions
        {
            TypeInfoResolver = resolver,
            PropertyNamingPolicy = PropertyNamingPolicy,
            RootFormat = RootFormat,
            NullValueHandling = NullValueHandling,
            RejectDuplicateCompoundNames = RejectDuplicateCompoundNames,
            MaxDepth = MaxDepth,
            MaxCollectionLength = MaxCollectionLength,
            MaxDocumentBytes = MaxDocumentBytes,
        };

        foreach (var converter in _converters)
        {
            copy.Converters.Add(converter);
        }

        return copy;
    }

    /// <summary>
    /// Gets or initializes the resolver that supplies type information for CLR types. The resolver
    /// is consulted after <see cref="Converters"/> and before the built-in converters.
    /// </summary>
    public INbtTypeInfoResolver? TypeInfoResolver { get; init; }

    /// <summary>
    /// Gets or initializes the naming policy used by reflection mapping for members without
    /// <see cref="NbtPropertyNameAttribute"/>. Source-generated contexts apply their configured
    /// policy at generation time; setting this property does not rename already-generated members.
    /// </summary>
    public NbtNamingPolicy? PropertyNamingPolicy { get; init; }

    /// <summary>Gets or initializes whether the root tag carries a name. The default is named.</summary>
    public NbtRootFormat RootFormat
    {
        get;
        init
        {
            if (value is not NbtRootFormat.Named and not NbtRootFormat.Unnamed)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            field = value;
        }
    } = NbtRootFormat.Named;

    /// <summary>Gets or initializes how null compound members are handled. The default is to omit them.</summary>
    public NbtNullValueHandling NullValueHandling
    {
        get;
        init
        {
            if (value is not NbtNullValueHandling.Omit and not NbtNullValueHandling.Throw)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            field = value;
        }
    } = NbtNullValueHandling.Omit;

    /// <summary>
    /// Gets or initializes whether duplicate compound member names are rejected while reading. The
    /// default is <see langword="false"/>, which keeps the last occurrence.
    /// </summary>
    public bool RejectDuplicateCompoundNames { get; init; }

    /// <summary>Gets or initializes the maximum nesting depth. The default is 512.</summary>
    public int MaxDepth
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    } = 512;

    /// <summary>Gets or initializes the maximum number of elements in a list or array.</summary>
    public int MaxCollectionLength
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    } = 16 * 1024 * 1024;

    /// <summary>Gets or initializes the maximum document size in bytes. 0 means unlimited.</summary>
    public long MaxDocumentBytes
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    }

    internal NbtReaderOptions CreateReaderOptions() => new()
    {
        RootFormat = RootFormat,
        MaxDepth = MaxDepth,
        MaxCollectionLength = MaxCollectionLength,
        MaxDocumentBytes = MaxDocumentBytes,
    };

    internal NbtWriterOptions CreateWriterOptions() => new()
    {
        RootFormat = RootFormat,
        MaxDepth = MaxDepth,
        MaxCollectionLength = MaxCollectionLength,
        MaxDocumentBytes = MaxDocumentBytes,
    };

    [RequiresUnreferencedCode("Converter resolution may construct converters for generic types.")]
    [RequiresDynamicCode("Converter resolution may construct converters for generic types.")]
    internal NbtConverter GetConverter(Type typeToConvert)
    {
        if (TryGetConverter(typeToConvert, out var converter))
        {
            return converter;
        }

        throw new NotSupportedException(
            $"No NBT converter or type information is available for {typeToConvert}. " +
            "Add a converter to NbtSerializerOptions.Converters or configure a TypeInfoResolver.");
    }

    [RequiresUnreferencedCode("Converter resolution may construct converters for generic types.")]
    [RequiresDynamicCode("Converter resolution may construct converters for generic types.")]
    internal bool TryGetConverter(Type typeToConvert, out NbtConverter converter)
    {
        if (_isSharedDefault)
        {
            MakeReadOnly();
        }

        ArgumentNullException.ThrowIfNull(typeToConvert);

        var cache = _cache;
        if (cache.TryGetValue(typeToConvert, out converter!))
        {
            return true;
        }

        lock (_cacheLock)
        {
            cache = _cache;
            if (cache.TryGetValue(typeToConvert, out converter!))
            {
                return true;
            }

            if (!TryResolveConverter(typeToConvert, out converter))
            {
                converter = null!;
                return false;
            }

            _cache = _cache.Add(typeToConvert, converter);
            return true;
        }
    }

    private void InvalidateCore()
    {
        _cache = ImmutableDictionary<Type, NbtConverter>.Empty;
    }

    // Idempotent. The volatile fast path stays lock-free; the write takes _cacheLock so that a
    // freeze and a converter mutation are mutually exclusive. GetCustomConverter is reachable
    // from TryResolveConverter while _cacheLock is already held, and Lock is reentrant on the
    // owning thread, so acquiring the lock again on that path is safe.
    internal void MakeReadOnly()
    {
        if (_isFrozen)
        {
            return;
        }

        lock (_cacheLock)
        {
            _isFrozen = true;
        }
    }

    private void ThrowIfFrozen()
    {
        if (_isFrozen)
        {
            throw new InvalidOperationException(
                "This NbtSerializerOptions instance is read-only because it has already been used " +
                "by the serializer. Create a new NbtSerializerOptions instance to configure converters.");
        }
    }

    /// <summary>
    /// Applies a converter-list mutation under the cache lock as one critical section: the frozen
    /// check, the mutation, and the cache invalidation. Freezing in <see cref="MakeReadOnly"/>
    /// takes the same lock, so a freeze and a mutation are mutually exclusive. Callers must have
    /// validated their arguments; the mutation itself runs with the lock held.
    /// </summary>
    internal void MutateConverters(Action<List<NbtConverter>> mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);

        lock (_cacheLock)
        {
            ThrowIfFrozen();
            mutation(_converterItems);
            InvalidateCore();
        }
    }

    /// <summary>See <see cref="MutateConverters(Action{List{NbtConverter}})"/>.</summary>
    internal T MutateConverters<T>(Func<List<NbtConverter>, T> mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);

        lock (_cacheLock)
        {
            ThrowIfFrozen();
            var result = mutation(_converterItems);
            InvalidateCore();
            return result;
        }
    }

    internal NbtConverter? GetCustomConverter(Type typeToConvert)
    {
        if (_isSharedDefault)
        {
            MakeReadOnly();
        }

        ArgumentNullException.ThrowIfNull(typeToConvert);

        foreach (var converter in _converters)
        {
            if (!converter.CanConvert(typeToConvert))
            {
                continue;
            }

            return converter is NbtConverterFactory factory
                ? factory.CreateConverter(typeToConvert, this)
                : converter;
        }

        return null;
    }

    [RequiresUnreferencedCode("Converter resolution may construct converters for generic types.")]
    [RequiresDynamicCode("Converter resolution may construct converters for generic types.")]
    private bool TryResolveConverter(Type typeToConvert, out NbtConverter converter)
    {
        var custom = GetCustomConverter(typeToConvert);
        if (custom is not null)
        {
            converter = custom;
            return true;
        }

        var typeInfo = TypeInfoResolver?.GetTypeInfo(typeToConvert, this);
        if (typeInfo is not null)
        {
            converter = typeInfo.Converter is NbtConverterFactory typeInfoFactory
                ? typeInfoFactory.CreateConverter(typeToConvert, this)
                : typeInfo.Converter;
            return true;
        }

        if (NbtBuiltInConverters.TryCreate(typeToConvert, out converter))
        {
            return true;
        }

        converter = null!;
        return false;
    }
}

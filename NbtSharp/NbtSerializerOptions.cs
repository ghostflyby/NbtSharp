using System.Diagnostics.CodeAnalysis;

namespace NbtSharp;

/// <summary>
/// Options that control serialization, deserialization, converter resolution, and NBT limits.
/// </summary>
/// <remarks>
/// An instance may be reused across operations. Mutating an instance while an operation is running
/// is not supported. A value of 0 for a limit selects the library default; for
/// <see cref="MaxDocumentBytes"/>, 0 means that the size is not limited.
/// </remarks>
public sealed class NbtSerializerOptions
{
    private readonly NbtConverterCollection _converters;
    private readonly Dictionary<Type, NbtConverter> _cache = [];
    private readonly Lock _cacheLock = new();

    /// <summary>Initializes the options with the default limits.</summary>
    public NbtSerializerOptions()
    {
        _converters = new NbtConverterCollection(this);
    }

    /// <summary>Gets the custom converters, checked in order before the built-in converters.</summary>
    public IList<NbtConverter> Converters => _converters;

    /// <summary>Gets or initializes the resolver that supplies type information for CLR types.</summary>
    public INbtTypeInfoResolver? TypeInfoResolver
    {
        get;
        init => field = value;
    }

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
    public bool RejectDuplicateCompoundNames
    {
        get;
        init => field = value;
    }

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
        ArgumentNullException.ThrowIfNull(typeToConvert);

        lock (_cacheLock)
        {
            if (_cache.TryGetValue(typeToConvert, out var cached))
            {
                return cached;
            }

            var converter = ResolveConverter(typeToConvert);
            _cache[typeToConvert] = converter;
            return converter;
        }
    }

    internal void Invalidate()
    {
        lock (_cacheLock)
        {
            _cache.Clear();
        }
    }

    [RequiresUnreferencedCode("Converter resolution may construct converters for generic types.")]
    [RequiresDynamicCode("Converter resolution may construct converters for generic types.")]
    private NbtConverter ResolveConverter(Type typeToConvert)
    {
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

        if (NbtBuiltInConverters.TryCreate(typeToConvert, out var builtIn))
        {
            return builtIn;
        }

        var typeInfo = TypeInfoResolver?.GetTypeInfo(typeToConvert, this);
        if (typeInfo is not null)
        {
            return typeInfo.Converter is NbtConverterFactory factory
                ? factory.CreateConverter(typeToConvert, this)
                : typeInfo.Converter;
        }

        throw new NotSupportedException(
            $"No NBT converter or type information is available for {typeToConvert}. " +
            "Add a converter to NbtSerializerOptions.Converters or configure a TypeInfoResolver.");
    }
}

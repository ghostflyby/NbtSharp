using Microsoft.CodeAnalysis;

namespace NbtSharp.SourceGeneration;

internal static class Diagnostics
{
    private const string Category = "NbtSharp.SourceGeneration";

    public static readonly DiagnosticDescriptor ContextNotPartial = new(
        "NBTSG001",
        "NbtSerializerContext must be partial",
        "The NbtSerializerContext type '{0}' must have the partial modifier",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ContextInvalid = new(
        "NBTSG002",
        "Invalid NbtSerializerContext",
        "The NbtSerializerContext type '{0}' {1}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedType = new(
        "NBTSG003",
        "Unsupported NBT type",
        "The type '{0}' {1} cannot be mapped by the NBT source generator",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "For a member type, mark the member with [NbtUseConverter] to resolve its converter at runtime, or [NbtUseConverter(typeof(...))] to specify a converter type. Roots and collection elements need a converter in NbtSerializerOptions.Converters.");

    public static readonly DiagnosticDescriptor NoUsableConstructor = new(
        "NBTSG004",
        "No usable constructor",
        "The type '{0}' needs a public parameterless constructor or a public constructor whose parameters match its NBT members",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DuplicateMemberName = new(
        "NBTSG005",
        "Duplicate NBT member name",
        "The type '{0}' maps more than one member to the NBT name '{1}'",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InaccessibleType = new(
        "NBTSG006",
        "Type is not accessible",
        "The type '{0}' is not accessible from the generated context",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RequiredValueType = new(
        "NBTSG007",
        "Required members on value types are not supported",
        "The value type '{0}' has required members; source-generated construction of required value types is not supported",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RequiredMemberNotMapped = new(
        "NBTSG015",
        "Required member is not serializable",
        "The required member '{1}' on type '{0}' is not serialized and the selected constructor is not marked with SetsRequiredMembers",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidUseConverter = new(
        "NBTSG016",
        "Invalid NbtUseConverter declaration",
        "The [NbtUseConverter] declaration on member '{0}' is invalid: {1}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor GeneratedNameConflict = new(
        "NBTSG008",
        "Generated member name conflict",
        "The context '{0}' already declares a member named '{1}'",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NestedContext = new(
        "NBTSG009",
        "Nested contexts are not supported",
        "The NbtSerializerContext type '{0}' must be declared at namespace scope",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedCollection = new(
        "NBTSG011",
        "Unsupported collection type",
        "The collection type '{0}' is not supported; use an array, List<T>, IReadOnlyList<T>, IReadOnlyCollection<T>, or a string-keyed dictionary",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor BuiltInRoot = new(
        "NBTSG013",
        "Type already uses a built-in converter",
        "The type '{0}' is handled by a built-in converter and does not need source generation",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}

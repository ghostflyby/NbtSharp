# Source generation and Native AOT

The source generator emits converters and `NbtTypeInfo<T>` instances at compile time. The
type-information overloads of `NbtSerializer` use that generated metadata, so they carry no
`RequiresUnreferencedCode` or `RequiresDynamicCode` annotations. The generated path is designed to be
safe for Native AOT and trimming when every reachable type is covered by a generated context or by an
AOT-safe custom converter; the repository verifies this with the SDK AOT/trim analyzers at build time,
not with an end-to-end native publish.

## Define a context

```csharp
using NbtSharp;

[NbtSerializable(typeof(Player))]
[NbtSerializable(typeof(Item))]
internal partial class GameContext : NbtSerializerContext
{
}
```

`NbtSerializerContext` supplies the type-information resolver and a `Default` singleton. The
generator emits one property per root (`GameContext.Default.Player`, `.Item`) and nested private type
information for every reachable type.

Use the type-information overloads:

```csharp
var context = GameContext.Default;
byte[] data = NbtSerializer.Serialize(player, context.Player, "Player");
Player copy = NbtSerializer.Deserialize(data, context.Player);
```

The reflection-based generic overloads (`Serialize(value, "Player")`) remain available but are not
AOT-safe.

## What the generator maps

- Built-in scalars: `sbyte`, `byte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `char`,
  `float`, `double`, `bool`, `string`, and their nullable variants.
- Enums, including flags combinations, undefined values, and nullable enums. They use the signed tag
  matching the underlying width; unsigned values preserve their bit pattern.
- Classes and structs with public instance properties, plus public fields marked `[NbtInclude]`.
- Arrays, nullable values, and the supported sequence/map shapes. See the README for the full list.
- Types discovered through `CollectionBuilderAttribute`, companion `Create`/`CreateRange` factories,
  or public constructor/`Add` patterns.

Set-like and sorted collection behavior follows the runtime collection type: sorted maps with string
keys and sorted sets with string elements use ordinal ordering. Unsupported shapes such as
`PriorityQueue<TElement,TPriority>` report `NBTSG003` from the generator and `NotSupportedException`
from the reflection resolver.

## Member selection

- `[NbtPropertyName("name")]` renames a member. An explicit name wins over a context naming policy.
- `[NbtIgnore]` excludes a member, including a base virtual member hidden by an override when the
  attribute is declared on the base property.
- Public fields require `[NbtInclude]`; `[NbtIgnore]` on a field also applies.
- Hidden (`new`) members report `NBTSG017` because NBT has one member namespace per compound.
- Mapping attributes declared on a virtual base property are inherited by overrides.
- Private setters in another assembly are not visible to the generator. The member is still mapped
  through its public getter, but generated deserialization leaves it at its default because there is
  no writable setter symbol, and no diagnostic is reported. Expose the member as `public`/`init`, make
  it `internal` and add `InternalsVisibleTo`, or put the context in the same assembly.

## Constructors and required members

A public parameterless constructor is preferred. Otherwise the generator uses a public constructor
whose parameter names match mapped members. Missing constructor-bound members use the parameter's
declared default value when present, then the CLR default.

Required members are strict:

- Every mapped `required` member must be present in the compound, otherwise deserialization throws
  `NbtException`.
- A required mapped member whose value is null cannot be serialized, regardless of
  `NullValueHandling`.
- A required member excluded from the mapping requires `[SetsRequiredMembers]` on the selected
  constructor; otherwise the generator reports `NBTSG015`.
Required members on a value type are supported only when the selected constructor is marked
`[SetsRequiredMembers]`. Because the generator prefers a parameterless constructor, an explicit
public parameterless constructor with the attribute is the usable form; otherwise the generator
reports `NBTSG007`. A parameterized `[SetsRequiredMembers]` constructor is not selected while an
implicit parameterless constructor exists.

`init`-only setters and non-public setters that are visible to the context assembly are written
through `UnsafeAccessor`. Required members are assigned directly when they have a public setter or
passed to the selected constructor, so they also work without reflection.

## Custom converters for members

Types the generator does not model can opt out per member:

```csharp
internal sealed class Player
{
    [NbtUseConverter]
    public Guid Id { get; init; }

    [NbtUseConverter(typeof(DecimalConverter))]
    public decimal Balance { get; init; }
}
```

- No argument: the member is resolved at runtime from `options.Converters`, then the resolver, then
  built-ins. This path is not AOT-safe by itself; register an AOT-safe converter.
- Converter type: generated code instantiates that converter directly. It takes precedence over
  `options.Converters`. The type must be closed, non-abstract, accessible, derived from
  `NbtConverter<T>` for the member type (or `NbtConverterFactory`), and have a public parameterless
  constructor.
- A converter for `Guid` does not automatically apply to `Guid?`; use runtime resolution or a
  nullable-aware converter.
- Marked members remain normal DTO members, so required, null, and constructor semantics are
  unchanged. Fields also require `[NbtInclude]`.

Invalid declarations report `NBTSG016`.

## Context options

```csharp
[NbtSerializable(typeof(Player))]
[NbtSourceGenerationOptions(
    NamingPolicy = NbtNamingPolicyKind.SnakeCaseLower,
    NullValueHandling = NbtNullValueHandling.Throw)]
internal partial class GameContext : NbtSerializerContext;
```

`NamingPolicy` renames mapped members without `[NbtPropertyName]` while generating converters. The
generated context overrides `CreateDefaultOptions`, so `GameContext.Default.Options` exposes the same
`PropertyNamingPolicy` and `NullValueHandling`. Passing explicit options to a context constructor
keeps those options' settings for nested resolution, while generated member names stay baked in.

`CreateDefaultOptions` is reserved when the attribute is present. A context that declares its own
override conflicts (`NBTSG008`); an inherited override is replaced unless it is sealed.

## Native AOT checklist

1. Put every serialized type behind `[NbtSerializable]` roots so the generated graph covers it.
2. Use the type-information overloads rather than the reflection overloads.
3. Use `[NbtUseConverter(typeof(...))]` with an AOT-safe converter for unsupported member types.
4. Avoid `NbtReflectionTypeInfoResolver` in AOT-published code, including as a resolver-chain
   fallback.
5. Build with `IsAotCompatible` or publish with trimming/AOT enabled and treat IL2026/IL3050 as
   errors. The repository's `NbtSharp.Aot.Tests` project demonstrates this setup.

Generated converters suppress AOT/trim diagnostics only when every member is covered by the
generated graph or an explicit typed converter. Runtime and factory-based `[NbtUseConverter]`
members stay analyzer-visible by design.

## Incremental generation

Each attributed context is prepared inside the incremental transform and emits a symbol-free spec.
Generated source is cached per prepared context, a content-keyed reachability fingerprint skips
graph construction for unchanged contexts, and no symbol-carrying value is retained by the pipeline.
Editing an unrelated declaration does not regenerate the context; editing a reachable declaration
does. Prepared contexts that carry diagnostics, including `NBTSG013` warnings, are not cached, so
those are prepared again on each generation pass. The cache is process-wide, bounded, and
FIFO-evicted.

See [diagnostics.md](diagnostics.md) for the complete `NBTSG` list and
[migration.md](migration.md) for moving an existing reflection-based setup.

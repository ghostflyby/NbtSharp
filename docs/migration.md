# Migrating from reflection to source generation

The reflection path and the generated path share the same mapping rules and wire format for the
intersection of shapes they both support. Migration can be incremental. `[NbtUseConverter]` works on
both paths: runtime mode resolves through `options.Converters`, and the explicit form uses the given
converter (or factory) for that member.

## Before

```csharp
var options = new NbtSerializerOptions
{
    TypeInfoResolver = new NbtReflectionTypeInfoResolver(),
};

byte[] data = NbtSerializer.Serialize(player, "Player", options);
Player copy = NbtSerializer.Deserialize<Player>(data, options);
```

## After

```csharp
[NbtSerializable(typeof(Player))]
internal partial class GameContext : NbtSerializerContext;

var context = GameContext.Default;
byte[] data = NbtSerializer.Serialize(player, context.Player, "Player");
Player copy = NbtSerializer.Deserialize(data, context.Player);
```

The context is the resolver. `Context.Default.Options` binds it as `TypeInfoResolver`.

## Incremental migration with a resolver chain

Keep the reflection resolver as a fallback for types that are not generated yet:

```csharp
var chain = new NbtTypeInfoResolverChain(
    GameContext.Default,
    new NbtReflectionTypeInfoResolver());
var options = GameContext.Default.Options.WithTypeInfoResolver(chain);
```

Start from `Context.Default.Options` so the configured `PropertyNamingPolicy` and
`NullValueHandling` also apply to reflection fallbacks. A separately constructed
`new NbtSerializerOptions` does not inherit `[NbtSourceGenerationOptions]` settings.

Resolution order is first non-null result. `options.Converters` still outranks the resolver chain.
A generated context wins for its own types; unknown types fall back to reflection. Nested members
resolve through the same options. A reflection root can contain a generated member; a generated root
contains a reflection-mapped member only when that member is opted out with runtime
`[NbtUseConverter]` and its converter is registered in `options.Converters`.

Chains inherit the trimming/AOT requirements of their resolvers: a chain that includes
`NbtReflectionTypeInfoResolver` is not AOT-safe. The type-information overloads use the options bound
to the matched type information, which for a generated context is the context's own `Options`, so
prefer the options-based overloads while migrating.

## Keep the same names

Reflection applies `[NbtPropertyName]` and, when configured,
`NbtSerializerOptions.PropertyNamingPolicy`. Generated contexts bake names at compile time, so:

- Set `NbtSourceGenerationOptions.NamingPolicy` instead of `PropertyNamingPolicy` for generated
  types. The generated default options expose the same policy for reflection fallbacks.
- Keep `[NbtPropertyName]` on both paths for explicit names.
- Applying `PropertyNamingPolicy` directly to options does not rename already-generated members.

## Expected differences

- Reflection now honors `[NbtUseConverter]`, matching the generated path for declarations the
  generator accepts. Reflection is more permissive about converter type accessibility because it
  instantiates the converter directly. Reflection-only DTOs that carry the attribute can therefore
  change their wire output (an explicit converter takes precedence over `options.Converters`) or
  start throwing for declarations the generator would reject, such as a field with
  `[NbtUseConverter]` but no `[NbtInclude]`. Previously the attribute was ignored on the reflection
  path. Runtime-mode `[NbtUseConverter]` still leaves the member inside reflection's object mapping;
  only generated contexts remove it from the graph.
- For a closed generic DTO, reflection validates an explicit converter against the substituted
  member type. Generation validates the open member type, so a converter declared on a generic type
  parameter may need restructuring before that model can move to source generation.
- Unsupported types become build diagnostics (`NBTSG003`, `NBTSG011`, …) instead of runtime
  `NotSupportedException`. Apply the suggested fix before deleting the reflection fallback.
- Private setters in another assembly are not visible to the generator. The member is still
  serialized through its getter, but generated deserialization silently leaves it at its default and
  reports no diagnostic. Expose it as `public`/`init`, make it `internal` and add
  `InternalsVisibleTo`, or move the context into the owning assembly. Reflection can write a private
  setter, so this difference can corrupt data if the reflection fallback is removed too early.
- Hidden (`new`) members are rejected by both paths; rename or exclude one.
- Required members follow the same presence and null rules. A constructor that initializes a mapped
  required member still needs the member present in the compound; `[SetsRequiredMembers]` only
  excuses required members that are excluded from the mapping.
- Polymorphism requires opt-in `[NbtDerived]` registrations on the base type. Both paths use the
  same `{ "$type": ..., "$value": ... }` envelope. Generated dispatch ignores `options.Converters`
  for individual derived types; register a converter for the base type to override it. Reflection
  dispatch resolves derived types through the options.
- Compound member order is not a contract. Generated and reflection converters may emit fields and
  properties in different orders, and NBT compounds are unordered. Compare behavior through
  round-trips rather than raw byte equality when a type mixes fields and properties or uses
  inheritance.

## Remove the fallback

When all reachable types compile without `NBTSG` errors:

1. Remove `NbtReflectionTypeInfoResolver` from the resolver chain.
2. Switch generic reflection overloads to type-information overloads.
3. Enable `IsAotCompatible`/trim analysis and fix remaining IL2026/IL3050 warnings.
4. Delete the reflection imports and options.

See [source-generation.md](source-generation.md) for supported shapes and the AOT checklist, and
[diagnostics.md](diagnostics.md) for every diagnostic.

# NbtSharp Roadmap

Status: 2026-11-20  
Baseline: PR #16 merged (`e8fb22a`) with source-generated serialization, enum support, strict
`required` semantics, a lock-free converter cache, typed conversion fast paths, fully incremental
generation, member converter opt-out, and broad collection/map shape support.

This document lists possible follow-up work. Priority reflects user impact and risk, not a strict
schedule.

## Implementation status (2026-11-20)

- P0 #1 and #2 are implemented.
- P0 #3 is implemented as compile-time AOT/trim analysis in the test projects. Native publish and
  package-content checks were deliberately excluded from scope.
- P1 #4, #5, #6, #7, and #8 are implemented.
- P2 #9, #10, #11, and #13 are implemented. Resolver chains, naming policies/context options, and
  async serialization are covered by stacked PRs.
- P3 #14 and #16 are implemented. #15 has benchmarks but no CI baseline comparison; #12 is deferred
  pending a wire-format decision.
- P2 #11 includes sequence/map shape interfaces, immutable/frozen/concurrent/sorted shapes, and
  `CollectionBuilderAttribute`/companion factory/constructor/`Add` pattern discovery. Sorted maps
  with string keys and sorted sets with string elements use ordinal ordering;
  `PriorityQueue<TElement,TPriority>` is rejected with a diagnostic. Span-only builders are
  supported by source generation and by the reflection resolver through runtime-created delegates.
- Built-in scalar support also covers `byte`, `ushort`, `uint`, `ulong`, and `char`.

## P0: highest impact

### 1. Full transform-level incremental generation

**Status (2026-11-20): implemented.** The pipeline uses a symbol-free per-context structural model,
a content-keyed reachability fingerprint, and a `ContextSpecCache`; unrelated edits leave prepared
and generated steps cached, and tests cover 300 contexts plus a single 120-DTO context.

**Original plan**

- A `PreparedContext` model with a structural fingerprint caches validation, type-graph data, name
  assignment, and diagnostics.
- Unrelated compilation changes skip source emission and `AddSource`.
- Tests assert that prepared and generated steps are cached for unrelated changes.

**Original gap (addressed)**

- Semantic model changes still rebuild the type graph for every context.
- `Collect()` still merges all contexts into one batch, so one changed context can trigger
  preparation of unrelated contexts.

**Scope**

- Replace symbol-carrying pipeline models with an equatable, symbol-free structural model.
- Split the collected batch into independent per-context incremental nodes.
- Cache graph construction itself when the structural fingerprint is unchanged.
- Add generation benchmarks with large contexts (hundreds of DTOs).

**Acceptance**

- Editing an unrelated file leaves both `NbtPreparedContexts` and `NbtGeneratedContexts` cached.
- Graph construction and source emission are both skipped for unchanged contexts.
- Large-context benchmark shows no full regeneration after an unrelated edit.

### 2. Custom converters in source-generated DTOs

**Status (2026-11-19): implemented.** `[NbtUseConverter]` supports runtime resolution and
`[NbtUseConverter(typeof(...))]` supplies an explicit typed converter or factory. Marked members are
excluded from graph discovery, follow normal converter precedence, and remain AOT-safe when the
explicit converter is AOT-safe.

**Original gap**

- Runtime converter lookup is `options.Converters` → type-info resolver → built-in converters.
- A DTO member with a type unsupported by the generator (`Guid`, `decimal`, `DateTime`, custom
  structs, etc.) produces `NBTSG003` even if a custom converter is registered at runtime.

**Why it matters**

- This is the main migration blocker for real-world DTOs that already rely on custom converters.

**Scope**

- Add a member-level opt-out such as `[NbtUseConverter]` or
  `[NbtConverter(typeof(MyConverter))]`.
- Or allow a context/type-level declaration that a member is resolved at runtime.
- Ensure the generated code still honors `options.Converters` precedence.
- Improve the diagnostic to explain the available escape hatches.

**Acceptance**

- A DTO containing `Guid`/`decimal` members compiles with source generation when the member is
  explicitly marked.
- The marked member uses the runtime converter and remains AOT-safe when the converter is
  AOT-safe.

### 3. AOT and package verification in CI

**Current state**

- Native AOT and `dotnet pack` are verified manually.
- The analyzer ships in `NbtSharp` under `analyzers/dotnet/cs`.

**Scope**

- Add a Native AOT consumer project to the repository and CI: publish and run it.
- Cover plain DTOs, `init`, `required`, enums, generic DTOs, and collections.
- Add a CI `dotnet pack` step that unzips the package and asserts the analyzer DLL is present.
- Optionally add a trimming/AOT analyzer-enabled consumer test.

**Acceptance**

- AOT or packaging regressions fail the PR checks automatically.

## P1: performance and semantics

### 4. Array and collection allocation improvements

**Status (2026-11-19): implemented.** Arrays with fixed-width element tags are allocated once at the
declared length, `sbyte[]` is read directly, and lists up to 4096 elements use
`CollectionsMarshal` span filling.

**Original state**

- Non-special `T[]` values are read into a `List<T>` and then copied with `ToArray()`.
- `sbyte[]` is read as `byte[]` and copied again.
- `List<T>` is preallocated for counts up to 4096; larger lists grow from zero.
- `Dictionary<string,TValue>` is not preallocated because compounds do not carry a member count.

**Scope**

- Read `T[]` directly using the declared list count.
- Read `sbyte[]` directly without a `byte[]` intermediate.
- Evaluate span-based list filling (`CollectionsMarshal.SetCount` / `CollectionsMarshal.AsSpan`).
- Consider making the list preallocation threshold configurable.

**Acceptance**

- `T[]` deserialization performs one array allocation.
- Benchmarks show reduced allocation for array-heavy DTOs.

### 5. Generated member converter caching

**Status (2026-11-20): implemented.** Generated converters emit static typed converter fields for
generated member types and use them when `options.Converters` is empty and the resolver is the
context itself; otherwise they fall back to per-options resolution so custom converters and
alternative resolvers keep precedence. The mixed-DTO benchmark covers the path.

**Acceptance**

- Covered by mixed-DTO benchmarks and custom-converter precedence tests.

### 6. Complete the scalar fast path

**Status (2026-11-19): implemented.** Generated converters directly read/write `sbyte`, `byte`,
`short`, `ushort`, `int`, `uint`, `long`, `ulong`, `char`, `float`, `double`, `bool`, `string`, and
their nullable variants when no custom converters are registered. Required getters are read once for
null checks; unsigned values and `char` preserve their bit patterns in the same-width signed tags.

### 7. Required and constructor semantics

**Status (2026-11-18): implemented.** Required value types need an explicit public parameterless
constructor marked `[SetsRequiredMembers]` (`NBTSG007` otherwise). `[SetsRequiredMembers]` only
excuses required members that are excluded from the mapping; mapped required members are always
presence-checked. Missing constructor-bound members use
the parameter's declared default value when it has one, otherwise the CLR default. Hidden (`new`)
members are rejected with `NBTSG017` and `NotSupportedException` from the reflection resolver.
Cross-assembly `init`/required members and friend-assembly (`internal`) setters are covered by
parity tests; private setters in another assembly are documented as not visible to source generation.

**Acceptance**

- Covered by generated/reflection parity tests and documented in the README.

### 8. Converter cache cold-start cost

**Status (2026-11-20): implemented.** The resolution cache is an `ImmutableDictionary` updated
under the existing lock, so resolving `n` distinct types costs O(n log n) instead of O(n²).
Converters are still created once per type/options instance, and cache reads remain lock-free.

**Acceptance**

- Concurrent-read correctness tests cover distinct-type resolution.

## P2: API and feature expansion

### 9. Resolver chain and migration support

**Status (2026-11-20): implemented.** `NbtTypeInfoResolverChain` consults resolvers in order and
uses the first non-null result. A generated context can be combined with
`NbtReflectionTypeInfoResolver` for migration; overlapping resolvers are resolved by order and the
precedence is documented in the README.

### 10. Naming policy and generation options

**Status (2026-11-20): naming and null handling implemented.** `NbtNamingPolicy` provides
camelCase, snake_case, and kebab-case presets for reflection through
`NbtSerializerOptions.PropertyNamingPolicy`. `[NbtSourceGenerationOptions]` configures the naming
policy and default null handling for a generated context; the emitted `CreateDefaultOptions`
override exposes both through `Context.Default.Options`, and declaring a conflicting override reports
`NBTSG008`. Generation mode selection (`Metadata` versus `Serialization`) remains undecided because
the generator only emits serialization-oriented converters today; it is deferred until a concrete
consumer requires metadata-only output.

### 11. Additional collection shapes

**Status (2026-11-18): implemented.** Automatic sequence/map support now covers the collection
interfaces, mutable set/queue/stack/linked-list shapes, immutable/frozen/concurrent/sorted shapes,
and custom types discovered through `CollectionBuilderAttribute`, companion factories, or
constructor/`Add` patterns. Unsupported shapes such as `PriorityQueue<TElement,TPriority>` are
reported explicitly instead of being mapped as objects.

### 12. Polymorphic members

**Status (2026-11-20): deferred pending a wire-format decision.** Abstract and interface members
still report `NBTSG003` / `NotSupportedException`. The implementation shape is understood:
`[NbtDerived(typeof(X))]` registration on the base type, a discriminator member, and generated plus
reflection dispatch. The unresolved choice is the NBT representation:

- Flattened: `{ "$type": "Cat", ...derived members }` — compact and closest to `System.Text.Json`,
  but reading needs buffering or DOM materialization and the writer must emit into an already-open
  compound.
- Envelope: `{ "$type": "Cat", "$value": { ...derived members } }` — isolated and simpler to
  stream, but more verbose and unlike `System.Text.Json`.

The discriminator value (short name, namespace-qualified name, or an explicit per-derived name) also
needs to be fixed before the format becomes a compatibility contract.

### 13. Asynchronous serialization

**Status (2026-11-20): implemented.** `SerializeAsync` is available for `Stream` and `PipeWriter`,
with and without explicit type information, and `SerializeDocumentAsync` covers `NbtRoot` values
while preserving `NbtRoot.Name`. It reuses the synchronous writer core and observes the cancellation
token on the asynchronous flush; caller-owned `PipeWriter` instances are flushed but not completed,
and stream overloads leave the stream open.

## P3: quality and documentation

### 14. Test matrix expansion

**Status (2026-11-20): implemented.** Coverage now includes flags and undefined enum values (members,
roots, lists, and maps), nested CLR collections, inherited mapping attributes and base fields, the
first-match converter ordering and explicit-member-versus-built-in cells of the precedence matrix,
deterministic random/mutation/truncation corpora for the reader, and a single generated context with
120 linked DTOs. Moving the generator timing evidence into CI remains part of #15.

### 15. Benchmarks and regression gates

- A BenchmarkDotNet project covers scalar, nullable-scalar, array, collection-shape, enum, and mixed
  DTOs, plus cold/warm generator incrementality at 100 and 300 contexts.
- A before/after comparison gate in CI or a scheduled job is still missing. Threshold-based gates
  need a stable runner and a chosen noise margin.

### 16. Documentation

**Status (2026-11-20): implemented.** Documentation now includes a source-generation and Native AOT
guide, a reflection-to-source-generation migration guide, required/null/constructor semantics, a
complete `NBTSG` diagnostic reference, and performance/allocation notes under `docs/`.

## Suggested sequencing

P0, P1 and P2 #9/#11/#13 are complete. For the remaining work:

1. P2 #10/#12 and the P3 items when their design prerequisites are settled.

## Deliberate non-goals for now

- A runtime schema/versioning system.
- Binary-compatible support for every CLR collection interface.
- Reflection-based AOT support; source generation remains the AOT path.

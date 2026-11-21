# NbtSharp Roadmap

Status: 2026-11-18  
Baseline: PR #16 merged (`e8fb22a`) with source-generated serialization, enum support, strict
`required` semantics, a lock-free converter cache, typed conversion fast paths, fully incremental
generation, member converter opt-out, and broad collection/map shape support.

This document lists possible follow-up work. Priority reflects user impact and risk, not a strict
schedule.

## Implementation status (2026-11-18)

- P0 #1 and #2 are implemented.
- P0 #3 is implemented as compile-time AOT/trim analysis in the test projects. Native publish and
  package-content checks were deliberately excluded from scope.
- P1 #4 and #6 are implemented.
- P2 #11 is implemented (PR #16 and the collection wrap-up): sequence/map shape interfaces,
  immutable/frozen/concurrent/sorted shapes, and `CollectionBuilderAttribute`/companion
  factory/constructor/`Add` pattern discovery. String sorted sets use ordinal ordering;
  `PriorityQueue<TElement,TPriority>` is rejected with a diagnostic. Span-only builders are
  supported by source generation and by the reflection resolver through runtime-created delegates.
- Built-in scalar support also covers `byte`, `ushort`, `uint`, `ulong`, and `char`.

## P0: highest impact

### 1. Full transform-level incremental generation

**Current state**

- A `PreparedContext` model with a structural fingerprint caches validation, type-graph data, name
  assignment, and diagnostics.
- Unrelated compilation changes skip source emission and `AddSource`.
- Tests assert that prepared and generated steps are cached for unrelated changes.

**Gap**

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

**Current state**

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

**Current state**

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

**Status (2026-11-18): implemented.** Required value types remain a permanent limitation
(`NBTSG007`). `[SetsRequiredMembers]` only excuses required members that are excluded from the
mapping; mapped required members are always presence-checked. Missing constructor-bound members use
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
uses the first non-null result. It exposes a read-only resolver view, snapshots the constructor
array, and rejects type information returned for a different type. A generated context can be
combined with `NbtReflectionTypeInfoResolver` for migration; overlapping resolvers are resolved by
order and the precedence is documented in the README. Additional overlap diagnostics remain out of
scope.

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

- Current behavior rejects abstract/interface members.
- Possible design: a discriminator member plus derived-type registration such as
  `[NbtDerived(typeof(X))]`.
- This requires a deliberate NBT representation choice because NBT has no native polymorphism.

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

- Add a BenchmarkDotNet project to the repository.
- Keep stable benchmarks for mixed scalar/string DTOs, numeric DTOs, list-heavy DTOs, enums, and
  large contexts.
- Run comparisons against a baseline in CI or a scheduled job.

### 16. Documentation

- Source-generation and Native AOT guide.
- Reflection-to-source-generation migration guide.
- Required/null/constructor semantics and `[SetsRequiredMembers]` behavior.
- Complete `NBTSG` diagnostic reference.
- Performance characteristics and allocation notes.

## Suggested sequencing

P0, P1 and P2 #9/#11/#13 are complete. For the remaining work:

1. P2 #10/#12 and the P3 items when their design prerequisites are settled.

## Deliberate non-goals for now

- A runtime schema/versioning system.
- Binary-compatible support for every CLR collection interface.
- Reflection-based AOT support; source generation remains the AOT path.

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

**Current state**

- Each generated member read/write resolves the converter through `NbtSerializerOptions` and then
  dispatches through the typed path.
- The cache read is lock-free, but it is still a dictionary lookup per member.

**Scope**

- Let generated type information hold nested typed converters.
- Cache member converters for the duration of an operation.
- Add a static fast path when `options.Converters` is empty.
- Preserve custom-converter precedence when the list is non-empty.

**Acceptance**

- Generated mixed DTO benchmark improves without changing custom-converter behavior.

### 6. Complete the scalar fast path

**Current state**

- Generated converters directly read/write `sbyte`, `byte`, `short`, `int`, `long`, `float`, and
  `double` when no custom converters are registered.
- `bool` and `string` still use the converter path.

**Scope**

- Add a direct `bool` path with the existing 0/1 validation.
- Add a direct `string` path that preserves `NullValueHandling` and error paths.
- Add a typed `Nullable<T>` path that avoids boxing.
- Avoid reading required properties twice when performing null checks.

**Acceptance**

- Scalar-heavy benchmarks improve; null and error-path tests remain unchanged.

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

**Current state**

- The cache uses copy-on-write dictionaries. Resolving `n` distinct types copies the dictionary
  `n` times, for O(n²) total copying.

**Scope**

- Evaluate `ImmutableDictionary`, segmented locking, or a batch-prewarm path.
- Preserve the current guarantee that a converter is created once per type/options instance.

**Acceptance**

- Large-context first-use benchmark shows acceptable scaling.
- Concurrent-read correctness tests continue to pass.

## P2: API and feature expansion

### 9. Resolver chain and migration support

- Add an `NbtTypeInfoResolverChain` or an equivalent fallback mechanism.
- Allow generated contexts to be combined with the reflection resolver during migration.
- Define precedence and diagnostics for overlapping resolvers.

### 10. Naming policy and generation options

- Add an STJ-style `NbtNamingPolicy` (camelCase, snake_case, etc.).
- Add a context-level source-generation options attribute for naming, null handling, and generation
  mode.
- Consider `GenerationMode.Metadata` versus `GenerationMode.Serialization`.

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

- Add `SerializeAsync` overloads for `Stream`/`PipeWriter` with explicit type information.
- Reuse the existing writer core and keep cancellation behavior consistent with
  `DeserializeAsync`.

## P3: quality and documentation

### 14. Test matrix expansion

- Enum flags, undefined values, nested collections, and enum dictionaries.
- Override attribute inheritance and inherited fields.
- Custom-converter precedence matrix.
- Malformed-input and fuzz tests for the reader.
- Generator performance tests with large contexts.

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

P0 and P1 #4/#6/#7 are complete. For the remaining work:

1. P1 #8 and #5: converter-cache cold start, then generated member converter caching.
2. P2 #13 and #9: `SerializeAsync`, then resolver-chain migration support.
3. P2 #10/#12 and the P3 items when their design prerequisites are settled.

## Deliberate non-goals for now

- A runtime schema/versioning system.
- Binary-compatible support for every CLR collection interface.
- Reflection-based AOT support; source generation remains the AOT path.

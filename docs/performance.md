# Performance characteristics

NbtSharp separates the DOM, the converter layer, and generated converters. The notes below describe
the intended hot paths and the trade-offs that can disable them.

## Converter resolution

- `NbtSerializerOptions` caches resolved converters in an `ImmutableDictionary`. Reads are lock-free;
  a miss takes a lock, resolves once, and publishes a new dictionary. Resolving `n` distinct types
  costs O(n log n) instead of repeated dictionary copies.
- Converters are created once per options instance and type. Reuse a single options instance (or a
  context's `Default.Options`) across operations instead of constructing options per call.
- `options.Converters` is consulted before the resolver and built-ins. Mutating the converter list
  invalidates the resolution cache; do that during setup, not on a hot path.
- `NbtReflectionTypeInfoResolver` caches reflection converters per type and naming-policy instance.
  Custom naming policies should be immutable; converters snapshot member names on creation.

## Generated converters

- When `options.Converters` is empty, generated object converters read and write numeric, boolean,
  string, and nullable scalar members directly, without per-member converter lookup.
- Generated member types get static typed `Lazy<NbtConverter<T>>` fields. They are used only when the
  converter list is empty and the exact context is the resolver; otherwise resolution falls back to
  the options so custom converters and alternative resolvers keep precedence.
- Registering a custom converter disables the direct scalar path for that operation. Prefer
  member-level explicit converters when only one member needs special handling.
- Required reference and nullable members are captured once before their null check, so the getter is
  not invoked twice.
- The `byte[]` overloads write through an `ArrayBufferWriter<byte>` and return a right-sized copy.
  Use the `IBufferWriter<byte>` overload to avoid the final copy. Stream serialization flushes once
  at the end and leaves the stream open.

## Collections and arrays

- Arrays with fixed-width element tags are allocated once at the declared list length instead of
  being built through `List<T>`.
- Variable-size element arrays and stream-backed reads grow from a bounded capacity, so malformed or
  partially buffered input cannot force a large upfront allocation.
- `sbyte[]` is read directly without an intermediate `byte[]`.
- Lists at or below 4096 elements are filled through `CollectionsMarshal` span access; larger
  declared lengths grow as elements are read.
- `Dictionary<string,TValue>` cannot be preallocated because compounds carry no member count.
- Empty collections encode as `TAG_List<TAG_End>`; a non-empty declared element type is still
  checked for built-in converters.

## Async paths

`SerializeAsync` and `DeserializeAsync` reuse the synchronous writer/reader core and perform I/O
asynchronously. Cancellation requested before the first write is observed before producing bytes;
cancellation during stream serialization is checked on the final flush. `PipeReader` and
`ReadOnlySequence<byte>` overloads leave bytes after the root available to the caller; stream
overloads may read ahead.

## Source generator

- Each attributed context is prepared independently, converted to a symbol-free spec, and emitted
  once per prepared context.
- A content-keyed reachability fingerprint skips graph construction for unchanged contexts, and the
  prepared/generated pipeline steps are cached for unrelated edits.
- The spec cache is process-wide, bounded, and FIFO-evicted. It skips graph/spec construction; the
  fingerprint walk still runs when a transform is invalidated.
- Large-context behavior is covered by a 120-DTO single-context test and a 300-context incremental
  rebuild test.

## Benchmarks

The repository includes a BenchmarkDotNet project:

```bash
dotnet run --project NbtSharp.SourceGeneration.Benchmarks/NbtSharp.SourceGeneration.Benchmarks.csproj \
  -c Release -- --filter '*SerializationBenchmarks*'
dotnet run --project NbtSharp.SourceGeneration.Benchmarks/NbtSharp.SourceGeneration.Benchmarks.csproj \
  -c Release -- --filter '*GeneratorIncrementalityBenchmarks*'
```

`SerializationBenchmarks` covers scalar-heavy and nullable-scalar DTOs, arrays and allocation-heavy
arrays, collection shapes, enum-heavy DTOs (including flags and undefined values), and a mixed DTO. `GeneratorIncrementalityBenchmarks` covers cold
generation and unrelated-edit warm runs for 100 and 300 contexts. Benchmark results are currently
informational; CI does not enforce a regression threshold.

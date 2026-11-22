# Performance characteristics

NbtSharp separates the DOM, the converter layer, and generated converters. The notes below describe
the intended hot paths and the trade-offs that can disable them.

## Converter resolution

- `NbtSerializerOptions` caches resolved converters in an `ImmutableDictionary`. Reads are lock-free;
  a miss takes a lock, resolves once, and publishes a new dictionary. Resolving `n` distinct types
  costs O(n log n) instead of repeated dictionary copies.
- Converters resolved through `NbtSerializerOptions.GetConverter` are created once per options
  instance and type. The options-less overloads share the cached `NbtSerializerOptions.Default`
  singleton, so each type is resolved once per process; pass one options instance (or a context's
  `Default.Options`) to reuse a custom configuration. Factory-based `[NbtUseConverter(typeof(...))]`
  members call `CreateConverter` for every read or write instead of caching the produced converter.
- `options.Converters` is consulted before the resolver and built-ins. Mutating the converter list
  invalidates the resolution cache; do that during setup, not on a hot path. The shared `Default`
  instance freezes on its first use, and converter-list mutations on it throw
  `InvalidOperationException` afterwards; register converters before the first options-less call,
  or use a dedicated options instance for per-call configuration.
- `NbtReflectionTypeInfoResolver` caches reflection converters per type and naming-policy instance.
  Custom naming policies should be immutable; converters snapshot member names on creation.

## Generated converters

- When `options.Converters` is empty, generated object converters read and write numeric, boolean,
  string, and nullable scalar members directly, without per-member converter lookup.
- Each converter class embeds its members' names as pre-encoded modified UTF-8 byte tables
  (`__name{i}`). The direct read path dispatches through `NbtReader.NameEquals` without decoding
  the wire name, and the direct write path uses the writer's byte-name overloads, so member names
  are never transcoded on the hot path. The slow (converter-registered) path keeps the decoded
  string switch.
- `NameEquals` compares by decoded name value: the byte comparison is a fast path for canonically
  encoded wire names, and non-canonical encodings (raw `0x00`, overlong two- or three-byte forms)
  fall back to decoding both names so they match their canonical equivalents.
- The reader decodes names lazily: `NbtReader.Name` materializes and caches the string on first
  access, so names matched through `NameEquals` or bypassed by `Skip` are never decoded. Deep name
  validation errors therefore surface on the first `Name` access (or the decoded `NameEquals`
  fallback) instead of during `Read`; lead bytes of 0xF0 or greater are still rejected while
  reading. `Skip` never validates the names it skips.
- The writer's byte-name overloads (`WriteInt32(ReadOnlySpan<byte>, ...)` and friends,
  `WriteMemberHeader(NbtTagType, ReadOnlySpan<byte>)`) write the name bytes verbatim without
  modified UTF-8 validation — they can emit names the reader will reject; validity is the caller's
  responsibility. A name longer than 65535 bytes throws before any byte is written.
- Generated member types get static typed `Lazy<NbtConverter<T>>` fields. They are used only when the
  converter list is empty and the exact context is the resolver; otherwise resolution falls back to
  the options so custom converters and alternative resolvers keep precedence.
- Registering a custom converter disables the direct scalar path for that operation. Prefer
  member-level explicit converters when only one member needs special handling.
- Required reference and nullable members are captured once before their null check, so the getter is
  not invoked twice.
- Polymorphic values add one envelope compound plus a discriminator string. Generated dispatch uses
  static typed converters and an exact runtime-type check; reflection resolves the concrete type
  through the options.
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
- Empty collections encode as `TAG_List<TAG_End>`; a declared element type other than `TAG_End` is
  still checked for built-in converters.

## Read-only DOM

`NbtDocument.Parse` indexes the entire document in one pass over contiguous bytes, so its cost is
a full structural validation plus one 28-byte slot per indexed value — up to roughly 6x the input
size in the worst case (one-byte list elements), far less for typical documents. Slot arrays and
copied sequence/stream buffers come from `ArrayPool` and are returned by `Dispose`; memory input is
not copied at all. After parsing, every element access is a lookup: counts are O(1), scalar
getters read the indexed bytes directly, and member lookup walks the compound's members. Names and
string payloads are decoded lazily per access, never during indexing (only their length prefixes
are validated), which is why probing a large document through `NameEquals` and `Skip`-style access
never pays for names it does not materialize.

## Async paths

`DeserializeAsync` reads asynchronously through a `PipeReader`. `SerializeAsync` writes through the
synchronous writer core and awaits the final flush. Cancellation requested before the first write is
observed before producing bytes; cancellation during stream serialization is checked on the final
flush. `PipeReader` and `ReadOnlySequence<byte>` overloads leave bytes after the root available to the
caller; stream overloads may read ahead.

A truncated asynchronous parse is not retried on every arriving chunk. The document is reparsed once
the writer completes, or once the buffered data has grown by at least the number of bytes the
truncation point reported it still needs, or — when that number is unknown — by a clamped fallback
between 4 KiB and 64 KiB. Each retry therefore advances past the point where the previous parse
starved, but the worst case is still quadratic in principle: a producer that feeds exactly one
deficit at a time forces a reparse per step, and an open, silent writer can hold a completed
document back by up to the clamped growth. Converters may still run more than once for the same
document; the throttle only lowers how often.

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

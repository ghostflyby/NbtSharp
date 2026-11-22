# System.Text.Json gap analysis

Status: 2026-11-22

This document inventories feature and performance gaps between NbtSharp and `System.Text.Json`
(STJ, as shipped across .NET 8–10). NbtSharp deliberately mirrors the STJ architecture
(`NbtSerializer`/`JsonSerializer`, `NbtReader`/`NbtWriter` vs `Utf8JsonReader`/`Utf8JsonWriter`,
`NbtConverter<T>`/`JsonConverter<T>`, `INbtTypeInfoResolver`/`IJsonTypeInfoResolver`,
`NbtSerializerContext`/`JsonSerializerContext`, `NbtReflectionTypeInfoResolver`/
`DefaultJsonTypeInfoResolver`, `[NbtDerived]`/`[JsonDerivedType]`), which makes STJ a useful
reference — but not every STJ capability has an NBT meaning. Each entry below is classified through
the NBT format itself:

- **Non-gap, format-intrinsic.** The STJ capability has no NBT counterpart because of the wire
  format. Recorded so it is not re-litigated.
- **Gap (NBT-adapted).** A real gap; the entry states the NBT constraints a design must respect.
- **Gap (straightforward).** No NBT-specific design questions beyond naming.
- **Deliberate non-goal.** Already decided in roadmap.md.

roadmap.md remains the work tracker. Priorities here are suggestions, subject to the same rule as
the roadmap: new work starts from a concrete consumer request.

## Format-intrinsic differences (non-gaps)

| STJ capability | Why NBT differs |
| --- | --- |
| Self-describing values; a converter writes whenever it is ready | NBT writes the tag type before the value in member, root, and list headers, so converters must predict it (`TryGetTagType`). The contract is format-driven, not an oversight. |
| `null` literals; `JsonIgnoreCondition.WhenWritingNull`/`Always`; null-aware reference handling | NBT has no null tag. Null compound members are omitted or rejected (`NbtNullValueHandling`); null roots and null list elements always throw. A richer null grammar has nothing to bind to. |
| Dictionary preallocation from JSON object member counts | Compounds carry no member count, so `Dictionary<string,T>` cannot be preallocated. |
| Unbounded JSON arrays | NBT list headers declare the element count, which NbtSharp already exploits: one-shot array allocation, `MaxCollectionLength` prevalidation, and `CollectionsMarshal` span filling. STJ cannot do any of this. |
| `WriteIndented`, `JavaScriptEncoder`, escaping policies | Binary format; there is no text rendering to control. |
| `LineNumber` on `JsonException` | Binary format; `NbtException` reports `BytePosition` and a `$.Path` instead (STJ gained `Path` only in .NET 8). |
| `JsonNumberHandling` string/number leniency | Tags are typed on the wire. The NBT analog — accepting narrower tags on read — is listed as G-TYPE-5. |
| Flat polymorphic payloads / .NET 10 `[JsonUnion]`-style layouts | The `$type`/`$value` envelope was chosen because the discriminator must be known before the payload; a flat layout would require lookahead buffering (roadmap #12). |

One row deserves emphasis because it defines shipped behavior rather than ruling a feature out:
the null-member default is a deliberate match for the Java ecosystem. The NBT wire format has no
null tag, so an absent key is the only null representation that exists. Vanilla Java code only ever
writes a member when the value exists (`if (x != null) tag.put("X", x)`), and Mojang's DFU
(`NbtOps`) skips entries whose value is `empty()` when building a compound. `NbtNullValueHandling.Omit`
(the default) reproduces this on write for DTO members and dictionary values alike; a missing key on
read keeps the member default, analogous to DFU's `optionalFieldOf`, with `required` playing the
strict `fieldOf` role. Null roots and null list elements throw because there is no key to omit and
no tag to write — the same outcome Java libraries produce. `NullValueHandling.Throw` is an
NbtSharp-only strictness option, off by default.

## Feature gaps

### Contract extensibility

**G-CONTRACT-1 — runtime contract modification.** Priority: P1.

STJ's main extensibility axis is its mutable contract model: `DefaultJsonTypeInfoResolver` modifier
callbacks, `IJsonTypeInfoResolver.WithAddedModifier`, and the `JsonTypeInfo`/`JsonPropertyInfo`
surface (rename members, replace member converters, change ignore conditions, reorder, replace
setters, introspect `Kind`). NbtSharp's `NbtTypeInfo` (NbtSharp/NbtTypeInfo.cs) is opaque —
`Type`, `Options`, `Converter` — and `NbtReflectionTypeInfoResolver` has no callback hook. Runtime
mapping adjustment currently requires subclassing a resolver or editing attributes on the DTO.

NBT constraints for a design: member names are wire identifiers in modified UTF-8, so a modifier
must operate on the final NBT name, after the naming policy is applied; a kind model would follow
NbtSharp's converter families (compound / list / map) rather than STJ's; and modified members must
preserve the duplicate-name and single-member-namespace invariants that mapping already enforces.

**Status (2026-11-22): implemented on feat/stj-gap-p1.** Scope: the reflection resolver only —
`NbtReflectionTypeInfoResolver` accepts `Action<NbtContractInfo>` modifier callbacks (constructor,
`WithAddedModifier` appending to an array snapshot with an independent cache), and `NbtContractInfo`
/`NbtMemberInfo` expose member-level rename (`NbtName`), `Ignore`, `Converter`, and `Order` over the
discovered contract, frozen after the callbacks run. Contracts are keyed per `(Type,
PropertyNamingPolicy)` with a per-key in-flight guard that turns modifier re-entrancy into
`InvalidOperationException`; nested resolution of sibling types is allowed. Generated contexts bake
contracts at compile time, so there is no interface-level hook. Setters/kind introspection and
converter-collection callbacks remain open.

### Member semantics

**G-MEMBER-1 — extension data.** Priority: P1.

No `JsonExtensionData` equivalent: unknown compound members are skipped and dropped on read
(NbtReflectionObjectConverter.cs:119). Round-tripping unknown members matters for forward
compatibility — files written by newer tools gain fields that older readers should preserve. The
natural NBT shape is a catch-all `NbtCompound`-typed member that receives unknown members verbatim
on read and is appended after mapped members on write.

Design constraints: the reserved polymorphic names (`$type`, `$value`) must stay outside the
catch-all; the interaction with `RejectDuplicateCompoundNames` (a duplicate across a mapped member
and the extension member) must be defined; and capturing values verbatim means either DOM
materialization or a raw-capture capability (see G-DOM-1) — with today's DOM the extension member
pays full materialization.

**Status (2026-11-22): implemented on feat/stj-gap-p1.** `[NbtExtensionData]` claims a single
`NbtCompound`- or `Dictionary<string, NbtValue>`-typed member per type; unknown members are
captured through the new built-in DOM helpers (`NbtMetadataServices.ReadDomValue`/`WriteDomMember`)
and appended after mapped members on write, with name conflicts (including names omitted by null
handling) rejected. Capturing is deliberately decoupled from `options.Converters`: the built-in DOM
converters now form a closed family (`JsonElement` semantics), so converters registered for
`NbtValue`/`NbtList`/`NbtCompound` still apply when such a type is the declared member or root type
but no longer intercept values nested inside built-in DOM conversion. Raw pass-through remains
open for G-DOM-1's `GetRawBytes` hook.

**G-MEMBER-2 — default-value elision on write.** Priority: P1–P2.

STJ: `JsonIgnoreCondition.WhenWritingDefault`. For NBT this is a file-size feature (Minecraft-style
data is member-sparse), not cosmetics. Constraints: defaults must be defined per tag type
(`(sbyte)0`, `0`, empty string, and so on); an empty list whose declared element type is not
`TAG_End` must not be elided, because omission would lose the element-type declaration; elision is
independent of `TryGetTagType` (a member either appears or it does not).

**G-MEMBER-3 — strict unknown-member mode.** Priority: P2.

STJ `JsonUnmappedMemberHandling.Disallow`; today unknown members are always skipped. A cheap
validation switch; interacts with G-MEMBER-1 (extension data first, disallow the remainder).

**G-MEMBER-4 — constructor pinning and parameter names.** Priority: P3.

No `[JsonConstructor]` equivalent; selection is the "most matching parameters" heuristic on both
paths, and there is no parameter-level rename attribute. Low urgency because the heuristic plus
duplicate-name checks covers most DTOs.

**G-MEMBER-5 — populate-into-existing collections.** Priority: P3.

STJ `JsonObjectCreationHandling.Populate`. For NBT, populating a pre-initialized
`Dictionary<string,T>` or list member is natural; duplicate-key and stack-order semantics must be
defined.

**G-MEMBER-6 — member order control.** Priority: P3.

Write order is declaration order; a `JsonPropertyOrder` equivalent would only affect write order
and does not interact with name matching.

**G-MEMBER-7 — case-insensitive member matching.** Priority: P3, candidate non-goal.

NBT names are exact wire identifiers. Lenient matching helps only migration reads and cannot
round-trip the original casing through a DTO member.

### Type system

**G-TYPE-1 — string enums.** Priority: P1.

No `JsonStringEnumConverter<T>` equivalent; enums always use integer tags. Minecraft-style data
itself favors string identifiers (`minecraft:stone`), so a `TAG_String`-backed enum converter with
cached name tables is the natural fit. Constraint: an undefined name on read cannot fall back to a
numeric value the way a numeric enum representation could — storage is a string — so the policy is
throw or an explicit `IsDefined`-style check.

**Status (2026-11-22): implemented on feat/stj-gap-p1.** `[NbtStringEnum]` routes an enum through
`NbtStringEnumConverter<TEnum>` with STJ-aligned semantics: cached ordinal name tables,
first-declared-name writes for aliased values, invariant numeric strings for undefined values,
comma-split flags parsing with per-segment trim, and case-sensitive reads. Both the reflection
resolver and the source generator honor the attribute; see the README's Converters section.

**G-TYPE-2 — built-in common scalar types.** Priority: P2, blocked on wire-form decisions.

`decimal`, `Guid`, the date/time family, `Half`, `Int128`/`UInt128`, `BigInteger`, and `nint`/
`nuint` have no built-in converters (rejected in both TypeGraph.cs and
NbtReflectionTypeInfoResolver.cs; the escape hatch is `[NbtUseConverter]`). STJ maps these to JSON
strings natively; NBT must choose a canonical tag for each — for example `Guid` as a 16-byte
`TAG_Byte_Array`, ticks as `TAG_Long`, `decimal` as a string or a scaled integer. There is no
cross-library NBT standard for any of them, so a wrong canonical choice bakes into files.
Recommended direction: ship documented, clearly-namespaced converters as opt-ins and keep the core
minimal.

**G-TYPE-3 — non-string dictionary keys.** Priority: P3.

NBT keys are strings; enum- or integer-keyed maps would be a convenience conversion on top (STJ
supports this through key converters).

**G-TYPE-4 — `object` members / `UnknownTypeHandling`.** Priority: non-goal candidate.

A typed binary format has no dynamic-typing escape hatch worth defining; an `NbtValue` member
already covers "anything".

**G-TYPE-5 — integer widening on read.** Priority: P2.

The NBT analog of `JsonNumberHandling` leniency: accept `TAG_Byte`/`TAG_Short` (and so on) for a
wider integer member on read. Today users hand-roll this (the README's `CompactInt32Converter`). A
per-member or global "accept narrower tags" read option must define the lossless ranges; the write
side and `TryGetTagType` are unaffected.

### DOM and references

**G-DOM-1 — pooled read-only DOM and raw pass-through.** Priority: P1 for performance-sensitive
consumers.

No `JsonDocument`/`JsonElement` equivalent: `NbtValue` is a boxed union and materializes every
scalar, and converters cannot capture a member's raw bytes to re-emit them (STJ's `JsonElement`
raw-value capture). NBT makes such a view tractable: nearly every payload is length-delimited (the
compound end tag is the exception), so a disposable, skip-based view over a
`ReadOnlySequence<byte>` is feasible on top of the existing `Skip` machinery. Unlocks cheap probing
of large files and decode-free re-emit in converters.

**Status (2026-11-22): implemented on feat/stj-gap-p1.** `NbtDocument`/`NbtElement` provide the
pooled read-only DOM: a single-pass indexer that validates the whole document at parse time, with
`Parse` over memory (zero-copy), sequences, and streams, lazy modified UTF-8 name decoding,
`NameEquals` byte fast paths, O(1) counts, and `ToNbtValue` materialization. The raw pass-through
half is still open for converters; the `GetRawBytes` hook is in place as an internal API, ready to
expose when converter capture lands.

**G-DOM-2 — reference preservation and cycle handling.** Priority: P3.

No `ReferenceHandler` equivalent; cyclic DTOs hit `MaxDepth` with a clear error. `$id`/`$ref`
envelopes would collide with the reserved `$type`/`$value` polymorphic names, and NBT use cases
(game data files) rarely carry object graphs. If anything, an IgnoreCycles-style elision is the
defensible subset.

**G-DOM-3 — schema export.** Priority: non-goal until a consumer asks.

There is no NBT schema standard to export to.

### Options lifecycle

**G-OPT-1 — cached default options.** Priority: P0.

Every options-less call constructs a fresh `NbtSerializerOptions` (NbtSerializer.cs:65 and the
other `??=` sites), so the convenience overloads rebuild the converter cache per call; users who
also construct a resolver per call pay full reflection member analysis every time. STJ caches
`JsonSerializerOptions.Default`. Constraint: options are init-only except `Converters`, so a shared
default must either reject converter registration after first use or hand out copies (STJ throws
when a read-only options instance is mutated).

**Status (2026-11-22): implemented on feat/stj-gap-p0.** Options-less overloads now share the
cached `NbtSerializerOptions.Default` singleton. The shared instance freezes on its first use and
rejects converter-list mutations afterwards, while instances created with the constructor stay
mutable between operations.

**G-OPT-2 — read-only enforcement.** Priority: P2.

Mutating an options instance mid-operation is documented as unsupported but not enforced; STJ
freezes options on first use and reports clear errors.

### Async

**G-ASYNC-1 — resumable async read.** Tracked under PERF-3.

**G-ASYNC-2 — `IAsyncEnumerable<T>`.** Priority: P3.

Asymmetric in NBT: deserialization can stream list elements after the declared header (the count is
known), but serialization must buffer the entire sequence before the header can be written. Low
demand; recorded for completeness.

### Source generation

**G-SG-1 — cross-assembly `internal` via `InternalsVisibleTo`.** Priority: P2.

The generator requires same-assembly accessibility for `internal` types (TypeGraph.cs:1439) while
the reflection path accepts friend-assembly members. Aligning them means teaching the accessibility
check about IVT grants.

**G-SG-2 — metadata-only generation mode.** Deliberate non-goal; see roadmap #10.

## Performance gaps

**PERF-1 — member names are transcoded in both directions, every time.** Priority: P0.

Read: `NbtReader.Read()` decodes every compound member name to a `string` immediately
(NbtReader.cs:214, NbtReader.cs:806) and the API exposes no bytes, so generated converters can only
switch on strings. Write: `WriteNamedHeader` re-encodes the name through
`GetByteCount`/`GetBytes` for every member (NbtWriter.cs:709). STJ's generated fast path compares
pre-encoded UTF-8 name bytes on read and writes pre-encoded literals.

NBT adaptation: names are modified UTF-8, not UTF-8 — C# `u8` literals are wrong for names
containing U+0000 or supplementary characters. The generator must encode name literals with the
modified encoder at generation time, and the runtime needs name-bytes exposure on the reader
(`NameSpan` or `NameEquals(ReadOnlySpan<byte>)`) plus a bytes-based
`WriteMemberHeader(tag, ReadOnlySpan<byte> name)` on the writer. This is the largest single
hot-path win.

**Status (2026-11-22): implemented on feat/stj-gap-p0.** The reader records name bytes without
decoding (`NbtReader.NameEquals(ReadOnlySpan<byte>)` compares against pre-encoded names, with a
decoded fallback that keeps non-canonical wire encodings matching their canonical equivalents;
`Name` is now lazy and cached per token). The writer gained byte-name overloads for the scalar
writes and `WriteMemberHeader(NbtTagType, ReadOnlySpan<byte>)`. Generated converters embed
per-member modified UTF-8 name byte tables, dispatch reads through `NameEquals`, and write through
the byte overloads on the direct path. Deferred name validation (`Name` throws on first access,
skipped names are never decoded) and the writer's verbatim name bytes are documented API behavior.

**PERF-2 — options-less calls rebuild converter caches.** See G-OPT-1; same fix.

**Status (2026-11-22): implemented** through the shared frozen `Default` singleton (see G-OPT-1).

**PERF-3 — `DeserializeAsync` restarts the whole parse on truncation.** Priority: P1.

`DeserializeAsyncCore` rebuilds an `NbtReader` over the accumulated buffer on every retry
(NbtSerializer.cs:537–579): worst-case O(n²) parsing for chunked input, and converters may run more
than once for the same bytes (documented in the API remarks). STJ resumes through a state machine.
A fully resumable converter protocol is a large redesign; a pragmatic middle ground is to re-parse
only when the buffer has grown meaningfully or the read has completed, instead of on every
`ReadAsync` chunk.

**Status (2026-11-22): partially mitigated on feat/stj-gap-p1.** Truncation sites now report how
many bytes they still need (`NbtException.TruncatedAdditionalBytes`, a lower bound), and the retry
loop reparses only once the writer completes, the reported deficit has arrived, or a 4–64 KiB
clamped fallback growth has accumulated. Each retry advances past the previous starvation point and
open-pipe documents complete as soon as their deficit arrives, but a fully resumable state machine
remains open.

**PERF-4 — caches never freeze.** Priority: P2.

The options converter cache is an `ImmutableDictionary` under a lock (NbtSerializerOptions.cs:17)
and the reflection resolver a plain `Dictionary` under a lock
(NbtReflectionTypeInfoResolver.cs:56). STJ switches to `FrozenDictionary` after first use; at least
the reflection resolver could freeze its per-policy snapshot once built.

**PERF-5 — generated collections have no inline loop, and the fast-path granularity is coarse.**
Priority: P2.

Only scalar members get direct read/write code, guarded by `options.Converters.Count == 0`
(ContextEmitter.cs:1064) — registering any single converter disables the entire scalar fast path —
and non-scalar members always pass through one converter indirection via `NbtMetadataServices`.
`GetTypeInfo` is a linear `if` chain (ContextEmitter.cs:567). STJ's fast path covers whole object
graphs and degrades per type rather than globally.

**PERF-6 — per-instance allocations in the reader and writer.** Priority: P3.

`List<ContainerState>` plus the `long[]` value-counter arrays are allocated per reader/writer
instance (`_containers ??= []`). STJ's reader tracks depth with an inline `BitStack`. Bounded, but
visible at high call rates on small documents.

**PERF-7 — strings are validated twice on decode.** Priority: P3.

`DecodeString` scans every byte for `>= 0xF0` and then `ModifiedUTF8Encoding.GetString` validates
again (NbtReader.cs:831–856). A single pass, or a `SearchValues`-vectorized prefilter, removes one
full scan per string.

**PERF-8 — `Serialize` to `byte[]` allocates a fresh `ArrayBufferWriter` per call**, plus the
final `ToArray` copy (NbtSerializer.cs:44). Pooled buffer writers (STJ's approach since .NET 8)
would reduce this to the final copy.

## Where NbtSharp already exceeds STJ

- Default `MaxCollectionLength` and `MaxDocumentBytes` limits; STJ has only `MaxDepth`.
- One-shot array allocation and bounded-capacity reads from declared list lengths.
- span/sequence/pipe-based reader and writer surface.
- Error `Path` from the start; STJ gained it in .NET 8.
- Compile-time naming policy baking (STJ naming is purely runtime), with generator/runtime parity
  covered by tests.
- Order-reversing round-trips for stack-like collections.

## Suggested sequencing

| Priority | Items |
| --- | --- |
| P0 | PERF-1 / G-OPT-1 — name-bytes API with pre-encoded names; cached default options |
| P1 | G-CONTRACT-1, G-MEMBER-1, G-TYPE-1, G-DOM-1, PERF-3 |
| P2 | G-MEMBER-2, G-MEMBER-3, G-TYPE-2, G-TYPE-5, G-OPT-2, G-SG-1, PERF-4, PERF-5 |
| P3 | G-MEMBER-4–7, G-TYPE-3, G-DOM-2, G-ASYNC-2, PERF-6–8 |

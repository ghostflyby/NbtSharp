# NbtSharp

An NBT (Named Binary Tag) library for .NET with a `System.Text.Json`-style serialization layer.

- `net11.0`; Native AOT analysis is enabled.
- Java Edition encoding: big-endian fixed-width numbers and Java modified UTF-8 strings.
- Union-based DOM: `NbtValue` is a C# 15 union, while `NbtList` is a hand-written `[Union]` struct.
- Converter-based serialization over `ReadOnlySpan<byte>`, `ReadOnlySequence<byte>`,
  `PipeReader`/`PipeWriter`, and `IBufferWriter<byte>`.

## Design

The DOM mirrors the NBT type system directly and is unchanged by the serialization layer:

```csharp
public union NbtValue(
    sbyte, short, int, long, float, double,
    sbyte[], string, NbtList, NbtCompound, int[], long[]
);

[Union]
public readonly struct NbtList
{
    public NbtList(List<int> value) { }    // one constructor per element type
    public NbtList(NbtEndList value) { }   // TAG_List<TAG_End> marker
    public object Value { get; }           // never null
    public NbtTagType ElementType { get; }
    public int Count { get; }
}

public sealed class NbtCompound : Dictionary<string, NbtValue>;
```

Scalars use the CLR type whose width matches the NBT type, so tag identity is preserved by the type
system. A default-initialized `NbtList` represents `TAG_List<TAG_End>`; an empty `List<int>` still
encodes as `TAG_List<TAG_Int>` with length zero.

The serialization layer is split like `System.Text.Json`:

- `NbtReader` and `NbtWriter` are `ref struct` token APIs. The reader accepts a span, a sequence,
  or a stream; the writer targets a buffer writer or a pipe writer.
- `NbtSerializer` provides generic `Serialize<T>` / `Deserialize<T>` entry points plus DOM
  convenience overloads.
- `NbtConverter<T>` converts one CLR type. A converter may choose a different NBT tag type for
  different values; `TryGetTagType` predicts that type before a named member, root, or non-empty
  list header is written.
- `NbtConverterFactory` creates converters for generic type families.
- `NbtTypeInfo<T>` and `INbtTypeInfoResolver` describe a CLR type. The built-in
  `NbtReflectionTypeInfoResolver` maps ordinary classes and structs to compounds.

## Usage

DOM serialization:

```csharp
var player = new NbtCompound
{
    ["Health"] = 20,
    ["Name"] = "Steve",
    ["Speed"] = 0.1f,
    ["Alive"] = (sbyte)1,
};

byte[] data = NbtSerializer.Serialize(new NbtRoot("Player", player));
NbtRoot root = NbtSerializer.Deserialize(data);

// DOM root with explicit options:
byte[] configured = NbtSerializer.SerializeDocument(
    new NbtRoot("Player", player),
    new NbtSerializerOptions());
```

Generic serialization uses the same facade:

```csharp
var options = new NbtSerializerOptions
{
    TypeInfoResolver = new NbtReflectionTypeInfoResolver(),
};

byte[] bytes = NbtSerializer.Serialize(playerDto, "Player", options);
PlayerDto copy = NbtSerializer.Deserialize<PlayerDto>(bytes, options);

int value = NbtSerializer.Deserialize<int>(data, out string? rootName);
```

Overloads without an `options` argument share the cached `NbtSerializerOptions.Default` singleton,
which freezes on first use; register converters on it before the first options-less call, or create
an `NbtSerializerOptions` instance for custom settings.

Reflection mapping uses public instance properties by default. `[NbtPropertyName("name")]` renames a
member, `[NbtIgnore]` excludes it, `[NbtUseConverter]` selects a runtime or explicit member converter,
and public fields are included only with `[NbtInclude]`. A
public parameterless constructor is preferred; otherwise a constructor whose parameter names match
the members is used. Unknown members are skipped; a missing constructor-bound member uses the
parameter's declared default value when it has one, falling back to the CLR default. Hidden (`new`)
members are rejected because NBT has one member namespace per compound. Mapping attributes declared
on a virtual base property are inherited by overrides, matching their `AttributeUsage.Inherited`
contract; public `[NbtInclude]` fields declared on a base type are included too.

Unknown members can be preserved with `[NbtExtensionData]` on a single
`NbtCompound`- or `Dictionary<string, NbtValue>`-typed member (both mapping paths): unknown members
are captured into it on read and appended after the mapped members on write, so files written by
newer tools round-trip fields an older reader does not map. The member needs a setter, cannot be
`required`, cannot be combined with `[NbtPropertyName]`/`[NbtUseConverter]`, does not participate
in constructor binding, and fields also need `[NbtInclude]`. Duplicate unknown names follow
`RejectDuplicateCompoundNames`; writing an extension key that collides with any mapped member name
(including members omitted by null handling) throws `NbtException`. Captured values always use the
built-in DOM converters, even when converters for `NbtValue`/`NbtList`/`NbtCompound` are registered
in `options.Converters`: such converters still apply when a DOM type is the declared member or root
type, but no longer intercept values nested inside built-in DOM conversion (the `JsonElement`
behavior).

Reflection mapping can apply a preset naming policy to members without `[NbtPropertyName]` by
setting `NbtSerializerOptions.PropertyNamingPolicy`, for example
`PropertyNamingPolicy = NbtNamingPolicy.SnakeCaseLower`. The presets are `CamelCase`,
`SnakeCaseLower`, `SnakeCaseUpper`, `KebabCaseLower`, and `KebabCaseUpper`; an explicit
`[NbtPropertyName]` always wins.

## Source generation

Source-generated type information avoids reflection and runtime generic construction:

```csharp
[NbtSerializable(typeof(Player))]
[NbtSerializable(typeof(Item))]
partial class GameContext : NbtSerializerContext;

var context = GameContext.Default;
byte[] data = NbtSerializer.Serialize(player, context.Player, "Player");
Player copy = NbtSerializer.Deserialize(data, context.Player);
```

A context can configure generation-time defaults:

```csharp
[NbtSerializable(typeof(Player))]
[NbtSourceGenerationOptions(
    NamingPolicy = NbtNamingPolicyKind.SnakeCaseLower,
    NullValueHandling = NbtNullValueHandling.Throw)]
partial class GameContext : NbtSerializerContext;
```

`NamingPolicy` renames mapped members that have no `[NbtPropertyName]` while generating converters.
The generated context overrides `CreateDefaultOptions`, so `GameContext.Default.Options` exposes the
same `PropertyNamingPolicy` and the configured `NullValueHandling`; a reflection resolver used with
those options therefore applies the same policy. Setting
`NbtSerializerOptions.PropertyNamingPolicy` directly does not rename members that were already
generated. Declaring `CreateDefaultOptions` in the same context type while using
`[NbtSourceGenerationOptions]` reports `NBTSG008`; generated defaults replace an inherited override
unless it is sealed.

The generator emits a converter for every reachable class or struct and type information for arrays,
the supported sequence shapes (lists, sets, queues, stacks, linked lists, mutable/read-only wrappers,
and immutable or frozen collections), string-keyed dictionary shapes, and nullable value types. Enums are
serialized as the signed NBT integer tag that matches their underlying type width (`byte`/`sbyte`
use `TAG_Byte`, `short`/`ushort` use `TAG_Short`, `int`/`uint` use `TAG_Int`, and `long`/`ulong`
use `TAG_Long`); unsigned values preserve their bit pattern. Built-in scalars include
`sbyte`/`byte`, `ushort`, `uint`, `ulong`, and `char`: numbers use the signed tag of the same width,
and `char` is stored as its UTF-16 code unit in `TAG_Short`. `nint`/`nuint`, `decimal`, `Guid`, date/time types,
`Half`, and `Int128`/`UInt128` still require a custom converter. The generator follows the same
mapping rules as the reflection resolver: public instance properties, `[NbtInclude]` fields, `[NbtIgnore]`,
`[NbtPropertyName]`, constructor binding, unknown-member skipping, and null and duplicate handling.
`init`-only and `required` members are supported through `UnsafeAccessor`; hidden (`new`) members
report `NBTSG017`. A private setter in another assembly is not visible to source generation; expose
it as `public`/`init`, make it `internal` and grant the context assembly `InternalsVisibleTo`, or put
the context in the same assembly. Unsupported shapes are reported as `NBTSG` diagnostics at compile
time.

Members whose type is not modeled by the generator can opt out with `[NbtUseConverter]`:

```csharp
internal sealed class Player
{
    [NbtUseConverter]
    public Guid Id { get; init; }

    [NbtUseConverter(typeof(DecimalConverter))]
    public decimal Balance { get; init; }
}
```

With no argument, the member's converter is resolved from `options.Converters` at runtime, preserving
the normal converter precedence. With a converter type, the generated code creates that converter
directly and uses it for the member; this form takes precedence over `options.Converters`. An explicit
`NbtConverter<T>` is AOT-safe when the converter itself is AOT-safe. The converter type must be a
closed, non-abstract type derived from `NbtConverter<T>` whose `T` matches the member type (or a
`NbtConverterFactory`), with a
public parameterless constructor. A converter for `Guid` therefore does not automatically apply to a
`Guid?` member; use runtime resolution or a nullable-aware converter. Marked members remain normal DTO
members, so required/null/constructor semantics are unchanged. Fields also require `[NbtInclude]`.
Invalid declarations report `NBTSG016`.

Runtime resolution may use reflection, so an AOT/trim-enabled consumer sees the corresponding
analyzer diagnostics for `[NbtUseConverter]` without a converter type; factories remain
analyzer-visible as well. Use the explicit typed `NbtConverter<T>` form when the generated path must
remain warning-free.

The analyzer ships inside the `NbtSharp` package. The type-information overloads do not carry
`RequiresUnreferencedCode` or `RequiresDynamicCode`; Native AOT works when every reachable type is
generated. Reflection remains available through `NbtReflectionTypeInfoResolver` for types that are
not part of a generated context, and it also supports enum members through the built-in enum
converter. When no custom converters are registered, generated object converters read and write
numeric, boolean, string, and nullable scalar members directly; registering a custom converter
disables that fast path for the operation. Generated member types additionally get static typed
converter fields, used when the converter list is empty and the context is the resolver; otherwise
resolution falls back to the options so custom converters and alternative resolvers keep precedence.
Required reference and nullable members are captured once before their null check, so the property
getter is not invoked twice. Converter resolution uses a lock-free immutable cache, so resolving many
types on a fresh options instance scales without repeated dictionary copies.
Each attributed context is prepared inside the incremental transform and emits a symbol-free
`PreparedContext`; generated source is cached per prepared context. A content-keyed reachability
fingerprint cache skips graph construction for unchanged contexts, and no symbol-carrying value is
retained by the pipeline. The cache is process-wide, bounded, and FIFO-evicted; it skips graph/spec
construction, but the fingerprint walk still runs when a transform is invalidated. Partial context
declarations are combined into one transform output. Editing an unrelated declaration in the same file
does not regenerate the context; positional-only changes reuse the cached spec unless the context has
diagnostics whose locations must be refreshed. Edits inside a reachable declaration rebuild that
context's graph because declaration text is part of the content fingerprint.

Required members are strict: every required member that is part of the mapping must be present in
the compound, otherwise deserialization throws `NbtException`. A required mapped member whose value
is null cannot be serialized and throws regardless of `NullValueHandling`. A required member that is
excluded from the mapping requires the selected constructor to be marked with
`[SetsRequiredMembers]`; otherwise the source generator reports `NBTSG015` and the reflection
resolver throws `NotSupportedException`. `[SetsRequiredMembers]` only covers excluded members:
mapped required members are still presence-checked, so a compound that omits one fails even when the
constructor initializes it. Required members on a value type require the selected constructor to be
marked with `[SetsRequiredMembers]`; because a parameterless constructor is preferred, this means an
explicit public parameterless constructor with the attribute. Otherwise the generator reports
`NBTSG007`.

### Polymorphic members

Abstract classes and interfaces opt in to polymorphic members with `[NbtDerived]`:

```csharp
[NbtDerived(typeof(Cat))]
[NbtDerived(typeof(Dog), TypeDiscriminator = "dog")]
interface IAnimal
{
    string Name { get; set; }
}

sealed class Cat : IAnimal { /* ... */ public int Lives { get; set; } }
sealed class Dog : IAnimal { /* ... */ public bool GoodBoy { get; set; } }

sealed class PetOwner
{
    public IAnimal? Pet { get; set; }
}
```

A base-typed value is encoded as an envelope compound:

```text
Pet = {
  "$type": "Cat",
  "$value": { "Name": "Milo", "Lives": 9 }
}
```

The discriminator defaults to the derived type's short name and can be overridden with
`TypeDiscriminator`. The base must be a non-generic abstract class or interface. Registrations must
be closed concrete classes or structs assignable to the base, must not be collection shapes or
polymorphic themselves, and each derived type may be registered only once. Discriminators must be
unique. Invalid declarations report `NBTSG018` in generated contexts and throw
`NotSupportedException` from the reflection resolver. Unknown or missing discriminators and missing
`$value` members throw `NbtException`.

Generated contexts dispatch through generated converters, and an unregistered runtime type throws
`NbtException` on write. Register a converter for the base type to override the whole dispatch.
Reflection dispatch resolves the concrete type through the options, so custom converters for
derived types keep working there.

## Streaming

The reader produces value and container tokens. Compound members carry a name; list elements do not:

```csharp
var reader = new NbtReader(data);

while (reader.Read())
{
    Console.WriteLine($"{reader.TokenType} {reader.Name} {reader.TagType} depth={reader.CurrentDepth}");
    if (reader.TokenType == NbtTokenType.Value)
    {
        reader.Skip();
    }
}
```

Named writes are atomic and follow the NBT wire order `Type → Name → Payload`:

```csharp
var buffer = new ArrayBufferWriter<byte>();
var writer = new NbtWriter(buffer);

writer.WriteStartCompound("Player");
writer.WriteInt32("Health", 20);
writer.WriteStartList("Tags", NbtTagType.String, 2);
writer.WriteString("a");
writer.WriteString("b");
writer.WriteEndList();
writer.WriteEndCompound();
writer.Complete();

byte[] bytes = buffer.WrittenSpan.ToArray();
```

Anonymous writes—`WriteInt32(20)`, `WriteString("a")`, `WriteStartCompound()`—are used for list
elements and for converter payloads whose member or root header was already written. A compound
converter opens its payload with `WriteStartCompound()` and writes members with named methods or
`NbtSerializer.SerializeMember`, which handles dynamic member types.

A list header carries its element type and count before the elements. The writer verifies that every
element has the declared type and that exactly the declared number of elements were written.
`WriteString` accepts both `string` and `ReadOnlySpan<char>`; the span overload is the
allocation-free path and is used by the built-in string converter.

Names can also travel as bytes. `NbtReader.NameEquals(ReadOnlySpan<byte>)` compares the current name
against pre-encoded modified UTF-8 bytes, and the writer accepts pre-encoded names —
`WriteInt32(ReadOnlySpan<byte>, int)` and the other scalar overloads, plus
`WriteMemberHeader(NbtTagType, ReadOnlySpan<byte>)` — producing byte-identical output to the string
overloads. Names are compared by decoded value, so any two valid encodings of the same name match; a
`ReadOnlySpan<char>` overload compares against an already-decoded name. Comparisons decode into
stack buffers for names of at most 256 chars and pooled buffers for longer names, so no strings are
allocated. `Name` itself is decoded lazily and cached per token. The byte-name write
overloads do not validate the name content as modified UTF-8; they can emit names the reader will
reject, and validity is the caller's responsibility.

## Read-only DOM (NbtDocument)

`NbtDocument` is for random access over a document you want to probe repeatedly without a
forward-only token loop or a fully materialized `NbtValue` tree. `Parse` indexes the whole
document in one pass — structure is fully validated at parse time — and every
`NbtElement` getter afterwards reads the indexed bytes without re-parsing. `Dispose` returns the
pooled buffers, so the recommended shape is `using`:

```csharp
using var document = NbtDocument.Parse(data);

int health = document.RootElement.Compound["Health"].Int32;
foreach (var element in document.RootElement.Compound["Numbers"].List)
{
    Console.WriteLine(element.Int32);
}
```

Input handling mirrors `System.Text.Json.JsonDocument`. `Parse(ReadOnlyMemory<byte>)` reads
directly from the supplied memory with no copy — the caller must keep the underlying buffer alive
and unmodified until `Dispose` (returning it to an array pool early is undefined behavior), and
`byte[]` arguments share the array the same way through implicit conversion. Sequence and stream
inputs are copied into pooled buffers first; the copy is clamped to
`NbtReaderOptions.MaxDocumentBytes`, and trailing bytes after the root value never count towards
the limit. There is no `ReadOnlySpan<byte>` overload by design: span callers wrap the span in a
memory themselves, which keeps the zero-copy contract explicit.

Each indexed value costs a 28-byte slot, so indexing multiplies the document size by up to about
6x in the worst case (one-byte list elements); `Dispose` returns both the slot array and the
copied buffer to their pools. Documents are bounded by `int.MaxValue` bytes — longer input throws
`NbtException` at parse time, because element offsets are 32-bit (the streaming reader has no such
limit). A document is safe to read from multiple threads concurrently, but `Dispose` must not
race with reads; elements of a disposed document throw `ObjectDisposedException`.

Access costs are deterministic: `Count`/`ElementType` are O(1); member and element enumeration follows
wire order; the list indexer is O(1) on lists of scalars, strings, and arrays, and O(index) on lists
of compounds or nested lists; the compound indexer and `TryGetValue` walk the members, with
duplicated names resolving to the last occurrence. Typed getters can be called repeatedly — unlike
the reader, they do not consume anything. Compound lookup compares names by decoded value, decoding
wire names into stack buffers or pooled buffers for long names; `NbtStringElement.GetString` decodes
the payload on every access, and invalid bytes throw at that point rather than at parse time. Lists,
compounds, strings, and arrays are views over the slot index; `ToArray` and `GetString` materialize
only when called, and compound pair enumeration decodes each member name.

`NbtListElement` implements `IReadOnlyList<NbtElement>`; interface enumeration boxes the struct
enumerator, while direct `foreach` over the view stays allocation-free. `NbtCompoundElement`
implements `IReadOnlyCollection<KeyValuePair<string, NbtElement>>`: enumeration is wire-order and
preserves duplicate names, materializing each name as the pair key. Its indexer and `TryGetValue`
resolve the last occurrence; it is not `IReadOnlyDictionary`.

## Converters

A converter writes exactly one complete value; it does not write the member name. Dynamic tag types
are allowed:

```csharp
public sealed class CompactInt32Converter : NbtConverter<int>
{
    public override int Read(ref NbtReader reader, NbtSerializerOptions options) => reader.TagType switch
    {
        NbtTagType.Byte => reader.GetSByte(),
        NbtTagType.Short => reader.GetInt16(),
        _ => reader.GetInt32(),
    };

    public override void Write(ref NbtWriter writer, int value, NbtSerializerOptions options)
    {
        if (value is >= sbyte.MinValue and <= sbyte.MaxValue)
            writer.WriteSByte((sbyte)value);
        else if (value is >= short.MinValue and <= short.MaxValue)
            writer.WriteInt16((short)value);
        else
            writer.WriteInt32(value);
    }

    public override bool TryGetTagType(int value, NbtSerializerOptions options, out NbtTagType tagType)
    {
        tagType = value is >= sbyte.MinValue and <= sbyte.MaxValue
            ? NbtTagType.Byte
            : value is >= short.MinValue and <= short.MaxValue
                ? NbtTagType.Short
                : NbtTagType.Int;
        return true;
    }
}
```

Add converters to `options.Converters`. Custom converters are checked before built-in converters.
Both generated and reflection mapping honor `[NbtUseConverter]`: the attribute opts a member out of
the generated type graph or reflection's object mapping and resolves its converter at runtime;
`[NbtUseConverter(typeof(MyConverter))]` uses a specific converter and does not consult
`options.Converters` for that member. Invalid declarations report `NBTSG016` during generation and
throw `NotSupportedException` from the reflection resolver. Null member values follow the normal
`NullValueHandling` rules and are not passed to the member converter. The explicit form requires the
converter to be an `NbtConverter<T>` for the exact member type or a `NbtConverterFactory`. Fields
also require `[NbtInclude]`. Generated code additionally requires the converter type to be accessible
from the context assembly; reflection instantiates it directly and does not apply that
context-assembly check.
`TryGetTagType` is required for named compound members, root values, and the first element of a
non-empty list, because the tag type must be known before the header is written. If it returns
`false`, those operations throw `NotSupportedException`; implement the prediction to use the
converter in those positions. Within a list, all elements must still share one actual tag type, and
the writer validates every element against the predicted header type.

### String enums

`[NbtStringEnum]` on an enum switches it from an integer tag to `TAG_String`, following
System.Text.Json's `JsonStringEnumConverter` semantics:

```csharp
[NbtStringEnum]
public enum MobKind : byte
{
    Zombie,
    Creeper,
}
```

Defined values are written as their member name (the first declared name when several names share a
value) and read back with an exact, case-sensitive ordinal match. Values without a defined name —
including undefined flags combinations — round-trip as the invariant numeric string of the
underlying width, so `(MobKind)200` serializes to `"200"`. A `[Flags]` enum additionally reads a
comma-separated list: every segment is trimmed and resolved as a name or a numeric string before
being OR-combined, so `"Walk, Swim"`, `"1, 2"`, and `"Walk, 2"` all parse; the write side uses the
runtime's flags formatting, which emits the plain number when a combination has no complete name
decomposition. Unresolvable input throws `NbtException` naming the offending string and the enum
type.

Nullable enums, collection elements, map values, object members, and root values all inherit the
string shape. Converter precedence is unchanged: a member's `[NbtUseConverter]` wins, then
`options.Converters` (register `new NbtStringEnumConverter<TEnum>()` there to apply the string form
to an unmarked enum), then the enum's `[NbtStringEnum]`, then the numeric built-in.

Both the reflection resolver and the source generator honor the attribute; the generator
instantiates `NbtStringEnumConverter<TEnum>` directly, so string enums are AOT-safe. In trimmed
applications that use the reflection path, the attribute may be stripped from the compiled enum,
in which case the enum silently falls back to the numeric representation; the generated path sees
the attribute at compile time and is unaffected.

Automatic sequence support covers arrays, `List<T>`, `IEnumerable<T>`, `ICollection<T>`, `IList<T>`,
`IReadOnlyList<T>`, `IReadOnlyCollection<T>`, `HashSet<T>`, `ISet<T>`, `IReadOnlySet<T>`,
`SortedSet<T>`, `Queue<T>`, `Stack<T>`, `LinkedList<T>`, `Collection<T>`, `ObservableCollection<T>`,
`ReadOnlyCollection<T>`, `ReadOnlyObservableCollection<T>`, `ConcurrentBag<T>`, `ConcurrentQueue<T>`,
`ConcurrentStack<T>`,
`BlockingCollection<T>`, `ImmutableArray<T>`, `ImmutableList<T>`, `IImmutableList<T>`,
`ImmutableHashSet<T>`, `IImmutableSet<T>`, `ImmutableQueue<T>`, `IImmutableQueue<T>`,
`ImmutableStack<T>`, `IImmutableStack<T>`, `ImmutableSortedSet<T>`, and `FrozenSet<T>`.

Concrete collection types are discovered by their implemented interfaces. Construction uses a
`CollectionBuilderAttribute` builder, a companion static `CreateRange`/`Create` factory, or a public
constructor/`Add`/`Enqueue`/`Push` pattern; the generator emits the matching builder into the context.
Interfaces are materialized the same way C# collection expressions choose targets: list interfaces
become `List<T>` and set interfaces become `HashSet<T>`. NBT list order is the collection's enumeration
order; stack-like types are filled in reverse so they round-trip, while set-like shapes collapse
duplicate elements. `SortedSet<T>` and `ImmutableSortedSet<T>` use ordinal ordering for string
elements, so round-tripping does not depend on the current culture; custom types derived from them
follow their own construction comparer. An `IEnumerable<T>` value that is
not an `IReadOnlyCollection<T>` is materialized before writing. `PriorityQueue<TElement,TPriority>`
is rejected with a `NotSupportedException` (and `NBTSG003` in generated contexts) because its
element/priority pairs have no natural NBT sequence representation; register a custom converter to
choose one.

Automatic map support covers `Dictionary<string,T>`, `IDictionary<string,T>`,
`IReadOnlyDictionary<string,T>`, `ReadOnlyDictionary<string,T>`, `SortedDictionary<string,T>`,
`SortedList<string,T>`, `ConcurrentDictionary<string,T>`, `ImmutableDictionary<string,T>`,
`IImmutableDictionary<string,T>`, `ImmutableSortedDictionary<string,T>`, and
`FrozenDictionary<string,T>`. Keys must be strings; duplicate keys keep the last value unless
`RejectDuplicateCompoundNames` is set. Dictionaries and sets do not preserve NBT order, so their
serialized member order is the collection's enumeration order.

The reflection resolver supports the same built-in shapes and uses a reflection-based pattern
fallback for collection-builder and constructor/`Add` types. `CollectionBuilderAttribute` and
companion factories that take `ReadOnlySpan<T>` or `Span<T>` are invoked through a runtime-created
delegate; this requires dynamic code, so source generation remains the AOT-safe path. If the delegate
cannot be created, the resolver falls back to the other construction patterns and throws
`NotSupportedException` only when none is usable.

On read, arrays with fixed-width element tags are allocated once at the declared list length instead
of being built through `List<T>`; variable-size element arrays and stream-backed reads grow from a
bounded capacity, so malformed or partially buffered input cannot force a large upfront allocation.
`sbyte[]` is read directly without an intermediate `byte[]`. Lists at or below 4096 elements are
filled through `CollectionsMarshal` span access; larger declared lengths grow as elements are read.

The generic writer encodes an empty collection as `TAG_List<TAG_End>`, which every supported target
collection accepts. An empty list with a concrete element type is checked against the requested element type
for built-in converters; custom converters and object-mapped element types are not inferred when
the list is empty.

## Options, null, and errors

`NbtSerializerOptions` controls:

- `Converters`, `TypeInfoResolver`, and `RootFormat` (named or unnamed root).
  Converter lookup checks `Converters` first, then `TypeInfoResolver`, then the built-in
  converters, so a resolver can supply converters for collection types that the built-in
  factories would otherwise construct dynamically. `NbtTypeInfoResolverChain` consults resolvers
  in order and uses the first non-null result; for migration, combine a generated context with
  the reflection resolver, for example
  `new NbtTypeInfoResolverChain(context, new NbtReflectionTypeInfoResolver())`. A chain inherits
  the trimming/AOT requirements of its resolvers, so the reflection fallback is not AOT-safe.
  Type-information serializer overloads use the options bound to the matched type information;
  for a generated context that is the context's own `Options`.
- `NullValueHandling`: omit a null compound member (default) or throw. Null roots and null list
  elements always throw because NBT has no null tag.
- `PropertyNamingPolicy`: a preset `NbtNamingPolicy` that renames reflection-mapped members without
  `[NbtPropertyName]`.
- `RejectDuplicateCompoundNames`: while reading, keep the last duplicate (default) or throw.
- `MaxDepth`, `MaxCollectionLength`, and `MaxDocumentBytes` safety limits. A value of 0 selects the
  library default; `MaxDocumentBytes` 0 means unlimited.

`NbtException` reports `BytePosition`, a JSON-path-like `Path` such as `$.Items[0].Count`, and the
expected/actual tag types. API misuse (reading a value twice, closing a container that is not open)
throws `InvalidOperationException`. A CLR type without a converter or type information throws
`NotSupportedException`.

### Modifying reflection contracts at runtime

A `NbtReflectionTypeInfoResolver` created with an `Action<NbtContractInfo>` callback — or extended
through `WithAddedModifier`, which appends to a snapshot and keeps an independent cache — can
rename members (`NbtMemberInfo.NbtName`, already reflecting attributes and the naming policy),
exclude them (`Ignore`), replace their converters (`Converter`), or reorder them (`Order`) without
editing DTO attributes. The contract is frozen after the callbacks run; later mutations of a
stashed `NbtMemberInfo` throw `InvalidOperationException`.

Callbacks are pure functions: they run at most once per `(Type, PropertyNamingPolicy)` pair on
first use, the resulting converter is cached by the resolver, and a thrown exception is not
cached, so the next operation retries. They run under the resolver's cache lock, which serializes
type resolution for that resolver: keep them short and non-blocking, and never block inside a
callback on another thread that resolves types through the same options. Unlike
`System.Text.Json`, the callback receives no options instance — the contract is fixed per type and
naming policy on first use. Resolving other types inside a callback is allowed through same-thread
lock reentrancy, but looping back to the type currently being built throws
`InvalidOperationException` instead of overflowing the stack. Polymorphic envelopes stay
attribute-driven; their derived concrete types resolve normally and run the callbacks. In an
`NbtTypeInfoResolverChain`, a modified reflection resolver only affects types that fall through to
the reflection fallback, so a generated context in front of it is untouched (generated contexts
bake contracts at compile time and have no runtime modifier hook).

## Scope and AOT

This package implements Java Edition encoding. Bedrock encodings and compression wrappers are not
implemented. Strings are encoded through
[Myitian.Text.ModifiedUTF8Encoding](https://github.com/Myitian/Modified-UTF8-Encoding); decoding
follows Java's `DataInputStream.readUTF` and rejects lead bytes of 0xF0 or greater.

Generic serializer entry points that may resolve converters or reflection are annotated with
`RequiresUnreferencedCode` and `RequiresDynamicCode`. DOM entry points use built-in converters and
remain AOT-clean. The reflection resolver is opt-in. Source-generated type information and the
type-information serializer overloads provide the fully AOT-safe path. The `NbtSharp.Aot.Tests`
project sets `IsAotCompatible` and covers generated converters, required/init members, enums,
generic DTOs, collections, nullable values, explicit member converters, and polymorphic
registrations under the SDK AOT/trim analyzers. Generated converters suppress AOT/trim diagnostics
only when every member is covered by the generated graph or by an explicit typed converter; runtime
and factory-based `[NbtUseConverter]` members remain analyzer-visible.

Synchronous `Serialize(Stream)` flushes the document when serialization completes and leaves the
stream open; `Deserialize(Stream)` reads the stream through a fixed-size buffer, so non-seekable
streams are supported. A stream is treated as one document: trailing bytes after the root value are
ignored, and the stream may have been read ahead. `DeserializeAsync` is available for `Stream`,
`ReadOnlySequence<byte>`, and `PipeReader`; it consumes one document asynchronously through a
`PipeReader`. The `PipeReader` and `ReadOnlySequence<byte>` overloads leave bytes after the root
available to the caller, while the `Stream` overload may read ahead and discard them.

`SerializeAsync` is available for `Stream` and `PipeWriter`, with and without type information.
Writing is synchronous and the cancellation token is observed by the asynchronous flush; after a
cancellation, the stream wrapper does not flush its buffered output. The caller owns a `PipeWriter`
and must complete it; the `Stream` overload completes only its internal pipe after a successful or
non-cancellation failure and leaves the stream open. `SerializeDocumentAsync(Stream/PipeWriter,
NbtRoot, options, ...)` preserves `NbtRoot.Name`; the generic async overloads use `rootName`, like the
generic synchronous overloads. The writer does not roll back partial output if a converter throws.

## Documentation

- [Source generation and Native AOT](docs/source-generation.md)
- [Migrating from reflection](docs/migration.md)
- [Diagnostics](docs/diagnostics.md)
- [Performance characteristics](docs/performance.md)
- [System.Text.Json gap analysis](docs/stj-gap-analysis.md)
- [Roadmap](docs/roadmap.md)

## Build and test

```bash
dotnet build NbtSharp.slnx
dotnet test NbtSharp.slnx

# Source-generation cold/warm and runtime scalar/array serialization benchmarks.
dotnet run --project NbtSharp.SourceGeneration.Benchmarks/NbtSharp.SourceGeneration.Benchmarks.csproj -c Release -- --filter '*GeneratorIncrementalityBenchmarks*'
dotnet run --project NbtSharp.SourceGeneration.Benchmarks/NbtSharp.SourceGeneration.Benchmarks.csproj -c Release -- --filter '*SerializationBenchmarks*'
```

Requires the .NET 11 SDK (see `global.json`). Tests run on xunit.v3 through
Microsoft.Testing.Platform.

## License

[Apache-2.0](LICENSE)

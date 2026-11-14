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
  or a pipe reader; the writer targets a buffer writer or a pipe writer.
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

Reflection mapping uses public instance properties by default. `[NbtPropertyName("name")]` renames a
member, `[NbtIgnore]` excludes it, and public fields are included only with `[NbtInclude]`. A
public parameterless constructor is preferred; otherwise a constructor whose parameter names match
the members is used. Unknown members are skipped and missing members keep their defaults.

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
`TryGetTagType` is required for named compound members, root values, and the first element of a
non-empty list, because the tag type must be known before the header is written. If it returns
`false`, those operations throw `NotSupportedException`; implement the prediction to use the
converter in those positions. Within a list, all elements must still share one actual tag type, and
the writer validates every element against the predicted header type.

Automatic list support covers arrays, `List<T>`, `IReadOnlyList<T>`, and `IReadOnlyCollection<T>`,
whose length is known before the list header is written. `IList<T>`, `ICollection<T>`, and
`IEnumerable<T>` are not automatically supported; materialize to `List<T>` or provide a custom
converter that writes the list header and elements itself.

The generic writer encodes an empty collection as `TAG_List<TAG_End>`, which every supported target
collection accepts. An empty list with a concrete element type is checked against the requested element type
for built-in converters; custom converters and object-mapped element types are not inferred when
the list is empty.

## Options, null, and errors

`NbtSerializerOptions` controls:

- `Converters`, `TypeInfoResolver`, and `RootFormat` (named or unnamed root).
- `NullValueHandling`: omit a null compound member (default) or throw. Null roots and null list
  elements always throw because NBT has no null tag.
- `RejectDuplicateCompoundNames`: while reading, keep the last duplicate (default) or throw.
- `MaxDepth`, `MaxCollectionLength`, and `MaxDocumentBytes` safety limits. A value of 0 selects the
  library default; `MaxDocumentBytes` 0 means unlimited.

`NbtException` reports `BytePosition`, a JSON-path-like `Path` such as `$.Items[0].Count`, and the
expected/actual tag types. API misuse (reading a value twice, closing a container that is not open)
throws `InvalidOperationException`. A CLR type without a converter or type information throws
`NotSupportedException`.

## Scope and AOT

This package implements Java Edition encoding. Bedrock encodings and compression wrappers are not
implemented. Strings are encoded through
[Myitian.Text.ModifiedUTF8Encoding](https://github.com/Myitian/Modified-UTF8-Encoding); decoding
follows Java's `DataInputStream.readUTF` and rejects lead bytes of 0xF0 or greater.

The generic serializer can resolve reflection and construct closed generic converters at runtime, so
its entry points are annotated with `RequiresUnreferencedCode` and `RequiresDynamicCode`. The
reflection resolver is opt-in. A source generator for fully AOT-safe type information is planned.

The `Stream` overloads are synchronous. `Serialize(Stream)` flushes the document when serialization
completes and leaves the stream open; `Deserialize(Stream)` reads the stream incrementally, so
non-seekable streams are supported. A stream is treated as one document: trailing bytes after the
root value are ignored, and the stream may have been read ahead. To read consecutive documents from
one stream, use `Deserialize(PipeReader)` with a caller-owned pipe reader. A caller-owned pipe must
return data synchronously from `TryRead`; a live producer that requires an asynchronous read is not
supported. The writer does not roll back partial output if a converter throws.

## Build and test

```bash
dotnet build NbtSharp.slnx
dotnet test NbtSharp.slnx
```

Requires the .NET 11 SDK (see `global.json`). Tests run on xunit.v3 through
Microsoft.Testing.Platform.

## License

[Apache-2.0](LICENSE)

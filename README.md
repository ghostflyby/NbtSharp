# NbtSharp

An NBT (Named Binary Tag) library built on C# union types.

- `net11.0`, Native AOT compatible (`IsAotCompatible`).
- Uses C# 15 union types: `NbtValue` is a `union` declaration, while `NbtList` is a hand-written
  `[Union]` struct.

## Design

The DOM mirrors the NBT type system directly:

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
    public bool HasValue { get; }          // always true
    public NbtTagType ElementType { get; }
    public int Count { get; }
}

public sealed class NbtCompound : Dictionary<string, NbtValue>;
```

Scalars use the CLR type whose width matches the NBT type, so tag identity is preserved by the type
system. `NbtList` is a hand-written union: its twelve element cases are standard `List<T>` values,
and the thirteenth is the `NbtEndList` marker. A default-initialized `NbtList` stores no list and
represents `TAG_List<TAG_End>`; its `Value` returns the shared `NbtEndList.Instance` marker, so
`default(NbtList) is NbtEndList` is true and `default(NbtList) is null` is false. An empty list of a
known element type still encodes as that type with length zero, so the two empty-list encodings stay
distinct. Because `NbtValue` stores its case as a boxed object, test an End list through the list
value (`nbtValue is NbtList list && list is NbtEndList`); `nbtValue.Value is NbtEndList` is false.

## Usage

```csharp
var player = new NbtCompound
{
    ["Health"] = 20,
    ["Name"] = "Steve",
    ["Speed"] = 0.1f,
    ["Alive"] = (sbyte)1,
};

if (player["Health"] is int health)
{
    Console.WriteLine(health);
}

using var stream = File.Create("player.nbt");
NbtSerializer.Serialize(stream, new NbtRoot("Player", player));
```

Union pattern matching unwraps the contained value, so `player["Health"] is int health` tests the
value rather than the union wrapper. Different integer widths produce different tag types:

```csharp
NbtValue a = (short)20; // TAG_Short
NbtValue b = 20;        // TAG_Int
NbtValue c = 20L;       // TAG_Long
```

## Streaming

`NbtReader` and `NbtWriter` expose the same encoding as a forward-only token stream, which avoids
building a DOM for large files:

```csharp
using var reader = new NbtReader(stream);

while (reader.Read())
{
    Console.WriteLine($"{reader.TokenType} {reader.Name} {reader.TagType}");
    if (reader.TokenType == NbtTokenType.Value)
    {
        reader.Skip();
    }
}
```

A list header carries its element type and count, so `WriteStartList` takes the count up front and
the writer verifies that exactly that many elements follow.

## Scope

This milestone covers the DOM types, Java Edition encoding (big-endian, fixed-width numbers, Java
modified UTF-8 strings), the streaming reader and writer, and DOM serialization. Bedrock encodings
and compression wrappers are not implemented yet.

Strings are encoded and decoded through
[Myitian.Text.ModifiedUTF8Encoding](https://github.com/Myitian/Modified-UTF8-Encoding). Decoding
follows Java's `DataInputStream.readUTF`: sequences with a lead byte of 0xF0 or greater (standard
UTF-8 four-byte forms) are rejected, while raw null bytes and overlong three-byte forms are
accepted and normalized when the value is written again.

Current decisions: compound members enumerate in dictionary insertion order (an implementation
detail, not a contract); a duplicate name in binary data keeps the last occurrence; serializing an
uninitialized `NbtValue` throws `NbtException`. `NbtValue` and `NbtList` are plain structs, so they
compare by reference for reference-typed contents; a value-equality API is not yet provided.

## Build and test

```bash
dotnet build NbtSharp.slnx
dotnet test NbtSharp.slnx
```

Requires the .NET 11 SDK (see `global.json`). Tests run on
[xunit.v3](https://xunit.net/) driven natively by `dotnet test` through
Microsoft.Testing.Platform.

## License

[Apache-2.0](LICENSE)

# NbtSharp

An NBT (Named Binary Tag) library built on C# union types.

- `net11.0`, Native AOT compatible (`IsAotCompatible`).
- Uses C# 15 union declarations, which .NET 11 and its default language version support directly.

## Design

The DOM mirrors the NBT type system directly:

```csharp
public union NbtValue(
    sbyte, short, int, long, float, double,
    sbyte[], string, NbtList, NbtCompound, int[], long[]
);

public union NbtList(
    List<sbyte>, List<short>, List<int>, List<long>, List<float>, List<double>,
    List<sbyte[]>, List<string>, List<NbtList>, List<NbtCompound>,
    List<int[]>, List<long[]>, NbtEmptyList
);

public sealed class NbtCompound : Dictionary<string, NbtValue>;
```

Scalars use the CLR type whose width matches the NBT type, so tag identity is preserved by the type
system. List cases are standard `List<T>` values, which keeps elements homogeneous while the closed
union case set restricts them to legal NBT element types. `NbtEmptyList` represents
`TAG_List<TAG_End>`, which is distinct from an empty list of a known element type.

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

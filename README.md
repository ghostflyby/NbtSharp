# NbtSharp

A .NET library.

- `net10.0`, Native AOT compatible (`IsAotCompatible`).

## Install

```bash
dotnet add package NbtSharp
```

## Build and test

```bash
dotnet build NbtSharp.slnx
dotnet test NbtSharp.slnx
```

Requires the .NET 10 SDK (see `global.json`). Tests run on
[xunit.v3](https://xunit.net/) driven natively by `dotnet test` through
Microsoft.Testing.Platform.

## License

[Apache-2.0](LICENSE)

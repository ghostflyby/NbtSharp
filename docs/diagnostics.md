# Diagnostics

The source generator reports `NBTSG` diagnostics as build errors or warnings. All active
diagnostics are listed here; gaps in the numeric sequence are retired identifiers.

| Id | Severity | Meaning | Resolution |
| --- | --- | --- | --- |
| `NBTSG001` | Error | A context marked with `[NbtSerializable]` is not `partial`. | Add the `partial` modifier to the context declaration. |
| `NBTSG002` | Error | The context shape is invalid, or a `[NbtSourceGenerationOptions]` value is not a defined enum value. | A context must be a non-static, non-abstract, non-generic, non-file-local class at namespace scope, derive from `NbtSerializerContext`, be public or internal, declare a parameterless constructor, and use defined option values. |
| `NBTSG003` | Error | A root, member, collection element, or map value type cannot be mapped by the generator. | Use a supported type. For a member, add `[NbtUseConverter]` or `[NbtUseConverter(typeof(MyConverter))]`. Roots and collection elements need a converter registered in `NbtSerializerOptions.Converters`. |
| `NBTSG004` | Error | No usable constructor exists for an object type. | Add a public parameterless constructor, or a public constructor whose parameter names match the mapped members. |
| `NBTSG005` | Error | More than one member maps to the same NBT name. | Rename or exclude one member with `[NbtPropertyName]` or `[NbtIgnore]`. A naming policy can also cause a collision; use an explicit name on one member. |
| `NBTSG006` | Error | A reachable type is not accessible from the generated context. | Make the type and its containing types public or internal, or move the context into an assembly that can access it. |
| `NBTSG007` | Error | A value type has `required` members. | Source-generated construction of required value types is not supported. Use a class, or remove `required` from the value type. |
| `NBTSG008` | Error | A generated member name conflicts with a context member or another generated member. | Rename the conflicting member, or choose a different `TypeInfoPropertyName`. `Default`, `Options`, `GetTypeInfo`, and `CreateDefaultOptions` are reserved. A context that uses `[NbtSourceGenerationOptions]` must not declare its own `CreateDefaultOptions`; an inherited override is replaced unless it is sealed. |
| `NBTSG009` | Error | A context is nested inside another type. | Move the context to namespace scope. |
| `NBTSG011` | Error | A collection shape is not supported. | Use a supported sequence or string-keyed map shape, or provide `CollectionBuilderAttribute` or a public constructor/`Add` pattern that the generator can use. |
| `NBTSG013` | Warning | A root type is already handled by a built-in converter. | Remove the unnecessary `[NbtSerializable]` root; built-in scalars, arrays, and built-in collection shapes do not need generated type information. |
| `NBTSG015` | Error | A `required` member is excluded from the mapping and the selected constructor is not marked `[SetsRequiredMembers]`. | Mark the constructor with `[SetsRequiredMembers]`, include the member in the mapping, or remove `required`. |
| `NBTSG016` | Error | A `[NbtUseConverter]` declaration is invalid. | The converter type must be closed, non-abstract, accessible, derived from `NbtConverter<T>` for the member type (or `NbtConverterFactory`), and have a public parameterless constructor. Fields also need `[NbtInclude]`. |
| `NBTSG017` | Error | A mapped member is hidden by a member with the same name. | Rename the hiding member, or exclude the base member with `[NbtIgnore]`. NBT maps each type to one compound, so hidden members are ambiguous. |

## Notes

- Diagnostics are reported at the declaration that caused them; reachable-type errors point at the
  member or root that introduced the type.
- Context diagnostics keep their locations fresh when declarations move; unrelated edits reuse the
  cached prepared context.
- A context with any error diagnostics does not emit source. Fixing the error regenerates it on the
  next build.
- Reflection mapping reports analogous problems as `NotSupportedException` at runtime instead of
  build diagnostics.

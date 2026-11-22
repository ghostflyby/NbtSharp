# Diagnostics

The source generator reports `NBTSG` diagnostics as build errors or warnings. All active
diagnostics are listed here; gaps in the numeric sequence are retired identifiers.

| Id | Severity | Meaning | Resolution |
| --- | --- | --- | --- |
| `NBTSG001` | Error | A context marked with `[NbtSerializable]` is not `partial`. | Add the `partial` modifier to the context declaration. |
| `NBTSG002` | Error | The context shape is invalid, or a `[NbtSourceGenerationOptions]` value is not a defined enum value. | A context must be a non-static, non-abstract, non-generic, non-file-local class at namespace scope, derive from `NbtSerializerContext`, be public or internal, declare a constructor with no required parameters (parameterless or all-optional/`params`), and use defined option values. |
| `NBTSG003` | Error | A root, member, collection element, or map value type cannot be mapped by the generator. | For a member, add `[NbtUseConverter]` or `[NbtUseConverter(typeof(MyConverter))]`; for a collection or map nested in a member, mark the containing member the same way and register a converter in `NbtSerializerOptions.Converters`. Roots and collection elements cannot be fixed by registering an options converter while the generator still has to map them. Map keys must be `string`. |
| `NBTSG004` | Error | No usable constructor exists for an object type. | Add a public parameterless constructor, or a public constructor whose parameter names and types match the mapped members (no `ref`/`out` parameters). |
| `NBTSG005` | Error | More than one member maps to the same NBT name. | Rename or exclude one member with `[NbtPropertyName]` or `[NbtIgnore]`. A naming policy can also cause a collision; use an explicit name on one member. |
| `NBTSG006` | Error | A reachable type is not accessible from the generated context. | Make the type public, or move the context into the type's declaring assembly. The generator accepts `internal` types only from the context assembly and does not consult `InternalsVisibleTo` for this check. |
| `NBTSG007` | Error | A value type has mapped `required` members and its selected constructor is not marked `[SetsRequiredMembers]`. | Give the value type an explicit public parameterless constructor marked `[SetsRequiredMembers]`, or remove `required`. A parameterized `[SetsRequiredMembers]` constructor is not selected while a parameterless constructor exists. |
| `NBTSG008` | Error | A generated member name conflicts with a context member or another generated member. | Rename the conflicting member, or choose a different `TypeInfoPropertyName`. `Default`, `Options`, `GetTypeInfo`, and `CreateDefaultOptions` are reserved. A context that uses `[NbtSourceGenerationOptions]` must not declare its own `CreateDefaultOptions`; an inherited override is replaced unless it is sealed. |
| `NBTSG009` | Error | A context is nested inside another type. | Move the context to namespace scope. |
| `NBTSG010` | Error | A `[NbtExtensionData]` declaration is invalid. | The member type must be `NbtCompound` or `Dictionary<string, NbtValue>` (exactly; no derived, interface, or other closed generic forms), there may be only one per type, the member must have a setter, cannot be `required`, cannot be combined with `[NbtPropertyName]` or `[NbtUseConverter]`, and fields also need `[NbtInclude]`. The reflection resolver applies the same rules and throws `NotSupportedException`. Extension members do not participate in constructor binding. |
| `NBTSG011` | Error | A collection shape is not supported. | Use a supported sequence or string-keyed map shape, or provide `CollectionBuilderAttribute` or a public constructor/`Add` pattern that the generator can use. |
| `NBTSG013` | Warning | A root type is in the generator's built-in set and does not need generated type information. | Remove the unnecessary `[NbtSerializable]` root. The built-in set covers scalars, `NbtValue`/`NbtList`/`NbtCompound`, and `byte[]`/`sbyte[]`/`int[]`/`long[]`; other arrays and collections still get generated type information without this warning. |
| `NBTSG015` | Error | A `required` member is excluded from the mapping and the selected constructor is not marked `[SetsRequiredMembers]`. | Mark the constructor with `[SetsRequiredMembers]`, include the member in the mapping, or remove `required`. |
| `NBTSG016` | Error | A `[NbtUseConverter]` declaration is invalid. | The converter type must be closed, non-abstract, derived from `NbtConverter<T>` for the member type (or `NbtConverterFactory`), and have a public parameterless constructor. It must be public or internal in the context assembly; `InternalsVisibleTo` is not considered. Fields also need `[NbtInclude]`. The reflection resolver validates the same type-shape rules and throws `NotSupportedException`; it instantiates the converter directly, so it does not apply the context-assembly accessibility restriction. |
| `NBTSG017` | Error | A mapped member is hidden by a member with the same name. | Rename the hiding member, or exclude the base member with `[NbtIgnore]`. NBT maps each type to one compound, so hidden members are ambiguous. |
| `NBTSG018` | Error | A `[NbtDerived]` declaration is invalid. | The base must be a non-generic abstract class or interface; the derived type must be a closed concrete class or struct assignable to it, not a collection shape, and not itself polymorphic. Each derived type may be registered only once; discriminators must be unique and non-empty. Generic polymorphic bases are rejected. |

## Notes

- Diagnostics are reported at the declaration that caused them; reachable-type errors point at the
  member or root that introduced the type.
- Context diagnostics keep their locations fresh when declarations move; unrelated edits reuse the
  cached prepared context.
- A context with any error diagnostics does not emit source. Fixing the error regenerates it on the
  next build.
- Reflection mapping reports analogous problems as `NotSupportedException` at runtime instead of
  build diagnostics.

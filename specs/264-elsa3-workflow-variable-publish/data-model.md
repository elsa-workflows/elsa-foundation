# Data model: Elsa 3 converter: workflow variables that can be published

No persisted model changes. The change is in how one Elsa 3 shape maps to one Elsa 4 shape.

## Elsa 3 variable (input, `Elsa3Variable`)

| Field | Meaning |
|---|---|
| `id`, `name` | Reference key and display name |
| `typeName` | Elsa 3 type alias or CLR type name; unresolvable falls back to `Object` |
| `isArray` | The variable holds an array of `typeName` |
| `value` | The starting value as text, or absent |
| `storageDriverTypeName` | Storage driver (unchanged by this unit) |

## Elsa 4 variable (output, `VariableDefinition`)

| Field | Before | After |
|---|---|---|
| `ReferenceKey`, `Name`, `Type`, `StorageDriverType` | unchanged | unchanged |
| `Default` when `value` is absent | `ArgumentValue(null, ExpressionType: null)` | `null` |
| `Default` when `value` converts | `ArgumentValue("<text>", ExpressionType: null)` | `ArgumentValue(<converted value as JSON>, "Literal")` |
| `value` does not convert | `ArgumentValue("<text>", null)`, then fails at publish | refused: `ELS3-ACTIVITY-VARIABLE-VALUE-UNCONVERTIBLE` |

## Starting value by type (Elsa 3's rule, R-2)

| Variable type | Elsa 3 text | Starts as |
|---|---|---|
| any | absent | absent (reads as `null`) |
| `Object` | any text, including JSON | the text |
| `String` | any text | the text |
| `Int32`, `Boolean`, other primitives, enums | `"5"`, `"true"`, `"OK"` | the converted value |
| `List<T>`, `T[]`, array flag, with `T` known to Elsa 4 | a JSON array | Elsa 4 type `T`, kind List or Array; the items converted to `T` |
| `List<T>`, `T[]` with `T` unknown (`FormFile[]`) | any, or none | refused (#3194) |
| `ObjectDictionary`, `HttpHeaders` | a JSON object | `Object` holding the object |
| any collection | text that is not a JSON array (`a, b`) | refused; the guidance shows `["a","b"]` |
| any dictionary | text that is not a JSON object | refused, with an example object |
| a scalar type | text that does not convert in strict mode (`"abc"` for `Int32`) | refused |
| a scalar type Elsa 4's registry does not know (an unregistered enum) | any text | refused |

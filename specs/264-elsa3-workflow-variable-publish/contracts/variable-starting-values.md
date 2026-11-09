# Contract: variable starting values

## Converted shape

A converted Elsa 4 `VariableDefinition` has one of two starting values:
- no starting value (`Default` is `null`), when the Elsa 3 variable has no value;
- a `Literal` starting value (`ExpressionType` is `"Literal"`), whose value is the Elsa 3 value converted to the
  variable's type by Elsa 3's rule and written as JSON.

The converter never produces a variable starting value with no expression type. This applies to workflow variables,
container variables and the variables of a reusable activity's graph manifest.

## Refusal

| Field | Value |
|---|---|
| Code | `ELS3-ACTIVITY-VARIABLE-VALUE-UNCONVERTIBLE` |
| Severity | Error: the definition is not converted |
| Path | `$.variables[i]` for a workflow variable; the container's `Variables` property for a container variable |
| Message (collection) | `Variable '{name}' has the starting value '{value}', which is not a JSON array.` Guidance: `In the Elsa 3 workflow, write the '{typeName}' starting value as a JSON array, for example {suggestion}. Elsa 4 does not split text on commas.` The suggestion is the comma-separated items as a JSON array |
| Message (resource) | `Variable '{name}' has the type '{typeName}', a resource (a file, a stream or raw bytes) Elsa 4 cannot hold yet, so it cannot be converted.` Raised with or without a starting value, for `FormFile`, `IFormFile`, `Stream` and `byte[]`, alone or as a collection element. Guidance links #3194 |
| Message (items) | `Variable '{name}' has the starting value '{value}', whose items cannot be converted to '{element}'.` |
| Message (dictionary) | `Variable '{name}' has the starting value '{value}', which is not a JSON object.` Guidance shows an example object |
| Message (value) | `Variable '{name}' has the starting value '{value}', which cannot be converted to its type '{typeName}'.` Strict conversion: a value that only converts by falling back to the type's default is refused |
| Message (type) | `Variable '{name}' has the starting value '{value}', but its type '{typeName}' is not known in Elsa 4, and the value is not JSON, so it cannot start as it did in Elsa 3.` A JSON value on such a type converts to `Object` |
| Guidance | `In the Elsa 3 workflow, change the variable's starting value to one its type accepts, or change its type to Object to keep the value as text. Elsa 3 started such a variable with its type's default or empty.` |
| Metadata | the plan's usual metadata, plus `VariableName` |

The value in the message is cut to 80 characters, like other value echoes in Elsa 3 diagnostics.

The code is stable: it is never renamed or reused.

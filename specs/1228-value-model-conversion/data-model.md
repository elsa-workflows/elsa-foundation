# Data model: Value model and conversion

## Core value (unchanged, written down)

| Field | Meaning |
|---|---|
| Type | A type reference (below), optional JSON Schema |
| Presence | Absent, explicit null, present, withheld |
| Content | Valid JSON, or a reference to content stored elsewhere |
| Protection | Sensitive, requires encryption, redaction mode, retention |

## Type reference (changed)

| Field | Meaning |
|---|---|
| Alias | A registered alias. The dynamic kind is `Any`. |
| Collection kind | None, array/list, dictionary |
| Element (new) | Optional type reference for a nested collection |

Rules: `Object`, `System.Object` and `Elsa.Any` read as `Any`. `DateOnly` and `TimeOnly` are bare aliases.
Example: `IDictionary<string, string[]>` = alias `String`, kind dictionary, element { kind array }.

## Entry record (new, per script run, not stored)

| Field | Meaning |
|---|---|
| Value key | The variable, input or argument read |
| Entered | The `JsonElement` as it entered |
| Lossy paths | The paths of numbers JavaScript cannot hold exactly, with the stored text |

## Detected change (new, per script run)

| Field | Meaning |
|---|---|
| Value key | What changed |
| Kind | Member patch, replace, whole array |
| Path | The changed path |
| New value | Core value to write |
| Lossy paths carried | Lossy entered numbers that the new value contains |

## Precision decision (new)

| Field | Meaning |
|---|---|
| Outcome | Allow or refuse |
| Diagnostics | Per lossy path: path, stored value, written value, reason, remedy; severity warning or error |

## Executable additions

| Field | Meaning |
|---|---|
| JavaScript adapter version | Pinned at publish (FR-027) |
| Accept precision loss | Per script node, default false (FR-014) |

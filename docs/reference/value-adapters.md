# Value adapters

A value crosses a boundary only through an adapter with a written contract ([ADR 0046](../adr/0046-output-binding-coercion-uses-pinned-value-representations.md) V3,
amended by [#3422](https://github.com/elsa-workflows/elsa-foundation/pull/3422)). This page is that contract for
JavaScript, activity inputs and outputs, and Liquid ([spec 1228](../../specs/1228-value-model-conversion/spec.md)
FR-027). The core value is valid JSON with a type and a presence (V1). An adapter rule never changes in place: a
change after 4.0 comes with its own plan for executables already published (FR-028). Today there is one version of each
adapter; nothing is stamped into an executable or checked at run time.

## JavaScript — JSON profile, version `json-1`

Script activities (Run JavaScript) and JavaScript input expressions use the same adapter (D-15), through the
replaceable script value bridge (`IScriptValueBridge`; default: copy, compare, patch). No .NET object, type or member
is reachable from either (D-16).

### Into JavaScript

| Core value | JavaScript value | Loss |
|---|---|---|
| Object | Plain, changeable object; key order and casing kept | none |
| Array | Real, changeable array | none |
| String (dates included) | String | none |
| Boolean, null | Boolean, null | none |
| Number whose JavaScript JSON form has the same value (`10.50` → `10.5`, `2.0` → `2`, `1e21` → `1e+21`) | Number | none |
| Integer beyond ±(2^53 − 1) or decimal with more digits than a double holds (`9007199254740993`, `0.10000000000000000000000000001`) | Number (the nearest double) | lossy; recorded when it enters |
| A number that becomes `Infinity`, or a non-zero number that becomes `0` | Number | lossy |
| Absent | `undefined` | S-2 rules |

A number is lossy when the JavaScript number, written as JSON, differs in value from the stored one (V9). The check
is exact for any length and exponent. A number held as a string (`"9007199254740993"`) is a string and never lossy.

### Out of JavaScript

| What the script did | What the bridge reports | Then |
|---|---|---|
| Read only | Unchanged | Nothing is written |
| Changed a member | Member change | The step write keeps the other members' stored text |
| Assigned a whole value | Whole assignment | The step write replaces it |
| Changed an array | Whole array | The step write replaces the array |
| Set a member equal, as a JavaScript value, to what it read | Unchanged | Nothing (ADR 0090 C2; a string forces the write) |
| Added a member JSON cannot hold (a function, a Symbol, `undefined`) | Unchanged | Nothing: JSON drops such a member, so the stored value is what it was (an input expression is not refused for it) |
| Returned a value | Result | The result, as JSON |

An entered value written unchanged, as a result or inside another value, is written from its original JSON, so it
carries no loss. Any other written number that equals, as a double, a lossy entered number carries that loss
(FR-011a). Changing `args` is local to the evaluation and never a write.

A script activity reads workflow variables through `variables` and `getVariable` over its step's variable session
(spec 1222 FR-033): each read is recorded with its version and protection, and returns the committed value plus the
step's own writes. Its changes go to the step write path of ADR 0090 (spec 1231), committed with the step; every
change meets the precision policy before any is written. Its result takes the strongest protection among the values
it read (spec 1228 US7). An input expression never writes (ADR 0083 `binding-readonly-v1`).

### Refusals and warnings

| Case | Default | Code | Diagnostic |
|---|---|---|---|
| A lossy number reaches a write | Refused by the precision policy | `VF-PREC-001` | Path, stored and written value; an integer "exceeds the largest integer a JavaScript number holds exactly (9007199254740991)", a decimal "has more digits than a JavaScript number holds"; remedy: hold the number as a string. When the step or expression read a protected value: the target and the reason only, no number and no path |
| A lossy element of an array written whole | Refused by the precision policy | `VF-PREC-001` | The array path, "written as a whole", each lossy element with both values; remedy: hold it as a string or do not change the array |
| A lossy number read but not written | One warning per evaluation, recorded on the activity execution and shown in its inspection (`Warnings`) | `VF-PREC-002` | The lossy paths and why; never a value. When the evaluation or step read a protected value, and in the log, only the roots (`variables.order`, `args`): a member below can be a key of that value |
| A lossy number passed to a host function | Not yet: the isolated engine has no host functions. When S-1 adds them: logged warning, call not refused | `VF-PREC-002` | Function and argument path |
| An input expression assigns a variable or a member of one, visibly in its source | Refused at publish | `VF-BIND-001` | Node, input, variable, path; "input expressions only read"; use a script activity |
| An input expression changes a read value in a way publication cannot see | Evaluation fails, nothing written | `VF-BIND-001` | Same; when a value it read is protected, the declared variable only, no path, and no name it computed |
| A script activity changes a value it read without assigning the variable (`variables.order.total = 1`) | Written (a partial update); one warning recorded on the activity execution | `VF-WRITE-001` | The variable and the changed paths; when a value it read is protected, the variable only. Assign the variable to make the write explicit, or copy it (`{...variables.order}`) to change a local copy |
| A script activity reads a name no visible variable has | Reads `undefined`; one warning | `VF-READ-001` | The name; when a value it read is protected, none |

The precision policy is one replaceable host-wide component (`IJavaScriptPrecisionPolicy`, D-17). Elsa ships only
`RefusingPrecisionPolicy`, with no options and no per-workflow or per-node override. A host that wants other handling
installs its own policy.

The bridge records its cost per evaluation on the `Elsa.Expressions.JavaScript` meter:
`elsa.javascript.value_bridge.enter.duration`, `elsa.javascript.value_bridge.collect.duration` (milliseconds) and
`elsa.javascript.value_bridge.bytes` (FR-010).

## Activity inputs and outputs

| Direction | Accepts | Produces | Lossless | Refused |
|---|---|---|---|---|
| Core value → .NET input | JSON whose kind matches the input type | The .NET value, through the shared serializer baseline (S-9) | Every value the type holds | A JSON kind that does not match a scalar target; text is never parsed into a boolean or a number (D-12) |
| Dynamic kind (`Any`, a script result) → a scalar input (`Boolean`, `String`, a number) | A JSON value of the matching kind, through `elsa.json@1`; an integral target only a number that fits | The same value | Always | Another kind, at run time (FR-025). An imported Elsa 3 script result is checked at import instead (`ELS3-UNTYPED-RESULT`, spec 1228 US6) |
| Dynamic kind (`Any`) → `JsonElement`, `JsonElement?`, `JsonNode` | Any valid JSON | The same JSON, no conversion step | Always | — |
| Dynamic kind → `JsonObject`, `JsonArray` | Any valid JSON at publish | The same JSON | When the shape matches | A value of another shape, at run time, with a diagnostic |
| .NET output → core value | The output's .NET value | JSON through the shared serializer baseline | Every value the type holds | A value with no JSON form |

Dates, unmatched keys and member casing follow S-9 (spec 1207);
this page adds no second rule. `DateOnly` and `TimeOnly` are stored as ISO 8601 text (`"2026-10-28"`, `"09:15:00"`).

## Liquid

Liquid reads JSON numbers as decimals and does not narrow them, so it needs no precision policy. It renders text: a
template's result is a string. A member that only a .NET object would have renders empty; no .NET member is
reachable.

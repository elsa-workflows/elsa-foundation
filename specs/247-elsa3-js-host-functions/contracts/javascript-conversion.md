# Contract: Elsa 3 JavaScript conversion (spec 247)

This table defines what each Elsa 3 JavaScript construct becomes in Elsa 4. The tests pin each row, and the Mapping
README's migration story is generated from this contract.

Every refusal is an Error, and a refused construct anywhere in a definition stops the whole definition from converting.

## Converts

| Elsa 3 | Elsa 4 | Notes |
|---|---|---|
| Top-level `return x` (expression) | `(function () {\n…\n})()` | The body runs as a function. The last statement must be a `return`, or an expression, which is prefixed with `return` |
| Top-level `return x` (RunJavaScript) | The body is unchanged apart from reads | Elsa 4 already runs a script as a function body |
| `get{Name}()`, where `{Name}` is a workflow input name exactly (standalone) or `Pascalize(input)` (reusable) | The same binding as `getInput("{input}")` | In a reusable conversion Elsa 3 declares inputs as variables, so only the Pascal-cased accessor exists (R-2) |
| `get{Name}()`, where `{Name}` is `Pascalize(variable)` exactly | The same binding as `getVariable("{variable}")` | Only variables whose name is all letters and digits get an accessor |
| `getCorrelationId()` | Parameter `elsa3CorrelationId` bound to `WorkflowInstance(correlationId)` | `null` when the instance has no correlation ID |
| `getWorkflowInstanceId()` | Parameter `elsa3WorkflowInstanceId` bound to `WorkflowInstance(instanceId)` | |
| `isNullOrEmpty(x)` | `__elsa3IsNullOrEmpty(x)`, a prelude local function: `v == null \|\| v === ""` | The argument is evaluated once |
| `isNullOrWhiteSpace(x)` | `__elsa3IsNullOrWhiteSpace(x)`: `v == null \|\| /^[\t-\r \u0085   -     　]*$/.test(v)` | The class is .NET's `char.IsWhiteSpace` set, which differs from JavaScript's `\s` |

Generated parameter names begin with `elsa3`, and prelude functions with `__elsa3`. Both are refused as ambiguous
(`VF-IMP-002`) if the script already declares the same name.

## Refused

These rows use the existing codes. Each row has its own message and its own guidance, which tells the Elsa 3 author
what to change so the workflow converts.

| Elsa 3 | Code | Guidance (Elsa 3 rewrite) |
|---|---|---|
| `get{Output}From{Activity}()` (an output accessor, which shadows all other accessors) | VF-IMP-005 | Capture the output into a variable and read it with `get{Variable}()` |
| `get{Name}()` with no matching input, variable or output | VF-IMP-005 | Declare the variable or input, or read it with `getVariable`/`getInput` |
| A generated accessor whose name equals a built-in (for example the variable `CorrelationId`) | VF-IMP-002 | Rename the variable or input |
| `set{Name}(…)`, `setVariable`, or mutating a read value | VF-IMP-005 | Use a Set Variable activity |
| `newGuid()`, `newGuidString()`, `newShortGuid()`, `getGuidString()`, `getShortGuid()` | VF-IMP-005 | For a value that is unique per run, use `getWorkflowInstanceId()`. Otherwise pass the value in as a workflow input. Elsa 4 has no random values in expressions |
| `Date` in any form, including `Date.now()` | VF-IMP-005 | Replace a `wait(ms)` loop with a Delay activity. Pass "now" and date values in as workflow inputs. Elsa 4 has no clock in expressions |
| `Math.random()`, `crypto.randomUUID()` | VF-IMP-005 | Pass the value in as a workflow input |
| `setOutcome(…)`, `setOutcomes(…)` | VF-IMP-005 | Elsa 4 scripts have no outcomes. Route with a Decision or Switch activity whose conditions carry the script's logic |
| `toJson(x)` | VF-IMP-005 | Use `JSON.stringify(x)`; to copy, use `JSON.parse(JSON.stringify(x))`. Elsa 3's `toJson` escapes HTML characters and keeps .NET casing |
| `parseGuid(s)` | VF-IMP-005 | Keep the GUID as a string, and validate its format in the script if needed |
| `bytesToString`, `bytesFromString`, `bytesToBase64`, `bytesFromBase64`, `streamToBytes`, `streamToBase64` | VF-IMP-005 | Elsa 4 values are JSON and have no bytes or streams. Move the conversion into an activity |
| `stringToBase64`, `stringFromBase64` | VF-IMP-005 | Elsa 4 expressions have no Base64 helpers. Move the conversion into an activity |
| `getConfig(…)` | VF-IMP-005 | Pass the setting in as a workflow input |
| `getWorkflowDefinitionId`, `getWorkflowDefinitionVersionId`, `getWorkflowDefinitionVersion`, `getWorkflowInstanceName`, `setWorkflowInstanceName`, `setCorrelationId`, `getLastResult` | VF-IMP-005 | Specific message per name. Elsa 4 expressions cannot read or set these |
| `DateTime`, `DateTimeOffset`, `TimeSpan`, `Guid`, `Random`, lodash `_`, `moment` | VF-IMP-005 | Unchanged from spec 236 |
| Elsa 3 `args` | VF-IMP-005 | Unchanged from spec 236 |
| `getOutputFrom` or helpers with a computed name, or an accessor used as a value | VF-IMP-006 | Unchanged from spec 236 |
| Any other call to an undeclared function | VF-IMP-005 | Unchanged: "unknown or unapproved host function" |
| A body with a top-level `return` whose last statement is neither a `return` nor an expression | VF-IMP-006 | End the script with `return` |

# Data Model: Scripts Read Variables By Name

**Spec**: [spec.md](spec.md) · Provisional (initiative #3052)

These are conceptual entities for the read model, not storage schemas. Reads add no new persisted state beyond the
existing input snapshot, which now also holds the reads recorded by the run-time fallback.

## Entities

### Pinned Variable parameter

A spec 095 declared parameter produced by publish for one resolved literal read.

| Field | Meaning |
|---|---|
| `declaringScopeNodeId` | The scope whose declaration the name resolved to (iteration / container / workflow). |
| `variableKey` | The variable's key, as declared. |
| `surface` | `variables` or `args` (default `args`). `variables` exposes it under the `variables` root (R-2 / OQ-1). |

Validation: the name MUST resolve to exactly one visible declaration at publish (else refused); the variable MUST be
durable (a transient literal read keeps its publish diagnostic).

### Fallback marker (publish)

Present on a compiled binding when publish found a computed or opaque read.

| Field | Meaning |
|---|---|
| `candidates` | The conservative read report: references (declaring scope + key) of the variables visible at that point. |

Drives: making the candidate set readable before evaluation, the rename flag, and the "possible protected read" and
"transient candidate" diagnostics. Absent on literal-only expressions, which then pay no fallback cost.

### Recorded read (run time)

One actual read served by the fallback lookup.

| Field | Meaning |
|---|---|
| `name` | The name the script asked for. |
| `declaringScopeNodeId` / `variableKey` | Where it resolved, nearest scope first; empty when absent. |
| `present` | `false` when the name matched no visible variable (the script read `undefined` / nil, a warning was recorded). |

Recorded reads are protection-checked as they happen and stored in the step's input snapshot with the pinned values;
secret values are redacted wherever a recorded read is logged.

### `variables` root (run time)

A frozen object the evaluator installs. Pinned names (`surface = variables`) return the pinned value; on a marked
expression, any other name (`variables[expr]`) goes through the fallback lookup. `getVariable(name)` is the same lookup
as a function: a literal argument was pinned at publish; a non-literal argument uses the fallback. Generated
`get<Name>()` accessors no longer exist (decision 6). Unset declared variable → JSON `null`; missing computed name →
`undefined` (JavaScript) / nil (Liquid).

### `args` root (run time)

Unchanged: pinned parameters whose `surface = args` (literals, composed expressions, inputs, activity results, instance
metadata). Separate object from `variables`, so no name collision.

### Read set (publish)

The set of literal variable names an expression reads, derived by parsing the source. Drives pinning, the publish
protection check and the shadow-change warning. Computed and opaque access is not a read-set entry; it sets the
fallback marker.

### Diagnostics

| Diagnostic | When | Severity |
|---|---|---|
| Unresolved literal name | A literal name resolves to no visible declaration (spec 1216 FR-027) | refuse at publish |
| Transient literal read | A literal name resolves to a non-durable variable | refuse at publish (retained) |
| Shadow change | A literal name now resolves to a different declaration than the previously published version | warn at publish |
| Computed read reported | An expression has computed or opaque reads; lists its candidates | info at publish |
| Possible protected read | A computed read's candidates include a protected variable | warn at publish |
| Transient candidate | A computed read's candidates include a transient variable | warn at publish |
| Missing computed name | A computed name matches no visible variable (spec 1216 FR-025/FR-026) | warn at run time; read marked absent |
| Withheld read | A read reaches a value the context may not read | fault at run time (value not provided) |
| Transient computed read | A computed name resolves to a transient variable (OQ-3) | fault at run time |
| Converter: `args` collision | A script's own `args` would collide with lowered `args` reads (#3296) | refuse at import |
| Converter: resolution differs | Elsa 4 nearest-scope resolution ≠ Elsa 3 resolution for a literal read | refuse at import |
| Converter: shadowed candidate | A computed read's candidate name is declared in more than one visible scope | warn at import |
| Converter: transient-in-Elsa-4 | A converted read targets a variable transient in Elsa 4 | flag at import |

### Protected (secret) variable

A variable whose value is redacted from logs, diagnostics and recorded reads. A literal read is protection-checked at
publish; a computed read is checked at run time for the variable actually read. A no-permission (withheld) value is not
provided to the script.

### State transitions

None. A read is pure; it produces no write (writes are ADR 0090 / S-7). Input-binding expressions read the input
snapshot and never see pending writes. The `RunJavaScript` body reads through its session view (ADR 0092), which shows
its own pending writes; its recorded reads still enter its input snapshot.

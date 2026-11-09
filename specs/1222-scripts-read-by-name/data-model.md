# Data Model: Scripts Read Variables By Name

**Spec**: [spec.md](spec.md) · Provisional (initiative #3052)

These are conceptual entities for the read model, not storage schemas. Reads add no new persisted state beyond the
existing input snapshot.

## Entities

### Pinned Variable parameter

A spec 095 declared parameter produced by publish for one resolved direct read.

| Field | Meaning |
|---|---|
| `declaringScopeNodeId` | The scope whose declaration the name resolved to (iteration / container / workflow). |
| `variableKey` | The variable's key, as declared. |
| `surface` | `variables` or `args` (default `args`). `variables` exposes it under the `variables` root (R-2 / OQ-1). |

Validation: the name MUST resolve to exactly one visible declaration at publish (else refused); the variable MUST be
durable (a transient variable read is refused).

### `variables` root (run time)

A frozen object the evaluator installs, built from pinned parameters whose `surface = variables`. One property per
resolved variable name, value = the pinned (snapshot) value. No `getVariable`/`get<Name>()` runtime functions remain
(decision 6); `getVariable('literal')` and `variables['literal']` are publish-time aliases that pin like `variables.X`.

### `args` root (run time)

Unchanged: pinned parameters whose `surface = args` (literals, composed expressions, inputs, activity results, instance
metadata). Separate object from `variables`, so no name collision.

### Read set (publish)

The set of direct variable names an expression reads, derived by parsing the source. Drives: pinning, the publish
protection check, and the shadow-change warning. Computed access is not a read-set entry — it is refused.

### Diagnostics (publish)

| Diagnostic | When | Severity |
|---|---|---|
| Unresolved name | A name resolves to no visible declaration | refuse |
| Computed name | `variables[expr]` / `getVariable(expr)` / passing the accessor as a value | refuse |
| Transient read | A name resolves to a non-durable variable | refuse |
| Shadow change | A name now resolves to a different declaration than the previously published version | warn |
| Converter: `args` collision | A script's own `args` would collide with lowered `args` reads (#3296) | refuse |
| Converter: resolution differs | Elsa 4 nearest-scope resolution ≠ Elsa 3 resolution | refuse |
| Converter: transient-in-Elsa-4 | A converted read targets a variable transient in Elsa 4 | flag at import |

### Protected (secret) variable

A variable whose value is redacted from logs/diagnostics and whose direct read is protection-checked at publish. A
no-permission (withheld) value is not provided to the script.

### State transitions

None. A read is pure; it produces no write (writes are ADR 0090 / S-7). The `RunJavaScript` body reads through its
session view (ADR 0092), which equals the pinned snapshot until writes exist.

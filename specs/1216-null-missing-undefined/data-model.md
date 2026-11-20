# Data Model: Null, missing and undefined

## Value state (existing, `ValueEnvelope.Presence`)

| State | Meaning in this spec |
|---|---|
| `Absent` | No value was supplied and none was materialized. After admission, never seen for a declared workflow input. |
| `ExplicitNull` | The value `null`, supplied or materialized. Valid for nullable targets. |
| `Present` | A value. |
| `Withheld` | Unchanged; out of scope. |

ADR 0091 decision 8: these two states are the null representation for declared members. Every store already writes
`Presence`; T017 verifies round-trips per provider. A variable or workflow-input change that the S-8 bridge writes
as JSON `null` is stored as `ExplicitNull`.

## `WorkflowStrategyOptions.NullHandling` (new, authored)

| Field | Type | Default | Meaning |
|---|---|---|---|
| `DefaultOnNull` | bool | `false` | `null` for a non-nullable target becomes the type default (`Boolean`, the number types and enums only, FR-001b). |
| `UndefinedAsNull` | bool | `false` | An `undefined` JavaScript result or property becomes `null`; a consumed script result that is `undefined` gives `null` instead of a fault. |

## `WorkflowExecutable.ValuePolicies` (new, pinned)

Same two fields. Written in the hash payload as `valuePolicies` only when at least one is `true`.

## `WorkflowDeclaredInput.IsNullable` (new, pinned)

From the input's type (Q8: value types not nullable, every other type and every collection nullable). The shared
`InputDefinition.IsNullable` property stays, because .NET activity descriptors carry it (derived from the C# type);
for authored members Studio no longer sends it and the backend derives it from the type. Written in `inputContract` only when `true`.

## Undefined policy at evaluation (no new pinned field)

The JavaScript evaluator receives the executable's pinned `ValuePolicies.UndefinedAsNull` with each evaluation (the
caller already holds the executable), and passes it to S-8's value bridge writer. No per-binding copy is pinned: the
workflow-level value is already in the hash, and a second copy would only have to agree with it.

## Admission (state transition)

`WorkflowStartDispatcher.ApplyInputContract`, added by #3731, before the start policy and enqueue. Steps marked
*S-2* are what this spec adds; the rest is on `main` already.

```text
request ──default-on-null (S-2)──▶ null for a non-nullable input with a type default → type default
        ──validate──▶ refused (blank, unknown, duplicate or mistyped name/value, missing required;
                                null for non-nullable without policy: S-2)
        └─complete──▶ every declared member: supplied | declared default | ExplicitNull (nullable, S-2) | type default (S-2)
                    ──enqueue──▶ start command ──▶ start checkpoint (seed)
```

## `Elsa3MappingOptions` (new)

| Field | Type | Default |
|---|---|---|
| `ApplyElsa3NullPolicies` | bool | `true` |

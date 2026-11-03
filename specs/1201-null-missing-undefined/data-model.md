# Data Model: Null, missing and undefined

## Value state (existing, `ValueEnvelope.Presence`)

| State | Meaning in this spec |
|---|---|
| `Absent` | No value was supplied and none was materialized. After admission, never seen for a declared workflow input. |
| `ExplicitNull` | The value `null`, supplied or materialized. Valid for nullable targets. |
| `Present` | A value. |
| `Withheld` | Unchanged; out of scope. |

ADR 0091 decision 8: these two states are the null representation for declared members. Every store already writes
`Presence`; T017 verifies round-trips per provider.

## `WorkflowStrategyOptions.NullHandling` (new, authored)

| Field | Type | Default | Meaning |
|---|---|---|---|
| `DefaultOnNull` | bool | `false` | `null` for a non-nullable target becomes the type default (value types and enums). |
| `UndefinedAsNull` | bool | `false` | An `undefined` JavaScript result or property becomes `null`; a script activity without a return gives `null`. |

## `WorkflowExecutable.ValuePolicies` (new, pinned)

Same two fields. Written in the hash payload as `valuePolicies` only when at least one is `true`.

## `WorkflowDeclaredInput.IsNullable` (new, pinned)

From `InputDefinition.IsNullable`. Written in `inputContract` only when `true`.

## `RuntimeExpressionBinding.UndefinedPolicy` (new, pinned)

`Error` (default) or `AsNull`, copied from `ValuePolicies.UndefinedAsNull` for JavaScript bindings at publication.
Hashed with the binding only when `AsNull`.

## Admission (state transition)

```text
request ──validate──▶ refused (unknown name, missing required, null for non-nullable without policy)
        └─complete──▶ every declared member: supplied | declared default | ExplicitNull (nullable) | type default
                    ──seed──▶ start checkpoint
```

## `Elsa3MappingOptions` (new)

| Field | Type | Default |
|---|---|---|
| `ApplyElsa3NullPolicies` | bool | `true` |

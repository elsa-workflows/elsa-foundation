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

## `WorkflowDeclaredInput.IsNullable` (pinned, type-derived)

From the input's type (Q8: value types not nullable, every other type and every collection nullable). The shared
`InputDefinition.IsNullable` property stays, because .NET activity descriptors carry it (derived from the C# type);
for authored members Studio no longer sends it and the backend derives it from the type. Written in `inputContract`
only when `false` (non-nullable), since most workflow inputs are nullable.

Main already carries this property and `HasExplicitNullDefault`; reconcile them after main sync rather than adding
a second field. Preserve the marker distinguishing an explicit-null declared default from no declared default.
Main's current hash writes `isNullable` when true; S-2's canonical representation writes it only when false, so
T009 must cover that encoding change and regenerate affected fixtures in place.

## Undefined policy at evaluation (no new pinned field)

The JavaScript evaluator receives the executable's pinned `ValuePolicies.UndefinedAsNull` with each evaluation and
passes it to S-8's value bridge writer. Bindings get it from the input materializer, which holds the executable;
`Run JavaScript` gets it from its `ActivityExecutionContext`, on which the runtime puts the executable's value
policies. No per-binding copy is pinned: the
workflow-level value is already in the hash, and a second copy would only have to agree with it.

## Admission (state transition)

`WorkflowStartDispatcher.ApplyInputContract`, added by #3731, before the start policy and enqueue. Steps marked
*S-2* are what this spec adds; the rest is on `main` already.

```text
request ──validate names / required presence──▶ refused when invalid or required input absent
        └─complete──▶ supplied | declared default | ExplicitNull (nullable, S-2) | type default (S-2)
                    ──default-on-null (S-2)──▶ normalized non-nullable null → supported type default
                    ──validate null / type──▶ refused if still incompatible
                    ──enqueue──▶ start command ──▶ start checkpoint (seed)
```

The policy-aware validator overload receives the executable's `WorkflowValuePolicies`. Substitution sees declared
defaults as well as supplied values. Known-inputs mode permits unresolved required inputs and omits null/type-default
completion, while keeping declared-default completion and the same policy/null/type checks.

## `Elsa3MappingOptions` (new)

| Field | Type | Default |
|---|---|---|
| `ApplyElsa3NullPolicies` | bool | `true` |

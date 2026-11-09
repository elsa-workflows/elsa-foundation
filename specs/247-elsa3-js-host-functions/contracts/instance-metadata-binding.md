# Contract: workflow-instance metadata parameter binding (spec 247)

A new canonical expression parameter binding kind in `Elsa.Expressions.Core`. It amends ADR 0045 (§2 and §5) and the
spec 095 expression contract, which listed exactly four kinds.

## Wire shape

```json
{ "kind": "WorkflowInstance", "member": "correlationId" }
```

| Field | Values | Rule |
|---|---|---|
| `kind` | `"WorkflowInstance"` | Polymorphic discriminator, beside `Literal`, `WorkflowRequest`, `Variable` and `ActivityResult` |
| `member` | `"correlationId"` or `"instanceId"` | Ordinal and case-sensitive. Any other value is rejected when constructed and when deserialized |

## Resolution (runtime)

| Member | Value | When absent |
|---|---|---|
| `correlationId` | The latest committed value of the identity durable value `identity:correlationId`, seeded at start from the dispatch payload and updated by `SetCorrelationId` | JSON `null` |
| `instanceId` | `WorkflowExecutionId` | Never absent |

## Guarantees

- **Declared, not ambient.** An expression sees the value only through its declared parameter (`args.<name>`). The
  binding-pure-v1 profile and its flags are unchanged (ADR 0083).
- **Committed state only.** The value is read from committed durable values, never from live memory. Re-materializing
  an input snapshot (replay of a coalesced segment, a `While` pass) therefore sees the committed value, the same as
  every other durable read.
- **Correlation ID changes are visible downstream.** After a `SetCorrelationId` intrinsic commits, activities whose
  inputs are materialized later see the new value. Input snapshots already pinned are not re-evaluated, which is the
  existing rule for all bindings.
- **Hashed.** The binding participates in the executable hash and the expression fingerprint, as every binding does.
- **Dependency policy.** The binding is not sensitive and adds no redaction policy.

## Compatibility

A runtime build that predates this kind cannot deserialize an executable that contains it. Elsa 4 is pre-GA and
supports no mixed-version deployment or rollback across this change. The ADR 0045 amendment records this.

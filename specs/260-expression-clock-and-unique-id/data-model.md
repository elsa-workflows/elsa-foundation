# Data model: Clock and UniqueId parameters and binding-pure-v2

## ExpressionParameterBinding (polymorphic, existing)

Discriminator `kind`. Existing members: `Literal`, `WorkflowRequest`, `Variable`, `ActivityResult`, `WorkflowInstance`.
This feature adds two more.

| Kind | Record | Fields | Value bound | Policy |
|---|---|---|---|---|
| `Clock` | `ClockExpressionParameterBinding` | none | ISO 8601 UTC string, millisecond precision, equal to the evaluation instant | none, not sensitive |
| `UniqueId` | `UniqueIdExpressionParameterBinding` | none | lowercase hyphenated UUID version 7, new per parameter, timestamped with the evaluation instant | none, not sensitive |

Validation: neither record accepts extra members; unknown JSON members are ignored, as for the other kinds.
Fingerprint: the records take part through the existing generic fingerprint, so a definition that uses them differs
from one that does not, and existing fingerprints are unchanged.

## RuntimeInputBindingResolutionContext (existing)

New optional field `EvaluatedAt : DateTimeOffset?`.

| Producer | Value |
|---|---|
| `RuntimeActivityInputMaterializer.MaterializeSnapshotAsync` | `materializedAt` (equal to `ActivityInputSnapshot.MaterializedAt`) |
| `WorkflowIntrinsicExecutor.MaterializeValueAsync` | one `TimeProvider.GetUtcNow()` read before evaluation |
| any other constructor | `null` — resolving `Clock` or `UniqueId` fails (`VF-EXP-CLOCK-001`) |

## Capability profile (existing string)

| Profile | Languages | Grant | Sandbox |
|---|---|---|---|
| `binding-pure-v1` | JavaScript, Liquid | binding-pure | `Date`, `Temporal`, `Intl`, `Math.random` undefined |
| `binding-pure-v2` | JavaScript only | binding-pure | v1 plus the clockless `Date` ([contract](contracts/clock-uniqueid-and-binding-pure-v2.md)) |

Stamping: plain-source JavaScript is stamped `binding-pure-v2` at publish; full portable definitions keep the profile
they declare; Liquid and `GraphActivity` mappings keep `binding-pure-v1`; stored executables keep their stamp.

## Lifecycle of a value

1. **Materialize**: the evaluation instant is fixed, and each `Clock` and `UniqueId` parameter is resolved from it.
2. **Evaluate**: the expression result becomes an input value (or an intrinsic result).
3. **Commit**: the snapshot (`ActivityStarted`), the `While` pass decision with `ParentCompletionProcessedWorkItemId`,
   or the intrinsic completion commits. From here on, every reader sees the committed value.
4. **Lost before commit**: a replay re-runs steps 1 to 3 with new values. Nothing from the lost attempt was observable.

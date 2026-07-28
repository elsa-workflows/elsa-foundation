# Data Model: Reusable Activity Boundary Outcomes

## Authored boundary outcome mapping

Stored only in `elsa.activity-graph` manifest schema 2.

| Field | Type | Rule |
|---|---|---|
| `sourceOutcomeReferenceKey` | non-empty string | Identifies an emitted outcome on the resolved direct-entry contract; unique |
| `boundaryOutcomeReferenceKey` | non-empty string | Identifies an emitted public reusable outcome; target reuse is allowed |

The set is total over emitted public boundary outcomes. Each emitted target has at least one mapping, and distinct sources may converge on the same target.

## Compiled runtime outcome mapping

Stored in the graph runtime descriptor.

| Field | Type | Rule |
|---|---|---|
| `sourceOutcomeName` | non-empty string | Runtime name resolved from the entry dependency contract |
| `boundaryOutcomeName` | non-empty string | Runtime name resolved from the reusable activity contract |

Old runtime descriptor payloads omit the collection and retain implicit `done` behavior.

## Published authoring outcome port

Stored in the `elsa.outcomes` design facet payload.

| Field | Type | Rule |
|---|---|---|
| `referenceKey` | non-empty string | Stable design identity copied from the emitted outcome contract |
| `name` | non-empty string | Emitted public boundary outcome name |
| `type` | literal `"outcome"` | Existing generic Studio port discriminator |

## State transitions

1. Author declares schema-2 public outcomes and mappings.
2. Provider validates the manifest and exact entry dependency contract.
3. Compiler resolves references to names and pins runtime mappings.
4. Child completes with one outcome name.
5. Boundary selects and retains exactly one public outcome name.
6. Completion checkpoint commits that name.
7. Parent routes only the matching connection.

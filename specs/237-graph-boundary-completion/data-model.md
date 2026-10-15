# Data Model: Graph Boundary Completion From Any Depth

## Complete activity (catalog, Graph Runtime)

| Field | Value |
|---|---|
| Type key | `Elsa.Activities.Graph.Runtime.Activities.Complete` |
| Display name | `Complete` |
| Kind | Structural activity with no child slots |
| Input | `outcome`: string, required, literal only. It names a boundary outcome **reference key** of the enclosing reusable activity. |
| Outcomes | None declared. Its own completion never routes. |
| Side-effect profile | Default (not ReplaySafe): it stages a durable notification, so re-running it is not indistinguishable from running it once |

**State transitions** (its own activity execution): `Running`, then `Completed`. That happens in the same commit
that stages the ancestor notification. If no enclosing boundary is found, or `outcome` is blank, the execution goes
from `Running` to `Faulted`.

## Ancestor notification request (runtime core, new)

| Field | Meaning |
|---|---|
| `AncestorConsumerKey` | The consumer key of the nearest ancestor to address, e.g. `elsa.graph-activity`. |
| `Code` | The notification code, e.g. `elsa.graph.complete`. |
| `Payload` | String key/value payload, e.g. `{ "outcome": "<reference key>" }`. |

**Resolution**:
1. The runtime walks `ParentActivityExecutionId` from the notifier's parent upward, nearest first.
2. The target is the first ancestor whose executable node descriptor has `ConsumerKey == AncestorConsumerKey`.
3. The runtime emits the existing `NotifyParentActivity` command to that target.
4. Ordering: the work item is enqueued **before** the notifier's own completion intent.
5. If no ancestor matches, the notifier faults with `runtime.ancestor-notification.target-not-found`.

## Graph activity descriptor (runtime, extended)

New optional field:

| Field | Type | Meaning |
|---|---|---|
| `CompleteOutcomes` | list of `{ ReferenceKey, BoundaryOutcomeName }` | The boundary outcomes that `Complete` nodes in this body may select, keyed by reference key. It is empty when the body has no `Complete` node. |

**Validation on load**: reference keys must be unique and every `BoundaryOutcomeName` must be non-blank. The JSON
property is omitted when the list is empty, which keeps existing payloads byte-identical.

## Graph boundary execution (behaviour, extended)

| Trigger | Today | After this spec |
|---|---|---|
| Entry completes; mappings exist | Exactly one mapped outcome, or throw | Unchanged |
| Entry completes; no mappings; no `CompleteOutcomes` | `Done` | Unchanged |
| Entry completes; no mappings; `CompleteOutcomes` not empty | n/a | Fault `activity.graph.entry-completed-without-mapping` |
| `elsa.graph.complete` notification; key known | n/a | Select the mapped name, cancel the entry subtree durably, `Complete(name)` with outputs captured in the same commit |
| `elsa.graph.complete` notification; key unknown | n/a | Fault `activity.graph.complete.outcome-unknown` |
| Any work item after the boundary has stopped `Running` | Dropped | Dropped. This is how the first committed exit wins. |

## Activity graph manifest, schema 2 (design, relaxed rules only)

There is no shape change. The validation rules are:

- **Changed**: an emitted boundary outcome is *reachable* when at least one `outcomeMappings` entry **or** at least
  one `Complete` node in the body names its reference key. This relaxes `activity.graph.outcome-mapping-required`.
- **New**: every `Complete` node's `outcome` input is a single literal string
  (`activity.graph.complete.outcome-literal-required`).
- **New**: every `Complete` node names an emitted boundary outcome (`activity.graph.complete.outcome-unknown`).
- **Schema 1, new**: any `Complete` node is rejected (`activity.graph.complete.schema-unsupported`).

**Compiled boundary payload, schema 2**: gains `completeOutcomes` only when the body has at least one `Complete`
node.

## Workflow definition draft (design, new rule)

- `CompleteOutsideGraphDraftValidator` returns a `ValidationError` with type `Graph/CompleteOutsideGraph` for each
  `Complete` node in a workflow definition draft. Promotion is blocked while the error exists.

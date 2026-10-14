# Contract: the `Complete` activity in an activity-graph body

This is the contract that emitters (Studio, spec 236's converter, REST clients) write against.

## Authored node

```json
{
  "nodeId": "exit-fail",
  "activityVersionId": "<catalog version id of Elsa.Activities.Graph.Runtime.Activities.Complete>",
  "inputs": [
    {
      "referenceKey": "outcome",
      "value": { "value": "fail", "expressionType": "Literal" }
    }
  ]
}
```

- **Type key**: `Elsa.Activities.Graph.Runtime.Activities.Complete`. Resolve the version id from the activity
  catalog, as for any CLR activity.
- **Input key**: `outcome`.
- **Value**: exactly one literal string, the **reference key** of an emitted public outcome in the reusable
  activity's contract (`contract.outcomes[].referenceKey`). It is not the outcome's display name. Expressions,
  arrays and blank values are rejected.
- **Placement**: anywhere in the body of a schema-2 activity graph, at any depth, in any child slot of any
  composite. A `Complete` node may also be the root of the body.
- **Connections**: the node has no outcome ports, and nothing connected after it ever runs.

## Contract outcomes and mappings, schema 2

- Every emitted boundary outcome must be reachable through at least one `outcomeMappings` entry (natural completion
  of the entry) **or** at least one `Complete` node.
- A graph may have no `outcomeMappings` at all when every outcome is reached through `Complete` nodes. If its body
  then ends naturally without reaching a `Complete` node, the boundary faults at run time with
  `activity.graph.entry-completed-without-mapping`.

## Run-time semantics

- **Target**: the nearest enclosing reusable activity (graph boundary) completes with the chosen outcome.
  Intermediate composites, including loops and Parallel, are bypassed. A nested reusable activity is its own nearest
  boundary.
- **Remaining body**: every other live execution in the body is cancelled durably in the same commit. Its
  bookmarks, durable timers and queued work are deleted.
- **Outputs**: public outputs come from the existing `outputMappings`, evaluated at the moment of exit. A missing
  required output faults the completion.
- **Several exits**: the first committed exit wins. Later exits, and a later natural completion, are ignored.
- **`Break`**: unchanged and unrelated.

## Diagnostics

| Code | When |
|---|---|
| `activity.graph.complete.outcome-literal-required` | Publish: `outcome` is not one literal string |
| `activity.graph.complete.outcome-unknown` | Publish: names no emitted boundary outcome. Run time: the descriptor has no such key (safety net). |
| `activity.graph.complete.schema-unsupported` | Publish: `Complete` in a schema-1 body |
| `activity.graph.outcome-mapping-required` | Publish: an emitted outcome is reached by neither a mapping nor a `Complete` node |
| `graph.complete.outside-graph` | Workflow draft validation and promotion: `Complete` in a plain workflow |
| `activity.graph.complete.outcome-required` | Run time: blank `outcome` |
| `runtime.ancestor-notification.target-not-found` | Run time: no enclosing graph boundary |
| `activity.graph.entry-completed-without-mapping` | Run time: natural completion with no mapping, in a body whose outcomes come only from `Complete` |

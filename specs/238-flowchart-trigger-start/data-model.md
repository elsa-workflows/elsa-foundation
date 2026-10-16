# Data Model: Flowchart Starts at the Trigger That Fired

This unit adds no persisted entity, field, metadata key or schema version (FR-007). It reads values that already
exist and changes two in-memory computations.

The placement revision (D8–D13) also adds no persisted data. It adds three in-memory pieces:
- the runtime contract `IStructuralStartEntryProvider` (research R10);
- the `Placement` facet;
- composite node id and structure kind properties on `WorkflowTriggerPreflightException`.

A placement is computed from the published executable alone: the root-to-trigger path, each composite's structure
kind, and its provider's answer.

## Values read

| Value | Where it lives | Used for |
|---|---|---|
| Trigger node id | Run seed state (`runtime.triggerNodeId`), exposed as `IRuntimeActivityExecutionContext.TriggerNodeId` | Choosing the first node (R1) |
| Trigger marker | `ExecutableNode.Metadata["executionType"] == "Trigger"`, stamped at publish | Root order for loop-edge classification (R2) |
| `startNodeId`, `connections` | Flowchart structure payload (draft and executable) | Entry points at design time (R3) and the fallback start at runtime |

## Flowchart execution state

`FlowchartExecutionState` (schema version 1) is unchanged. The only difference is which node id the root scope's
`ownerNodeId` and the root path's `currentNodeId` hold when the state is first created: the node the run started
at, not always the start node. Both fields already accept any direct child id. Runs that are already in flight keep
their persisted state and are not affected.

## In-memory design-time model

`FlowchartControlFlow` (private to `ValueFlowValidator`):

| Member | Today | After |
|---|---|---|
| Roots | `startNodeId` | `EntryNodeIds`: `startNodeId` plus direct children that no connection targets |
| `nodeIds` | Edge endpoints plus the start node | Edge endpoints plus every entry |
| `Dominates(candidate, node)` | Single-root dominators | Multi-root dominators over a virtual root (R4) |
| `EntriesReaching(node)` | none | Entries from which `node` is reachable (R5) |

### Rules

- An entry's dominator set is `{entry}`.
- `Dominates` is false when either node is unreachable from every entry, the same as today with one root.
- Two writes are exclusive when both `EntriesReaching` sets are non-empty and disjoint.

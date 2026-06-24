# Data Model: Workflow Definition Test Runs

## Workflow Test Run

Represents one designer-initiated attempt to run an editable workflow definition version.

| Field | Description | Validation |
|---|---|---|
| `TestRunId` | Unique identity for the test run | Required, non-empty |
| `DefinitionId` | Source workflow definition identity | Required, non-empty |
| `DefinitionVersionId` | Source workflow definition version identity captured at request time | Required, non-empty |
| `ArtifactId` | Transient runnable artifact identity prepared for the run, when compilation succeeds | Required for accepted/dispatch-attempted runs |
| `WorkflowExecutionId` | Runtime execution identity, when dispatch succeeds | Required when dispatch status is accepted |
| `Status` | Test-run status | `Rejected`, `DispatchAccepted`, `DispatchRejected`, or `DispatchDeferred` |
| `Reason` | User-actionable rejection/defer reason | Required for rejected/deferred statuses |
| `RequestedBy` | Actor or surface that requested the test run | Required, non-empty |
| `RequestedAt` | Request timestamp | Required |
| `ExpiresAt` | Retention boundary for the transient runnable artifact | Required for accepted/dispatch-attempted runs |
| `Metadata` | Correlation metadata for designer/runtime diagnostics | Optional key-value snapshot |

### State transitions

```text
Requested
  ├─ compile validation failure ─> Rejected
  └─ compiled transient artifact
       ├─ runtime dispatch accepted ─> DispatchAccepted
       ├─ runtime dispatch rejected ─> DispatchRejected
       └─ runtime dispatch deferred ─> DispatchDeferred
```

## Transient Runnable Artifact

Runtime-owned executable artifact prepared for a designer test run.

| Field | Description | Validation |
|---|---|---|
| `Identity` | Runtime executable identity | Required |
| `RootActivity` | Compiled executable root activity | Required |
| `CreatedAt` | Artifact creation time | Required |
| `ExpiresAt` | Time after which it cannot be used for new starts | Required |
| `Scope` | Artifact visibility/scope | Must be test/transient |
| `CompatibilityMetadata` | Runtime metadata including source and test-run correlation | Required snapshot |

## Source Workflow Snapshot

The source workflow version read by the compile bridge.

| Field | Description | Validation |
|---|---|---|
| `DefinitionId` | Source definition identity | Required |
| `DefinitionVersionId` | Source version identity | Required |
| `Version` | Source version label | Required |
| `RootActivity` | Authored root activity | Required for accepted test runs |

## Relationships

- One workflow definition version can have many workflow test runs.
- One workflow test run prepares one transient runnable artifact when compilation succeeds.
- One accepted workflow test run dispatches at most one workflow execution.
- A transient runnable artifact references its source workflow version, but Runtime execution must not load that source to run.

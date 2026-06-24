# Implementation Plan: Activity Execution Inspection

**Branch**: `sfmskywalker-activity-executions-design` | **Date**: 2026-06-25 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/079-activity-execution-inspection/spec.md`

## Summary

Add checkpoint-gated activity execution inspection to Workflow Runtime. Each concrete activity execution becomes inspectable through a runtime-owned projection keyed by activity execution identity, committed with the same runtime checkpoint as the lifecycle state it explains. Scheduler-boundary transitions become mandatory checkpoint barriers that enqueue dependent scheduler work only through post-commit scheduler intents.

The design keeps continuation/lifecycle state, inspection projections, runtime history, and Studio visualization separate. Runtime remains Design-free; Studio and other consumers join runtime execution evidence to authored layout by `AuthoredActivityId`.

## Technical Context

**Language/Version**: C# / .NET `net10.0`

**Primary Dependencies**: Existing Workflows Runtime Core scheduler/checkpoint services, runtime stores, Groundwork document store, FastEndpoints API surface, mediator handlers

**Storage**: Existing runtime state stores plus a new runtime-owned activity execution inspection projection store; Groundwork persistence adds a document kind/index for inspection projections

**Testing**: xUnit unit tests for scheduler-boundary checkpoint behavior, projection accumulation/commit behavior, API handlers/endpoints, Groundwork persistence, and Flowchart provenance propagation

**Target Platform**: Elsa Server and Workflows Runtime packages in the foundation workspace

**Project Type**: Runtime core contract/model/store/API enhancement with scheduler persistence refactor

**Performance Goals**: Instance detail summaries should remain lightweight by avoiding eager value-snapshot hydration; per-execution details should load only for the selected execution

**Constraints**: Runtime packages must not reference Workflows.Design. Activity execution inspection must reflect committed checkpoints only. Scheduler-boundary checkpoints must not be skipped by optional persistence policy. Value payload capture must obey runtime payload capture decisions.

**Scale/Scope**: One workflow execution inspection at a time; first slice handles current-instance activity execution detail and does not implement cross-workflow chain traversal or full workflow time-travel

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Workflows.Design/Runtime split (§E2.2)**: PASS. The feature is runtime-owned and does not require Runtime to read design documents.
- **Artifact-only runtime (§E2.6.2)**: PASS. Execution and inspection evidence remain derived from runtime artifacts and runtime state, not design documents.
- **Triplet separation (§E2.9)**: PASS. `WorkflowDefinitionState`, `WorkflowExecutable`, runtime lifecycle state, inspection projections, and Studio visualization remain separate contracts.
- **Feature/API registration tests (§2.23.1)**: PASS with work required. Any new runtime API feature registration or store registration must have focused registration tests.
- **Implementation tests (§2.23.2)**: PASS with work required. Scheduler handlers, checkpoint assembly, stores, projection mapping, payload capture decisions, and API handlers require branch-covered unit tests.

Initial gate status: **PASS**. No violations requiring Complexity Tracking.

## Design Decisions

1. **Canonical term**: Use `Activity execution` for one concrete invocation of an executable activity node. Avoid `ActivityRun`, `ActivityAttempt`, and `ExecutionFrame` for this concept.
2. **Projection split**: Keep `ActivityExecutionState` focused on lifecycle/continuation state and add an `ActivityExecutionInspectionProjection` for read evidence.
3. **Checkpoint-driven evidence**: Inspection evidence is committed only through `RuntimeCheckpointCommit`. If a checkpoint is skipped, its inspection evidence is not durable.
4. **Scheduler-boundary checkpoints**: Scheduling, starting, suspending, completing, faulting, cancelling, and recovering activity executions are mandatory durability barriers before dependent scheduler work advances.
5. **Post-commit advancement**: Dependent scheduler work is enqueued using `RuntimePostCommitIntentKinds.EnqueueSchedulerWork`, not direct queue writes after durable state changes.
6. **Typed provenance**: Add a small runtime-owned scheduling provenance shape with structural parent, temporal scheduler, branch/iteration, execution path/scope, scheduling cause, and metadata.
7. **Value capture**: Inspection value snapshots default to no payload or metadata-only; payloads appear only when runtime payload capture policy allows them.
8. **Current projection**: Store one current inspection projection per activity execution with checkpoint metadata, not a versioned snapshot per checkpoint.
9. **History events**: Runtime history remains auxiliary observability; inspection projection changes are direct checkpoint state changes.
10. **ADR**: Record the checkpoint-gated scheduler-boundary decision because it deliberately replaces direct state writes/direct queue writes in several handlers.

## Project Structure

### Documentation (this feature)

```text
specs/079-activity-execution-inspection/
├── spec.md
├── plan.md
├── data-model.md
├── contracts/
│   └── activity-execution-inspection.md
└── checklists/
    └── requirements.md

docs/adr/
└── 0001-checkpoint-gated-activity-execution-inspection.md
```

### Source Code (repository root)

```text
src/Elsa/Workflows/Runtime/Core/
├── Contracts/
│   ├── IActivityExecutionInspectionStore.cs
│   └── IRuntimeActivityExecutionInspectionAccumulator.cs
├── Models/
│   ├── ActivityExecutionState.cs
│   ├── ActivityExecutionInspectionProjection.cs
│   ├── ActivityExecutionInspectionValueSnapshot.cs
│   ├── ActivitySchedulingProvenance.cs
│   ├── RuntimeCheckpointCommit.cs
│   ├── RuntimeChildActivityScheduleRequest.cs
│   └── RuntimeScheduleActivityCommandPayload.cs
└── Services/
    ├── WorkflowScheduleActivitySchedulerWorkHandler.cs
    ├── WorkflowStartActivitySchedulerWorkHandler.cs
    ├── WorkflowCompleteActivitySchedulerWorkHandler.cs
    ├── WorkflowCreateBookmarkSchedulerWorkHandler.cs
    ├── RuntimeCheckpointCommitter.cs
    └── InMemoryActivityExecutionInspectionStore.cs

src/Elsa/Activities/Runtime/Services/
├── WorkflowInvokeActivitySchedulerWorkHandler.cs
├── WorkflowResumeBookmarkSchedulerWorkHandler.cs
└── ActivityFaultIncidentRecorder.cs

src/Elsa/Activities/Flowchart/Internal/
└── FlowchartExecutionEngine.cs

src/Elsa/Workflows/Runtime/Api/
├── Endpoints/GetActivityExecution.cs
├── Handlers/GetActivityExecutionRequestHandler.cs
├── Models/WorkflowExecutionViews.cs
└── Requests/GetActivityExecution.cs

src/Elsa/Persistence/Groundwork/
├── ElsaRuntimeStorageManifest.cs
└── Stores/GroundworkActivityExecutionInspectionStore.cs

tests/Elsa/Workflows/Runtime/Tests/
tests/Elsa/Activities/Runtime/Tests/
tests/Elsa/Activities/Flowchart/Tests/
tests/Elsa/Persistence/Groundwork/Tests/
```

**Structure Decision**: Keep activity execution inspection in Workflows Runtime Core and Runtime API. Flowchart contributes typed generic provenance through runtime scheduling contracts; Studio and design layout remain consumers outside Runtime.

## Technical Design

### Runtime model

Add `ActivitySchedulingProvenance`:

- `ParentActivityExecutionId`
- `SchedulingActivityExecutionId`
- `SchedulingWorkflowExecutionId`
- `BranchId`
- `IterationId`
- `ExecutionPathId`
- `ExecutionScopeId`
- `SchedulingCause`
- `Metadata`

Update `ActivityExecutionState` to carry the minimal provenance needed for lifecycle summaries and correlation. Keep detailed value snapshots and summaries out of lifecycle state.

Add `ActivityExecutionInspectionProjection`:

- identity: workflow execution, activity execution, executable node, authored activity, activity type/version
- lifecycle: status, substatus, execution sequence, timestamps
- checkpoint: first/last checkpoint id, last committed at
- provenance: typed scheduling provenance
- evidence: outcome names, bookmark summaries, incident summaries, value snapshots
- metadata: policy and diagnostic metadata safe for inspection

Add `ActivityExecutionInspectionValueSnapshot`:

- subject: activity input or activity output
- value name, type descriptor, capture mode, captured at
- payload when allowed by policy
- metadata and redaction/capture reason

### Checkpoint change set

Extend `RuntimeCheckpointStateChangeSet` with an `ActivityExecutionInspections` lane:

```text
IReadOnlyCollection<RuntimeStateChange<ActivityExecutionInspectionProjection>> ActivityExecutionInspections
```

Validate state-change ids against `ActivityExecutionId`.

### Scheduler-boundary checkpoints

Refactor scheduler handlers so lifecycle transitions that create or advance durable activity execution state are committed before dependent scheduler work:

- `ScheduleActivity`: checkpoint upserts scheduled state + initial inspection projection; post-commit intent enqueues `StartActivity`.
- `StartActivity`: checkpoint upserts running state + inspection update; post-commit intent enqueues `InvokeActivity`.
- `InvokeActivity` normal completion: checkpoint upserts completed state, durable outputs, inspection update, and downstream scheduler intents.
- `CreateBookmark`: existing bookmark checkpoint gains inspection projection update.
- Fault handling: existing incident checkpoint gains faulted state and inspection projection update.
- Cancellation/recovery paths: add inspection projection updates at their scheduler-boundary checkpoints.

Direct state writes/direct queue writes may remain only for pre-checkpoint intake paths that do not advance durable lifecycle state.

### Pending inspection accumulator

Introduce a runtime-owned pending inspection accumulator to collect observed evidence during activity invocation. It flushes to the checkpoint change set only when a checkpoint is assembled. Evidence includes:

- materialized input metadata and policy-governed payload snapshots
- recorded output metadata and policy-governed payload snapshots
- outcome names
- bookmark summaries
- incident summaries
- scheduling provenance and lifecycle timestamps

### API contract

Keep workflow instance details lightweight by returning activity execution summaries.

Add per-execution detail:

```text
GET /runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}
```

The endpoint returns one committed activity execution inspection projection or not found.

### Consumer update for spec 077

Workflow instance inspection should consume activity execution summaries for graph/timeline aggregation and lazy-load the detail endpoint when a user selects a concrete activity execution.

Graph nodes with repeated executions should show aggregate count and highest-severity/latest-terminal status summary, not a single ambiguous node status.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| None | N/A | N/A |

# Feature Specification: Runtime Checkpoint Slot Decomposition (ADR 0029 Move 2, first slice)

**Feature Branch**: `claude/move2-checkpoint-slot-cancel`

**Created**: 2026-07-02

**Status**: Draft — establishes the Move 2 decomposition pattern; **seeks architect approval of the pattern before the remaining handlers**.

**Input**: Begin ADR 0029 **Move 2** — relocate handlers' inlined phases into their named pipeline slots, per the **slot-invoked handler model** pinned in the [ADR 0029 addendum](../../docs/adr/0029-runtime-execution-flows-through-the-pipelines.md#addendum-2026-07-02-move-2-handler-invocation-model--slot-invoked-handlers). This first slice extracts the shared **Checkpoint** phase (the uniform `RuntimeCheckpointCommitter.CommitAsync` tail) into the workflow pipeline's `Checkpoint` slot, proven on the single simplest handler (`WorkflowCancelSchedulerWorkHandler`). Behavior-preserving.

## Context

Move 1 (spec 082) made the pipeline the live execution spine but left every phase inlined in the scheduler work handlers, which run as the pipeline **terminal**; the built-in slots are pass-throughs. Move 2 moves that behavior into the slots so a slot's name reflects where its behavior runs.

**Handler-invocation model (ADR 0029 addendum).** In the decomposed pipeline a handler runs **inside a core `Invoke` slot** (before-`next`), not as the terminal. The workflow pipeline — which had only `LoadState → Scheduling → Checkpoint → PostCommit` — gains an `Invoke` slot (`LoadState(100) → Invoke(150) → Scheduling(200) → Checkpoint(300) → PostCommit(400)`), symmetric with the activity pipeline. A built-in `Invoke`-slot middleware (`RuntimeWorkflowInvokeMiddleware`) invokes the work item's selected handler (staged by the dispatcher on the workspace); the pipeline **terminal becomes a no-op**.

**Handlers opt into context-awareness incrementally.** A migrated handler additionally implements `IRuntimePipelineWorkHandler` (`HandleAsync(workItem, IRuntimePipelineContext, ct)`) — the context is threaded **explicitly** (no ambient/AsyncLocal accessor). It **stages** its assembled `RuntimeCheckpointCommit` on the per-dispatch `RuntimePipelineWorkspace`; the `Checkpoint` slot commits it in the **before-`next`** direction, so `PostCommit` naturally follows the commit. A handler that has not migrated keeps only `IWorkflowSchedulerWorkHandler` and runs unchanged via its plain method; direct (no-pipeline) dispatch commits inline exactly as before.

For this slice only `WorkflowCancelSchedulerWorkHandler` is migrated. Every other workflow handler still runs its plain path via the `Invoke` slot (behavior-preserving); the activity pipeline is untouched (its handlers still run as the terminal and adopt the model when an activity handler is first migrated).

This is accepted-ADR implementation under the [Runtime Execution Seam](../../docs/program-goals/runtime-execution-seam.md) bucket; sizing in [pipeline wiring sizing](../../docs/reports/runtime-execution-pipeline-wiring-sizing.md).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - The Checkpoint slot persists a handler-staged commit (Priority: P1)

A workflow-pipeline handler assembles its checkpoint commit and stages it; the `Checkpoint` slot performs the actual commit, so checkpoint persistence lives in its named slot (where atomic-commit folding will eventually live) rather than inline in the handler.

**Why this priority**: This is the point of the slice — behavior moves into its slot. It also establishes the Move 2 pattern (workspace + `Invoke` slot + `IRuntimePipelineWorkHandler`) for the remaining handlers.

**Independent Test**: Stage a commit on a workflow context and invoke the Checkpoint middleware; assert it persisted the commit; assert it no-ops when nothing is staged.

**Acceptance Scenarios**:

1. **Given** a workflow context whose workspace has a pending checkpoint commit, **When** the `Checkpoint` middleware runs, **Then** it commits it via the committer.
2. **Given** a workflow context with no pending commit, **When** the `Checkpoint` middleware runs, **Then** it does nothing.

### User Story 2 - Cancel commits through the Checkpoint slot, unchanged in effect (Priority: P1)

A workflow cancellation dispatched through the pipeline produces exactly the same persisted result as before, but the commit is performed by the `Checkpoint` slot rather than inline in the Cancel handler.

**Why this priority**: Behavior preservation is the hard constraint; the migrated handler must be observably identical.

**Independent Test**: Dispatch a Cancel work item through the feature-composed pipeline and assert the same checkpoint commit (workflow → Cancelled) is persisted; assert the standalone (no-pipeline) handler path still commits directly.

**Acceptance Scenarios**:

1. **Given** dispatch through the pipeline, **When** the Cancel handler's context-aware method runs in the `Invoke` slot, **Then** it stages the commit (does not commit inline) and the staged commit reflects the cancellation.
2. **Given** direct dispatch (the plain handler method), **When** the Cancel handler runs, **Then** it commits inline exactly as before.
3. **Given** a Cancel work item dispatched through the feature pipeline, **When** it runs, **Then** the checkpoint commit (workflow Cancelled) is persisted via the `Checkpoint` slot.

### Edge Cases

- **Handler throws before staging**: nothing is staged; the `Checkpoint` slot commits nothing (matches inline behavior where a throw precedes the commit).
- **Un-migrated handlers**: other workflow-pipeline handlers (Start, Checkpoint, CompleteActivity-routing) stage nothing, so the `Checkpoint` slot is a no-op for them and they commit as before — no double commit, no extra I/O.
- **Commit throws**: propagates through the pipeline to the drainer's existing per-item fault handling, exactly as an inline commit throw would.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A mutable per-dispatch `RuntimePipelineWorkspace` MUST be carried on both pipeline contexts, exposing the staged handler invocation and a stageable pending checkpoint commit. The context records otherwise remain immutable.
- **FR-002**: The workflow pipeline MUST gain a core `Invoke` slot (order 150, between `LoadState` and `Scheduling`) whose built-in middleware runs the work item's selected handler (staged by the dispatcher) in the before-`next` direction; the pipeline terminal MUST be a no-op for the workflow pipeline.
- **FR-003**: Handlers MUST opt into pipeline-awareness via `IRuntimePipelineWorkHandler` (context threaded explicitly, no ambient accessor). The dispatcher MUST invoke the context-aware method for a migrated handler and the plain `IWorkflowSchedulerWorkHandler` method otherwise.
- **FR-004**: The workflow `Checkpoint` slot (`RuntimeWorkflowCheckpointMiddleware`) MUST commit the workspace's pending checkpoint commit in the before-`next` direction when present, and do nothing when absent.
- **FR-005**: `WorkflowCancelSchedulerWorkHandler` MUST implement `IRuntimePipelineWorkHandler` and stage its assembled commit on the passed context; its plain `HandleAsync` MUST commit inline (unchanged) for direct dispatch.
- **FR-006**: The change MUST be behavior-preserving: a Cancel dispatched through the pipeline persists the same commit as before; every other workflow handler runs unchanged via the `Invoke` slot; the activity pipeline is untouched; existing tests pass.
- **FR-007**: No `Elsa.Workflows.Design.*` dependency introduced (§E2.2/§E2.6). `RuntimePipelineContractTests` MUST pass (updated for the new workflow `Invoke` slot). Only the Cancel handler's body is changed; no other handler is touched.

### Key Entities

- **RuntimePipelineWorkspace**: mutable side-channel on the context; holds the staged handler invocation and the pending checkpoint commit (extensible to staged state/intents in later slices).
- **IRuntimePipelineContext**: kind-agnostic surface (work item + workspace) both contexts implement.
- **IRuntimePipelineWorkHandler**: opt-in context-aware handler method invoked by the `Invoke` slot (explicit context, no ambient state).
- **RuntimeWorkflowInvokeMiddleware**: the workflow `Invoke` slot; runs the staged handler before-`next`.
- **RuntimeWorkflowCheckpointMiddleware**: the `Checkpoint` slot; commits the staged commit before-`next`.

## Success Criteria *(mandatory)*

- **SC-001**: A Cancel dispatched through the feature pipeline persists exactly the same checkpoint commit (workflow Cancelled) as the pre-Move-2 inline path.
- **SC-002**: 100% of the pre-existing runtime suite passes unchanged, including `RuntimePipelineContractTests` and the existing Cancel handler tests.
- **SC-003**: Un-migrated workflow-pipeline handlers perform no extra commit and no extra I/O (the `Checkpoint` slot no-ops for them).
- **SC-004**: No new Runtime→Design dependency.

## Assumptions

- **Slot-invoked handlers, explicit context (ADR 0029 addendum).** Handlers run in the `Invoke` slot and receive the context explicitly via `IRuntimePipelineWorkHandler` — no ambient accessor. Opt-in per handler keeps migration incremental (no big-bang signature change across ~11 handlers); the plain `IWorkflowSchedulerWorkHandler` path is retained for direct dispatch and un-migrated handlers.
- **Checkpoint-slot-first over LoadState-first.** The uniform `CommitAsync` tail is a clean shared extraction; `LoadState`-first would need an eager-vs-lazy loading policy to avoid redundant I/O for un-migrated handlers. Checkpoint-first avoids that.
- **Workflow pipeline gains an `Invoke` slot.** The slot contract change is justified in the ADR addendum; the activity pipeline already has an `Invoke` slot and is untouched this slice (its handlers still run as the terminal until an activity handler is migrated).
- Only Cancel is migrated. The remaining handlers, `LoadState`/`Scheduling`/`PostCommit` slot behavior, and the Move 2 hazards (atomic checkpoint-commit folding #310, transactional fault arms, control-leaf intents #260/#308, container scope-completion capture #210/ADR 0027, inspection toggle) are out of scope for this slice and untouched.

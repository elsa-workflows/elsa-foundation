# Feature Specification: Runtime Checkpoint Slot Decomposition (ADR 0029 Move 2, first slice)

**Feature Branch**: `claude/move2-checkpoint-slot-cancel`

**Created**: 2026-07-02

**Status**: Draft — establishes the Move 2 decomposition pattern; **seeks architect approval of the pattern before the remaining handlers**.

**Input**: Begin ADR 0029 **Move 2** — relocate handlers' inlined phases into their named pipeline slots. This first slice extracts the shared **Checkpoint** phase (the uniform `RuntimeCheckpointCommitter.CommitAsync` tail) into the workflow pipeline's `Checkpoint` slot, proven on the single simplest handler (`WorkflowCancelSchedulerWorkHandler`). Behavior-preserving.

## Context

Move 1 (spec 082) made the pipeline the live execution spine but left every phase inlined in the scheduler work handlers — the built-in slots are pass-throughs. Move 2 moves that behavior into the slots so a slot's name reflects where its behavior runs.

The **Checkpoint** phase is the cleanest first shared extraction: `WorkflowCancelSchedulerWorkHandler` and `WorkflowCheckpointSchedulerWorkHandler` (and others) all end by assembling a `RuntimeCheckpointCommit` and calling `_checkpointCommitter.CommitAsync(commit)`. This slice moves the *commit call* into `RuntimeWorkflowCheckpointMiddleware` (the `Checkpoint` slot), for Cancel only.

**Structural constraint that shapes the design.** The pipeline invokes the selected handler as a *bare terminal delegate* (`() => handler.HandleAsync(workItem, ct)`) — the handler receives no pipeline context by parameter. So for the handler to hand its assembled commit to the Checkpoint slot, we need a bridge. This slice introduces an ambient `IRuntimePipelineContextAccessor` (AsyncLocal), mirroring the existing `IWorkflowExecutionAmbientServicesAccessor`, plus a mutable per-dispatch `RuntimePipelineWorkspace` carried on the context.

**Transitional shape (documented, deliberate).** Because the handler is still the terminal (innermost), the Checkpoint middleware commits the staged commit in its **after-`next`** unwind. This is correct and behavior-preserving now (the only after-`next` work is this commit; `PostCommit` is still a placeholder). When a later slice moves a handler's commit *assembly* out of the terminal into the `Invoke` slot, the commit can flip to the forward (before-`next`) direction so `PostCommit` naturally follows it. This slice does **not** move assembly.

This is accepted-ADR implementation under the [Runtime Execution Seam](../../docs/program-goals/runtime-execution-seam.md) bucket; sizing in [pipeline wiring sizing](../../docs/reports/runtime-execution-pipeline-wiring-sizing.md).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - The Checkpoint slot persists a handler-staged commit (Priority: P1)

A workflow-pipeline handler assembles its checkpoint commit and stages it; the `Checkpoint` slot performs the actual commit, so checkpoint persistence lives in its named slot (where atomic-commit folding will eventually live) rather than inline in the handler.

**Why this priority**: This is the point of the slice — behavior moves into its slot. It also establishes the Move 2 pattern (workspace + ambient context accessor) for the remaining handlers.

**Independent Test**: Stage a commit on a workflow context and invoke the Checkpoint middleware; assert it persisted the commit; assert it no-ops when nothing is staged.

**Acceptance Scenarios**:

1. **Given** a workflow context whose workspace has a pending checkpoint commit, **When** the `Checkpoint` middleware runs, **Then** it commits it via the committer.
2. **Given** a workflow context with no pending commit, **When** the `Checkpoint` middleware runs, **Then** it does nothing.

### User Story 2 - Cancel commits through the Checkpoint slot, unchanged in effect (Priority: P1)

A workflow cancellation dispatched through the pipeline produces exactly the same persisted result as before, but the commit is performed by the `Checkpoint` slot rather than inline in the Cancel handler.

**Why this priority**: Behavior preservation is the hard constraint; the migrated handler must be observably identical.

**Independent Test**: Dispatch a Cancel work item through the feature-composed pipeline and assert the same checkpoint commit (workflow → Cancelled) is persisted; assert the standalone (no-pipeline) handler path still commits directly.

**Acceptance Scenarios**:

1. **Given** an ambient pipeline context, **When** the Cancel handler runs, **Then** it stages the commit (does not commit inline) and the staged commit reflects the cancellation.
2. **Given** no ambient pipeline context (handler used directly), **When** the Cancel handler runs, **Then** it commits inline exactly as before.
3. **Given** a Cancel work item dispatched through the feature pipeline, **When** it runs, **Then** the checkpoint commit (workflow Cancelled) is persisted via the `Checkpoint` slot.

### Edge Cases

- **Handler throws before staging**: nothing is staged; the `Checkpoint` slot commits nothing (matches inline behavior where a throw precedes the commit).
- **Un-migrated handlers**: other workflow-pipeline handlers (Start, Checkpoint, CompleteActivity-routing) stage nothing, so the `Checkpoint` slot is a no-op for them and they commit as before — no double commit, no extra I/O.
- **Commit throws**: propagates through the pipeline to the drainer's existing per-item fault handling, exactly as an inline commit throw would.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A mutable per-dispatch `RuntimePipelineWorkspace` MUST be carried on both pipeline contexts, exposing a stageable pending checkpoint commit. The context records otherwise remain immutable.
- **FR-002**: An ambient `IRuntimePipelineContextAccessor` (AsyncLocal) MUST expose the running dispatch's context; the dispatcher MUST push the context for the duration of the pipeline invocation (including the terminal handler) and pop it after.
- **FR-003**: The workflow `Checkpoint` slot (`RuntimeWorkflowCheckpointMiddleware`) MUST, after invoking the rest of the pipeline, commit the workspace's pending checkpoint commit when present, and do nothing when absent.
- **FR-004**: `WorkflowCancelSchedulerWorkHandler` MUST stage its assembled commit on the ambient context when one is present, and MUST commit inline (unchanged) when none is present.
- **FR-005**: The change MUST be behavior-preserving: a Cancel dispatched through the pipeline persists the same commit as before; un-migrated handlers are unaffected; existing Cancel tests pass unchanged.
- **FR-006**: No `Elsa.Workflows.Design.*` dependency introduced (§E2.2/§E2.6). The `RuntimePipelineContractTests` slot contract MUST still pass. Only the Cancel handler's body is changed; no other handler is touched.

### Key Entities

- **RuntimePipelineWorkspace**: mutable side-channel on the context; holds the pending checkpoint commit (extensible to staged state/intents in later slices).
- **IRuntimePipelineContext**: kind-agnostic surface (work item + workspace) both contexts implement.
- **IRuntimePipelineContextAccessor**: AsyncLocal ambient access to the running dispatch's context; the terminal-handler bridge.
- **RuntimeWorkflowCheckpointMiddleware**: the now-real `Checkpoint` slot; commits the staged commit.

## Success Criteria *(mandatory)*

- **SC-001**: A Cancel dispatched through the feature pipeline persists exactly the same checkpoint commit (workflow Cancelled) as the pre-Move-2 inline path.
- **SC-002**: 100% of the pre-existing runtime suite passes unchanged, including `RuntimePipelineContractTests` and the existing Cancel handler tests.
- **SC-003**: Un-migrated workflow-pipeline handlers perform no extra commit and no extra I/O (the `Checkpoint` slot no-ops for them).
- **SC-004**: No new Runtime→Design dependency.

## Assumptions

- **Ambient accessor over interface change (design fork).** The terminal handler reaches the context via an AsyncLocal accessor rather than changing `IWorkflowSchedulerWorkHandler.HandleAsync` to take the context. Rationale: least-invasive for the transition (no big-bang signature change across ~11 handlers + all their tests), and it mirrors the existing `IWorkflowExecutionAmbientServicesAccessor`. **Open for architect review** — the alternative (thread the context/workspace explicitly through the handler signature) is cleaner long-term and could be adopted once most phases have moved out of the terminals.
- **Checkpoint-slot-first over LoadState-first.** The uniform `CommitAsync` tail is a clean shared extraction; `LoadState`-first would need an eager-vs-lazy loading policy to avoid redundant I/O for un-migrated handlers. Checkpoint-first avoids that.
- **Transitional after-`next` commit** (see Context) is intentional for this slice and flips to before-`next` once assembly also moves out of the terminal.
- Only Cancel is migrated. The remaining handlers, `LoadState`/`Scheduling`/`PostCommit` slot behavior, and the Move 2 hazards (atomic checkpoint-commit folding #310, transactional fault arms, control-leaf intents #260/#308, container scope-completion capture #210/ADR 0027, inspection toggle) are out of scope for this slice and untouched.

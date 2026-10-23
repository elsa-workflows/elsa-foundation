# Implementation Plan: Runtime Checkpoint Slot Decomposition (ADR 0029 Move 2, first slice)

**Branch**: `claude/move2-checkpoint-slot-cancel` | **Date**: 2026-07-02 | **Spec**: [spec.md](spec.md)

## Summary

Adopt the **slot-invoked handler model** (ADR 0029 addendum): the workflow pipeline gains a core `Invoke` slot whose built-in middleware runs the selected handler before-`next` (terminal becomes a no-op). Handlers opt into context-awareness via `IRuntimePipelineWorkHandler` (explicit context, no ambient accessor). Migrate the single simplest handler (Cancel) to stage its checkpoint commit in the `Invoke` slot; the `Checkpoint` slot commits it before-`next`. Behavior-preserving; one handler.

## Constitution Check

- **§E2.2 / §E2.6**: PASS — all new types Runtime-owned; operate over runtime state + the work item; no Design types.
- **§2.23 (focused unit tests)**: Met — middleware/staging/end-to-end tests.
- **§2.21 (preserve tests)**: Met — existing Cancel tests + `RuntimePipelineContractTests` unchanged and passing; the Move-1 dispatch-test provider gains the committer registration the now-real Checkpoint middleware needs.

## Design

### New (Core)
- `Models/RuntimePipelineWorkspace.cs` — mutable workspace: `Func<IRuntimePipelineContext, ValueTask>? InvokeHandler` (staged handler) + `RuntimeCheckpointCommit? PendingCheckpointCommit`.
- `Contracts/IRuntimePipelineContext.cs` — `WorkItem` + `Workspace`; both context records implement it.
- `Contracts/IRuntimePipelineWorkHandler.cs` — opt-in context-aware handler method (`HandleAsync(workItem, IRuntimePipelineContext, ct)`).
- `Middleware/RuntimeWorkflowInvokeMiddleware.cs` — workflow `Invoke` slot: `if (workspace.InvokeHandler is {} invoke) await invoke(context); await next(context);`.
- `Middleware/RuntimeWorkflowCheckpointMiddleware.cs` — real `Checkpoint` slot: `if (workspace.PendingCheckpointCommit is {} c) await committer.CommitAsync(c); await next(context);` (removed from the placeholders file).

### Changed
- `Constants/RuntimeWorkflowPipelineSlots.cs` — add `Invoke(150)` between `LoadState(100)` and `Scheduling(200)`.
- `Builders/WorkflowRuntimePipelineBuilder.cs` — register the `Invoke` built-in.
- `Models/RuntimePipelineContexts.cs` — implement `IRuntimePipelineContext`; add `Workspace { get; init; } = new()`.
- `Services/RuntimeExecutionPipelineDispatcher.cs` — workflow: stage the handler invocation (aware vs plain) on the workspace, no-op terminal; activity: unchanged (terminal). No accessor.
- `Services/WorkflowCancelSchedulerWorkHandler.cs` — implement `IRuntimePipelineWorkHandler`; factor `BuildCommitAsync`; plain path commits, aware path stages.
- `Api/WorkflowsRuntimeApiFeature.cs` — register `RuntimeWorkflowInvokeMiddleware`; remove the (now-deleted) accessor registration.

### Removed
- `IRuntimePipelineContextAccessor` + `AsyncLocalRuntimePipelineContextAccessor` — superseded by explicit context threading (the earlier draft of this slice used them; the ADR addendum replaced them).

### Why these choices (see spec Assumptions + ADR addendum)
- **Slot-invoked handler, explicit context** — clean before-`next` order; no ambient state; incremental via the opt-in interface.
- **Checkpoint-slot-first** (not LoadState-first) — clean uniform tail; avoids the eager-vs-lazy load policy LoadState-first would need.

## Sequencing after this slice (Move 2 remainder)
Extract the shared `LoadState` slot; then migrate handlers easiest→hardest: Cancel (done) → Start → Checkpoint → CreateBookmark → ResumeBookmark → ParentCompletion → **InvokeActivity last**. Each is its own separately-approved change and carries the hazards (atomic checkpoint-commit folding #310, fault arms, control-leaf intents #260/#308, container scope-completion capture #210/ADR 0027, inspection toggle).

## Complexity Tracking

Adds a slot to the workflow pipeline's locked contract (`Invoke`) — justified in the ADR 0029 addendum. `RuntimePipelineContractTests` passes unchanged (it derives its expected built-in slots from `RuntimeWorkflowPipelineSlots.All`); an explicit `Invoke`-before-`Checkpoint` ordering test and a fail-loud-when-`Invoke`-missing test lock the new behavior. The handler-invocation model (handler runs in the `Invoke` slot, not as the terminal) is the architecturally-significant decision; it is pinned in the ADR addendum and proven behavior-preserving here (runtime suite 542/542; every workflow handler runs unchanged via the `Invoke` slot; activity pipeline untouched).

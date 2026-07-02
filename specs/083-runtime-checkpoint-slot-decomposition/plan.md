# Implementation Plan: Runtime Checkpoint Slot Decomposition (ADR 0029 Move 2, first slice)

**Branch**: `claude/move2-checkpoint-slot-cancel` | **Date**: 2026-07-02 | **Spec**: [spec.md](spec.md)

## Summary

Extract the shared **Checkpoint** phase (`CommitAsync`) into the workflow pipeline's `Checkpoint` slot, proven on the single simplest handler (Cancel). Introduce the Move 2 foundation — a mutable per-dispatch workspace on the context + an ambient context accessor so the terminal handler can stage its commit — and make `RuntimeWorkflowCheckpointMiddleware` real. Behavior-preserving; one handler.

## Constitution Check

- **§E2.2 / §E2.6**: PASS — all new types Runtime-owned; operate over runtime state + the work item; no Design types.
- **§2.23 (focused unit tests)**: Met — middleware/staging/end-to-end tests.
- **§2.21 (preserve tests)**: Met — existing Cancel tests + `RuntimePipelineContractTests` unchanged and passing; the Move-1 dispatch-test provider gains the committer registration the now-real Checkpoint middleware needs.

## Design

### New (Core)
- `Models/RuntimePipelineWorkspace.cs` — mutable workspace: `RuntimeCheckpointCommit? PendingCheckpointCommit`.
- `Contracts/IRuntimePipelineContext.cs` — `WorkItem` + `Workspace`; both context records implement it.
- `Contracts/IRuntimePipelineContextAccessor.cs` + `Services/AsyncLocalRuntimePipelineContextAccessor.cs` (+ `Noop`) — AsyncLocal ambient bridge (mirrors `IWorkflowExecutionAmbientServicesAccessor`).
- `Middleware/RuntimeWorkflowCheckpointMiddleware.cs` — real `Checkpoint` slot: `await next(context); if (workspace.PendingCheckpointCommit is {} c) await committer.CommitAsync(c);` (removed from the placeholders file).

### Changed
- `Models/RuntimePipelineContexts.cs` — implement `IRuntimePipelineContext`; add `Workspace { get; init; } = new()`.
- `Services/RuntimeExecutionPipelineDispatcher.cs` — optional `IRuntimePipelineContextAccessor` (Noop default); push the context around `InvokeAsync`.
- `Services/WorkflowCancelSchedulerWorkHandler.cs` — optional accessor param; stage the commit when a context is ambient, else commit inline.
- `Api/WorkflowsRuntimeApiFeature.cs` — register `IRuntimePipelineContextAccessor`. (The committer and the Checkpoint middleware type are already registered; DI now injects the committer into the middleware and the accessor into the dispatcher/Cancel handler.)

### Why these choices (see spec Assumptions)
- **Ambient accessor** (not a handler-signature change) — least-invasive transition; mirrors existing precedent; flagged for architect review.
- **Checkpoint-slot-first** (not LoadState-first) — clean uniform tail; avoids the eager-vs-lazy load policy LoadState-first would need.
- **After-`next` commit** — transitional while the handler is still the terminal; flips to before-`next` once assembly moves to `Invoke`.

## Sequencing after this slice (Move 2 remainder)
Extract the shared `LoadState` slot; then migrate handlers easiest→hardest: Cancel (done) → Start → Checkpoint → CreateBookmark → ResumeBookmark → ParentCompletion → **InvokeActivity last**. Each is its own separately-approved change and carries the hazards (atomic checkpoint-commit folding #310, fault arms, control-leaf intents #260/#308, container scope-completion capture #210/ADR 0027, inspection toggle).

## Complexity Tracking

The ambient AsyncLocal accessor is a deliberate transitional bridge (the handler is a bare terminal delegate and cannot receive the context by parameter). It is the one architecturally-significant decision in this slice and is surfaced for review; the alternative (explicit context threading through the handler signature) is recorded in the spec.

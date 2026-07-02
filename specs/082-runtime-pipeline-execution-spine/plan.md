# Implementation Plan: Runtime Pipeline Execution Spine (ADR 0029 Move 1)

**Branch**: `claude/confident-grothendieck-c5bbe6` | **Date**: 2026-07-02 | **Spec**: [spec.md](spec.md)

**Input**: [spec.md](spec.md); accepted decision [ADR 0029](../../docs/adr/0029-runtime-execution-flows-through-the-pipelines.md); sizing [pipeline wiring sizing](../../docs/reports/runtime-execution-pipeline-wiring-sizing.md).

## Summary

Wrap the scheduler's single handler-dispatch point with the appropriate runtime pipeline so registered middleware actually runs, keeping execution byte-for-byte identical (Option B "wrap, not replace"). Add a pipeline executor (delegate-chain composer), a work-item→pipeline-kind selector, and a thin dispatcher seam; refine the pipeline context to carry the originating work item with optional loaded state; register everything in the runtime feature; and add a guardrail test proving a registered middleware runs on a real dispatch.

## Technical Context

**Language/Version**: C# / .NET (Elsa Foundation runtime libraries).

**Primary Dependencies**: `Microsoft.Extensions.DependencyInjection`; existing runtime contract types under `Elsa.Workflows.Runtime.Core`.

**Storage**: N/A for Move 1 — the executor performs no I/O. Existing in-memory runtime stores are used only by handlers, unchanged.

**Testing**: xUnit. New unit tests under `tests/Elsa/Workflows/Runtime/Tests`.

**Project Type**: Library (Workflows Runtime core).

**Performance Goals**: Added cost is a single delegate chain over no-op slots per work item; expected negligible. No extra I/O.

**Constraints**: Behavior-preserving (byte-for-byte identical execution outcomes with only placeholder middleware); no `Elsa.Workflows.Design.*` dependency; handlers untouched; scheduler unchanged.

**Scale/Scope**: One dispatch seam + three small services + one context refinement + DI wiring + one guardrail test + a selector unit test. No handler decomposition (Move 2).

## Constitution Check

*GATE: Runtime Execution Seam constraints (constitution §E2.2 / §E2.6) + framework testing gates (§2.21 / §2.23).*

- **§E2.2 (no Runtime→Design dependency)**: PASS — all new types live in `Elsa.Workflows.Runtime.Core` and operate over runtime state + the work item. No Design types referenced.
- **§E2.6 (artifact-only runtime)**: PASS — the executor operates over runtime state and the work item; it never reads Design-side data.
- **§2.23 (focused unit tests for new logic)**: Met — selector unit test + dispatcher guardrail test.
- **§2.21 (preserve existing tests)**: Met — `RuntimePipelineContractTests` and all scheduler/execution tests unchanged and passing; the drainer dependency is optional so existing direct-construction tests are unaffected.
- No new constitution gate required (ADR 0029 §"Constitution alignment").

## Design

### Components (all under `src/Elsa/Workflows/Runtime/Core`)

1. **Pipeline executor** — `Contracts/IRuntimeWorkflowExecutionPipeline` + `Contracts/IRuntimeActivityExecutionPipeline`, implemented by `Services/RuntimeWorkflowExecutionPipeline` + `Services/RuntimeActivityExecutionPipeline`.
   - Constructed from a `RuntimePipelinePlan` and the resolved middleware instances (ordered per the plan's steps).
   - `InvokeAsync(context, terminal)` folds the ordered middleware right-to-left into a delegate chain and invokes it; the innermost `next` is the `terminal` delegate. With only pass-through placeholders, `InvokeAsync` reduces to a direct call of `terminal` — the behavior-preservation guarantee.
   - Exposes the `Plan` for inspectability (ADR "Ordering and inspectability").

2. **Pipeline selector** — `Contracts/IRuntimeSchedulerPipelineSelector`, implemented by `Services/RuntimeSchedulerPipelineSelector`.
   - `Select(RuntimeSchedulerWorkItem) → RuntimePipelineKind`.
   - Mapping (matches the handlers' `CanHandle` discriminator):
     - Workflow: `Start`, `Checkpoint`, `Cancel`, and `CompleteActivity` whose payload `CompletionKind != ParentCompletionEvaluation` (or absent/malformed payload — mirrors `WorkflowCompleteActivitySchedulerWorkHandler.CanHandle`).
     - Activity: `ScheduleActivity`, `StartActivity`, `InvokeActivity`, `ResumeBookmark`, `CreateBookmark`, and `CompleteActivity` whose payload `CompletionKind == ParentCompletionEvaluation` (mirrors `WorkflowParentActivityCompletionSchedulerWorkHandler.CanHandle`).
     - Any other kind that reaches the drainer (`RunSchedulerWork`, `ContinueVolatileWait`, `Pause`/`Unpause`, `DeliverSignal`, `GeneratedEvent`): default to **workflow** (lifecycle) deterministically; selection never throws.
   - `CompleteActivity` payload deserialization uses the same tolerant try/catch as the handlers; on failure it returns workflow (the routing default), never throwing.

3. **Pipeline dispatcher** — `Contracts/IRuntimeExecutionPipelineDispatcher`, implemented by `Services/RuntimeExecutionPipelineDispatcher`.
   - `DispatchAsync(workItem, handler, ct)`: `var kind = selector.Select(workItem)`; build the matching context from the work item; `return kind == Activity ? activityPipeline.InvokeAsync(activityCtx, _ => handler.HandleAsync(workItem, ct)) : workflowPipeline.InvokeAsync(workflowCtx, _ => handler.HandleAsync(workItem, ct))`.
   - This is the only place that knows both the selector and the two pipelines, keeping the drainer change tiny.

4. **Context refinement** — `Models/RuntimePipelineContexts.cs`.
   - Refine to carry the originating `RuntimeSchedulerWorkItem` (always available, zero I/O) and make the typed-state fields optional:
     - `WorkflowRuntimePipelineContext(RuntimeSchedulerWorkItem WorkItem, WorkflowExecutionState? WorkflowExecution = null, SchedulerState? Scheduler = null)`
     - `ActivityRuntimePipelineContext(RuntimeSchedulerWorkItem WorkItem, WorkflowExecutionState? WorkflowExecution = null, ActivityExecutionState? ActivityExecution = null, SchedulerState? Scheduler = null)`
   - Rationale (FR-004): no full state is loaded at the dispatch point; `Start` runs before its `WorkflowExecutionState` exists; deriving `ActivityExecutionState` would require per-kind payload parsing = handler-internal duplication. State population is the `LoadState` slot's job in Move 2. The contract test constrains only the middleware **parameter type**, so this refinement keeps `RuntimePipelineContractTests` green.

### Drainer wrapping (`Services/WorkflowSchedulerDrainer`)

- Add an **optional** `IRuntimeExecutionPipelineDispatcher?` constructor parameter (last, defaulted `null`) via one new overload; keep all existing constructors delegating.
- In `DispatchAsync`, replace the single line `await handler.HandleAsync(workItem, cancellationToken);` with:
  - if dispatcher present: `await _pipelineDispatcher.DispatchAsync(workItem, handler, cancellationToken);`
  - else: `await handler.HandleAsync(workItem, cancellationToken);`
- Everything else (fault capture, result construction, terminal-status checks) is unchanged. Existing tests that construct the drainer without a dispatcher exercise the else-branch → byte-for-byte identical.

### DI registration (`src/Elsa/Workflows/Runtime/Api/WorkflowsRuntimeApiFeature.cs`)

- Register each built-in placeholder middleware type as a singleton (so the executor can resolve them by type from the plan).
- Register the two pipeline plans + pipeline implementations (built from `new WorkflowRuntimePipelineBuilder().BuildPlan()` / `new ActivityRuntimePipelineBuilder().BuildPlan()` and resolving middleware from the provider).
- Register `IRuntimeSchedulerPipelineSelector` and `IRuntimeExecutionPipelineDispatcher`.
- Pass the resolved `IRuntimeExecutionPipelineDispatcher` into the `WorkflowSchedulerDrainer` factory so production dispatch flows through the pipeline.

## Project Structure

### Documentation (this feature)

```text
specs/082-runtime-pipeline-execution-spine/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── pipeline-execution.md
├── checklists/requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/Elsa/Workflows/Runtime/Core/
├── Contracts/
│   ├── IRuntimeWorkflowExecutionPipeline.cs      # new
│   ├── IRuntimeActivityExecutionPipeline.cs       # new
│   ├── IRuntimeSchedulerPipelineSelector.cs       # new
│   └── IRuntimeExecutionPipelineDispatcher.cs     # new
├── Services/
│   ├── RuntimeWorkflowExecutionPipeline.cs        # new
│   ├── RuntimeActivityExecutionPipeline.cs        # new
│   ├── RuntimeSchedulerPipelineSelector.cs        # new
│   ├── RuntimeExecutionPipelineDispatcher.cs      # new
│   └── WorkflowSchedulerDrainer.cs                # +optional dispatcher param, wrap dispatch
├── Models/
│   └── RuntimePipelineContexts.cs                 # refine (work item + optional state)

src/Elsa/Workflows/Runtime/Api/
└── WorkflowsRuntimeApiFeature.cs                  # register pipelines/middleware/selector/dispatcher

tests/Elsa/Workflows/Runtime/Tests/
├── RuntimeExecutionPipelineDispatchTests.cs       # new — guardrail + selector unit tests
└── RuntimePipelineContractTests.cs                # unchanged, must still pass
```

**Structure Decision**: Single library feature. New execution-spine services join the existing scheduler services under `Workflows/Runtime/Core`; DI wiring lives in the existing `WorkflowsRuntimeApiFeature` that already constructs the drainer.

## Complexity Tracking

No constitution violations. One deliberate deviation from "match the context shapes exactly": the context is refined to carry the work item and optional state (documented in FR-004 / Assumptions), because the non-nullable state shape is infeasible to satisfy at the dispatch point without extra I/O and handler-internal payload parsing. This is the minimal change that keeps the executor behavior-preserving and the contract test green.

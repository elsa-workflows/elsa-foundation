# Tasks: Runtime Pipeline Execution Spine (ADR 0029 Move 1)

**Spec**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md)

Ordering: contracts → context → executor → selector → dispatcher → drainer seam → DI → tests → docs. `[P]` = parallelizable with siblings.

## Phase 1 — Contracts & context

- [ ] **T001** Refine `Models/RuntimePipelineContexts.cs`: add `RuntimeSchedulerWorkItem WorkItem` and make `WorkflowExecution` / `ActivityExecution` / `Scheduler` optional. (FR-004)
- [ ] **T002 [P]** Add `Contracts/IRuntimeWorkflowExecutionPipeline.cs` + `Contracts/IRuntimeActivityExecutionPipeline.cs` (`Plan` + `InvokeAsync(context, terminal)`). (FR-001)
- [ ] **T003 [P]** Add `Contracts/IRuntimeSchedulerPipelineSelector.cs`. (FR-003)
- [ ] **T004 [P]** Add `Contracts/IRuntimeExecutionPipelineDispatcher.cs`. (FR-002)

## Phase 2 — Services

- [ ] **T005** Implement `Services/RuntimeWorkflowExecutionPipeline.cs` + `Services/RuntimeActivityExecutionPipeline.cs`: fold `Plan.Steps` middleware (resolved by type) right-to-left over the terminal delegate. (FR-001, FR-006)
- [ ] **T006** Implement `Services/RuntimeSchedulerPipelineSelector.cs` per the selection table; tolerant `CompleteActivity` payload read; never throw. (FR-003, SC-003)
- [ ] **T007** Implement `Services/RuntimeExecutionPipelineDispatcher.cs`: select → build context → invoke pipeline with handler terminal. (FR-002)

## Phase 3 — Drainer seam

- [ ] **T008** `Services/WorkflowSchedulerDrainer.cs`: add optional `IRuntimeExecutionPipelineDispatcher?` ctor param (new overload, defaulted null; existing ctors delegate) and wrap the single `handler.HandleAsync` call. (FR-002, FR-007)

## Phase 4 — DI

- [ ] **T009** `Api/WorkflowsRuntimeApiFeature.cs`: register built-in middleware types, both pipeline plans + implementations, selector, dispatcher; pass dispatcher into the drainer factory. (FR-005)

## Phase 5 — Tests (guardrail + selector)

- [ ] **T010** Add `tests/.../RuntimeExecutionPipelineDispatchTests.cs`:
  - Guardrail: marker middleware + recording handler → dispatch real work item via drainer wired with the dispatcher → assert marker ran AND handler ran (activity + workflow). (FR-008, SC-001)
  - Short-circuit: middleware that skips `next` → handler does NOT run. (User Story 1 AS-3)
  - Selector unit: each command kind + both `CompleteActivity` completion kinds → expected pipeline. (FR-003, SC-003)
- [ ] **T011** Run `RuntimePipelineContractTests` + full runtime suite; confirm unchanged/green (behavior-preserving). (FR-006, FR-010, SC-002)

## Phase 6 — Docs & tracking

- [ ] **T012** Update AGENTS.md SPECKIT pointer → `specs/082-runtime-pipeline-execution-spine/plan.md`.
- [ ] **T013** Log completion into [Runtime Execution Seam](../../docs/program-goals/runtime-execution-seam.md) (Move 1 done; Move 2 remains, InvokeActivity last).

## Phase 7 — Module contribution DX (folded into Move 1 during review; FR-011..FR-014)

- [ ] **T014** Cleanups from the review: dedup pipeline runners into `RuntimeExecutionPipelineCore`; selector reads only the `CompletionKind` discriminator (no full deserialize).
- [ ] **T015** Add `RuntimeMiddlewareAttribute`, `Workflow/ActivityRuntimeMiddlewareContribution`, and `AddWorkflow/ActivityRuntimeMiddleware<T>` extensions (attribute default + explicit override; missing slot throws).
- [ ] **T016** Builder: non-generic `Use(Type,…)` + interface validation; `Replace`/`Remove`; `BuildPlan()` deterministic sort + collision error (built-in-aware).
- [ ] **T017** Feature: apply DI contributions to the builder before composing; log resolved plan at Debug.
- [ ] **T018** Tests: attribute/override/missing-placement, end-to-end DI dispatch through the feature, deterministic order, collision + built-in collision, Replace/Remove, interface validation. Update the earlier guardrail tests to place markers at a non-zero order (order 0 now collides with the built-in by design).
- [ ] **T019** Amend ADR 0029 + spec 082 (spec/plan/data-model/contracts) + bucket note to record the contribution DX and deterministic-ordering decision.

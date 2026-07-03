# Tasks: Runtime Structure — ADR 0029 Move 2 Remainder + Drain-Path De-ambienting (W12)

**Spec**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md)

Continues [spec 083](../083-runtime-checkpoint-slot-decomposition/tasks.md) (first slice, Cancel — done/#366). Delivered as ordered internal slices under one draft PR; full baseline at slices 4, 8, and final.

## RT-4 — Core-owned composition root
- [x] **T001** `RuntimeCoreServiceCollectionExtensions.AddWorkflowRuntimeCore(IServiceCollection)` holds the full runtime registration set (`TryAdd*` throughout). (FR-005)
- [x] **T002** `WorkflowsRuntimeApiFeature` composes `AddWorkflowRuntimeCore()` + adds only API/endpoint concerns. (FR-005)
- [x] **T003** Lifetime story (singleton reference stores, overridable) documented in the extension XML docs + `docs/runtime-durable-resumption.md`. (FR-005)
- [x] **T004** `RuntimeCoreCompositionRootTests`: compose the root into a bare collection + drive a Cancel drain with no API feature. (SC-002)

## RT-8 — Collapse telescoping ctors
- [x] **T005** `WorkflowSchedulerDrainer` single primary ctor; state store **required** (W5 terminal guard un-disableable); accessor param removed. (FR-008)
- [x] **T006** `InMemoryRuntimeCheckpointCommitStore` single primary ctor; DI registration shape unchanged (W9 decorators wrap it). (FR-008)
- [x] **T007** Lighter cases (`WorkflowInvokeActivitySchedulerWorkHandler`, `WorkflowParentActivityCompletionSchedulerWorkHandler`) → single primary ctor, TimeProvider defaulted. (FR-008)

## RT-7 — De-ambient the drain path
- [x] **T008** Drainer injects `IWorkflowExecutionStateStore` directly; ambient state-store fallback deleted. (FR-006)
- [x] **T009** Thread `AmbientServices` explicitly: drainer → `DispatchAsync(..., ambientServices, ct)` → dispatcher stages `Workspace.AmbientServices`. (FR-001/FR-006)
- [x] **T010** Delete `IWorkflowExecutionAmbientServicesAccessor` + AsyncLocal/Noop impls + drainer `Push`; nested-invoke handlers read `Workspace.AmbientServices`. (FR-006)
- [x] **T011** Preserve W9 `IRuntimeCoalescingSessionAccessor` session-flag gating exactly; document the exception. (FR-007)

## RT-6 — Move 2 remainder (slot-invoked model)
- [x] **T012** `RuntimePipelineWorkspace` stages an ordered commit **list** (`PendingCheckpointCommits` + `StageCheckpointCommit`, single-commit convenience) + `AmbientServices`. (FR-001)
- [x] **T013** Real activity slots: `RuntimeActivityInvokeMiddleware` (runs staged handler before-`next`) + `RuntimeActivityCheckpointMiddleware` (drains list in order, one committer call per entry, never folds); activity terminal → no-op guard. (FR-002/FR-003)
- [x] **T014** Migrate workflow `Checkpoint` handler to the slot-invoked model. (FR-004)
- [x] **T015** Migrate `CreateBookmark`, `ScheduleActivity`, `StartActivity` (stage commit; keep plain inline path). (FR-004)
- [x] **T016** Migrate `ParentActivityCompletion` to the `Invoke` slot; commits **inline** (stages nothing) — behavior-preserving. (FR-004)
- [x] **T017** Migrate `InvokeActivity` (last); commits **inline** in the `Invoke` slot (stages nothing); no bailout needed. (FR-004)

## RT-11 — Deserialize once
- [x] **T018** `RuntimeCompleteActivityPayloadMemo` (`ConditionalWeakTable`, caches successful parses); wire into selector routing + `CanHandle` + handler body — 4 parses → 1. (FR-009)

## Tests + baselines
- [x] **T019** Commit-list ordering test (one committer call per staged entry, in order, never folded). (FR-003/SC-001)
- [x] **T020** Ambient-services staged on the workspace + drainer required-state-store terminal-guard tests. (FR-006/FR-008)
- [x] **T021** RT-11 single-deserialize assertion. (FR-009)
- [x] **T022** Full baseline green at slices 4, 8, final: build 0 err; Architecture 37; Runtime (grew with new tests); Groundwork 150; Publishing 52; Activities.Runtime 145; Resumption 12; Scheduling.runtime 19; Activities.Scheduling 8; Modularity 104. W1/W5/W9/W2/W7/W8 tripwires green. (SC-006)

## Housekeeping
- [x] **T023** Tick T013 in spec 083 tasks; open this spec 084 for the remainder; repoint `.specify/feature.json` + AGENTS.md SPECKIT block → 084.
- [x] **T024** `EXTENSION_POINTS.md` (Runtime Core) additive: `IRuntimePipelineWorkHandler` slot-invoked model, real `Invoke`/`Checkpoint` slots, `RuntimePipelineWorkspace` staging surface + `AmbientServices`, `AddWorkflowRuntimeCore`; ownership-accessor note corrected. Regenerate all map layers.
- [x] **T025** Draft PR `--base main` (verify `baseRefName==main`); trailer; reference the elsa-4-review-remediation bucket; PR body states W9's `IRuntimeCoalescingSessionAccessor` is a preserved opt-in ambient session flag distinct from the removed ambient service location.

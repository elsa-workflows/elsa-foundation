# Implementation Plan: Runtime Structure — ADR 0029 Move 2 Remainder + Drain-Path De-ambienting (W12)

**Branch**: `sfmskywalker-w12-runtime-structure` | **Date**: 2026-07-04 | **Spec**: [spec.md](spec.md)

## Summary

Finish ADR 0029 **Move 2** across every remaining scheduler handler and fold in the surrounding runtime structural remediation from the elsa-4 review unit **W12**: split the hosting-agnostic composition root out of the API feature (RT-4), remove the two ambient service locators from the drain path (RT-7), collapse the telescoping constructors (RT-8), and deserialize the `CompleteActivity` payload once (RT-11). One behavior-preserving structural pass, delivered in ordered internal slices under a single draft PR. Continues [spec 083](../083-runtime-checkpoint-slot-decomposition/spec.md) (first slice, Cancel — done/#366).

## Constitution Check

- **§E2.2 / §E2.6**: PASS — all new/changed types Runtime-owned; operate over runtime state + the work item; no Design types; no new Runtime→Design dependency.
- **§2.23 (focused unit tests)**: Met — composition-root, slot-ordering, commit-list ordering, ambient-removal, single-deserialize, and per-handler stage-vs-inline tests.
- **§2.21 (preserve tests)**: Met — all pre-existing runtime suites and the W1/W5/W9/W2/W7/W8 tripwires pass unchanged; test-site ctor updates are mechanical (accessor arg removed, primary ctor).

## Design

### RT-4 — Core-owned composition root
- `Core/Extensions/RuntimeCoreServiceCollectionExtensions.cs` — `AddWorkflowRuntimeCore(IServiceCollection)` holds the full runtime registration set (stores, scheduler queue/drainer, committer, pipelines + built-in middleware, dispatcher, ownership fencing, post-commit outbox). `TryAdd*` throughout.
- `WorkflowsRuntimeApiFeature.ConfigureServices` composes `AddWorkflowRuntimeCore()` and adds only API/endpoint concerns.
- Lifetime story (singleton reference stores, overridable) documented in the extension XML docs + `docs/runtime-durable-resumption.md`.
- Guard: `RuntimeCoreCompositionRootTests` composes the root into a bare collection and drives a Cancel drain with no API feature.

### RT-6 — Move 2 remainder (slot-invoked model)
- **Workspace commit-list**: `RuntimePipelineWorkspace` stages `PendingCheckpointCommits` (ordered `List`) with a `PendingCheckpointCommit` single-commit convenience + `StageCheckpointCommit`. Add `AmbientServices` (RT-7 carrier).
- **Activity slots become real**: `RuntimeActivityInvokeMiddleware` runs the staged handler before-`next`; `RuntimeActivityCheckpointMiddleware` drains the list in order (one committer call per entry). Dispatcher stages the activity handler on the workspace; activity terminal becomes a no-op guard.
- **Workflow Checkpoint** handler migrated to the slot-invoked model (finishes the workflow pipeline).
- **Migrate committing handlers** easiest→hardest: `CreateBookmark` → `ScheduleActivity` → `StartActivity` → `ParentActivityCompletion` → `InvokeActivity` (last). Each keeps its plain (inline-commit) path; the two nested-invoke handlers commit **inline** in the `Invoke` slot and stage nothing (behavior-preserving — see spec Assumptions).

### RT-7 — De-ambient the drain path
- Drainer injects `IWorkflowExecutionStateStore` directly (required); ambient state-store fallback deleted.
- Drain request's `AmbientServices` threaded explicitly: drainer → `IRuntimeExecutionPipelineDispatcher.DispatchAsync(workItem, handler, ambientServices, ct)` → dispatcher stages it on `Workspace.AmbientServices` → nested-invoke handlers read it there.
- Delete `IWorkflowExecutionAmbientServicesAccessor` + AsyncLocal/Noop impls + the drainer `Push`.
- **Coalescing preserved**: W9's `IRuntimeCoalescingSessionAccessor` (opt-in session flag) untouched; documented as the deliberate exception.

### RT-8 — Collapse telescoping ctors
- `WorkflowSchedulerDrainer`: single primary ctor; state store **required** (W5 terminal guard un-disableable); TimeProvider defaulted; accessor param removed (RT-7).
- `InMemoryRuntimeCheckpointCommitStore`: single primary ctor (store params optional-defaulted); DI registration shape unchanged (W9 decorators wrap it).
- Lighter cases: `WorkflowInvokeActivitySchedulerWorkHandler`, `WorkflowParentActivityCompletionSchedulerWorkHandler` → single primary ctor, TimeProvider defaulted.

### RT-11 — Deserialize once
- `Core/Services/RuntimeCompleteActivityPayloadMemo.cs` — static `ConditionalWeakTable<RuntimeSchedulerWorkItem, RuntimeCompleteActivityCommandPayload>`; `Deserialize(workItem)` returns cached-or-parsed and caches successful parses only (exceptions propagate to each call site's existing filter). Wired into the selector routing, `CanHandle`, and the handler body — 4 parses collapse to 1.

## Slice ordering (internal commits; one draft PR)
1. RT-4 composition root extraction + host-agnostic guard test + doc.
2. RT-8 telescoping collapse (drainer + commit store + light cases).
3. RT-7 drainer ambient state-store removal (direct injection).
4. RT-6(a) workflow Checkpoint handler → slot. **[full baseline]**
5. RT-6(b) activity slot-invoked model stand-up + workspace commit-list.
6. RT-6(b) migrate CreateBookmark, ScheduleActivity, StartActivity.
7. RT-7 nested-invoke ambient→context + migrate ParentActivityCompletion (+ RT-11 fold).
8. RT-6(b) migrate InvokeActivity (last) + finalize ambient deletion. **[full baseline]**
9. Speckit/docs/maps housekeeping (this spec 084, AGENTS.md, feature.json, EXTENSION_POINTS.md, regenerate maps). **[full baseline]**

## Test plan
- After each slice: build + touched suites; full baseline suite run at slices 4, 8, final.
- New tests: host-agnostic composition; drainer required-state-store terminal-guard; activity Invoke-before-Checkpoint ordering + fail-loud-when-Invoke-missing; commit-list drains in order; each migrated handler stages vs inline-commits; RT-11 single-deserialize; ambient-services staged on the workspace.
- Tripwires (stay green): W1 poison/retry, W5 ownership fencing (TOCTOU), W9 coalescing + queue-frontier invariant, W2 durable queue, W7 stimulus routing, W8 durable timers, `RuntimePipelineContractTests`.

## Complexity Tracking

The highest-risk change is the InvokeActivity migration (large handler, multiple interleaved commits). It was kept behavior-preserving by (a) staging **nothing** — it commits inline in the `Invoke` slot through the resolved provider — and (b) the workspace commit-**list** so any staging handler can sequence N commits without folding. Folding stays exclusively W9's job; the `Checkpoint` slot only sequences. The activity `Invoke` slot addition mirrors the workflow slot already justified in the ADR 0029 addendum; `RuntimePipelineContractTests` derives its expected slots from the slots constant and passes unchanged.

## Sequencing after this unit
Move 2 is complete after this unit. Remaining ADR 0029 hazards deferred elsewhere: atomic checkpoint-commit folding (#310, W9's domain), transactional fault arms, control-leaf intents (#260/#308), container scope-completion capture (#210/ADR 0027), inspection toggle. Ack-based dequeue (item-level window-C replay) remains future work layered on W5/W2.

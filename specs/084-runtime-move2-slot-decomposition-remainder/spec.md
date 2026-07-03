# Feature Specification: Runtime Structure — ADR 0029 Move 2 Remainder + Drain-Path De-ambienting (W12)

**Feature Branch**: `sfmskywalker-w12-runtime-structure`

**Created**: 2026-07-04

**Status**: Draft — completes ADR 0029 **Move 2** across all remaining scheduler handlers and folds in the surrounding runtime structural remediation (RT-4/RT-7/RT-8/RT-11) from the elsa-4 architecture review. Behavior-preserving.

**Input**: Continuation of [spec 083](../083-runtime-checkpoint-slot-decomposition/spec.md) (Move 2 first slice — Cancel only, merged as #366) and the [elsa-4 architecture review roadmap](../../docs/reports/elsa-4-architecture-review-2026-07/roadmap.md) unit **W12. Runtime structure** (findings RT-4, RT-6, RT-7, RT-8, RT-11). Spec 083 was deliberately scoped "first slice, Cancel only" and is **done**; this spec owns the remainder so history stays clean.

## Context

Move 1 (spec 082) made the pipeline the live execution spine. Move 2 first slice (spec 083) proved the **slot-invoked handler model** on the single simplest handler (`WorkflowCancelSchedulerWorkHandler`): a migrated handler additionally implements `IRuntimePipelineWorkHandler`, runs inside the pipeline's `Invoke` slot (before-`next`) with the context threaded **explicitly** (no ambient accessor), and stages its `RuntimeCheckpointCommit` for the `Checkpoint` slot to commit. Every other handler still committed inline as the pipeline terminal, and the runtime composition still lived entirely inside the FastEndpoints API feature with two ambient service locators in the drain path.

This unit finishes the job across the whole runtime, delivered as one behavior-preserving structural pass:

- **RT-6 (Move 2 remainder).** Migrate every remaining committing handler to the slot-invoked model — workflow `Checkpoint`, and the activity pipeline (`CreateBookmark`, `ScheduleActivity`, `StartActivity`, and the two nested-invoke handlers `ParentActivityCompletion` + `InvokeActivity`). Stand up the real activity `Invoke`/`Checkpoint` slot middleware and extend the workspace to stage an **ordered list** of commits (InvokeActivity commits more than once per dispatch and interleaves commit+enqueue).
- **RT-4 (composition root).** Split the hosting-agnostic runtime registration out of `WorkflowsRuntimeApiFeature` into a Core-owned `AddWorkflowRuntimeCore(IServiceCollection)` the API feature composes, so the runtime is usable without the API feature. Lifetime story documented deliberately (singleton reference stores, `TryAdd` overridable).
- **RT-7 (de-ambient the drain path).** Remove the two ambient **service locators** — the drainer's `IWorkflowExecutionAmbientServicesAccessor` state-store lookup and the AsyncLocal pipeline-context accessor smuggling the mutable workspace — in favor of explicit parameters/context members. W9's `IRuntimeCoalescingSessionAccessor` opt-in ambient **session flag** is preserved exactly (a documented exception, distinct in kind).
- **RT-8 (ctor collapse).** Collapse the telescoping constructors on `WorkflowSchedulerDrainer` and `InMemoryRuntimeCheckpointCommitStore` (and the lighter nested-invoke handlers) into a single primary ctor; the drainer's state store becomes **required** so W5's terminal ownership guard cannot be silently disabled by construction; the commit store's DI registration shape is unchanged (W9 decorators wrap it).
- **RT-11 (deserialize once).** The `CompleteActivity` payload was deserialized up to 4× per dispatch (selector routing, `CanHandle`, handler body). Deserialize once and reuse.

This is accepted-ADR implementation under the [Runtime Execution Seam](../../docs/program-goals/runtime-execution-seam.md) bucket and the [elsa-4-review-remediation](../../docs/reports/elsa-4-architecture-review-2026-07/roadmap.md) Phase 2 wave.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Every committing handler commits through its named slot, unchanged in effect (Priority: P1)

Each remaining committing scheduler handler, dispatched through the pipeline, produces exactly the same persisted result as before, but its commit is performed by the `Checkpoint` slot (draining the staged commit list) rather than inline as the terminal — except the two nested-invoke handlers, which commit inline in the `Invoke` slot through a dynamically-resolved provider (staging nothing) because converting their multi-branch commits to staged form would not be behavior-preserving.

**Why this priority**: Completing Move 2 is the point of the unit; behavior preservation is the hard constraint.

**Independent Test**: Dispatch each handler's work item through the feature-composed pipeline and assert the identical checkpoint commit(s) persist in the identical order; assert direct (no-pipeline) dispatch still commits inline.

**Acceptance Scenarios**:

1. **Given** a migrated staging handler dispatched through the pipeline, **When** its context-aware method runs in the `Invoke` slot, **Then** it stages its commit(s) and the `Checkpoint` slot drains them **in order, one committer call per staged entry** (never folded/batched).
2. **Given** a work item that stages multiple commits (e.g. InvokeActivity's path), **When** the `Checkpoint` slot runs, **Then** the committer is called once per staged commit in staging order, byte-identical to the pre-Move-2 inline sequence.
3. **Given** direct dispatch of any handler (the plain `IWorkflowSchedulerWorkHandler` path), **When** it runs, **Then** it commits inline exactly as before.

### User Story 2 - The runtime is usable without the API feature (Priority: P1)

A worker/test harness composes `AddWorkflowRuntimeCore` into a bare service collection and drives a real drain to completion, with no FastEndpoints API feature present.

**Why this priority**: Host-agnosticism is the RT-4 deliverable and the acceptance gate.

**Independent Test**: `RuntimeCoreCompositionRootTests` composes `AddWorkflowRuntimeCore` and drives a Cancel drain end-to-end with no API feature.

**Acceptance Scenarios**:

1. **Given** only `AddWorkflowRuntimeCore` registered, **When** the drainer + pipelines are resolved and a drain runs, **Then** it completes and persists the expected commit.

### User Story 3 - No ambient service location in the drain path (Priority: P1)

The drain path resolves its collaborators explicitly; the two ambient service locators are gone.

**Why this priority**: RT-7 is a named review finding; the ambient smuggling is the architectural smell being removed.

**Independent Test**: The drainer injects its state store directly (required); ambient-services flow via `RuntimePipelineWorkspace.AmbientServices`; the deleted accessor types have no references.

**Acceptance Scenarios**:

1. **Given** a drain, **When** the drainer needs the state store, **Then** it uses the directly-injected required dependency (no ambient fallback).
2. **Given** a nested-invoke handler, **When** it needs the request-scoped provider, **Then** it reads `Workspace.AmbientServices` (staged explicitly by the dispatcher), not an AsyncLocal `.Current`.
3. **Given** W9 coalescing is registered, **When** a coalescing session is active, **Then** its `IRuntimeCoalescingSessionAccessor` session-flag gating behaves exactly as before (preserved, not removed).

### Edge Cases

- **Handler throws before staging**: nothing is staged; the `Checkpoint` slot commits nothing (matches inline behavior where a throw precedes the commit).
- **Multi-commit ordering**: a handler that stages N commits produces exactly N committer calls in staging order — no fold, no reorder, no batch.
- **Un-migrated / no-pipeline dispatch**: plain path commits inline exactly as before; the `Checkpoint` slot no-ops when nothing is staged.
- **Nested-invoke inline commit**: `InvokeActivity`/`ParentActivityCompletion` run in the `Invoke` slot but commit inline through a resolved provider and stage nothing — no double commit.
- **Durable provider override**: a store registered by a durable provider wins over the `TryAdd`'d reference store regardless of composition order.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: `RuntimePipelineWorkspace` MUST stage an **ordered list** of pending checkpoint commits (`PendingCheckpointCommits`), retaining a single-commit convenience, plus an explicit `AmbientServices` carrier for the drain's request-scoped provider. Context records otherwise remain immutable.
- **FR-002**: The activity pipeline MUST run its selected handler through a real `Invoke`-slot middleware (before-`next`) and drain the staged commit list through a real `Checkpoint`-slot middleware; the activity terminal MUST become a no-op guard.
- **FR-003**: The `Checkpoint` slot MUST commit staged commits **in order, one `RuntimeCheckpointCommitter.CommitAsync` per staged entry**. It MUST NOT batch or fold multiple staged commits (folding is W9's coalescing decorators' responsibility; batching would change W9 boundary detection and W5 fencing granularity).
- **FR-004**: All remaining committing workflow + activity handlers MUST implement `IRuntimePipelineWorkHandler`. Staging handlers stage; the two nested-invoke handlers commit inline in the `Invoke` slot (staging nothing) — deliberately, to preserve behavior. Every handler retains its plain `IWorkflowSchedulerWorkHandler` path for direct dispatch.
- **FR-005**: `AddWorkflowRuntimeCore(IServiceCollection)` MUST register the full hosting-agnostic runtime; `WorkflowsRuntimeApiFeature` MUST compose it and add only API/endpoint concerns. All Core registrations MUST use `TryAdd*` (durable-provider overridable). The lifetime decision (singleton reference stores) MUST be documented in the composition-root XML docs and `docs/runtime-durable-resumption.md`.
- **FR-006**: The drain path MUST NOT use ambient service location. The drainer MUST inject `IWorkflowExecutionStateStore` directly (required); nested-invoke handlers MUST read the request-scoped provider from `Workspace.AmbientServices` (staged by the dispatcher from the drain request). `IWorkflowExecutionAmbientServicesAccessor` and its AsyncLocal/Noop implementations MUST be deleted.
- **FR-007**: W9's `IRuntimeCoalescingSessionAccessor` opt-in ambient **session flag** MUST be preserved exactly (a deliberate documented exception, distinct from the removed service locators). Its invariant — the durable scheduler queue never advances past the last flushed state — MUST hold.
- **FR-008**: `WorkflowSchedulerDrainer` and `InMemoryRuntimeCheckpointCommitStore` MUST each expose a **single public constructor**. The drainer's state store MUST be **required** (W5 terminal guard un-disableable by construction). The commit store's DI registration shape MUST be unchanged (W9 decorators wrap it).
- **FR-009**: The `CompleteActivity` payload MUST be deserialized **at most once per dispatch**; selector routing, `CanHandle`, and the handler body MUST reuse the single parse.
- **FR-010**: The change MUST be behavior-preserving. Existing runtime + activities-runtime + resumption + scheduling suites MUST pass; the W1/W5/W9/W2/W7/W8 tripwire suites MUST stay green; no new Runtime→Design dependency (§E2.2/§E2.6).

### Key Entities

- **RuntimePipelineWorkspace**: mutable per-dispatch side-channel — staged handler invocation, ordered pending checkpoint commit **list** (+ single-commit convenience), and `AmbientServices` (explicit RT-7 carrier).
- **IRuntimePipelineWorkHandler**: opt-in context-aware handler method invoked by the `Invoke` slot (explicit context, no ambient state).
- **RuntimeActivityInvokeMiddleware / RuntimeActivityCheckpointMiddleware**: the real activity `Invoke`/`Checkpoint` slots (mirror the workflow slots).
- **AddWorkflowRuntimeCore**: Core-owned host-agnostic composition root.
- **RuntimeCompleteActivityPayloadMemo**: single-parse memo (`ConditionalWeakTable` keyed by the work item) for the `CompleteActivity` payload.

## Success Criteria *(mandatory)*

- **SC-001**: Every migrated handler dispatched through the feature pipeline persists exactly the same checkpoint commit(s), in the same order, as the pre-Move-2 inline path.
- **SC-002**: A bare `AddWorkflowRuntimeCore` composition drives a real drain with no API feature present.
- **SC-003**: The drain path contains no ambient service locators; the deleted accessor types have zero references; W9 coalescing gating is unchanged.
- **SC-004**: `WorkflowSchedulerDrainer` and `InMemoryRuntimeCheckpointCommitStore` each have one public ctor; the drainer state store is required; the commit-store DI shape is unchanged.
- **SC-005**: The `CompleteActivity` payload is parsed at most once per dispatch.
- **SC-006**: All pre-existing runtime suites plus the W1/W5/W9/W2/W7/W8 tripwires pass unchanged. No new Runtime→Design dependency.

## Assumptions

- **Nested-invoke handlers commit inline, deliberately.** `InvokeActivity` and `ParentActivityCompletion` run in the `Invoke` slot but commit inline through a dynamically-resolved provider and stage nothing. Converting their multi-branch, interleaved commit+enqueue paths to staged commits would change the commit boundaries W9 coalescing detects and the granularity W5 fences — i.e. not behavior-preserving. This is the approved outcome (the InvokeActivity bailout protocol was available but not needed; the migration did not balloon).
- **Singleton reference-store lifetimes (RT-4).** The in-memory reference stores *are* the durable state for the reference host, so a single shared instance is correct. `TryAdd` keeps every store overridable by a durable provider that chooses its own lifetime. Scoped/per-request lifetimes are out of scope (captive-dependency ripple).
- **Commit-list drains individually, never folds.** Folding is W9's job; the slot only sequences. Pinned by a commit-list ordering test.
- **Coalescing preserved, not redesigned.** RT-7 targets ambient **service location**; W9's ambient **session flag** is a separate, opt-in, documented mechanism left intact.

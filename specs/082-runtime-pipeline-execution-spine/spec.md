# Feature Specification: Runtime Pipeline Execution Spine (ADR 0029 Move 1)

**Feature Branch**: `claude/confident-grothendieck-c5bbe6`

**Created**: 2026-07-02

**Status**: Draft

**Input**: Implement "Pipeline Move 1" from [ADR 0029](../../docs/adr/0029-runtime-execution-flows-through-the-pipelines.md): make the already-defined runtime workflow/activity execution pipeline the real execution spine by invoking it around the scheduler's single handler-dispatch point, without changing execution behavior.

## Context

The runtime pipeline contract already exists (`IWorkflowRuntimeMiddleware` / `IActivityRuntimeMiddleware`, the pipeline builders, `RuntimeWorkflowPipelineSlots` / `RuntimeActivityPipelineSlots`, `RuntimePipelinePlan`, and empty placeholder middleware for every slot) but **nothing invokes it**. Real execution is inlined in the scheduler work handlers, which the `WorkflowSchedulerDrainer` dispatches. A module that registers an `IActivityRuntimeMiddleware`/`IWorkflowRuntimeMiddleware` today is **silently never run** — a false affordance, the same failure class as the dead JS expression pre-processors.

Move 1 makes the pipeline live by *wrapping* handler dispatch (Option B "wrap, not replace"): the handler stays intact as the pipeline's `Invoke`-slot body, the built-in slots stay pass-throughs, and execution is byte-for-byte identical to today. Move 2 (relocating handler-inlined phases into slot middleware) is explicitly **out of scope**.

This is accepted-decision implementation work under the [Runtime Execution Seam](../../docs/program-goals/runtime-execution-seam.md) bucket. Sizing: [pipeline wiring sizing](../../docs/reports/runtime-execution-pipeline-wiring-sizing.md).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A module-registered runtime middleware actually runs during execution (Priority: P1)

A framework/module author registers a custom `IActivityRuntimeMiddleware` (or `IWorkflowRuntimeMiddleware`) against a named pipeline slot to add a cross-cutting concern (tracing, tenanting, authorization). When a workflow executes and the scheduler drains a work item, the registered middleware runs around the handler that performs the work.

**Why this priority**: This is the entire point of Move 1 — it removes the false affordance and turns the pipeline from dead scaffolding into a live extension surface. Without it, nothing in the change has value.

**Independent Test**: Register a marker middleware in the pipeline, dispatch a real scheduler work item through the drainer, and assert the marker recorded that it ran *and* that the underlying handler also ran (the guardrail test required by the ADR).

**Acceptance Scenarios**:

1. **Given** a marker activity middleware registered in the activity pipeline, **When** an activity-kind work item is dispatched by the drainer, **Then** the marker middleware's `InvokeAsync` runs and the selected handler's `HandleAsync` runs exactly once, in that nesting order.
2. **Given** a marker workflow middleware registered in the workflow pipeline, **When** a workflow-lifecycle-kind work item is dispatched, **Then** the marker middleware runs and the handler runs.
3. **Given** a middleware that short-circuits by not calling `next`, **When** a matching work item is dispatched, **Then** the handler does **not** run (proving the handler is genuinely the inner terminal delegate, not called independently).

### User Story 2 - Existing execution behavior is unchanged (Priority: P1)

An operator or existing test runs workflows exactly as before. With only the built-in placeholder middleware registered, every workflow produces the same outcomes, the same persisted state, and the same checkpoints as before Move 1.

**Why this priority**: Behavior preservation is a hard constraint of the ADR. A regression here is unacceptable regardless of the extensibility win.

**Independent Test**: The full existing runtime test suite (including `RuntimePipelineContractTests` and all scheduler/drain/execution tests) passes unchanged.

**Acceptance Scenarios**:

1. **Given** only built-in placeholder middleware registered, **When** any work item of any command kind is dispatched, **Then** the observable result (handler selected, state written, checkpoint committed, drain result) is identical to direct handler dispatch.
2. **Given** a drainer constructed without a pipeline dispatcher (as existing unit tests do), **When** it drains work, **Then** it calls the handler directly and behaves exactly as today.

### User Story 3 - The pipeline used matches the work item's kind (Priority: P2)

The runtime routes each work item through the pipeline appropriate to its kind so that a middleware registered for activity execution sees activity work and a middleware registered for workflow-lifecycle work sees workflow work.

**Why this priority**: Correct kind→pipeline selection is what makes the two distinct pipelines meaningful. It is required for the contract to be honest, though no built-in behavior depends on it in Move 1.

**Independent Test**: Feed the selector each command kind (and both `CompleteActivity` completion sub-kinds) and assert the selected pipeline matches the ADR mapping.

**Acceptance Scenarios**:

1. **Given** a `Start`, `Checkpoint`, `Cancel`, or routing `CompleteActivity` work item, **When** the selector runs, **Then** it selects the **workflow** pipeline.
2. **Given** a `ScheduleActivity`, `StartActivity`, `InvokeActivity`, `ResumeBookmark`, or `CreateBookmark` work item, **When** the selector runs, **Then** it selects the **activity** pipeline.
3. **Given** a `CompleteActivity` work item whose payload `CompletionKind` is `ParentCompletionEvaluation`, **When** the selector runs, **Then** it selects the **activity** pipeline (matching the parent-completion handler); any other/absent completion kind selects the **workflow** pipeline (matching the routing handler).

---

### User Story 4 - A module contributes middleware through DI and controls its placement (Priority: P2)

A framework or third-party module registers its runtime middleware through a single DI call and declares where it sits in the pipeline, without editing a central ordering list and without its placement depending on registration order.

**Why this priority**: This makes User Story 1's extensibility reachable through the supported surface (not just hand-constructed pipelines). Folded into Move 1 (per ADR 0029) because it completes the change's stated value.

**Independent Test**: Register a middleware via `AddActivityRuntimeMiddleware<T>(…)` on a feature-composed provider, dispatch a real work item, and assert it runs at the declared slot/order; separately assert the ordering/collision/replace rules on the builder.

**Acceptance Scenarios**:

1. **Given** a middleware annotated `[RuntimeMiddleware(slot, Order = -100)]` registered with `AddActivityRuntimeMiddleware<T>()`, **When** the pipeline is composed, **Then** it is placed at that slot/order; explicit arguments to the registration call override the attribute.
2. **Given** two distinct middleware registered at the same `(slot, order)`, **When** the plan is built, **Then** composition fails with an error naming the conflict (and, when one is the built-in at order 0, guidance to choose a negative/positive order).
3. **Given** the same set of middleware registered in different orders, **When** the plan is built, **Then** the resolved order is identical (deterministic by slot, order, then type name).
4. **Given** `Replace<TOld,TNew>()` / `Remove<T>()` on the builder, **When** the plan is built, **Then** the built-in is swapped/dropped at its placement.

### Edge Cases

- **Start before state exists**: A `Start` work item is dispatched before its `WorkflowExecutionState` exists. The pipeline must still run (the guardrail must hold for every dispatch), so the context cannot require a pre-loaded `WorkflowExecutionState`.
- **Unmapped command kinds**: Kinds with no per-execution semantics that still reach the drainer (e.g. `RunSchedulerWork`, `GeneratedEvent`, `DeliverSignal`) must select a deterministic pipeline and never throw during selection.
- **Malformed `CompleteActivity` payload**: If the payload cannot be deserialized, selection must not throw; it falls back to the workflow pipeline (the routing handler's own default), preserving dispatch.
- **Middleware short-circuit / throw**: If a middleware throws, the exception propagates to the drainer's existing per-item fault handling exactly as a handler exception would today (the work item is recorded Faulted).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The runtime MUST provide a pipeline executor that composes the registered middleware for a pipeline (in the order given by its `RuntimePipelinePlan`) into a delegate chain and invokes it around an inner terminal delegate.
- **FR-002**: At the single handler-dispatch point in the drain path (`WorkflowSchedulerDrainer` — the one `handler.HandleAsync(workItem, …)` call), the runtime MUST invoke the pipeline as `pipeline.InvokeAsync(context, () => handler.HandleAsync(workItem, ct))` instead of calling the handler directly, when a pipeline dispatcher is wired.
- **FR-003**: The runtime MUST select the workflow pipeline vs the activity pipeline from the work item, using the same discriminator the handlers' `CanHandle` uses: command kind for all kinds, plus the payload `CompletionKind` to disambiguate `CompleteActivity` (parent-completion → activity; routing/other → workflow).
- **FR-004**: The runtime MUST build the pipeline context from the work item. The workflow context carries the workflow-execution identity and optional `WorkflowExecutionState`/`SchedulerState`; the activity context additionally carries optional `ActivityExecutionState`. State fields are optional because some dispatches (e.g. `Start`) precede state creation, and state population is the `LoadState` slot's responsibility (Move 2), not the executor's.
- **FR-005**: The runtime MUST register both pipelines — workflow and activity — with their built-in placeholder slots, plus the executor, selector, and dispatcher, in the appropriate runtime feature's DI container.
- **FR-006**: With only the built-in placeholder middleware registered, dispatch through the pipeline MUST be behavior-preserving: identical handler selection, identical state/checkpoint effects, and identical drain results compared to direct handler dispatch.
- **FR-007**: The pipeline dispatcher MUST be an **optional** dependency of the drainer so that existing code paths and tests that construct the drainer without it keep calling handlers directly, unchanged.
- **FR-008**: A guardrail test MUST assert that a registered marker middleware is **actually invoked during a real work-item dispatch** (not merely that the builder registers it), preventing silent re-orphaning of the pipeline.
- **FR-009**: The change MUST NOT modify any scheduler work handler's body, MUST NOT introduce any `Elsa.Workflows.Design.*` dependency (constitution §E2.2/§E2.6), and MUST leave the scheduler (queue/drain/checkpoint) otherwise unchanged.
- **FR-010**: The existing `RuntimePipelineContractTests` (the locked slot contract) MUST continue to pass unchanged.
- **FR-011**: The runtime MUST expose a single DI contribution mechanism (`AddWorkflowRuntimeMiddleware<T>` / `AddActivityRuntimeMiddleware<T>`) that atomically registers the middleware type and its placement, usable identically by framework built-ins, first-party modules, and third-party modules (no privileged path). The feature MUST apply DI-registered contributions to the pipeline before composing it.
- **FR-012**: Middleware placement MUST be declarative: a `[RuntimeMiddleware(slot, Order)]` attribute supplies the default slot/order/name, and explicit arguments to the registration call MUST override it. Registration with neither an attribute nor an explicit slot MUST fail with a clear error.
- **FR-013**: The resolved pipeline order MUST be deterministic and independent of registration/module-load order (ordered by slot, then order, then a stable type key), and two distinct middleware sharing the same `(slot, order)` MUST be a build-time error that names the conflict (with before/after guidance when one is the built-in at order 0).
- **FR-014**: The builder MUST support `Replace<TOld,TNew>()` and `Remove<T>()` for swapping or disabling middleware (including built-ins) at their placement, and the fully-resolved plan MUST be logged at Debug on composition so ordering is inspectable. Concrete-neighbour ("after middleware X") ordering MUST NOT be supported (slots + numeric order only).

### Key Entities *(include if feature involves data)*

- **Runtime execution pipeline (workflow / activity)**: The ordered set of middleware for a pipeline kind, derived from its `RuntimePipelinePlan`, exposing an invoke operation over its context type and a terminal delegate.
- **Pipeline selector**: Maps a scheduler work item to a `RuntimePipelineKind` using command kind and, for `CompleteActivity`, the payload completion kind.
- **Pipeline dispatcher**: The seam invoked at the drainer's dispatch point; selects the pipeline, builds the context, and invokes the pipeline around the handler.
- **Pipeline context (workflow / activity)**: The data carrier passed to middleware — the originating work item plus optional loaded runtime state.
- **Middleware placement attribute**: `[RuntimeMiddleware(slot, Order, Name)]` — the default placement declared on a middleware type.
- **Middleware contribution**: a DI-collected record of `(type, slot, order, name)` applied to the builder when the pipeline is composed.
- **Registration extensions**: `AddWorkflow/ActivityRuntimeMiddleware<T>(slot?, order?, name?)` — the atomic register-type-plus-placement surface.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A middleware registered against a pipeline slot runs on 100% of matching real work-item dispatches (proven by the guardrail test), where before Move 1 it ran on 0%.
- **SC-002**: 100% of the pre-existing runtime test suite passes unchanged, including `RuntimePipelineContractTests` and all scheduler/drain/execution tests — demonstrating zero behavior change.
- **SC-003**: Every command kind that can reach the drainer resolves to exactly one pipeline deterministically, with no selection path throwing.
- **SC-004**: No new dependency from Workflows Runtime onto Workflows Design is introduced (verified by the existing structural dependency tests).
- **SC-005**: A module-registered middleware (via the DI contribution surface) runs on a real dispatch through the feature-composed pipeline at its declared placement; resolved ordering is identical regardless of registration order; a same-`(slot, order)` collision fails composition. (All proven by tests.)

## Assumptions

- The single handler-dispatch point is `WorkflowSchedulerDrainer.DispatchAsync` (verified: the one `handler.HandleAsync` call site). The `WorkflowExecutionDrainCoordinator` orchestrates drain cycles but does not itself dispatch handlers.
- Built-in placeholder middleware are stateless singletons; resolving them once per pipeline is safe. Scoped/module middleware ordering-and-lifetime concerns beyond singletons are a Move 2+ concern.
- The context shape defined in `RuntimePipelineContexts.cs` may be refined to carry the originating work item and to make the typed state fields optional; this is necessary because no full state is loaded at the dispatch point and `Start` runs before its state exists. The contract test constrains the middleware **parameter type**, not the context constructor arity, so this refinement is compatible.
- Move 2 (per-handler slot decomposition, `WorkflowInvokeActivitySchedulerWorkHandler` last) and all its hazards (atomic checkpoint-commit folding #310, transactional fault arms, control-leaf intents #260/#308, container scope-completion capture #210/ADR 0027, inspection toggle) are out of scope and untouched.

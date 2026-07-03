# Phase 0 Research: Runtime Pipeline Execution Spine (Move 1)

## Verified facts (code is truth over reports)

- **Single dispatch point**: `WorkflowSchedulerDrainer.DispatchAsync` contains the one `handler.HandleAsync(workItem, cancellationToken)` call (`src/Elsa/Workflows/Runtime/Core/Services/WorkflowSchedulerDrainer.cs`). `WorkflowExecutionDrainCoordinator` runs drain cycles + outbox but does not dispatch handlers itself. → wrap in the drainer. No blocking condition ("multiple call sites") is present.
- **Behavior preservation is achievable without touching handlers**: built-in middleware are pass-throughs (`*MiddlewareBase.InvokeAsync => next(context)`), so wrapping the handler as the terminal delegate is byte-for-byte identical. No blocking condition ("cannot preserve behavior without touching handler internals") is present.
- **Command-kind discriminator**: `WorkflowExecutionCommandKind` enum. Handlers select by `CommandKind` in `CanHandle`, EXCEPT `CompleteActivity`, which is claimed by TWO handlers disambiguated by the payload `RuntimeCompleteActivityCommandPayload.CompletionKind`:
  - `WorkflowCompleteActivitySchedulerWorkHandler` (workflow, routing): `CompletionKind != ParentCompletionEvaluation` (or no/invalid payload).
  - `WorkflowParentActivityCompletionSchedulerWorkHandler` (activity, parent-completion): `CompletionKind == ParentCompletionEvaluation`.
  → the selector must mirror this exact sub-discriminator for `CompleteActivity`, matching the ADR's split of "complete-activity routing" (workflow) vs "parent-activity completion" (activity).
- **No pre-loaded state at dispatch**: the work item carries `WorkflowExecutionId`, `CommandKind`, and an opaque `Payload` (JsonElement). It does NOT carry `WorkflowExecutionState`/`ActivityExecutionState`. Handlers load their own state internally. `Start` runs before its `WorkflowExecutionState` exists. → the executor must not require pre-loaded, non-null state; the context state fields must be optional.
- **`ActivityExecutionState` is not uniformly derivable** at the dispatch point without per-kind payload parsing (which would duplicate handler internals). → the executor does not load it; the `LoadState` slot (Move 2) will.
- **Context types are unreferenced by construction**: `WorkflowRuntimePipelineContext` / `ActivityRuntimePipelineContext` are used only as the middleware `InvokeAsync` parameter type and in `RuntimePipelineContractTests` (via reflection on the parameter type). Nothing constructs them. → refining their constructor is safe and keeps the contract test green.
- **Drainer is constructed both by DI and directly by tests**: `RuntimeSchedulerDrainTests` constructs `WorkflowSchedulerDrainer` directly with several constructor overloads. → the pipeline dispatcher must be an OPTIONAL dependency (new overload, defaulted null) so those tests are unaffected and behavior stays direct-dispatch.

## Decisions

- **D1 — Wrap in the drainer, optional dependency.** Chosen over wrapping in the coordinator (wrong layer) and over a mandatory dependency (would break existing direct-construction tests / force behavior change).
- **D2 — Dedicated selector service.** Chosen over deriving the pipeline from the resolved handler type (would require touching the handler interface / all handlers) and over a pure enum switch (cannot disambiguate `CompleteActivity`).
- **D3 — Context carries the work item + optional state.** Chosen over (a) loading state in the executor (extra I/O, impossible for `Start`, requires payload parsing for activity state) and (b) keeping non-nullable state (infeasible at the dispatch point). Matches the slot design where `LoadState` owns state population.
- **D4 — Resolve middleware once per pipeline as singletons.** Built-ins are stateless singletons. Scoped/module-middleware lifetime is a Move 2+ concern; noted, not solved here.

## Alternatives rejected

- Replace handler internals with per-slot middleware now (ADR Option A / Move 2): out of scope; forces the hazardous decomposition up front.
- One unified pipeline for both kinds: rejected by the ADR and the contract test (distinct context types).

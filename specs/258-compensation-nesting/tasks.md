# Tasks: nested, repeated and confirmed compensation (M3)

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

## Phase A — Runtime seams (PR 1)

- [x] T301 [P] R-1: `RuntimeAncestorSelector` (consumer key or activity type, optional authored id, `StopAtGraphBoundary`, `Optional`) and the selector overload of `RequestAncestorNotification`; the string overload maps to it. `ParentNotificationEvaluation.FindNearestAncestorAsync` matches by type and stops at an `elsa.graph-activity` ancestor when asked. Unit tests: type match, authored-id match, boundary stop, optional no-match sends nothing, required no-match faults with the existing code.
- [x] T302 [P] `ActivityChildNotifiedContext.ViaChildActivityExecutionId` / `ViaChildExecutableNodeId`, filled for direct and ancestor notifications. Unit test with a two-level ancestry.
- [x] T303 R-2: allow ancestor notifications from a child-completion evaluation ending `Complete`, enqueued ahead of the completion intent in one checkpoint. Keep refusing every other evaluation and continuation. Tests: ordering (ancestor evaluates first), refusal cases, restart between the two work items.
- [x] T304 R-3: `IRuntimeActivityDescendantFaultHandler` and `ActivityDescendantFaultedContext`; capability flag compiled into the executable node; delivery from the bypass branch of `WorkflowParentActivityCompletionSchedulerWorkHandler`. Absorption cancels the via-child's subtree; intervention keeps the incident blocking. Tests: Sequence, ForEach, While, For under a flagged parent; a Flowchart in between still suspends; a reusable boundary stops the walk; unflagged trees unchanged (the existing Sequence fault tests pass untouched).
- [x] T305 `EXTENSION_POINTS.md` (runtime) lists R-1 to R-3. Regenerate the maps.

## Phase B — Scope v3, Confirm, Compensate (PR 2)

- [x] T306 State v3 types (`Confirmed`, `handlerExecutableNodeId`, `origin`, cause kinds `explicit` / `requested`, `confirmations`, `receivedHandOvers`) and `CompensationScopeStateUpcaster` v2→v3. Unit tests for every upcast row and no downgrade.
- [x] T307 Routing keys in request metadata and provenance metadata for every scheduled child; outcome matching falls back to provenance (FR-304). Test: operator retry of a handed-over handler routes back to the outer scope.
- [x] T308 Hand-over on `Done` (R-1 optional, boundary stop, R-2, batches of 16) and `confirmOnComplete`. Scope implements `IRuntimeActivityChildNotificationHandler` and `IRuntimeActivityChildOutcomeHandler` (research R8).
- [x] T309 Receiving hand-overs in every phase, de-duplication, fresh ordinals, transitive origin; scheduling handed-over handlers (FR-307). First test: the runtime accepts a handler node owned by an inner scope's slot as the outer scope's child (research R5); stop and re-plan if it does not.
- [x] T310 Late handler bindings: prove an `ActivityResult` reference to the inner forward step resolves when the handler runs under the outer scope (research R6); validator refuses container-local reads in a scope that can hand over. (The publication check is deferred to T324: research R18.)
- [x] T311 `Confirm` activity and the scope's confirm transition; refusal during recovery.
- [x] T312 `Compensate` activity, explicit cause, stopping the running step (uncertain journaling, R9), cascade through intermediate scopes, refusal inside a handler.
- [x] T313 Descendant-fault handling in the scope (`OnDescendantFaultedAsync`, R-3).
- [x] T314 Validators: Confirm/Compensate targets must be enclosing scopes on the same side of every reusable boundary.
- [x] T315 Integration and restart tests for every [quickstart](quickstart.md) journey in Durable, Balanced and Ephemeral, with the four injection points.
- [x] T316 Workbench owned-process journey N1 + C1 over REST with a process kill.
- [x] T317 Run the six SC-305 negative controls once by hand and record them in the PR.
- [x] T318 Docs: `website/docs/essentials/compensation.md` (nesting, loops, confirmation, Compensate, cancellation, the composition matrix and its known limit), glossary rows.

## Phase C — Studio (PR 3)

- [ ] T319 `compensationRuntime.ts`: origin scope and iteration per entry, `Confirmed` with the confirming occurrence, "Compensable by {scope}", explicit cause, per-occurrence grouping for repeated scopes. Vitest per state and for partial pages.
- [ ] T320 `CompensationScopeDesigner.tsx` run view: origin link, "✓ Confirmed", occurrence picker (keyboard and screen reader), cancelled scope shows "Cancelled — nothing undone". Text plus icon for every state.
- [ ] T321 Confirm and Compensate property editors with a target picker limited to valid enclosing scopes; validation messages from T314.
- [ ] T322 Mocked browser suite with axe at both widths and both themes for nested, loop and confirmed runs.
- [ ] T324 Publication check: refuse a compensation handler that reads variables local to containers inside a scope that can hand over (FR-307, research R18). Unit tests per container kind.
- [ ] T323 Spec 258 status becomes `Implemented`, with the validation evidence. Close #3046.

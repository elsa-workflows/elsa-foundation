# Implementation plan: nested, repeated and confirmed compensation (M3)

**Spec**: [spec.md](spec.md) · **Issue**: [#3046](https://github.com/elsa-workflows/elsa-foundation/issues/3046)

## Technical context

.NET 10 runtime modules under `Essentials/`, React 19/Vite Studio under `Studio/`. No new tables. M3 adds three generic runtime seams (R-1 to R-3, [contracts/runtime-seams.md](contracts/runtime-seams.md)), two activities (Confirm, Compensate), scope state v3 and design validators. Evidence is in [research.md](research.md); state in [data-model.md](data-model.md); transitions in [contracts/semantics.md](contracts/semantics.md); proofs in [quickstart.md](quickstart.md).

## Placement

| Item | Location |
|---|---|
| R-1 selector, via-child ids | `IRuntimeActivityExecutionContext`, `SimpleActivityExecutionContext`, `ParentNotificationEvaluation`, `ActivityChildNotifiedContext` |
| R-2 | `WorkflowParentActivityCompletionSchedulerWorkHandler` (ancestor notifications on `Complete`), `ParentNotificationEvaluation.BuildAncestorAsync` |
| R-3 | new `IRuntimeActivityDescendantFaultHandler` + `ActivityDescendantFaultedContext` (`Workflows/Runtime/Core`), capability flag in executable compilation, delivery in the bypass branch of `WorkflowParentActivityCompletionSchedulerWorkHandler` |
| Scope v3, hand-over, Confirm/Compensate handling | `Essentials/src/Activities/Compensation/` (`CompensationScope`, `Internal/CompensationScopeState`, upcaster v2→v3) |
| Confirm, Compensate activities | `Essentials/src/Activities/Compensation/Activities/` |
| Validators | `Internal/CompensationScopeValidator` (targets, reusable boundary, late-handler bindings) |
| Studio | `compensationRuntime.ts`, `CompensationScopeDesigner.tsx`, Confirm/Compensate property editors |
| Docs | `website/docs/essentials/compensation.md`, glossary, `EXTENSION_POINTS.md` |

## Constitution and ADR checks

- **§E2.2:** Runtime and Activities only; Studio client for UI.
- **§E2.6.1:** v2 scopes resume under v3 through the upcaster.
- **ADR 0080 D1:** unit tests for each seam and transition, integration and restart tests per journey, Vitest per state.
- **ADR 0080 D3:** new public surface: the selector record and overload, the descendant-fault interface and context, the two activities.
- **Extension points:** `EXTENSION_POINTS.md` lists R-1 to R-3; maps regenerate.

## Delivery as PRs

1. Runtime seams R-1, R-2, R-3.
2. Scope v3, Confirm, Compensate, validators, journeys, Workbench, docs.
3. Studio.

Each PR merges onto `main` before the next starts.

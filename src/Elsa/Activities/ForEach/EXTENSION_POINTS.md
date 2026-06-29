# Elsa.Activities.ForEach Extension Points

This module does not expose replaceable service contracts in v1. Its activity-owned contracts are:

- `ForEach.Body` child slot (at most one body activity, run once per collection item)
- A `Done` composite outcome emitted when the collection is exhausted (or empty)
- A per-iteration variable scope (built via `RuntimeLoopIterationScopeFactory`, ADR 0028) exposing the
  current item (`foreach.currentItem` / `currentItem`) and, by default, the zero-based index
  (`foreach.currentIndex` / `currentIndex`) to the body

## Cross-domain contributions

- `ForEachStructureHandler` implements `IActivityStructureHandler` (`Elsa.Workflows.Design.Core`). It
  projects the single body slot from the authored structure, compiles the executable structure (the body
  node id), and round-trips both through publishing so the runtime resolves the body without re-reading
  the design document. `ForEach` is not a container scope, so it declares no container-scoped variables
  (`SupportsScopedVariables` defaults to `false`); its per-iteration item/index are loop-owned and live
  in a per-pass iteration scope, not a container scope.

## Runtime seam dependency

- `ForEach` reads `ActivityChildCompletedContext.CompletedChildIterationId` (threaded by
  `WorkflowParentActivityCompletionSchedulerWorkHandler` from the completed body execution's persisted
  `IterationId`) to recover which pass just finished and advance to the next item.

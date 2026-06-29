# Elsa.Activities.ForEach Extension Points

This module does not expose replaceable service contracts in v1. Its activity-owned contracts are:

- `ForEach.Body` child slot (at most one body activity, run once per collection item)
- A `Done` composite outcome emitted when the collection is exhausted (or empty)
- A per-iteration variable scope (built via `RuntimeLoopIterationScopeFactory`, ADR 0028) exposing the
  current item (variable name `currentItem`) and, by default, the zero-based index (`currentIndex`) to the
  body. Under the merged loop-scope wiring (#296) the variable name doubles as the scope's reference key.

## Cross-domain contributions

- `ForEachStructureHandler` implements `IActivityStructureHandler` (`Elsa.Workflows.Design.Core`). It
  projects the single body slot from the authored structure, compiles the executable structure (the body
  node id), and round-trips both through publishing so the runtime resolves the body without re-reading
  the design document. `ForEach` is not a container scope, so it declares no container-scoped variables
  (`SupportsScopedVariables` defaults to `false`); its per-iteration item/index are loop-owned and live
  in a per-pass iteration scope, not a container scope.

## Runtime seam dependencies

- `ForEach` reads `ActivityChildCompletedContext.CompletedChildIterationId` (threaded by
  `WorkflowParentActivityCompletionSchedulerWorkHandler` from the completed body execution's persisted
  `IterationId`) to recover which pass just finished and advance to the next item.
- For each pass `ForEach` writes the loop owner node id, the item variable name + JSON value, and
  (optionally) the index name + JSON value into the **body's scheduling provenance metadata** under the
  merged loop-scope keys (`RuntimeMetadataKeys.LoopIterationOwnerNodeId` /
  `LoopIterationItemName` / `LoopIterationItemValue` / `LoopIterationIndexName` / `LoopIterationIndexValue`,
  shared with `For`, #296). The runtime's `RuntimeContainerScopeService.BuildScopeAsync` reads these back
  when it builds the body's visible scope chain and calls
  `RuntimeLoopIterationScopeFactory.BuildIterationScope` to layer a fresh per-pass iteration scope as the
  innermost scope (ADR 0028). This is how the body resolves `currentItem`/`currentIndex` through the real
  expression evaluator: a body input bound to a `Variable` expression with
  `{ referenceKey: "currentItem", declaringScopeId: <ForEach node id> }` resolves to the current pass's
  item (the variable name is the reference key). This consumes the `#259` loop-scope primitive that the
  merged wiring (#296) connected into the execution path.

# Elsa.Activities.Compensation Extension Points

This module does not expose replaceable service contracts in v1. Its activity-owned contracts are:

- `CompensationScope.Step[{name}]` child slots (one per step, exactly one forward activity each)
- `CompensationScope.Compensation[{name}]` child slots (one per step, at most one handler each)
- `Done` and `Compensated` outcomes
- The `compensation.role`, `compensation.scopeActivityExecutionId`, `compensation.step`, `compensation.entryId` and
  `compensation.forwardActivityExecutionId` scheduling-metadata keys on the executions it schedules, and the
  `compensation` scheduling cause on handler executions. A handed-over handler also carries
  `compensation.originScopeActivityExecutionId` and `compensation.originEntryId`. The keys ride in both the request
  and the provenance metadata, so an operator retry rebuilt from provenance still routes. Inspection reads these keys.
- The `compensation.register`, `compensation.confirm` and `compensation.compensate` ancestor-notification codes a
  scope receives, addressed by activity type key `Elsa.Activities.Compensation.Activities.CompensationScope` with a
  stop at reusable-activity boundaries; their payloads carry identities and keys only (spec 258 data model).
- The `Confirm` and `Compensate` activities, with `scope` (an enclosing scope's node id) and, on Compensate, `reason`.
  The `scope` input carries the UI hint `compensation-confirm-target` or `compensation-compensate-target`, which
  Studio's target picker claims (spec 258 R23).
- The fault codes `compensation.scope.compensated`, `compensation.scope.compensate-requested` (category
  `compensation.target:{scopeNodeId}` while a cascade passes outward), `compensation.scope.uncertain-unhandled`
  (category `outcome-unknown`) and `compensation.scope.abandoned` (category `outcome-unknown`, spec 260: an operator
  abandoned the recovery).
- Operator decisions (spec 260, runtime seam R-5): a waiting scope offers `compensation.resolve.effect-absent`,
  `compensation.resolve.effect-accepted` and `compensation.abandon` on its intervention incident and implements
  `IRuntimeActivityInterventionDecisionHandler` to apply them. Entry status `ResolvedManually` and phase `Abandoned`
  are part of private state version 4.
- Descendant faults (spec 258 R-3): the scope implements `IRuntimeActivityDescendantFaultHandler`, so a fault under a
  Sequence or loop step reaches it as a fault of that step.

## Cross-domain contributions

- `CompensationScopeStructureHandler` implements `IActivityStructureHandler` (`Elsa.Workflows.Design.Core`). It
  projects two slots per step from the authored structure, compiles the executable structure (the ordered steps with
  their forward and handler node ids) and remaps it through publishing. Its `ChildrenCompletedBefore` tells expression
  authoring which results are guaranteed: a step sees the steps before it, and a handler sees its own step first, then
  the earlier steps, nearest first.
- `CompensationScopeValidator` implements `IDraftValidator` (`Elsa.Workflows.Design.Validations.Core`). It reports a
  blank or duplicate step name and a step without a forward activity, with path `{scopeNodeId}/steps/{index}`. The
  compile-time guard in the structure handler and the runtime navigator are backstops.
- `CompensationTargetValidator` implements `IDraftValidator`. It reports a Confirm or Compensate without an enclosing
  scope (`Compensation/NoEnclosingScope`), a literal `scope` that names no enclosing scope
  (`Compensation/TargetNotEnclosing`) and one placed in a compensation handler (`Compensation/InsideHandler`), with
  the node id as path. It resolves the node's catalog version through `IActivityDefinitionLookup`
  (`Elsa.Activities.Design.Core`).
- `CompensationHandlerVariableValidator` implements `IDraftValidator`. For a scope that can hand over, it reports a
  compensation handler that uses a variable declared by a container between that scope and the outermost scope its
  entries can reach (`Compensation/HandlerUsesContainerVariable`, path `{nodeId}/inputs/{key}`,
  `{nodeId}/outputs/{key}` or `{nodeId}/variable`). It reads container declarations through
  `IActivityStructureService.ProjectScopedVariables` and checks structured `Variable` references only; script
  expressions are not analyzed (spec 258 R18).
- `CompensationJournalProjector` implements `IActivityStateViewProjector` (`Elsa.Workflows.Runtime.Core`, spec 260
  R-6). Registered by `ActivitiesCompensationFeature` for private state `Elsa.CompensationScope.State`, it projects the
  journal view `compensation.journal` version 1 (phase, cause, entries with attempts, waits and offered decisions,
  resolutions, origins and confirmations, the entries still to undo, hand-over progress and the abandonment). It carries
  no captured value; the cause message needs the sensitive-values grant.

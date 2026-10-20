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
- The fault codes `compensation.scope.compensated`, `compensation.scope.compensate-requested` (category
  `compensation.target:{scopeNodeId}` while a cascade passes outward) and `compensation.scope.uncertain-unhandled`
  (category `outcome-unknown`).
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

# Elsa.Activities.Compensation Extension Points

This module does not expose replaceable service contracts in v1. Its activity-owned contracts are:

- `CompensationScope.Step[{name}]` child slots (one per step, exactly one forward activity each)
- `CompensationScope.Compensation[{name}]` child slots (one per step, at most one handler each)
- `Done` and `Compensated` outcomes
- The `compensation.role`, `compensation.scopeActivityExecutionId`, `compensation.step`, `compensation.entryId` and
  `compensation.forwardActivityExecutionId` scheduling-metadata keys on the executions it schedules, and the
  `compensation` scheduling cause on handler executions. Inspection reads these keys.

## Cross-domain contributions

- `CompensationScopeStructureHandler` implements `IActivityStructureHandler` (`Elsa.Workflows.Design.Core`). It
  projects two slots per step from the authored structure, compiles the executable structure (the ordered steps with
  their forward and handler node ids) and remaps it through publishing.
- `CompensationScopeValidator` implements `IDraftValidator` (`Elsa.Workflows.Design.Validations.Core`). It reports a
  blank or duplicate step name and a step without a forward activity, with path `{scopeNodeId}/steps/{index}`. The
  compile-time guard in the structure handler and the runtime navigator are backstops.

# Elsa.Activities.Switch Extension Points

This module does not expose replaceable service contracts in v1. Its activity-owned contracts are:

- `Switch.Case[{match}]` child slots (one per case, at most one branch activity each)
- `Switch.Default` child slot (at most one branch activity)
- A composite outcome per case match value, plus a `Default` outcome for the no-match branch

## Cross-domain contributions

- `SwitchStructureHandler` implements `IActivityStructureHandler` (`Elsa.Workflows.Design.Core`). It
  projects the per-case and default branch slots from the authored structure, compiles the executable
  structure (the ordered cases with their match values and branch node ids, plus the default branch node
  id), and round-trips both through publishing so the runtime resolves the matching branch without
  re-reading the design document. `Switch` is not a container scope, so it declares no container-scoped
  variables (`SupportsScopedVariables` defaults to `false`).

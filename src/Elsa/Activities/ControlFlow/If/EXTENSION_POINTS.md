# Elsa.Activities.If Extension Points

This module does not expose replaceable service contracts in v1. Its activity-owned contracts are:

- `If.Then` child slot (at most one branch activity)
- `If.Else` child slot (at most one branch activity)
- `True` / `False` composite outcomes

## Cross-domain contributions

- `IfStructureHandler` implements `IActivityStructureHandler` (`Elsa.Workflows.Design.Core`). It projects
  the two named branch slots from the authored structure, compiles the executable structure (the `Then` /
  `Else` branch node ids), and round-trips both through publishing so the runtime resolves the matching
  branch without re-reading the design document. `If` is not a container scope, so it declares no
  container-scoped variables (`SupportsScopedVariables` defaults to `false`).

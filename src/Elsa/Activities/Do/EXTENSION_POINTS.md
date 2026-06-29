# Elsa.Activities.Do Extension Points

This module does not expose replaceable service contracts in v1. Its activity-owned contracts are:

- `Do.Body` child slot (at most one body activity)
- A `Done` composite outcome emitted once the condition no longer holds or a body `Break` ends the loop
- A `Break` body outcome (matched by name) that ends the loop early
- A distinct engine `IterationId` per pass, threaded through the body child's
  `ActivitySchedulingProvenance.IterationId` (ADR 0028 / #259)

## Cross-domain contributions

- `DoStructureHandler` implements `IActivityStructureHandler` (`Elsa.Workflows.Design.Core`). It
  projects the body branch slot from the authored structure, compiles the executable structure (the body
  branch node id), and round-trips both through publishing so the runtime resolves the body without
  re-reading the design document. `Do` is a condition-only loop with no per-iteration item or index and
  is not a container scope, so it declares no container-scoped variables (`SupportsScopedVariables`
  defaults to `false`).

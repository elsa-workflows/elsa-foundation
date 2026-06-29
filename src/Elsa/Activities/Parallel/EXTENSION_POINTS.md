# Elsa.Activities.Parallel Extension Points

This module does not expose replaceable service contracts in v1. Its activity-owned contracts are:

- `Parallel.Branch[{name}]` child slots (one per branch, at most one branch activity each)
- A distinct engine `BranchId` per forked branch (`{compositeExecutionId}:parallel-branch:{branchNodeId}`)
- A `Done` composite outcome emitted once the join condition (default: all branches; optional threshold) is met

## Cross-domain contributions

- `ParallelStructureHandler` implements `IActivityStructureHandler` (`Elsa.Workflows.Design.Core`). It
  projects the per-branch slots from the authored structure, compiles the executable structure (the ordered
  branches with their names and branch node ids, plus the optional join threshold), and round-trips both
  through publishing so the runtime forks/joins without re-reading the design document. `Parallel` is not a
  container scope, so it declares no container-scoped variables (`SupportsScopedVariables` defaults to
  `false`).

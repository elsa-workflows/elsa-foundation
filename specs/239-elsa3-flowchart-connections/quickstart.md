# Quickstart: verify spec 239

Run from the worktree root. Build and test only the touched projects (shared machine).

```bash
dotnet test Extensions/Elsa3/tests/Mapping/Tests --filter "FullyQualifiedName~Flowchart"
```

```bash
dotnet test Extensions/Elsa3/tests/Mapping/Tests
```

Expected:
- Every flowchart fixture in `Fixtures/Flowcharts` converts with 0 errors and passes structure handling and value-flow
  validation (SC-002, SC-004).
- The multi-trigger validation tests are reported as skipped with the reason "needs spec 238 (#2850)" until H-1 is
  cleared.
- One test per new code produces exactly that code (SC-003).
- No test asserts `ELS3-ACTIVITY-GRAPH-CONVERSION-PENDING` as an outcome (SC-001).

Manual corpus check (SC-006), outside the repository:
- Convert each of the 36 JTest definitions in `JTEST-nexxbiz/workflow-definitions` through the single-definition
  importer (or a scratch test harness pointed at that folder; do not commit the definitions).
- Record per file: converted, or the list of codes. Expected graph refusals: `branching-fork-parallel` and
  `branching-wait-any` (`GRAPH-JOIN-UNSUPPORTED`). Other refusals are spec 236 or #2717 causes.
- Paste the table into the PR description.

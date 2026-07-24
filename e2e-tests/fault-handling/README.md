# fault-handling — faults, propagation, terminal status

Backend REST tests for how activity faults behave. Shared helper: `_FaultCommon.ps1`. Runs against a from-source
`Elsa.Server` (see ../README.md).

## Scripts

| Script | What it exercises |
|--------|-------------------|
| `Test-FaultActivity.ps1` | the `Fault` activity ends the workflow `Faulted`, skips downstream, records 1 `Blocking`/`WaitForIntervention` incident (`failureType=ActivityReturnedFault`, custom message) |
| `Test-NestedFaultPropagation.ps1` | a fault inside a nested `Sequence` propagates up and aborts the whole run; everything after the fault is skipped |
| `Test-FaultVsFinish.ps1` | contrast: `Fault` -> `Faulted` (1 incident) vs `Finish` -> `Completed` (0 incidents); both skip downstream |

## Notes / findings

- Foundation has a dedicated **`Fault` activity** (`Elsa.Activities.Primitives.Activities.Fault`, input `message`) — the deterministic way to fault; it records a clean `ActivityReturnedFault` incident.
- Every fault currently records **`WaitForIntervention`** (`Blocking`) — there is no incident-strategy layer to retry/continue/suspend (issue #1015). These tests assert the fault/propagation behaviour that exists.
- There is **no catch/continue** construct: an uncaught fault aborts the run.
- Catalog note (current `main`): **`Parallel` is not in the activity catalog**, so parallel-branch fault isolation isn't testable here — and the committed `branching/Test-ParallelFork.ps1` references an activity that the default server no longer composes (flagged separately).

# Contract: variable lifecycle inspection API

All routes are under the existing runtime prefix and use the existing activity-execution inspection permission.
Values are never inline: a `ValueStateView` with `state: captured` carries an `evidenceId` for the existing authorized
payload read. Paging uses `after` (a commit sequence) and `take` (default 100, max 1000), and returns `next`.

| Method | Route | Returns |
|---|---|---|
| GET | `runtime/workflows/instances/{workflowExecutionId}/variables` | `VariableSummaryView[]` |
| GET | `runtime/workflows/instances/{workflowExecutionId}/variable-writes?scopeId&variableKey&source&outcome&overwrites&after&take` | page of `VariableWriteEntryView` |
| GET | `runtime/workflows/instances/{workflowExecutionId}/journal?after&take` | page of `JournalCommitView` |
| GET | `runtime/workflows/instances/{workflowExecutionId}/compare/{otherWorkflowExecutionId}` | `InstanceComparisonView` |
| GET | `runtime/workflows/instances/{workflowExecutionId}` (existing) | + `lostUpdateWarningCount`, `concurrentOverwriteCount` |

Filters: `outcome` ∈ `applied`, `lostUpdateWarning`, `lostUpdateFault`, `acceptedOverride`; `overwrites=true` keeps only concurrent
overwrites; `source` ∈ `capture`, `intrinsic`, `sessionChange`, `alteration`, `backgroundResult`,
`acceptedCompletion`.

Problems (existing problem shapes):
- `404` instance not found (either side of a comparison).
- `409` comparison of different executable versions, with reason `differentExecutable`.
- `403` without inspection permission.

Stability: the views are new public contracts of the runtime API; members are added, never renamed, until the
initiative lands.

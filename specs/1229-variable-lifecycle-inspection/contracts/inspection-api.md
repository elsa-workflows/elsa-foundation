# Contract: variable lifecycle inspection API

All routes are under the existing runtime prefix and use the existing activity-execution inspection permission.
Values are never inline: a `ValueStateView` with `state: captured` carries a `payloadRef` for the authorized payload
read. Paging uses `cursor` (opaque: commit sequence and position within the commit) and `take` (default 100, max
1000), and returns `next`.

| Method | Route | Returns |
|---|---|---|
| GET | `runtime/workflows/instances/{workflowExecutionId}/variables` | `VariableSummaryView[]` |
| GET | `runtime/workflows/instances/{workflowExecutionId}/variable-writes?frameId&scopeId&variableKey&source&outcome&overwrites&cursor&take` | page of `VariableWriteEntryView` |
| GET | `runtime/workflows/instances/{workflowExecutionId}/journal?cursor&take` | page of `JournalCommitView` |
| GET | `runtime/workflows/instances/{workflowExecutionId}/compare/{otherWorkflowExecutionId}` | `InstanceComparisonView` |
| GET | `runtime/workflows/instances/{workflowExecutionId}/variable-writes/{entryId}/payload?value=written\|overwritten\|refused` | the value, under the payload permission |
| GET | `runtime/workflows/instances/{workflowExecutionId}` (existing) | + `lostUpdateWarningCount` |

Filters on `variable-writes`: `outcome` ∈ `applied`, `lostUpdateWarning`, `lostUpdateFault`, `acceptedOverride`;
`overwrites=true` keeps only concurrent overwrites and returns `concurrentOverwriteTotal`; `source` ∈ `capture`,
`intrinsic`, `sessionChange`, `alteration`, `backgroundResult`, `acceptedCompletion`.

Problems (existing problem shapes):
- `404` instance or entry not found (either side of a comparison).
- `409` comparison of different executable versions, with reason `differentExecutable`.
- `403` without inspection permission, or without payload permission on the payload route.

Stability: the views are new public contracts of the runtime API; members are added, never renamed.

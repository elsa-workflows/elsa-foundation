# Runtime checkpoint committer callers

Status: reviewed inventory for #1133 T015 (2026-08-05).

This is the exact no-bypass input for T016's parameterized caller-coverage test. It inventories
production calls to `RuntimeCheckpointCommitter.CommitAsync`; it excludes constructor injection,
service registration, comments, indirect helper calls, and `IRuntimeCheckpointCommitStore` calls.
The checked source pattern is `(_checkpointCommitter!?|checkpointCommitter|request.CheckpointCommitter).CommitAsync(`.

- Production call sites: **28**
- Production source files: **22**
- Runtime source changes for this inventory: **none**

Before any Runtime implementation change, re-run the source pattern, update this manifest for any
intentional caller change, and keep T016's no-bypass matrix synchronized with every row. A caller
not represented here is not covered by the T016 acceptance gate.

| # | Classification | Direct caller |
|---:|---|---|
| 1 | Activity-attempt activation claim | `src/Elsa/Activities/Runtime/Services/ActivityAttemptActivationClaimer.cs:422` |
| 2 | Activity-cancellation checkpoint | `src/Elsa/Activities/Runtime/Services/ActivityCancellationCheckpointService.cs:107` |
| 3 | Incident: activity-fault recorder | `src/Elsa/Activities/Runtime/Services/ActivityFaultIncidentRecorder.cs:113` |
| 4 | Activity invocation path | `src/Elsa/Activities/Runtime/Services/WorkflowInvokeActivitySchedulerWorkHandler.cs:1030` |
| 5 | Activity invocation path | `src/Elsa/Activities/Runtime/Services/WorkflowInvokeActivitySchedulerWorkHandler.cs:1170` |
| 6 | Activity invocation path | `src/Elsa/Activities/Runtime/Services/WorkflowInvokeActivitySchedulerWorkHandler.cs:903` |
| 7 | Activity-parent notification path | `src/Elsa/Activities/Runtime/Services/WorkflowNotifyParentActivitySchedulerWorkHandler.cs:528` |
| 8 | Activity-parent notification path | `src/Elsa/Activities/Runtime/Services/WorkflowNotifyParentActivitySchedulerWorkHandler.cs:598` |
| 9 | Activity-parent completion path | `src/Elsa/Activities/Runtime/Services/WorkflowParentActivityCompletionSchedulerWorkHandler.cs:1027` |
| 10 | Activity-parent completion path | `src/Elsa/Activities/Runtime/Services/WorkflowParentActivityCompletionSchedulerWorkHandler.cs:937` |
| 11 | Checkpoint middleware: activity pipeline | `src/Elsa/Workflows/Runtime/Middleware/RuntimeActivityCheckpointMiddleware.cs:35` |
| 12 | Checkpoint middleware: workflow pipeline | `src/Elsa/Workflows/Runtime/Middleware/RuntimeWorkflowCheckpointMiddleware.cs:23` |
| 13 | Alteration checkpoint writer | `src/Elsa/Workflows/Runtime/Services/Alterations/RuntimeWorkflowAlterationCheckpointWriter.cs:112` |
| 14 | Incident: blocking workflow-fault observer | `src/Elsa/Workflows/Runtime/Services/BlockingIncidentWorkflowFaultObserver.cs:139` |
| 15 | Bookmark-consumption checkpoint | `src/Elsa/Workflows/Runtime/Services/BookmarkConsumptionCheckpointService.cs:146` |
| 16 | Synthetic coalescing flush | `src/Elsa/Workflows/Runtime/Services/Coalescing/RuntimeCoalescingDrainScopeFactory.cs:66` |
| 17 | Incident-resolution batch | `src/Elsa/Workflows/Runtime/Services/IncidentResolutionBatchExecutor.cs:172` |
| 18 | Incident: poisoned scheduler-work observer | `src/Elsa/Workflows/Runtime/Services/PoisonedSchedulerWorkIncidentObserver.cs:186` |
| 19 | Direct scheduler handler: cancel activity scope | `src/Elsa/Workflows/Runtime/Services/WorkflowCancelActivityScopeSchedulerWorkHandler.cs:26` |
| 20 | Direct scheduler handler: cancel workflow | `src/Elsa/Workflows/Runtime/Services/WorkflowCancelSchedulerWorkHandler.cs:49` |
| 21 | Direct scheduler handler: checkpoint | `src/Elsa/Workflows/Runtime/Services/WorkflowCheckpointSchedulerWorkHandler.cs:71` |
| 22 | Direct scheduler handler: create bookmark | `src/Elsa/Workflows/Runtime/Services/WorkflowCreateBookmarkSchedulerWorkHandler.cs:61` |
| 23 | Direct scheduler handler: retry activity boundary | `src/Elsa/Workflows/Runtime/Services/WorkflowRetryActivityBoundarySchedulerWorkHandler.cs:26` |
| 24 | Direct scheduler handler: schedule activity fused path | `src/Elsa/Workflows/Runtime/Services/WorkflowScheduleActivitySchedulerWorkHandler.cs:122` |
| 25 | Direct scheduler handler: schedule activity ordinary path | `src/Elsa/Workflows/Runtime/Services/WorkflowScheduleActivitySchedulerWorkHandler.cs:61` |
| 26 | Direct scheduler handler: start activity fused path | `src/Elsa/Workflows/Runtime/Services/WorkflowStartActivitySchedulerWorkHandler.cs:177` |
| 27 | Direct scheduler handler: start activity ordinary path | `src/Elsa/Workflows/Runtime/Services/WorkflowStartActivitySchedulerWorkHandler.cs:73` |
| 28 | Direct scheduler handler: start workflow | `src/Elsa/Workflows/Runtime/Services/WorkflowStartSchedulerWorkHandler.cs:147` |

The inventory deliberately includes the two checkpoint middlewares, all incident and bookmark
paths, alteration handling, the activity-parent completion/notification paths, and the synthetic
coalescing flush. Those are separate production routes; none may be collapsed into a representative
handler test.

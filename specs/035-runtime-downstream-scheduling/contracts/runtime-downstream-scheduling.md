# Contract: Runtime Downstream Scheduling

## Completion Continuation

`WorkflowCompleteActivitySchedulerWorkHandler` handles `SchedulerCompletionKind.ContinuationScheduling`.

1. Load the pinned `WorkflowExecutable`.
2. Find outgoing `ExecutableEdge` records where:
   - `SourceNodeId` equals the completed executable node ID.
   - `SourcePort` matches one of the completion outcome names.
3. Create deterministic `ScheduleActivity` scheduler work for each matching target node.
4. Attach those work items as checkpoint post-commit intents.
5. Enqueue the `ActivityCompleted` checkpoint work.

No matching edges produces no downstream scheduler intents.

## Checkpoint Commit

`WorkflowCheckpointSchedulerWorkHandler` copies checkpoint payload post-commit intents into `RuntimeCheckpointCommit.PostCommitIntents`.

`RuntimeCheckpointCommitter` dispatches those intents only after the checkpoint writer succeeds.

## Default Dispatcher

`RuntimeSchedulerPostCommitIntentDispatcher` handles scheduler-work intents by deserializing the scheduler work item payload and enqueuing it through `IWorkflowSchedulerWorkQueue`.

Unsupported intent kinds fault dispatch so misconfigured post-commit behavior is visible.

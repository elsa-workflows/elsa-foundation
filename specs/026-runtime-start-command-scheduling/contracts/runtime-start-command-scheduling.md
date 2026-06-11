# Contract: Runtime Start Command Scheduling

This slice makes `WorkflowExecutionCommandKind.Start` scheduler work meaningful without executing activities.

## Handler Behavior

`WorkflowStartSchedulerWorkHandler`:

1. Accepts only `RuntimeSchedulerWorkItem` values whose `CommandKind` is `Start`.
2. Requires a `WorkflowExecutionStartCommandPayload`.
3. Loads `RequestedArtifactId` through `IWorkflowExecutableStore`.
4. Confirms the loaded artifact identity equals `PinnedExecutable`.
5. Enqueues one `ScheduleActivity` scheduler work item per artifact start node.

## Guarantees

- Scheduled work is keyed by `WorkflowExecutionId`.
- Scheduled work references executable node IDs, not authored Design activity nodes.
- Invalid start command data faults through the scheduler drain result.
- The handler does not invoke activities, write checkpoints, or process bookmarks.

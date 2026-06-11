# Contract: Runtime Completion Checkpoint Enqueue

## Checkpoint Work

`RuntimeCheckpointCommandPayload` represents scheduler work for a named runtime checkpoint boundary.

- `PinnedExecutable`: exact executable artifact snapshot tied to the workflow execution.
- `CheckpointName`: named runtime boundary, such as `RuntimeCheckpointNames.ActivityCompleted`.
- `ActivityExecutionIds`: activity executions associated with the checkpoint boundary.
- `Reason`: stable runtime reason for the scheduler work.

## Dispatch

`WorkflowCompleteActivitySchedulerWorkHandler` enqueues `WorkflowExecutionCommandKind.Checkpoint` when it handles `ContinuationScheduling` completion work. `WorkflowCheckpointSchedulerWorkHandler` validates checkpoint work and accepts it only. Invoking `RuntimeCheckpointCommitter` is out of scope for this slice.

# Contract: Runtime Activity Output Capture

## Activity Invocation

For each executable node output capture declaration, the invoke handler supplies an `OutputArgument` keyed by the runtime output name during activity construction.

After successful activity execution:

- recorded outputs are published to `IRuntimeActivityOutputRegister` as `ActiveActivityOutput`;
- captures with `CaptureOnSuccessfulCompletion` are committed as `DurableValueState` through a `DurableValueCaptured` checkpoint;
- normal activity completion work is then enqueued.

If the activity faults, is skipped, or requests durable bookmark suspension, no successful-completion output publication or capture is produced.

## Identity

Output publication and durable capture use:

- `WorkflowExecutionId`
- `ActivityExecutionId`
- executable node output name

Authored activity IDs and history/audit output snapshots are not runtime continuation sources.

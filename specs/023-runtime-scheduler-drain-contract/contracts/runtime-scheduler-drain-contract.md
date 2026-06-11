# Runtime Scheduler Drain Contract

This slice adds a deterministic drain boundary for scheduler work.

Required guarantees:

- Scheduler work is drained by `WorkflowExecutionId`.
- Work is dispatched in queue order.
- A configured maximum limits how many work items one drain call handles.
- Handler faults are surfaced in per-item results and stop the drain.
- The default handler acknowledges work without executing activities or writing checkpoints.

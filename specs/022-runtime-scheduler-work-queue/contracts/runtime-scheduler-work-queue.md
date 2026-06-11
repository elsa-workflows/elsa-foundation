# Runtime Scheduler Work Queue Contract

This slice adds the first command-processing default after the in-process execution agent provider.

Required guarantees:

- Accepted command envelopes can be recorded as scheduler work.
- Scheduler work is isolated by `WorkflowExecutionId`.
- Per-workflow insertion order is preserved.
- Queue insertion is idempotent by work item ID within each workflow execution.
- The default command processor records scheduler work only; scheduler drain and activity execution remain later slices.

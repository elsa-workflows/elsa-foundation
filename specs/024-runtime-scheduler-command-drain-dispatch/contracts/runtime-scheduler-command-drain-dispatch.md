# Runtime Scheduler Command Drain Dispatch Contract

This slice connects accepted command processing to scheduler draining.

Required guarantees:

- The command processor records scheduler work before drain dispatch.
- Drain dispatch is policy-controlled.
- The default policy drains the same workflow execution immediately.
- Drain results can be observed without making history/audit continuation state.
- Handler faults represented in drain results do not turn mailbox acceptance into rejection.

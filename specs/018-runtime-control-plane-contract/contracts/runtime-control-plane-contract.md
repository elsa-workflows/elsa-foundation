# Runtime Control Plane Contract

This slice introduces runtime contracts for administrative pause/unpause.

Required contract guarantees:

- Pause/unpause are control-plane operations, not durable suspension/resume.
- Workflow execution pause is cooperative and takes effect at named safe boundaries.
- Pause state is represented by `ControlPlaneState`.
- Scheduler decisions can name a safe boundary and a continuation policy.
- Ingress defaults are source-specific.
- Command names keep `UnpauseWorkflowExecution`, `ResumeBookmark`, and `ContinueVolatileWait` distinct.

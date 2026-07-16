# DispatchWorkflow runtime

This module owns the Foundation-native `DispatchWorkflow` activity and its child-start post-commit handler. Enable the `ActivitiesDispatchWorkflowRuntime` shell feature; it has a hard dependency on `WorkflowsRuntimeResumption` so cross-execution child starts are delivered by the global pump, outside workflow actor mailboxes.

#676 supports fire-and-forget dispatch against the exact live Published source pinned into the parent executable. The parent completion checkpoint atomically records the `Dispatched` result, deterministic child ID, Pending dispatch record, and start intent. `WaitForCompletion=true` is rejected until #679.

The in-memory provider proves asynchronous semantics and replay convergence within one process. It is not process-crash durable. Groundwork explicitly rejects workflow-dispatch state until #678 adds its persistence and restart-convergence capability.

See [EXTENSION_POINTS.md](EXTENSION_POINTS.md) and the feature specification under `specs/096-dispatch-workflow-fire-and-forget/`.

# DispatchWorkflow runtime

This module owns the Foundation-native `DispatchWorkflow` activity and its child-start post-commit handler. Enable the `ActivitiesDispatchWorkflowRuntime` shell feature; it has a hard dependency on `WorkflowsRuntimeResumption` so cross-execution child starts are delivered by the global pump, outside workflow actor mailboxes.

#676 supports fire-and-forget dispatch against the exact live Published source pinned into the parent executable. The parent completion checkpoint atomically records the `Dispatched` result, deterministic child ID, Pending dispatch record, and start intent. `WaitForCompletion=true` is rejected until #679.

#677 makes that pin deterministic and retained. Publication validates the child artifact's versioned workflow-input contract and records an exact child artifact ID/hash dependency bound to the dispatch node. Runtime validates realized dynamic inputs before checkpoint staging, materializes supported literal defaults, and starts the retained child through immutable parent-artifact/node authority even after the child's source is replaced or unpublished. Typed runtime context such as tenant and authority never comes from the workflow input bag.

Cross-workflow dispatch increments durable `DispatchNestingDepth` exactly once. Root starts and legacy payloads begin at zero; the default maximum child depth is 32 and hosts can configure a positive alternative through `DispatchWorkflowRuntimeFeature.MaxNestingDepth`. Delivery and replay preserve the staged depth and recheck the configured limit before materialization.

The in-memory provider proves asynchronous semantics and replay convergence within one process. It is not process-crash durable. Groundwork explicitly rejects workflow-dispatch lifecycle state until #678 adds its persistence and restart-convergence capability; Groundwork already persists executable dependency metadata and closure-wide artifact lease fencing from #677.

The runtime feature registers the default allow start policy. Hosts may replace `IWorkflowExecutableStartPolicy` with exactly one implementation to deny future child materialization using immutable executable/authority context. Policy denial does not rewrite the artifact or affect already-materialized execution state.

This slice does not add WorkflowDefinitionActivity, Studio UI, broker or MassTransit selection, waited completion, cancellation propagation, lifecycle observation, redrive, test-scope dispatch, or distributed placement.

See [EXTENSION_POINTS.md](EXTENSION_POINTS.md), the fire-and-forget specification under `specs/096-dispatch-workflow-fire-and-forget/`, and the deterministic dependency specification under `specs/097-dispatch-dependency-hardening/`.

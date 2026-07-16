# DispatchWorkflow runtime

This module owns the Foundation-native `DispatchWorkflow` activity and its child-start post-commit handler. Enable the `ActivitiesDispatchWorkflowRuntime` shell feature; it has a hard dependency on `WorkflowsRuntimeResumption` so cross-execution child starts are delivered by the global pump, outside workflow actor mailboxes.

#676 supports fire-and-forget dispatch against the exact live Published source pinned into the parent executable. The parent completion checkpoint atomically records the `Dispatched` result, deterministic child ID, Pending dispatch record, and start intent.

#677 makes that pin deterministic and retained. Publication validates the child artifact's versioned workflow-input contract and records an exact child artifact ID/hash dependency bound to the dispatch node. Runtime validates realized dynamic inputs before checkpoint staging, materializes supported literal defaults, and starts the retained child through immutable parent-artifact/node authority even after the child's source is replaced or unpublished. Typed runtime context such as tenant and authority never comes from the workflow input bag.

Cross-workflow dispatch increments durable `DispatchNestingDepth` exactly once. Root starts and legacy payloads begin at zero; the default maximum child depth is 32 and hosts can configure a positive alternative through `DispatchWorkflowRuntimeFeature.MaxNestingDepth`. Delivery and replay preserve the staged depth and recheck the configured limit before materialization.

The in-memory provider proves asynchronous semantics and replay convergence within one process. It is not process-crash durable. Groundwork persists executable dependency metadata, closure-wide artifact lease fencing, dispatch lifecycle state, and post-commit delivery evidence.

The runtime feature registers the default allow start policy. Hosts may replace `IWorkflowExecutableStartPolicy` with exactly one implementation to deny future child materialization using immutable executable/authority context. Policy denial does not rewrite the artifact or affect already-materialized execution state.

#678 persists dispatch records, retained executable dependencies, and post-commit outbox state through the Groundwork runtime checkpoint transaction. The provider-backed stores preserve deterministic identities, fenced claim completion, and authenticated inspection across process restart.

#679 adds successful `WaitForCompletion=true` execution. The parent wait checkpoint atomically records its non-expiring bookmark, suspended activity, Pending dispatch record, child ID output, and child-start intent. A successful child terminal checkpoint records the Completed dispatch projection and one deterministic parent-resume intent containing only policy-safe outputs. Resume delivery is performed by the global post-commit pump and retries with positive backoff until bookmark consumption is durably observable; duplicate delivery before or after consumption converges without a second logical completion. Unbounded retries emit payload-free structured warnings for operational alerting.

Fault and cancellation propagation, retry exhaustion/dead-letter/redrive, TestRun behavior, and distributed two-node delivery remain owned by #680, #681, #682, and #683 respectively.

See [EXTENSION_POINTS.md](EXTENSION_POINTS.md) and the feature specifications under `specs/096-dispatch-workflow-fire-and-forget/` through `specs/099-dispatch-wait-success/`.

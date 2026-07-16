# Extension points — DispatchWorkflow runtime

## `DispatchWorkflowRuntimeFeature`

- Shell feature: `ActivitiesDispatchWorkflowRuntime`.
- Depends on `WorkflowsRuntimeResumption`.
- Contributes `ChildStartExecutor` for the stable `Elsa.Activities.DispatchWorkflow.StartChild` post-commit intent kind.
- Contributes `ParentResumeExecutor` for the stable `Elsa.Activities.DispatchWorkflow.ResumeParent` kind with an unbounded, positive-backoff retry policy. The aggregate dispatcher owns `DispatchAsync`; contributed handlers own `HandleAsync`, and each registration is the sole source of its kind and retry policy.

## Shared runtime contracts

The module stages `WorkflowDispatchCheckpointRequest` through the additive `IWorkflowDispatchStagingContext` capability implemented by the Foundation runtime context. The runtime engine, rather than the activity, owns folding the dispatch record and intent into the activity-completed checkpoint. Providers replace `IWorkflowDispatchStore` alongside their checkpoint persistence implementation; a provider must never ignore non-empty `WorkflowDispatches` state changes.

The activity has no transport selector. `ChildStartExecutor` delegates to `IWorkflowStartDispatcher`, which retains authority over exact-source gating and actor-provider selection.
The committed child-start payload carries the parent's immutable retained pin and one replay-stable dispatch depth. Retained-pin starts may use the exact child after its source is replaced or unpublished, but still pass contributed runtime start policies and the configured depth limit before actor acquisition. Realized child inputs are checked against the declared-input snapshot before the dispatch checkpoint is staged.

Configure `DispatchWorkflowOptions.MaxNestingDepth` through the standard options surface. The default maximum child depth is 32, values must be positive, root and legacy starts begin at zero, and one DispatchWorkflow edge increments depth exactly once.

Successful wait mode stages a deterministic, non-expiring completion bookmark through the runtime checkpoint. `WorkflowDispatchCompletionEnricher` turns a successful child terminal checkpoint into one deterministic parent-resume intent. It uses `IWorkflowOutputSource` so only policy-safe terminal outputs cross the execution boundary; redacted outputs retain metadata but never their value. If that intent was already committed, the enricher retrieves it through `IPostCommitOutboxLookupStore` and reuses the exact durable representation instead of recapturing outputs under a changed policy.

`ParentResumeExecutor` dispatches the replay-stable bookmark stimulus and then verifies the exact parent workflow, activity, and bookmark state. Missing state is acknowledged only when durable state proves the parent activity or workflow terminal; otherwise delivery remains retryable. The activity callback accepts only its deterministic target and identity tuple, then materializes the successful child result. Fault and cancellation result shapes are deliberately deferred to #680.

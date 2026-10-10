# Contract: Runtime Post-Commit Intent Handler Contribution

## Handler

Each handler accepts one committed `RuntimePostCommitIntent` plus cancellation. It returns only after its delivery-side effect has completed. Exceptions are not swallowed by the composite; the existing outbox processor classifies and records them.

## Registration

Runtime modules use one generic service-collection extension to register a stable, non-empty intent kind with a handler type. Repeating the same extension call for the same kind and handler type is idempotent. Built-in scheduler delivery uses this same extension.

## Resolution rules

1. Compare intent kinds ordinally.
2. Collapse repeated contributions of the same handler type and intent kind.
3. Reject blank kinds.
4. Reject a kind claimed by multiple distinct handler types; the error names the kind and sorted handler identities.
5. Dispatch an intent only to the single handler mapped to its exact kind.
6. Throw an actionable unsupported-kind error when no handler is mapped. The existing outbox policy, not the handler contract, selects the persisted failed state.

## Delivery boundary

Handlers are invoked by the post-commit outbox processor. The global runtime resumption sweep queries all deliverable kinds and invokes the processor outside workflow execution actor mailboxes. Checkpoint commit records work but never invokes a handler inline.

## Scheduler compatibility

The built-in scheduler handler retains the current payload deserialization and validation contract, workflow-execution identity check, and `IWorkflowSchedulerWorkQueue.EnqueueAsync` call. Persisted intent and work-item identities are unchanged.

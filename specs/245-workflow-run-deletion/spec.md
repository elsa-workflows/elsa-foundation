# Individual and bulk workflow-run deletion

**Feature Branch**: `agent/codex/2978-workflow-run-deletion`
**Created**: 2026-10-18
**Status**: Implemented; PR verification pending
**Input**: Owner request #2978. Related retention policy work: #1770. Program bucket: none/free-flow.

An operator can select exact runs, review a confirmation, and delete one or several runs from history. This is explicit manual deletion, not an age-based retention sweep. Selection is limited to the visible page and is reset when the backend, query or page changes. Failed items remain clearly retryable; partial failure cannot become an all-success message.

Deletion goes through a manage-authorized runtime boundary, with tenant and resource authorization before mutation. The provider atomically revalidates a read candidate and its terminal status, publishes the existing ownership deletion fence, clears execution-owned historical/inspection state, and removes the main execution row last. Unsupported or mixed persistence compositions do not advertise the operation. Atomicity is against the canonical checkpoint write protocol and retained scheduler delivery; independent raw sidecar writes must honor checkpoint ownership. Failed cleanup must leave a retryable retained run rather than report deletion complete.

Keep the liveness tombstone and operational checkpoint, outbox, background-work and delivery receipts required to reject replay or settle already committed intents. Shared executable artifacts, activation/recurring trigger definitions, unselected child runs and separately managed execution evidence are not deletion targets. Linked dispatch records retain their existing two-sided retention/redrive contract. This operation is not a promise of erasing every operational audit record or undoing external effects.

The owner selected stop-then-delete for active runs. Request cancellation through the ordinary execution mailbox, wait for a terminal checkpoint, reload and reauthorize the candidate, then purge. Acceptance of the cancellation command alone is not deletion success. A bounded timeout leaves the run retained and explains how to retry.

Acceptance: single/bulk confirmation and cancellation, exact ID selection, bounded requests, truthful partial errors, capability gating, permission denial, tenant isolation, repeated/missing IDs, concurrent state changes, provider atomic rollback/fencing, retained shared resources, keyboard operation, and narrow layouts. Tests use disposable data, never the owner's demo runs.

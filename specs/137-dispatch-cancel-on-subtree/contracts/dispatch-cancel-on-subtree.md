# Contract: Dispatch Cancellation on Local Activity Teardown

## Checkpoint trigger contract

`WorkflowDispatchCancellationEnricher` derives child-cancellation responsibility when either:

1. the checkpoint transitions the whole parent workflow to `Cancelled`; or
2. the checkpoint upserts the exact dispatch-owning activity execution to `Cancelled`.

If neither signal exists, enrichment is a no-op and does not query dispatch records.

## Exact ownership contract

Local cancellation authority is scoped by ordinal equality between the cancelled activity-execution ID and `WorkflowDispatchRecord.ParentActivityExecutionId`. Cancelling one activity does not grant authority over sibling dispatches under the same workflow execution.

## Eligibility contract

The existing contract remains unchanged after ownership selection:

- only `WaitForCompletion` dispatches participate;
- effective cancellation propagation must be enabled;
- active or cancellation-marked dispatches participate;
- terminal state suppresses new work unless committed-outbox replay proves an equivalent responsibility already exists.

## Determinism and replay contract

Whole-parent and local cancellation use the same cancellation request and child-cancel intent identity. A checkpoint containing both signals produces one request and one intent per dispatch. Existing equivalence and conflict checks remain authoritative.

Every parent dispatch page is inspected in stable order. A page with no local-owner match does not terminate discovery. A provider cursor that does not advance remains an invariant failure.

## Delivery contract

Provider resolution, admission races, child visibility retry, actor Cancel delivery, terminal acknowledgement, and late resume absorption remain governed by [work unit 100](../../100-dispatch-fault-cancellation/contracts/dispatch-fault-cancellation.md).

## Compatibility contract

- No public API, activity input, output, outcome, feature registration, provider capability, or persisted schema changes.
- Whole-parent cancellation behavior remains unchanged.
- Fire-and-forget and propagation opt-out behavior remains unchanged.
- No BPMN-specific hook or dependency is introduced.

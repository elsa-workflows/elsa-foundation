# Research: Durable and Inspectable Detached Dispatch

## Decision 1: Persist dispatches as a first-class runtime document

**Decision**: Add a `workflowDispatch` Groundwork storage unit and `GroundworkWorkflowDispatchStore`. Its envelope projects collection, parent execution ID, child execution ID, lifecycle status, tenant ID, and the complete safe runtime record.

**Rationale**: A dedicated document gives atomic checkpoint participation, exact identity, bounded inspection indexes, lifecycle concurrency, and retention enumeration without coupling dispatch to parent execution JSON.

**Alternatives rejected**:

- Embed records only in parent state: child lifecycle must outlive and update independently of the parent.
- Infer lifecycle from outbox/execution state on every read: loses committed historical linkage and safe capture metadata.
- Add provider-specific tables: violates Groundwork manifest ownership and provider parity.

## Decision 2: Reuse the checkpoint unit-of-work

**Decision**: Add the dispatch document kind to `RuntimeCheckpointCommitScope`, create the dispatch store over the transactional `DocumentUnitOfWorkStore`, validate transitions there, and apply dispatch changes before the marker commit.

**Rationale**: The existing writer already guarantees state + outbox + marker all-or-nothing with replay reconciliation. Extending that boundary is smaller and stronger than a second transaction.

## Decision 3: Centralize lifecycle validation

**Decision**: Add one runtime-core lifecycle helper/service that validates immutable identity equality, timestamp monotonicity, legal forward transitions, and idempotent equality. In-memory and Groundwork stores use the same rules.

**Rationale**: #676 currently embeds transition validation in only the in-memory checkpoint writer. Durable direct Started updates and terminal checkpoint updates need identical behavior.

Checkpoint ownership is status-sensitive: Pending creation belongs to the parent execution; Started/terminal checkpoint projection belongs to the exact child execution. The existing parent-only validation must be replaced by this rule in both in-memory and Groundwork writers so unrelated commits cannot mutate lifecycle.

## Decision 4: Repair Started through fully deterministic redelivery

**Decision**: Keep the public dispatch-start request unchanged. `WorkflowStartDispatcher` already validates the committed parent dispatch for retained-pin authorization; carry its dispatch ID privately and derive stable internal command, envelope, scheduler-work, and root-activity identities from it. Before enqueue, an exact existing child execution may short-circuit as Duplicate; conflicting identity or context fails closed. After Accepted or exact Duplicate, update the dispatch to Started. A crash after materialization is repaired by byte-equivalent redelivery.

**Rationale**: Child materialization occurs through a separate committed execution path. A deterministic child execution ID alone is insufficient because current replay creates fresh internal identities and the actor idempotency cache is process-local. Stable private materialization identities plus durable queue convergence provide the repair bridge without a new caller-controlled identity surface.

## Decision 4a: Add the missing outbox delivery claim

**Decision**: Add a separate `IRuntimePostCommitOutboxClaimStore` so `IRuntimePostCommitOutboxStore` and existing model constructors remain source compatible. It atomically claims Pending or expired Delivering items. A claim carries owner ID, monotonically increasing fencing token, and visibility expiry. Claim-aware acknowledgement/failure requires the current claim; stale owners are rejected. The coalescing adapter forwards claim operations correctly both inside and outside an active session. In-memory uses the shared process state lock and Groundwork uses optimistic document concurrency.

**Rationale**: The current processor reads and acknowledges without ownership. #678 explicitly requires a crash boundary at the outbox lease, so restart convergence cannot be proven without a real claim/visibility contract.

## Decision 5: Enrich terminal child checkpoints before fingerprinting

**Decision**: Add an optional runtime checkpoint enricher invoked before outbox folding and fingerprint persistence. When the checkpoint carries terminal child execution state, it always appends the same linked terminal change, deriving status and `UpdatedAt` from the checkpoint even if the stored dispatch is already terminal. This makes replay fingerprints identical. A terminal record supersedes a later Started update, which becomes a safe no-op.

**Rationale**: The lifecycle change then shares the child checkpoint transaction and replay fingerprint. Provider-specific terminal inference inside Groundwork would diverge from in-memory behavior.

**Scope note**: This mirrors already-established child Completed/Faulted/Cancelled status. It does not define parent propagation or cancellation authority, which remain #680.

## Decision 6: Add query and delete capabilities without widening the existing store

**Decision**: Keep `IWorkflowDispatchStore` unchanged. Add `IWorkflowDispatchQueryStore.QueryAsync(WorkflowDispatchQuery)` and `IWorkflowDispatchDeleteStore.DeleteAsync(dispatchId)`, implemented by built-in in-memory and Groundwork stores. Queries accept optional parent, child, status, and bounded `Take`; tenant comes only from persistence access context, never the request.

**Rationale**: One explicit query prevents an overload explosion and expresses supported intersections. Separate contracts preserve third-party implementers of the public #676 store. Groundwork chooses the narrowest declared index, then applies every remaining predicate before `Take`; it uses composite routes or continuation-based candidate reads and never caps an intermediate candidate set before post-filtering.

## Decision 7: Allowlist API projections

**Decision**: Add list/get runtime endpoints at `runtime/workflows/dispatches` and `runtime/workflows/dispatches/{dispatchId}` and protect both with `WorkflowRuntimeRead`. Map records to a dedicated view containing identities, mode/status, child executable identity/type, input name/type descriptors, timestamps, and allowlisted diagnostic code/category metadata only.

**Rationale**: Serializing the domain record directly would expose authority and arbitrary metadata. Denylisting unsafe keys is not robust.

## Decision 8: Retain while either execution exists

**Decision**: Add a bounded collector that considers only terminal records whose parent and child execution look absent, repeats both reads immediately before delete, and retains nonterminal records or any read failure. A separate delete capability avoids changing the base store and no store performs cascading delete.

**Rationale**: Existing execution retention is authoritative and already treats terminal states as retained roots. A guarded sweep composes with that policy without inventing durations or weakening fail-closed behavior.

## Decision 9: Assess composition through contributed capability evidence

**Decision**: Runtime composition contributes process-local durability evidence. Groundwork contributes durable checkpoint, dispatch, outbox, and scheduler evidence; the resumption feature contributes pump evidence. `IWorkflowDispatchReadinessAssessor.AssessAsync` is the stable provider-neutral reporting surface and feeds host readiness/health reporting. The assessment reports Unsafe and ProcessLocal compositions accurately without changing existing host startup behavior.

**Rationale**: Merely registering Groundwork is insufficient if the pump or durable queue is omitted. Stable provider-neutral component codes are useful to hosts and tests and avoid probing or leaking connection details.

## Decision 10: Final delivery failure is one claim-fenced durable transition

**Decision**: Add an additive finalization capability used only when the selected delivery policy makes a child-start failure final. It verifies the current outbox owner and fencing token and commits the outbox final-failure state plus the linked dispatch `DispatchFailed` projection in one storage transaction. The in-memory implementation uses its shared state lock; Groundwork uses one document unit of work. The projection authority is this claim-fenced finalization service, not an unconstrained observer.

**Rationale**: An after-the-fact observer can crash after terminalizing the outbox and strand the dispatch at Pending with no redelivery available. One atomic transition closes that crash boundary while leaving retry exhaustion, dead-lettering, and redrive policy to #681.

## Compatibility and Provider Findings

- Preserve existing `IWorkflowDispatchStore.ListAsync(parent)` and public constructors.
- Add `workflowDispatch` current schema version 1; no upcaster is required for a new kind.
- Adding claim token and expiry changes the existing post-commit outbox wire shape: bump that kind to v2, add a v1-to-v2 upcaster, and retain v1 plus new v2 fixtures.
- Pending/Started dispatch documents are executable-artifact retention roots until child materialization and record cleanup; executable GC must include their pinned child artifacts and fail closed on query uncertainty.
- Composite parent-plus-status and child-plus-status Groundwork routes keep intersection queries physically bounded; collection supports retention sweeps.
- Supported inspection combinations are parent, child, status, parent+status, child+status, parent+child, and parent+child+status. All predicates are applied before `Take`; an implementation may use composite routes or continuation-based candidate reads but may not cap before post-filtering.
- Dispatch top-level indexed fields must match Groundwork manifest dot paths and physicalization across relational/Mongo providers.
- Replace the architecture coverage-ledger deferral for #678 with an explicit mapped implementation/storage unit.
- Provider failure and uncertain-ack tests should inject at document unit-of-work commit boundaries already used by checkpoint tests; outbox tests additionally cover claim expiry and stale acknowledgement.
- In-memory tests prove process-local replay/idempotency but deliberately recreate empty state after a simulated process restart.

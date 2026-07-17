# Contract: Dispatch Test-Run Scope

## Root test-run start

The publishing test-run start operation creates one Open Runtime scope before root dispatch and supplies its immutable snapshot to the root start request. The existing Publishing test-run record is a projection with the same ID/expiry, not a second lifecycle owner. Root admission atomically validates Open state; teardown winning before delayed/replayed root materialization prevents the root from starting. A draft parent still compiles as a test artifact, while every DispatchWorkflow target remains the retained Published child pin.

## Execution and dispatch propagation

Root start → start command → started checkpoint → execution state → DispatchWorkflow record/payload → child start all carry the same optional scope snapshot. Run kind remains an independent immutable field and is visible in existing lifecycle inspection.

## Child registration gate

When a test-scoped parent commits a child dispatch, the provider transaction must prove:

- the scope exists and is Open;
- current time precedes expiry;
- scope ID, tenant, partition, and expiry match the parent snapshot;
- the dispatch/payload snapshot is identical.

Failure commits no dispatch, outbox item, or partial scope mutation.

## Close and cleanup

Expiry and internal teardown invoke one idempotent close contract. Closing blocks new root and child registration. The cleaner performs bounded scope-indexed pages and atomically resolves each detached dispatch:

- Pending → Cancelled before admission, no child actor command;
- Started → durable scope-cancellation marker plus deterministic cancel outbox item;
- terminal → unchanged.

Cleanup and child admission are mutually exclusive provider-atomic transitions. If cleanup wins while a start item is already claimed, delivery and replay observe the cancellation and return without materializing a child. If admission wins, cleanup observes Started and commits cancellation responsibility.

Waited dispatches remain governed by the existing production parent-cancellation contract. Parent cancellation is waited-only and scope cleanup detached-only. Repeated close/cleanup calls are equivalent. Closed means no eligible live detached dispatch remains.

## Internal application teardown

The internal application capability accepts only the test-run/scope ID. Tenant is selected by active persistence context; partition and expiry come from the stored scope. Publishing expiry cleanup closes the Runtime scope before deleting projection/source artifacts. This slice adds no public HTTP route.

`IWorkflowTestScopeStore` and cleanup/admission stores are single-provider replacement contracts. Feature composition fails clearly when more than one implementation is registered.

## Compatibility and exclusions

Legacy missing scope is readable but not cleanup eligible. Activity inputs/outcomes, exact pinning, waited results, delivery recovery, authority inheritance, and retention remain. No #683 transport/placement, broker, Studio, or WorkflowDefinitionActivity work is included.

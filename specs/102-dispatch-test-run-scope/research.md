# Research: Preserve Dispatch Test-Run Scope

## Decision 1: Keep child resolution on the retained Published pin

**Decision**: Allow `DispatchPinSource` to contribute pins while a parent compiles in `TestRun` scope, but continue querying and pinning only live `Published` child references. Child start uses the existing retained-dependency `WorkflowExecutableStartAuthority` and `WorkflowExecutableReferenceScope.Published`; no child draft lookup is introduced.

**Rationale**: The runtime activity and `ChildStartExecutor` already establish the correct artifact boundary, but the design pin source currently rejects TestRun parent compilation. The gate must be opened without widening child reference scope.

## Decision 2: Separate run kind from authoritative scope membership

**Decision**: Add an optional immutable `WorkflowTestScope` snapshot to root/child start, checkpoint payload, execution state, and dispatch state. A new root `TestRun` and every newly committed DispatchWorkflow descendant must carry it; non-test runs must not. Legacy missing scope stays missing and is never inferred from run kind or metadata, and retained replay of an older unscoped descendant remains compatible.

**Rationale**: `RunKind=TestRun` is classification, not an authorization or cleanup key. Scope identity, expiry, tenant, and partition require explicit durable facts.

## Decision 3: Use one runtime-owned scope registry

**Decision**: The replacement contract `IWorkflowTestScopeStore` owns an idempotently created scope record with immutable ID/expiry/context and monotonic Open→Closing→Closed state. Publishing test-run start creates it; expiry and internal explicit teardown close it. The existing Publishing `WorkflowTestRun` record is a projection with the same ID/expiry, and its cleanup closes the Runtime scope before removing projection/source artifacts. Feature composition rejects multiple scope-store implementations.

**Alternatives rejected**: The in-memory `IWorkflowTestRunStore` is an API tracking store and has no Groundwork implementation or atomic relationship with runtime checkpoints. Parent execution metadata cannot prevent late child registration after explicit teardown.

## Decision 4: Validate scope openness inside root and child provider transactions

**Decision**: A test-scoped root start/checkpoint and `WorkflowDispatchCheckpointRequest` require the provider transaction to load and validate the matching Open, unexpired scope before admitting execution or adding the child dispatch/start outbox item. Cleanup and `IWorkflowDispatchAdmissionStore.TryAdmitAsync` serialize atomically: cleanup-winning Pending makes claimed/replayed start delivery a no-op; admission-winning Started receives durable cancellation responsibility.

**Rationale**: A service-level precheck can race with teardown. The transaction that creates responsibility is the first boundary that can guarantee no post-close root or child escapes cleanup.

## Decision 5: Close first, then reconcile bounded child pages

**Decision**: Closing is a durable monotonic transition. Cleanup pages dispatches by scope and resolves each through a provider-owned atomic capability. New registrations are already blocked, so repeated scans converge and can mark the scope Closed when no live responsibility remains.

**Rationale**: A scope may have many descendants. One unbounded cross-document transaction is neither provider-neutral nor scalable.

## Decision 6: Reuse deterministic child-cancel responsibility

**Decision**: Pending detached scope children become Cancelled before admission. Started detached children receive the same deterministic cancel payload/intent/envelope already used for parent cancellation, after the dispatch is durably marked with an authoritative scope-cancellation state. `ChildCancelExecutor` accepts either the existing waited-parent marker/policy or the new detached scope marker. Waited dispatches are not direct scope-cleanup targets.

**Rationale**: Reusing the actor command and deterministic delivery path preserves single-writer rules and avoids a second cancel handler. Parent cancellation remains waited-only and scope teardown detached-only, so they do not have dual cancellation authority over one dispatch mode.

## Decision 7: Sweep expiry and unfinished cleanup through global resumption

**Decision**: Extend the global resumption task with a bounded scope cleanup participant. It closes expired scopes, resumes Closing scopes, and relies on existing outbox draining for cancel delivery.

**Rationale**: Cleanup must continue without the originating HTTP request and after process restart. A new scheduler or broker is unnecessary.

## Decision 8: Keep explicit teardown as an internal application capability

**Decision**: Add an idempotent internal teardown capability keyed only by test-run/scope ID. Active persistence context supplies tenant scope; the stored scope supplies partition. This slice adds no public HTTP route.

**Rationale**: Test runs are created through publishing management. Internal cleanup must not accept tenant, partition, child IDs, authority, or cancellation targets from an untrusted request.

## Decision 9: Persist provider-native scope and dispatch indexes

**Decision**: Groundwork declares a test-scope document with lifecycle/expiry query support and adds scope identity to dispatch materialization/indexes. Scope assertion, root admission, child admission, and per-dispatch cleanup use its transaction substrate and version/fencing semantics. Each single-provider scope capability is an explicit replacement contract with duplicate-registration conflict detection.

## Compatibility and Scope Findings

- Existing `WorkflowExecutionState.RunKind` and dispatch inspection already expose run kind; scope propagation fills the missing child/root durability contract.
- Missing legacy scope is safe and ineligible for cleanup.
- Ordinary parent completion, parent cancellation, and test-scope closure are distinct events.
- Direct scope cancellation applies to fire-and-forget children; waited children retain production parent-cancellation behavior and activity `CancelChildOnParentCancellation` remains unchanged.
- No distributed placement/transport, broker, Studio, WorkflowDefinitionActivity, or activity-authored scope control is included.

# Extension points — Workflows Runtime Distributed Groundwork persistence

The Groundwork v2 row-store adapter that makes the distributed workflow-execution provider durable. It
replaces the leaf's in-memory placement store and cross-node command transport
(`InMemoryExecutionPlacementStore`, `InMemoryExecutionCommandTransport`) with Groundwork-backed stores so
per-execution placement leases and the durable command inbox survive process restarts and are shared across
nodes through one host-selected provider connection. The store contracts themselves (`IExecutionPlacementStore`,
`IExecutionCommandTransport`) are owned by the leaf and catalogued in
[`Runtime EXTENSION_POINTS`](../../../EXTENSION_POINTS.md); this feature is a concrete,
overridable persistence provider for them.

## Provider selection — host composition

| Shell feature | Scope | Registration |
|---|---|---|
| `WorkflowsRuntimeDistributedGroundworkPersistence` | Server runtime (DependsOn `WorkflowsRuntimeDistributed`) | `WorkflowsRuntimeDistributedGroundworkPersistenceFeature` → `AddGroundworkDistributedRuntimeStores()` |

`AddGroundworkDistributedRuntimeStores()` calls `RemoveAll` for each leaf store contract, then registers the
Groundwork-backed stores as scoped services. Registration is override-friendly and composition-order-independent
(the distributed feature registers its in-memory defaults with `TryAddScoped`). The singleton pump and actor provider
resolve those stores only inside fresh persistence operation scopes. The host selects exactly one public v2 provider
connection and `AddGroundworkDistributedRuntimeStores()` registers three ordinary scoped storage units. MongoDB must be a writable
transaction-capable replica set whenever the selected combined host claims checkpoint atomicity.

## Persisted storage units

The adapter declares fresh v2 units through `DistributedGroundworkStorageManifest.CreateUnits()`. Each row carries
typed query columns plus one canonical JSON payload for the Elsa domain object. The clean break intentionally has no
v1 envelope, schema stamp, upcaster, or compatibility path.

| Storage unit | Scope | Payload | Primary key | Declared indexes |
|---|---|---|---|---|
| `elsa-distributed-execution-placement` (`elsa_distributed_execution_placement`) | Scoped | `ExecutionPlacementLease` | workflow execution id | owner / expiry / workflow execution id |
| `elsa-distributed-command-stream-head` (`elsa_distributed_command_stream_head`) | Scoped | stream sequence and pending-command head summary | workflow execution id | exact primary-ID read; pending execution by workflow execution / pending visibility |
| `elsa-distributed-command-transport` (`elsa_distributed_command_transport`) | Scoped | `ExecutionCommandTransportItem` | `transport:{escaped-execution-id}:{sequence}` | workflow execution / sequence ascending; workflow execution / visible-at / sequence ascending; workflow execution ascending / sequence descending |

The public query model expresses owner/expiry, visibility/sequence, latest-per-execution, total-count, and page
bounds directly against those declarations.

## Concurrency model

Every mutation is a storage-level compare-and-swap through v2 `WriteOptions`: first claims and sends are create-only —
the provider refuses the loser of a concurrent race at its storage layer — renewals/leases CAS on the loaded
row version, and release/ack CAS-delete. Transport row ids embed the per-execution sequence, so
duplicate sequence allocation collides on the id and the loser retries with the next number — sequences are
strictly unique and monotonic per execution, enforced by the store. Placement is still routing, not the
correctness backstop: W5 single-writer fencing at checkpoint commit remains the double-execution guard.

## Capability admission and actor fencing

The distributed runtime's process-local default implements `IWorkflowExecutionLeaseFencingCapability` as
unavailable. This Groundwork leaf replaces it and reports available only when the selected v2 connection advertises
`AtomicCommit`; command sequence-head advancement and row insertion then execute in one exact two-unit UOW.

## Schema evolution

Catalogs are fresh for the v2 clean break. Future declaration changes use Groundwork's ordinary schema fingerprint
and apply/verify lifecycle; they do not introduce a v1 data bridge.

## Cross-references

- Runtime provider/session and checkpoint admission: [`../../../../../Persistence/Groundwork/EXTENSION_POINTS.md`](../../../../../Persistence/Groundwork/EXTENSION_POINTS.md)
- Host-selected deployment source and CLI rules: [`../../../../../../../specs/094-harden-groundwork-stores/contracts/storage-composition.md`](../../../../../../../specs/094-harden-groundwork-stores/contracts/storage-composition.md)

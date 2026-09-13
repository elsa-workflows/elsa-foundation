# Extension points — Workflows Runtime Distributed Groundwork persistence

The Groundwork v2 row-store adapter that makes the distributed workflow-execution provider durable. It
replaces the leaf's in-memory placement store and cross-node command transport
(`InMemoryExecutionPlacementStore`, `InMemoryExecutionCommandTransport`) with Groundwork-backed stores so
per-execution placement leases and the durable command inbox survive process restarts and are shared across
nodes through one host-selected provider connection. The store contracts themselves (`IExecutionPlacementStore`,
`IExecutionCommandTransport`) are owned by the leaf and catalogued in
[`Runtime EXTENSION_POINTS`](../../../EXTENSION_POINTS.md); this feature is a concrete,
overridable persistence provider for them.

When the opt-in EF Core D01 provider is selected, this feature retains Groundwork ownership of
`IExecutionCommandTransport` and leaves EF ownership of `IExecutionPlacementStore` intact. When
the opt-in EF Core D02-D03 provider is selected, the inverse mixed composition is supported: this
feature retains Groundwork ownership of `IExecutionPlacementStore` while EF owns
`IExecutionCommandTransport`. The two ownership markers are independent and neither registration
order may provision a second active implementation or withdraw the unrelated persistence unit.

## Provider selection — host composition

| Shell feature | Scope | Registration |
|---|---|---|
| `WorkflowsRuntimeDistributedGroundworkPersistence` | Server runtime (DependsOn `WorkflowsRuntimeDistributed`) | `WorkflowsRuntimeDistributedGroundworkPersistenceFeature` → `AddGroundworkDistributedRuntimeStores()` |

`AddGroundworkDistributedRuntimeStores()` validates both backend markers before changing the service collection. An
EF-owned placement or transport descriptor is preserved, while each in-memory or Groundwork-owned descriptor is
replaced with its scoped Groundwork adapter. An explicit unmarked host implementation is never overwritten implicitly.
The distributed feature registers each in-memory default only when that contract has no owner, so both feature
orderings remain deterministic. The singleton pump and actor provider resolve these stores only inside fresh persistence
operation scopes. The host selects exactly one public v2 provider connection. A Groundwork-only composition registers
all three ordinary scoped storage units. When EF owns one contract, Groundwork registers only the units for the other;
selecting EF after Groundwork withdraws exactly the replaced unit declarations before the provider is built. MongoDB
must be a writable transaction-capable replica set whenever the selected combined host claims checkpoint atomicity.

## Persisted storage units

The adapter declares fresh v2 units through `DistributedGroundworkStorageManifest`. Each row carries
typed query columns plus one canonical JSON payload for the Elsa domain object. The clean break intentionally has no
v1 envelope, schema stamp, upcaster, or compatibility path. The placement unit is conditional as described above;
the two command units remain registered whenever Groundwork owns command transport.

| Storage unit | Scope | Payload | Primary key | Declared indexes |
|---|---|---|---|---|
| `elsa-distributed-execution-placement` (`elsa_distributed_execution_placement`) | Scoped | `ExecutionPlacementLease` | workflow execution id | owner / expiry / workflow execution id |
| `elsa-distributed-command-stream-head` (`elsa_distributed_command_stream_head`) | Scoped | stream sequence and pending-command head summary | internal stream-head id (equal to the workflow execution id) | pending visibility / workflow execution / stream-head id; workflow execution / stream-head id / pending count |
| `elsa-distributed-command-transport` (`elsa_distributed_command_transport`) | Scoped | `ExecutionCommandTransportItem` | `transport:{escaped-execution-id}:{sequence}` | workflow execution / sequence / transport item; workflow execution / visible-at / sequence |

The public query model expresses owner/expiry, visibility/sequence, latest-per-execution, the maintained
pending-count projection, and page bounds directly against those declarations.

## Concurrency model

Every mutation is a storage-level compare-and-swap through v2 `WriteOptions`: first claims and sends are create-only —
the provider refuses the loser of a concurrent race at its storage layer — renewals/leases CAS on the loaded
row version, and release/ack CAS-delete. Transport row ids embed the per-execution sequence, so
duplicate sequence allocation collides on the id and the loser retries with the next number — sequences are
strictly unique and monotonic per execution, enforced by the store. Placement is still routing, not the
correctness backstop: W5 single-writer fencing at checkpoint commit remains the double-execution guard.

## Capability admission and actor fencing

The distributed runtime's process-local default implements `IWorkflowExecutionLeaseFencingCapability` as unavailable.
This Groundwork slice persists routing and transport but still reports that checkpoint lease fencing is unavailable;
command sequence-head advancement and row insertion remain atomic within their exact two-unit UOW. The EF placement and
command-transport leaves likewise do not claim the separate checkpoint-fencing capability.

## Schema evolution

Catalogs are fresh for the v2 clean break. Future declaration changes use Groundwork's ordinary schema fingerprint
and apply/verify lifecycle; they do not introduce a v1 data bridge.

## Cross-references

- Runtime provider/session and checkpoint admission: [`../../../../../Persistence/Groundwork/EXTENSION_POINTS.md`](../../../../../Persistence/Groundwork/EXTENSION_POINTS.md)
- Host-selected deployment source and CLI rules: [`../../../../../../../specs/094-harden-groundwork-stores/contracts/storage-composition.md`](../../../../../../../specs/094-harden-groundwork-stores/contracts/storage-composition.md)

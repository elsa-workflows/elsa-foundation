# Extension points — Workflows Runtime Distributed Groundwork persistence

The Groundwork document-store bridge that makes the distributed workflow-execution provider durable. It
replaces the leaf's in-memory placement store and cross-node command transport
(`InMemoryExecutionPlacementStore`, `InMemoryExecutionCommandTransport`) with Groundwork-backed stores so
per-execution placement leases and the durable command inbox survive process restarts and are shared across
nodes through one host-selected document store. The store contracts themselves (`IExecutionPlacementStore`,
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
resolve those stores only inside fresh persistence operation scopes. The host selects exactly one provider through
the SQLite, PostgreSQL, SQL Server, or MongoDB unified registration; that provider materializes the manifest
snapshot containing `DistributedGroundworkStorageManifest.Create()`. MongoDB must be a writable
transaction-capable replica set whenever the selected combined host claims checkpoint atomicity.

## Persisted document kinds

The bridge owns its own `DistributedGroundworkStorageManifest` (identity `elsa-workflows-runtime-distributed`,
owner `elsa.workflows.runtime.distributed`, schema `1.1.0`), mirroring the Identity/Secrets precedent. The
document-kind **literals** stay owned by the leaf's `DistributedRuntimeStorageManifest` — wire-safe, stable
persistence identifiers frozen by committed golden fixtures in the leaf test suite
(`Fixtures/v1/executionCommandTransport.json`, `Fixtures/v1/executionPlacement.json`).

| Document kind | Scope | Nested frozen shape | Document id | Declared indexes |
|---|---|---|---|---|
| `executionPlacement` | Scoped | `ExecutionPlacementLease` (`lease`) | workflow execution id | `placement-by-owner-expiry` |
| `executionCommandStreamHead` | Scoped | stream-head state | workflow execution id | exact primary-ID read |
| `executionCommandTransport` | Scoped | `ExecutionCommandTransportItem` (`item`) | `transport:{escaped-execution-id}:{sequence}` | `command-by-execution-sequence`, `pending-command-by-execution-sequence` |

The wrapping documents add only index plumbing (constant collection partition, lifted execution id); the
nested lease/item is the frozen v1 wire shape, unchanged and drift-test-protected.

The exact scale-bearing bounded-route identities are `list-owned-live-placements`,
`lease-visible-commands-by-execution`, `list-visible-command-executions`, and
`count-pending-commands-by-execution`. They run only through the admitted physical route; no descriptive
“bounded” alias or client-side fallback is a supported route identity.

## Concurrency model

Every mutation is a storage-level compare-and-swap through the provider's `ExpectedVersion` contract
(Groundwork spec 014 amendment): first-claims and sends create with `ExpectedVersion = 0` (create-only —
the provider refuses the loser of a concurrent race at its storage layer), renewals/leases CAS on the loaded
envelope version, and release/ack CAS-delete. Transport document ids embed the per-execution sequence, so
duplicate sequence allocation collides on the id and the loser retries with the next number — sequences are
strictly unique and monotonic per execution, enforced by the store. Placement is still routing, not the
correctness backstop: W5 single-writer fencing at checkpoint commit remains the double-execution guard.

## Capability admission and actor fencing

The distributed runtime's process-local default implements `IWorkflowExecutionLeaseFencingCapability` as
unavailable. This Groundwork leaf replaces it, but returns available only after
`GroundworkProviderCapabilityAdmission` has published a successful selected-provider snapshot for the exact
runtime `checkpointCommit` / `runtime-checkpoint-commit` path with `AtomicCommit` and observed
`multi-document-transactions` topology. Configuration, package support, or a route admitted for another provider
cannot enable the actor's `LeaseFencing` capability. The provider remains unavailable when startup admission
fails, so the actor contract cannot advertise a fence it cannot prove.

## Schema evolution

Both nested document shapes are frozen by the leaf's golden-fixture drift tests. Evolving a shape requires,
in the same change: bump `DistributedGroundworkStorageManifest.SchemaVersion`, add a reader/upcaster for the
old stamp in `DistributedGroundworkDocuments` (reads currently fail loudly on any non-current stamp),
regenerate the leaf fixture (`ELSA_DISTRIBUTED_FIXTURE_REGEN=1`) as a NEW versioned fixture, and keep the old
fixtures so historical documents still load.

## Cross-references

- Runtime provider/session and checkpoint admission: [`../../../../../Persistence/Groundwork/EXTENSION_POINTS.md`](../../../../../Persistence/Groundwork/EXTENSION_POINTS.md)
- Host-selected deployment source and CLI rules: [`../../../../../../../specs/094-harden-groundwork-stores/contracts/storage-composition.md`](../../../../../../../specs/094-harden-groundwork-stores/contracts/storage-composition.md)

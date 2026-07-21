# Extension points — Persistence.Groundwork (runtime) domain

The Groundwork document-store bridge that persists Elsa runtime state (bookmarks, executables, activity/workflow execution state, durable values, scheduler/operational/control-plane/incident state, checkpoint commits, detached workflow-dispatch lifecycles, the post-commit outbox, the durable scheduler work queue, and workflow trigger bindings) for shells that select Groundwork runtime persistence. Contracts are defined and defaulted in this feature; the store implementations map the runtime `.Core` state records onto provider-neutral `Groundwork.Documents` envelopes.

This catalog covers the **schema-versioning** seams added so persisted runtime state can evolve without silently breaking suspended workflows. See [`../../../../docs/serialization.md`](../../../../docs/serialization.md) (**Schema evolution**) for the contract and the sanctioned-exception rationale.

## Provider selection — host composition

The runtime persistence seams are backed by a Groundwork document store only when a host composes a
provider shell feature. The provider choice is the host's; runtime and domain code reference only the
neutral ports. Each provider feature registers a scoped access-bound `IDocumentStore` adapter and calls
`AddGroundworkRuntimeStores()` (runtime-only) or the unified registration (all lanes).

| Shell feature | Provider | Scope | Registration |
|---|---|---|---|
| `GroundworkRuntimePersistenceSqlite` | SQLite | Runtime only | `SqliteGroundworkRuntimePersistenceShellFeature` |
| `GroundworkUnifiedPersistenceSqlite` | SQLite | Six provider-level families; Identity explicit | `AddGroundworkSqliteUnifiedPersistence` |
| `GroundworkRuntimePersistencePostgreSql` | PostgreSQL | Runtime only | `PostgreSqlGroundworkRuntimePersistenceShellFeature` |
| `GroundworkUnifiedPersistencePostgreSql` | PostgreSQL | Six provider-level families; Identity explicit | `AddGroundworkPostgreSqlUnifiedPersistence` |
| `GroundworkRuntimePersistenceSqlServer` | SQL Server | Runtime only | `SqlServerGroundworkRuntimePersistenceShellFeature` |
| `GroundworkUnifiedPersistenceSqlServer` | SQL Server | Six provider-level families; Identity explicit | `AddGroundworkSqlServerUnifiedPersistence` |
| `GroundworkRuntimePersistenceMongoDb` | MongoDB replica set | Runtime only | `MongoDbGroundworkRuntimePersistenceShellFeature` |
| `GroundworkUnifiedPersistenceMongoDb` | MongoDB replica set | Six provider-level families; Identity explicit | `AddGroundworkMongoDbUnifiedPersistence` |

**Optional cross-drain group commit (spec 115).** `AddGroundworkRuntimeGroupCommit(options?)` — called
after `AddGroundworkRuntimeStores()` — registers a process-wide `RuntimeGroupCommitCoordinator` that folds
concurrent checkpoint commits contending for the single durable writer into one shared unit-of-work / one
fsync (`RuntimeGroupCommitOptions.MaxBatchSize`, default 64). Off unless registered; a lone committer is
never batched, and a failed batch degrades to per-member individual commits.

The unified features share one host-selected provider-neutral manifest snapshot for Runtime, Secrets,
Distributed Runtime, Workflows Design, Activities Design, and Publishing. Identity contributes its own
manifest only when the host explicitly selects it and uses the matching deployment schema. SQLite stays the default composition; PostgreSQL is opt-in via
`shells.json` (e.g. `"GroundworkUnifiedPersistencePostgreSql": { "Options": { "ConnectionString": "Host=…" } }`).

**Startup admission — async initialization.** Runtime startup inspects the deployment-applied physical schema
and publishes immutable provider resources, so it is not done inside the synchronous `ConfigureServices`
factory. Each provider registers a document-store initializer as **both** an `IHostedService` (plain
hosts / tests) and a CShells `IShellInitializer` in the `LifecyclePhase.Prepare` phase (shell-composed hosts,
where shell-scoped hosted services do not run). The initializer admits the selected manifest once and publishes
a provider-owned `GroundworkStoreSessionSource`. Each scoped adapter invocation acquires a fresh immutable
Groundwork session bound to the current provider-neutral `PersistenceAccessContext`; no mutable ambient scope or
application-wide store instance is retained. The `Prepare` phase guarantees the source is ready before any
initializer reads it, and both host lifecycles await the initializer before request handling begins. The
four unified provider registrations and provider shell-feature signatures are unchanged. A
bare `IServiceProvider` built without a host lifecycle (e.g. some tests) has no hook to run the initializer, so
it must drive it explicitly before the first provider operation. `IDocumentStore` resolves beforehand as a
`GroundworkScopedDocumentStore`; its first operation throws a descriptive `InvalidOperationException` until the
session source is admitted. Resolution itself never blocks or performs provider I/O.

**Atomic runtime admission.** The runtime manifest declares the logical `runtime-checkpoint-commit` path on
the `checkpointCommit` storage unit as requiring `AtomicCommit` plus observed
`multi-document-transactions` topology. The unit is the stable admission anchor; the capability covers the
cross-unit checkpoint transaction that fence-touches ownership and commits checkpoint state, outbox state, and
the idempotency marker together. Every provider initializer must activate that exact feature/unit/path tuple.
SQLite, PostgreSQL, and SQL Server report transactional storage as a provider invariant. MongoDB reports atomic
commit evidence only after the initializer has observed a matching writable replica set and completed a real
transaction round trip; configured intent alone is never admission evidence.

MongoDB keeps one validate-only admitted handle for the provider lifetime and derives access-bound stores from
that immutable runtime. It does not create a client, probe topology, or repeat schema admission for each scoped
operation. Disposing the session source drains an in-flight store binding before it releases the provider handle.

**Query-shape validation (PostgreSQL).** The PostgreSQL Groundwork provider serves the same query shapes
Elsa already relies on for SQLite: `PostgreSqlDocumentStore` derives from the shared
`RelationalDocumentStore` base, `PostgreSqlGroundworkCapabilities.Runtime()` advertises the full
`PortableQueryOperation` set and `IndexCapabilities.All`, and the dialect implements `Contains` (`ILIKE …
ESCAPE`) plus `LIMIT/OFFSET` pagination. The neutral querying layer
([`Querying/`](Querying/GroundworkReadStore.cs)) and the store bridges therefore need no PostgreSQL-specific
workarounds — no equality-only restriction applies to this provider's published surface.

## Persisted kinds, scope, and bounded routes

`ElsaRuntimeStorageManifest` version `1.0.0` owns the runtime kinds. Every runtime unit is
`TenancyPolicy.Scoped`; a caller acquires `DocumentStoreAccess.Scoped(StorageScope)` through the access context,
not by supplying a scope field in JSON. The kinds are `bookmarkState`, `workflowExecutable`,
`executableActivityTemplate`, `executableActivityTemplateHashClaim`, `workflowExecutableSourceReference`,
`activityExecutionState`, `activityExecutionInspection`, `activityExecutionHierarchy`,
`workflowExecutionState`, `workflowTestScope`, `durableValueState`, `schedulerState`, `operationalState`,
`controlPlaneState`, `incidentState`, `checkpointCommit`, `postCommitOutbox`, `workflowDispatch`,
`schedulerWorkItem`, `schedulerPoison`, `durableTimer`, `workflowTriggerBinding`,
`publicationProjectionState`, and `recurringTriggerSchedule`.

The following are current exact physical bounded-route identities, not descriptive aliases. A host must admit
the selected manifest before any of these routes can execute; unsupported routes fail readiness rather than
falling back to a scan.

| Family | Exact route identities used by the current store seams |
|---|---|
| Bookmarks and trigger lookup | `list-by-stimulus-and-type`, `list-by-stimulus-type`, `page-live-by-scope` |
| Recovery scanner | `list-recovery-detected`, `list-recovery-detected-by-lease-owner`, `list-recovery-detected-by-heartbeat-owner`, `list-recovery-detected-ownerless`, `list-recovery-by-lease-expiry`, `list-recovery-by-lease-expiry-and-owner`, `list-recovery-by-lease-acquisition`, `list-recovery-by-lease-acquisition-and-owner`, `list-recovery-by-heartbeat`, `list-recovery-by-heartbeat-and-owner` |
| Scheduler queue | `list-by-workflow-execution`, `list-pending-scheduler-workflow-executions` |
| Post-commit outbox | `list-deliverable`, `list-deliverable-by-workflow`, `list-deliverable-by-intent-kind`, `list-deliverable-by-workflow-and-intent-kind`, `list-claimable`, `list-claimable-by-workflow`, `list-claimable-by-intent-kind`, `list-claimable-by-workflow-and-intent-kind`, `list-immediate`, `list-immediate-by-workflow`, `list-immediate-by-intent-kind`, `list-immediate-by-workflow-and-intent-kind`, `list-expired-claims`, `list-expired-claims-by-workflow`, `list-expired-claims-by-intent-kind`, `list-expired-claims-by-workflow-and-intent-kind` |
| Timers and schedules | `list-due`, `page-by-publication` |

`runtime-checkpoint-commit` is an admitted atomic operation path, not a `BoundedQueryDeclaration` identity.
Its checkpoint bundle therefore has no invented “native route” label: its evidence is the selected physical
path's `AtomicCommit` capability and `multi-document-transactions` topology.

## Capability admission and distributed fencing

`GroundworkProviderCapabilityAdmission` publishes one immutable capability snapshot only after the selected
provider has completed physical schema admission. Claims are evaluated against the exact active feature, storage
unit, route, capability, and topology—not package references, configuration intent, or an unselected provider.
The Groundwork distributed leaf consumes this snapshot through
`IWorkflowExecutionLeaseFencingCapability`: it reports `true` only when the admitted
`runtime-checkpoint-commit` path has `AtomicCommit` and the observed
`multi-document-transactions` topology. The process-local distributed default always reports `false`.
See the [distributed Groundwork catalog](../../Workflows/Runtime/Distributed/Persistence/Groundwork/EXTENSION_POINTS.md)
for the durable placement/transport replacement seam.

## Override — replacement contracts

Exactly one implementation is active per runtime host. Logic-bearing runtime stores are scoped so request
access cannot cross DI scopes; immutable serializers, manifests, and admitted provider resources use their
reviewed longer-lived registrations.

| Contract | Default implementation | Responsibility |
|---|---|---|
| `IGroundworkRuntimeDocumentSerializer` | `GroundworkRuntimeDocumentSerializer` | Elsa facade over Groundwork's `VersionedJsonDocumentCodec`. Owns the frozen bridge `JsonSerializerOptions` and Elsa's per-kind policies; Groundwork stamps and enforces versions and rejects below-minimum/unknown/future versions. This is the single sanctioned serialization surface for Runtime stores. |
| `IGroundworkStoreSessionFactory` | `GroundworkStoreSessionFactory` | Maps the current provider-neutral context to one immutable access-bound session. `ExecutePrivilegedAcrossScopesAsync` rejects every non-across-scope context before provider acquisition, records acquisition, disposes the provider lease, then records exactly one terminal outcome. |
| `IGroundworkPrivilegedAccessEmitter` | Scoped `GroundworkPrivilegedAccessRecorder` writing to the singleton bounded `GroundworkPrivilegedAccessSink` | Emits correlated, sanitized acquisition/outcome records. Scoped tenant identities are represented by a stable SHA-256 reference; raw tenants and exception messages never become metric labels or retained event fields. |
| `IWorkflowDispatchStore`, `IWorkflowDispatchQueryStore`, `IWorkflowDispatchDeleteStore`, `IWorkflowDispatchRetentionRootStore`, `IWorkflowDispatchAdmissionStore`, `IWorkflowDispatchCancellationStore` | Scoped `GroundworkWorkflowDispatchStore` | Persists immutable dispatch identity/provenance plus monotonic lifecycle state, serves declared parent/child/status inspection routes, deletes retention-approved terminal records, exposes every Pending/Started child artifact as a garbage-collection root, conditionally admits child materialization, and resolves parent cancellation inside the checkpoint transaction. |
| `IWorkflowTestScopeStore`, `IWorkflowTestScopeAdmissionStore`, `IWorkflowTestScopeCleanupStore` | Scoped `GroundworkWorkflowTestScopeStore` plus `GroundworkTestScopeCleanupStore` | Persists finite test-scope lifecycle and exact-scope indexes. Detached admission touches Open scope with the dispatch transition; cleanup touches Closing scope and commits cancellation state plus any deterministic cancel outbox item in one cross-unit transaction. |
| `IRuntimePostCommitOutboxStore`, `IRuntimePostCommitOutboxClaimStore`, `IRuntimePostCommitOutboxClaimCompletionStore`, `IPostCommitOutboxLookupStore`, `IWorkflowDispatchRedriveStore` | Scoped `GroundworkRuntimePostCommitOutboxStore` | Persists post-checkpoint intents, supports exact committed-item lookup, grants visibility-bounded fenced claims, rejects stale completions, and atomically records a final outbox result with its optional dispatch-failure projection. The same transaction owner performs separately authorized fire-and-forget redrive over the linked dispatch and failed-final item, advancing generation/fencing without changing logical identity. Exact lookup lets replay reuse an already committed parent-resume intent rather than recapturing outputs. |

Tenant-agnostic design-store query flags are query intent only. They never grant authority: the caller must
already hold a named `PersistenceAccessContext.PrivilegedAcrossScopes` context, and the adapter executes only
the admitted bounded collection route. Ordinary and privileged-but-scoped contexts fail before provider
resources are opened.

## Schema migration contributions

There is no Elsa-owned upcaster contribution surface. Workflow executables use a clean current-only v6
baseline. Compatible future windows must retain every supported fixture and extend Groundwork serializer
composition deliberately rather than reintroducing an Elsa-specific codec or registry.

## Schema-version model

Versions live in the Groundwork **envelope** `SchemaVersion` field (already persisted per document), not inside content JSON and not on the domain state records. Elsa declares per-kind current/minimum-readable policy in `ElsaRuntimeDocumentVersions`; Groundwork's `VersionedJsonDocumentCodec` owns the generic parser/formatter lifecycle, chain validation, upcasting capability, and structured `DocumentSchemaVersionException`. Only positive-integer document stamps are accepted. `workflowExecutable` is current and minimum-readable version `6`; every activity input carries required explicit nullability, and older executables require the documented persistence reset and republish. `workflowExecutableSourceReference` and `workflowExecutionState` are current-only version `4`; activity-execution state is current-only version `4`; activity-execution inspection, scheduler work items, workflow trigger bindings, recurring schedules, and durable timers are current-only version `2`; `postCommitOutbox` is version `3`; `workflowDispatch` starts at version `1`; unchanged kinds remain at version `1`. Each kind has a current golden fixture, making any unversioned persisted-shape change a test failure.

Groundwork checkpoint commits keep the child Completed projection, policy-safe output snapshot, and deterministic parent-resume outbox item in the same admitted transaction. Effective-final child-start delivery checks deterministic child-execution visibility and then either acknowledges the already-admitted child or commits the failed-final dead letter, safe `DispatchFailed` projection, and optional wait-parent resume together. Fire-and-forget redrive uses one cross-unit transaction over the existing dispatch and outbox document kinds; no new persisted kind or index is introduced. Restart tests recreate runtime services around checkpoint, claim, exhaustion, redrive, acknowledgement, and bookmark-consumption boundaries to prove claim expiry and uncertain acknowledgement converge without leaking redacted values.

## Cross-references

- Serialization rule and schema-evolution contract: [`../../../../docs/serialization.md`](../../../../docs/serialization.md)
- Design-time Groundwork provider catalog: [`../../Workflows/Design/Persistence/Groundwork/EXTENSION_POINTS.md`](../../Workflows/Design/Persistence/Groundwork/EXTENSION_POINTS.md)
- Repo-wide index: [`../../../../EXTENSION_POINTS.md`](../../../../EXTENSION_POINTS.md)

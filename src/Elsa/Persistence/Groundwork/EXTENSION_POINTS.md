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
| `IRuntimePostCommitOutboxStore`, `IRuntimePostCommitOutboxClaimStore`, `IRuntimePostCommitOutboxClaimCompletionStore`, `IPostCommitOutboxLookupStore` | Scoped `GroundworkRuntimePostCommitOutboxStore` | Persists post-checkpoint intents, supports exact committed-item lookup, grants visibility-bounded fenced claims, rejects stale completions, and atomically records a final outbox result with its optional dispatch-failure projection. Exact lookup lets replay reuse an already committed parent-resume intent rather than recapturing outputs. |

Tenant-agnostic design-store query flags are query intent only. They never grant authority: the caller must
already hold a named `PersistenceAccessContext.PrivilegedAcrossScopes` context, and the adapter executes only
the admitted bounded collection route. Ordinary and privileged-but-scoped contexts fail before provider
resources are opened.

## Schema migration contributions

There is no Elsa-owned upcaster contribution surface before GA. Every Runtime document kind admits only its
current fixture, so any older persisted generation requires the documented complete persistence reset. After a
released shape exists, a compatible in-place or rolling upgrade may add explicit Groundwork
`IDocumentJsonUpcaster` contributions and retain every supported historical fixture; that future change must
also extend serializer composition deliberately rather than reintroducing an Elsa-specific codec or registry.

## Schema-version model

Versions live in the Groundwork **envelope** `SchemaVersion` field (already persisted per document), not inside content JSON and not on the domain state records. Elsa declares per-kind current/minimum-readable policy in `ElsaRuntimeDocumentVersions`; Groundwork's `VersionedJsonDocumentCodec` owns the generic parser/formatter lifecycle, chain validation, upcasting capability, and structured `DocumentSchemaVersionException`. Before GA, minimum-readable equals current for every kind, only positive-integer document stamps are accepted, and the codec receives an empty `IDocumentJsonUpcaster` set. `workflowExecutable`, `workflowExecutableSourceReference`, and `workflowExecutionState` are version `4`; activity-execution state and inspection documents, scheduler work items, workflow trigger bindings, and recurring schedules are version `2`; `postCommitOutbox` is version `3`; `workflowDispatch` starts at version `1`; unchanged kinds remain at version `1`. Each kind has only its current golden fixture, making any unversioned persisted-shape change a test failure.

Groundwork checkpoint commits keep the child Completed projection, policy-safe output snapshot, and deterministic parent-resume outbox item in the same admitted transaction. Restart tests recreate runtime services around checkpoint, claim, acknowledgement, and bookmark-consumption boundaries to prove claim expiry and uncertain acknowledgement converge without leaking redacted values.

## Cross-references

- Serialization rule and schema-evolution contract: [`../../../../docs/serialization.md`](../../../../docs/serialization.md)
- Design-time Groundwork provider catalog: [`../../Workflows/Design/Persistence/Groundwork/EXTENSION_POINTS.md`](../../Workflows/Design/Persistence/Groundwork/EXTENSION_POINTS.md)
- Repo-wide index: [`../../../../EXTENSION_POINTS.md`](../../../../EXTENSION_POINTS.md)

# Extension points — Persistence.Groundwork (runtime) domain

The Groundwork document-store bridge that persists Elsa runtime state (bookmarks, executables, activity/workflow execution state, durable values, scheduler/operational/control-plane/incident state, checkpoint commits, the post-commit outbox, the durable scheduler work queue, and workflow trigger bindings) for shells that select Groundwork runtime persistence. Contracts are defined and defaulted in this feature; the store implementations map the runtime `.Core` state records onto provider-neutral `Groundwork.Documents` envelopes.

This catalog covers the **schema-versioning** seams added so persisted runtime state can evolve without silently breaking suspended workflows. See [`../../../../docs/serialization.md`](../../../../docs/serialization.md) (**Schema evolution**) for the contract and the sanctioned-exception rationale.

## Provider selection — host composition

The runtime persistence seams are backed by a Groundwork document store only when a host composes a
provider shell feature. The provider choice is the host's; runtime and domain code reference only the
neutral ports. Each provider feature registers a scoped access-bound `IDocumentStore` adapter and calls
`AddGroundworkRuntimeStores()` (runtime-only) or the unified registration (all lanes).

| Shell feature | Provider | Scope | Registration |
|---|---|---|---|
| `GroundworkRuntimePersistenceSqlite` | SQLite | Runtime only | `SqliteGroundworkRuntimePersistenceShellFeature` |
| `GroundworkUnifiedPersistenceSqlite` | SQLite | All seven persistence families | `AddGroundworkSqliteUnifiedPersistence` |
| `GroundworkRuntimePersistencePostgreSql` | PostgreSQL | Runtime only | `PostgreSqlGroundworkRuntimePersistenceShellFeature` |
| `GroundworkUnifiedPersistencePostgreSql` | PostgreSQL | All seven persistence families | `AddGroundworkPostgreSqlUnifiedPersistence` |
| `GroundworkRuntimePersistenceSqlServer` | SQL Server | Runtime only | `SqlServerGroundworkRuntimePersistenceShellFeature` |
| `GroundworkUnifiedPersistenceSqlServer` | SQL Server | All seven persistence families | `AddGroundworkSqlServerUnifiedPersistence` |
| `GroundworkRuntimePersistenceMongoDb` | MongoDB replica set | Runtime only | `MongoDbGroundworkRuntimePersistenceShellFeature` |
| `GroundworkUnifiedPersistenceMongoDb` | MongoDB replica set | All seven persistence families | `AddGroundworkMongoDbUnifiedPersistence` |

The unified features share one host-selected provider-neutral manifest snapshot, so Runtime, IAM, Secrets,
Distributed Runtime, Workflows Design, Activities Design, and Publishing document kinds are defined once and
admitted per provider. SQLite stays the default composition; PostgreSQL is opt-in via
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
| `IGroundworkRuntimeDocumentSerializer` | `GroundworkRuntimeDocumentSerializer` | Owns the frozen bridge `JsonSerializerOptions`; stamps each document with its kind's current schema version on write and enforces the stamp on read (deserialize current, upcast older, fail loudly on unknown/future). The single sanctioned serialization surface for runtime documents — stores must not call `System.Text.Json` directly. |
| `IGroundworkRuntimeDocumentUpcasterRegistry` | `GroundworkRuntimeDocumentUpcasterRegistry` | Indexes contributed upcasters per kind and applies them one version at a time; validates the chain **eagerly at construction** (duplicate step, chain gap, step at/beyond a kind's current version, or an incomplete known-kind chain all fail at startup). |
| `IGroundworkStoreSessionFactory` | `GroundworkStoreSessionFactory` | Maps the current provider-neutral context to one immutable access-bound session. `ExecutePrivilegedAcrossScopesAsync` rejects every non-across-scope context before provider acquisition, records acquisition, disposes the provider lease, then records exactly one terminal outcome. |
| `IGroundworkPrivilegedAccessEmitter` | Scoped `GroundworkPrivilegedAccessRecorder` writing to the singleton bounded `GroundworkPrivilegedAccessSink` | Emits correlated, sanitized acquisition/outcome records. Scoped tenant identities are represented by a stable SHA-256 reference; raw tenants and exception messages never become metric labels or retained event fields. |

Tenant-agnostic design-store query flags are query intent only. They never grant authority: the caller must
already hold a named `PersistenceAccessContext.PrivilegedAcrossScopes` context, and the adapter executes only
the admitted bounded collection route. Ordinary and privileged-but-scoped contexts fail before provider
resources are opened.

## Extend — contribution (fan-in)

| Interface | Kind | What to register |
|---|---|---|
| `IGroundworkRuntimeDocumentUpcaster` | Source (per-version migration step) | One implementation per `(DocumentKind, FromVersion)` step. Register any number in the service collection; the registry discovers them all via `IEnumerable<IGroundworkRuntimeDocumentUpcaster>`. Each rewrites content JSON from `FromVersion` to `FromVersion + 1`. |

Adding an upcaster never removes another. When bumping a kind's current version in `ElsaRuntimeDocumentVersions`, register an upcaster for the previous version in the same change (see the evolution checklist in `docs/serialization.md`).

## Schema-version model

Versions live in the Groundwork **envelope** `SchemaVersion` field (already persisted per document), not inside content JSON and not on the domain state records. Per-kind current versions are integers declared in `ElsaRuntimeDocumentVersions`; `workflowExecutable`, `workflowExecutableSourceReference`, and `workflowExecutionState` are version `3` with complete production upcaster chains, `workflowTriggerBinding` and `recurringTriggerSchedule` are version `2`, and unchanged kinds remain at version `1`. The legacy manifest-wide stamp `"1.0.0"` parses as version `1` for every kind. Committed versioned golden fixtures make any unversioned change to a persisted state-record shape a test failure.

## Cross-references

- Serialization rule and schema-evolution contract: [`../../../../docs/serialization.md`](../../../../docs/serialization.md)
- Design-time Groundwork provider catalog: [`../../Workflows/Design/Persistence/Groundwork/EXTENSION_POINTS.md`](../../Workflows/Design/Persistence/Groundwork/EXTENSION_POINTS.md)
- Repo-wide index: [`../../../../EXTENSION_POINTS.md`](../../../../EXTENSION_POINTS.md)

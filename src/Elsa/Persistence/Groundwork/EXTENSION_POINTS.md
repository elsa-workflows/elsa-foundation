# Extension points — Persistence.Groundwork (runtime) domain

The Groundwork document-store bridge that persists Elsa runtime state (bookmarks, executables, activity/workflow execution state, durable values, scheduler/operational/control-plane/incident state, checkpoint commits, the post-commit outbox, the durable scheduler work queue, and workflow trigger bindings) for shells that select Groundwork runtime persistence. Contracts are defined and defaulted in this feature; the store implementations map the runtime `.Core` state records onto provider-neutral `Groundwork.Documents` envelopes.

This catalog covers the **schema-versioning** seams added so persisted runtime state can evolve without silently breaking suspended workflows. See [`../../../../docs/serialization.md`](../../../../docs/serialization.md) (**Schema evolution**) for the contract and the sanctioned-exception rationale.

## Provider selection — host composition

The runtime persistence seams are backed by a Groundwork document store only when a host composes a
provider shell feature. The provider choice is the host's; runtime and domain code reference only the
neutral ports. Each provider feature registers the concrete `IDocumentStore` and calls
`AddGroundworkRuntimeStores()` (runtime-only) or the unified registration (all lanes).

| Shell feature | Provider | Scope | Registration |
|---|---|---|---|
| `GroundworkRuntimePersistenceSqlite` | SQLite | Runtime only | `SqliteGroundworkRuntimePersistenceShellFeature` |
| `GroundworkUnifiedPersistenceSqlite` | SQLite | Runtime + workflows-design + activities-design | `AddGroundworkSqliteUnifiedPersistence` |
| `GroundworkRuntimePersistencePostgreSql` | PostgreSQL | Runtime only | `PostgreSqlGroundworkRuntimePersistenceShellFeature` |
| `GroundworkUnifiedPersistencePostgreSql` | PostgreSQL | Runtime + workflows-design + activities-design | `AddGroundworkPostgreSqlUnifiedPersistence` |

The unified features share one provider-neutral union manifest (`GroundworkUnifiedManifest` in
`Elsa.Persistence.Groundwork.Unified`), so the composition of the three lanes' document kinds is defined
once and materialized per provider. SQLite stays the default composition; PostgreSQL is opt-in via
`shells.json` (e.g. `"GroundworkUnifiedPersistencePostgreSql": { "Options": { "ConnectionString": "Host=…" } }`).

**Startup materialization — async initialization.** Materializing the document store (opening the connection
and applying the manifest schema) is async, so it is not done inside the synchronous `ConfigureServices`
factory. Instead each provider registers a document-store initializer as **both** an `IHostedService` (plain
hosts / tests) and a CShells `IShellInitializer` in the `LifecyclePhase.Prepare` phase (shell-composed hosts,
where shell-scoped hosted services do not run). The initializer awaits store creation once at host startup and
populates a shared, provider-neutral `GroundworkDocumentStoreHolder`; the registered `IDocumentStore` resolves
from that holder, so consumers still resolve a fully-initialized singleton with no synchronous block on the
resolving thread. The `Prepare` phase guarantees the store is ready before any other shell initializer that
reads it, and both host lifecycles await the initializer before request handling begins. The
`AddGroundwork{Sqlite,PostgreSql}UnifiedPersistence` and provider shell-feature signatures are unchanged. A
bare `IServiceProvider` built without a host lifecycle (e.g. some tests) has no hook to run the initializer, so
it must drive it explicitly before resolving `IDocumentStore` — resolving beforehand throws a descriptive
`InvalidOperationException` rather than silently blocking.

**Query-shape validation (PostgreSQL).** The PostgreSQL Groundwork provider serves the same query shapes
Elsa already relies on for SQLite: `PostgreSqlDocumentStore` derives from the shared
`RelationalDocumentStore` base, `PostgreSqlGroundworkCapabilities.Runtime()` advertises the full
`PortableQueryOperation` set and `IndexCapabilities.All`, and the dialect implements `Contains` (`ILIKE …
ESCAPE`) plus `LIMIT/OFFSET` pagination. The neutral querying layer
([`Querying/`](Querying/GroundworkReadStore.cs)) and the store bridges therefore need no PostgreSQL-specific
workarounds — no equality-only restriction applies to this provider's published surface.

## Override — replacement contracts

Exactly one implementation is active per runtime host (registered with `TryAddSingleton` in `AddGroundworkRuntimeStores()`, so a host may replace either default).

| Contract | Default implementation | Responsibility |
|---|---|---|
| `IGroundworkRuntimeDocumentSerializer` | `GroundworkRuntimeDocumentSerializer` | Owns the frozen bridge `JsonSerializerOptions`; stamps each document with its kind's current schema version on write and enforces the stamp on read (deserialize current, upcast older, fail loudly on unknown/future). The single sanctioned serialization surface for runtime documents — stores must not call `System.Text.Json` directly. |
| `IGroundworkRuntimeDocumentUpcasterRegistry` | `GroundworkRuntimeDocumentUpcasterRegistry` | Indexes contributed upcasters per kind and applies them one version at a time; validates the chain **eagerly at construction** (duplicate step, chain gap, step at/beyond a kind's current version, or an incomplete known-kind chain all fail at startup). |

## Extend — contribution (fan-in)

| Interface | Kind | What to register |
|---|---|---|
| `IGroundworkRuntimeDocumentUpcaster` | Source (per-version migration step) | One implementation per `(DocumentKind, FromVersion)` step. Register any number in the service collection; the registry discovers them all via `IEnumerable<IGroundworkRuntimeDocumentUpcaster>`. Each rewrites content JSON from `FromVersion` to `FromVersion + 1`. |

Adding an upcaster never removes another. When bumping a kind's current version in `ElsaRuntimeDocumentVersions`, register an upcaster for the previous version in the same change (see the evolution checklist in `docs/serialization.md`).

## Schema-version model

Versions live in the Groundwork **envelope** `SchemaVersion` field (already persisted per document), not inside content JSON and not on the domain state records. Per-kind current versions are integers declared in `ElsaRuntimeDocumentVersions`; `workflowExecutable`, `workflowExecutableSourceReference`, `workflowTriggerBinding`, and `recurringTriggerSchedule` are version `2` with complete production v1-to-v2 upcasters, while unchanged kinds remain at version `1`. The legacy manifest-wide stamp `"1.0.0"` parses as version `1` for every kind. Committed versioned golden fixtures make any unversioned change to a persisted state-record shape a test failure.

## Cross-references

- Serialization rule and schema-evolution contract: [`../../../../docs/serialization.md`](../../../../docs/serialization.md)
- Design-time Groundwork provider catalog: [`../../Workflows/Design/Persistence/Groundwork/EXTENSION_POINTS.md`](../../Workflows/Design/Persistence/Groundwork/EXTENSION_POINTS.md)
- Repo-wide index: [`../../../../EXTENSION_POINTS.md`](../../../../EXTENSION_POINTS.md)

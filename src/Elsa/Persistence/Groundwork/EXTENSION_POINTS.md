# Extension points — Persistence.Groundwork (runtime) domain

The Groundwork document-store bridge that persists Elsa runtime state (bookmarks, executables, activity/workflow execution state, durable values, scheduler/operational/control-plane/incident state, checkpoint commits, detached workflow-dispatch lifecycles, the post-commit outbox, the durable scheduler work queue, and workflow trigger bindings) for shells that select Groundwork runtime persistence. Contracts are defined and defaulted in this feature; the store implementations map the runtime `.Core` state records onto provider-neutral `Groundwork.Documents` envelopes.

This catalog covers the **schema-versioning** seams added so persisted runtime state can evolve without silently breaking suspended workflows. See [`../../../../docs/serialization.md`](../../../../docs/serialization.md) (**Schema evolution**) for the contract and the sanctioned-exception rationale.

## v2 public composition ports

The v2 adapter boundary is provider-neutral. Elsa features contribute declared storage units and
resolve these ports; the selected provider owns physical connections, schema admission, and native
capability evidence. These ports are the clean-break replacement boundary; they must not be used to
reintroduce a v1 compatibility or migration path.

### `IGroundworkStorageSessionSource` *(Composition contract — `Elsa.Persistence.Groundwork`)*

- **Kind:** provider-neutral adapter port; one singleton source serves every composed target.
- **Role:** opens a non-owning `IStorageSession` for a declared unit and explicit `StorageAccess`,
  begins a provider-owned `IUnitOfWork` over declared units, and returns the declared `StorageUnit`
  definition. `targetName` selects a named physical store; omitting it selects `default`.
- **Register:** `services.AddGroundworkStorageUnit(unit, targetName?)` creates the singleton
  `GroundworkStorageSessionSource` and aliases it as this interface. A provider connection must be
  registered with `AddGroundworkStorageProviderConnection` before startup admission or the first
  operation.
- **Lifetime and ownership:** the source is a singleton and is registered for both `IHostedService`
  and CShells `IShellInitializer`, so plain-host and shell startup admit the same declarations.
  Sessions are non-owning views held valid by the provider connection; the source does not dispose
  the connection. The service provider owns a connection registered through the public connection
  helper and disposes it after its sessions. Unit-of-work instances own their transaction and must be
  disposed by the caller.
- **Refusal behavior:** opening an undeclared unit, selecting a target without a connection, using
  invalid access, or beginning a unit of work with no unit fails descriptively. The source admits each
  target/unit schema fingerprint once; it never silently substitutes another target or a host-wide
  union.

### `IGroundworkStorageCapabilitySource` *(Composition contract — `Elsa.Persistence.Groundwork`)*

- **Kind:** read-only provider-capability port; one snapshot per selected target.
- **Role:** exposes the selected connection's `CapabilityDescriptor` values after the provider has
  been selected. Runtime adapters use this evidence for atomic-commit and other capability gates;
  configuration intent or an unselected provider is not evidence.
- **Default:** the shipped `GroundworkStorageSessionSource` implements this port alongside
  `IGroundworkStorageSessionSource`. A custom source must publish the same provider-admitted,
  target-specific snapshot before claiming a capability.
- **Lifetime:** follows the singleton source/connection lifetime. The returned list is a read-only
  snapshot; callers do not acquire or dispose provider resources through this interface.
- **Refusal behavior:** a source must refuse claims that are absent from the selected target's
  admitted capabilities. Capability checks must fail readiness or the operation with an actionable
  error; they must not fall back to configuration, another target, or a weaker query/transaction
  path.

### `AddGroundworkStorageProviderConnection` *(Composition registration — `Elsa.Persistence.Groundwork`)*

- **Role:** publishes one host-selected `IStorageProviderConnection` for a target, either from a
  factory or an already-created instance. The default target is available through keyed and ordinary
  resolution; named targets are keyed only.
- **Ownership:** factory registrations are lazy singletons and the service provider owns the returned
  connection. Caller-supplied instance registrations remain caller-owned; the caller must keep the
  instance alive for the composition lifetime and dispose it after the service provider. The
  connection owns the non-owning sessions opened from it.
- **Refusal behavior:** null services, factories, or connections are rejected; registering a second
  connection for the same target is rejected unless it is the exact same instance registration.
  Give each physical store a distinct target name. This helper never creates a provider, infers a
  connection string, or silently replaces an existing target.

### `AddGroundworkStorageUnit` and `GroundworkStorageUnitRegistry` *(Composition declarations — `Elsa.Persistence.Groundwork`)*

- **Role:** features contribute provider-neutral `StorageUnit` declarations, optionally bound to a
  target. The singleton registry keys each declaration by `(target, unit id)`, exposes stable ordered
  registrations to the session source, and retains the schema fingerprint used for admission.
- **Composition:** an exact repeat of the same unit shape is idempotent. A second declaration with
  the same target/unit identity but a different schema is a composition error and is rejected before
  provider startup. `Require` rejects an undeclared unit rather than inventing one.
- **Lifetime:** the registry and session source are singleton composition state; provider connections
  remain the owner of physical resources. Declaring a unit also installs the hosted/shell admission
  hooks once, so repeated feature registration cannot create competing sources.
- **Target rule:** target names are normalized before identity and admission. A declaration bound to
  one target is never served by another target's connection, even when both use the same provider.

## Target selection — host composition

A Groundwork **target** is one admitted physical store: an opaque, operator-chosen name, the provider leaf
that opens it, and the schema composed from the lanes bound to it. A host declares targets on
`GroundworkTargets`, enables the provider leaf features that can open them, and binds each persistence lane
to a target by name. Since [#1156](https://github.com/elsa-workflows/elsa-foundation/issues/1156) a host may
declare several, so design data and runtime state can live in different databases:

Provider-specific feature classes and factory selection are host-owned. For example, Workbench defines the
`GroundworkProviderSqlite` (and equivalent provider) shell features; this persistence assembly only supplies
the provider-neutral ports and target/lane contracts that those host features compose.

```jsonc
"GroundworkProviderSqlite": {},
"GroundworkTargets": { "Targets": {
    "default":   { "Provider": "sqlite", "ConnectionString": "Data Source=elsa-runtime.db" },
    "authoring": { "Provider": "sqlite", "ConnectionString": "Data Source=elsa-design.db" } } },
"WorkflowsRuntimeGroundworkPersistence":  { "Target": "default" },
"WorkflowsDesignGroundworkPersistence":   { "Target": "authoring" },
"ActivitiesDesignGroundworkPersistence":  { "Target": "authoring" }
```

Declaring one target name twice against different stores is a composition error; an exact repeat is
idempotent. Each target composes and admits only its own lanes and derives its own manifest identity, so two
targets never contend for one Groundwork schema-state row. `default` is the only well-known name and keeps
the bare `elsa-documents` identity, so databases admitted before targets existed are unaffected.

**Two operations still require co-located lanes.** Reusable-activity publication commits design, runtime and
publishing documents in one transaction and Groundwork has no cross-store transaction, so splitting those
three fails with the lane-to-target mapping named. The dashboard portfolio tile spans design and runtime and
switches to per-target queries with in-memory correlation when they differ.

### Applying a split host's schema

The host passes each target name to the provider connection and lane registrations. Startup admission then
inspects the physical schema for the units selected by that host; missing units and definition drift fail
before a store is used. Keep transaction-spanning lanes on one target, and apply independent target schemas
separately through the provider's normal deployment tooling.

The provider choice is the host's; runtime and domain code reference only neutral ports.

| Shell feature | Provider | Scope | Registration |
|---|---|---|---|
| `GroundworkProviderSqlite` | SQLite | Host-selected provider connection | `AddGroundworkSqliteProvider` |
| `GroundworkProviderPostgreSql` | PostgreSQL | Host-selected provider connection | `AddGroundworkPostgreSqlProvider` |
| `GroundworkProviderSqlServer` | SQL Server | Host-selected provider connection | `AddGroundworkSqlServerProvider` |
| `GroundworkProviderMongoDb` | MongoDB replica set | Host-selected provider connection | `AddGroundworkMongoDbProvider` |

**Optional cross-drain group commit (spec 115).** `AddGroundworkRuntimeGroupCommit(options?)` — called
after `AddGroundworkRuntimeStores()` — registers a process-wide `RuntimeGroupCommitCoordinator` that folds
concurrent checkpoint commits contending for the single durable writer into one shared unit-of-work / one
fsync (`RuntimeGroupCommitOptions.MaxBatchSize`, default 64). Off unless registered; a lone committer is
never batched, and a failed batch degrades to per-member individual commits.

The host-selected features share one provider-neutral manifest snapshot for Runtime, Secrets,
Distributed Runtime, Workflows Design, Activities Design, and Publishing. Identity contributes its own
manifest only when the host explicitly selects it and uses the matching deployment schema.
`DiagnosticsGroundworkPersistence` is the corresponding atomic host feature for Structured Logs and
OpenTelemetry: it selects both Groundwork adapters, contributes the combined diagnostics document schema,
and selects a deployment schema that also exposes the five diagnostic-record streams. The selected provider
leaf supplies the matching `IDiagnosticRecordStoreSessionFactory`; the streams must be applied by
`Groundwork.Tool` before runtime admission when startup auto-apply is disabled. When
`AutoApplySchemaOnStartup` is enabled, the host provider feature also registers a provider-native
`IDiagnosticRecordDeploymentApplier`: a `Prepare`-phase initializer creates only missing streams, rejects
definition drift before mutation, and re-inspects the complete deployment before diagnostics startup.
SQLite stays the default composition; PostgreSQL is opt-in via
`shells.json` (for example, enable `GroundworkProviderPostgreSql` alongside the lane features it needs).

**Startup admission — async initialization.** Runtime startup inspects the deployment-applied physical schema
and publishes immutable provider resources, so it is not done inside the synchronous `ConfigureServices`
factory. Each provider registers a document-store initializer as **both** an `IHostedService` (plain
hosts / tests) and a CShells `IShellInitializer` in the `LifecyclePhase.Prepare` phase (shell-composed hosts,
where shell-scoped hosted services do not run). The initializer admits the selected manifest once and publishes
a provider-owned `GroundworkStoreSessionSource`. Each scoped adapter invocation acquires a fresh immutable
Groundwork session bound to the current provider-neutral `PersistenceAccessContext`; no mutable ambient scope or
application-wide store instance is retained. The `Prepare` phase guarantees the source is ready before any
initializer reads it, and both host lifecycles await the initializer before request handling begins. The
The provider connection and lane registration signatures are explicit. A
bare `IServiceProvider` built without a host lifecycle (e.g. some tests) has no hook to run the initializer, so
it must drive it explicitly before the first provider operation. `IDocumentStore` resolves beforehand as a
`GroundworkScopedDocumentStore`; its first operation throws a descriptive `InvalidOperationException` until the
session source is admitted. Resolution itself never blocks or performs provider I/O.

The diagnostics host feature registers a second `Prepare` initializer at order `1`, after document admission at order `0`.
It participates only when the selected deployment source declares diagnostic-record streams. With startup
auto-apply disabled it performs no mutation and the existing diagnostics admission reports missing streams;
with auto-apply enabled it calls the provider deployment applier. Runtime diagnostic session factories remain
read-only in both modes.

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
([`Querying/`](Querying/GroundworkNamedQueryExecutor.cs)) and the store bridges therefore need no PostgreSQL-specific
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

## Writing a publication-projected store (`GroundworkPublicationProjectionStore<TItem>`)

Stores whose documents are projected per publication and flipped active in one atomic transition derive
from `GroundworkPublicationProjectionStore<TItem>` (itself a `GroundworkDocumentStore`). The base owns the
lifecycle both shipped derivations previously duplicated: idempotent prepare (re-prepare with identical
serialized state is a no-op; different state throws), candidate/replacement activation through
`GroundworkPublicationProjectionTransition`, per-publication delete, and the atomic commit that keeps item
documents and projection-state documents in one unit of work. A derivation supplies `ProjectionKind` (the
projection-state id discriminator), `ProjectionNoun` (error-message family), `ItemId`, `WithActive`,
`StoragePayload` (the item or its storage envelope), and `ListAllByPublicationCoreAsync`; its queries,
validation, and non-publication operations stay its own. Shipped derivations:
`GroundworkWorkflowTriggerBindingStore`, `GroundworkRecurringTriggerScheduleStore`.

## Writing a Groundwork persistence shell feature

Provider selection and persistence-lane composition are separate host concerns. The Workbench provider
features derive from `GroundworkProviderFeatureBase` and expose the connection string and optional target;
they register only the provider-owned `IStorageProviderConnection`. The host then enables the lane features
it needs, such as `GroundworkWorkflowRuntime` and the workflow/activity design features. The runtime feature
owns its target and workflow-executable cache settings and calls `AddGroundworkV2RuntimeStores` directly.

New host features should keep provider settings on the provider feature, lane settings on the lane feature,
and compose the corresponding registration explicitly. This keeps a provider adapter independent of the
host's choice of runtime, design, publishing, dashboard, or other persistence lanes.

## Cross-references

- Serialization rule and schema-evolution contract: [`../../../../docs/serialization.md`](../../../../docs/serialization.md)
- Design-time Groundwork provider catalog: [`../../Workflows/Design/Persistence/Groundwork/EXTENSION_POINTS.md`](../../Workflows/Design/Persistence/Groundwork/EXTENSION_POINTS.md)
- Repo-wide index: [`../../../../EXTENSION_POINTS.md`](../../../../EXTENSION_POINTS.md)

# Groundwork design persistence provider — implementation plan

Status: **Implemented for Elsa-side provider switch** (Groundwork foundation proven and consumed). Owner program goal:
[Groundwork persistence readiness](../program-goals/groundwork-persistence-readiness.md).
Companion verdict: [Groundwork host-configurable persistence feasibility](groundwork-host-configurable-persistence-feasibility.md).
Companion handoff: [Groundwork closed-query capability spec](groundwork-closed-query-capability-spec.md).

## Why this plan exists

The feasibility investigation is **answered and proven in code**: Elsa's design lanes no longer speak
`IQueryable`/LINQ. They speak the closed, provider-neutral `Query<TEntity>` spec through named per-aggregate
read ports, and that spec demonstrably executes on **either** a relational database (EF Core, via
`EFCoreReadStore`) **or** a document database (Groundwork, via `GroundworkReadStore<TEntity>`) returning the
same result set. What remains is **productionization**: turning the proven generic read store into a
registrable, host-selectable Groundwork **design** provider that backs every design aggregate — reads and
writes — so a host can wire one provider for the whole product.

This is deliberately captured as a plan rather than improvised: the six design aggregates are **coupled**
through their write commands (e.g. `IAddWorkflowDefinitionCommand` writes a definition *and* its first
version together), and the rich entities carry a real **serialization-model decision**. Both warrant a
deliberate, reviewed build.

## Foundation already in place (committed on the universal-provider branch)

| Building block | Location | Proof |
|---|---|---|
| Closed query contract | `Elsa.Persistence.Core/Queries/Query.cs` | Eq/In/Contains + AND-of-OR + 1 order |
| EF Core translator | `Elsa.Persistence.EFCore/.../EFCoreQueryTranslator` + `EFCoreReadStore` | 7 proof tests |
| In-memory fallback evaluator | `Elsa.Persistence.Core/Queries/InMemoryQueryEvaluator` | 11 tests, EF-identical semantics |
| Document read store | `Elsa.Persistence.Groundwork.Querying/GroundworkReadStore<TEntity>` | 11 tests, same result set as EF |
| Document envelope | `Elsa.Persistence.Groundwork.Querying/GroundworkDocument<TEntity>` | by-collection partition technique |
| Named read ports (all 6) | `*.Design.Persistence.Core/Stores/I*Store.cs` | consumers migrated; `IQueries`/`IFilter` deleted |
| Query-uplift handoff for Groundwork | capability spec | 5 bounded capabilities |

## The serialization-model decision (RECORDED)

For documents, persist the **domain projection** of each entity, not the EF storage shape:

- **Include the logical state directly.** `WorkflowDefinitionVersion.State`, `WorkflowDefinitionDraft.State`,
  `ActivityDefinitionVersion.DescriptorPayload`/`Inputs`/`Outputs`/`DesignFacets`, and
  `WorkflowDefinitionVersionLayout.Records` are serialized as first-class JSON. (These are `[NotMapped]` for EF
  only because EF stores them in a shadow `*Source` string column via saving/loading handlers. A document store
  has no such constraint, so the logical object is the payload.)
- **Exclude the EF shadow `*Source` strings.** They are a relational-storage artifact (write-once columns); the
  logical state above is the source of truth in a document.
- **Exclude navigation properties.** `WorkflowDefinitionVersion.Definition`,
  `ActivityDefinitionVersion.Definition`, etc. are separate aggregates. The `GetWithDefinition*` port methods
  already model the relationship as an **explicit second read** (no join), so the nav need never be embedded —
  avoiding write-amplification and update anomalies across aggregate boundaries.
- **Keep the indexable scalars top-level** so the by-collection index field and any future native-pushdown index
  fields (`Id`, `DefinitionId`, `SemVerSortKey`) resolve directly from the JSON. `SemVerSortKey` is a precomputed
  plain string — the store needs zero SemVer knowledge.

**Mechanism (no entity pollution):** drive exclusion with a `System.Text.Json`
`DefaultJsonTypeInfoResolver` modifier (or per-type `JsonTypeInfo` tweaks) in a shared
`GroundworkDesignJson` options factory, rather than scattering `[JsonIgnore]` across core entities. Web
(camelCase) defaults so field paths match declared index names — same convention as the runtime bridge's
`GroundworkRuntimeJson`. Deserialization targets each entity's existing parameterized constructor (members
matched by name) plus its settable/`init` members.

## Per-aggregate work (read side — mechanical once serialization lands)

Each named read port gets a Groundwork adapter that wraps `GroundworkReadStore<TEntity>`, exactly mirroring the
EF adapter that wraps `EFCoreReadStore<TDbContext,TEntity>`:

1. `IWorkflowDefinitionStore` → `WorkflowDefinition` (simplest: Id/Name/Description, no nav, no NotMapped). **Start here.**
2. `IWorkflowDefinitionVersionStore` → `WorkflowDefinitionVersion` (rich: `State`, nav, `SemVerSortKey` order, `GetWithDefinition` = 2nd read).
3. `IWorkflowDefinitionDraftStore` → `WorkflowDefinitionDraft` (rich: `State`).
4. `IWorkflowDefinitionVersionLayoutStore` → `WorkflowDefinitionVersionLayout` (rich: `Records` value-converted collection).
5. `IActivityDefinitionStore` → `ActivityDefinition`.
6. `IActivityDefinitionVersionStore` → `ActivityDefinitionVersion` (rich: `DescriptorPayload`/`Inputs`/`Outputs`/`DesignFacets`, nav, sort-key order).

The `GetWithDefinition*` methods perform the parent read as a second `FindByIdAsync` — never a join.

## Write side (the larger, coupled piece)

The design write surface is a set of named command interfaces with EF implementations over `DbContext`:

- Workflows: `IAddWorkflowDefinitionCommand`, `ICreateDraftCommand`, `IUpdateDraftCommand`,
  `IDiscardDraftCommand`, `IPromoteDraftToVersionCommand`, `ISubmitWorkflowDefinitionCommand`,
  `ICloneDraftFromVersionCommand`.
- Activities: `IAddActivityDefinitionCommand`.

Each needs a Groundwork implementation that writes `GroundworkDocument<TEntity>` envelopes via
`IDocumentStore.SaveAsync` / removes via `DeleteAsync`, stamping the constant collection partition. Because
commands span multiple aggregates (definition + version + draft) and Groundwork preview documents are
**autonomous per operation** (no cross-document transaction — see runtime Phase 2 finding), each command must be
**idempotent and ordered** so a mid-sequence failure is recoverable on retry (write the child, then the parent
pointer; or use deterministic ids so re-execution converges). This mirrors the runtime checkpoint writer's
per-id durable-marker approach.

## Design storage manifest

Mirror `ElsaRuntimeStorageManifest`: a `WorkflowsDesignStorageManifest` + `ActivitiesDesignStorageManifest`
(or one combined design manifest) declaring each document **kind** with a **by-collection keyword index**
(equality, the only thing every Groundwork provider supports today) and `PortableQueryDeclaration`s for the
enumerate-collection read. Intent `PortableDocument`, `IdentityPolicy.StringId`, `ConcurrencyPolicy.Optimistic`,
`SerializationPolicy.Json`, `PhysicalizationPolicy.Portable`. As Groundwork ships the capability-spec operators,
add native index declarations and push individual clauses down without changing the port contracts.

## Single-provider host composition

One registration entry point wires **every** lane to the chosen provider, e.g.
`AddGroundworkDesignStores(...)` alongside the existing `AddGroundworkRuntimeStores(...)`, both pointed at one
host-selected `IDocumentStore` (e.g. one produced by Groundwork's provider factory). Use the runtime registration's
`RemoveAll<TPort>() + AddSingleton<TPort, TGroundworkAdapter>()` swap pattern so composing the Groundwork
provider replaces the EF (or in-memory) registrations. The acceptance test: a host that registers only the
Groundwork provider runs the full design + runtime surface against one document database.

## Suggested project layout (mirrors the EF Core split)

- `src/Elsa/Workflows/Design/Persistence/Groundwork/Elsa.Workflows.Design.Persistence.Groundwork.csproj`
- `src/Elsa/Activities/Design/Persistence/Groundwork/Elsa.Activities.Design.Persistence.Groundwork.csproj`

Each references its `*.Design.Persistence.Core` plus `Elsa.Persistence.Groundwork.Querying`. The
host/provider feature (Sqlite document store) already exists as `Elsa.Persistence.Groundwork.Sqlite` and is
reused unchanged. (An adapter project may reference the heavy `Groundwork.Documents` package because it is **not**
a `*.Core` project — same rule the runtime bridge follows.)

## Execution order

1. **WorkflowDefinition** read adapter + design manifest + `GroundworkDesignJson` + DI swap + tests (the
   replicable pattern; no serialization complexity).
2. **WorkflowDefinitionVersion** read adapter — first rich entity; lands the serialization-model decision and a
   round-trip test (proves `State` survives, nav excluded, sort-key order works, `GetWithDefinition` = 2nd read).
3. Remaining four read adapters (mechanical replication).
4. Write commands per aggregate (idempotent/ordered), with restart-recovery tests like the runtime writer.
5. Single-provider host composition + end-to-end "one document DB backs everything" test.
6. Refresh generated maps (`bash tools/maps/generate-extension-point-map.sh`) and close out the feasibility
   report.

## Out of scope (already settled)

- No relational Groundwork providers for the design lane — every lane runs on relational **or** document, host's
  choice (host decision, 2026).
- No full ORM in Groundwork — only the bounded capability-spec uplift.
- No `Include`/join — modeled as explicit second reads.
- No SemVer logic in the store — precomputed `SemVerSortKey`.

## Progress snapshot (2026-06-18)

**Read side — done.** All six design read ports have a committed, tested Groundwork (document) adapter
(`WorkflowDefinition`, `WorkflowDefinitionVersion`, `WorkflowDefinitionDraft`, `WorkflowDefinitionVersionLayout`,
`ActivityDefinition`, `ActivityDefinitionVersion`) plus the two design manifests, the shared
`GroundworkDocumentSerialization` builder, and per-lane DI registration
(`AddGroundworkWorkflowsDesignStores` / `AddGroundworkActivitiesDesignStores`) with registration tests. A host
can now select Groundwork to back **every design read**.

## Write-side architecture decision (the central fork)

Inventorying the EF write commands showed the design **write** surface is not pure persistence — it interleaves
**provider-neutral domain orchestration** with **EF-specific persistence**, and the orchestration currently
lives *inside* the EFCore command classes (`src/Elsa/Workflows/Design/Persistence/EFCore/Commands/`,
`.../Activities/Design/Persistence/EFCore/Services/`):

- **Neutral orchestration** (already on neutral services): `IIdentityGenerator`, `IDistributedLockProvider`
  (`workflow-draft:{id}` per-draft lock), the validation gate (`OnDraftValidating`/`OnDraftValidated` events +
  `ExecuteValidations`), `IDraftStateDiffEngine`, SemVer next-version computation, lifecycle event publishing.
- **EF-specific persistence**: `IDbContextFactory<T>`, `DbSet.AddAsync`, `Entry(x).State = Modified`,
  `SaveChangesAsync` (atomic across up to 6 entities), FK **cascade delete** (DiscardDraft), upsert via
  `FirstOrDefault` + add/modify, and `OnEntityLoading` hydration of `*Source` shadows.

Re-implementing each command in a Groundwork project would duplicate ~500 lines of orchestration per provider —
wrong. **Decision:** introduce a small **provider-neutral write/unit-of-work port** and lift the command
orchestration above it so the commands become provider-neutral and depend only on read ports + the write port +
the (already neutral) collaborators. Shape (to refine):

- `IDesignUnitOfWork` (or per-lane equivalent): stage `Add(entity)` / `Update(entity)` / `Delete<T>(id)` across
  aggregates, then `CommitAsync()` for an **atomic** flush; plus explicit cascade on delete.
- EF implementation wraps a `DbContext` + `SaveChangesAsync`. Groundwork implementation batches document
  `SaveAsync`/`DeleteAsync`.
- **Hydration is not needed on the neutral layer**: for documents the `GroundworkDocumentSerialization`
  projection *is* the authored-content (de)serialization (no `*Source` shadows); the EF write-port impl keeps the
  `OnEntitySaving`/`OnEntityLoading` handlers internally.

### Open dependency — cross-document atomicity

Several commands write 2–6 related documents in one logical transaction (`Submit` = 6; `CreateDraft` = 3;
`PromoteDraftToVersion` = 2; `AddWorkflowDefinition`/`AddActivityDefinition` = 2). A document store has no
cross-document transaction by default. The neutral write port's `CommitAsync()` atomicity therefore depends on
whether Groundwork exposes a **multi-document transaction / batch** primitive (or whether we accept compensating
writes / sibling eventual-consistency). This is being investigated by the Groundwork capability-uplift session
(valence-works/Groundwork, branch `feature/closed-query-capability`); the write-side build should wait on that
finding before committing to an atomicity strategy. Until then the design write path stays on EF Core (reads can
already run on Groundwork independently).

### Host store — manifest union

A single registered `IDocumentStore` materializes from **one** `StorageManifest`. For one provider to back
runtime **and** both design lanes, that store's manifest must be the **union** of `ElsaRuntimeStorageManifest`,
`WorkflowsDesignStorageManifest`, and `ActivitiesDesignStorageManifest` (document kinds are already disjoint).
The merge belongs at the host/composition layer (which references all three). Confirm whether Groundwork's
materializer can host multiple manifests' kinds in one database, or whether a neutral
`StorageManifest`-composition helper is needed — also pending the Groundwork session's materialization findings.

---

## Resolved dependencies + concrete write design (2026-06-18)

The Groundwork capability-uplift session (branch `sfmskywalker/feature-closed-query-capability`) answered both
open dependencies above.

### Cross-document atomicity — RESOLVED (UoW being added)
Today `IDocumentStore` has **no** cross-document transaction; each `SaveAsync`/`DeleteAsync` is its own
DB transaction (so a single document + its index rows + projection is atomic, but multiple documents are not).
Groundwork *does* have an atomic-commit shape in the **operational** lane
(`IOperationalSessionFactory.BeginAsync -> IOperationalUnitOfWork` with `CommitAsync`/`RollbackAsync`,
dispose-without-commit = rollback; providers without cross-unit atomicity throw `UnsupportedAtomicCommitException`).
**Authorized** a symmetric **document-lane** UoW: `IDocumentSessionFactory.BeginAsync(scope) -> IDocumentUnitOfWork`
(`Save<T>`/`Delete<T>` staging + `CommitAsync`/`RollbackAsync`), relational = one shared `DbTransaction`,
Mongo = `IClientSessionHandle` multi-document transaction on a replica set else loud `UnsupportedAtomicCommitException`.
The neutral write port binds to this; implementation in elsa-foundation waits for the real Groundwork types.

### Host manifest union — RESOLVED (helper shipped)
Groundwork added `StorageManifestComposition.Union(identity, owner, version, params StorageManifest[])`
(commit `7644ab3`): merges `StorageUnits`, unions `RequiredCapabilities`, de-dups compatibility notes, throws
`StorageManifestCompositionException` on overlapping storage-unit identity, output passes the manifest validator.
The single-provider host feature will compose `Union("elsa.documents", runtime + workflows-design +
activities-design manifests)` behind one `IDocumentStore` (kinds are disjoint). Use a **stable** composite
identity so schema-history is stable (materialization/planning is per-manifest-identity).

### Document aggregate boundaries (the key write-model decision)
The manifest declares **four** workflow-design document kinds — `workflowDefinition`, `workflowDefinitionVersion`,
`workflowDefinitionDraft`, `workflowDefinitionVersionLayout` — and the read ports expose only these. There is
**no** document kind or read port for `WorkflowDefinitionDraftLayout` or `WorkflowDefinitionDraftValidation`
(in EF they are FK siblings of the draft with cascade delete). Decision: in the document model the draft's
**layout records and validation errors EMBED into the `workflowDefinitionDraft` document** (the draft aggregate
carries `State` + layout records + validation errors). Consequences:
- `CreateDraft`, `UpdateDraft`, `DiscardDraft` each touch **one** document (the draft) → atomic with a plain
  `SaveAsync`/`DeleteAsync`, **no UoW required**.
- `PromoteDraftToVersion` reads the draft (embedded layout + validation), gates on embedded error count, then
  writes `workflowDefinitionVersion` + `workflowDefinitionVersionLayout` = **2 docs** → needs the UoW. Last-version
  lookup (`OrderByDescending(SemVerSortKey)`) maps to the new single-field ORDER BY + `Take(1)` closed-query op.
- `AddWorkflowDefinition` writes `workflowDefinition` + `workflowDefinitionDraft` = **2 docs** → UoW.
- `SubmitWorkflowDefinition` writes `workflowDefinition` + `workflowDefinitionDraft` (embedded layout+validation)
  + `workflowDefinitionVersion` + `workflowDefinitionVersionLayout` = **4 docs** → UoW.
- Activities `AddActivityDefinitionCommand` = `activityDefinition` + `activityDefinitionVersion` = **2 docs** → UoW.

The version+layout read adapters already exist; embedding draft layout/validation means the draft read adapter
must serialize those embedded sections (currently it serializes the bare entity — extend the draft document shape
on the write slice, keeping the read store's projection in sync).

### Neutral write port + orchestration lift (per command)
Command **contracts** are already provider-neutral in `Core/Contracts` (`IAddWorkflowDefinitionCommand`,
`ICreateDraftCommand`, `IUpdateDraftCommand`, `IPromoteDraftToVersionCommand`, `IDiscardDraftCommand`,
`ICloneDraftFromVersionCommand`, `ISubmitWorkflowDefinitionCommand`). Only the **implementations** in
`EFCore/Commands` carry EF coupling. Plan:
1. Add a neutral `Core/Stores` write surface (e.g. `IDesignDocumentWriter` / per-aggregate write ports) +
   a neutral `IDesignUnitOfWork` (stage Save/Delete across aggregates, `CommitAsync` atomic).
2. Move each command implementation **into `Core`**, depending only on neutral deps it already uses
   (`IIdentityGenerator`, `IDistributedLockProvider`, `IEventPublisher`, `IDraftStateDiffEngine`, `SemVer`,
   `IActivityStructureService`) + the neutral write surface/UoW. The lock keys, validation gate
   (`OnDraftValidating` sequential), diff stream, SemVer bump, and lifecycle events are all already neutral.
3. EF write-port impl: wraps `IDbContextFactory` + `SaveChangesAsync`; keeps `OnEntityLoading` hydration and the
   `*Source` shadow serialization **internal** to the EF impl (the neutral layer never sees hydration).
4. Groundwork write-port impl: maps Save/Delete onto `IDocumentStore` (single-doc commands) and `IDocumentUnitOfWork`
   (multi-doc commands); no hydration (the document projection *is* the content).
5. `CloneDraftFromVersion` already delegates to `ICreateDraftCommand` via read ports — provider-neutral as-is.
6. Tests: re-run the existing EF command tests against the lifted Core implementation (behavior parity), then add
   Groundwork write tests mirroring the read-test doubles.

**Sequencing:** implement once the Groundwork `IDocumentUnitOfWork` types are published/consumable, so the
Groundwork write adapter binds to real types in one pass (EF parity refactor can land first if desired).

### RESUME TRIGGER (single remaining gate)
Everything above is committed + pushed. The remaining write-side + single-provider-host work is gated on making
the Groundwork build consumable here:
- **Action (owner):** publish `0.0.1-preview.closedquery` (the 10 nupkgs in the Groundwork worktree
  `artifacts/packages/`, branch `sfmskywalker/feature-closed-query-capability`, PR open) to feedz — or merge the
  Groundwork PR and let CI publish a preview. (A local NuGet source works for local-only validation but must not
  be committed.)
- **Then, in one pass** (bind to real types): bump the central Groundwork package version; build the neutral
  write port + EF adapter + Groundwork write adapter (over `IDocumentTransaction`) + lift command orchestration;
  build the single-provider host feature using `StorageManifestComposition.Union("elsa.documents", runtime +
  workflows-design + activities-design)`; add the end-to-end "one document DB backs runtime + design" test; then
  `gw-fallback-cleanup` (drop the `GroundworkReadStore` in-memory operator fallback where
  `ClosedQueryNativeSupport.Evaluate` reports native support); refresh the extension-point map; close the
  feasibility report.

### FINAL Groundwork document-UoW API (commit 0931f0a — supersedes the earlier prototype shape)
The document UoW was reshaped to mirror the operational lane. Bind the Groundwork write adapter to THESE types:
- `IDocumentStore : IDocumentSessionFactory` (the store IS the factory — no separate object to resolve).
  - `TransactionBoundary TransactionBoundary { get; }` — runtime atomicity detection.
  - `Task<IDocumentUnitOfWork> BeginAsync(DocumentCommitScope scope, CancellationToken ct = default)`.
- `IDocumentUnitOfWork : IAsyncDisposable` — `SaveAsync(SaveDocumentRequest)`, `DeleteAsync(DeleteDocumentRequest)`,
  `LoadAsync(kind, id)` (read-your-writes), `CommitAsync`, `RollbackAsync`. Dispose-without-commit = rollback;
  ops after completion throw.
- `DocumentCommitScope.Of(params string[] kinds)`; `Groundwork.Core.Transactions.TransactionBoundary`
  `{ PerOperation, CrossUnitAtomic }`; `UnsupportedAtomicCommitException { IReadOnlyList<string> Units; string? Reason }`.
- Namespaces: `Groundwork.Documents.UnitOfWork` (factory/UoW/scope), `Groundwork.Core.Transactions` (boundary/exception).

Contract: staging Save/Delete return their normal `DocumentStoreWriteResult` immediately (NOT auto-committed) —
**all-or-nothing is caller-enforced** (roll back on any non-success status or exception). Relational =
`CrossUnitAtomic` (one `DbTransaction`, holds the single connection for the UoW lifetime; Postgres aborts the
whole tx on first failed statement → rollback is the only valid next step). Mongo = `CrossUnitAtomic` only on a
replica set/sharded deployment; on standalone `TransactionBoundary` reports `PerOperation` and `BeginAsync`
throws `UnsupportedAtomicCommitException`.

**Neutral-port mapping decision:** the neutral write port checks `store.TransactionBoundary` UP FRONT — if it is
not `CrossUnitAtomic`, select a compensation path rather than assuming atomicity (no try/catch needed). We chose
**runtime detection over a manifest-declared AtomicCommit capability** because the guarantee is deployment-
dependent (Mongo standalone vs replica set), so a static manifest flag could not tell the truth. The neutral
`IDesignUnitOfWork` should expose `Stage(Save/Delete)` + `LoadAsync` (read-your-writes) + `CommitAsync` and map
1:1 onto `IDocumentUnitOfWork`; the EF adapter maps the same surface onto `DbContext` + `SaveChangesAsync`
(inherently atomic, `TransactionBoundary.CrossUnitAtomic`).

---

## CONSUMED — preview.6 landed; union host + write lane proven

The original Groundwork capability uplift was published as preview.6 and consumed here at the time
(`Directory.Packages.props`). The single-provider story is now demonstrated end-to-end in code + tests,
not just on paper.

Follow-up: Groundwork `0.0.1-preview.7` now carries the reusable document helpers and SQLite factory that this
repo previously hosted locally. Elsa now consumes those upstream APIs instead of keeping local copies.

### Done + committed (branch 073-flowchart-scoped-execution)
- **preview.6 remediation** (`4d3a2eb1`): the grown `IDocumentStore : IDocumentSessionFactory` surface
  (native closed-query overloads + `BeginAsync`/`TransactionBoundary`) is implemented on the production
  SQLite store and the 4 in-memory test doubles. All read-side Groundwork suites green.
- **Single-provider union host** (`ca025144`): `Elsa.Persistence.Groundwork.Sqlite.Unified` — one
  `IDocumentStore` materialized from `StorageManifestComposition.Union(runtime + workflows-design +
  activities-design)` under identity `elsa-documents`, registered via
  `AddGroundworkSqliteUnifiedPersistence` / the `GroundworkUnifiedPersistenceSqlite` shell feature.
  Every runtime + design read/write port resolves against the one store. E2E tests prove one in-memory
  SQLite DB materializes + serves all three lanes and that design reads run off it.
- **Design write lane — multi-document add commands** (`7f5ce090`): `IAddWorkflowDefinitionCommand` and
  `IAddActivityDefinitionCommand` are implemented as Groundwork document adapters over
  `IDocumentUnitOfWork` (definition + first child committed atomically). Shared helpers
  `GroundworkDocumentWriter` (envelope-shaped save/delete requests, read/write parity) and the atomic write
  path originally lived in `Elsa.Persistence.Groundwork.Querying`; the atomic helper has since moved upstream
  to Groundwork. Registered alongside the read ports, so the union host wires the write lane automatically.
  Tests: atomic commit + rollback, per-lane add round-trips, and a unified-host write-then-read.

### Done — Elsa-side Groundwork write lane and server switch
- **Provider-neutral orchestration moved out of EF:** `IDraftStateDiffEngine`, `DraftStateDiffEngine`,
  `WorkflowDefinitionLookup`, and `ActivityDefinitionLookup` now live in the persistence core layers and
  resolve through named stores rather than EF `DbContext` services.
- **Draft embed model implemented:** Groundwork `workflowDefinitionDraft` documents embed the draft entity,
  designer layout records, and validation errors in one document. `IWorkflowDefinitionDraftStore` exposes
  provider-neutral reads for draft lookup, layout, and validation state.
- **Orchestration-bearing workflow commands implemented for Groundwork:** `ICreateDraftCommand`,
  `IUpdateDraftCommand`, `IDiscardDraftCommand`, `IPromoteDraftToVersionCommand`,
  `ISubmitWorkflowDefinitionCommand`, `ICloneDraftFromVersionCommand`, `ISaveWorkflowDefinitionCommand`, and
  `IDeleteWorkflowDefinitionPermanentlyCommand` are registered by `AddGroundworkWorkflowsDesignStores()`.
  Multi-document writes use Groundwork atomic document writes; create/update preserve the per-draft lock and
  sequential validation gate; promotion rejects drafts with embedded validation errors.
- **Server composition switched:** `Elsa.Server` now uses `GroundworkUnifiedPersistenceSqlite` for workflow and
  activity design persistence, with demo reset clearing Groundwork document kinds instead of EF design tables.
- **Tests and catalogs updated:** focused Groundwork command tests cover create/update/promote/discard behavior,
  Groundwork activity tests cover the expanded activity store contract, and the Groundwork workflow/activity
  extension-point catalogs plus generated maps record the replacement-contract registrations.

### Remaining optimization
- `gw-fallback-cleanup`: drop the `GroundworkReadStore` in-memory operator fallback where
  `ClosedQueryNativeSupport.Evaluate` reports native support. Requires the in-memory test doubles to
  faithfully implement the `PortableDocumentQuery` overloads (currently NotSupported stubs).

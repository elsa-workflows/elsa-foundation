# Persistence & Durability Review — Elsa 4 (elsa-foundation)
---

## Executive Summary

Elsa 4's persistence layer is actually **two separate stacks** wearing one name:

1. **Design-time / definition persistence** (`Elsa.*.Design.Persistence.{Core,Groundwork,EFCore}`, `Elsa.Diagnostics.*.Persistence.EFCore`) — a genuinely DRY, template-method EF Core framework (`EFCorePersistenceShellFeatureBase<TDbContext>`) shared by Workflows, Activities, StructuredLogs and OpenTelemetry.
2. **Runtime / suspended-execution persistence** (`Elsa.Workflows.Runtime.Core` contracts + `Elsa.Persistence.Groundwork` bridge) — a well-modeled *split continuation state* domain (`WorkflowExecutionState`, `ActivityExecutionState`, `BookmarkState`, `DurableValueState`, `IncidentState`, `OperationalState`, a post-commit outbox, and a checkpoint commit ledger), backed by an **external, pre-1.0 preview package** ("Groundwork", `0.0.1-preview.*`) rather than the in-repo EF Core stack.

The architecture is intentional and documented (`docs/program-goals/groundwork-persistence-readiness.md`, `docs/reports/groundwork-runtime-evaluation.md`) as an incremental, benchmark-gated migration. That governance discipline is a genuine strength. However, digging into the actual code surfaces the exact class of problems this review was asked to hunt for:

- **There is no persisted-state schema/version stamp that is ever read or acted upon.** A single constant (`"1.0.0"`) is written to every document and never checked on load, never bumped, and there is no upcasting mechanism anywhere in the codebase. For a workflow engine, this means "what happens when a new engine version loads old state" is currently **undefined** — the answer today is "hope the record shape didn't change."
- **The durability chain has a real hole even when the durable option is switched on.** The scheduler work queue — the thing that says "what runs next" — has no Groundwork-backed implementation at all and remains process-memory-only, and nothing in the reviewed code re-drives the durable post-commit outbox after a crash. State can be perfectly durable and yet the workflow never resumes.
- **The runtime persistence bridge silently bypasses the project's own single-serializer policy** (`docs/serialization.md`), hand-rolling `System.Text.Json` in 13 files for the most important persisted payload in the system, and is not listed as a sanctioned exception.
- Two different, undocumented, coarse **global in-process semaphores** (one in the Groundwork checkpoint writer, one in the generic EF Core save command) serialize all writes of a given kind across the whole process — a correctness *crutch* that also caps throughput and does not protect multi-instance deployments.
- The design-time EF Core store is clean and DRY, but the runtime Groundwork bridge duplicates ~850 lines of boilerplate across 10 near-identical store classes with no shared base — the mirror image of the EF Core side's discipline.

Positives worth crediting up front: the split-state model is a legitimate improvement over Elsa 3's monolithic `WorkflowState` blob; type identity now persists as a rename-proof alias (`TypeReference`) instead of Elsa 3's decomposed assembly/version info; migrations run through a documented, single-node-gated startup task; and the EF Core feature-base pattern (`EFCorePersistenceShellFeatureBase<TDbContext>`) is a legitimately reusable template, in contrast to Elsa 3's per-provider-per-domain project explosion (19 EFCore persistence `.csproj` for 3 domains in elsa-core vs. one shared base + thin per-provider classes here).

---

## Persistence Architecture Map

**Design-time stack** (Workflows/Activities Design, Diagnostics): `IShellFeature` → `EFCorePersistenceShellFeatureBase<TDbContext>` → per-domain `EFCore*PersistenceFeatureBase` → per-provider `Sqlite*PersistenceShellFeature` → generic `EFCoreReadStore<TDbContext,TEntity>` / `EFCoreSaveCommand<TDbContext,TEntity>` / `EFCoreQueryTranslator` operating over a closed `Query<TEntity>` spec, entity configs with explicit indexes, EF Core migrations.

**Runtime stack** (suspended workflow state): `RuntimeCheckpointCommitter` → `IRuntimeCheckpointPersistencePolicy` (Immediate) → `IRuntimeCheckpointCommitStore` → either `InMemoryRuntimeCheckpointCommitStore` (default) or, opt-in via `AddGroundworkRuntimeStores()`, `GroundworkRuntimeCheckpointWriter`, which atomically writes 9 state kinds + a commit-idempotency marker + the post-commit outbox through one Groundwork document unit-of-work, backed by an **external** provider (`Groundwork.Core`/`Groundwork.Documents`, SQLite/SQL Server/PostgreSQL/MongoDB). Post-commit intents are drained inline, per workflow-execution, by `WorkflowExecutionDrainCoordinator` → `RuntimePostCommitOutboxProcessor` → `IRuntimePostCommitIntentDispatcher` → `IWorkflowSchedulerWorkQueue` (**always** `InMemoryWorkflowSchedulerWorkQueue`, singleton, never replaced by the Groundwork registration).

```mermaid
flowchart TB
    subgraph DesignTime["Design-time persistence (fully in-repo, EF Core)"]
        Shell[EFCorePersistenceShellFeatureBase&lt;TDbContext&gt;]
        WF[EFCoreWorkflowsPersistenceFeatureBase]
        ACT[EFCoreActivitiesPersistenceFeatureBase]
        SL[EFCoreStructuredLogsPersistenceFeatureBase]
        OTel[EFCoreOpenTelemetryPersistenceFeatureBase]
        Shell --> WF & ACT & SL & OTel
        WF --> SqliteWF[SqliteWorkflowsDesignPersistenceShellFeature]
        ACT --> SqliteACT[SqliteActivitiesDesignPersistenceShellFeature]
        ReadStore[EFCoreReadStore&lt;T,E&gt; / EFCoreSaveCommand&lt;T,E&gt;]
        WF & ACT --> ReadStore
        ReadStore --> Migrations[(EF Core Migrations, per-DbContext startup task)]
    end

    subgraph Runtime["Runtime / suspended-state persistence"]
        Committer[RuntimeCheckpointCommitter]
        Policy[IRuntimeCheckpointPersistencePolicy = Immediate only]
        CommitStore[IRuntimeCheckpointCommitStore]
        InMem[InMemoryRuntimeCheckpointCommitStore]
        GW[GroundworkRuntimeCheckpointWriter opt-in]
        Committer --> Policy --> CommitStore
        CommitStore -.default.-> InMem
        CommitStore -.opt-in AddGroundworkRuntimeStores().-> GW
        GW --> UoW[Groundwork Document Unit-of-Work: 9 state stores + commit marker + outbox, one transaction]
        UoW --> ExtPkg[(External NuGet: Groundwork.Core / Groundwork.Documents, v0.0.1-preview.*)]
        Outbox[RuntimePostCommitOutboxProcessor]
        Drain[WorkflowExecutionDrainCoordinator - inline, per workflow execution]
        Drain --> Outbox --> Dispatcher[RuntimeSchedulerPostCommitIntentDispatcher]
        Dispatcher --> Queue[IWorkflowSchedulerWorkQueue]
        Queue -.always.-> QueueMem[InMemoryWorkflowSchedulerWorkQueue — NOT durable, no Groundwork impl exists]
    end

    Serializer[IPayloadSerializer / JsonPayloadSerializer]
    WF -.entity *Source columns.-> Serializer
    GW -.bypasses, uses raw JsonSerializer + GroundworkRuntimeJson.Options.-> RawJson[13 files: hand-rolled System.Text.Json]
```

---

## Findings

### PS-1 — CRITICAL: No schema/version stamping or evolution contract for persisted runtime state
This is the existential question the review was asked to dig into, and the answer is: **there is none.**
- `ElsaRuntimeStorageManifest.SchemaVersion` is a single constant, `"1.0.0"` (`src/Elsa/Persistence/Groundwork/ElsaRuntimeStorageManifest.cs:15`), passed unconditionally to every `SaveDocumentRequest` across all 10+ store bridges (e.g. `GroundworkWorkflowExecutionStateStore.cs:28`, `GroundworkBookmarkStateStore.cs:28`, `GroundworkRuntimeCheckpointWriter.cs:178`).
- No code path reads or reacts to this version on load — `grep -rn "Upcast\|SchemaMigration\|StateVersion"` across `src/` returns zero hits.
- None of the persisted record models carry a version field themselves: `WorkflowExecutionState` (`src/Elsa/Workflows/Runtime/Core/Models/WorkflowExecutionState.cs:6-18`), `ActivityExecutionState` (`.../ActivityExecutionState.cs:20-38`), `SchedulerState`, `BookmarkState` — none declare a `Version`/`SchemaVersion` property.
- `docs/serialization.md` (the one document dedicated to persisted-payload serialization, 56 lines total) never mentions schema evolution, versioning, or a "contract for adding fields." It only states the naming/casing policy and converter registry.
- `JsonPayloadSerializer` (`src/Elsa/Serialization/SystemText/Services/JsonPayloadSerializer.cs:68-84`) configures naming policy and converters but no envelope/version metadata.

**Consequence:** today, adding/renaming/removing a field on any runtime state record is a silent breaking change for every previously-persisted suspended workflow. A record's default C# behavior (missing JSON properties get default values, e.g. `null`/`0`/`false`) will apply with no warning, no migration hook, and no test gate enforcing round-trip compatibility across versions.

**Recommendation:** Add a mandatory, per-document-kind version field that is actually read on deserialize; introduce an upcaster/migration chain (even a simple `IStateUpcaster<TFrom,TTo>` registry keyed by document kind + version) exercised by a compatibility test suite that deserializes historical fixture documents on every CI run. Treat `ElsaRuntimeStorageManifest.SchemaVersion` bump as a required step of any change to a runtime state record, enforced by a test.

---

### PS-2 — CRITICAL: Durable checkpoint, non-durable dispatch target — a genuine crash-recovery gap
Even when a host explicitly opts into full durability via `AddGroundworkRuntimeStores()` (`src/Elsa/Persistence/Groundwork/DependencyInjection/GroundworkRuntimeStoreRegistration.cs`), the scheduler work queue is **not** included in the swap list, and no Groundwork-backed `IWorkflowSchedulerWorkQueue` exists anywhere in the repo:
- `WorkflowsRuntimeApiFeature.cs:64`: `services.TryAddSingleton<IWorkflowSchedulerWorkQueue, InMemoryWorkflowSchedulerWorkQueue>();` — unconditional, never removed by `GroundworkRuntimeStoreRegistration.cs` (lines 15-54), which swaps 10 other contracts but not this one.
- `InMemoryWorkflowSchedulerWorkQueue` (`src/Elsa/Workflows/Runtime/Core/Services/InMemoryWorkflowSchedulerWorkQueue.cs`) is a plain in-process `Dictionary`/`Queue` — lost on process restart.
- Post-commit outbox delivery is **only** driven inline, per single workflow execution, from `WorkflowExecutionDrainCoordinator.DrainSchedulerAndPostCommitWorkAsync` (`src/Elsa/Workflows/Runtime/Core/Services/WorkflowExecutionDrainCoordinator.cs:47-91`), scoped by `request.WorkflowExecutionId`.
- `IRuntimeRecoveryScanner`/`InMemoryRuntimeRecoveryScanner` (`src/Elsa/Workflows/Runtime/Core/Services/InMemoryRuntimeRecoveryScanner.cs`) and `IRuntimePostCommitOutboxProcessor` are registered in DI (`WorkflowsRuntimeApiFeature.cs:54,63`) but — confirmed by repo-wide grep — **`ScanAsync` and the system-wide (`workflowExecutionId: null`) `GetDeliverableAsync` sweep are never invoked from any hosted service, timer, or startup task** in the reviewed scope (`grep -rn "BackgroundService\|IHostedService" src/Elsa/Workflows/Runtime` → no hits).

**Crash window:** checkpoint commits (state + outbox items) durably atomically via the Groundwork unit-of-work — that part is correct (see PS transactionality section below). But if the process dies *after* that commit and *before* the inline drain loop dispatches the outbox item into the in-memory queue, the workflow's continuation is durably recorded but **nothing will ever act on it** unless an external caller happens to re-issue a command against that exact `workflowExecutionId`. The `ByCollectionIndex` "list-all" query and the recovery scanner exist specifically to support a system-wide sweep on restart — but no such sweep is wired into the runtime feature.

**Recommendation:** Ship a scheduled/hosted background service that (a) periodically calls `IRuntimePostCommitOutboxProcessor.ProcessAsync` with `workflowExecutionId: null` across all providers, and (b) periodically drives `IRuntimeRecoveryScanner.ScanAsync` and re-enqueues candidates. Until then, do not describe `AddGroundworkRuntimeStores()` as delivering durability for suspended workflows — it delivers durable *storage*, not durable *resumption*.

---

### PS-3 — HIGH: Runtime state serialization bypasses the project's own single-serializer policy
`docs/serialization.md` states as a hard rule: *"All domain-payload JSON serialization and deserialization goes through `IPayloadSerializer`... Do not hand-roll `System.Text.Json.JsonSerializer` / `JsonDocument` for data that another component reads,"* with an explicit, closed list of "sanctioned exceptions" (EF Core `ValueConverter`s, HTTP boundary, expression/scripting, custom `JsonConverter`s, the reconciliation hasher).

The entire runtime persistence bridge does not comply and is not on that list:
- `GroundworkRuntimeJson.cs` (`src/Elsa/Persistence/Groundwork/Serialization/GroundworkRuntimeJson.cs`) defines an independent `JsonSerializerOptions` (`JsonSerializerDefaults.Web` + a custom `TypeInfoResolver`), not `IPayloadSerializer`.
- 13 files under `src/Elsa/Persistence/Groundwork/Stores/` call `JsonSerializer.Serialize`/`Deserialize` directly with these options (e.g. `GroundworkWorkflowExecutionStateStore.cs:22,60`, `GroundworkBookmarkStateStore.cs:23,77`, `GroundworkRuntimeCheckpointWriter.cs:173`, `GroundworkRuntimePostCommitOutboxStore.cs:125,136,144`) — vs. exactly 1 file in that area (`GroundworkDocumentSerialization.cs`, for the unrelated runtime-defined-business-data feature) that uses `IPayloadSerializer`.

**Why it matters for durability:** this is precisely the layer that would need to carry a version envelope and converter-based upcasting (PS-1). Having it silently diverge from the documented, centrally-governed serializer means any future change to the payload-serializer's naming/casing/converter policy (the thing `docs/serialization.md` exists to keep consistent) will *not* apply to suspended-workflow state, and vice versa — nobody reviewing `docs/serialization.md` would know this domain is exempt, because it isn't declared as an exception.

**Recommendation:** either route Groundwork runtime documents through `IPayloadSerializer` (extending it to accept custom `JsonSerializerOptions` composition, e.g. for the `DropDerivedExecutableProjections` modifier), or add it explicitly to the sanctioned-exceptions list in `docs/serialization.md` with the same rigor as the EF Core `ValueConverter` exception, and make it the natural home for the schema-version envelope from PS-1.

---

### PS-4 — HIGH: Architecture decision docs say "no"/"not yet" to exactly what the code already does
`docs/reports/groundwork-runtime-evaluation.md` is a "G8 decision artifact" whose header states *"This report does not migrate workflow runtime stores"* and whose matrix marks:
- **Workflow checkpoint state → `BenchmarkGate`**: *"Needs atomic state-change persistence, conflict handling, retry evidence, and checkpoint diagnostics"* before use.
- **Post-commit intents and outbox → `NoGo` (specialized provider)**: *"Requires outbox ordering, retry, idempotency, and partial-processing recovery semantics"* — explicitly **not** appropriate for the generic document store.

`docs/program-goals/groundwork-persistence-readiness.md` lists as out-of-scope: *"Folding queues, execution logs, outbox records, timers, or distributed locks into ordinary document storage without benchmark evidence."*

Yet `GroundworkRuntimeCheckpointWriter` (checkpoint state) and `GroundworkRuntimePostCommitOutboxStore` (the outbox) are both fully implemented against the generic `IDocumentStore`, already wired into `GroundworkRuntimeStoreRegistration.AddGroundworkRuntimeStores()` (lines 46-50), with the outbox store's own doc-comment (`GroundworkRuntimePostCommitOutboxStore.cs:13-24`) acknowledging the tension directly: *"This bridge deliberately uses the portable document store rather than Groundwork's operational `IOutboxStore`... reproduces the authoritative in-memory lifecycle exactly, now durable"* — a design rationale, not the benchmark evidence the governance doc requires. No benchmark artifacts or evidence references were found alongside this code.

This is not necessarily wrong (it's opt-in, and the reconciliation may simply be that the decision doc is stale relative to a later, undocumented decision) — but it is an unresolved discrepancy between the project's own stated go/no-go gate and what ships, on exactly the two capabilities (checkpoint durability, outbox correctness) this review was asked to scrutinize hardest.

**Recommendation:** either update `groundwork-runtime-evaluation.md` to reflect a superseding decision with the evidence that justified it, or gate `AddGroundworkRuntimeStores()` behind a clearer "experimental" flag until that evidence exists.

---

### PS-5 — HIGH: Two undocumented global in-process semaphores serialize all writes of a kind
- `GroundworkRuntimeCheckpointWriter` holds a single instance field `private readonly SemaphoreSlim _writeGate = new(1, 1);` (`GroundworkRuntimeCheckpointWriter.cs:28`), acquired around the *entire* `CommitAsync` body (lines 83-105). The class is registered `AddSingleton` (`GroundworkRuntimeStoreRegistration.cs:47`), so this is **one lock for every checkpoint commit of every workflow execution in the process** — not partitioned by workflow execution id. Under any real concurrency (hundreds/thousands of in-flight instances), every checkpoint commit in the process is fully serialized, one at a time, even though the underlying store already provides atomicity via `TransactionBoundary.CrossUnitAtomic` (line 129-130) and a durable idempotency marker (lines 111-125) — the lock is redundant with, not a substitute for, the transaction, and buys nothing but a throughput ceiling.
- `EFCoreSaveCommand<TDbContext,TEntity>` has an analogous `private static readonly SemaphoreSlim Semaphore = new(1, 1);` (`src/Elsa/Persistence/EFCore/Services/EFCoreSaveCommand.cs:15`, with its own comment acknowledging the pattern) — because it's `static` on a generic type, .NET gives one instance per closed `<TDbContext,TEntity>` pair, so this serializes all saves of one entity type process-wide (e.g. all `WorkflowDefinitionVersion` saves), still a coarse bottleneck, and — because it's process-local — provides **no protection at all** across multiple horizontally-scaled instances of the app talking to the same database (see PS-6).

**Recommendation:** remove the redundant lock in `GroundworkRuntimeCheckpointWriter` (the transaction + idempotency marker already give correctness) or, if it exists to avoid store-level contention, scope it per `WorkflowExecutionId`. Replace `EFCoreSaveCommand`'s semaphore with a real optimistic-concurrency check (see PS-6) that works across processes, not just within one.

---

### PS-6 — HIGH: No optimistic concurrency tokens on design-time EF Core entities
`grep -rn "ConcurrencyToken\|RowVersion\|ConcurrencyCheck\|IsRowVersion"` across all of `src/Elsa` returns **zero results**. `EFCoreSaveCommand.SaveAsync` (`EFCoreSaveCommand.cs:33-40`) does an `AnyAsync` existence check, then sets `EntityState.Modified`/`Added` unconditionally — classic last-write-wins, with no `DbUpdateConcurrencyException` handling visible beyond a generic `IDbExceptionHandler` passthrough. `EFCoreUpdateCommand.UpdateAsync`/`UpdatePartialAsync` (`EFCoreUpdateCommand.cs:18-31`) behave the same way.

By contrast, the Groundwork runtime manifest explicitly declares `ConcurrencyPolicy.Optimistic()` for every storage unit (`ElsaRuntimeStorageManifest.cs:155`) — i.e. the *newer*, external-package-backed runtime layer has a real concurrency story, while the *in-repo*, longer-standing design-time entity layer (workflow/activity definitions, drafts, versions) does not. The in-process semaphore in PS-5 masks this within a single process but provides zero protection once the design API is scaled to more than one node writing to the same database — a normal production topology for a workflow engine's design/authoring surface.

**Recommendation:** add a `RowVersion`/`xmin`-style concurrency token to entities that support concurrent edits (at minimum `WorkflowDefinitionDraft`, `WorkflowDefinitionVersion`), configure it as `IsConcurrencyToken()`, and handle `DbUpdateConcurrencyException` as a first-class conflict result rather than an unhandled throw.

---

### PS-7 — MEDIUM (DRY): 10 near-identical Groundwork store bridges, no shared base
Counted via `wc -l` on `src/Elsa/Persistence/Groundwork/Stores/*.cs` (excluding the 457-line checkpoint writer and 180-line outbox store, which are legitimately bespoke):

| File | Lines |
|---|---|
| GroundworkWorkflowExecutionStateStore.cs | 63 |
| GroundworkActivityExecutionStateStore.cs | 68 |
| GroundworkDurableValueStateStore.cs | 78 |
| GroundworkControlPlaneStateStore.cs | 82 |
| GroundworkOperationalStateStore.cs | 83 |
| GroundworkBookmarkStateStore.cs | 85 |
| GroundworkSchedulerStateStore.cs | 100 |
| GroundworkIncidentStateStore.cs | 103 |
| GroundworkActivityExecutionInspectionStore.cs | 109 |
| GroundworkWorkflowExecutableStore.cs | 110 |
| **Total** | **~881 lines** |

Each hand-rolls the identical shape: `JsonSerializer.Serialize(state, GroundworkRuntimeJson.Options)` → `store.SaveAsync(new SaveDocumentRequest(kind, id, ElsaRuntimeStorageManifest.SchemaVersion, content))`, a `Map(DocumentEnvelope)` deserializer, and (where applicable) a `BuildId`/escaping helper (compare `GroundworkWorkflowExecutionStateStore.cs:16-33,59-62` to `GroundworkBookmarkStateStore.cs:17-33,76-84` — near line-for-line identical except type names and the id key shape). This is exactly the kind of generic infrastructure the EF Core side has (`EFCoreReadStore<TDbContext,TEntity>`, `EFCoreSaveCommand<TDbContext,TEntity>`) but the Groundwork side does not.

**Recommendation:** extract a `GroundworkDocumentStateStore<TState, TKey>` base (serialize/save/load/map/id-building) parameterized by document kind and key projection; the 10 bridges above should shrink to near-zero-line subclasses, mirroring the discipline already present in `Elsa.Persistence.EFCore`.

---

### PS-8 — MEDIUM (DRY): Per-provider Sqlite feature classes duplicate boilerplate; will multiply
`SqliteWorkflowsDesignPersistenceShellFeature.cs`, `SqliteActivitiesDesignPersistenceShellFeature.cs`, `SqliteOpenTelemetryPersistenceShellFeature.cs`, `SqliteStructuredLogsPersistenceShellFeature.cs` (35–45 lines each, ~150 lines total) are structurally identical: default the connection string from `SqliteConstants.DefaultConnectionString` if unset, `services.TryAddScoped<IEntityModelCreatingHandler, SqliteEntityModelCreatingHandler>()`, and `builder.UseElsaSqlite(...)` in `ConfigureProvider`. Only class/namespace names and manifest attribute text differ.

This is a smaller, more tolerable duplication than Elsa 3's (elsa-core ships **19** separate `*.Persistence.EFCore*` `.csproj` across just 3 domains — `Elsa.Persistence.EFCore{,.MySql,.Oracle,.PostgreSql,.SqlServer,.Sqlite}`, `Elsa.AI.Persistence.EFCore{...}`, `Elsa.Secrets.Persistence.EFCore{...}` — a full project per domain × provider). elsa-foundation currently ships **only Sqlite** for every domain; the base-class pattern means adding SQL Server/PostgreSQL should cost one ~40-line class per domain rather than a new project, which is a real improvement — but that promise is untested since no second provider exists yet in this repo.

**Recommendation:** extract a small `SqliteConnectionStringDefaultingMixin`/helper so the "default connection string + register `SqliteEntityModelCreatingHandler`" pair isn't retyped per domain; revisit once a second provider is actually added to confirm the base class holds up.

---

### PS-9 — MEDIUM (DRY): Six near-identical validation methods in one class
`RuntimeCheckpointStateChangeSet` (`src/Elsa/Workflows/Runtime/Core/Models/RuntimeCheckpointCommit.cs`) declares `ValidateBookmarks` (83-89), `ValidatePostCommitOutbox` (91-97), `ValidateActivityExecutionInspections` (99-105), `ValidateIncidents` (107-113), `ValidateDurableValues` (115-121), `ValidateOperational` (123-129) — six methods with an identical shape (`if (collection.Any(change => change.StateId != change.State.XId)) throw new ArgumentException(...)`), differing only by the property name compared. A single generic helper `ValidateStateIdMatches<TState>(collection, idSelector, label)` would collapse ~45 lines to ~10.

---

### PS-10 — MEDIUM: Registered-but-orphaned recovery/outbox capability (restated from PS-2, listed separately as a DRY-adjacent "dead surface" concern)
`IRuntimeRecoveryScanner` is a fully-implemented, well-designed lease/heartbeat/interruption detector (`InMemoryRuntimeRecoveryScanner.cs`, note the name is misleading — see naming table) with **zero production callers**. Combined with PS-2, this looks like a capability that was built ahead of its integration point. Recommend either wiring it up or marking it clearly experimental/unused in doc comments so reviewers don't assume it runs.

---

### PS-11 — LOW: `Query<TEntity>` has no native paging
`src/Elsa/Persistence/Core/Queries/Query.cs` exposes `And`/`Or`/`OrderBy`/`IgnoreTenant` but no `Skip`/`Take`. Paging is bolted on separately via `QueryableExtensions.WithPage(pageArgs)` (`src/Elsa/Persistence/EFCore/Extensions/QueryableExtensions.cs:11-12`), applied *after* `EFCoreQueryTranslator.Apply`. Functionally fine (and correctly ordered after filtering), but it means paging is not part of the declared, provider-neutral store contract that `Query<TEntity>` otherwise formalizes — a new provider implementer could miss it since it isn't visible from the `Query<TEntity>` type itself.

---

### PS-12 — Positive / Informational: type-identity design correctly avoids a classic Elsa-3-style break
`TypeReference` (`src/Elsa/Primitives/Primitives/Models/TypeReference.cs:3-9`) persists only `{ Alias, CollectionKind }` — no namespace, assembly name, or assembly version — resolved through `IWellKnownTypeRegistry`/`TypeAliasConvention`. `docs/serialization.md` explicitly documents this as a deliberate fix ("the former decomposed `TypeInformation` (namespace/assembly/version) has been removed, so a package bump never breaks resolution or construction"). This is exactly the right lesson to take from workflow-engine persistence history (assembly-qualified type strings breaking on refactors/renames/version bumps is a well-known failure mode in this class of system) and is worth preserving as new state kinds are added — but note that this discipline (alias-based, no version data) is the *opposite* of what PS-1 needs for the *document envelope itself*, which does need version stamping. The two are not in tension (type identity vs. document schema are different axes), but it means the "no assembly/version anywhere" instinct evident in this codebase should not be over-applied to argue against adding a schema-version field.

---

## Transactionality Deep-Dive (Dimension 3 Detail)

- **Commit path:** `RuntimeCheckpointCommitter.CommitAsync` (`RuntimeCheckpointCommitter.cs:23-64`) folds post-commit intents into the change set *before* handing it to the store, then hard-fails (`InvalidOperationException`, line 56-61) if the store's returned `PendingPostCommitWorkIds` count doesn't match what was submitted — a good "don't silently drop continuation work" guard.
- **Store-level atomicity:** `GroundworkRuntimeCheckpointWriter.ApplyAtomicallyAsync` (`GroundworkRuntimeCheckpointWriter.cs:127-153`) requires `_commitLedger.TransactionBoundary == TransactionBoundary.CrossUnitAtomic` and throws otherwise (line 129-130) — a real fail-fast rather than a silent non-atomic fallback. All 9 state kinds + outbox + commit marker are written in one `BeginAsync(...)/CommitAsync(...)` unit of work (lines 134-147). This is a correct design for the storage half of the problem.
- **Idempotency:** a durable `CheckpointCommitDocumentKind` marker keyed by `CommitId` is checked first (`IsCommittedAsync`, lines 111-125) and short-circuits redelivery — genuine at-least-once-safe commit semantics for the storage layer.
- **Outbox delivery is at-least-once, not exactly-once, by design and by gap:** `RuntimePostCommitOutboxProcessor.ProcessItemAsync` (`RuntimePostCommitOutboxProcessor.cs:57-95`) dispatches first, then records the delivery result; a crash between those two steps leaves the item `Pending`/redeliverable on the next drain. The scheduler-side dispatcher (`RuntimeSchedulerPostCommitIntentDispatcher.cs`) relies on the target queue's own dedup (`InMemoryWorkflowSchedulerWorkQueue.EnqueueAsync`, keyed by `(WorkflowExecutionId, WorkItemId)`, lines 18-19) for idempotent re-enqueue — that dedup is correct *in-memory*, but per PS-2 there is no durable equivalent, and per PS-2 there is also no scheduled redelivery, so the theoretical at-least-once guarantee is not actually exercised end-to-end in production today.
- **Crash windows enumerated:**
  1. Crash before checkpoint commit: nothing persisted, safe (caller retries from prior state).
  2. Crash during the Groundwork unit-of-work: rolled back atomically by the underlying transaction (if the provider genuinely supports `CrossUnitAtomic` — unverified for MongoDB replica-set-less deployments from this codebase alone).
  3. Crash after commit, before inline outbox drain reaches the scheduler queue: **state and intent are durable, but nothing currently re-drives them** (PS-2) — this is the critical gap.
  4. Crash after in-memory enqueue, before the scheduler actually processes the work item: item is lost (in-memory queue), and again nothing re-derives it from the durable outbox record automatically.

---

## Naming Table

| Type / Member | File | Issue |
|---|---|---|
| `InMemoryRuntimeRecoveryScanner` | `Services/InMemoryRuntimeRecoveryScanner.cs:6` | Named "InMemory" but its only dependency is the abstract `IOperationalStateStore` — it has no in-memory-specific logic and works identically against a Groundwork-backed store. Misleading vs. genuinely in-memory-specific types like `InMemoryWorkflowSchedulerWorkQueue`. |
| `RuntimeCheckpointCommitResult` vs `RuntimeCheckpointCommitStoreResult` | `Models/RuntimeCheckpointCommitResult.cs`, `Models/RuntimeCheckpointCommit.cs` (contract file) | One extra word distinguishes "committer-level result incl. policy decision" from "store-level result (just outbox ids)" — easy to confuse in code review / IntelliSense. |
| `RuntimePostCommitOutboxItems` (static helper, plural) vs `RuntimePostCommitOutboxItem` (data record, singular) | `Services/RuntimePostCommitOutboxItems.cs` vs `Models/...` | Near-identical names for a static factory class vs. the model it produces; a stray `s` is the only differentiator. |
| `GroundworkRuntimeCheckpointWriter` | `Stores/GroundworkRuntimeCheckpointWriter.cs:26` | Called a "Writer" but it is really a cross-store transaction *coordinator* (orchestrates 9 stores + idempotency check + marker); the name undersells its responsibility. |
| `EFCorePersistenceShellFeatureBase<TDbContext>` | `Persistence/EFCore/EFCorePersistenceShellFeatureBase.cs:23` | Bundles four concerns (DbContext factory/pooling, migration scheduling, command registration, query/loading-handler registration) under one "Shell" name borrowed from the unrelated `CShells.Features` hosting concept — readable once understood, but the overload of "feature" (CShells feature vs. Elsa persistence feature) is a recurring source of ambiguity across the whole persistence tree. |
| `ElsaRuntimeStorageManifest` | `Persistence/Groundwork/ElsaRuntimeStorageManifest.cs:13` | Lives in `namespace Elsa.Persistence.Groundwork` and is the Groundwork manifest for the runtime domain, yet every sibling type in that folder is prefixed `Groundwork*` (e.g. `GroundworkRuntimeCheckpointWriter`) while this one is prefixed `Elsa*` — inconsistent naming convention within the same folder. |
| `WorkflowExecutableIdentity` / `IWorkflowExecutableStore` / `"workflowExecutable"` doc kind | `ElsaRuntimeStorageManifest.cs:29-39`, `WorkflowExecutionState.cs:8` | "Executable" as the noun for a pinned, compiled workflow-definition-version snapshot is non-obvious next to the design-time `WorkflowDefinitionVersion`; the relationship between the two ("PinnedExecutable" references which design entity, exactly how) isn't discoverable from naming alone. |
| `RuntimeWaitDependentIntentFailurePolicy` | `Models/RuntimeCheckpointCommit.cs:191-198` | Five-word compound enum type name; readable but verbose relative to its five simple members. |
| `IRuntimeCheckpointPersistencePolicy` with sole implementation `ImmediateRuntimeCheckpointPersistencePolicy` | `Services/ImmediateRuntimeCheckpointPersistencePolicy.cs` | An interface implying a family of strategies (batched/deferred/immediate) that today has exactly one member — fine as forward design, but worth flagging so reviewers don't assume batching/deferred persistence already exists. |

---

## DRY Findings Summary (Counts)

| Area | Duplicated units | Approx. duplicated lines | Contrast |
|---|---|---|---|
| Groundwork runtime state store bridges (PS-7) | 10 classes | ~880 | EF Core side has a 2-class generic base (`EFCoreReadStore<T,E>`, `EFCoreSaveCommand<T,E>`) covering the same shape for N domains |
| Sqlite per-domain shell features (PS-8) | 4 classes | ~150 | Elsa 3 comparison: 19 EFCore persistence `.csproj` across 3 domains × up to 6 providers each |
| `RuntimeCheckpointStateChangeSet` validators (PS-9) | 6 methods | ~45 | Trivially collapsible to one generic helper |
| EF Core feature-base composition (positive contrast) | 5 concrete feature-base classes (34–192 lines) inherit one shared base | N/A — this *is* the DRY pattern | Demonstrates the template-method approach the Groundwork side lacks |

---

## Comparison Anchor: Elsa 3 (elsa-core) — Lessons Addressed vs. Repeated

**Addressed:**
- Monolithic `WorkflowState` blob (elsa-core `src/modules/Elsa.Workflows.Management/Entities/WorkflowInstance.cs:34`, one JSON tree for the whole execution) → replaced by split, independently-queryable state kinds (`WorkflowExecutionState`, `ActivityExecutionState`, `BookmarkState`, etc.) in elsa-foundation — a real improvement for partial reads/writes and for reasoning about what changed in a checkpoint.
- Decomposed, assembly/version-qualified type persistence (a classic Elsa-lineage foot-gun) → replaced by alias-based `TypeReference` (PS-12).
- Project-explosion-per-provider-per-domain (19 EFCore persistence `.csproj` in elsa-core for 3 domains) → replaced by a shared template-method base plus thin per-provider classes (currently proven for Sqlite only).

**Repeated / not addressed:**
- Elsa 3's `WorkflowState` (`elsa-core/src/modules/Elsa.Workflows.Core/State/WorkflowState.cs`) has a `DefinitionVersion` field but **no state-schema version** of its own — exactly the gap re-created in elsa-foundation (PS-1). Neither generation stamps or checks a *format* version for the serialized execution state, only a *definition* version.
- Elsa 3's crash-recovery signal was a simple `WorkflowInstance.IsExecuting` boolean flag (`elsa-core/.../Entities/WorkflowInstance.cs:44-52`, explicitly documented as "allowing the system to retry execution upon restarting"); elsa-foundation designed a more principled model (`OperationalState` with `ExecutionLease`/`Heartbeat`/`InterruptedExecution`, consumed by `IRuntimeRecoveryScanner`) but — per PS-2/PS-10 — **never wires it into an actual restart-time sweep**, so today it is arguably a regression in practice (Elsa 3 at least had a flag a host *could* query at startup; elsa-foundation has a nicer model with no automatic trigger).
- Provider-count multiplication risk is deferred, not eliminated: elsa-foundation hasn't yet added a second EF Core provider or a second Groundwork provider to prove the "thin per-provider class" promise holds; Elsa 3's pain came specifically from that multiplication once MySQL/Oracle/PostgreSQL/SqlServer were all supported.

---

## Open Questions

1. **Is the external `Groundwork.*` package family (currently `0.0.1-preview.*` on NuGet) considered production-ready for hosting suspended workflow state?** The entire opt-in durability story depends on a pre-1.0 preview dependency outside this repository; its own migration/versioning guarantees could not be verified from `elsa-foundation` source alone.
2. **What is the supported production deployment recommendation *today***, given `IWorkflowSchedulerWorkQueue` has no durable implementation even under `AddGroundworkRuntimeStores()`? Is this documented anywhere for operators, or is it assumed every deployment runs a single, never-restarted process?
3. Does `GroundworkRuntimeCheckpointWriter`'s hard requirement of `TransactionBoundary.CrossUnitAtomic` (line 129-130) actually hold for the MongoDB provider mentioned throughout the manifest comments, which requires a replica set for multi-document transactions? Not verifiable from this repo.
4. Is the discrepancy in PS-4 (decision doc says BenchmarkGate/NoGo, code already ships the migration) a known, accepted supersession, or an oversight that should block promoting `AddGroundworkRuntimeStores()` beyond experimental status?
5. Should `docs/serialization.md` be updated to either bring Groundwork runtime documents under `IPayloadSerializer` or formally declare them a sanctioned exception (PS-3)? Right now neither is true, which is itself the problem.
6. Given `RuntimeWaitDependentIntentFailurePolicy` and the broader post-commit intent model already anticipate compensation/failure semantics, is there a planned spec for exactly-once outbox delivery, or is at-least-once + external idempotency the permanent target semantics? This should be stated explicitly rather than left implicit in code comments.
# Groundwork Host-Configurable Persistence Feasibility

Program goal state: [Groundwork Persistence Readiness](../program-goals/groundwork-persistence-readiness.md).

Status: draft feasibility report. Updated after re-assessment against the live Groundwork repository and a landed in-repo bridge (see "Update" below).

## Outcome

Groundwork is feasible — and now landed — as a provider-neutral persistence lane for the **entire workflow runtime** in `elsa-foundation`, with provider choice remaining host-owned via feature composition. All ten runtime persistence seams plus a durable checkpoint writer are implemented and tested over Groundwork's portable document store. **Design-time/definition persistence is out of scope for a Groundwork verdict today** — it is bound to an `IQueryable`/LINQ contract that Groundwork's equality-only portable query model cannot serve (see "Design-persistence scope"). The practical recommendation: treat Groundwork as the runtime persistence provider (opt-in), keep design persistence on EF Core, and gate any operational-hot-path or design-persistence expansion on Groundwork capability + benchmark evidence.

## Update — live re-assessment and landed bridge

This report originally concluded against an earlier Groundwork that shipped only a document store, leaving seven hot-path operational gaps (preserved below for history). Two things have changed since:

1. **Groundwork now ships an operational layer.** The live repository adds `Groundwork.Operational` (Outbox, WorkQueue, Leases, UnitOfWork) plus `Groundwork.Operational.Relational`, alongside the Documents/Relational providers (Sqlite/SqlServer/PostgreSql/MongoDb). These provide atomic claim with visibility timeout, ordered destructive dequeue, fencing leases with TTL, cross-unit atomic commit, and retry/idempotency/dead-letter state — i.e. the seven gaps in the table below are now addressed by first-class contracts that sit *alongside* `IDocumentStore`, exactly as this report recommended. The remaining question for operational stores is **evidence/benchmark**, not capability.

2. **A real opt-in bridge is landed and tested in this repo.** An `Elsa.Persistence.Groundwork` bridge implements two runtime seams — `IBookmarkStateStore` and `IWorkflowExecutableStore` — purely over Groundwork's provider-neutral `IDocumentStore`, with a host-owned `Elsa.Persistence.Groundwork.Sqlite` feature selecting the SQLite provider. The dependency is the feedz.io preview feed (`Groundwork.* @ 0.0.1-preview.4`). Tests prove identical behavior across the SQLite provider and an in-memory document store (including round-tripping the full nested `WorkflowExecutable` graph), and prove the in-memory runtime defaults are untouched unless the host composes the bridge.

This confirms the central goal: runtime/domain code depends only on Elsa's seam contracts; only the host names a concrete provider.

### Bridge design notes worth carrying forward

- **Unfiltered list via a constant partition.** `IWorkflowExecutableStore.ListAsync()` has no filter, but Groundwork's portable query contract is declared-index equality (no "scan all"). The bridge stamps a constant `collection` value on each executable document and lists via an equality query on it, so enumeration stays inside the portable contract every provider supports rather than depending on a provider-specific full scan.
- **Don't persist derived projections.** `WorkflowExecutable` recomputes `Nodes`/`NodesById` from `RootActivity` in its constructor. A bridge-local `JsonTypeInfo` modifier drops those two properties from serialization (the constructor rebuilds them on load), avoiding storing the executable graph three times. This is a serialization concern owned by the bridge, not a domain-model change.

## Update 2 — full runtime seam coverage, durable checkpoint writer, outbox

The bridge has since been extended from two seams to **all ten runtime persistence seams**, plus a durable checkpoint writer. Everything below is landed and tested in `Elsa.Persistence.Groundwork` against both the real Groundwork SQLite provider and an in-memory document store.

### Seam coverage (10/10)

`IBookmarkStateStore`, `IWorkflowExecutableStore`, `IActivityExecutionStateStore`, `IWorkflowExecutionStateStore`, `IDurableValueStateStore`, `ISchedulerStateStore`, `IOperationalStateStore`, `IControlPlaneStateStore`, `IIncidentStateStore`, and `IRuntimePostCommitOutboxStore` are all implemented over the portable `IDocumentStore`. The host swaps the in-memory defaults by composing `AddGroundworkRuntimeStores()` (driven by the `Elsa.Persistence.Groundwork.Sqlite` feature); a registration test asserts every seam plus the checkpoint writer is replaced.

### Durable checkpoint writer — and the atomicity finding

`GroundworkRuntimeCheckpointWriter : IRuntimeCheckpointWriter` orchestrates the bridged seam stores for a `RuntimeCheckpointCommit` (which spans up to seven document kinds) and records a **durable per-`CommitId` marker document** for restart-safe idempotency.

The important architectural finding: **Groundwork's preview document store is autonomous per operation — there is no cross-document transaction.** Each `SaveAsync`/`DeleteAsync` commits independently; the relational document store is constructed over a bare `DbConnection` and does not enlist in a unit of work. Groundwork's atomic `IOperationalUnitOfWork` covers only the operational stores (Outbox/Leases/WorkQueue), not documents.

This is **not** a blocker, because the checkpoint contract does not require cross-store atomicity. The reference `InMemoryRuntimeCheckpointWriter` is itself sequential and non-transactional — it applies the seam stores one by one behind a write gate and relies on **idempotent redelivery keyed by `CommitId`**. The Groundwork writer matches that model and strengthens it: the dedup marker is durable (survives restart, unlike the in-memory dedup set), and the incident append is applied idempotently so an at-least-once redelivery of a partially-applied commit completes safely. A test proves multi-seam state survives a real SQLite connection close/reopen ("restart").

If a future requirement genuinely needs all-or-nothing cross-document commit, that is a **Groundwork capability request** (a document store that can enlist in `IOperationalUnitOfWork` / share a `DbTransaction`), not something the bridge can synthesize.

### Outbox bridged over documents, not the operational outbox — and why

`IRuntimePostCommitOutboxStore` is bridged over `IDocumentStore`, deliberately **not** over Groundwork's operational `IOutboxStore`. The two contracts are structurally incompatible:

| Concern | Groundwork `IOutboxStore` | Elsa `IRuntimePostCommitOutboxStore` |
|---|---|---|
| Identity | server-generated `MessageId` + `Sequence` (not returned from append) | caller-supplied deterministic `OutboxItemId` (`commitId:intentId`) |
| Claim model | lease token + `LeaseExpiresAt` returned by `GetDeliverable` | no lease; ownership is an optional item field the in-memory store doesn't even implement |
| Ack | `RecordDeliveryResult` **requires** a valid `LeaseToken` | records by `OutboxItemId` + status, no token |
| Inline path | n/a | committer records `Delivered` immediately after `SavePending`, with no preceding `GetDeliverable` (so no lease could exist) |

Modelling each outbox item as a document reproduces the authoritative in-memory lifecycle (pending → delivering → delivered / retryable / final, with retry policy, attempt counts, and availability windows) durably, on the same portable substrate as every other seam. Groundwork's operational outbox remains a candidate for a future **transport-level** outbox, but it does not fit the runtime post-commit outbox contract.

### Provider primitive finding — no atomic insert-only

The SQLite provider has no portable atomic insert-only primitive: `ExpectedVersion = 0` on an absent document returns `NotFound` (not a create sentinel), and the in-memory test double diverges by treating it as a create. `IIncidentStateStore.TryAddAsync` is therefore implemented as read-then-create with a documented race window. This is a provider capability gap worth raising with Groundwork.

### Net effect on scope

The entire **runtime** persistence story is now provider-agnostic with only the host naming a provider. What remains for a "de facto Groundwork persistence" verdict is a product decision on **design-time/definition persistence** (currently EF Core), plus optional benchmark evidence before moving operational hot paths onto Groundwork's operational layer.

## Update 3 — Path B: closed query contract unblocks the universal provider

The Phase 4 finding below ("Design-persistence scope") concluded design persistence could **not**
become a Groundwork lane because it was bound to a full `IQueryable`/LINQ read surface
(`IQueries<TEntity>` / `IFilter<TEntity>`). That blocker has now been **removed in Elsa** (Path B /
Option 2 — chosen because the requirement is "one DB configured once at the host backs every module"):

- **A closed, provider-neutral query spec.** `Query<TEntity>` (`Elsa.Persistence.Core/Queries`) is a
  finite operation set — field `Equal` / `In` / `Contains` (case-insensitive), AND-of-OR composition,
  a single `OrderBy`/`OrderByDescending`, optional tenant-agnostic flag — covering the *entire* design-lane
  query vocabulary inventoried for this work. No `IQueryable`, no arbitrary expression trees.
- **Named per-aggregate read ports.** Every design aggregate exposes a small intent-revealing read
  port (`IWorkflowDefinitionStore`, `IWorkflowDefinitionVersionStore`, `IWorkflowDefinitionDraftStore`,
  `IWorkflowDefinitionVersionLayoutStore`, `IActivityDefinitionStore`, `IActivityDefinitionVersionStore`)
  with closed methods. Related-entity loads are explicit second reads (`GetWithDefinitionAsync`), so a
  non-relational provider needs no joins; `SemVer` knowledge stays out of the store (callers pass a
  precomputed sort key).
- **EF Core implements the same contract.** A generic `EFCoreReadStore<TDbContext, TEntity>` translates
  `Query<TEntity>` to LINQ (`EFCoreQueryTranslator`), preserving tenant filters + the `OnEntityLoading`
  hydration pipeline. Relational users are unaffected.
- **The legacy surface is gone.** `IQueries<TEntity>`, `IFilter<TEntity>`, `EFCoreQueries<,>`, and
  `ConfigureQueries<>` have been deleted; all design consumers (lookups, reconcilers, publishing, the
  clone command, the API handlers) now speak only the named ports.

**Consequence:** the design lanes are no longer LINQ-bound. The remaining work for a single universal
provider is now bounded and additive: (a) a **Groundwork adapter** implementing the named read ports
over Groundwork's portable query (native where available, in-adapter fallback until Groundwork ships the
scoped query uplift — `IN`, substring-contains, OR-composition, single-field `ORDER BY`, total-count),
and (b) **single-provider host composition** that wires every lane to one provider. The bounded query
uplift (NOT a full ORM) is specified for the Groundwork maintainers in the
[Groundwork closed-query capability spec](groundwork-closed-query-capability-spec.md). The "Recommended
implementation route" item 5 is therefore reclassified from a product decision to **in-progress
engineering**.

## What the current codebase already gives us

- Runtime persistence seams already exist as replacement contracts in `Elsa.Workflows.Runtime.Core` (`IWorkflowExecutableStore`, `IWorkflowExecutionStateStore`, `IBookmarkStateStore`, `IDurableValueStateStore`, `IIncidentStateStore`, `IOperationalStateStore`, `ISchedulerStateStore`, `IRuntimePostCommitOutboxStore`).
- The current runtime API feature registers in-memory defaults for those seams (`WorkflowsRuntimeApiFeature`), which means host composition can replace them without changing runtime domain code.
- Design persistence is currently EF Core + SQLite feature composition (`WorkflowsDesignPersistenceEFCoreSqlite`, `ActivitiesDesignPersistenceEFCoreSqlite`) and is already host-composed through shell features.

## Groundwork fit by persistence category

| Category | Fit | Notes |
|---|---|---|
| Runtime low-risk artifact/document stores | High | Good first POC target for provider-neutral host switching. |
| Runtime continuation-state stores | Medium / benchmark-gated | Possible, but requires evidence for latency/concurrency/recovery behavior. |
| Runtime operational hot-path stores (outbox ordering, mailbox/ownership, locks/leases) | Unproven | Not rejected; requires explicit operational contracts beyond current document-store guarantees. |
| Design-definition persistence | Low (current Groundwork) | Architecturally bound to `IQueryable`/LINQ + relational navigation; not served by the portable document contract today. See "Design-persistence scope" below. |

## Answer to "can Groundwork support runtime hot-path stores?"

Potentially yes, but not by assumption.

Groundwork's current document-store contract is strong for document persistence and declared indexes. Hot-path operational stores need additional guarantees that must be explicit and tested:

- deterministic ordering behavior where required,
- ownership/lease semantics for agent-style processing,
- retry and idempotency contracts under partial failure,
- recovery behavior after restart/failure windows,
- operational observability (metrics/traces/diagnostics).

Until those guarantees are modeled and proven, operational stores stay specialized or benchmark-gated.

## Hot-path gap analysis (historical — superseded by Groundwork's operational layer)

> The seven gaps below were identified against an earlier Groundwork that shipped only `IDocumentStore`. The live Groundwork now ships `Groundwork.Operational` (Outbox, WorkQueue, Leases, UnitOfWork), which supplies these primitives as contracts alongside the document store. The table is retained to document the original analysis and the contract family it predicted; treat each row as **addressed pending benchmark evidence** rather than missing.

Comparing the runtime operational contracts (`IRuntimePostCommitOutboxStore`, `IWorkflowSchedulerWorkQueue`, `IOperationalStateStore`, `IIncidentStateStore`, plus the multi-store checkpoint commit) against Groundwork's *earlier* `IDocumentStore` (Save/Load/Delete/Query with per-document optimistic concurrency, single-field equality query, per-Save transaction scope, offset paging) surfaced seven concrete gaps.

| # | Gap | Driving runtime contract | Groundwork today | Needed |
|---|---|---|---|---|
| 1 | Atomic claim with visibility timeout (lease-on-read) | `IRuntimePostCommitOutboxStore.GetDeliverableAsync` + `RecordDeliveryResultAsync` | Only `SaveAsync(expectedVersion)`; emulating claim via OCC loops degrades to retry storms under concurrency | A claim/lease primitive that marks a batch in-flight until ack or lease expiry |
| 2 | Ordered, destructive dequeue (FIFO per partition) | `IWorkflowSchedulerWorkQueue` (per-`WorkflowExecutionId` order, idempotent `DequeueAsync`) | `QueryAsync` is equality + offset paging with no ordering guarantee; no destructive dequeue | Ordering keys + atomic dequeue |
| 3 | Cross-unit / multi-document atomic commit | Runtime checkpoint commits bookmark + durable value + incident + operational + scheduler state as one logical commit | `RelationalDocumentStore` opens a transaction per `Save`; no multi-unit boundary | A unit-of-work / batch-commit contract that document-only providers can honor or explicitly reject |
| 4 | Ownership / leases / fencing | `IOperationalStateStore` (mailbox/agent ownership, recovery scanning); G8 distributed locks/leases | No lease, TTL/expiry, or fencing token model | Lease acquisition with fencing tokens and expiry |
| 5 | First-class retry/idempotency metadata | Outbox delivery (attempt counts, next-visible-at, dead-letter) | Would be hand-rolled inside `ContentJson` | Store-level attempt/visibility/dead-letter state |
| 6 | Range & comparison query operations | "items where nextVisibleAt <= now ordered by sequence" | `PortableQueryOperation` is used as `Equal` only | `<=`/`>=` and ordered scans |
| 7 | Insert-only semantics (already covered) | `IIncidentStateStore.TryAddAsync` (insert-only, false on duplicate) | Maps cleanly to unique-index conflict → `ConcurrencyConflict` | No change — this is the proof that some operational needs already fit |

The gaps cluster into **one new contract family** (claim-with-lease, ordered dequeue, visibility timeout, unit-of-work commit, range queries) that should sit **alongside** `IDocumentStore`, not inside it. Folding them into the document store would corrupt the portable document contract that is currently clean across SQLite/SQL Server/PostgreSQL/MongoDB. Groundwork's own `WorkloadFamily.OperationalStream` / `SpecializedProvider` taxonomy already anticipates this lane; the implementation simply stops at documents.

## Design note: capability model vs. workload-classification enum

Groundwork's `StorageUnit` declares a `WorkloadClassification(Family, CandidateCategory)`. The `StorageManifestValidator` then hardcodes verdicts (e.g. `OperationalStream` must be `SpecializedProvider`; `RuntimeContinuationState` cannot be `GroundworkDefault`). In parallel, Groundwork already has a richer mechanism: `ProviderCapabilityReport` + `ProviderCapabilityValidator` match a manifest's needs against a provider's declared capabilities.

Observations:

- **`CandidateCategory` is a conclusion masquerading as an input.** `GroundworkDefault` / `BenchmarkGated` / `SpecializedProvider` is the *answer* to "can this provider serve this workload?" Asking the manifest author to self-declare it is backwards, and it leaks Groundwork-centric vocabulary into what should be a neutral storage description.
- **Two parallel taxonomies answer the same question.** The `Family`→`CandidateCategory` rules and the capability report both decide provider compatibility. The enum path is the coarser, more brittle of the two, and closed enums force every new workload shape to edit core.
- **The family→category rules are policy, not schema.** Hardcoding "OperationalStream must be SpecializedProvider" freezes a judgment that should be evidence/policy-driven — exactly the G8 benchmark-gate idea.

Recommended direction: let storage units declare **requirements** (capabilities), let providers declare **support**, and **derive** the verdict.

```csharp
// Author declares WHAT the data needs — neutral, open, composable:
new StorageUnit(
    identity,
    "Outbox",
    requires: StorageRequirements.Operational(
        ordering: OrderingGuarantee.FifoPerPartition,
        delivery: DeliverySemantics.AtLeastOnceWithLease,
        visibilityTimeout: true,
        atomicBatchWith: ["bookmarks", "scheduler"]),
    ...);

// 'Family' survives only as a soft intent/label for docs & diagnostics:
intent: WorkloadIntent.OperationalStream
```

```csharp
ProviderFit fit = capabilityValidator.Evaluate(manifest, providerCapabilities);
// => Supported | RequiresEvidence(reasons) | Unsupported(missingCapabilities)
```

Wins:

- **Open & composable** — new workloads are new capability combinations, not new core enum members.
- **Intuitive for developers** — you describe your data's needs ("FIFO, leased, at-least-once"), not Groundwork's internal verdict words.
- **One source of truth** — the capability validator becomes the only compatibility authority; `BenchmarkGated`/`SpecializedProvider` becomes a computed `ProviderFit`, optionally combined with an explicit policy/evidence gate.

Keep `WorkloadFamily` as a **non-binding intent label** for diagnostics and human readability; stop letting it (and a self-declared category) be the gate.

## Design-persistence scope (Phase 4 finding — SUPERSEDED by Update 3)

> **Superseded.** This finding concluded design persistence could not become a Groundwork lane while it
> was bound to the `IQueries<TEntity>` / `IFilter<TEntity>` `IQueryable` surface. That surface has since
> been replaced by the closed `Query<TEntity>` spec + named read ports (see **Update 3**), so the blocker
> below no longer holds. The section is retained for historical context.

**Decision: a "de facto Groundwork persistence" verdict should be scoped to the runtime, with design-time/definition persistence staying on EF Core for now.** This is a capability conclusion, not a preference.

Design persistence (`Elsa.Workflows.Design.Persistence`, `Elsa.Activities.Design.Persistence`) is built on a generic `IQueries<TEntity>` / `IFilter<TEntity>` abstraction (`Elsa.Persistence.Core`) that is a **full LINQ-provider contract**:

- `IFilter<TEntity>.Apply(IQueryable<TEntity>) → IQueryable<TEntity>` — filters are arbitrary LINQ over `IQueryable`.
- `IQueries<TEntity>` exposes `Expression<Func<TEntity,bool>>` predicates, `Func<IQueryable,IQueryable>` shaping, `OrderDefinition<TEntity,TProp>` arbitrary ordering, `Expression` projections/selectors, related-entity `Include`, `Page<TEntity>` paging, `Count`, and `Any`.
- Concrete design filters use substring search (`Name.Contains(term)` case-insensitive, i.e. `LIKE %term%`), `IN` membership (`Ids.Contains(x.Id)`), multi-field equality, and computed sort keys (`SemVerSortKey`).
- The design entities are a **related cluster** — `WorkflowDefinition` ↔ `WorkflowDefinitionVersion` ↔ `WorkflowDefinitionDraft` ↔ layouts/validation — with eager-loaded navigations.

Groundwork's portable query contract (the one every provider must honor) is declared-index **equality only** (`PortableQueryOperation.Equal`, `QuerySortSupport.None`, offset paging). It has no substring/`LIKE`, no `IN`, no `ORDER BY`, no range/comparison, no joins/includes, and no `IQueryable` surface. Bridging `IQueries<TEntity>` onto it would force one of three bad outcomes:

1. **Materialize-and-LINQ-in-memory** — load whole tables and run LINQ-to-Objects. Only viable for trivial datasets; defeats the purpose.
2. **A LINQ→Groundwork translator** — impossible against an equality-only portable contract (no operators to translate `Contains`/ordering/joins into), and a major undertaking even partially.
3. **Provider-specific raw queries** — breaks the portability promise that makes the bridge valuable.

This is the mirror image of the runtime seams, which are narrow key/index access patterns (`Save`/`Load`/`Delete`/equality-`Query`) that map cleanly onto documents. **Runtime persistence fits Groundwork; design persistence does not — yet.**

For design persistence to become a Groundwork lane, Groundwork would need to grow (a) a richer portable query capability (comparison, substring, `IN`, multi-field `ORDER BY`, paging) or a queryable provider surface, and (b) an aggregate/related-entity model for the design cluster. Those are **Groundwork roadmap items**, not bridge work, and should be evidence/benchmark-gated like the operational hot paths. Until then, design persistence stays EF Core, and the host still composes it independently — so the "only the host names a provider" property already holds for both lanes (EF Core for design, Groundwork for runtime).

## Recommended implementation route

1. ✅ **Done.** Opt-in `Elsa.Persistence.Groundwork` bridge with the provider adapter resolved at host composition time (`Elsa.Persistence.Groundwork.Sqlite`).
2. ✅ **Done (all ten runtime seams).** Every runtime store now has a Groundwork bridge over `IDocumentStore` — `IBookmarkStateStore`, `IWorkflowExecutableStore`, `IActivityExecutionStateStore`, `IWorkflowExecutionStateStore`, `IDurableValueStateStore`, `ISchedulerStateStore`, `IOperationalStateStore`, `IControlPlaneStateStore`, `IIncidentStateStore`, `IRuntimePostCommitOutboxStore` — each with provider-neutral and registration tests. See "Update 2" above.
3. ✅ **Done.** A durable `GroundworkRuntimeCheckpointWriter` orchestrates the seam stores for atomic-by-idempotent-replay checkpoint commits, proven across a simulated SQLite restart.
4. ✅ **Done.** Existing in-memory defaults stay intact when Groundwork is not composed (covered by a regression test).
5. 🚧 **In progress (Path B — see Update 3).** Design-time/definition persistence is being unbound from the `IQueryable`/LINQ surface so a single host-selected provider can back every lane. Done: closed `Query<TEntity>` spec, named per-aggregate read ports, EF Core adapter on the same contract, and deletion of `IQueries<T>`/`IFilter<T>`. Remaining: Groundwork design adapter, single-provider host composition, and the Groundwork capability spec handoff.
6. ⏳ **Open (evidence-gated).** Add a hot-path viability matrix/benchmark before migrating operational seams onto Groundwork's *operational* layer (leases/work queue/transport outbox), gated on evidence rather than capability.

## POC acceptance criteria

- Provider can be switched in host composition without runtime/domain code changes.
- Runtime consumes the same store contract independent of provider adapter.
- Existing non-Groundwork composition still runs unchanged.
- Reported operational hot-path candidates have explicit evidence gates before migration.


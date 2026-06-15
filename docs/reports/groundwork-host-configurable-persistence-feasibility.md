# Groundwork Host-Configurable Persistence Feasibility

Program goal state: [Groundwork Persistence Readiness](../program-goals/groundwork-persistence-readiness.md).

Status: draft feasibility report. Updated after re-assessment against the live Groundwork repository and a landed in-repo bridge (see "Update" below).

## Outcome

Groundwork is feasible as a provider-neutral persistence lane in `elsa-foundation`, with provider choice remaining host-owned via feature composition. A full all-in replacement for every runtime persistence surface is not yet proven.

## Update — live re-assessment and landed bridge

This report originally concluded against an earlier Groundwork that shipped only a document store, leaving seven hot-path operational gaps (preserved below for history). Two things have changed since:

1. **Groundwork now ships an operational layer.** The live repository adds `Groundwork.Operational` (Outbox, WorkQueue, Leases, UnitOfWork) plus `Groundwork.Operational.Relational`, alongside the Documents/Relational providers (Sqlite/SqlServer/PostgreSql/MongoDb). These provide atomic claim with visibility timeout, ordered destructive dequeue, fencing leases with TTL, cross-unit atomic commit, and retry/idempotency/dead-letter state — i.e. the seven gaps in the table below are now addressed by first-class contracts that sit *alongside* `IDocumentStore`, exactly as this report recommended. The remaining question for operational stores is **evidence/benchmark**, not capability.

2. **A real opt-in bridge is landed and tested in this repo.** An `Elsa.Persistence.Groundwork` bridge implements the runtime `IBookmarkStateStore` seam purely over Groundwork's provider-neutral `IDocumentStore`, with a host-owned `Elsa.Persistence.Groundwork.Sqlite` feature selecting the SQLite provider. The dependency is the feedz.io preview feed (`Groundwork.* @ 0.0.1-preview.4`). Tests prove identical behavior across the SQLite provider and an in-memory document store, and prove the in-memory runtime default is untouched unless the host composes the bridge.

This confirms the central goal: runtime/domain code depends only on Elsa's seam contract; only the host names a concrete provider.

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
| Design-definition persistence | Medium | Possible future lane, but out of current POC scope. |

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

## Recommended implementation route

1. ✅ **Done.** Opt-in `Elsa.Persistence.Groundwork` bridge with the provider adapter resolved at host composition time (`Elsa.Persistence.Groundwork.Sqlite`).
2. ✅ **Done (first seam).** One runtime store landed behind an existing replacement contract — `IBookmarkStateStore` over Groundwork's `IDocumentStore` — with provider-neutral and registration tests. `IWorkflowExecutableStore` remains a candidate for the next document-shaped seam.
3. ✅ **Done.** Existing in-memory defaults stay intact when Groundwork is not composed (covered by a regression test).
4. ⏳ **Open.** Add a hot-path viability matrix/checklist before migrating operational seams (outbox, scheduler work queue, leases) onto Groundwork's operational layer, gated on benchmark evidence rather than capability.

## POC acceptance criteria

- Provider can be switched in host composition without runtime/domain code changes.
- Runtime consumes the same store contract independent of provider adapter.
- Existing non-Groundwork composition still runs unchanged.
- Reported operational hot-path candidates have explicit evidence gates before migration.


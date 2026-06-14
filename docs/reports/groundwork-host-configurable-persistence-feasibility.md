# Groundwork Host-Configurable Persistence Feasibility

Program goal state: [Groundwork Persistence Readiness](../program-goals/groundwork-persistence-readiness.md).

Status: draft feasibility report.

## Outcome

Groundwork is feasible as a provider-neutral persistence lane in `elsa-foundation`, with provider choice remaining host-owned via feature composition. A full all-in replacement for every runtime persistence surface is not yet proven.

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

## Hot-path gap analysis

Comparing the runtime operational contracts (`IRuntimePostCommitOutboxStore`, `IWorkflowSchedulerWorkQueue`, `IOperationalStateStore`, `IIncidentStateStore`, plus the multi-store checkpoint commit) against Groundwork's current `IDocumentStore` (Save/Load/Delete/Query with per-document optimistic concurrency, single-field equality query, per-Save transaction scope, offset paging) surfaces seven concrete gaps.

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

1. Implement an opt-in `Elsa.Persistence.Groundwork` bridge with provider adapters resolved at host composition time.
2. Implement one runtime low-risk POC store behind an existing replacement contract (`IWorkflowExecutableStore`).
3. Keep existing defaults intact when Groundwork is not enabled.
4. Add a hot-path viability matrix/checklist to prevent silent migration and prevent premature rejection.

## POC acceptance criteria

- Provider can be switched in host composition without runtime/domain code changes.
- Runtime consumes the same store contract independent of provider adapter.
- Existing non-Groundwork composition still runs unchanged.
- Reported operational hot-path candidates have explicit evidence gates before migration.


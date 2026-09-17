# Phase 0 Research: Outbox Delivery Contention

**Feature**: 169-outbox-delivery-contention · **Branch**: `1305-outbox-delivery-contention` · **Date**: 2026-09-17

All findings below were read from source at `main` (`6c4d6d236`). No unknowns remain; the Technical Context in [plan.md](./plan.md) carries no `NEEDS CLARIFICATION` markers.

---

## R1 — The failing chain, confirmed from source

**Decision**: The chain described in issue #1798 is accurate, and the failure is a read-then-record race plus a second deterministic trigger.

**Evidence**:

| Step | Location |
|---|---|
| Live drain forces the claim-less branch | [`RuntimePostCommitOutboxProcessor.ProcessAsync`](../../src/Elsa/Workflows/Runtime/Services/RuntimePostCommitOutboxProcessor.cs#L123) — `if (_claimStore is not null && !DeliversInMemory(request))`, else-branch at :138 processes each item with `claim: null` |
| Claim-less recording routes to the single-arg contract | [`RecordDeliveryResultAsync`](../../src/Elsa/Workflows/Runtime/Services/RuntimePostCommitOutboxProcessor.cs#L293) — `else await _outboxStore.RecordDeliveryResultAsync(result, cancellationToken)` |
| EF store rejects any claimed or fenced row | [`EfRuntimePostCommitOutboxStore`](../../src/Elsa/Workflows/Runtime/Persistence/EntityFrameworkCore/Stores/EfRuntimePostCommitOutboxStore.cs#L158) — `if (current.Status == Delivering \|\| current.DeliveryFencingToken > 0) throw` |
| The result model has no owner or fence to present | `RuntimePostCommitOutboxDeliveryResult` carries `outboxItemId`, `status`, `recordedAt`, `failureMessage` only |

**Alternatives considered**: that the reporter's stack trace was from a stale build — rejected, the code at `main` matches the trace exactly.

---

## R2 — The competing deliverer is the resumption sweep

**Decision**: The competitor is the timer-driven resumption sweep, not another workflow start.

**Rationale**: The live-drain scope is keyed to a single execution — [`AppliesTo`](../../src/Elsa/Workflows/Runtime/Core/Models/RuntimeLiveDrainDeliveryScope.cs#L41) matches one `WorkflowExecutionId`, and [`DrainImmediateAsync`](../../src/Elsa/Workflows/Runtime/Services/WorkflowDrainOrchestrator.cs#L239) pushes it for that execution only. Two concurrent starts are two different executions and do not contend. But [`RuntimeResumptionService.SweepAsync`](../../src/Elsa/Workflows/Runtime/Services/RuntimeResumptionService.cs#L111) calls the processor with `workflowExecutionId: null, intentKind: null` — no filter at all — on the **claim** path, driven by [`RuntimeResumptionPumpTask`](../../src/Elsa/Workflows/Runtime/Resumption/RuntimeResumptionPumpTask.cs#L75) on a timer.

**This falsifies a documented invariant.** [`RuntimeLiveDrainDeliveryScope`](../../src/Elsa/Workflows/Runtime/Core/Models/RuntimeLiveDrainDeliveryScope.cs#L10) states: *"Ownership is bounded by the drain's single-writer lease, so no other deliverer competes for the same execution's intents."* The sweep competes for every execution's intents. FR-014/FR-015 correct this.

**Corroboration**: this also explains the reporter's correction that a single start reproduces the fault — the sweep supplies the contention, so no second start is needed.

---

## R3 — The issue's top-ranked candidate is already implemented

**Decision**: Do not "fix" the deliverable selection's `Delivering` exclusion. It exists.

**Evidence**: [`QueryCandidatesAsync`](../../src/Elsa/Workflows/Runtime/Persistence/EntityFrameworkCore/Stores/EfRuntimePostCommitOutboxStore.cs#L404) filters `DeliverableAtUtcTicks != null` for `CandidateSelection.Deliverable`, and [`DeliverableAt`](../../src/Elsa/Workflows/Runtime/Persistence/EntityFrameworkCore/Stores/EfRuntimePostCommitOutboxStore.cs#L579) returns `null` unless the item is `Pending` or non-exhausted `FailedRetryable` — so `Delivering` is already excluded.

**Consequence**: a read-side fix cannot close the window, because the sweep's claim lands between the read and the record. This is why the fix must be on the **write** side (FR-001). Recorded prominently in the spec so it is not re-proposed.

---

## R4 — The second trigger is deterministic, not a race

**Decision**: Treat the fencing-token condition as a distinct defect, fixed in the same unit (FR-010).

**Rationale**: [`Claim`](../../src/Elsa/Workflows/Runtime/Core/Models/RuntimePostCommitOutbox.cs#L443) computes `checked(item.DeliveryFencingToken + 1)` and nothing ever resets it. An expired or retryably-failed claim returns the item to `Pending`/`FailedRetryable`, which `DeliverableAt` treats as deliverable — while the row still carries `DeliveryFencingToken > 0`. The store's `|| current.DeliveryFencingToken > 0` clause then rejects the next claim-less recording **on the first attempt, every time**.

**Alternatives considered**: reset the fence on release. Rejected — the fence's monotonicity is what makes stale-claim rejection sound ([`Complete`](../../src/Elsa/Workflows/Runtime/Core/Models/RuntimePostCommitOutbox.cs#L476) compares `current.DeliveryFencingToken != claim.FencingToken`). Excluding fenced rows from the claim-less candidate set preserves that guarantee.

---

## R5 — The dead ownership filter is the reimplementation vector

**Decision**: Remove `RuntimePostCommitOutboxQuery.OwnerId` rather than implement it (FR-011, FR-013).

**Evidence**: The parameter is declared at [`RuntimePostCommitOutboxQuery`](../../src/Elsa/Workflows/Runtime/Core/Models/RuntimePostCommitOutbox.cs#L705) and validated for non-blankness, but **no production caller sets it**. Four call sites construct the query — the EF store's own claim path, the migration quiescence probe, the processor, and tests — none passes `ownerId:`. Both surviving stores reject it with the same sentence:

- [`EfRuntimePostCommitOutboxStore`](../../src/Elsa/Workflows/Runtime/Persistence/EntityFrameworkCore/Stores/EfRuntimePostCommitOutboxStore.cs#L88): *"The EF post-commit outbox store does not implement delivery ownership filtering."*
- [`InMemoryRuntimeCheckpointCommitStore`](../../src/Elsa/Workflows/Runtime/Services/InMemoryRuntimeCheckpointCommitStore.cs#L315): *"The in-memory post-commit outbox store does not implement delivery ownership filtering."*

Groundwork carried the identical sentence before [PR #1764](https://github.com/elsa-workflows/elsa-foundation/pull/1764) removed it. **Three stores, one refusal, reimplemented each time.** The parameter looks like the ownership hook, so each new store dutifully reimplements the refusal instead of solving the problem. Deleting it removes the invitation.

**Impacted test**: [`EfRuntimePostCommitOutboxStoreTests.cs:88`](../../tests/Elsa/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/EfRuntimePostCommitOutboxStoreTests.cs#L88) asserts the throw and must be removed — see the §2.21.1 gate in [plan.md](./plan.md#constitution-check).

---

## R6 — Precedent for returning an outcome from this contract family

**Decision**: Model the superseded outcome on the existing claim-completion outcome enum.

**Rationale**: [`IRuntimePostCommitOutboxClaimCompletionStore.CompleteClaimAsync`](../../src/Elsa/Workflows/Runtime/Core/Contracts/IRuntimePostCommitOutboxClaimCompletionStore.cs) **already returns** `RuntimePostCommitOutboxClaimCompletionOutcome` ([`Persisted`, `DeliveredOnChildEvidence`](../../src/Elsa/Workflows/Runtime/Core/Models/RuntimePostCommitOutbox.cs#L349)), and the processor already branches on it. The new outcome extends an enum the subsystem already uses for exactly this purpose: *"the store persisted something other than what you asked for, here is what."*

This matters for §2.24 (sanctioned patterns): no new pattern is introduced. It is the same shape, applied to the one contract in the family that lacks it.

**Alternatives considered**: a bespoke result record per call. Rejected — gratuitously different from the established shape, for no gain.

---

## R7 — Blast radius of the breaking change

**Decision**: Three implementations change; one is a decorator.

| Implementation | Role |
|---|---|
| [`EfRuntimePostCommitOutboxStore`](../../src/Elsa/Workflows/Runtime/Persistence/EntityFrameworkCore/Stores/EfRuntimePostCommitOutboxStore.cs#L18) | Durable store — carries the throw being replaced |
| [`InMemoryRuntimeCheckpointCommitStore`](../../src/Elsa/Workflows/Runtime/Services/InMemoryRuntimeCheckpointCommitStore.cs#L20) | In-memory store — must report the same outcomes |
| [`CoalescingRuntimePostCommitOutboxStore`](../../src/Elsa/Workflows/Runtime/Services/Coalescing/CoalescingRuntimePostCommitOutboxStore.cs#L12) | Overlay decorator — returns the session's outcome when the session owns the item, otherwise passes through |

The coalescing overlay needs care: when a session owns the item it records into the working set and cannot be superseded (no durable claim exists yet), so it reports the persisted outcome. Pass-through must propagate the inner store's outcome unchanged rather than discarding it.

**Callers**: the only production caller of the single-argument recording method is [`RuntimePostCommitOutboxProcessor`](../../src/Elsa/Workflows/Runtime/Services/RuntimePostCommitOutboxProcessor.cs#L293). Its private `RecordDeliveryResultAsync` already returns an outcome, so threading the store's outcome through it is a small change.

---

## R8 — Test infrastructure available for the D5 gate

**Decision**: Processor-level and store-level tests use existing infrastructure; a container-backed test reuses the existing PostgreSQL fixture; no host-level test is built.

**Findings**:

- **No existing test at any layer runs two deliverers against one row.** Every fencing test serializes the claimers on one thread (e.g. `ExpiredClaim_IsReclaimedWithHigherFenceAndRejectsStaleAcknowledgement` advances a clock rather than racing). This is why the defect survived.
- **SQLite lane**: `tests/Elsa/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/` uses a private in-memory SQLite `TestDatabase` per file; no Npgsql, no containers. SQLite's serialized writes would mask this race.
- **PostgreSQL lane**: `tests/.../EntityFrameworkCore/ProviderTests/` runs `postgres:16-alpine` via Testcontainers, with `[SkippableFact]` + `Skip.IfNot(fixture.IsAvailable, …)` so container-less machines skip. `RuntimeOperationalStateProviderSmokeTests` already drives this exact store there. Already a CI matrix entry (`.github/workflows/ci.yml:158`).
- **No host-level path exists**: the only host mapping the execute route stubs `IWorkflowExecutionStartService`; `Elsa.Foundation.Host` is never booted in a test; there is no `WebApplicationFactory` in the repository; `WorkflowExecutionHarness` hardcodes `WorkflowExecutionId = "wfexec-1"`.

**Consequence**: FR-016/FR-017 are cheap and deterministic; FR-018 is cheap because the fixture exists; the host-level test is out of scope (spec, *Out of Scope*).

---

## R9 — The duplicate-key log noise (scope boundary)

**Decision**: Out of scope, recorded as a non-defect (D6/FR-021). Not diagnosed.

**Findings**: The PK is a SHA-256 of `(scope, workflowExecutionId, workItemId)`, and the materialized work item — including its `WorkItemId` — is frozen into the outbox payload at intent creation, so a double dispatch of one intent is a same-PK attempt by construction. [`EfSchedulerWorkQueueStore`](../../src/Elsa/Workflows/Runtime/Persistence/EntityFrameworkCore/Stores/EfSchedulerWorkQueueStore.cs#L49) catches `DbUpdateException`, classifies SqlState `23505`, re-reads the winner and returns it; EF Core's own update logger emits the raw error at Error level *before* that `when` filter runs — exactly the reported "logged, no 500" signature.

**Why not diagnosed**: two other routes produce an identical log line and cannot be distinguished from source — double *execution* of one work item (successor ids derive deterministically via `RuntimeChainId.Derive`), and ordinary multi-pump concurrency across roughly nine enqueue sites. The confirming check is recorded in the spec's *Out of Scope* section.

---

## R10 — Follow-up deferred by decision (D7)

**Decision**: The missing concurrency guard on the EF claim-completion path is **filed separately**, not fixed here.

**Evidence, corrected after reading the code**: `ClaimAsync` catches `DbUpdateConcurrencyException` and detaches (skipping the item). `CompleteClaimAsync` **does have a guard too** — the original note that it had none was wrong. It rolls back, re-reads, re-runs the transition to surface a stale claim, then throws `InvalidOperationException("…changed concurrently; retry completion.")`.

The remaining question is whether that asymmetry is a defect. It is plausibly correct: a skipped claim is re-claimed next cycle, whereas a skipped completion loses the record of a delivery that already happened. Not the reporter's defect either way — their logs carry only the claim-less message.

**Scheduling constraint** (architect, this session): downstream consumers are blocked by the 500 this unit fixes, so nothing here may sit on that fix's critical path. Reflected in the phase ordering in [plan.md](./plan.md#implementation-phases).

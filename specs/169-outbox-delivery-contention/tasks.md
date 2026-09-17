# Tasks: Tolerate Concurrent Post-Commit Outbox Delivery Contention

**Feature**: 169-outbox-delivery-contention · **Branch**: `1305-outbox-delivery-contention` · **Date**: 2026-09-17

**Input**: [spec.md](./spec.md) · [plan.md](./plan.md) · [research.md](./research.md) · [data-model.md](./data-model.md) · [contracts/](./contracts/outbox-delivery-recording.md) · [quickstart.md](./quickstart.md)

**Tests are REQUIRED for this feature.** D5 makes tests the acceptance gate, and FR-019 requires each to be proven red on revert.

---

## Hazards — read before starting

These were audited from source, not assumed. Two of them invert what you would expect.

| # | Hazard | Audited finding | Task |
|---|---|---|---|
| **H1** | Removing `OwnerId` shifts `intentKind` from the 5th to the 4th parameter — a 4-positional call site would reinterpret silently | **Does not materialize.** All 63 construction sites audited; maximum positional count is **3**. The three `ownerId` references are all **named**, so all three become compile errors. Risk is to *external* callers only. | T023 |
| **H2** | Adding an enum value is usually caught by a non-exhaustive `switch` | **No safety net exists.** No `switch` over this enum anywhere — every consumer uses `==` — and `Directory.Build.props` is warnings-only with no `TreatWarningsAsErrors`. **Zero** build-time signal. Every `==` site silently takes its `else`. | T012, T013 |
| **H3** | The issue's top-ranked fix (exclude `Delivering` from the deliverable selection) | **Already implemented.** Changing it accomplishes nothing and the defect returns. The race is read-to-write; only the write-side fix closes it. | T019 |
| **H4** | "One test asserts the removed refusal" | **Three sites, of three different kinds.** Only one is a whole test method needing §2.21.1 approval. | T026, T027, T028 |
| **H5** | Two independent production changes share one test suite | Reverting both at once cannot tell you which test guards which fix. They must be reverted **independently**. | T017, T018 |

---

## Phase 1: Setup

- [x] T001 Confirm the working tree builds clean before any change, by building `src/Elsa/Workflows/Runtime/Elsa.Workflows.Runtime.csproj` and running `tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj`, recording the baseline pass count so later red/green comparisons are against a known baseline
- [x] T002 Read `docs/agents/` guidance on building on a machine shared by parallel sessions, and confirm no other session holds the runtime build outputs before starting

---

## Phase 2: Foundational — the contract

**Blocking.** Every user story depends on the outcome value and the contract signature existing. No story can start until this phase completes.

- [x] T003 Add the `SupersededByOtherOwner` value to `RuntimePostCommitOutboxClaimCompletionOutcome` in `src/Elsa/Workflows/Runtime/Core/Models/RuntimePostCommitOutbox.cs`, with an XML doc stating that nothing was written, the owning deliverer's completion governs, and the item stays recoverable by claim expiry and the sweep
- [x] T004 Change `IRuntimePostCommitOutboxStore.RecordDeliveryResultAsync` in `src/Elsa/Workflows/Runtime/Core/Contracts/IRuntimePostCommitOutboxStore.cs` to return `ValueTask<RuntimePostCommitOutboxClaimCompletionOutcome>`, documenting obligations C1–C7 from [contracts/outbox-delivery-recording.md](./contracts/outbox-delivery-recording.md#4-behavioural-contract-for-implementers)
- [x] T005 Build the solution and capture the complete list of resulting compile errors — this list IS the blast radius, and it must match the three implementations named in [research.md R7](./research.md#r7--blast-radius-of-the-breaking-change). Investigate any site the research did not predict before proceeding

**Checkpoint**: contract compiles; every implementation is a known, enumerated break.

---

## Phase 3: User Story 1 — A start succeeds while background delivery is in flight (P1) 🎯 MVP

**Goal**: The contended recording returns an outcome instead of throwing, so the start answers success.

**Independent test**: Record a delivery result claim-lessly for an item another deliverer holds; assert `SupersededByOtherOwner` and no state change, with no exception.

### Implementation

- [x] T006 [US1] Replace the throw in `EfRuntimePostCommitOutboxStore.RecordDeliveryResultAsync` in `src/Elsa/Workflows/Runtime/Persistence/EntityFrameworkCore/Stores/EfRuntimePostCommitOutboxStore.cs:158` with `return SupersededByOtherOwner`, writing nothing — no status, owner, fence, attempt count, failure message or availability change (FR-001, FR-003, obligations C1/C2). **Keep the terminal-item throw at :157 and the not-found throw intact** (C4, C5) — those are double-completion and missing-row bugs, not contention
- [x] T007 [P] [US1] Update `InMemoryRuntimeCheckpointCommitStore.RecordDeliveryResultAsync` in `src/Elsa/Workflows/Runtime/Services/InMemoryRuntimeCheckpointCommitStore.cs` to return the same outcomes under the same conditions, so the in-memory and durable stores agree (FR-009)
- [x] T008 [P] [US1] Update `CoalescingRuntimePostCommitOutboxStore.RecordDeliveryResultAsync` in `src/Elsa/Workflows/Runtime/Services/Coalescing/CoalescingRuntimePostCommitOutboxStore.cs:33` to return `Persisted` on the session-owned branch (a session-owned item has no durable claim and cannot be superseded) and to **propagate the inner store's outcome unchanged** on the pass-through branch — do not discard it and do not substitute `Persisted`
- [x] T009 [US1] Thread the store's outcome through `RuntimePostCommitOutboxProcessor.RecordDeliveryResultAsync` in `src/Elsa/Workflows/Runtime/Services/RuntimePostCommitOutboxProcessor.cs:293`, replacing the unconditional `return Persisted` at :295 with the value the store returned
- [x] T010 [US1] Make `ProcessItemAsync` in `src/Elsa/Workflows/Runtime/Services/RuntimePostCommitOutboxProcessor.cs:165` classify a superseded success-path item as neither delivered nor failed, so it is excluded from `DeliveredCount` (FR-004) and from `FailedCount` (FR-005). This requires a processed-item representation that can express the third state — `RuntimePostCommitOutboxProcessResult` in `src/Elsa/Workflows/Runtime/Core/Models/RuntimePostCommitOutboxProcessContracts.cs` computes both counts from `RequestedDeliveryResultStatus`, which today has no value meaning "not mine"
- [x] T011 [US1] Make the failure path in `ProcessItemAsync` handle supersession too: when the dispatch failed **and** the recording was superseded, do not report a failure the owning deliverer will record itself (FR-005, and the *"item superseded on a failure recording"* edge case in [spec.md](./spec.md#edge-cases))

### Hazard H2 — the enum audit (do not skip; there is no compiler help here)

- [x] T012 [US1] Audit **every** `==` comparison against `RuntimePostCommitOutboxClaimCompletionOutcome` and decide the new value's behaviour at each. Confirmed sites: `RuntimePostCommitOutboxProcessor.cs:207`, `:248`, `:280`. Re-grep to confirm none were added. **`:207` is the high-risk one**: `outcome == DeliveredOnChildEvidence ? Delivered : effectiveStatus` would log a superseded item with the *failure* status, misreporting an item this deliverer never wrote
- [x] T013 [US1] Add a test in `tests/Elsa/Workflows/Runtime/Tests/RuntimePostCommitOutboxProcessorTests.cs` pinning the logged status for a superseded item, so the `:207` branch is covered by assertion rather than by inspection

### Tests for User Story 1 (the primary gate)

- [x] T014 [US1] Add the injected-steal test to `tests/Elsa/Workflows/Runtime/Tests/RuntimePostCommitOutboxProcessorTests.cs` (FR-016): one live-drain delivery whose store reports the item claimed by another owner at record time. Assert the process result reports the item as neither delivered nor failed, and that **no exception escapes**. Follow the file's existing fake-store idiom (see the `CompleteClaimAsync` fake at `:804`); xunit `Assert.*` only, no FluentAssertions
- [x] T015 [P] [US1] Add a test asserting a superseded recording leaves the row untouched — status, owner, fence, attempt count and failure message all unchanged (FR-003, C2, C7)
- [x] T016 [P] [US1] Add a test asserting a superseded item remains recoverable: claim expiry still makes it claimable and the sweep still redelivers it (FR-006, C3)

### The revert-to-red gate (FR-019) — H5 applies

- [x] T017 [US1] Revert **only** the T006 store change, keeping every test, and confirm T014 fails. Record the observed failure output. If it passes, the test is worthless — rewrite it before continuing
- [x] T018 [US1] Restore T006 and confirm the tests pass again, so the red was caused by the revert and not by a broken test

**Checkpoint**: US1 is independently shippable. The reported 500 is fixed. **This is the MVP and the unblocking change** — downstream consumers are unblocked here, per D7's scheduling constraint.

---

## Phase 4: User Story 2 — A previously-claimed item can be delivered again (P1)

**Goal**: Close the deterministic fence trigger, which needs no concurrency and fails on first attempt.

**Independent test**: Claim an item, return it to a deliverable state, deliver it claim-lessly, assert success.

> **H3**: do **not** touch the `Delivering` exclusion — it already exists ([research.md R3](./research.md#r3--the-issues-top-ranked-candidate-is-already-implemented)). Only the fencing-token condition is new.

- [x] T019 [US2] Add a fencing-token exclusion to `CandidateSelection.Deliverable` in `src/Elsa/Workflows/Runtime/Persistence/EntityFrameworkCore/Stores/EfRuntimePostCommitOutboxStore.cs:404` so rows with `DeliveryFencingToken > 0` are not offered to the claim-less path (FR-010). **Leave `CandidateSelection.Claimable` at `:407` untouched** — the claim path must still see fenced rows or an expired claim could never be reclaimed
- [x] T020 [US2] Apply the equivalent exclusion to the in-memory store's deliverable predicate in `src/Elsa/Workflows/Runtime/Services/InMemoryRuntimeCheckpointCommitStore.cs`, keeping the two stores' deliverable semantics identical
- [x] T021 [US2] Add a store-level test (FR-017) in `tests/Elsa/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/EfRuntimePostCommitOutboxStoreTests.cs`: claim an item, return it to `Pending`/`FailedRetryable`, assert it is **not** returned by the deliverable query while fenced, and that a claim-less recording of it reports `SupersededByOtherOwner` rather than throwing
- [x] T022 [US2] Revert **only** the T019 change — independently of T006, per H5 — and confirm T021 fails. Record the output. Restore and confirm green

**Checkpoint**: both triggers fixed, each guarded by a test proven red against its own production change.

---

## Phase 5: User Story 3 — A store cannot silently omit ownership reporting (P2)

**Goal**: Remove the dead parameter that invited three separate stores to reimplement the same refusal.

- [x] T023 [US3] Remove `ownerId` from the `RuntimePostCommitOutboxQuery` constructor and the `OwnerId` property in `src/Elsa/Workflows/Runtime/Core/Models/RuntimePostCommitOutbox.cs:701-729`, including its blank-string validation guard (FR-011). **H1 is audited and clear**: no call site passes four positional arguments, so no silent reinterpretation is possible in this repository — but re-run the audit after the edit to confirm nothing was added since
- [x] T024 [P] [US3] Delete the `NotSupportedException` refusal in `src/Elsa/Workflows/Runtime/Persistence/EntityFrameworkCore/Stores/EfRuntimePostCommitOutboxStore.cs:88-89`
- [x] T025 [P] [US3] Delete the identical refusal in `src/Elsa/Workflows/Runtime/Services/InMemoryRuntimeCheckpointCommitStore.cs:315-316`
- [x] T026 [US3] **Test removal — §2.21.1 approval required.** Delete the whole test method `InMemoryRuntimeCheckpointCommitStore_RejectsOwnerFilteredQueriesBecauseClaimingIsOutOfScope` at `tests/Elsa/Workflows/Runtime/Tests/RuntimePostCommitOutboxStoreTests.cs:36-47`. Its subject is gone, not moved. **Do not merge without the architect's approval sentence recorded in the PR body**
- [x] T027 [P] [US3] Delete the two assertion lines at `tests/Elsa/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/EfRuntimePostCommitOutboxStoreTests.cs:87-88`. This is **not** a test removal — the enclosing test keeps its other objectives, so §2.21.1 approval does not apply
- [x] T028 [P] [US3] Delete the blank-`ownerId` guard assertion at `tests/Elsa/Workflows/Runtime/Tests/RuntimeOperationalRecoveryOutboxContractTests.cs:207`, which asserts a guard that no longer exists. Also **not** a test removal

**Checkpoint**: the reimplementation vector is gone. A new store has nothing to refuse.

---

## Phase 6: Evidence and documentation

**Must not delay Phases 3–4** (D7 scheduling constraint). Land separately if it would.

- [x] T029 [P] Add `tests/Elsa/Workflows/Runtime/Persistence/EntityFrameworkCore/ProviderTests/RuntimePostCommitOutboxConcurrencyProviderSmokeTests.cs` (FR-018): two deliverers over the same connection string racing one row on real PostgreSQL. Reuse `RuntimeBookmarksPostgreSqlFixture`, use `[SkippableFact]` + `Skip.IfNot(fixture.IsAvailable, fixture.SkipReason)`, and follow the two-context `Task.WhenAll` idiom already used in `RuntimeWorkflowExecutionProviderSmokeTests.cs:86-95`
- [x] T030 [P] Correct the false single-writer invariant in the `RuntimeLiveDrainDeliveryScope` class documentation at `src/Elsa/Workflows/Runtime/Core/Models/RuntimeLiveDrainDeliveryScope.cs:10` (FR-014). It currently claims no other deliverer competes for the execution's intents
- [x] T031 [P] Extend that documentation to name the resumption sweep as a legitimate competing deliverer, citing that `RuntimeResumptionService.SweepAsync` processes with no execution or intent filter on the claim path (FR-015), so the next reader does not rebuild on the false invariant

---

## Phase 7: Polish and pre-merge

- [x] T032 Confirm no FluentAssertions, Shouldly, Moq or NSubstitute reference was introduced; all new assertions use xunit `Assert.*` (FR-020)
- [x] T033 Run the full runtime test suite and confirm no pre-existing test regressed — §2.21.1's golden rule requires every surviving test to keep passing
- [x] T034 Verify §2.23.2 branch coverage: every new branch in all three store implementations and both processor paths has a test, **including the coalescing decorator's pass-through branch** from T008
- [ ] T035 Write the PR body: the revert-to-red evidence from T017/T018 and T022 (FR-019), **the architect's approval sentence for the T026 test removal (§2.21.1)**, and a breaking-change note warning external callers about the H1 positional shift they do not get compile protection from
- [ ] T036 Record in the PR that this fix is expected to reduce duplicate-key log noise as a side effect, stating the confirming correlation check and explicitly **not** claiming the diagnosis (FR-021)
- [ ] T037 Confirm FR-022 holds: no claim-completion concurrency guard was added in this unit. That is D7, filed separately
- [ ] T038 File the D7 follow-up issue for the unguarded `CompleteClaimAsync` concurrency path at `src/Elsa/Workflows/Runtime/Persistence/EntityFrameworkCore/Stores/EfRuntimePostCommitOutboxStore.cs:237`, noting that `ClaimAsync:132` has the guard it lacks

---

## Dependencies

```
Phase 1 (Setup)
   └─> Phase 2 (Foundational: T003–T005)  ← BLOCKS EVERYTHING
          ├─> Phase 3 (US1, P1)  ← MVP, unblocks downstream consumers
          │      └─> Phase 4 (US2, P1)   [shares the stores US1 modifies]
          │             └─> Phase 5 (US3, P2)
          └─> Phase 6 (evidence/docs)  [independent; must not delay 3–4]
                 └─> Phase 7 (pre-merge)
```

**Story independence**: US1 ships alone and fixes the reported 500. US2 touches the same store files as US1, so it follows rather than parallels. US3 is independent of both in principle but sequenced last because it is P2 and its test deletions are easier to review once behaviour is settled.

**Parallel opportunities**: T007/T008 (different store files); T015/T016; T024/T025; T027/T028; T029/T030/T031.

---

## Implementation Strategy

**MVP = Phase 1 + Phase 2 + Phase 3.** That is the unblocking change: the 500 stops, downstream consumers move. Ship it without waiting for the rest if that helps them sooner — D7 makes this explicitly the priority.

**Then Phase 4**, which closes a deterministic failure that will otherwise surface on its own schedule.

**Then Phase 5**, which is the durability of the fix rather than the fix — without it, the next store reimplements the defect, exactly as three stores already have.

**Phase 6 never blocks 3–4.**

### Two things that will waste your time if ignored

**The compiler will not help you with the enum (H2).** No `switch`, warnings-only build. T012 is a manual audit and the `:207` site is genuinely wrong if you skip it.

**Revert the two production changes independently (H5).** T006 and T019 fix different bugs. Reverting both at once tells you only that *something* is guarded, which is precisely the false confidence that let four previous fixes ship with tests that never could have failed.

---

## Task count

**38 tasks.** US1: 13 (T006–T018) · US2: 4 (T019–T022) · US3: 6 (T023–T028) · Setup/Foundational: 5 · Evidence/docs: 3 · Polish: 7.

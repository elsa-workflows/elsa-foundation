# Implementation Plan: Tolerate Concurrent Post-Commit Outbox Delivery Contention

**Branch**: `1305-outbox-delivery-contention` | **Date**: 2026-09-17 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/169-outbox-delivery-contention/spec.md`

## Summary

The claim-less ("live drain") post-commit delivery path records its result through a contract that cannot express "another deliverer owns this item". When the background resumption sweep claims a row between the live drain's read and its record, the store rejects the recording, the exception escapes the drain orchestrator, and the workflow start answers HTTP 500. A second, deterministic trigger reaches the same rejection with no concurrency at all: the fencing token never resets, so any item that was *ever* claimed is offered back to the claim-less path while still carrying a fence.

The fix gives the delivery-record contract an outcome to return, adds a `SupersededByOtherOwner` value to the outcome enum this contract family already uses, excludes fenced rows from the claim-less candidate set, and deletes the dead ownership-filter parameter that has now caused three separate stores to reimplement the same refusal.

**Scheduling constraint (D7)**: downstream consumers are blocked by this 500. Phase 1 and Phase 2 below are the unblocking change and ship first; nothing else sits on their critical path.

## Technical Context

**Language/Version**: C# / .NET (repository-pinned; no version change)

**Primary Dependencies**: Entity Framework Core, Npgsql (provider tests only), xunit 2.9.3, Xunit.SkippableFact, Testcontainers (provider tests only)

**Storage**: Runtime operational state via EF Core. Durable store `EfRuntimePostCommitOutboxStore` over `RuntimeDbContext`; in-memory store `InMemoryRuntimeCheckpointCommitStore`; coalescing overlay decorator over either. **No schema change** — the fix reads existing columns (`Status`, `DeliveryFencingToken`) and adds no persisted state.

**Testing**: xunit only, `Assert.*` idiom. FluentAssertions and other fluent assertion libraries are constitutionally excluded and must not be introduced.

**Target Platform**: Server-side runtime host. Defect measured on PostgreSQL 17, image `sha-71852a1`.

**Project Type**: Modular library within the Elsa foundation monorepo — `Elsa.Workflows.Runtime` domain, `.Core` contracts plus EF persistence module.

**Performance Goals**: No regression on the live-drain fast path. The whole reason the claim-less branch exists is to skip a durable claim round-trip; the fix must not reintroduce one. The added work is a comparison against already-loaded row state.

**Constraints**: No database migration. No new NuGet dependency. No change to crash-recovery semantics — a superseded item must remain redeliverable by claim expiry and the sweep.

**Scale/Scope**: 3 store implementations, 1 contract, 1 enum, 1 query model, 1 processor, 1 documentation comment. Roughly 10 production files and 4 test files.

## Constitution Check

*Gates evaluated against `.specify/memory/constitution.md` v4.1.0 and `constitution-framework.md` v4.0.0.*

| Gate | § | Status | Notes |
|---|---|---|---|
| **CQS at the persistence boundary** | §2.10 / pattern 13 | **PASS** | §2.10 permits a command to return "a confirmation token". `SupersededByOtherOwner` *is* a confirmation token — it reports which mutation was persisted. It is **not** a queryable view of the mutated data, which is what §2.10 forbids. Precedent in the same family: `CompleteClaimAsync` already returns `RuntimePostCommitOutboxClaimCompletionOutcome`. |
| **Sanctioned patterns — closed catalog** | §2.24.2 | **PASS** | No new pattern. The change applies the outcome-returning shape already used by `IRuntimePostCommitOutboxClaimCompletionStore` to the one contract in the family that lacks it. §2.24.3's ratification gate is therefore not triggered. |
| **Replacement contracts** | §2.6.2 / pattern 5 | **PASS** | Store ownership is unchanged. `RuntimePostCommitOutboxStoreBackend` continues to assert exclusive ownership of the contract family; no registration shape changes. |
| **Per-implementation unit tests, branch-covered** | §2.23.2 | **GATE — must be satisfied** | The new outcome adds a branch to all three implementations and to the processor. **Every** branch needs a test, including the pass-through branch of the coalescing decorator. |
| **Feature-class registration test** | §2.23.1 | **N/A** | No feature class is added or changed. Existing registration tests must continue to pass. |
| **Exception boundaries** | §2.23.5 | **PASS, and improved** | This change *removes* an infrastructure-shaped `InvalidOperationException` from a normal control-flow path and replaces it with a domain-vocabulary outcome. No infrastructure exception is newly swallowed. |
| **Golden rule of refactoring** | §2.21.1 | **GATE — architect approval required** | FR-012 removes a test that asserts a refusal being deleted. §2.21.1: *"removing a test requires explicit recorded approval from at least one architect."* See *Complexity Tracking*. |
| **Integration testing** | §2.23.6 | **PASS (permitted, not prescribed)** | §2.23.6 places integration testing outside the constitution's scope — it neither requires nor forbids it. The container-backed provider test (FR-018) is permitted, reuses an existing fixture and CI lane, and is **not** a substitute for the §2.23.2 unit obligations. |
| **Duplication beats dependency** | §2.17 | **PASS** | The supersession check is a few lines per store against already-loaded row state. No shared helper is introduced — below the three-consumer threshold, and a helper would create a dependency between store modules. |
| **Consolidation / subtractive obligation** | §2.25 | **PASS, actively satisfied** | The unit deletes a dead query parameter and two refusal implementations. Subtraction is verified by compilation, not inferred. |

**Result**: no unjustified violations. Two gates carry obligations, both tracked below.

## Project Structure

### Documentation (this feature)

```text
specs/169-outbox-delivery-contention/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── outbox-delivery-recording.md
├── checklists/
│   └── requirements.md
└── tasks.md             # Created by /speckit.tasks — NOT by this command
```

### Source Code (repository root)

```text
src/Elsa/Workflows/Runtime/
├── Core/
│   ├── Contracts/
│   │   └── IRuntimePostCommitOutboxStore.cs          # FR-007: recording returns an outcome
│   └── Models/
│       ├── RuntimePostCommitOutbox.cs                # FR-001: new outcome value; FR-011: remove OwnerId
│       └── RuntimeLiveDrainDeliveryScope.cs          # FR-014/015: correct the false invariant
├── Services/
│   ├── RuntimePostCommitOutboxProcessor.cs           # FR-004/005: classify superseded items
│   ├── InMemoryRuntimeCheckpointCommitStore.cs       # FR-009: report outcome; remove refusal
│   └── Coalescing/
│       └── CoalescingRuntimePostCommitOutboxStore.cs # FR-009: propagate inner outcome
└── Persistence/EntityFrameworkCore/Stores/
    └── EfRuntimePostCommitOutboxStore.cs             # FR-001/010/011: replace throw; exclude fenced rows

tests/Elsa/Workflows/Runtime/
├── Tests/
│   └── RuntimePostCommitOutboxProcessorTests.cs      # FR-016: injected-steal test (PRIMARY GATE)
└── Persistence/EntityFrameworkCore/
    ├── Tests/
    │   └── EfRuntimePostCommitOutboxStoreTests.cs    # FR-017 fence test; FR-012 remove OwnerId test
    └── ProviderTests/
        └── RuntimePostCommitOutboxConcurrencyProviderSmokeTests.cs  # FR-018: NEW, real PostgreSQL
```

**Structure Decision**: Existing structure; no new project. Contracts and models live in `Runtime/Core` per the three-layer separation (§2.1); the EF store stays in its persistence module. One new test file in the existing `ProviderTests` project, which already carries the Testcontainers PostgreSQL fixture and is already wired into the CI matrix.

## Implementation Phases

Ordered by the D7 scheduling constraint: the unblocking fix first.

**Phase 1 — Contract and outcome** (unblocking, on the critical path)
Add the outcome value; change the recording contract to return it; update all three implementations; replace the EF throw with the superseded outcome; thread the outcome through the processor so a superseded item counts as neither delivered nor failed.

**Phase 2 — Gating tests** (unblocking, on the critical path)
The processor-level injected-steal test (FR-016) and the store-level fence test (FR-017), each proven red on revert (FR-019).

**Phase 3 — The deterministic trigger and the dead parameter**
Exclude fenced rows from the claim-less candidate selection; delete `OwnerId` and both refusals; remove the test asserting the deleted refusal (architect approval recorded below).

**Phase 4 — Evidence and documentation** (must not delay Phases 1–2)
The container-backed provider concurrency test (FR-018); the corrected live-drain scope documentation (FR-014/015).

## Complexity Tracking

| Item | Gate | Why needed | Resolution |
|---|---|---|---|
| **Breaking change to a public contract** | §2.24.3 not triggered; D3 | An optional additive interface can be omitted by a store, and an omitting store reproduces this exact defect — which is how it survived a store migration. A required signature change makes every implementer confront it at compile time. | Accepted by the architect as decision **D3**. Rejected alternative (fifth additive capability interface) recorded in [spec.md](./spec.md#settled-decisions). |
| **Removal of a test method** — `RuntimePostCommitOutboxStoreTests.InMemoryRuntimeCheckpointCommitStore_RejectsOwnerFilteredQueriesBecauseClaimingIsOutOfScope` (`:36-47`) | **§2.21.1**: test removal requires explicit recorded approval from at least one architect | The test's *subject* (the `OwnerId` query filter) is deleted by FR-011, so its objective becomes inapplicable rather than merely relocated. §2.21.1's own diagnostic — *"has the subject moved?"* — resolves to "the subject is gone". | **APPROVAL REQUIRED AND NOT YET RECORDED.** Joey Barten is an architect and settled FR-011 as decision **D4**, but §2.21.1 requires the *removal* to be approved explicitly and recorded in the PR description. **Action: record the approval sentence in the PR body before merge.** This is the one gate obligation that cannot be discharged by code. |
| **Two further `ownerId` assertion sites** — `EfRuntimePostCommitOutboxStoreTests.cs:87-88` and `RuntimeOperationalRecoveryOutboxContractTests.cs:207` | §2.21.1 | Assertion lines inside tests whose other objectives survive. | **Not test removals** — the enclosing tests keep their subject and objective, so §2.21.1 approval is not required for these two. Recorded here so the distinction is deliberate rather than assumed. |
| **Enum extension with no compile-time signal** | §2.23.2 branch coverage | No `switch` over the outcome enum exists (all consumers use `==`), and `Directory.Build.props` sets warnings-only with no `TreatWarningsAsErrors`. Adding a value therefore produces **no** build-time signal; every `==` site silently takes its `else`. | Handled as a dedicated audit task with its own test, not folded into implementation. The highest-risk site is `RuntimePostCommitOutboxProcessor.cs:207`, where a superseded item would otherwise be logged with the failure status. |
| **Container-backed provider test** | §2.23.6 (integration testing out of constitutional scope) | The race cannot be exercised on SQLite, whose serialized writes mask it. | Permitted, not prescribed. Reuses the existing fixture and CI lane. Does **not** substitute for §2.23.2 unit coverage, which is satisfied independently in Phases 1–3. |

# Tasks: Coalesced Command Scope

Approved implementation plan for T19/#2450; root and independent review accepted the design and before-fix tests. One correction branch; no concurrent writer on these files. Spec/plan, dependency evidence and source audit are available. No new project/package/schema is needed.

## Phase 1: Setup

- [x] T001 Refresh issue #2450 comments/referencing PRs, failed-base source and local preferences; record the current scope claim and source pins in specs/194-coalesced-command-scope/evidence/ownership.md.
- [x] T002 Review spec/plan/contracts and both constitutions; record independent acceptance before runtime implementation in specs/194-coalesced-command-scope/evidence/design-review.md.

## Phase 2: Foundational Contract Proof

- [x] T003 [US1] Add shared real-EF composition controls for two scopes with ValidateScopes true/false in tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/ProviderTests/RuntimeCoalescingScopeRegistrationTests.cs; avoid database queries, Docker fixtures and reflection.
- [x] T004 [US1] Execute only the new controls against unchanged runtime registration, retaining exact command/TRX/source/test diff and a lifetime-contract failure in specs/194-coalesced-command-scope/evidence/before-fix.md.

## Phase 3: User Story 1 — Independent Command Scopes

**Goal**: Align the default factory with existing command-scoped persistence collaborators.

**Independent test**: Under both validation modes, resolve consistent scoped identities inside each scope and distinct factory/committer/inner-store/context identities across scopes.

- [x] T005 [US1] Replace only the default factory TryAddSingleton with TryAddScoped in src/essentials/Workflows/Runtime/Api/Coalescing/CoalescingRuntimeCheckpointPersistenceExtensions.cs, preserving registration order and custom-factory behavior.
- [x] T006 [US1] Run corrected controls and a temporary registration-revert mutation, restore correct source and retain green/red evidence in specs/194-coalesced-command-scope/evidence/scope-contract.md.

## Phase 4: User Story 2 — Preserve Runtime Behavior and Customization

**Goal**: Keep existing durability, responses and extension behavior.

**Independent test**: Existing behavior suite, repeated/custom registration controls, plus rebuilt valid HttpEndpoint and REST workflows each with expected output, Completed and no incidents.

- [x] T007 [US2] Add only missing idempotency/custom-factory controls using shared setup in tests/essentials/Workflows/Runtime/Tests/RuntimeCheckpointCoalescingTests.cs; preserve every existing assertion.
- [ ] T008 [US2] Run affected runtime/provider correctness suites, relevant rebuilt backend HTTP e2e and the existing primary/REST controls; retain exact source/DLL/fixture/settings/outcomes/cleanup in specs/194-coalesced-command-scope/evidence/integrated-correctness.md, carrying final concurrent acceptance ownership to T17/T18 without claiming prior error attribution.

## Phase 5: Integration and Delivery

- [ ] T009 Review complete diff, run tests/essentials/Architecture/Elsa.Architecture.Tests.csproj and generated Maps freshness check, refresh genuinely changed snapshots including docs/maps/manifest.json, and record checks/review/PR/publication-hold transitions in specs/194-coalesced-command-scope/evidence/delivery.md.
- [ ] T010 Reconcile the T19 entry and T17 native dependency in docs/plans/runtime-db-access-program.md and docs/program-goals/runtime-db-access.md with the canonical accounting lane; locally commit/push a coherent draft PR after competing-work recheck, retaining exact-head review and resulting-main gates before issue acceptance.

## Dependencies and Parallel Opportunities

T001 → T002 → T003 → T004 → T005 → T006; T006 and T007 both precede T008 → T009 → T010. T007 may be prepared after T002 in its separate existing test file, but root keeps one runtime writer and all builds/hosts serial. No [P] task is assigned in this small correction. US2 verification depends on corrected US1 wiring; its custom/idempotency controls can be reviewed independently.

## Implementation Strategy

First prove the before-fix lifetime contract, then make the minimal registration correction and mutation-sensitive proof. Preserve current runtime behavior through focused affected checks and normal host controls. Reconcile canonical evidence through the root integration lane. No PR merge occurs while the publication-boundary question remains unanswered; package publication/deployment remain outside scope. A green diagnostic run or unavailable review never substitutes for the recorded main gate or required exact-head review.

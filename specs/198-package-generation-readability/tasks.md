# Tasks: Protect Package Generations

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md), and [quickstart.md](quickstart.md).

**Ownership**: Existing Foundation #2164 under Program #2500. One implementation writer; root reviews, integrates, and verifies. These are execution steps within the existing task, not additional GitHub issues.

**Test discipline**: This is a correction/refactor of an existing feature. Preserve all existing test subjects/objectives and add registration and implementation coverage under framework §§2.21.1/2.23. No test deletion is approved. Regression and mutation evidence establish causality; no greenfield TDD policy is inferred. Heavy commands run serially through the shared build-slot wrapper.

## Phase 1: Setup

- [ ] T001 Inventory existing assertions and confirm no competing #2164 claim/PR before edits; record the retained test objectives in `specs/198-package-generation-readability/quickstart.md`.
- [ ] T002 Prepare an isolated private copy for public `.166`/`.99` preflight, with that copy's own `Directory.Packages.props` and `packages.lock.json` files; record reproducible copy/restore commands in `specs/198-package-generation-readability/quickstart.md`. Do not change the committed Foundation pins/locks.

## Phase 2: Foundational Wiring

- [ ] T003 Add the root-only public sealed adapter in `src/essentials/Cluster/Readability/NuplanePackageGenerationBuildParticipant.cs` and update `src/essentials/Cluster/Readability/EfSchemaReadabilityServiceCollectionExtensions.cs`: one canonical root adapter, participant alias, lifecycle factory that resolves it before binding, shared nondisposable/nonparticipant source, idempotent shutdown, no initializer-based release proxy.
- [ ] T004 Verify registration identity, custom-source override, duplicate composition, child-provider source identity, participant exclusion, and root shutdown ownership in `tests/essentials/Cluster/Readability/Tests/NuplanePackageGenerationBuildParticipantTests.cs` and the existing `SupersededPackageGenerationShellTests.cs`.

## Phase 3: User Story 1 — Protect Exact Selected Generations (P1)

**Goal**: Count a candidate before catalog reads, then count its exact selected features until confirmed provider teardown.

**Independent test**: Hold an old-selected candidate before its first initializer, commit a newer catalog, and drain all other old generations. The old declaration must remain counted until that candidate's provider has finished disposing.

- [ ] T005 [US1] Implement unresolved Begin pins, atomic exact-feature selection, and point-in-time live-pin copying after awaited catalog reads in `src/essentials/Cluster/Readability/NuplanePackageGenerations.cs`; never await or call user code under the tracking gate.
- [ ] T006 [US1] Add deterministic real-registry pre-read and old-selected-snapshot barriers in `tests/essentials/Cluster/Readability/Tests/SupersededPackageGenerationShellTests.cs`, including disabled features and sibling-context protection without pinning unrelated scanned assemblies.
- [ ] T007 [US1] Cover direct lease selection, cancellation, duplicate release, unknown feature evidence, short-gate races, and repeated pre-provider unwind/descriptor cleanup with stub dependencies in `tests/essentials/Cluster/Readability/Tests/NuplanePackageGenerationBuildParticipantTests.cs`.

## Phase 4: User Story 2 — Preserve Failure and Overlap Safety (P1)

**Goal**: Give each candidate its own pin and release authority; incomplete teardown never releases it.

**Independent test**: Failed initialization, overlapping old/new candidates, slow provider teardown, and throwing disposal retain or release only the affected generation at the confirmed boundary.

- [ ] T008 [US2] Retain the public lifecycle facade's conservative fallback only for shells never owned by a build lease; prevent late callbacks from recreating released pins in `src/essentials/Cluster/Readability/NuplanePackageGenerations.cs`.
- [ ] T009 [US2] Adapt manual providers to a test-owned provider/lease wrapper and explicit fake-drain confirmation in `tests/essentials/Cluster/Readability/Tests/SupersededPackageGenerationTests.cs`; preserve current assertions/objectives, including the unobserved-shell fallback and failed-drain cases.
- [ ] T010 [US2] Add actual-CShells failure-before-provider, failed-initializer cleanup, overlapping generations, blocked full-provider disposal, incomplete teardown retention, and late lifecycle callback regressions in `tests/essentials/Cluster/Readability/Tests/SupersededPackageGenerationShellTests.cs`.

## Phase 5: User Story 3 — Publish Changed Readability Evidence (P1)

**Goal**: Use committed notifications for the stock catalog, preserve custom-catalog fallback, and report retirement-set growth and shrinkage.

**Independent test**: Retire an exact assembly, reintroduce it, and commit unchanged evidence. Changed constraints publish in both directions; unchanged evidence is quiet; root shutdown detaches subscriptions and stops the fallback watch.

- [ ] T011 [US3] Implement subscribe-before-read reconciliation, enqueue-only commit handlers, custom-catalog watcher fallback, and owned shutdown in `src/essentials/Cluster/Readability/NuplanePackageGenerationBuildParticipant.cs` and `NuplanePackageGenerations.cs`.
- [ ] T012 [US3] Queue reevaluation on Begin/selection/release/commit and compare complete retired sets in `src/essentials/Cluster/Readability/NuplanePackageGenerations.cs`; preserve asynchronous coalescing and failure isolation without claiming synchronous report persistence before initialization.
- [ ] T013 [US3] Add commit, late subscription, rollback/reintroduction, unchanged-set quietness, custom-catalog fallback, and stop/unsubscribe tests in `tests/essentials/Cluster/Readability/Tests/NuplanePackageGenerationBuildParticipantTests.cs` and `SupersededPackageGenerationTests.cs`; establish deterministic fixture baselines for existing exact publication-count assertions.

## Phase 6: Integration, Review, and Final Qualification

- [ ] T014 Run the affected readability project against actual public preview packages in the private copy, independently review the full candidate diff, and execute reversible bypass-Begin/current-snapshot/early-release/growth-only mutations; record exact commands/results and provenance in `specs/198-package-generation-readability/quickstart.md`.
- [ ] T015 Review duplication and cleanup, preserve the abstractions-only/EF-free closure, reconcile program execution/evidence in `docs/program-goals/modular-hosting-upstream-delivery.md` and `docs/plans/modular-hosting-upstream/evidence.md`, and commit the prepared source without claiming unchanged-default-pin CI or final acceptance.
- [ ] T016 After upstream stable publication, coordinate final pins/locks and actual Foundation-host readability qualification with #2509, run affected suites/e2e, architecture and map gates, independent exact-head review and resulting-main checks; record acceptance in `specs/198-package-generation-readability/quickstart.md` before merge/Done. Do not wait for coordinating Features #145/#2509 to close before performing the consumer proof that supplies their acceptance.

## Dependencies and Execution

Setup → wiring → US1 → US2 → US3 → integration/private qualification → stable qualification. The single writer serializes changes to the shared tracker and test fixtures. US1 is the first useful behavioral checkpoint; all three stories remain required for delivery.

The readiness/readability work does not depend on Nuplane's pending shared-root pruning policy. Stable final qualification does depend on the authorized upstream release sequence. Delivered upstream source tasks/checks and exact public-preview Foundation consumer proof establish upstream release readiness; coordinating Feature #145 closure/M1 final acceptance do not gate the stable publication required by their own downstream proof. The independent pruning release gate is preserved. T016 stays unchecked until stable actual-host evidence exists; preview success cannot close #2164 or CShells Feature #145.

## Parallel Opportunities

No implementation task carries `[P]`: the tracker, adapter, and fixtures share state and require one integration lane. Read-only design/test review can run alongside the writer; mutation runs and other heavy tests stay serial. A source review of T005–T010 can proceed while the writer prepares T011–T013, but only the complete final candidate receives integration acceptance.

# Tasks: Source-owned composite activities with dependencies

**Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md)

Implementation starts after the coordinator approves the spec (D-1, D-2).

## Phase 1: Commit contract and EF storage (US1)

- [x] T001 [US1] Add `DirectDependencies` (default empty) to `SourceActivityPublicationCommit<,>` in `essentials/src/Activities/Design/Persistence/Core/Contracts/ReusableActivityCommands.cs`.
- [x] T002 [US1] In `EfActivityPublicationDesignCommit`, refuse existing edge ids in the source admissibility check, add the edges in source staging, and stage the dependency projection with owner `ActivityVersion`; share the edge-to-item mapping with the draft path.
- [x] T003 [US1] In `EfSourceActivityPublicationCommand.Validate`, refuse edges whose tenant or owner version does not match the publication.
- [x] T004 [P] [US1] EF tests: a source commit with edges stores them and the projection lists the owner; an existing edge id is refused before any write; mismatched edge tenant is refused.

## Phase 2: Publisher (US1, US3)

- [x] T005 [US1] In `SourceOwnedActivityVersionPublisher`, remove the `activity.source.dependencies-unsupported` refusal, build edges from `Compilation.DirectDependencies` with stable ids, and pass them to the commit; update the class and method docs.
- [x] T006 [P] [US1] Publisher tests: the former refusal test now publishes with one edge per dependency; edge ids are equal across two publishers; the CLR path passes no edges.

## Phase 3: Reconciler rounds (US2, US3)

- [x] T007 [US2] In `ActivityVersionReconciler`, with a source publisher, retry refused versions in rounds while a round publishes; record and log only final refusals.
- [x] T008 [P] [US2] Reconciler tests: a version that publishes in round 2 has no recorded refusal and no warning; a permanently refused version is recorded once with its last diagnostics; a refusal from an earlier pass is cleared when this pass publishes it; without a publisher nothing changes.
- [x] T009 [US2] [US3] EF pass test with the graph provider: T uses S uses CLR activities, T listed first, all publish with edges in one pass; unknown dependency refused with `activity.dependency.version-not-found` and nothing stored; a two-composite cycle refused and the pass ends.

## Phase 4: Docs and finish

- [x] T010 Update `essentials/src/Activities/Design/Reconciliation/Json/README.md` (FR-009).
- [ ] T011 Run the scoped tests (plan, Test scope) and the gate checker; set spec status to Implemented.

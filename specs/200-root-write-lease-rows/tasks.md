# Tasks: Root-Write Lease Coordination Without a Hot Row

**Input**: Design documents from `specs/200-root-write-lease-rows/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/root-write-lease-coordination.md, quickstart.md

**Tests**:
- This is a refactor of existing implementations, so framework §2.21.1 / §2.23.4 apply: every existing lease, guard, deletion and GC test must keep passing with its subject and objective unchanged.
- New logic-bearing code gets §2.23.2 tests.
- The Elsa derived constitution declares no test-first cadence. This plan uses red-first ordering for the regression and behavior tests anyway (plan, Validation Design step 1), because these are defect fixes.
- "Red" means the test is shown failing on the base code before its implementation task lands. Where the base code cannot fail a test (pure non-regression guards), the task says so.

**Local verification**:
- Build and test as described in the PR #2539 reproduction comment: local feed, `/tmp/claude-0/build/NuGet.local.config`, and `dotnet` run from outside the repo.
- Revert any `packages.lock.json` that the restore rewrites.
- Never commit lock-file or feed changes.

## Format: `[ID] [P?] [Story] Description`

## Phase 1: Setup

- [x] T001 Restore the local verification environment: start the disposable PostgreSQL 16 cluster on port 55432, restore the two Runtime EF test projects against the local feed, and confirm the 4 existing regression tests still fail at `HEAD`. Projects: `tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj` and `.../ProviderTests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests.csproj`.
- [x] T002 Inventory existing tests that read or write the coordination row's `Leases` JSON directly, or that otherwise depend on the single-row layout. Search `EfRuntimeArtifactScopeTests.cs`, `RuntimeArtifactsProviderSmokeTests.cs`, `WorkflowActivationCrashRepairContract.cs` and the GC concurrency tests under `tests/essentials/Workflows/Runtime/`. For each one, record its subject and objective, and whether only setup or wiring must change (§2.21.1). Put the list in the tasks.md Notes section below.

## Phase 2: Foundational (blocks all stories)

- [x] T003 Add `WorkflowExecutableRootWriteLeaseEntity` to `src/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Entities/RuntimeArtifactEntities.cs`, with the fields from data-model.md.
- [x] T004 Configure the entity in `src/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Configuration/RuntimeArtifactEntityConfigurations.cs`: primary key `Id`, `Revision` as concurrency token, a non-unique index on (`ScopeKeyHash`, `ArtifactIdHash`, `ArtifactId`, `ExpiresAtUtcTicks`), and column lengths matching the coordination entity.
- [x] T005 Register the table name in `src/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/RuntimeArtifactEfModule.cs`. Add the `DbSet` on `RuntimeDbContext`. Add the entity to the `RuntimeArtifact` `EfSchemaFamily` in `.../AssemblyInfo.cs`, and its text columns to the list in `.../RuntimeProviderContexts.cs`.
- [x] T006 Generate the incremental migration `WorkflowExecutableRootWriteLeases` for Sqlite, PostgreSql, SqlServer and MySql under `src/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Migrations/Runtime/<Provider>/`, using the `tools/ef/Elsa.EntityFrameworkCore.Tooling` project and the provider-call stripping the existing incremental migrations use. Update each provider's model snapshot. The migration must be expand-only (one `CreateTable` plus one index).
- [x] T007 Run the EF migrations suite (`tests/essentials/Persistence/EntityFrameworkCore/Migrations`, SQLite locally) and `bash tools/ef/module-migrate.sh script-check db/migrations`. Fix any model/migration mismatch.
- [x] T008 Add the isolated lease-context helper in `src/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Stores/EfWorkflowExecutableStore.cs` (research R4, as implemented):
  - `CreateLeaseContext()` builds a sibling of the injected context from its own `IDbContextOptions` through the context type's options constructor, and each lease operation disposes it when done;
  - the sibling carries the same provider, interceptors and schema write gate, with its own change tracker and, in a host, its own pooled connection;
  - no `IServiceScopeFactory`, so the store's constructor, registration and test fixtures are unchanged;
  - guard and non-lease operations keep the injected context.

**Checkpoint**: the model, migrations and context helper compile, and the existing tests are unchanged and green except the 4 known regressions.

## Phase 3: User Story 1 — many executions of one workflow run concurrently (P1) 🎯 MVP

**Goal**: A holder's acquire, renew and release touch only its own lease record (FR-001, FR-002, FR-006, FR-007).

**Independent Test**: The 3 SQLite regression tests and the PostgreSQL parallel test pass. The SQL Server and MySQL variants pass in CI.

- [x] T009 [P] [US1] Add `SqlServer_concurrent_root_write_lease_holders_of_one_artifact_all_succeed` and `MySql_concurrent_root_write_lease_holders_of_one_artifact_all_succeed` to `tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/ProviderTests/RuntimeArtifactsProviderSmokeTests.cs`, reusing `RunConcurrentRootWriteLeaseHoldersAsync`. Red: both fail in CI on the base code.
- [x] T010 [US1] Reimplement `TryAcquireRootWriteLeaseAsync` in `EfWorkflowExecutableStore.cs`, on the isolated context, following research R3:
  - read the pair for incarnation and guard;
  - return the existing token for a live same-id row of the same incarnation;
  - insert a new row under `UniqueKey` retry, reloading on a conflict;
  - overwrite an expired or other-incarnation row by `Revision` compare-and-swap;
  - never write the coordination row.
- [x] T011 [US1] Reimplement `RenewRootWriteLeaseAsync` as one `ExecuteUpdate`, filtered on id, token, `ExpiresAtUtcTicks > now` and incarnation, returning `true` exactly when one row was affected. Reimplement `ReleaseRootWriteLeaseAsync` as one `ExecuteDelete`, filtered on id and token. Both live in `EfWorkflowExecutableStore.cs` and run on the isolated context. Keep provider-failure normalization (§2.23.5).
- [x] T012 [US1] Run `EfRuntimeArtifactScopeTests` and the PostgreSQL provider test locally. The 3 regression tests plus `PostgreSql_concurrent_root_write_lease_holders_of_one_artifact_all_succeed` must pass. All other existing tests must pass, with only the wiring changes recorded in T002.
- [x] T013 [P] [US1] Add `Root_write_lease_operations_do_not_save_or_discard_changes_staged_on_the_callers_context` to `EfRuntimeArtifactScopeTests.cs` (research R4). Stage an unrelated tracked change on the fixture context, then run acquire, renew and release. The staged change must still be pending and unsaved afterwards. Red: fails on the base code if today's `ChangeTracker.Clear()` discards it; otherwise it is a non-regression guard (record which).

**Checkpoint**: US1 is independently demonstrable.

## Phase 4: User Story 2 — unused versions are still collected safely (P1)

**Goal**: Guard/lease mutual exclusion under write-then-check, incarnation fencing, legacy leases honoured, and per-attempt checkpoint lease identity (FR-003, FR-004, FR-005, FR-010, FR-015).

**Independent Test**: The race, legacy, fencing and #2286 tests pass in memory and on EF SQLite. All existing GC tests pass.

- [x] T014 [P] [US2] Add race tests to `EfRuntimeArtifactScopeTests.cs`, using interceptors that commit the other side at the exact point:
  - (a) `Acquire_withdraws_and_returns_null_when_a_guard_commits_between_its_lease_commit_and_check`
  - (b) `Begin_deletion_cancels_and_returns_null_when_a_lease_commits_between_its_guard_commit_and_count`
  - (c) `Guarded_delete_wins_over_an_acquirer_between_its_lease_commit_and_check`

  Each test asserts that no live lease and live guard coexist, and that a refused acquirer leaves no live lease row. These pass trivially on the base code (single row). Bite-proof them in T020.
- [x] T015 [P] [US2] Add `Legacy_shared_row_leases_block_deletion_until_they_expire` to `EfRuntimeArtifactScopeTests.cs`. Seed an unexpired lease into the coordination `ContentJson` `Leases` dictionary. `TryBeginDeletionAsync` and guarded `DeleteAsync` must refuse until `now` passes its expiry, then succeed.
- [x] T016 [P] [US2] Add `Leases_from_a_deleted_incarnation_are_not_honoured_after_recreate` to `EfRuntimeArtifactScopeTests.cs`.
- [x] T017 [P] [US2] Add the #2286 contract test `Overlapping_attempts_of_one_commit_hold_independent_leases`. Put it in `tests/essentials/Workflows/Runtime/Tests/RuntimeCheckpointCommitTests.cs` for the in-memory store, and in an EF counterpart in `tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/`. When the first attempt releases, the second attempt's closure stays protected and its renewal succeeds. Red: fails on the base code.
- [x] T018 [US2] Implement the guard side in `EfWorkflowExecutableStore.cs` (research R2, R5, R6):
  - `TryBeginDeletionAsync` commits the guard by compare-and-swap on the coordination row, dropping expired legacy leases. It then counts live lease rows of the current incarnation plus live legacy leases. If any exist, it cancels the guard and returns `null`.
  - Guarded `DeleteAsync` re-verifies guard and live leases (rows plus legacy) inside its transaction, and deletes the executable, coordination and all lease rows of the artifact.
  - The unguarded `DeleteAsync(artifactId)` also deletes lease rows.
  - Both guard paths purge expired lease rows for the artifact in bounded batches.
- [x] T019 [US2] Add the acquire post-commit check in `EfWorkflowExecutableStore.cs`. After the lease row commits, read the pair in a fresh statement on the isolated context. If a live guard exists, or the artifact is missing or has another incarnation, delete the row by token and return `null`.
- [x] T020 [US2] Bite-proof the race tests from T014 (temporary mutations, never committed):
  - remove the T019 check: (a) must fail;
  - remove the T018 count: (b) must fail;
  - remove the guarded-delete re-verify: (c) must fail.

  Restore the code and record the results in the PR evidence comment.
- [x] T021 [US2] Use `checkpoint:{CommitId}:{nonce}` (fresh `Guid.NewGuid():N` per attempt) in `src/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Stores/EfRuntimeCheckpointCommitStore.cs` and in `src/essentials/Workflows/Runtime/Services/Checkpoints/InMemoryRuntimeCheckpointCommitStore.cs`. Leave replay resolution before lease acquisition unchanged. T017 must pass.
- [x] T022 [US2] Run all existing GC, lease, guard and deletion tests (Runtime in-memory and EF SQLite), plus T014–T017. Confirm in-memory parity (FR-008): the in-memory store passes the same contract cases, with no semantic change expected in `InMemoryWorkflowExecutableStore.cs`.

**Checkpoint**: US1 and US2 together satisfy SC-002 to SC-004 locally.

## Phase 5: User Story 3 — a durable checkpoint is never reported as failed because of lease cleanup (P2)

**Goal**: FR-011.

**Independent Test**: The manager tests below pass.

- [x] T023 [P] [US3] Add tests to `tests/essentials/Workflows/Runtime/Tests/WorkflowExecutableRootWriteLeaseManagerTests.cs`:
  - `Successful_write_is_reported_as_success_when_release_fails`, with a Warning logged per failed release;
  - `Write_failure_surfaces_unchanged_when_release_also_fails`;
  - `Lost_lease_during_write_still_cancels_and_surfaces`, which is non-regression.

  Red: the first two fail on the base code.
- [x] T024 [US3] In `src/essentials/Workflows/Runtime/Services/Executables/WorkflowExecutableRootWriteLeaseManager.cs`:
  - add an optional `ILogger<WorkflowExecutableRootWriteLeaseManager>` constructor parameter, defaulting to `NullLogger`;
  - make `ReleaseAllAsync` log each failure at Warning, with artifact id and lease id, and never throw;
  - keep renewal and write failure semantics.

  Verify that registration (`RuntimeCoreServiceCollectionExtensions.cs:473`) still resolves under scope validation.

## Phase 6: Polish & Cross-Cutting

- [x] T025 [P] Update the "Lease identity" and store-behavior notes in `src/essentials/Workflows/Runtime/EXTENSION_POINTS.md`: per-attempt checkpoint ids, plus the contract behavior from contracts/root-write-lease-coordination.md.
- [x] T026 Run the full affected suites locally:
  - Runtime tests;
  - Runtime EF tests;
  - Runtime EF provider tests on PostgreSQL;
  - the EF migrations suite;
  - architecture guards, if runnable;
  - `Elsa.Maps.Generator check`, refreshing and staging any genuinely changed maps.
- [x] T027 Bite-proof the regression tests. Temporarily restore the single-row lease path in `EfWorkflowExecutableStore.cs`. The 3 SQLite regression tests and the PostgreSQL parallel test must fail again. Restore the code and record the results.
- [ ] T028 Push. Watch CI ("Build & test", "EF container suites (runtime)" covering PostgreSQL, SQL Server and MySQL, "Core-only build & test", Maps) until it is green on the head. Handle every CodeRabbit finding per the PR rules.
- [ ] T029 Re-run the A1 end-to-end matrix on the Azure runner `rg-elsa-p2382-98e7664aa3` / `elsa-p2382-98e7664aa3` (8 cases, quickstart.md). This needs the owner to start the runner or relay a Codex run. All 8 must pass (SC-001). Post the results on #2532 and #2538.
- [ ] T030 Set the spec `**Status**` to `Implemented — PR #2539` in `specs/200-root-write-lease-rows/spec.md`. Update `docs/program-goals/runtime-throughput.md`: mark the lease unit done, and record that Track B is unblocked once T029 passes. Post the merge-gate evidence comment on PR #2539.

## Dependencies & Execution Order

- **Setup:** T001 and T002 come first.
- **Foundational:** T003, then T004, then T005, then T006, then T007. T008 can proceed in parallel with T006 and T007 once T005 is done.
- **US1:** T010, then T011, then T012, all after T008. T009 and T013 can be written any time after T001.
- **US2:** T018 and T019 depend on T010 and T011 (same file, sequential). T014–T017 can be written in parallel early. T020 comes after T019. T021 depends only on T001. T022 runs last in US2.
- **US3:** T023 and T024 are independent of US1 and US2 because they touch a different file. They can run in parallel with Phase 3.
- **Polish:** after all stories.

## Parallel Opportunities

- **Different files, early:** T009, T013, T014–T017, T021, T023 and T024.
- **Sequential, same file:** T010, T011, T018 and T019 all change `EfWorkflowExecutableStore.cs`.

## Implementation Strategy

1. **MVP = US1.** It alone is expected to remove the A1 failures, because the lease faults are the observed cause.
2. **US2 ships in the same PR.** Moving leases out of the shared row without the write-then-check guard side would weaken GC safety, so US1 must not merge without US2.
3. **US3 is small and independent.** It removes the "durable commit reported failed" class of incidents.
4. **One PR (#2539).** Commits are ordered: foundational → US1 → US2 → US3 → polish. Each commit has a local green run before it is pushed.

## Notes

- **T002 inventory** (§2.21.1). Every existing lease, guard, deletion and GC test passes. Three needed a change, each with its subject and objective kept:
  - `EfRuntimeArtifactScopeTests.Release_root_write_lease_reloads_after_contention_and_preserves_newer_leases`. **Wiring only.** Release is now one statement on its own row with no `SaveChanges`, so the newer lease is committed before that statement (`RecreateBeforeFirstCommandInterceptor`) instead of before `SaveChanges`. Objective kept: a contended release removes only its own lease, and the newer lease still renews.
  - `EfRuntimeArtifactScopeTests.Coordination_operations_reject_orphan_and_corrupt_executable_pairs`. **Assertion change for renew and release; owner approved on PR #2539.** Both now touch only the holder's own row and never read the pair (FR-002), so on an orphan pair renew returns `false` and release is a no-op that leaves no row, where they used to throw `InvalidDataException`. Acquire, begin-deletion and cancel still throw, and acquire still rejects a corrupt executable payload. Objective kept: no lease operation treats an orphan or corrupt pair as leased. At the manager the effect is the same as before: a refused renewal cancels the write as lease-lost, and a release failure is logged (FR-011).
  - `EfRuntimeCheckpointCommitStoreTests.Nonempty_execution_scheduler_and_fence_commit_as_one_replayable_unit`. **Contract change (#2286, owner decision).** The asserted lease id moves from `checkpoint:{CommitId}` to `checkpoint:{CommitId}:{32-hex nonce}`.
  - Unchanged and still green, but now satisfied by construction: `Renew_root_write_lease_retries_after_unrelated_coordination_contention`. Its interleaving fires on `SaveChanges`, which renewal no longer issues. Renewal under real contention is covered by `Root_write_lease_renew_is_not_failed_by_other_holders_of_the_same_artifact`.
- **Red and bite-proof evidence** (local; temporary mutations only, nothing committed):
  - On the base store: the 3 SQLite regression tests and `Root_write_lease_operations_do_not_save_or_discard_changes_staged_on_the_callers_context` fail. The base acquire's `ChangeTracker.Clear()` discards the staged change.
  - Acquire post-check removed: `Acquire_withdraws_and_returns_null_when_a_guard_commits_between_its_lease_commit_and_check` and `Acquire_returns_null_and_withdraws_when_the_artifact_is_deleted_before_its_lease_commits` fail.
  - Guard live-lease count removed: `Begin_deletion_cancels_and_returns_null_when_a_lease_commits_between_its_guard_commit_and_count` fails.
  - Guarded-delete live-lease re-check removed: `Guarded_delete_refuses_while_a_lease_committed_after_the_guard_is_live` fails.
  - The old throwing `ReleaseAllAsync` restored: `ExecuteAsync_ReportsASuccessfulWriteAsSuccessWhenALeaseReleaseFails` and `ExecuteAsync_SurfacesTheWriteFailureUnchangedWhenAReleaseAlsoFails` fail.
  - Expired-guard clearing removed (CodeRabbit finding on research R2): `PostgreSql_a_guard_that_expires_during_its_delete_cannot_delete_under_a_late_lease` fails with the delete committed under a granted lease, and `Acquire_clears_an_expired_guard_by_revision_so_a_delete_still_holding_it_cannot_commit` fails.
  - Nonce removed: the in-memory and EF `...AttemptsOfOneCommitHoldIndependentRootWriteLeases` / `Attempts_of_one_commit_hold_independent_root_write_leases` tests fail.
- **T007:** `module-migrate.sh script-check` does not apply. No `db/migrations` artifact is committed and CI does not run it. The EF migrations suite covers model/migration match.
- **T026:** the architecture guards could not run locally, because they need `Bpmn.*` packages from the blocked feed. CI runs them.
- **Out of scope:**
  - #2537: incident projection.
  - Lease skip for non-root-changing commits: follow-up unit.
  - Global expired-lease sweep: deferred (research R6).

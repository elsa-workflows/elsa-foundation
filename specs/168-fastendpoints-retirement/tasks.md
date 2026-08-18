---

description: "Task list for the final FastEndpoints retirement unit (#1376)"
---

# Tasks: Final FastEndpoints Retirement

**Input**: Design documents from `/specs/168-fastendpoints-retirement/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [quickstart.md](./quickstart.md)

**Governing constraint**: Framework constitution §2.25.3. A text search finds candidates and
authorizes nothing. Every removal is justified by a build-and-suite result.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to

---

## Phase 1: Establish the candidate set and the measurement baseline (US1)

**Purpose**: Produce the reviewable classification and the before-state that SC-003 is measured
against. No deletions in this phase.

- [x] T001 [US1] Scan `src/`, `tests/`, `tools/`, `docker/`, `docs/`, and `specs/` for FastEndpoints references across `.cs`, `.csproj`, `.json`, and `.md`, excluding `obj/` and `bin/`; record every hit as a classification entry with `kind` set and `disposition: Unresolved`
- [x] T002 [US1] Create `specs/168-fastendpoints-retirement/classification.md` holding the entries per the `data-model.md` schema
- [x] T003 [P] [US1] Capture executed test names for `Elsa.Architecture.Tests` via `--list-tests` into a baseline file
- [x] T004 [P] [US1] Capture executed test names for each per-module API test project appearing in the candidate set
- [x] T005 [US1] Record the current retirement-guard state: run `FastEndpointsTransitionTests` and confirm the discovered first-party surface is empty and the exception registry is `[]`
- [x] T006 [US1] Assign a disposition and a one-line reason to every entry; `Preserve` reasons MUST name the guarantee protected, not that the code compiles (V-4)
- [x] T007 [US1] Verify V-1 and V-2: every reference appears exactly once and the entries cover every hit from T001
- [x] T008 [US1] Resolve every `Unresolved` entry, or record why it blocks its own reference from removal; zero may remain at merge (SC-002)

**Checkpoint**: Classification is complete and reviewable. This is the point at which review is most
valuable, because a wrongly-removed guard cannot fail afterwards.

---

## Phase 2: Remove the shared infrastructure (US2)

**Purpose**: Delete the first-party FastEndpoints infrastructure that precondition 1 showed has no
production consumer. Each batch is gated.

- [x] T009 [US2] Delete `src/Elsa/Api/FastEndpoints/` and remove the project from `Elsa.Server.slnx`
- [x] T010 [US2] Delete `tests/Elsa/Api/FastEndpoints/Tests/`, whose sole purpose is testing the removed infrastructure
- [x] T011 [US2] Build `Elsa.Server.slnx`; record 0 errors and confirm no new warning versus the base commit
- [ ] T012 [US2] Run `Elsa.Architecture.Tests` in full; record executed/failed/skipped counts, not a bare "green"
- [x] T013 [US2] Assert the retirement guard still passes and the first-party surface is still empty (FR-005, SC-001)
- [x] T014 [US2] Attach the T011-T013 results to each `Remove` entry as its §2.25.3 evidence

**Checkpoint**: The program's headline outcome is reached and evidenced.

---

## Phase 3: Remove the coexistence oracles (US2, per maintainer decision)

**Purpose**: Delete the four oracles as transitional. Recorded as a §2.25.2 deviation, since no gate
replaces them.

- [x] T015 [P] [US2] Delete `tests/Elsa/Studio/Preferences/Tests/StudioPreferencesApiCoexistenceTests.cs` and its canary host support if unused elsewhere
- [x] T016 [P] [US2] Delete `tests/Elsa/Secrets/Tests/SecretsApiCoexistenceTests.cs` and `Support/SecretsCanaryHost.cs` if unused elsewhere
- [x] T017 [P] [US2] Delete `tests/Elsa/Diagnostics/StructuredLogs/Tests/StructuredLogsApiCoexistenceTests.cs`
- [x] T018 [P] [US2] Delete `tests/Elsa/Architecture/Wave2MixedHostCoexistenceTests.cs`
- [ ] T019 [US2] Build and run the four affected suites; record counts
- [x] T020 [US2] Confirm each deletion corresponds to a `Remove` entry and that no *other* test disappeared alongside them

---

## Phase 4: Prove no preserved guard vanished (US3)

**Purpose**: The check that a green summary cannot provide. This phase is why the baselines in
Phase 1 exist.

- [ ] T021 [US3] Re-capture executed test names for every suite baselined in T003-T004
- [ ] T022 [US3] Diff after-state against before-state; every disappearance MUST map to a `Remove` or `Archive` entry
- [ ] T023 [US3] Treat any unmapped disappearance as a defect and restore it, rather than reclassifying it after the fact
- [ ] T024 [US3] Confirm every `Preserve` entry's guard executes and passes, specifically the endpoint-security, permission-authorization-boundary, per-wave authorization, and Foundation Identity API guards (FR-006)
- [ ] T025 [US3] Confirm any `Re-anchor` entry's assertion is unchanged in substance, not merely still compiling

---

## Phase 5: Reconcile configuration (US4)

**Purpose**: String-keyed feature names are invisible to the compiler, so activation is the
instrument.

- [x] T026 [US4] Remove the `FastEndpoints` entry from `docker/compose/elsa-workbench.shells.json`
- [x] T027 [US4] Reconcile the `CShells.FastEndpoints.Abstractions` entry in `src/Apps/Elsa.Foundation.Host/appsettings.json` against what the host actually loads
- [ ] T028 [US4] Verify the Docker Workbench composition activates cleanly with no unresolved feature (FR-007)
- [x] T029 [US4] Confirm the source and Docker compositions no longer disagree about FastEndpoints
- [x] T030 [US4] Reconcile surviving `CShells.FastEndpoints` package references against the classification; drop those classified `Remove` and verify by build (R-005 is resolved here, by evidence)

---

## Phase 6: Archival decision (US5)

**Purpose**: Separate the frozen evidence from the tooling that regenerates it, per R-006.

- [x] T031 [US5] Classify each frozen baseline JSON under `tests/**/Baselines/` as retained or archived, with a reason
- [x] T032 [US5] Classify `tools/compatibility/RuntimeFastEndpointsCapture` and `WorkflowsDesignFastEndpointsCapture`, which hold the last first-party compile-time dependency on FastEndpoints
- [x] T033 [US5] Classify the `*BeforeCapture` projects under `tests/`
- [x] T034 [US5] Execute the decision; if any capture tool is retained, record that as a finding, since it keeps alive the dependency this unit exists to remove
- [x] T035 [US5] For each archived item, record what it proved and why it is no longer reproducible (FR-008)

---

## Phase 7: Stale prose sweep (US2/US3 support)

**Purpose**: The compiler is silent on prose. Wave 8 shipped this defect class twice.

- [x] T036 [US2] Search for surviving mentions of removed types across `src/`, `docs/`, and `specs/`, including `FastEndpointsFeatureBase`, `ElsaEndpoint*`, `ApiSecurityFeature`, and `PermissionNames`
- [x] T037 [US2] Read each hit and correct those that *describe* a removed type; a mention is not automatically wrong
- [ ] T038 [US2] Correct the `PermissionNames` reference in `IdentitySeeder` specifically (FR-009) — **BLOCKED, deliberately not done.** The file sits under the frozen ASP.NET Core Identity EF oracle owned by the Zero-EF program, whose ratchet permits no source change before its own approved removal unit. Attempting it turned `FrozenAspNetCoreIdentityEfOracleRatchetTests` red; the edit was reverted and the dangling reference is recorded in the completion report for that unit to fix.
- [x] T039 [US2] Confirm zero surviving comments or documents describe a removed type (SC-005) — met **except** the single T038 exception above, which is recorded rather than silently tolerated

---

## Phase 8: Maps, report, and program closure (US6)

**Purpose**: Publish the record and close the program.

- [ ] T040 [US6] Regenerate maps with `Elsa.Maps.Generator -- all`, then `-- check`; stage every changed map by explicit path, including `manifest.json` when it changed
- [ ] T041 [US6] Write the completion report under `docs/reports/` with route and owner counts, removal evidence, residual third-party compatibility boundaries, risks, and rollback guidance (FR-010)
- [ ] T042 [US6] Include the §2.25.4 **retired** list with its evidence
- [ ] T043 [US6] Include the §2.25.4 **examined and deliberately kept** list with reasons; this is what stops the next review re-deriving these conclusions
- [ ] T044 [US6] Record the withdrawal of mixed-host guard coverage, distinguishing the capability, preserved by construction, from the guard, withdrawn by decision (FR-011)
- [ ] T045 [US6] Record the §2.25.2 deviation: the oracles were deleted with no replacing gate
- [ ] T046 [US6] Record the packaging surface change: `Elsa.Api.FastEndpoints` ceases to be produced (§2.13)
- [ ] T047 [US6] Update ADR/program records and close completed child issues

---

## Phase 9: Delivery gates

- [ ] T048 Full `Elsa.Server.slnx` build: 0 errors, no branch-introduced warning
- [ ] T049 Full architecture suite and every affected per-module suite: report executed/failed/skipped
- [ ] T050 `Elsa.Maps.Generator -- check` green
- [ ] T051 Changed-file formatter and `git diff --check` green
- [ ] T052 Diff review of the complete branch against `main`, read rather than skimmed
- [ ] T053 Open the PR ready for review, linked to #1376, and post the QA gate evidence as a PR comment
- [ ] T054 Merge only on a green exact-head gate, using an exact-head match so no racing push can substitute a different tree
- [ ] T055 Verify CI, HTTP workflow performance, Maps, Packages, Docker Images, and Code Quality on the exact merged commit; fix forward or revert any red main gate
- [ ] T056 Comment the merged SHA and post-merge run URLs on #1376, set `status:done`, move its Project 45 item to Done, and release the claim
- [ ] T057 Close program #1342 once #1376 is Done and no child remains open

---

## Dependencies and execution order

- **T001-T008 block every removal.** FR-002 forbids removing anything not classified, and §2.25.3
  forbids classifying `Remove` on the strength of the scan alone.
- **T003-T004 must precede any deletion**, or the SC-003 measurement is impossible to reconstruct.
- Phase 2 precedes Phase 3 only for gate attribution; both are removals, and separating them keeps a
  red gate readable.
- **Phase 4 gates Phases 5-8.** If a preserved guard vanished, that is fixed before anything else
  proceeds.
- T030 resolves R-005 by build evidence and therefore depends on Phases 2 and 3 being complete.
- Phase 7 depends on removal being finished, since it searches for prose describing already-removed
  types.
- T040 depends on all project and spec changes being final, because maps regenerate from the tree.
- T057 depends on T056.

## Parallel opportunities

- T003 and T004 baseline different suites.
- T015-T018 delete files in four different modules.
- T031-T033 classify three independent artifact groups.

## Implementation strategy

1. Classify before touching anything; treat the classification as the reviewable checkpoint.
2. Remove in batches, gating each, so evidence attaches to specific deletions.
3. Prove the preserved guards still run by diffing test names, not by reading a green summary.
4. Verify configuration by activating it, because the compiler cannot see string-keyed features.
5. Report both what went and what stayed, and name the deviation rather than dressing it up.

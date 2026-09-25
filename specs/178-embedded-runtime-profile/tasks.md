# Tasks: First Embedded runtime starting profile

**Input**: [spec](spec.md), [plan](plan.md), [research](research.md), [contract](contracts/embedded-profile-v1.md)

## Phase 1: Setup

- [x] T001 Verify current branch, issue claim, map freshness and existing bundled-catalog conventions in `src/essentials/Modularity/Planning/` and `docs/maps/`.

## Phase 2: Foundation

- [x] T002 Add required-edge fallback from pinned reviewed dependency rationale, preserving loaded-descriptor authority, in `src/essentials/Modularity/Planning/Services/SelectionPlanner.cs` and focused `tests/essentials/Modularity/Planning/Tests/HostAssessmentTests.cs`.
- [x] T003 Update `src/essentials/Modularity/Planning/Bridge/CshellsSourceReader.cs` to understand the proven base-object plus selected-overlay-false combination, with source-preservation tests in `tests/essentials/Modularity/Planning/Tests/CompositionBridgeSourceTests.cs`.

## Phase 3: User Story 1 - Choose and inspect the starter

**Independent test**: Built-in profile yields exact 16 IDs and provenance; changed same-version digest refuses; explicit required-member removal remains absent and unresolved.

- [x] T004 [P] [US1] Add committed immutable 16-member Foundation selection catalog and strict loader in `src/essentials/Modularity/Planning/`, using existing digest/reader semantics.
- [x] T005 [US1] Expose a developer initialization path that writes a pinned composition from `embedded-runtime@1` in `src/essentials/Cli/`, reusing the common planner.
- [x] T006 [US1] Cover built-in catalog, initialization, exact plan, pin drift, removal and secret absence in `tests/essentials/Modularity/Planning/Tests/` and `tests/essentials/Cli/Tests/`.

## Phase 4: User Story 2 - Review a host candidate

**Independent test**: Supported object-map candidate re-reads to exact accepted IDs; base/unselected files remain intact; unsupported mappings and known missing edges publish nothing.

- [x] T007 [US2] Implement selected-overlay object-map feature additions/disables and explicit refusal cases in `src/essentials/Modularity/Planning/Bridge/CompositionCandidateBuilder.cs`.
- [x] T008 [US2] Re-read candidate and compare effective IDs with accepted planner set before publication in `src/essentials/Modularity/Planning/Bridge/CompositionCandidateBuilder.cs`.
- [x] T009 [US2] Surface redacted activation edits and known-edge refusal in `src/essentials/Cli/CompositionGenerateCommand.cs` and its projection.
- [x] T010 [US2] Add positive and negative file-candidate/CLI tests in `tests/essentials/Modularity/Planning/Tests/CompositionBridgeSourceTests.cs` and `tests/essentials/Cli/Tests/CompositionGenerateCliTests.cs`.

## Phase 5: User Story 3 - Run the host example

**Independent test**: Exact published profile activates in generic host with named SQLite/file lock and completes suspend/resume; missing lock fails.

- [x] T011 [US3] Tie built-in profile exact membership to the existing generic-host proof in `tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/EmbeddedFixtureHostEvidenceTests.cs`.
- [x] T012 [US3] Document developer command flow, named persistence, lock/signing prerequisites and API boundary in `docs/reference/` and `docs/reports/runtime-composition/first-profile-decision.md`.

## Phase 6: Polish and validation

- [x] T013 Review secret handling, source preservation, no silent upgrade and changed-file scope against `specs/178-embedded-runtime-profile/contracts/embedded-profile-v1.md`.
- [x] T014 Run affected tests, architecture guard, maps freshness/generation as needed, solution-filter check and diff review; record results on `#2052`.

## Dependencies and delivery

T002–T003 precede candidate mapping. T004 can proceed independently of those foundational edits; T005–T006 consume T004. T007–T010 consume T002–T003 and the accepted plan. T011 proves the published profile, and T012 follows tested behavior. MVP is US1 plus the known-edge gate; #2052 is complete only after US2/US3 and PR gates.

The issue tracks PR review, merge and post-merge main verification; these are delivery gates rather than code tasks.

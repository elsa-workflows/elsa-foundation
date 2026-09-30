# Tasks: Effective persistence preview

**Input**: [plan](plan.md), [spec](spec.md), research, data model and contracts.
**Status**: Planned implementation tasks; all unchecked. Issue2175 delivers this contract only. One coherent implementation issue/PR will own T001–T020 after review and specification delivery.

## Phase 1: Setup

- [ ] T001 Refresh/claim the linked implementation issue and inspect accepted contracts in `specs/187-effective-persistence-preview/`; recheck matching PRs and preserve dirty work.
- [ ] T002 Define shared real-host/captured-input fixture helpers in `tests/essentials/Cli/Tests/` and reuse existing built host fixtures; own deterministic teardown, with no new project/suite.

## Phase 2: Foundational

- [ ] T003 Write failing strict candidate/legacy-envelope contract tests in `tests/essentials/Cli/Tests/WorkerProtocolTests.cs`, then define private candidate version1 request/response shapes and command admission in `src/essentials/Cli/Worker/WorkerContract.cs`, with legacy envelopes unchanged and live fields forbidden.
- [ ] T004 Write failing source-byte/nonregular/drift tests with stubbed file dependencies in `tests/essentials/Cli/Tests/CompositionFileSourceTests.cs`, then add candidate-specific bounded immutable source capture/recheck to `src/essentials/Cli/CompositionFileSource.cs` and `CompositionInputSnapshot.cs`; all supplied profiles including unused files share invocation ownership with candidate bytes; reuse/extract fail-closed regular-file preflight so FIFO/device sources refuse before reads.

## Phase 3: User Story 1 — Inspect accepted edits (P1)

**Goal**: One actual candidate, one host producer and one visible safe preview.
**Independent test**: A01–A03/A05/A14 through real built CLI/worker/host, both import and workspace-profile starting paths.

- [ ] T005 [US1] Write failing actor/capability/consumer expectations and per-implementation stubbed public-surface branch tests in `tests/essentials/Cli/Tests/CandidateInspectionTests.cs` and `tests/essentials/Persistence/EntityFrameworkCore/Migrations/Tests/EfCandidateInspectionTests.cs`; cover every new logic class, then retain those objectives through implementation.
- [ ] T006 [US1] Write failing absent-file removal/retained setting/source-shape cases in `tests/essentials/Modularity/Planning/Tests/CompositionCandidateTests.cs`, then correct `src/essentials/Modularity/Planning/Bridge/CompositionCandidateBuilder.cs` to emit safe selected-overlay false declarations.
- [ ] T007 [US1] Add independent version1 capability and streamed operation in `src/essentials/Persistence/EntityFramework/Tooling/EfCandidateInspectionContract.cs`, `EfCandidateInspectionOperation.cs` and `EfToolingHost.cs`; validate compiled assembly layout separately, reuse real composer/configuration merge and CShells descriptors/dependency resolver, and invoke shared preparation true.
- [ ] T008 [US1] Add candidate-only capability/installed-closure dispatch in `src/essentials/Cli/Worker/ToolingEntryPoint.cs`, `WorkerRunner.cs` and `Program.cs`; satisfy failing branch tests, reject Restore before its call and prevent live-reader fallback.
- [ ] T009 [US1] Add explicit command and registration in `src/essentials/Cli/CompositionInspectCommand.cs` and `ElsaCli.cs`, using accepted pins, one Build call, separate host/source arguments and trust flag; satisfy conditional/default/error unit tests before actor acceptance.
- [ ] T010 [US1] Execute T005's real producer serialization→worker→consumer→human/JSON and both accepted-input journeys in `tests/essentials/Cli/Tests/CandidateInspectionTests.cs`, using actual host EF operation rather than a fake response as parity evidence.

## Phase 4: User Story 2 — Conflicts and evidence limits (P1)

**Goal**: Targets/refusals match runtime preparation while evidence limits remain visible.
**Independent test**: A04–A07/A14, actual default/edge/value comparisons and old v2 compatibility.

- [ ] T011 [US2] Write failing runtime/host target, refusal and per-implementation branch tests in `tests/essentials/Persistence/EntityFrameworkCore/Migrations/Tests/EfCandidateInspectionTests.cs` and `tests/essentials/Cli/Tests/CandidateInspectionTests.cs`, including defaults, descriptor conflicts and distinct connection values.
- [ ] T012 [US2] Implement exact accepted/requested/expanded reconciliation and removed/disabled-required refusals in `src/essentials/Persistence/EntityFramework/Tooling/EfCandidateInspectionOperation.cs`, and scope/target projection in `EfCandidateInspectionContract.cs`; legacy fields remain unprojected, old v2 affinity=false and runtime modes unchanged.
- [ ] T013 [US2] Validate closed response contents/correlation and render configurationResolution with safe plan reasons in `src/essentials/Cli/CompositionInspectCommand.cs`; satisfy all new consumer branches and do not promote Planning.PersistenceEvidence.
- [ ] T014 [US2] Execute actual runtime shell preparer/host candidate comparisons over identical candidate configuration in `tests/essentials/Persistence/EntityFrameworkCore/Migrations/Tests/EfCandidateInspectionTests.cs`; prove targets/refusals and old v2 compatibility with real enrolled assemblies.

## Phase 5: User Story 3 — Safe failure and recovery (P2)

**Goal**: Bounded transport, source stability, confidentiality and real process cleanup.
**Independent test**: A08–A13 and old-command no-worker controls.

- [ ] T015 [US3] Write failing bounded-byte/identity/version/console/process/cancellation/drift branch tests in `tests/essentials/Cli/Tests/CandidateProcessTests.cs` and `CandidateInspectionTests.cs`, plus producer boundary tests in `tests/essentials/Persistence/EntityFrameworkCore/Migrations/Tests/EfCandidateInspectionTests.cs`; stub dependencies for each logic implementation and retain real child adverse tests.
- [ ] T016 [US3] Implement bounded candidate process try/finally in `src/essentials/Cli/WorkerProcess.cs`, including literal mode argument, timeout, every I/O/cancellation stage, discarded stderr, kill-tree+wait and cleanup-failure outcome; legacy launches unchanged.
- [ ] T017 [US3] Enforce request/response bounds and fixed errors in `src/essentials/Cli/Worker/Program.cs`, `WorkerContract.cs`, `WorkerRunner.cs` and host candidate operation; suppress raw console before host code; recheck full inputs immediately before output in `src/essentials/Cli/CompositionInspectCommand.cs`.
- [ ] T018 [US3] Execute actual child cancellation/flood/timeout, stale/mixed/unused-profile capture, malformed/oversized exchange, unknown retention and no-access/canary sentinels in `tests/essentials/Cli/Tests/CandidateProcessTests.cs`, `CandidateInspectionTests.cs` and existing migrations fixtures; no surviving process/partial output.

## Phase 6: Polish and complete integration

- [ ] T019 Run all proof rows and meaningful selection/capture/affinity/lifecycle mutation controls; update `src/essentials/Cli/README.md`, `src/essentials/Persistence/EntityFramework/README.md` and `src/essentials/Persistence/EntityFramework/EXTENSION_POINTS.md` and evidence under `specs/187-effective-persistence-preview/`; root audits actual producer-consumer scope and DRY teardown.
- [ ] T020 Run final affected suites plus architecture/maps all/check, review generated findings and exact diff, obtain independent/root/exact-head review+CI and exact-main CI/Maps; update `specs/187-effective-persistence-preview/spec.md` to Implemented and synchronize issue/program/project delivery only after proof.

## Dependencies and parallel opportunities

T001→T002→T003/T004→US1→US2→US3→T019→T020. Within US1: T005 tests precede T006/T007 production; T008 depends on T007 capability/composition, T009 on T004/T006/T007/T008, and T010 on all integrated owners. Within US2: T011 tests precede T012/T013 and T014 requires both. Within US3: T015 tests precede T016/T017 and T018 requires both. Root integrates all owners before CLI journey proof. US2 depends on US1's real producer; US3 is independently verifiable on that lifecycle but required before release. Do not ship US1 without conflict/safety obligations.

No task is marked [P] because most production owners share files and reviewed producer/consumer changes must remain coordinated. Parallel examples: bounded independent source/review audits or T006 builder work and T007 host work in explicitly separate files/branches; T011 versus T015 test preparation after their dependencies, with heavy test execution serialized by root.

## Implementation strategy

Smallest useful MVP is the complete safe file-only actor journey across US1–US3, not a producer-only shim. Deliver through one issue/PR, using focused signals before final full affected gates. New acceptance tests are required explicitly by the spec/matrix; no repetitive new suite/provider matrix. Keep external providers, arbitrary portable unknown export, finished UI/human evaluation and apply/recovery in their existing owners.

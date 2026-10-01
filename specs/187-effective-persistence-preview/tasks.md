# Tasks: Effective persistence preview

**Input**: [plan](plan.md), [spec](spec.md), research, data model and contracts.
Delivery verified (2026-10-01): T001–T020 are complete for the bounded file-only candidate-inspection actor. PR #2183 merged as e4a699879; actual verified resulting main `7bee192e9` contains it and all six delivery workflows pass. See the current delivery audit in [implementation-evidence.md](implementation-evidence.md) for exact branch/main identities, actual job/log results and retained native/mutation limits. Earlier dated checkpoints below describe their then-current state. Program outcomes beyond spec187 and separately tracked incident causes/correction verification remain incomplete; GitHub #2216 is closed without an established bookmark-failure cause, #2185 remains open, and #2250 merged with its own resulting-main checkpoint still incomplete.

## Phase 1: Setup

- [X] T001 Refresh/claim the linked implementation issue and inspect accepted contracts in `specs/187-effective-persistence-preview/`; recheck matching PRs and preserve dirty work.
- [X] T002 Define shared real-host/captured-input fixture helpers in `tests/essentials/Cli/Tests/` and reuse existing built host fixtures; own deterministic teardown, with no new project/suite.

## Phase 2: Foundational

- [X] T003 Write failing strict candidate/legacy-envelope contract tests in `tests/essentials/Cli/Tests/WorkerProtocolTests.cs`, then define private candidate version1 request/response shapes and command admission in `src/essentials/Cli/Worker/WorkerContract.cs`, with legacy envelopes unchanged and live fields forbidden.
- [X] T004 Write failing source-byte/nonregular/drift/ownership tests with stubbed file dependencies in `tests/essentials/Cli/Tests/CompositionFileSourceTests.cs` and `tests/essentials/Cli/Tests/CompositionInspectionCaptureTests.cs`, then add candidate-specific bounded immutable source capture/recheck in `src/essentials/Cli/CompositionFileReader.cs`, `src/essentials/Cli/CompositionFileSource.cs`, `src/essentials/Cli/CompositionInputSnapshot.cs` and `src/essentials/Cli/CompositionInspectionCapture.cs`; one owner validates finite selection identities before one Build call and retains all supplied profiles including unused files with candidate bytes; reuse/extract fail-closed regular-file preflight so FIFO/device sources refuse before reads.

Foundation execution evidence is recorded in [implementation evidence](implementation-evidence.md). These checks do not close the actor/runtime/adverse acceptance rows.

## Phase 3: User Story 1 — Inspect accepted edits (P1)

**Goal**: One actual candidate, one host producer and one visible safe preview.
**Independent test**: A01–A03/A05/A14 through real built CLI/worker/host, both import and workspace-profile starting paths.

- [X] T005 [US1] Write failing actor/capability/consumer expectations and per-implementation stubbed public-surface branch tests in `tests/essentials/Cli/Tests/CandidateInspectionTests.cs` and `tests/essentials/Persistence/EntityFrameworkCore/Migrations/Tests/EfCandidateInspectionTests.cs`; cover every new logic class, then retain those objectives through implementation.
- [X] T006 [US1] Write failing absent-file removal/retained setting/source-shape cases in `tests/essentials/Modularity/Planning/Tests/CompositionCandidateTests.cs`, then correct `src/essentials/Modularity/Planning/Bridge/CompositionCandidateBuilder.cs` to emit safe selected-overlay false declarations.
- [X] T007 [US1] Add independent version1 capability and streamed operation in `src/essentials/Persistence/EntityFramework/Tooling/EfCandidateInspectionContract.cs`, `EfCandidateInspectionOperation.cs` and `EfToolingHost.cs`; validate compiled assembly layout separately, reuse real composer/configuration merge and CShells descriptors/dependency resolver, and invoke shared preparation true.
- [X] T008 [US1] Add candidate-only capability/installed-closure dispatch in `src/essentials/Cli/Worker/ToolingEntryPoint.cs`, `WorkerRunner.cs` and `Program.cs`; satisfy failing branch tests, reject Restore before its call and prevent live-reader fallback.
- [X] T009 [US1] Add explicit command and registration in `src/essentials/Cli/CompositionInspectCommand.cs` and `ElsaCli.cs`, using accepted pins, one Build call, separate host/source arguments and trust flag; satisfy conditional/default/error unit tests before actor acceptance.
- [X] T010 [US1] Execute T005's real producer serialization→worker→consumer→human/JSON and both accepted-input journeys in `tests/essentials/Cli/Tests/CandidateInspectionTests.cs`, using actual host EF operation rather than a fake response as parity evidence.

## Phase 4: User Story 2 — Conflicts and evidence limits (P1)

**Goal**: Targets/refusals match runtime preparation while evidence limits remain visible.
**Independent test**: A04–A07/A14, actual default/edge/value comparisons and old v2 compatibility.

- [X] T011 [US2] Write failing runtime/host target, refusal and per-implementation branch tests in `tests/essentials/Persistence/EntityFrameworkCore/Migrations/Tests/EfCandidateInspectionTests.cs` and `tests/essentials/Cli/Tests/CandidateInspectionTests.cs`, including defaults, descriptor conflicts and distinct connection values.
- [X] T012 [US2] Implement exact accepted/requested/expanded reconciliation and removed/disabled-required refusals in `src/essentials/Persistence/EntityFramework/Tooling/EfCandidateInspectionOperation.cs`, and scope/target projection in `EfCandidateInspectionContract.cs`; legacy fields remain unprojected, old v2 affinity=false and runtime modes unchanged.
- [X] T013 [US2] Validate closed response contents/correlation and render configurationResolution with safe plan reasons in `src/essentials/Cli/CompositionInspectCommand.cs`; satisfy all new consumer branches and do not promote Planning.PersistenceEvidence.
- [X] T014 [US2] Execute actual runtime shell preparer/host candidate comparisons over identical candidate configuration in `tests/essentials/Persistence/EntityFrameworkCore/Migrations/Tests/EfCandidateInspectionTests.cs`; prove targets/refusals and old v2 compatibility with real enrolled assemblies.

## Phase 5: User Story 3 — Safe failure and recovery (P2)

**Goal**: Bounded transport, source stability, confidentiality and real process cleanup.
**Independent test**: A08–A13 and old-command no-worker controls.

- [X] T015 [US3] Write failing bounded-byte/identity/version/console/process/cancellation/drift branch tests in `tests/essentials/Cli/Tests/CandidateProcessTests.cs` and `CandidateInspectionTests.cs`, plus producer boundary tests in `tests/essentials/Persistence/EntityFrameworkCore/Migrations/Tests/EfCandidateInspectionTests.cs`; stub dependencies for each logic implementation and retain real child adverse tests.
- [X] T016 [US3] Implement bounded candidate process try/finally in `src/essentials/Cli/CandidateWorkerProcess.cs`, including literal mode argument, timeout, every I/O/cancellation stage, discarded stderr, kill-tree+wait and cleanup-failure outcome; legacy launches unchanged.
- [X] T017 [US3] Enforce request/response bounds and fixed errors in `src/essentials/Cli/Worker/Program.cs`, `WorkerContract.cs`, `WorkerRunner.cs` and host candidate operation; suppress raw console before host code; recheck full inputs immediately before output in `src/essentials/Cli/CompositionInspectCommand.cs`.
- [X] T018 [US3] Execute actual child cancellation/flood/timeout, stale/mixed/unused-profile capture, malformed/oversized exchange, unknown retention and no-access/canary sentinels in `tests/essentials/Cli/Tests/CandidateProcessTests.cs`, `CandidateInspectionTests.cs` and existing migrations fixtures; no surviving process/partial output.

## Phase 6: Polish and complete integration

- [X] T019 Run all proof rows and meaningful selection/capture/affinity/lifecycle mutation controls; update `src/essentials/Cli/README.md`, `src/essentials/Persistence/EntityFramework/README.md` and `src/essentials/Persistence/EntityFramework/EXTENSION_POINTS.md` and evidence under `specs/187-effective-persistence-preview/`; root audits actual producer-consumer scope and DRY teardown.
- [X] T020 Run final affected suites plus architecture/maps all/check, review generated findings and exact diff, obtain independent/root/exact-head review+CI and exact-main CI/Maps; update `specs/187-effective-persistence-preview/spec.md` to Implemented and synchronize issue/program/project delivery only after proof.

## Dependencies and parallel opportunities

T001→T002→T003/T004→US1→US2→US3→T019→T020. Within US1: T005 tests precede T006/T007 production; T008 depends on T007 capability/composition, T009 on T004/T006/T007/T008, and T010 on all integrated owners. Within US2: T011 tests precede T012/T013 and T014 requires both. Within US3: T015 tests precede T016/T017 and T018 requires both. Root integrates all owners before CLI journey proof. US2 depends on US1's real producer; US3 is independently verifiable on that lifecycle but required before release. Do not ship US1 without conflict/safety obligations.

No task is marked [P] because most production owners share files and reviewed producer/consumer changes must remain coordinated. Parallel examples: bounded independent source/review audits or T006 builder work and T007 host work in explicitly separate files/branches; T011 versus T015 test preparation after their dependencies, with heavy test execution serialized by root.

## Implementation strategy

Smallest useful MVP is the complete safe file-only actor journey across US1–US3, not a producer-only shim. Deliver through one issue/PR, using focused signals before final full affected gates. New acceptance tests are required explicitly by the spec/matrix; no repetitive new suite/provider matrix. Keep external providers, arbitrary portable unknown export, finished UI/human evaluation and apply/recovery in their existing owners.


Review-correction checkpoint (2026-10-01): package-code and scalar/null removal baselines reproduce the intended failures; fixes are committed and current main is normally integrated. Combined CLI 871, Planning 118, migrations 448, architecture 622, actual Linux 228 and actual Windows 18 pass with zero skipped tests. The two new cancellation controls observe the actual pending operation-exit task through an unchanged native adapter. Final label-only Planning rerun 118 and a 12-file/28-canary passing-artifact scan are clear. Exact sources, limits, repaired test-helper/fixture issues and receipts are in `implementation-evidence.md`. T020 remains open for pushed correction replies, current-head re-review/hosted gates, merge and exact-main CI/Maps.

Latest integration checkpoint (2026-10-01): main `7a3a12c87` is normally integrated at `31dd` with rebuilt CLI 871, Planning 118, EF tooling/migrations 448, architecture 622 and actual Linux 228 passing. Two later platform review statements are explained against primary API contracts and actual native receipts; follow-up review 5378193644 has no actionable finding, with all 28 ledger items replied and zero unresolved threads. Subsequent main `978fe1130` is integrated at `136514`; rebuilt CLI 871, EF tooling/migrations 448, architecture 622 and maps check pass with zero skips. Planning/Linux/Windows proof retains its explicit earlier source identity. Root and independent source review, mixed-source bounded artifact scan and precise limits are recorded in `implementation-evidence.md`. This documentation delta changes no production/test inputs. T020 remains open for fresh pushed-head review/CI, normal merge and exact-main verification.

Delivery audit (2026-10-01): implementation PR #2183 merged as e4a699879; actual verified resulting main `7bee192e9` contains it and passes CI 36858770921, Maps 36858770506, filters 36858770517, Code Quality 36858769854, Packages 36858770618 and Docker 36859276255. Root inspected exact-main job/log results and refreshed merged-PR review/threads. T020 is complete for this bounded file-only actor; chronological prior-source/native limits remain in implementation-evidence.md. Program outcomes beyond spec187, #2250 review/verification, unexplained #2216 incidents and #2185 cleanup discovery remain separate tracked work.

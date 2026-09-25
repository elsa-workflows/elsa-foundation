# Tasks: Local composition file bridge

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md), [semantic contract](contracts/file-bridge-v1.md), [CLI contract](contracts/cli-file-bridge-v1.md), and [setting review input](contracts/setting-review-v1.md).

**Tests**: Required by spec 176's independent tests and success criteria. Write focused behavior tests before implementing each story. T001 is completed by the pinned-package spike. T002–T014 form the implemented import checkpoint; generation and release tasks remain open.

## Phase 1: Setup and package-semantics prerequisite

- [x] T001 Verify object-map, array, explicit-disabled, null/empty, and same-shape overlay behavior against the repository-pinned CShells `0.0.30-preview.159` package in `tests/essentials/Architecture/WorkbenchConfigurationTests.cs`; record unsupported behavior and contract narrowing in `specs/176-composition-file-bridge/research.md` before parser work. Evidence: [#2017](https://github.com/elsa-workflows/elsa-foundation/issues/2017).
- [x] T002 Add the contract's disposable two-shell JSON source, Production/Staging overlays, local `primary` resource and canaries under `tests/essentials/Cli/Tests/Fixtures/CompositionBridge/`; keep actual Workbench files as a separate compatibility sample.
- [x] T003 Validate the optional review-file shape and sample safe `/Flag` and `/Limit` declarations in `tests/essentials/Modularity/Planning/Tests/CompositionBridgeSettingReviewTests.cs` against `specs/176-composition-file-bridge/contracts/setting-review-v1.md`; revise the Draft contract before implementation if a safe scalar cannot be distinguished from unclassified source data.

**Checkpoint**: Pinned-package evidence and a reviewable, secret-canary fixture exist. A discrepancy narrows the Draft contract before development proceeds; it is not papered over in the bridge parser.

## Phase 2: Foundational source and review boundary

- [x] T004 Add invocation-local `SourceSelection`, `SourceSnapshot`, and selected-file identities in `src/essentials/Modularity/Planning/Bridge/SourceSnapshot.cs`; keep physical paths, bytes, and digests out of `AuthoredComposition` in `src/essentials/Modularity/Planning/Models/SelectionDocuments.cs`.
- [x] T005 Implement a raw CShells JSON layer reader with exact feature IDs, object-map disabled state, value types, and base/overlay provenance in `src/essentials/Modularity/Planning/Bridge/CshellsSourceReader.cs`; derive effective array entries by merged numeric indexes, refuse effective duplicates, scalar/object feature overrides, and unsupported cross-shape layers, and never infer array disabled state from `Enabled` or `State` fields.
- [x] T006 Implement strict, deny-by-default setting review parsing and logical-resource reference validation in `src/essentials/Modularity/Planning/Bridge/SettingReviewReader.cs`, including the stable unsafe/unresolved refusal classes from `specs/176-composition-file-bridge/contracts/file-bridge-v1.md`.
- [x] T007 Implement supported-file discovery, symlink/path-escape refusal, all-copied-file content freezing, and recheck in `src/essentials/Cli/CompositionFileSource.cs`; read selected files for effective preview while retaining unselected sibling overlays for output preservation.
- [x] T008 [P] Add parser/snapshot tests for object-map and array layers (including numeric-index overlays, scalar/object feature override refusal, and no inferred array disabled state), false/zero/null/empty raw values, duplicates after layering, changed selected and unselected files, and path escapes in `tests/essentials/Modularity/Planning/Tests/CompositionBridgeSourceTests.cs` and `tests/essentials/Cli/Tests/CompositionFileSourceTests.cs`. Generation's array-removal refusal remains T015.

**Checkpoint**: A source snapshot can be inspected safely without a host, worker, package feed, or database; no authored file or candidate is published yet.

## Phase 3: User Story 1 - Review and accept an existing host (P1)

**Goal**: Preview the selected local files, then explicitly accept a pinned no-profile authored baseline.

**Independent test**: The two-shell fixture reports A enabled, B disabled, Production Flag=true, base Limit=0, masked Future canary, logical `primary`, and unchecked external evidence. Decline writes nothing; acceptance writes strict authored v1 with no package locks or source bundle.

### Tests for User Story 1

- [x] T009 [P] [US1] Add pure import-preview and authored-projection tests, including planner candidate/accepted equality and no invented profile/locks, in `tests/essentials/Modularity/Planning/Tests/CompositionImportTests.cs`.
- [x] T010 [P] [US1] Add real-process import acceptance, non-TTY refusal, cancellation, catalog mismatch, and canary scans over stdout/stderr/authored/plan output in `tests/essentials/Cli/Tests/CompositionImportCliTests.cs`.

### Implementation for User Story 1

- [x] T011 [US1] Build a redacted `ImportPreview` and accepted no-profile `AuthoredComposition` from the selected source, reviewed fields, and `SelectionPlanner.Plan` in `src/essentials/Modularity/Planning/Bridge/CompositionImporter.cs`; keep unknown values local and unresolved findings visible.
- [x] T012 [US1] Register `composition import` without the persistence worker in `src/essentials/Cli/ElsaCli.cs` and implement its arguments, safe rendering, interactive one-preview decision, and stable refusal mapping in `src/essentials/Cli/CompositionImportCommand.cs`.
- [x] T013 [US1] Write the authored v1 document only after acceptance and a fresh all-file snapshot check, using a private adjacent file and no-overwrite move in `src/essentials/Cli/CompositionFilePublisher.cs`; cancellation and I/O refusal leave no published file.
- [x] T014 [US1] Run the independent import acceptance and redaction scenario in `specs/176-composition-file-bridge/quickstart.md` against the two-shell fixture and a disposable copy of `src/apps/Elsa.Workbench/` JSON; record selected-file versus running-host evidence limits. Evidence: real-process CLI tests and disposable Workbench source test; running-host evidence remains unchecked.

**Checkpoint**: User Story 1 works independently as a safe migration entry point. It is useful but does not yet claim that the accepted document can regenerate a host.

## Phase 4: User Story 2 - Generate a reviewed host-file candidate (P2)

**Goal**: Reopen explicit local sources and an accepted document, show a redacted diff, and publish a fresh candidate directory that changes only reviewed paths.

**Independent test**: Edit accepted A.Limit from 0 to 1. A new candidate changes that base field only; tenant-b, B disabled, unknown A.Future, CustomRoot, Production annotation, appsettings nodes, and Staging copies remain intact. Source files remain byte-identical.

### Tests for User Story 2

- [ ] T015 [P] [US2] Add semantic patch/diff tests for existing-layer edits, unknown-node preservation, unreviewed/new-field refusal, resource-name validation, and candidate/accepted selection drift in `tests/essentials/Modularity/Planning/Tests/CompositionCandidateTests.cs`.
- [ ] T016 [P] [US2] Add real-process generation tests for diff approval, non-TTY/cancellation, existing destination, changed selected/unselected file, output failure cleanup, and both canary streams in `tests/essentials/Cli/Tests/CompositionGenerateCliTests.cs`.

### Implementation for User Story 2

- [ ] T017 [US2] Compute a reviewed JSON patch against original source layers and a redacted semantic diff in `src/essentials/Modularity/Planning/Bridge/CompositionCandidateBuilder.cs`; never infer a target layer for a new field or reconstruct host files from `SelectionPlan`.
- [ ] T018 [US2] Register `composition generate` without the persistence worker in `src/essentials/Cli/ElsaCli.cs` and implement strict authored/catalog/review validation plus one explicit diff decision in `src/essentials/Cli/CompositionGenerateCommand.cs`.
- [ ] T019 [US2] Copy all supported frozen files to private same-volume staging, patch only approved paths, recheck every source file, and no-overwrite rename a complete fresh directory in `src/essentials/Cli/CompositionFilePublisher.cs`; clean staging on any refusal.
- [ ] T020 [US2] Run the full fixture before/after comparison and Workbench preservation sample in `specs/176-composition-file-bridge/quickstart.md`, including byte-identical source checks and parsed-subtree equality outside reviewed paths.

**Checkpoint**: Import and generation are separately reviewable and atomic. A generated bundle is a candidate, not a validated runtime or in-place apply.

## Phase 5: Polish and delivery gates

- [ ] T021 [P] Add one shared safe rendering/refusal helper only where import and generate duplicate logic in `src/essentials/Cli/CompositionFileBridgeOutput.cs`; keep raw exceptions, paths, and source excerpts out of diagnostics.
- [ ] T022 Run the whole affected Planning and CLI test projects plus `tests/essentials/Architecture/Elsa.Architecture.Tests.csproj`, refresh/check maps, check solution filters and `git diff --check`, and review the source/output/canary diff in the implementation worktree.
- [ ] T023 Mutate and restore one redaction or source-change assertion in `tests/essentials/Cli/Tests/CompositionGenerateCliTests.cs`, show the focused test fails then passes, and record exact-head evidence on the implementation PR and linked issue before merge.
- [ ] T024 Update `specs/176-composition-file-bridge/spec.md` status and `docs/program-goals/feature-composition-readiness.md` only after both stories and the file-only boundary are actually delivered; do not claim runtime, package, EF, or database readiness.

## Dependencies and execution order

T001 gated T004–T020 and narrowed the Draft source-shape contract. T002–T003 establish the disposable test and safety inputs. T004–T008 build the shared source boundary before either story. T009–T014 complete US1; T015–T020 use its accepted document and source model for US2. T021–T024 follow the desired story scope. Within each story, tests precede implementation and must first demonstrate the missing behavior. The complete first release candidate is **US1 + US2 together**, because only that combination proves the source-preserving round trip promised by spec 176; US1 is an independently testable checkpoint, not an export claim.

## Parallel opportunities

T002 fixture authoring and T003 review-shape tests can proceed alongside T001 evidence gathering, but no parser implementation starts until T001's result is reconciled. T008 parser and filesystem test files can be authored independently after T004–T007 interfaces are agreed. T009/T010 and T015/T016 each target different test projects and can be prepared in parallel within their story. T021 can proceed after both commands stabilize. The program's one-active-leaf rule still governs GitHub delivery; task-level parallelism does not authorize competing PRs for the same slice.

## Implementation strategy

First close T001's pinned-package evidence and any needed Draft-contract correction. Build the shared snapshot/parser, then prove US1 on the synthetic fixture and actual Workbench source layout. Add US2's candidate patch/publication against the same frozen source model. Open one scoped implementation issue and PR for the end-to-end first slice only when the package-shape gate is resolved; keep live host checking and in-place apply for their own issues. Verify with focused Planning/CLI tests and the repo gates. Do not assign the 24 EF container suites to a file-only change unless its dependency graph actually reaches EF; main's full matrix is a separate post-merge gate.

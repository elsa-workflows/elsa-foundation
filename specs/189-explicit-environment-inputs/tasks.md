---

description: "Dependency-ordered implementation and proof tasks for explicit private environment inputs"
---

# Tasks: Explicit Private Environment Inputs

**Input**: Frozen design documents in `specs/189-explicit-environment-inputs/`

**Prerequisites**: `spec.md`, `plan.md`, `research.md`, `data-model.md`, `quickstart.md`, and the four contract documents in `contracts/`

**Scope**: One additive production leaf covering all three P1 stories. Existing projects and fixtures are extended; no new project, provider, permanent test cadence, or transport is introduced.

**Evidence status**: Implementation and local configuration/operator proof are complete under #2292 after #2277 authoring and #2282 correction delivery. T001–T045 have reviewed implementation and executed evidence in [implementation-evidence.md](implementation-evidence.md), including the direct worker-operation coverage added after review reopened T032. Hosted delivery task T046 remains open. The proposed Implemented status ships with the implementation merge under the canonical spec lifecycle; it does not claim that PR #2296 has already merged or that resulting-main gates have passed.

## Phase 1: Setup

**Purpose**: Establish the additive source seams and shared proof fixtures while preserving closed candidate-v1 and WorkerContract v2 boundaries.

- [x] T001 Record the additive ownership map and compatibility seams in `src/essentials/Cli/Worker/WorkerContract.cs`, `src/essentials/Persistence/EntityFramework/Tooling/EfCandidateInspectionContract.cs`, `src/essentials/Persistence/EntityFramework/Tooling/EfToolingContract.cs`, and `src/essentials/Persistence/EntityFramework/Tooling/EfToolingHost.cs`; preserve the old request, method, and response shapes.
- [x] T002 [P] Add the version-1 additive capability declaration in `src/essentials/Persistence/EntityFramework/Tooling/EfCandidateEnvironmentInspectionContract.cs` and the exact assembly enrollment attribute contract in `src/essentials/Persistence/EntityFramework/Tooling/EfCandidateEnvironmentInputsAttribute.cs` (`AttributeUsage` for assemblies, `(int version, string policy)` constructor, read-only `Version`/`Policy`, no named declaration arguments).
- [x] T003 Add the reviewed Workbench enrollment declaration in `src/apps/Elsa.Workbench/WorkbenchEfToolingShellDefaults.cs` and preserve the existing source ordering in `src/apps/Elsa.Workbench/Program.cs` after T002 defines the attribute; do not enroll Foundation Host or infer enrollment from a host name.
- [x] T004 [P] Add shared test constants, safe canaries, capture IDs, and boundary builders to `tests/essentials/Cli/Tests/CandidateInspectionFixture.cs` without persisting private values or creating a new test project.

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Implement the closed protocol, ownership, loading, enrollment, and lifecycle foundations required by every story.

**Checkpoint**: The old candidate-v1/file-only lane remains closed, and the new lane has bounded private capture, exact capability negotiation, loader-only host loading, metadata-only enrollment validation, and a lane-aware response boundary before story behavior is added.

- [x] T005 [P] Add `HostClosure.LoadHostAssemblyForInspection(string hostDirectory, string hostName): Assembly` in `src/essentials/Cli/Worker/HostClosure.cs`; validate only the actual assembly name and location against the selected closure, preserve the existing public `void LoadHostAssembly(...)` signature, and share private loading code without scanning arbitrary loaded assemblies.
- [x] T006 [P] Implement exact reflective capability binding in `src/essentials/Cli/Worker/ToolingEntryPoint.cs`, orchestrated by the existing worker operation after it resolves the selected persistence assembly through the installed closure, and add separate metadata-only `ToolingEntryPoint.ValidateCandidateEnvironmentEnrollment(Assembly hostAssembly, Assembly persistenceAssembly): void` in `src/essentials/Cli/Worker/ToolingEntryPoint.cs`; bind the exact non-generic stream/stream/cancellation `Task<int>` capability first, then inspect `CustomAttributeData` for one exact attribute identity, constructor `(1, "workbench-json-explicit-environment-v1")`, no named arguments, and no duplicate declaration.
- [x] T007 Define the closed outer `CandidateEnvironmentWorkerRequestV2` shape and parser in `src/essentials/Cli/Worker/WorkerContract.cs` without EF references; define the inner `CandidateEnvironmentHostRequestV1` and lane response/error shapes in `src/essentials/Persistence/EntityFramework/Tooling/EfCandidateEnvironmentInspectionOperation.cs`; keep outer WorkerContract v2, old response semantics, correlation, fixed classifications, JSON depth, and serialized byte bounds independently validated at every nested object.
- [x] T008 [P] Extend private ownership and drift state in `src/essentials/Cli/CompositionInspectionCapture.cs` and `src/essentials/Cli/CompositionInputSnapshot.cs`; capture one bounded regular file as immutable owned bytes, preserve the operator-owned original, bind candidate/context/capture IDs, support stable detached reload after defensive copies, and refuse drift, mixed generation, disposed/reused state, cancellation, timeout, or cleanup failure without claiming physical memory erasure.
- [x] T009 Audit every old command reader and validator in `src/essentials/Cli/Worker/WorkerContract.cs`, `src/essentials/Cli/Worker/WorkerRunner.cs`, `src/essentials/Cli/Worker/CandidateWorkerOperation.cs`, and `src/essentials/Persistence/EntityFramework/Tooling/EfCandidateInspectionOperation.cs` so an added `environmentInput` field, including explicit `null`, is rejected by the old path and never falls back to ambient input.
- [x] T010 Add the separate lane-aware response validator and fixed local refusal mapping in `src/essentials/Persistence/EntityFramework/Tooling/EfCandidateEnvironmentInspectionOperation.cs`, `src/essentials/Cli/Worker/CandidateWorkerOperation.cs`, and `src/essentials/Cli/Worker/ToolingEntryPoint.cs`; inherit all closed safe-response semantics from Spec 187 while accepting only the new source/external-input state and the fixed correlated unenrolled-host exit-3 response.
- [x] T011 Preserve bounded owned-child execution and cleanup in `src/essentials/Cli/CandidateWorkerProcess.cs` and `src/essentials/Cli/Worker/CandidateWorkerOperation.cs`; transmit document bytes over bounded streams, keep private values and private-input paths out of child arguments, make no general sandbox promise, and never launch the selected host entry point or runtime startup.

## Phase 3: User Story 1 - Inspect an explicitly supplied intended environment (Priority: P1)

**Goal**: Let an operator opt into one explicit private overlay bound to one selected host, shell, environment, and invocation, using the enrolled Workbench policy and safe output only.

**Independent test**: Through the public CLI wrapper and the built `src/apps/Elsa.Workbench/Elsa.Workbench.csproj` closure, provide a valid overlay containing a blank value and an ordinary `ConnectionStrings__` key. Verify the supported source order and safe intended-input projection, while proving that ambient/command-line/custom/deployed/physical sources and private canaries do not enter the result. This is an internal proof checkpoint; it is not a deployment or MVP claim, and it does not replace the recovery and compatibility stories.

### Tests for User Story 1

- [x] T012 [P] [US1] Add raw-document admission tests in `tests/essentials/Cli/Tests/CompositionInspectionCaptureTests.cs` for strict UTF-8/BOM handling, closed properties, integer version, valid Unicode scalar keys, blank/newline/tab values, null/tombstone/removal rejection, NUL/control/`=`/surrogate rejection, omission, duplicate raw keys, case aliases, `__`/`:` collisions, and no trimming/case-folding/Unicode normalization.
- [x] T013 [P] [US1] Add capability, envelope, loader, and metadata-enrollment contract tests in `tests/essentials/Cli/Tests/ToolingEntryPointTests.cs` and `tests/essentials/Cli/Tests/WorkerProtocolTests.cs`; cover loader name/location-only behavior, capability-before-enrollment precedence, wrong signature/version, exact attribute identity/constructor/policy/cardinality, unenrolled Foundation Host, and old-reader rejection of an added field including `null`.
- [x] T014 [P] [US1] Add host source-order, explicit-overlay, no-ambient-source, no-runtime-effect, and safe-projection cases in `tests/essentials/Persistence/EntityFrameworkCore/Migrations/Tests/EfCandidateInspectionTests.cs`; assert `captured-workbench-json-explicit-environment-v1`, `externalInputs:supplied-intended`, and unavailable/unverified states without private values or provenance. Direct host requests must independently refuse invalid UTF-8/BOM, duplicate/unknown fields, raw key/value/count/encoded bounds, null/tombstone forms, invalid characters, alias collisions and each of the eleven prefixes; no client-normalized map is trusted. Exercise host identity, capability/version and enrollment rechecks before configuration/composer creation.

### Implementation for User Story 1

- [x] T015 [US1] Add `--environment-input <path>` admission and omission-preserving compatibility branching to `src/essentials/Cli/CompositionInspectCommand.cs`; accept only one private regular file for `composition inspect`, retain `--trust-host-code`, and never add inline, ambient, command-line, custom-provider, restore, save, or portable-export behavior.
- [x] T016 [US1] Implement strict raw overlay capture, parsing, UTF-8 byte counting, `__` normalization, OrdinalIgnoreCase collision detection, ordinary `ConnectionStrings__<name>` support, and all eleven case-insensitive service-prefix refusals in `src/essentials/Cli/CompositionInspectionCapture.cs`, `src/essentials/Cli/CompositionInputSnapshot.cs`, and `src/essentials/Cli/Worker/WorkerContract.cs`; share CLI/worker admission within the EF-free assembly, independently validate new worker inputs, and preserve original bytes for transport and drift checks.
- [x] T017 [US1] Implement the additive capability and public host wrapper in `src/essentials/Persistence/EntityFramework/Tooling/EfCandidateEnvironmentInspectionContract.cs` and `src/essentials/Persistence/EntityFramework/Tooling/EfToolingHost.cs`; expose `RunCandidateEnvironmentInspectionAsync(Stream, Stream, CancellationToken)` without changing `EfCandidateInspectionContract`, `EfToolingContract`, or old public entry points.
- [x] T018 [US1] Implement the `inspect-candidate-environment` worker branch and private outer/inner envelope exchange in `src/essentials/Cli/Worker/WorkerRunner.cs`, `src/essentials/Cli/Worker/CandidateWorkerOperation.cs`, and `src/essentials/Cli/CandidateWorkerProcess.cs`; load the actual host, resolve the persistence assembly, bind capability before enrollment, invoke the host operation through bounded streams, and correlate the immutable candidate/environment capture.
- [x] T019 [US1] Implement the enrolled Workbench host operation in `src/essentials/Persistence/EntityFramework/Tooling/EfCandidateEnvironmentInspectionOperation.cs`; independently validate the raw document grammar, strict shape, normalization, collisions, all eleven prefixes and byte bounds before configuration construction, recheck actual host name/location, selected persistence-assembly capability/version and equivalent enrollment metadata immediately before configuration/composer creation, without a CLI dependency, and apply the explicit overlay after appsettings base/environment and shells base/environment, use the existing host configuration builder and `EfToolingConfigurationContext.ComposeShell`, reconcile selections before preparation, avoid `CompositionCandidateBuilder`, call `EfPersistencePreparation.Prepare` through `src/essentials/Persistence/EntityFramework/ResourceResolution/EfPersistencePreparation.cs` with `verifyConnectionValues: true`, and emit the safe closed projection without starting services, a host entry point, a DbContext, migrations, publication, or save.
- [x] T020 [US1] Exercise the actual built Workbench closure through the public process wrapper in `tests/essentials/Cli/Tests/DotnetElsa.cs` and `tests/essentials/Cli/Tests/CandidateInspectionTests.cs`; require the real Workbench enrollment declaration and public wrapper path, retain fixture hosts only as companion controls, and verify blank values, ordinary `ConnectionStrings__` values, eleven prefix refusals, and ambient-source exclusion. Repeat the same supported input/context and representative invalid input, comparing safe results and fixed refusal classifications across fresh invocations.
- [x] T021 [US1] Add public output assertions in `tests/essentials/Cli/Tests/CandidateInspectionOutputTests.cs` for safe logical identities, intended-input labeling, truthful unverified/not-performed evidence, fixed diagnostics, and absence of raw values, raw configuration, private-input paths, exception excerpts, exact-file/key/provider provenance, deployment/readiness claims, and private-input-derived fingerprints.

**Checkpoint**: The explicit inspection path is internally provable through the actual Workbench closure and public wrapper, but delivery remains blocked until User Stories 2 and 3 plus the cross-cutting gates are complete.

## Phase 4: User Story 2 - Reconcile environment-driven selection before preparation (Priority: P1)

**Goal**: Refuse selection divergence before persistence preparation, preserve valid authored removals, and provide the existing edit/accept/fresh-capture recovery path.

**Independent test**: Supply an overlay that changes effective selection or leaves an accepted feature active after authored removal. Verify refusal before preparation and unchanged accepted files. Edit authored intent, run existing interactive `composition accept`, capture again, and verify a matching inspection uses the same host policy and captured input.

### Tests for User Story 2

- [x] T022 [P] [US2] Add accepted/requested/effective/disabled/implicit selection, valid-removal, stale-identity, active-removal, and required-edge conflict cases in `tests/essentials/Persistence/EntityFrameworkCore/Migrations/Tests/EfCandidateInspectionTests.cs`; assert refusal precedes `EfPersistencePreparation` and accepted intent is not silently rewritten.
- [x] T023 [P] [US2] Add feature graph and required-edge reconciliation cases in `tests/essentials/Modularity/Planning/Tests/CompositionCandidateTests.cs` and `tests/essentials/Modularity/Planning/Tests/SelectionExpansionTests.cs`; keep existing feature identity and authored removal semantics intact.
- [x] T024 [P] [US2] Add the actual Workbench external-toggle recovery journey in `tests/essentials/Cli/Tests/CandidateInspectionTests.cs`, retaining existing accept/lifecycle companion controls in `CompositionAcceptCliTests.cs` and `CandidateInspectionLifecycleTests.cs`; cover refusal, authored edit, existing interactive `composition accept`, fresh accepted file, fresh immutable capture, and successful matching inspection without silent acceptance.

### Implementation for User Story 2

- [x] T025 [US2] Complete the T019 host-owned reconciliation path in `src/essentials/Persistence/EntityFramework/Tooling/EfCandidateEnvironmentInspectionOperation.cs`; preserve valid authored removals and add refusal for divergent accepted/requested/effective/disabled/implicit sets, stale identities, active removals, and required-edge conflicts before the single existing `EfPersistencePreparation` call, without creating a second resolver or preparation path.
- [x] T026 [US2] Add same-capture runtime-preparer parity assertions and mutation/reload handling in `tests/essentials/Persistence/EntityFrameworkCore/Migrations/Tests/EfCandidateInspectionTests.cs` and `tests/essentials/Persistence/EntityFrameworkCore/Migrations/Tests/EfToolingHostTests.cs`; prove the actual `EfPersistencePreparation.Prepare(..., verifyConnectionValues: true)` path consumes the same frozen overlay and configuration policy rather than a frontend duplicate or reread, then temporarily remove the real overlay application or reconciliation gate, verify the focused parity/refusal test fails, restore the guard, and rerun it as a causal mutation/revert bite-proof.
- [x] T027 [US2] Wire fixed selection/refusal outcomes and the existing recovery handoff through `src/essentials/Cli/CompositionInspectCommand.cs` and `src/essentials/Persistence/EntityFramework/Tooling/EfCandidateEnvironmentInspectionOperation.cs`; do not add a recovery command, ambient acceptance, or automatic rewrite of accepted files.

**Checkpoint**: A successful selection result is only valid after one host-owned reconciliation and the existing preparer consumes the same captured input; divergence and recovery remain explicit operator actions.

## Phase 5: User Story 3 - Preserve compatibility and private review boundaries (Priority: P1)

**Goal**: Keep candidate-v1/file-only behavior closed and stable while making unsupported capabilities, unsafe inputs, privacy violations, cancellation, and side effects explicit refusals.

**Independent test**: Run the old command without `--environment-input` and the new negotiated command through the actual Workbench public wrapper. Verify old behavior is unchanged, unsupported hosts/capabilities refuse without ambient fallback, private canaries are absent from every allowed observation surface, input files remain unchanged, and owned cleanup completes under cancellation and timeout.

### Tests for User Story 3

- [x] T028 [P] [US3] Add old-command closed-shape tests in `tests/essentials/Cli/Tests/WorkerProtocolTests.cs` for explicit `environmentInput:null`, unknown nested fields, wrong versions, and old candidate-v1 response validation; assert no shared DTO widening changes the old lane.
- [x] T029 [P] [US3] Add old file-only and capability-negotiation compatibility cases in `tests/essentials/Cli/Tests/CandidateWorkerOperationTests.cs` and `tests/essentials/Cli/Tests/ToolingEntryPointTests.cs`; assert unsupported capability/version returns fixed capability-unavailable behavior with no legacy fallback.
- [x] T030 [P] [US3] Add distinct public-identity/private-value canaries (including syntactically safe private strings), unknown overlay-only error-identity refusal controls, and output/artifact assertions in `tests/essentials/Cli/Tests/CandidateInspectionOutputTests.cs`; inspect public JSON/text, diagnostics, generated/public files, exception handling, and preserved operator-owned input bytes while allowing only finite existing CLI location and loader/deps/package-root metadata where required by file checks/loading.
- [x] T031 [P] [US3] Add process-argument and stderr/stdout canary assertions in `tests/essentials/Cli/Tests/CandidateProcessTests.cs`; prove values, raw configuration, private-input paths, secret-bearing excerpts, and fingerprints derived from private inputs never cross the child argument or public diagnostic boundary.
- [x] T032 [US3] Add cancellation, timeout, disposed/reused capture, source-drift, malformed-response, and bounded-cleanup cases in `tests/essentials/Cli/Tests/CandidateProcessOwnerTests.cs`, `tests/essentials/Cli/Tests/CandidateInspectionLifecycleTests.cs`, and `tests/essentials/Cli/Tests/CandidateWorkerOperationTests.cs`; cover stdin write, response read, post-capture/prelaunch cancellation, cleanup cancellation, and no surviving worker or partial result.

### Implementation for User Story 3

- [x] T033 [US3] Preserve the omitted-flag file-only path and old response validator in `src/essentials/Cli/CompositionInspectCommand.cs` and `src/essentials/Persistence/EntityFramework/Tooling/EfCandidateInspectionOperation.cs`; ensure the new flag negotiates only the additive capability and never changes candidate-v1 semantics.
- [x] T034 [US3] Complete lane-specific fixed-code and diagnostic redaction handling in `src/essentials/Cli/Worker/CandidateWorkerOperation.cs`, `src/essentials/Cli/Worker/ToolingEntryPoint.cs`, and `src/essentials/Persistence/EntityFramework/Tooling/EfCandidateEnvironmentInspectionOperation.cs`; emit optional error identities only for canonical declared public accepted/source/known-host identities, omit unknown overlay-only identities even when syntactically safe, and keep capability-unavailable, input-invalid/too-large, collision, prefix, host-unenrolled, stale-response, timeout, cancellation, and cleanup classifications stable without peer exception text.
- [x] T035 [US3] Add the compatibility control to `tests/essentials/Cli/Tests/DotnetElsa.cs` and `tests/essentials/Cli/Tests/CandidateInspectionTests.cs`; run the old no-option command against actual built Workbench output and an unenrolled host control, proving file-only behavior and explicit refusal without ambient or command-line fallback.

**Checkpoint**: Both lanes have independently testable compatibility and privacy boundaries, but no production delivery claim is made until numeric, architecture, map, and hosted gate evidence is collected.

## Phase 6: Polish and Cross-Cutting Proof

**Purpose**: Close the full FR/SC matrix, documentation, architecture, maps, and gated delivery evidence without adding projects, providers, cadence, or unsupported claims.

- [x] T036 [P] Add exact-bound and one-over cases to existing tests in `tests/essentials/Cli/Tests/CandidateInspectionTests.cs` and `tests/essentials/Cli/Tests/CompositionInspectionCaptureTests.cs` for raw 1 MiB, selected file 1 MiB, selected aggregate 4 MiB, key 1,024 UTF-8 bytes, value 65,536 UTF-8 bytes, 1,024 entries, 4,096 selection IDs, JSON depth 64, timeout endpoints, and normalized-length redundancy; reuse existing selected-file/aggregate/depth coverage, identify compound aggregate refusals and shallow-shape redundancy as the proof matrix requires, and calculate encoded bytes before allocation.
- [x] T037 [P] Add combined participant/finding 1,022+2 accepted and 1,023+2 refused cases plus exact/one-over 8 MiB serialized request and 4 MiB response cases in `tests/essentials/Cli/Tests/CandidateWorkerResponseTests.cs` and `tests/essentials/Persistence/EntityFrameworkCore/Migrations/Tests/EfCandidateInspectionTests.cs`; keep final serialized limits authoritative over per-part limits.
- [x] T038 Add trusted-host/no-side-effect assertions in `tests/essentials/Cli/Tests/CandidateProcessTests.cs` and `tests/essentials/Persistence/EntityFrameworkCore/Migrations/Tests/EfCandidateInspectionTests.cs`; prove inspection does not start services, create DB files, connect, migrate, publish, or save. Keep the existing real-DB/provider project unchanged and classify it only as separate persistence evidence in T043.
- [x] T039 [P] Update the exact `PilotSources` inventory in `tests/essentials/Architecture/SecretsEfPersistencePilotArchitectureTests.cs` for the three new EF source files from T002/T019 (`EfCandidateEnvironmentInspectionContract.cs`, `EfCandidateEnvironmentInspectionOperation.cs`, and `EfCandidateEnvironmentInputsAttribute.cs`, all under `src/essentials/Persistence/EntityFramework/Tooling`), preserving ordinal ordering and exact inventory enforcement. Extend the existing architecture/dependency guard through `tests/essentials/Architecture/Elsa.Architecture.Tests.csproj` only with a missing useful boundary assertion; verify the worker remains EF-free, the host owns composition/reconciliation/preparation, public old seams remain compatible, and do not add boilerplate mirror tests or a new architecture suite.
- [x] T040 Update the owning catalog `src/essentials/Persistence/EntityFramework/EXTENSION_POINTS.md` in the implementation unit with the independently versioned environment-inspection capability, exact public host seam, enrollment marker and compatibility/trust boundaries required by framework §2.22.1, before T044 regenerates maps. Update the proposed additive ADR D1/D4/D7 implementation record in `specs/189-explicit-environment-inputs/contracts/adr0076-extension.md` and preserve the existing ADR source `docs/adr/0076-persistence-tooling-runs-inside-the-host-closure.md` without rewriting unrelated decisions or claiming acceptance before gates.
- [x] T041 Reconcile implementation paths, planned commands, proof ownership, and unchecked evidence statements in `specs/189-explicit-environment-inputs/quickstart.md` and `specs/189-explicit-environment-inputs/contracts/acceptance-proof-matrix.md`; retain actual built Workbench/public-wrapper requirements, recovery boundaries, allowed path metadata exceptions, and the distinction between planned and executed evidence.
- [x] T042 Run the existing affected project checks recorded by `specs/189-explicit-environment-inputs/quickstart.md`: `tests/essentials/Cli/Tests/Elsa.Cli.Tests.csproj`, `tests/essentials/Modularity/Planning/Tests/Elsa.Modularity.Planning.Tests.csproj`, `tests/essentials/Persistence/EntityFrameworkCore/Migrations/Tests/Elsa.Persistence.EntityFrameworkCore.Migrations.Tests.csproj`, and `tests/essentials/Architecture/Elsa.Architecture.Tests.csproj`; record exact outcomes without converting skipped or fixture-only cases into passes.
- [x] T043 Run the separate provider acceptance project `tests/essentials/Persistence/EntityFrameworkCore/CliAcceptance/ProviderTests/Elsa.Persistence.EntityFrameworkCore.CliAcceptance.ProviderTests.csproj` only as its existing real database/provider regression surface and keep its result separate from configuration-only inspection proof.
- [x] T044 Run `dotnet run --project tools/maps/Elsa.Maps.Generator -- check`, inspect `docs/maps/manifest.json`, and, after an authorized refresh only, review the generated findings and stage every changed map explicitly, including `docs/maps/manifest.json`; do not claim map freshness without the check.
- [x] T045 Review the complete diff and task-to-proof matrix against `specs/189-explicit-environment-inputs/spec.md`, `plan.md`, `data-model.md`, and `contracts/acceptance-proof-matrix.md`; verify all private-value/path/fingerprint canaries, old-reader rejection, loader/capability/enrollment order, recovery, and no-side-effect boundaries before opening a gated PR.
- [ ] T046 Publish implementation evidence through the gated pull-request checks and verify the resulting-main CI and maps workflows from `.github/workflows/ci.yml` and `.github/workflows/maps.yml`; update `specs/189-explicit-environment-inputs/spec.md` to `Implemented` only in the actual implementation-delivery PR after its evidence is complete, never in this authoring unit, and report exact current-head/resulting-main outcomes without inventing a PR/issue number or claiming skipped proof passed.

## Requirement and success-criteria coverage

| Requirement or outcome | Tasks |
|---|---|
| FR-001 explicit opt-in and host/shell/environment/invocation binding | T008, T015, T018, T020 |
| FR-002 supplied-only source policy | T016, T019, T020, T021 |
| FR-003 one immutable capture and drift checks | T008, T012, T026, T032 |
| FR-004 Workbench ordering and explicit enrollment | T003, T005, T006, T014, T018, T019 |
| FR-005 candidate-v1/file-only compatibility and negotiation | T001, T009, T017, T028, T029, T033, T035 |
| FR-006 deterministic raw-key grammar/collision admission | T012, T016, T036 |
| FR-007 blank/null/tombstone/omission semantics | T012, T016, T020, T036 |
| FR-008 ordinary `ConnectionStrings__` and all eleven prefix refusals | T012, T016, T020, T036 |
| FR-009 reconciliation before preparation and valid removals | T022, T023, T025, T026 |
| FR-010 edit/accept/fresh-capture recovery | T024, T027 |
| FR-011 same host policy and captured input | T019, T025, T026 |
| FR-012 truthful safe evidence states | T010, T019, T021, T034 |
| FR-013 privacy, original-file preservation, no private fingerprints | T008, T011, T021, T030, T031 |
| FR-014 unsupported/malformed/stale/changed refusal | T007, T009, T010, T032, T036, T037 |
| FR-015 trusted selected-host boundary without sandbox claim | T011, T018, T038 |
| FR-016 no runtime/DB effects and bounded cancellation cleanup | T011, T032, T038 |
| FR-017 repeatability and mixed-generation refusal | T008, T020, T026, T032, T036 |
| SC-001 supported Workbench result from supplied input only | T014, T019, T020, T042 |
| SC-002 old compatibility and explicit unsupported refusal | T028, T029, T033, T035, T046 |
| SC-003 zero canaries/fingerprints and unchanged source files | T021, T030, T031, T038, T045 |
| SC-004 stable input-class and boundary outcomes | T012, T016, T036, T037 |
| SC-005 refusal plus edit/accept/fresh-capture recovery | T022, T024, T025, T027, T042 |
| SC-006 no partial result/side effect for adverse lifecycle cases | T032, T036, T037, T038 |
| SC-007 truthful intended-input and unavailable evidence | T010, T019, T021, T041 |

## Dependencies and execution order

### Phase dependencies

- Setup T001–T004 establishes additive seams and shared fixture vocabulary.
- Foundational T005–T011 depends on Setup and blocks all stories.
- US1 T012–T021 depends on Foundational and establishes the explicit inspection lane.
- US2 T022–T027 depends on US1's host operation and capture binding because recovery must exercise the same lane.
- US3 T028–T035 may begin compatibility test authoring after Foundational, but its integration and checkpoint depend on US1's negotiated lane.
- Cross-cutting T036–T046 starts only after both US2 and US3 complete; all three stories are required before final gates.
- Polish T036–T046 depends on the implemented US1–US3 leaf; hosted PR/resulting-main evidence is last.

### User story dependency graph

```text
Setup
  -> Foundational
       -> US1 explicit inspection
            -> US2 selection reconciliation/recovery ─┐
            -> US3 compatibility/privacy/lifecycle ──┴─> Cross-cutting proof, maps, gated PR, resulting-main
```

### Honest parallel opportunities

- After Setup, T005, T006, and T008 can proceed in parallel because they touch distinct loader, metadata-validator, and capture files; T009 and T011 follow the shared transport decisions where they overlap.
- Within US1, T012, T013, and T014 can be authored in parallel because they touch distinct CLI, protocol, and EF migration test files; implementation tasks T015–T019 follow those contracts.
- Within US2, T022, T023, and T024 can be authored in parallel in their distinct migration, planning, and CLI acceptance files; T025–T027 integrate them sequentially.
- Within US3, T028–T031 can be authored in parallel in distinct protocol/output/process test files; T032–T035 then integrate lifecycle and compatibility behavior.
- In the final phase, T036, T037, and T039 can proceed in parallel across boundary, response, and architecture proof surfaces. T038 follows T037 because both touch `EfCandidateInspectionTests.cs`; documentation and hosted gates follow the resulting implementation.

### Concrete per-story parallel examples

- **US1**: T012 (`CompositionInspectionCaptureTests.cs`), T013 (`ToolingEntryPointTests.cs`/`WorkerProtocolTests.cs`), and T014 (`EfCandidateInspectionTests.cs`) can be authored together; T015–T019 then implement their distinct production seams.
- **US2**: T022 (`EfCandidateInspectionTests.cs`), T023 (`CompositionCandidateTests.cs`/`SelectionExpansionTests.cs`), and T024 (`CompositionAcceptCliTests.cs`/`CandidateInspectionLifecycleTests.cs`) can be authored together before T025–T027 integrate the single host path.
- **US3**: T028 (`WorkerProtocolTests.cs`), T030 (`CandidateInspectionOutputTests.cs`), and T031 (`CandidateProcessTests.cs`) can be authored together; T029 and T032–T035 then complete compatibility and lifecycle integration.

## Implementation strategy

This feature is one coherent production leaf. The US1 checkpoint is an internal proof milestone only; it is not an MVP, deployment, or release claim because privacy, recovery, compatibility, numeric boundaries, and resulting-main gates remain outstanding. Complete Setup and Foundational first, then implement US1, US2, and US3 as one reviewed change set before any final gate. Finish the complete existing-project proof matrix, separate real database/provider regression evidence from configuration-only inspection, run the maps check, and publish only the evidence actually returned by gated PR and resulting-main workflows.

## Notes

- Unchecked tasks describe remaining work. Checked tasks have executed evidence recorded in [implementation-evidence.md](implementation-evidence.md).
- `[P]` appears only where the marked work uses distinct files and has no incomplete dependency on another marked task.
- Existing explicit CLI file locations and finite assembly-loader/deps/package-root metadata remain allowed implementation metadata for current file checks/loading; private values, raw configuration, private-input paths in public diagnostics/results, and private-input-derived fingerprints remain prohibited.
- No task creates a new project, provider, cadence, transport, acceptance command, or ambient environment reader.

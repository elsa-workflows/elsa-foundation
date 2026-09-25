# Tasks: File-deployed composition activation and recovery

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md), [activation contract](contracts/activation-v1.md), and [quickstart](quickstart.md).

**Tests**: Required by the independent tests and success criteria in spec 177. Write a focused failing behavior test before each implementation slice. The list is future implementation work; #2036 itself delivers only specification artifacts.

**Readiness**: T001–T009 (US1) are delivered by #2038. T010–T011 are the #2039 host-attestation spike; its current-host decision is no-go for exact candidate matching. #2041 delivers only T013–T014's safe default-shell reload/readback subset. T012 and T015–T021 still require separate deployment-receipt, marker, and recovery work. No current implementation may claim candidate-match verification.

## Phase 1: Setup and fixture

- [X] T001 Extend the disposable two-shell base/Production/Staging fixture in `tests/essentials/Cli/Tests/Fixtures/CompositionBridge/` with explicit default-shell selection, a second copied shell, connection and unknown-field canaries, and a candidate file changed after review; keep the original spec 176 fixture behavior intact.
- [X] T002 Add a safe handoff JSON example and expected refusal/outcome cases to `specs/177-file-deployed-activation/contracts/activation-v1.md` and `specs/177-file-deployed-activation/quickstart.md` after validating them against the fixture; do not add an unkeyed source-file digest.

**Checkpoint**: One fixture has complete file roles and canaries before new code projects an identity.

## Phase 2: Foundational candidate boundary

- [X] T003 Re-run the existing selected/unselected change and publication tests in `tests/essentials/Cli/Tests/CompositionFileSourceTests.cs` and `tests/essentials/Cli/Tests/CompositionGenerateCliTests.cs`, then record any fixture mismatch in `specs/177-file-deployed-activation/research.md` before altering the bridge.
- [X] T004 Compare the proposed handoff fields in `specs/177-file-deployed-activation/contracts/activation-v1.md` with the strict authored v1 schema in `src/essentials/Modularity/Planning/Models/SelectionDocuments.cs`; document the separate-output boundary in `specs/177-file-deployed-activation/plan.md` so no new top-level authored field is inferred.

**Checkpoint**: Existing file-source behavior and the separate handoff output are confirmed before new behavior tests. No new shared infrastructure is needed for US1.

## Phase 3: User Story 1 - Hand off a reviewed candidate (P1) MVP

**Goal**: After a successful reviewed generation, emit a safe opaque handoff for the same complete candidate; a changed file refuses and no runtime-ready claim is made.

**Independent test**: Use the two-shell fixture. All included files appear by safe role, each changed-file injection refuses, both canaries occur zero times in outward output, and no host/package/database path is called.

### Tests for User Story 1

- [X] T005 [P] [US1] Write failing pure tests for opaque ID scope, catalog/accepted selection, repeated file roles, unresolved facts and zero raw values in `tests/essentials/Modularity/Planning/Tests/CompositionHandoffTests.cs`.
- [X] T006 [P] [US1] Write failing real-process tests for selected/unselected file changes, cancellation, handoff publication and canary scans in `tests/essentials/Cli/Tests/CompositionGenerateCliTests.cs`.

### Implementation for User Story 1

- [X] T007 [US1] Implement the opaque-ID, safe file-role and unchecked-finding projection from the reviewed candidate in `src/essentials/Modularity/Planning/Bridge/CompositionHandoff.cs`; keep `AuthoredComposition` v1 unchanged and never derive a shareable raw-file hash.
- [X] T008 [US1] Integrate handoff generation after the existing diff approval and candidate publication in `src/essentials/Cli/CompositionGenerateCommand.cs`; recheck both the source through `CompositionFileSource.VerifyUnchanged` and every published candidate file against the reviewed in-memory bytes through `src/essentials/Cli/CompositionHandoffFileVerifier.cs` before reporting success.
- [X] T009 [US1] Run the handoff fixture and redaction cases in `specs/177-file-deployed-activation/quickstart.md`; compare source/candidate trees and assert no host reload, management-save, package or database call.

**Checkpoint**: US1 is useful by itself as an operator handoff. It is a candidate label, not a deployment receipt or active-host claim. Complete its own PR before moving the program lane to the host gate.

## Phase 4: Host-attestation prerequisite for verified activation

- [X] T010 Probe the current generation/loaded-source seam in disposable Workbench hosts in `tests/essentials/Workbench/Tests/CompositionHostAttestationProbeTests.cs`, alongside controlled initializer and observer-timeout tests in `tests/essentials/Modularity/Tests/ServerReadinessTests.cs`. Vary six candidate file roles, a process override, and loadable package versions across host starts; compare previous/new active generations and safe response surfaces. The current seam has no sound complete-bundle marker. Same-process package reconciliation and actual lost HTTP response remain future full-activation gates, not proven by this spike.
- [X] T011 Record the no-go in `specs/177-file-deployed-activation/research.md`, `specs/177-file-deployed-activation/contracts/activation-v1.md`, and #1964/#2039. US2/US3 now keep candidate match unverified; #2041 is the narrow host-observation story and #2042 investigates a future marker. No current implementation may claim `candidate active`.

**Checkpoint**: A real-host marker is either proven with exact scope and redaction, or verified candidate matching remains blocked. T010–T011 form a separate spike, not a hidden part of US1.

## Phase 5: User Story 2 - Verify an externally deployed composition (P2)

**Goal**: Correlate an externally attested complete deployment with a sanitized host reload, default-shell generation, readiness, and (only if T010 passed) exact candidate match.

**Independent test**: Supply a trusted test deployment receipt and disposable host independently of US1's CLI. A successful reload shows new generation/readiness; mismatched or absent attestation never returns `candidateMatch=verified`.

### Tests for User Story 2

- [ ] T012 [P] [US2] Write failing contract tests for complete-switch receipt, expected-current conflict, missing artifact integrity, and safe outcome classes in `tests/essentials/Modularity/Tests/CompositionDeploymentReceiptTests.cs`.
- [X] T013 [P] [US2] Write failing rebuilt-host tests for management-key authorization, no prior active generation, successful default-shell reload/readback, failed candidate with retained prior generation, non-default route refusal, and redaction in `tests/essentials/Workbench/Tests/CompositionActivationObservationTests.cs`. Exercise controlled initializer failure and observer-timeout readback in `tests/essentials/Modularity/Tests/ServerReadinessTests.cs`. Exact candidate marker and external receipt cases remain separate.

### Implementation for User Story 2

- [X] T014 [US2] Add Workbench-root, management-key-protected default-shell GET observation and POST reload in `src/apps/Elsa.Workbench/Composition/CompositionActivationObservationEndpoints.cs`; project only generation/readiness/status with `candidateMatch=unverified`, never relay blueprint configuration or raw exception text, and exclude these root routes from lazy shell resolution.
- [ ] T015 [US2] Implement the generation-bound marker only through the seam proven by T010 in `src/apps/Elsa.Workbench/Composition/CompositionGenerationMarker.cs` and wire its authenticated observation in `src/apps/Elsa.Workbench/Program.cs`, preserving ADR 0037's server-side key boundary; otherwise record candidate match as unverified.
- [ ] T016 [US2] Reconcile the host observation and external receipt into the status dimensions in `src/apps/Elsa.Workbench/Composition/CompositionActivationOutcome.cs`; report observed shell readiness separately from unchecked package, connection and migration facts.

**Checkpoint**: US2 can report a real active generation without overclaiming exact candidate identity. A verified match requires T010's accepted proof and the matching test fixture.

## Phase 6: User Story 3 - Recover from failed or uncertain activation (P3)

**Goal**: Preserve/read back the prior active generation after a failed candidate, and require a fresh repair or external rollback decision before another reload.

**Independent test**: With a supplied test deployment owner, inject initializer failure after a complete switch and a lost reload response. The prior generation stays ready; current deployment and active generation are read before retry; repair/rollback plus reload yields an explicit new outcome.

### Tests for User Story 3

- [ ] T017 [P] [US3] Write failing registry/host tests for failed initializer, lost response, previous-generation retention and repaired reload in `tests/essentials/Modularity/Tests/CompositionActivationRecoveryTests.cs`.
- [ ] T018 [P] [US3] Write failing external-receipt conflict and source-rollback tests in `tests/essentials/Modularity/Tests/CompositionDeploymentReceiptTests.cs`; assert no database or migration rollback claim.

### Implementation for User Story 3

- [ ] T019 [US3] Add readback-before-retry and safe `uncertain`/`deployment-changed-not-active` evaluation in `src/apps/Elsa.Workbench/Composition/CompositionActivationOutcome.cs`; do not auto-replay a stale candidate ID.
- [ ] T020 [US3] Keep rollback as an explicit external deployment action in `src/apps/Elsa.Workbench/Composition/CompositionActivationObserver.cs`; observe the resulting reload/generation without writing host files or data stores.
- [ ] T021 [US3] Run the disposable failure, response-loss, repair and rollback scenarios in `specs/177-file-deployed-activation/quickstart.md`, scanning all outward results for connection/unknown-setting canaries, key material, blueprint data and raw exceptions.

**Checkpoint**: Failure and recovery states are independently observable. No successful status implies schema or data rollback.

## Phase 7: Polish and delivery gates

- [X] T022 Update `docs/program-goals/feature-composition-readiness.md`, `specs/177-file-deployed-activation/spec.md` status, and affected `docs/maps/` generated files only for outcomes actually delivered by the relevant PR; keep US2/US3 Draft if their host proof is still pending.
- [ ] T023 Run the affected CLI/Planning and rebuilt-host suites for the delivered story, `tests/essentials/Architecture/Elsa.Architecture.Tests.csproj`, `dotnet run --project tools/maps/Elsa.Maps.Generator -- check`, `git diff --check`, and a diff/redaction review; post exact-head results on each PR and verify post-merge `main` CI/Maps.

## Dependencies and execution order

T001–T002 prepare the fixture/contract; T003–T004 confirm the existing source/output seam. T005–T006 are written before T007–T008 behavior so they show the missing handoff; T009 closes US1 independently. T010–T011 are the separate host-attestation gate. T012–T016 (US2) require a decision from that gate before any verified-match implementation claim; T017–T021 (US3) require the same result plus a working deployment receipt/host observer. T022–T023 apply to each scoped implementation PR, not only a final combined change. The program's one-active-leaf rule still governs issue/PR delivery.

## Parallel opportunities

T001 fixture details and T002 contract example can be reviewed independently. T005 pure projection tests and T006 real-process CLI tests target different files after their shared output shape is fixed. T013 host tests are delivered independently of T012's future receipt tests; no competing implementation PRs should start. T017 and T018 cover distinct host/deployer failures. The root owner integrates and validates every result.

## Implementation strategy

Deliver US1 as a small file-only story first. Review the exact host-attestation result in a separate spike before filing a verified-activation story; if it fails, narrow US2 to honest unverified observation and amend the contract rather than claiming completion. Then implement US2 and US3 in ordered, separately testable slices. Use focused suites for each change, not the broad EF container matrix unless code or behavior crosses into EF. Main-branch CI may still run its own broader matrix.

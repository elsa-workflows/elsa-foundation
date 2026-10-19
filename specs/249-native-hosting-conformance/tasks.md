# Tasks: Native hosting conformance

Input: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md), [quickstart.md](quickstart.md). Branch: `agent/codex/3056-native-hosting-conformance`. Issue #3056 is the single claimed implementation leaf.

Test cadence: this extends an existing test fixture and production path, not greenfield production behavior. Preserve existing assertions, add scenarios with their fixture support, and run the affected proof before marking a story complete. Elsa declares no global test-first cadence; this unit does not establish one. No automated-test obligation is waived.

## Phase 1: Setup

- [x] T001 Review acceptance, architecture gates, source seams and scope in `specs/249-native-hosting-conformance/spec.md`; reserve 249 and claim #3056 before edits.
- [x] T002 Complete root and independent design/task review in `specs/249-native-hosting-conformance/plan.md` and `specs/249-native-hosting-conformance/tasks.md`; verify existing ignore patterns and keep optional auto-commit hooks disabled.

## Phase 2: Shared fixture

- [x] T003 Extract shared REST definition/publish/state, package assembly and journal observations into `Essentials/tests/Workbench/Tests/NativeHostingScenario.cs`, adapting `Essentials/tests/Workbench/Tests/WorkbenchBackgroundActivityRecoveryTests.cs` without removing assertions.
- [x] T004 [P] Add owned PID, hard-kill, suspend/resume and safe cleanup controls in `Essentials/tests/Workbench/Tests/WorkbenchProcess.cs`; preserve restart directory transfer.
- [x] T005 Add explicit provider profiles, one immutable package copied to both feeds, shared absolute persistence/key/lock paths and bounded diagnostics in `Essentials/tests/Workbench/Tests/NativeHostingFixture.cs` and `Essentials/tests/Workbench/Tests/NativeHostingScenario.cs`; add shared journal configuration to `Essentials/tests/Workbench/Fixtures/BackgroundActivities/ReceiptDeliveryBarrier.cs` and overlap-safe observations in `Essentials/tests/Workbench/Fixtures/BackgroundActivities/BackgroundProbe.cs`.

- [ ] T005a Repair the activation-token lifetime defect exposed by the preserved recovery baseline: separate startup cancellation from shell-owned background/recurring execution in `Essentials/src/Tasks/Elsa.Tasks/Services/TaskManager.cs`; prove cancellation during startup, survival after completed activation, shared shutdown joining and graceful event draining in Tasks/Events and real CShells warmup tests. Retain activation/body cancellation diagnostics in the Workbench witness. Do not attribute historical timeouts without matching evidence.

T005a delivery: focused [PR #3077](https://github.com/elsa-workflows/elsa-foundation/pull/3077), reviewed head `435574a3a`, merged as `c6271a807`. Local Tasks/Readiness/Events/Architecture and maps passed; exact-head CI/Studio, architecture/core and maps passed. Exact-merge Studio, CodeQL and maps passed, but backend CI failed in the unchanged standalone Jint cancellation test. Focused test-isolation [PR #3082](https://github.com/elsa-workflows/elsa-foundation/pull/3082) is open at `c93344869`; Jint 84, architecture 578, maps and the delayed-cancellation control passed locally. Its hosted CI and the resulting-main gate remain pending. Retain this task unchecked until delivery is verified. The diagnostic fixture remains part of the conformance branch.

Checkpoint: root reviewed the shared fixture and explicit profile adaptation. The 19 October focused process run passed 7/7 (both profiles × both receipt orders, cancellation under both profiles, and lease-wait arithmetic), with no failures/skips. This establishes T003/T006/T016; it does not qualify concurrent survivor ownership or the stale-write fence. The later resolved-EF process run passed 4/4: both concurrent receipt orders plus cancellation/restart under both profiles, with zero failures/skips. This establishes T005/T007; the stale-write case and full qualification remain open. Earlier owned-process controls passed 8/8 combined recovery/shutdown/process cases before the profile adaptation. No fixture is accepted from a subagent without root review.

## Phase 3: US1 — Recover accepted work (P1)

Independent test: the same REST-authored definition and pinned package recover admission, live bodies and committed receipts; both completion orders produce one native final step.

- [x] T006 [US1] Run the preserved crash-boundary and terminal-history scenario under explicit single-host/clustered profiles in `Essentials/tests/Workbench/Tests/WorkbenchBackgroundActivityRecoveryTests.cs`, retaining the published artifact rather than re-resolving catalog heads on another host.
- [x] T007 [US1] Add concurrent-owner/survivor recovery in `Essentials/tests/Workbench/Tests/NativeHostingConformanceTests.cs`, with pass-through native ownership, membership, placement and transport observations. Suspend the quiescent original owner, observe real survivor resumption forwarding through EF, then kill it and observe release of its still-live placement lease plus a fresh post-release snapshot showing departure. Cover fresh-read authorization separately at its owning layer in T015. Correlate an actual sent transport ID through survivor lease/ack; assert effective providers/shared paths, both real PIDs, stable invocation/attempt, both receipt orders and one native continuation. Keep execution fences, routing placement and worker claims distinct.
- [ ] T008 [US1] Run focused process cases from `specs/249-native-hosting-conformance/quickstart.md`; record logs/TRX and source/configuration identity for the final report.

## Phase 4: US2 — Reject the original stale write (P1)

Independent test: the original engine-generated commit reaches the real EF transaction with its original uncancelled token and receives the exact stale-token rejection; recovery then finishes.

- [ ] T009 [US2] Add opt-in post-configuration wiring in `Essentials/tests/Workbench/Fixtures/BackgroundActivities/NativeHostingWitness.cs` and `elsa-package.json`, with only necessary private test references in `BackgroundActivities.csproj`; verify dependency ordering, single descriptors, lifetime and fail-fast configuration through focused fixture registration coverage. Prove provider/scope shutdown disposes the captured backend exactly once through the retained holder.
- [ ] T010 [US2] Implement original-commit, real-successor-acquire and original-heartbeat barriers plus read-only durable observations in `Essentials/tests/Workbench/Fixtures/BackgroundActivities/NativeHostingWitness.cs`. Select the fold-free Immediate `ActivityCompleted` commit for the first ready background result, with its activity and receipt-acceptance participants; verify unchanged object/fingerprint, original token, no marker and a live successor lease. Assert empty consumed scheduler participants and an unchanged, unclaimed durable scheduler row; any observed overlay claim is explicitly in-memory evidence. Hold no transaction/write lease. Add a test-only scheduler-renewal gate only if needed to prevent cancellation, without fabricating success.
- [ ] T011 [US2] Add the suspend/takeover/resume case in `Essentials/tests/Workbench/Tests/NativeHostingConformanceTests.cs`, asserting exact exception/reason/tokens, the receipt still `ResultReady` with the same digest, unchanged durable activity/scheduler participants, elapsed suspension below the dispatch ceiling, no cancellation/partial durable writes and native completion after release; explicitly test/apply owned cleanup on failure.
- [ ] T012 [US2] Run the focused case with the actual EF fence stage temporarily bypassed in `Essentials/src/Workflows/Runtime/Persistence/EntityFrameworkCore/Stores/EfRuntimeCheckpointCommitStore.cs`; require persisted stale-write failure, restore source and require a passing rebuilt case. Record evidence without committing the mutation.

## Phase 5: US3 — Select and operate providers (P2)

Independent test: composition selects the intended provider, real graceful handoff recovers accepted work, and each lower-layer guarantee is mapped to its actual test tier.

- [ ] T013 [US3] Verify explicit in-process and clustered provider/membership composition and reject invalid fixture wiring in `Essentials/tests/Workbench/Tests/NativeHostingConformanceTests.cs` and the test-only feature registration coverage for `NativeHostingWitness.cs`.
- [ ] T014 [US3] Add graceful handoff/restart observations in `Essentials/tests/Workbench/Tests/NativeHostingConformanceTests.cs`, retaining accepted state/package and demonstrating admission moves away from the draining owner.
- [x] T015 [US3] Run affected placement/runnability/drain tests under `Essentials/tests/Workflows/Runtime/Distributed/Tests/Placement/`, EF scope/registration tests under `Essentials/tests/Workflows/Runtime/Distributed/Persistence/EntityFrameworkCore/Tests/`, and membership conformance under `Essentials/tests/Cluster/`; map actual versus modeled coverage and rollback instructions in `docs/reports/native-hosting-conformance.md`.

T015 evidence: placement 83 passed; distributed EF 107 passed; membership contracts 159 passed/30 inapplicable single-host skips; selected SQLite membership 88 passed/1 unavailable duplicate-host diagnostic skip. All four runs had zero failures. The [owning-layer report](../../docs/reports/native-hosting-conformance.md#owning-layer-qualification) records filters and actual-versus-modeled boundaries. This does not complete T014's real graceful handoff acceptance.

## Phase 6: US4 — Preserve outcome acceptance (P2)

Independent test: identical/conflicting delivery retains existing outcomes and cancelled work stays terminal after late receipts and process loss.

- [x] T016 [US4] Run/extend cancellation and late-receipt process scenarios for applicable profiles in `Essentials/tests/Workbench/Tests/WorkbenchBackgroundActivityRecoveryTests.cs` and `Essentials/tests/Workbench/Tests/NativeHostingConformanceTests.cs`.
- [ ] T017 [US4] Verify duplicate/conflicting acceptance using `Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/BackgroundActivityWorkTests.cs`, `BackgroundActivityWorkCheckpointAtomicityTests.cs` and `NativeBackgroundActivityFlowchartEfTests.cs`; retain their contract/EF label and add process delivery observations where needed in `NativeHostingConformanceTests.cs`.

## Phase 7: Integrated review and delivery

- [ ] T018 Review integrated behavior/DRY/cleanup and obtain independent review; complete the scenario evidence, limitations and source pins in `docs/reports/native-hosting-conformance.md` and runnable commands in `specs/249-native-hosting-conformance/quickstart.md`.
- [ ] T019 Update affected lock files/maps if needed, including `docs/maps/manifest.json`; run architecture, map freshness, diff checks and relevant e2e for any production behavior change, recording exact commands/results in `docs/reports/native-hosting-conformance.md`.
- [ ] T020 Recheck claims/open PRs; commit/push as the App, open scoped qualification PRs linked to #3056, following the separately delivered T005a prerequisite, post validation and review receipts, then require exact-current-head CI and Maps before normal authorized merge. Update terminal spec status in `specs/249-native-hosting-conformance/spec.md` only when implemented.
- [ ] T021 Verify post-merge CI/Maps, reconcile #3056/#2905/#2892 and `docs/reports/native-hosting-conformance.md` evidence, and release the exact claim. Keep the broader program and successor work open.

## Dependencies and parallel work

T001 → T002 → shared fixture (T003/T004 parallel, then T005). US1 establishes process infrastructure before US2. US3/US4 follow the integrated process fixture and may prepare read-only test inventories in parallel; edits to shared test files remain serial. T018–T021 require all stories. Build/test commands run serially through the machine's build-slot wrapper.

Useful bounded delegation: one worker owns only `WorkbenchProcess.cs` while another extracts `NativeHostingScenario.cs` and its original caller; root integrates and runs regression checks. Later, a worker may implement the isolated witness feature while root writes the scenario test, after agreeing its fixture record/barrier contract. Never run a mutation while another worker builds or edits its target.

US1 is the first demonstrable increment, but does not close #3056. All four stories and integrated gates are required for qualification.

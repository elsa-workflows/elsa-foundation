---
description: "Dependency-ordered work for adopting the shared shell activation runner in Foundation and Workbench"
---

# Tasks: Shared Startup Runner Adoption

**Input**: `spec.md`, `plan.md`, `research.md`, `data-model.md`, `contracts/startup-profiles.md`, and `quickstart.md` in `specs/200-adopt-startup-runner/`.

**Status**: Reviewed execution plan. Every task is unchecked. The preview package update is preparation for implementation; stable package adoption and actual-host acceptance remain a separate final gate.

**Test cadence**: The derived application constitution does not declare test-first, test-after, or another cadence. The order below follows implementation dependencies and does not impose TDD. Preserve every existing test subject and objective under framework §2.21.1/§2.23. Add registration and per-implementation tests for changed logic-bearing implementations; never weaken or remove existing assertions.

**Scope boundary**: Implement only the three reviewed host profiles. Keep settled-readiness policy, package-generation readability, host composition extraction, unload/prune, and PID-1 behavior in their separately owned units. Do not treat preview or local checks as stable release acceptance.

## Phase 1: Setup

**Purpose**: Pin the exact qualified preview API needed by the consumer work, without claiming stable adoption.

- [ ] T001 Update the eight CShells central versions in `Directory.Packages.props` from `0.0.30-preview.171` to the exact qualified `0.0.30-preview.173` family, retain the current Nuplane `0.0.11-preview.99` pins, and record source/package identity in `docs/reports/modular-hosting-package-qualification.md`.
- [ ] T002 Regenerate every reached `packages.lock.json` affected by T001, then verify coherent family versions and perform the affected locked restore from the configured qualified feed; leave unrelated lock entries unchanged.

## Phase 2: Foundational

**Purpose**: Establish the one root-owned runner registration both host profiles will consume before adapting either service.

- [ ] T003 Register the upstream `IShellActivationRunner` once as a root-owned service in each host composition in `src/apps/Elsa.Foundation.Host/Program.cs` and `src/apps/Elsa.Workbench/Program.cs`; add real-DI assertions in `tests/essentials/Modularity/Tests/HostOwnedServicesAreSharedWithShellsTests.cs` proving root resolution, the runner and its concrete implementation are absent from child-shell containers under the existing upstream exclusion, no extra hosted service from runner registration, and preservation of existing intentional host/shell service sharing. Do not add a second sharing policy or hosted-service registration for the runner; hosted adapters resolve it at the root.

**Checkpoint**: Complete setup and shared registration before implementing the story adapters. The Foundation and Workbench adapter changes can then proceed independently by application.

## Phase 3: User Story 1 — Foundation recovers without restarting (Priority: P1)

**Goal**: Keep the ordered, default-on pre-listen pass and independent ongoing recovery while delegating attempt scheduling and run ownership to the runner.

**Independent test**: With deterministic gates, exercise configured order, ordinary retry, EF refusal cadence, failure-row projection, fatal initial/background behavior, and a rebuilt Foundation process recovering from temporary database unavailability without restart.

- [ ] T004 [US1] Replace the local scheduling loop in `src/apps/Elsa.Foundation.Host/Shells/EagerShellActivationHostedService.cs` with the runner profile while preserving default-on/explicit-off configuration, configured order, pre-listen initial-pass completion, uncancelled `OperationCanceledException` classification, and independent retries.
- [ ] T005 [US1] Adapt `src/apps/Elsa.Foundation.Host/Shells/ShellActivationTracker.cs` and the failure observer to project callback `AttemptNumber`, preserve the existing failure/health JSON shape and EF refusal guidance, and select one immutable per-target retry decision without a duplicate counter or jitter calculation.
- [ ] T006 [P] [US1] Add or adapt focused Foundation implementation tests in `tests/essentials/Modularity/Tests/FoundationHostEagerActivationTests.cs` for ordinary retries, EF refusal, initial-fatal skip/rethrow, background-fatal target isolation, NotCurrent classification, active-notification reset, and the FR-020 no-notification row-retention/rebase case.
- [ ] T007 [P] [US1] Preserve and extend real-host recovery, boot, and refusal coverage in `tests/essentials/Cluster/EntityFrameworkCore/Tests/FoundationHostEagerActivationRetryTests.cs`, `tests/essentials/Cluster/EntityFrameworkCore/Tests/FoundationHostBootTests.cs`, and `tests/essentials/Cluster/EntityFrameworkCore/Tests/FoundationHostReloadRefusalTests.cs`, `tests/essentials/Cluster/EntityFrameworkCore/Tests/FoundationHostReconcileTests.cs`; use the latter for the actual external-settlement-then-removal/deactivation process scenario and verify the skipped-retry-policy boundary, operator guidance, and live readiness.

**Checkpoint**: Story 1 is independently testable only when both deterministic policy/projection tests and the actual-host recovery scenario remain effective.

## Phase 4: User Story 2 — Workbench retains its two startup phases (Priority: P2)

**Goal**: Share attempt execution without merging the opt-in eager phase and post-listen warmup policies, state, or telemetry.

**Independent test**: Verify default-off eager behavior and ordered one-shot execution, then gate `ApplicationStarted` and prove warmup waits, discovers before activation, records the returned generation, and reports its own outcome without adding a retry.

- [ ] T008 [P] [US2] Adapt `src/apps/Elsa.Workbench/Boot/EagerShellActivationHostedService.cs` to use a one-shot runner profile while preserving default-off, all/named selection, configured order/deduplication, continue-on-failure, cancellation propagation, and no retry.
- [ ] T009 [P] [US2] Adapt `src/apps/Elsa.Workbench/Readiness/DefaultShellWarmup.cs` to use a separate one-shot runner run after `ApplicationStarted`, preserve feature-discovery order and existing telemetry/state transitions, and record the exact generation returned by successful activation for built-in and supported custom registries.
- [ ] T010 [US2] Preserve and extend `tests/essentials/Modularity/Tests/EagerShellActivationTests.cs` and `tests/essentials/Modularity/Tests/ShellReadinessTests.cs` for eager defaults/selection/order, cancellation, warmup phase boundaries, no retry, same-shell eager/warmup overlap, telemetry/outcome separation, and exact returned-generation races; keep each existing assertion objective intact.

**Checkpoint**: Story 2 is independently testable when eager and warmup remain separate host-owned phases while sharing only the underlying activation execution.

## Phase 5: User Story 3 — Shutdown owns outstanding activation work (Priority: P2)

**Goal**: Make each host stop new attempts, cancel owned work, and retain/join outstanding work across caller-bounded waits.

**Independent test**: Gate cancellation-aware and cancellation-ignoring activation, prove bounded stop does not abandon the run, release the gate, and prove a subsequent stop joins the same task without a new attempt. Verify multiple fatal shutdown results are all retained and selected by configured order.

- [ ] T011 [US3] Complete cancellation and stop ownership in `src/apps/Elsa.Foundation.Host/Shells/EagerShellActivationHostedService.cs`, `src/apps/Elsa.Workbench/Boot/EagerShellActivationHostedService.cs`, and `src/apps/Elsa.Workbench/Readiness/DefaultShellWarmup.cs`: forward Foundation's caller token only while startup remains pending, then use the host token; retain one stop/join task per run and ensure cancellation callback failures do not skip joining.
- [ ] T012 [P] [US3] Add deterministic Foundation lifecycle tests in `tests/essentials/Modularity/Tests/FoundationHostEagerActivationTests.cs` for handoff cancellation, post-start caller-token detachment, bounded then repeated stop, cancellation-ignoring work, cancellation callback failure, and concurrent background fatals with original exception identity/order.
- [ ] T013 [P] [US3] Add deterministic Workbench shutdown and warmup lifecycle tests in `tests/essentials/Modularity/Tests/ShellReadinessTests.cs` for application-start cancellation, bounded join, no post-stop activation, and preserved warmup outcome/telemetry.

**Checkpoint**: Story 3 is independently testable only when stop never launches another attempt, a bounded wait does not discard work, and all later joins observe the owned terminal result.

## Phase 6: Polish and Cross-Cutting Concerns

**Purpose**: Preserve refactor evidence, qualify the complete consumer change, and keep preview proof distinct from final delivery.

- [ ] T014 Audit the pre-change assertion baselines in `tests/essentials/Modularity/Tests/FoundationHostEagerActivationTests.cs`, `tests/essentials/Modularity/Tests/EagerShellActivationTests.cs`, `tests/essentials/Modularity/Tests/ShellReadinessTests.cs`, `tests/essentials/Modularity/Tests/SharedNuplaneHostProfileDeliveryTests.cs`, `tests/essentials/Cluster/EntityFrameworkCore/Tests/FoundationHostEagerActivationRetryTests.cs`, `tests/essentials/Cluster/EntityFrameworkCore/Tests/FoundationHostBootTests.cs`, `tests/essentials/Cluster/EntityFrameworkCore/Tests/FoundationHostReloadRefusalTests.cs`, `tests/essentials/Cluster/EntityFrameworkCore/Tests/FoundationHostReconcileTests.cs`, and `tests/essentials/Cluster/EntityFrameworkCore/Tests/WorkbenchHostProcessTests.cs`; repair only test wiring and report that no subject/objective was weakened or removed.
- [ ] T015 Run two compiled behavioral mutations: suppress the FR-020 rebase in `src/apps/Elsa.Foundation.Host/Shells/ShellActivationTracker.cs` and substitute a later raw-current generation read for the returned generation in `src/apps/Elsa.Workbench/Readiness/DefaultShellWarmup.cs`; demonstrate the corresponding assertions in `tests/essentials/Modularity/Tests/FoundationHostEagerActivationTests.cs` and `tests/essentials/Modularity/Tests/ShellReadinessTests.cs` fail, restore each source file byte-for-byte, and rerun both affected tests.
- [ ] T016 Run the affected application test projects in `tests/essentials/Modularity/Tests/Elsa.Modularity.Tests.csproj`, `tests/essentials/Workbench/Tests/Elsa.Workbench.Tests.csproj`, and `tests/essentials/Cluster/EntityFrameworkCore/Tests/Elsa.Cluster.EntityFrameworkCore.Tests.csproj`, serializing builds through the repository wrapper and recording exact results.
- [ ] T017 Run `tests/essentials/Architecture/Elsa.Architecture.Tests.csproj`, exercise the Workbench backend using the owned-server and process-control guidance in `e2e-tests/README.md` (including the `e2e-tests/Test-WorkflowFlow.ps1` smoke), and run `dotnet run --project tools/maps/Elsa.Maps.Generator -- check`; review any generated-map change rather than restamping unrelated maps.
- [ ] T018 Complete `docs/reports/modular-hosting-package-qualification.md` for the exact `.173` CShells family and `.99` Nuplane dependency, including source/archive identity, locked restore, and actual rebuilt-host evidence; label all preview proof interim and do not use it as stable-release acceptance.
- [ ] T019 After the specified upstream source/package families are released, update `Directory.Packages.props` and all reached `packages.lock.json` files to the exact stable versions, run clean-cache locked restore, rerun the affected host/process gates, the owned-server Workbench backend smoke in `e2e-tests/README.md`, and architecture/maps; inspect resulting-main CI and record exact stable source commits, family archive identities, package versions, and host evidence in `docs/reports/modular-hosting-release-plan.md` before marking this feature complete.

## Dependencies & Execution Order

### Phase dependencies

- Setup T001 → T002 establishes the qualified preview dependency graph.
- Foundational T003 depends on T002 and blocks both host stories.
- User Story 1 T004–T007 and User Story 2 T008–T010 each depend on T003; the application implementations can proceed in parallel because they are in separate host trees, provided shared fixture or central pin edits are not duplicated.
- User Story 3 T011–T013 depends on the run integration from both stories, since it verifies host-owned cancellation and join for all three profiles.
- Polish T014–T018 depends on the preview implementation. T019 is separately release-gated and depends on stable upstream publication; preview completion does not satisfy it.

### User-story dependencies

- **US1** requires the shared runner registration, then delivers Foundation recovery independently.
- **US2** requires the shared runner registration, then delivers the two Workbench phases independently.
- **US3** depends on US1 and US2 because its ownership contract spans all adapters.

### Safe parallel opportunities

After T003, independent Foundation and Workbench implementation streams may proceed in parallel:

```text
Foundation stream: T004 -> T005 -> T006 -> T007
Workbench stream:  T008 -> T009 -> T010
```

Within US1, after T004–T005 stabilize the adapter/projection contract, Modularity unit coverage (T006) and real Cluster process coverage (T007) may proceed in separate test projects. Within US2, eager and warmup adapter files (T008–T009) are independent; preserve both test suites in T010. Within US3, after the shared stop contract is implemented in T011, Foundation lifecycle tests (T012) and Workbench lifecycle tests (T013) may proceed in separate files. Do not parallelize edits to a shared file, `Directory.Packages.props`, or the reached lockfile set.

## Implementation Strategy

### MVP increment

Complete T001–T007 first. This yields Foundation's independently valuable no-restart recovery path and its real-host proof. Do not call the overall feature delivered at this checkpoint: Workbench's two profiles, shared shutdown ownership, cross-cutting gates, and stable package adoption remain required.

### Full delivery

Complete US1 and US2, then US3; preserve old test objectives throughout. Finish preview qualification and cross-cutting verification before performing stable package adoption. Close the feature only after T019 proves released package families, coherent locks, actual-host behavior, and green resulting-main gates. A preview build, local candidate, or single story is not the full program deliverable.

## Validation and policy notes

- Preserve the existing tests' subjects/objectives under framework §2.21.1/§2.23; repair setup and DI wiring rather than deleting behavior assertions.
- The derived application constitution does not declare a test-creation cadence. This list orders tasks by dependencies and does not require tests-first or a failing pre-implementation run.
- Keep Foundation's fatal boundary and Workbench's distinct continue/cancel behavior separate. Keep readiness tied to the live registry, not runner history.
- Final acceptance is stable-release gated. Keep all preview evidence explicitly labeled interim.

# Tasks: Extensible Incident Strategies

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts](contracts/)

**Tests**: Required. Preserve all existing test objectives; add branch coverage for each new
logic-bearing implementation and registration coverage for changed feature wiring.

## Phase 1: Shared identity and clean-break model

- [x] T001 Add `IncidentStrategyReference` with validated alias/version and identity comparer behavior in `src/Elsa/Workflows/Primitives/Models/IncidentStrategyReference.cs`
- [x] T002 [P] Add primitive identity/serialization tests in `tests/Elsa/Workflows/Runtime/Tests/IncidentStrategyReferenceTests.cs`
- [x] T003 Add the Workflows Primitives project references to `src/Elsa/Workflows/Design/Core/Elsa.Workflows.Design.Core.csproj` and `src/Elsa/Workflows/Runtime/Core/Elsa.Workflows.Runtime.Core.csproj`
- [x] T004 Replace `WorkflowStrategyOptions.IncidentStrategyType` with nullable `IncidentStrategy` in `src/Elsa/Workflows/Design/Core/Models/WorkflowStrategyOptions.cs` and update Design round-trip fixtures/tests
- [x] T005 [P] Add `IncidentResolutionOutcome`, stable action kinds, and stable system sources in `src/Elsa/Workflows/Runtime/Core/Models/IncidentResolutionOutcome.cs` and `src/Elsa/Workflows/Runtime/Core/Constants/IncidentResolution*.cs`
- [x] T006 Replace `IncidentState.ResolutionAction` and `ActivityExecutionIncidentSummary.ResolutionAction` with nullable immutable outcomes in `src/Elsa/Workflows/Runtime/Core/Models/IncidentState.cs` and `src/Elsa/Workflows/Runtime/Core/Models/ActivityExecutionInspectionSummaries.cs`
- [x] T007 Update Runtime API incident views and mapping in `src/Elsa/Workflows/Runtime/Api/Models/WorkflowExecutionViews.cs`
- [x] T008 Update Groundwork runtime document versioning/serialization and clean-break fixtures for the outcome shape under `src/Elsa/Persistence/Groundwork/` and `tests/Elsa/Persistence/Groundwork/`
- [x] T009 Update all existing enum-based unit fixtures/assertions across `tests/` without deleting their behavioral objectives

## Phase 2: Foundational extension contracts and registry

- [x] T010 Add descriptor, attribute, policy-safe snapshots, `IIncidentStrategy`, and `IIncidentResolutionAction` contracts under `src/Elsa/Workflows/Runtime/Core/{Contracts,Models}/`
- [x] T011 [P] Add action-context capability contract and built-in action factory surface under `src/Elsa/Workflows/Runtime/Core/{Contracts,Models}/`
- [x] T012 [P] Add strategy-safe intent descriptor/registration contracts that exclude existing scheduler, dispatch, and stimulus kinds under `src/Elsa/Workflows/Runtime/Core/{Contracts,Models}/`
- [x] T013 Add `AddIncidentStrategy<T>(descriptor)`, attributed `AddIncidentStrategy<T>()`, and strategy-safe intent registration extensions in `src/Elsa/Workflows/Runtime/Core/Extensions/`
- [x] T014 Implement the startup-built descriptor/service registry and exact default resolver in `src/Elsa/Workflows/Runtime/Services/IncidentStrategyRegistry.cs`
- [x] T015 Register `Fault/1` and `ContinueWithIncidents/1` unconditionally and validate duplicate identity, reserved aliases/kinds, custom namespaces, attributes, safe intents, and host default in `src/Elsa/Workflows/Runtime/Extensions/RuntimeCoreServiceCollectionExtensions.cs`
- [x] T016 Add registry, reflection, duplicate, default, zero-construction discovery, lifetime, and feature-registration tests in `tests/Elsa/Workflows/Runtime/Tests/IncidentStrategyRegistryTests.cs` and `WorkflowsRuntimeApiFeatureTests.cs`

## Phase 3: User Story 1 — Select and execute built-in policy

**Goal**: Publish exact strategy selection and automatically apply Fault or Continue after ordinary
activity faults.

**Independent test**: Publish the same workflow with each built-in and prove distinct durable
incident/workflow results.

- [x] T017 [US1] Add failing compiler/default/hash tests for authored override, host default, `Fault/1` fallback, unknown references, republish behavior, and distinct executable identities in `tests/Elsa/Workflows/Publishing/Api/Tests/WorkflowExecutableCompilerTests.cs`
- [x] T018 [US1] Add required pinned `IncidentStrategy` to `src/Elsa/Workflows/Runtime/Core/Models/WorkflowExecutable.cs` and every constructor/serializer fixture
- [x] T019 [US1] Resolve and validate authored → host → Fault strategy during compilation in `src/Elsa/Workflows/Publishing/Api/Services/WorkflowExecutableCompiler.cs`
- [x] T020 [US1] Include exact pinned alias/version in `src/Elsa/Workflows/Publishing/Api/Services/WorkflowExecutableHasher.cs`
- [x] T021 [P] [US1] Implement `FaultIncidentStrategy`/`FaultWorkflowAction` and branch tests under `src/Elsa/Workflows/Runtime/Strategies/` and `tests/Elsa/Workflows/Runtime/Tests/`
- [x] T022 [P] [US1] Implement `ContinueWithIncidentsStrategy`/action and branch tests under `src/Elsa/Workflows/Runtime/Strategies/` and `tests/Elsa/Workflows/Runtime/Tests/`
- [x] T023 [US1] Add `IncidentResolutionBatchApplied` checkpoint name and mandatory coalescing boundary in `src/Elsa/Workflows/Runtime/Core/Constants/RuntimeCheckpointNames.cs` and `src/Elsa/Workflows/Runtime/Services/Coalescing/CoalescingRuntimeCheckpointPersistencePolicy.cs`
- [x] T024 [US1] Implement policy-safe context projection and exact pinned scoped strategy resolution under `src/Elsa/Workflows/Runtime/Services/`
- [x] T025 [US1] Implement incident-local action staging and ordered one-checkpoint `IncidentResolutionBatchExecutor` in `src/Elsa/Workflows/Runtime/Services/IncidentResolutionBatchExecutor.cs`
- [x] T026 [US1] Implement `IncidentStrategyResolutionDrainObserver` at outer-drain causal quiescence in `src/Elsa/Workflows/Runtime/Services/IncidentStrategyResolutionDrainObserver.cs`
- [x] T027 [US1] Order poison → strategy resolution → terminal safety observers and make `BlockingIncidentWorkflowFaultObserver` skip outcome-bearing incidents in `src/Elsa/Workflows/Runtime/Extensions/RuntimeCoreServiceCollectionExtensions.cs` and `src/Elsa/Workflows/Runtime/Services/BlockingIncidentWorkflowFaultObserver.cs`
- [x] T028 [US1] Add drain-order, two-incident stable order, one-checkpoint atomicity, Fault, Continue, Wait, independent-work, and coalescing tests under `tests/Elsa/Workflows/Runtime/Tests/`

## Phase 4: User Story 2 — Third-party strategies and decision objects

**Goal**: Let third parties register a scoped strategy and return executable custom action objects
without framework enum changes.

**Independent test**: Register an attributed strategy/action, publish it, execute it, inspect its
outcome, and prove forbidden capabilities are unavailable/rejected.

- [x] T029 [US2] Implement guarded public action context verbs, bounded metadata validation, and custom resolved/open/blocking transitions in `src/Elsa/Workflows/Runtime/Services/IncidentResolutionActionContext.cs`
- [x] T030 [US2] Implement separately authorized strategy-safe intent staging with runtime-derived deterministic identity/correlation in `src/Elsa/Workflows/Runtime/Services/`
- [x] T031 [US2] Add custom strategy/action tests for scoped resolution, direct object execution, namespaced exact kinds, target isolation, safe metadata, and safe intent atomicity in `tests/Elsa/Workflows/Runtime/Tests/CustomIncidentStrategyTests.cs`
- [x] T032 [US2] Add rejection tests for core scheduler/dispatch/stimulus intents, built-in kinds, activity mutation, workflow retry/suspend/complete/cancel, cross-incident mutation, absorption, suppression, and checkpoint access
- [x] T033 [US2] Implement and test null-return, resolve-throw, execute-throw, unrelated cancellation, supplied-token cancellation, fresh Fault fallback, staging discard, fallback failure, and checkpoint failure semantics
- [x] T034 [US2] Add crash/replay and outbox-redelivery tests proving no duplicate committed outcome or intent

## Phase 5: User Story 3 — Trustworthy system outcomes and discovery

**Goal**: Expose safe immutable provenance for strategy/system handling and descriptor-only authoring
discovery.

**Independent test**: Exercise every strategy/system path and GET discovery with/without permission,
then inspect exact status/outcome/source and zero strategy constructions.

- [x] T035 [US3] Convert structural absorption in parent propagation to internal `AbsorbFault` outcome with `StructuralFaultAbsorption`
- [x] T036 [P] [US3] Convert subtree cancellation/reclamation to internal `SuppressIncident` outcome with `SubtreeCancellation`
- [x] T037 [P] [US3] Convert activity activation failure and poisoned scheduler work to exact system outcomes and bypass ordinary strategy resolution
- [x] T038 [US3] Add pre-start missing-pinned-strategy handling that records Blocking + Wait + `MissingStrategyImplementation`, preserves Pending, and schedules no retry
- [x] T039 [US3] Add system-path tests for absorption, suppression, activation, poison, missing deployment, and terminal immutability
- [x] T040 [P] [US3] Add Publishing API request/flat response models, handler, route constant, and permission-protected `GET publishing/incident-strategies` endpoint under `src/Elsa/Workflows/Publishing/Api/`
- [x] T041 [P] [US3] Add the `incident-strategies` relation to `src/Elsa/Workflows/Publishing/Api/Capabilities/PublishingApiCapabilities.cs`
- [x] T042 [US3] Add endpoint/capability tests for permission, deterministic order, effective default, unknown default activation failure, exact response shape, and zero strategy construction in `tests/Elsa/Workflows/Publishing/Api/Tests/IncidentStrategyDiscoveryEndpointTests.cs`
- [x] T043 [US3] Update Runtime inspection, dashboard, dispatch-workflow, graph, BPMN, and diagnostics tests/consumers to use status + outcome semantics

## Phase 6: Documentation, architecture, and end-to-end verification

- [x] T044 [P] Document Runtime contracts, registration, built-ins, lifecycle behavior, failure semantics, and strategy-safe intents in `src/Elsa/Workflows/Runtime/README.md` and `src/Elsa/Workflows/Runtime/EXTENSION_POINTS.md`
- [x] T045 [P] Document Publishing discovery endpoint/capability in `src/Elsa/Workflows/Publishing/Api/README.md` and `src/Elsa/Workflows/Publishing/Api/EXTENSION_POINTS.md`
- [x] T046 Refresh the repo extension-point index/map only if its manifest inputs are stale and the narrow refresh is authorized; otherwise verify existing root `EXTENSION_POINTS.md` links remain sufficient
- [x] T047 Add architecture tests proving Runtime has no Design reference and obsolete enum/type-string/retry/suspension contracts are absent
- [x] T048 Add discovery GET coverage to `e2e-tests/get-endpoints/Test-PublishingGets.ps1` and update its documented assertion count
- [x] T049 Add Fault and Continue lifecycle cases plus outcome assertions to `e2e-tests/fault-handling/`
- [x] T050 Update persistence-querying e2e assertions and all clean-break JSON fixtures to the new outcome shape
- [x] T051 Run focused unit projects, architecture tests, supported persistence tests, full solution build/tests proportionate to changes, and relevant rebuilt-server e2e suites; record any environment-only omissions
- [x] T052 Run `git diff --check`, search for obsolete `IncidentResolutionAction`, `IncidentStrategyType`, retry/suspend strategy artifacts, and inspect project-reference drift
- [x] T053 Perform bounded self-review (correctness, replay/atomicity, security/capability, public API, docs, tests), remediate findings, and re-run affected checks
- [x] T054 Commit the approved work unit, push `codex/1015-incident-strategies` to `origin`, open a ready PR with `Closes #1015`, and verify the PR linkage/check status

## Dependencies and Parallel Opportunities

- T001–T009 establish the clean-break data model and block all user-story completion.
- T010–T016 establish extension contracts/registration and block publication/runtime/discovery.
- Within US1, built-in implementations T021/T022 can proceed in parallel after T010–T016; compiler
  T017–T020 can proceed alongside them after T001–T009.
- US2 depends on the batch executor T025 but its safe-intent authorization can be developed alongside
  publishing discovery.
- System conversions T035–T039 and discovery T040–T042 can proceed in parallel after foundations.
- Documentation and focused tests are updated in the same slices; final e2e waits for all slices.

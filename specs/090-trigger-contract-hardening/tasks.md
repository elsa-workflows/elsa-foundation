# Tasks: Trigger Publication Contract Hardening

**Input**: Design documents from `/specs/090-trigger-contract-hardening/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Required. Write each listed test first and confirm it fails for the intended reason before implementing its production task.

**Organization**: Tasks are grouped by user story and ordered so the P1 semantic-safety slice can land independently before non-start and compatibility follow-ups.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel because it changes different files and has no dependency on unfinished tasks in the same phase.
- **[Story]**: Maps work to the specification's user story.

## Phase 1: Setup and Baseline

**Purpose**: Confirm the handoff baseline and preserve evidence before changing the contract.

- [X] T001 Run the focused baseline commands in `specs/090-trigger-contract-hardening/quickstart.md` and record pass counts plus any pre-existing failures in that file's implementation-evidence section before editing source
- [X] T002 Verify `docs/maps/manifest.json` freshness for Runtime Core/Scheduling inputs and record which narrow generator must run after implementation in `specs/090-trigger-contract-hardening/quickstart.md`

---

## Phase 2: Foundational Preflight Vocabulary

**Purpose**: Add the non-persisted outcome and typed failure vocabulary shared by all stories, with tests first.

**⚠️ CRITICAL**: Complete this phase before any story implementation.

- [X] T003 Write failing provider-id, outcome invariant, and contextual exception tests in `tests/Elsa/Workflows/Runtime/Tests/WorkflowTriggerBindingExtractorTests.cs`
- [X] T004 [P] Add immutable preflight outcome/status models in `src/Elsa/Workflows/Runtime/Core/Models/WorkflowTriggerPreflightOutcome.cs`
- [X] T005 [P] Add the typed artifact/node/provider-aware failure in `src/Elsa/Workflows/Runtime/Core/Exceptions/WorkflowTriggerPreflightException.cs`
- [X] T006 Add the source/binary-compatible stable `ProviderId` default and additive preflight evaluation surface, classify the provider collection as an exact-one Strategy set, and document all public typed failures with XML `<exception>` declarations in `src/Elsa/Workflows/Runtime/Core/Contracts/IActivityTriggerStimulusProvider.cs`, `src/Elsa/Workflows/Runtime/Core/Contracts/IWorkflowTriggerBindingExtractor.cs`, and `src/Elsa/Workflows/Runtime/Core/Contracts/IWorkflowTriggerIndexer.cs`

**Checkpoint**: Core vocabulary compiles, remains non-persisted, and existing provider/extractor signatures still compile.

---

## Phase 3: User Story 1 - Reject Unroutable Start Triggers Before Index Mutation (Priority: P1) 🎯 MVP

**Goal**: Require exact-one provider ownership and complete descriptor/recurring-schedule materialization before trigger or schedule replacement.

**Independent Test**: Seed prior bindings and schedules, attempt invalid Event/Timer/Cron/Http publications, and prove each failure leaves both stores unchanged while valid workflows still register complete bindings/schedules.

### Tests for User Story 1

- [X] T007 [P] [US1] Write failing zero-provider, multiple-provider, blank-provider-id, duplicate-resulting-binding-id, mixed-valid/invalid-node, and provider-called-once-per-node preflight tests—while pinning distinct binding ids as valid fan-out—in `tests/Elsa/Workflows/Runtime/Tests/WorkflowTriggerBindingExtractorTests.cs`
- [X] T008 [P] [US1] Write failing all-or-nothing validator/index mutation tests using seeded prior bindings in `tests/Elsa/Workflows/Runtime/Tests/WorkflowTriggerIndexerTests.cs`
- [X] T009 [P] [US1] Replace the exhausted-Cron skip expectation with fail-before-inner/store-mutation tests; add invalid-later-node, inner-failure, and successful replacement ordering cases in `tests/Elsa/Workflows/Runtime/Scheduling/Tests/RecurringTriggerScheduleIndexerTests.cs`
- [X] T010 [P] [US1] Add first-party valid/invalid contract-matrix assertions for Event in `tests/Elsa/Activities/Runtime/Tests/EventTriggerStimulusProviderTests.cs`
- [X] T011 [P] [US1] Add first-party valid/invalid and trigger/schedule identity-parity assertions for Timer/Cron in `tests/Elsa/Activities/Scheduling/Tests/TimerCronProviderTests.cs`
- [X] T012 [P] [US1] Add first-party multi-binding, invalid-routing-identity, and provider-id assertions for HttpEndpoint in `tests/Elsa/Activities/Http/Tests/HttpEndpointTriggerStimulusProviderTests.cs`

### Implementation for User Story 1

- [X] T013 [US1] Implement all-provider evaluation, exact-one claim enforcement, provider-id validation, descriptor duplicate validation, and complete preflight outcomes in `src/Elsa/Workflows/Runtime/Services/WorkflowTriggerBindingExtractor.cs`
- [X] T014 [US1] Make `WorkflowTriggerIndexer` validate and apply the completed preflight binding set without changing delete/save/observer semantics in `src/Elsa/Workflows/Runtime/Services/WorkflowTriggerIndexer.cs`
- [X] T015 [US1] Pre-materialize the complete Timer/Cron schedule set before the inner indexer; wrap calculator/parser failures in contextual typed failures while preserving `InnerException`, reject exhausted schedules, and persist only prepared schedules in `src/Elsa/Workflows/Runtime/Scheduling/RecurringTriggerScheduleIndexer.cs`
- [X] T016 [P] [US1] Add explicit stable provider ids to Event in `src/Elsa/Activities/Primitives/Activities/EventTriggerStimulusProvider.cs`
- [X] T017 [P] [US1] Add explicit stable provider ids to Timer and Cron in `src/Elsa/Activities/Scheduling/Activities/TimerTriggerStimulusProvider.cs` and `src/Elsa/Activities/Scheduling/Activities/CronTriggerStimulusProvider.cs`
- [X] T018 [P] [US1] Add the explicit stable provider id to HttpEndpoint in `src/Elsa/Activities/Http/Activities/HttpEndpointTriggerStimulusProvider.cs`
- [X] T019 [US1] Add real publish-level completeness and preservation proofs: exhausted Cron with the recurring decorator preserves seeded bindings/schedules in `tests/Elsa/Workflows/Runtime/Scheduling/Tests/RecurringTriggerScheduleIndexerTests.cs`, while a matrix-driven Event/Timer/Cron/HttpEndpoint set proves valid publications produce complete expected bindings (with companion Timer/Cron schedules asserted by the scheduling test) and invalid publications preserve prior registrations in `tests/Elsa/Workflows/Publishing/Api/Tests/PublishWorkflowTriggerIndexingTests.cs`
- [X] T020 [US1] Run every P1 focused command from sections 1–3 of `specs/090-trigger-contract-hardening/quickstart.md`, including the Publishing.Api matrix added by T019, and confirm every new test first failed then passes

**Checkpoint**: Invalid/unmaterializable first-party start triggers fail before trigger/schedule mutation; valid triggers still register completely.

---

## Phase 4: User Story 2 - Preserve Explicit Non-Start Intent (Priority: P2)

**Goal**: Keep recognized-empty HttpEndpoint behavior successful and provider-identifiable.

**Independent Test**: Publish an explicit/unauthored non-starting HttpEndpoint and verify a provider-owned `IntentionallyNonStarting` outcome, zero start bindings, and unchanged mid-flow behavior.

### Tests for User Story 2

- [X] T021 [P] [US2] Write failing recognized-empty outcome/provider-id tests in `tests/Elsa/Workflows/Runtime/Tests/WorkflowTriggerBindingExtractorTests.cs`
- [X] T022 [P] [US2] Extend unauthored/false `CanStartWorkflow` coverage to assert provider identity and zero descriptors in `tests/Elsa/Activities/Http/Tests/HttpEndpointTriggerStimulusProviderTests.cs`
- [X] T023 [P] [US2] Preserve direct-run/mid-flow suspension regression coverage in `tests/Elsa/Activities/Http/Tests/HttpEndpointExecutionTests.cs`

### Implementation for User Story 2

- [X] T024 [US2] Map a single provider's recognized-empty result to `IntentionallyNonStarting` without emitting bindings or failing preflight in `src/Elsa/Workflows/Runtime/Services/WorkflowTriggerBindingExtractor.cs`
- [X] T025 [US2] Verify the HttpEndpoint provider keeps absent/false activation as `Recognized([])` while reporting its stable id in `src/Elsa/Activities/Http/Activities/HttpEndpointTriggerStimulusProvider.cs`
- [X] T026 [US2] Run the P2 focused Runtime and Activities.Http test filters from `specs/090-trigger-contract-hardening/quickstart.md`

**Checkpoint**: Intentional non-start remains distinct from unrecognized/invalid and creates no start registration.

---

## Phase 5: User Story 3 - Republish Existing Definitions Safely (Priority: P3)

**Goal**: Prove catalog, executable, and durable trigger state compatibility while corrected behavior applies on republish.

**Independent Test**: Load supported historical catalog/executable/binding shapes and republish representative first-party workflows without same-version hash conflicts or runtime Design reads.

### Tests for User Story 3

- [X] T027 [P] [US3] Expand legacy CLR trigger compilation/republication coverage across the approved first-party classification contract in `tests/Elsa/Workflows/Publishing/Api/Tests/WorkflowExecutableCompilerTests.cs`
- [X] T028 [P] [US3] Add publish-path compatibility and seeded-prior-binding preservation cases in `tests/Elsa/Workflows/Publishing/Api/Tests/PublishWorkflowTriggerIndexingTests.cs`
- [X] T029 [P] [US3] Pin same-version CLR catalog Action/hash compatibility for trigger annotations in `tests/Elsa/Activities/Design/Tests/Unit/ClrAssemblyScannerTests.cs`
- [X] T030 [P] [US3] Confirm executable and trigger-binding serialized shapes remain unchanged using existing goldens in `tests/Elsa/Workflows/Publishing/Api/Tests/WorkflowExecutableCompilerGoldenTests.cs` and `tests/Elsa/Persistence/Groundwork/Tests/GroundworkRuntimeDocumentFixtureTests.cs`

### Implementation for User Story 3

- [X] T031 [US3] Verify the additive default interface members are source/binary compatible with existing provider/extractor implementors, record the Runtime Core MINOR classification and required release-version action under the repository's packaging mechanism, and confirm publishing/compiler entry-point signatures remain unchanged in `specs/090-trigger-contract-hardening/plan.md`, `src/Elsa/Workflows/Runtime/Core/Elsa.Workflows.Runtime.Core.csproj`, `src/Elsa/Workflows/Runtime/Core/Contracts/IActivityTriggerStimulusProvider.cs`, and `src/Elsa/Workflows/Runtime/Core/Contracts/IWorkflowTriggerBindingExtractor.cs`
- [X] T032 [US3] If T030 detects any durable shape drift, STOP implementation and amend `specs/090-trigger-contract-hardening/spec.md`, `specs/090-trigger-contract-hardening/plan.md`, and `specs/090-trigger-contract-hardening/tasks.md` for explicit migration approval before touching `src/Elsa/Persistence/Groundwork/Serialization/ElsaRuntimeDocumentVersions.cs`; otherwise record that no migration was needed in `specs/090-trigger-contract-hardening/quickstart.md`
- [X] T033 [US3] Run the compatibility and boundary commands from sections 4–5 of `specs/090-trigger-contract-hardening/quickstart.md`

**Checkpoint**: Historical shapes remain readable, catalog hashes remain stable, and republish produces the hardened contract without runtime Design dependencies.

---

## Phase 6: Documentation, Maps, and Full Verification

**Purpose**: Finish the approved unit without absorbing Unit B or Unit C.

- [ ] T034 [P] Relocate the canonical Runtime extension-point catalog from `src/Elsa/Workflows/Runtime/Core/EXTENSION_POINTS.md` to `src/Elsa/Workflows/Runtime/EXTENSION_POINTS.md`, update `EXTENSION_POINTS.md` to link to the composition-root catalog, classify trigger providers as an exact-one Strategy set, and document provider identity, XML failure contracts, and recurring pre-materialization
- [ ] T035 [P] Reconcile as-built behavior and task completion notes in `specs/090-trigger-contract-hardening/{spec.md,plan.md,research.md,data-model.md,contracts/trigger-publication-contract.md,contracts/trigger-contract-matrix.md,quickstart.md,tasks.md`
- [ ] T036 Run `bash tools/maps/generate-extension-point-map.sh`, review the generated findings report, and include only expected snapshots under `docs/maps/` and `docs/reports/`
- [ ] T037 Run `dotnet build Elsa.Server.slnx` and `dotnet test Elsa.Server.slnx`; record exact results in the implementation handoff/PR and investigate any regression before completion
- [ ] T038 Perform a final scope audit against `specs/090-trigger-contract-hardening/spec.md`: no diagnostics API/status persistence, CShells/startup-health changes, Studio work, route-table invalidation, router/actor redesign, or publication-wide transactionality

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1**: Starts immediately.
- **Phase 2**: Depends on baseline capture; blocks all stories.
- **US1 (Phase 3)**: Depends on Phase 2 and is the MVP.
- **US2 (Phase 4)**: Depends on the US1 preflight evaluator but is independently verified through recognized-empty behavior.
- **US3 (Phase 5)**: Depends on the final US1/US2 contract shape so compatibility fixtures pin the delivered model.
- **Phase 6**: Depends on the stories selected for delivery; for full Unit A completion, all three stories are required.

### User Story Dependencies

```text
Foundational vocabulary
        |
        v
US1 exact-one + fail-before-mutation (MVP)
        |
        +----------> US2 intentional non-start
        |
        +----------> US3 compatibility proof (after contract stabilizes)
                         |
                         v
                docs/maps/full verification
```

### Within Each User Story

- Write and run the story's tests first; confirm the intended failure.
- Add models/contracts before dependent implementation.
- Finish all deterministic preflight work before any store mutation.
- Preserve existing provider-specific validators and observer ordering.
- Complete the story's focused checkpoint before starting the next priority.

### Parallel Opportunities

- T004 and T005 can run in parallel after T003 defines expected behavior.
- T007–T012 touch separate test files except where noted and can be split across agents.
- T016–T018 change different first-party provider packages and can run in parallel after T013 fixes the shared contract.
- T021–T023 can run in parallel.
- T027–T030 can run in parallel after US1/US2 stabilize.
- T034 and T035 can run in parallel before map generation and full verification.

## Parallel Example: User Story 1

```text
Agent A: T007 + T013 — shared extractor exact-one/preflight behavior
Agent B: T009 + T015 + T019 — recurring schedule pre-materialization
Agent C: T010/T011/T012 + T016/T017/T018 — first-party provider matrix and ids
Integrator: T008 + T014 + T020 — indexer all-or-nothing validation and focused gate
```

## Implementation Strategy

### MVP First

1. Complete baseline and foundational vocabulary (T001–T006).
2. Complete US1 tests and implementation (T007–T020).
3. Stop and validate the P1 checkpoint before adding non-start/compatibility follow-ups.

### Incremental Delivery

1. US1 closes silent invalid/exhausted publication and is independently reviewable.
2. US2 proves the hardening does not regress intentional non-start behavior.
3. US3 pins upgrade compatibility once the contract is stable.
4. Documentation/maps/full suite close the work unit without broadening it.

## Notes

- `[P]` means different files or independently reviewable provider packages; shared Core files remain serialized.
- No task authorizes implementation of Unit B diagnostics or Unit C composition/startup health.
- T032 is a mandatory stop gate, not authorization to create a migration inside Unit A.
- Publication-wide transactional repair remains a separately approved future unit.

# Tasks: Activity Input Editor Options

**Input**: Design documents in `specs/090-activity-input-editor-options/`
**Tests**: Required by the approved specification and implementation plan.

## Phase 1: Setup

- [x] T001 Verify clean paired feature worktrees and ignore rules in `elsa-foundation/.gitignore` and `elsa-foundation-studio/.gitignore`
- [x] T002 [P] Add shared backend descriptor/provider fixtures in `tests/Elsa/Activities/Design/Tests/ClrFixture/FixtureActivities.cs`
- [x] T003 [P] Add shared Studio descriptor fixtures in `../elsa-foundation-studio/src/Elsa.Studio.Workflows/Client/src/__tests__/fixtures/activityInputOptions.ts`

## Phase 2: Foundational Contracts

- [x] T004 Add activity input option attributes and canonical UI-hint constants in `src/Elsa/Activities/Runtime/Core/`
- [x] T005 [P] Add dynamic provider contribution models/contracts in `src/Elsa/Workflows/Design/Core/`
- [x] T006 [P] Add typed option/provider UI-specification SDK contracts in `../elsa-foundation-studio/src/Elsa.Studio.Web/Client/src/sdk/index.ts`

## Phase 3: User Story 1 — Author constrained activity inputs (P1)

**Goal**: CLR activity authors declare valid options that are validated and cataloged.

**Independent Test**: Scan fixture activities and inspect `InputDefinition` UI metadata and failures.

- [x] T007 [US1] Add scanner tests for string, typed, enum, inheritance, ordering, and invalid metadata in `tests/Elsa/Activities/Design/Tests/Unit/ClrAssemblyScannerTests.cs`
- [x] T008 [US1] Implement reflection-only option metadata reading and validation in `src/Elsa/Activities/Design/Reconciliation/Clr/Services/ClrAssemblyScanner.cs`
- [x] T009 [US1] Add descriptor projection/round-trip coverage in `tests/Elsa/Modularity/Tests/WorkflowManagementDescriptorProjectionTests.cs`
- [x] T010 [US1] Annotate `SupportedMethods` in `src/Elsa/Activities/Http/Activities/HttpEndpoint.cs`
- [x] T011 [US1] Verify `SupportedMethods` metadata in `tests/Elsa/Activities/Http/Tests/HttpEndpointMetadataTests.cs`

## Phase 4: User Story 2 — Select allowable values in Studio (P1)

**Goal**: Studio renders descriptor-driven dropdowns and checklists while preserving typed and stale values.

**Independent Test**: Render scalar and collection fixtures and author values without a dynamic provider.

- [x] T012 [US2] Add editor-resolution tests for defaults and explicit collection hints in `../elsa-foundation-studio/src/Elsa.Studio.Web/Client/src/__tests__/registry.test.ts`
- [x] T013 [US2] Add rendered editor tests for typed and stale values in `../elsa-foundation-studio/src/Elsa.Studio.Web/Client/src/__tests__/property-editors.test.tsx`
- [x] T014 [US2] Implement canonical option parsing, hint-aware selection, and stale-value rendering in `../elsa-foundation-studio/src/Elsa.Studio.Web/Client/src/app/propertyEditors.tsx`
- [x] T015 [US2] Integrate explicit collection hint behavior in `../elsa-foundation-studio/src/Elsa.Studio.Workflows/Client/src/ActivityPropertiesPanel.tsx`
- [x] T016 [US2] Add token-compliant unavailable/error styles in `../elsa-foundation-studio/src/Elsa.Studio.Workflows/Client/src/styles.css`

## Phase 5: User Story 3 — Resolve context-dependent options (P2)

**Goal**: Design-side providers resolve options from validated current workflow context and Studio refreshes dependencies safely.

**Independent Test**: Register a provider, call the operation with workflow context, then exercise load, refresh, cancellation, failure, stale-value, and retry UI states.

- [x] T017 [US3] Add keyed resolver tests for unique, duplicate, missing, failing, and cancelled providers in `tests/Elsa/Modularity/Tests/`
- [x] T018 [US3] Implement keyed provider resolution and duplicate-key startup validation in `src/Elsa/Workflows/Design/Api/Services/ActivityInputOptionsProviderResolver.cs` and `src/Elsa/Workflows/Design/Api/WorkflowsDesignApiFeature.cs`
- [x] T019 [US3] Add workflow-management operation tests for request validation, context, status codes, and no-store behavior in `tests/Elsa/Modularity/Tests/`
- [x] T020 [US3] Implement the dynamic options route and response mapping in `src/Apps/Elsa.Server/ElsaWorkflowManagementApi.cs`
- [x] T021 [P] [US3] Add Studio API-client request/response support in `../elsa-foundation-studio/src/Elsa.Studio.Workflows/Client/src/api/workflows.ts`
- [x] T022 [US3] Add dynamic loading/dependency/cancellation/retry tests in `../elsa-foundation-studio/src/Elsa.Studio.Workflows/Client/src/__tests__/`
- [x] T023 [US3] Implement provider option loading with 150 ms dependency debounce and cancellation in `../elsa-foundation-studio/src/Elsa.Studio.Workflows/Client/src/ActivityPropertiesPanel.tsx`

## Phase 6: Polish & Cross-Cutting Concerns

- [x] T024 Document the provider extension point in `EXTENSION_POINTS.md` and cross-link the work-unit contracts
- [x] T025 Run targeted foundation tests and the relevant full solution build from `elsa-foundation/`
- [x] T026 Run Studio Vitest, typecheck, CSS lint, and production builds from `../elsa-foundation-studio/`
- [x] T027 Refresh the narrow generated activity/domain, extension-point, and feature-dependency maps and review findings
- [x] T028 Validate `specs/090-activity-input-editor-options/quickstart.md` end to end and mark all tasks complete
- [x] T029 Review both repository diffs for DRYness, compatibility, generated noise, and unrelated changes
- [x] T030 Commit the completed work locally in each repository with descriptive messages

## Dependencies

- Phase 1 → Phase 2 → US1/US2.
- US3 depends on the provider metadata from US1 and the constrained editors from US2.
- Documentation, map refresh, full validation, review, and commits follow all user stories.

## Parallel Opportunities

- T002, T003, T005, and T006 touch independent repositories/files.
- After foundational contracts, US1 backend scanner work and US2 Studio static-editor work can proceed in parallel.
- T021 can proceed while backend resolver/endpoint work is underway once the API contract is fixed.

## Implementation Strategy

1. Deliver static authoring and Studio rendering as the independently testable P1 slice.
2. Layer dynamic provider resolution on the same canonical option shape.
3. Finish with the `SupportedMethods` reference scenario, cross-repository validation, maps, review, and separate local commits.

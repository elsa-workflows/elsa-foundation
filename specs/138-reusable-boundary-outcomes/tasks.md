# Tasks: Reusable Activity Boundary Outcomes

**Input**: Design documents from `/specs/138-reusable-boundary-outcomes/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Tests are mandatory for every behavior branch and must be written and observed failing before implementation.

## Phase 1: Provider schema and validation foundation

- [x] T001 Add failing schema-2 parse/canonicalization tests in `tests/Elsa/Activities/Graph/Tests/GraphActivityProviderTests.cs`.
- [x] T002 Implement schema-aware manifests and `outcomeMappings` in `src/Elsa/Activities/Graph/Design/Models/ActivityGraphManifest.cs`.
- [x] T003 Add failing proposal/validation/compilation tests for total, unique, reachable mappings and schema-1 compatibility in `tests/Elsa/Activities/Graph/Tests/GraphActivityProviderTests.cs`.
- [x] T004 Implement schema-aware provider capabilities, dependency-aware mapping validation, and compiled mappings in `src/Elsa/Activities/Graph/Design/Services/GraphActivityProvider.cs`.

## Phase 2: User Story 1 - Branch on reusable activity results (P1)

- [x] T005 [US1] Add failing mapped/unmapped/ambiguous child completion and checkpoint tests in `tests/Elsa/Activities/Graph/Tests/GraphActivityExecutionTests.cs`.
- [x] T006 [US1] Add runtime mapping descriptors and selected-outcome propagation in `src/Elsa/Activities/Graph/Runtime/Models/GraphActivityDescriptor.cs` and `Runtime/Activities/GraphActivity.cs`.
- [x] T007 [US1] Add an end-to-end graph-boundary/parent-flowchart test proving only the matching branch executes.

## Phase 3: User Story 2 - Preserve existing reusable activities (P2)

- [x] T008 [US2] Verify unchanged schema-1 proposal, validation, canonicalization, descriptor deserialization, and implicit `done` execution tests.
- [x] T009 [US2] Update `TestScripts/reusable-activities/Test-ReusableActivityOutcomeLimit.ps1` from limitation proof to multi-outcome acceptance proof.

## Phase 4: User Story 3 - Author and connect every boundary outcome (P3)

- [x] T010 [US3] Add a failing publication test for the `elsa.outcomes` catalog facet in `tests/Elsa/Workflows/Publishing/Api/Tests/ActivityDefinitionPublicationTests.cs`.
- [x] T011 [US3] Project emitted contract outcomes into generic catalog ports and source-owned publication contracts in `src/Elsa/Workflows/Publishing/Api/Services/ActivityDefinitionPublisher.cs`, `SourceOwnedActivityVersionPublisher.cs`, and the authoring catalog API.
- [x] T012 [US3] Add failing Studio tests for exact schema-2 contribution registration, mapping preservation/editing, contract filtering, and read-only behavior.
- [x] T013 [US3] Extend Studio editor props with the current draft contract and pass it from `ActivityDefinitionDraftEditor.tsx`.
- [x] T014 [US3] Add the exact schema-2 graph contribution and boundary outcome mapping UI while preserving schema-1 behavior.

## Phase 5: Polish and verification

- [ ] T015 Run Foundation focused Graph and Publishing test projects, then the full required solution suite.
- [x] T016 Run Studio focused tests, typecheck, and build/lint checks required by its repository.
- [x] T017 Validate `quickstart.md`, inspect diffs for DRY/compatibility issues, and mark completed tasks.
- [x] T018 Run independent standards and specification reviews against pre-change Foundation HEAD `67efaa76b719301c16a1fc017bdc93e17e660515` and Studio HEAD `33c88a4f`.
- [x] T019 Commit each repository locally with useful messages; do not push or open PRs without a configured Git operating-model preference.

## Dependencies

- T001 → T002 → T003 → T004
- T004 → T005 → T006 → T007
- T002/T004/T006 → T008/T009
- T010 → T011
- T004 and T011 → T012 → T013/T014
- All implementation tasks → T015/T016/T017 → T018 → T019

## Independent completion criteria

- **US1**: A schema-2 reusable graph maps a direct-entry completion to one public outcome and a parent executes only the matching branch.
- **US2**: All schema-1 tests remain green and old artifacts still complete with `done`.
- **US3**: Published emitted outcomes appear as catalog ports and Studio authors/preserves valid mappings.

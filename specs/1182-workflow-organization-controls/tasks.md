# Tasks: Workflow organization controls

**Input**: [spec](spec.md), [plan](plan.md), [research](research.md), [contract](contracts/organization.md), [validation guide](quickstart.md).

## Phase 1: Setup

- [x] T001 Claim #3595, allocate spec 1182 against current/archive/open-PR numbers, and create the branch/spec in `specs/1182-workflow-organization-controls/spec.md`.
- [x] T002 Review both constitutions and the specification/plan/research/contracts before implementation in `specs/1182-workflow-organization-controls/plan.md`.

## Phase 2: Shared foundations

- [x] T003 Extend existing host/probe seams for configured availability and retained-state observations in `Essentials/tests/Workflows/Design/Api/Tests/WorkflowLibraryApiContractTests.cs`; keep default host behavior unchanged.

## Phase 3: US1 — Configure availability

**Independent test**: One selected-backend Boolean review changes only that setting; refreshed Studio hides unsupported organization while ordinary paged browsing remains authorized and usable.

- [x] T004 [US1] Add causal default/on/off/store-absent, stale operation/placement and ordinary paging/authorization regressions in `Essentials/tests/Workflows/Design/Api/Tests/WorkflowLibraryApiContractTests.cs`; record the failing baseline.
- [x] T005 [US1] Add the default-true typed setting and internal immutable snapshot in `Essentials/src/Workflows/Design/Api/WorkflowsDesignApiFeature.cs`; preserve existing feature identity/composition and verify Boolean metadata/default/configured false.
- [x] T006 [US1] Gate organization discovery/actions/placement and metadata/permission hints while retaining store-based visibility and paging in `Essentials/src/Workflows/Design/Api/Capabilities/WorkflowLibraryCapabilitySource.cs`, `Services/WorkflowLibraryAccess.cs`, `Services/WorkflowLibraryProjection.cs`, `Endpoints/Library/WorkflowLibraryEndpoints.cs`, `Endpoints/Definitions/Add/Endpoint.cs`, `Endpoints/Definitions/List/Endpoint.cs` and `Endpoints/Definitions/IWorkflowDefinitionDetailsReader.cs`.
- [x] T007 [US1] Keep ordinary sort/publication support tied to advertised extended paged query grammar and guard each organization request by its own advertised relation in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/api/workflowDesign.ts`; add behavioral request tests.
- [x] T008 [US1] Verify the new Boolean descriptor through existing changed-fields-only/target-pinned review and cancel/Apply behavior in `Extensions/FeatureManagement/studio/Elsa.Studio.FeatureManagement/Client/src/__tests__/module.test.ts`; preserve existing generic settings UI.

## Phase 4: US2 — Retain organization and normal workflows

**Independent test**: Real configured off/restart/on retains exact organization identities/values, and normal authoring/publication plus existing published execution work while off under unchanged permissions.

- [x] T009 [US2] Extend an actual owned-host journey using `Studio/tests/live/workflow-library-journey.spec.ts` and the selected-backend setup from `specs/1178-studio-backends/quickstart.md`; capture parent/child folders, tags/assignments, private/shared views, favorites and publication identities before off.
- [x] T010 [US2] Prove reviewed false/true configuration, cancelled review, no unrelated setting/backend change, off-state direct refusal, ordinary edit/publish and published-artifact completion through the real-host journey in `Studio/tests/live/`; retain signed proof metadata in the PR evidence.
- [x] T011 [US2] Restart only owned hosts while disabled, re-enable and compare retained identities/revisions/values and restored saved columns using the same journey in `Studio/tests/live/`; qualify denied administration and source-owned setting behavior without changing their policy.

## Phase 5: US3 — Recover from unavailable organization

**Independent test**: A scoped URL does not broaden before explicit keyboard recovery; recovery retains ordinary filters/backend and saved columns, including partial operation support and delayed old responses.

- [x] T012 [US3] Add a pure unavailable-constraint/recovery helper and regression cases for folder, Unfiled, descendants, tags, favorites and view-only URLs in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/workflowDefinitionBrowseLocation.ts` and its existing tests; prove red/green.
- [x] T013 [US3] Add scoped browse hold/recovery, availability-filtered columns/metadata/actions and obsolete dialog/read retirement in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowDefinitions.tsx`, `WorkflowFolderNavigation.tsx`, `WorkflowOrganizationButton.tsx` and relevant support hooks; extend `workflowLibraryBrowse.test.tsx` and `workflowDefinitionRoute.test.tsx` for partial support and stale results.
- [x] T014 [US3] Extend `Studio/tests/browser/workflowLibraryFixture.tsx` and workflow-library browser coverage for desktop/320px keyboard recovery, Back/reload, retained column choices and no document overflow across existing appearance treatments.

## Phase 6: Documentation and delivery

- [x] T015 Document the existing feature's setting, defaults, data retention, capability refresh and unsupported-link behavior in `Essentials/src/Workflows/Design/Api/README.md` and `docs/program-goals/workflow-library-organization.md`; link canonical contracts rather than duplicating organization concepts.
- [x] T016 Run whole affected API and Workflows/Features suites, browser coverage, typecheck/lint, production builds/budget, targeted hosts, maps/architecture and actual live proof from `quickstart.md`; retain all statuses and counts without weakening tests.
- [x] T017 Open a draft PR for #3595; perform independent correctness/standards/acceptance review on the actual diff, verify/fix concrete findings and record each author-review round on the PR.
- [x] T018 Prepare the implementation PR's terminal spec status, remove the completed requirements checklist and link the canonical delivery tracking below.

## Delivery tracking

Hosted exact-head qualification, the normal authorized merge, actual resulting-main verification,
final receipts on #3595/PR/#2559 and release of the exact claim remain required by the specification.
Their current status and immutable run/commit receipts live on
[PR #3599](https://github.com/elsa-workflows/elsa-foundation/pull/3599) and
[issue #3595](https://github.com/elsa-workflows/elsa-foundation/issues/3595), following
[the repository merge gates](../../docs/contributing/merging.md). These external delivery checkpoints
are not recorded as completed by this implementation checklist.

## Dependencies and parallel opportunities

T001–T003 precede backend implementation. T004–T008 establish US1. US2 uses the real configured implementation, and US3 can be developed independently once the discovery contract is fixed. Backend/API and pure Studio recovery tests can be prepared independently; builds and owned-host mutations remain serial on this shared machine. Final review and delivery wait for every story and live proof.

## Implementation strategy

Deliver the backend setting and truthful operation behavior first, then Studio recovery/preservation, then actual on/off/on administration/runtime proof. There is no partial-delivery claim while a required story, gate or resulting-main proof remains outstanding. Behavioral tests are selected for risk; red/green is required for the claimed behavior changes, with no blanket cadence imposed on trivial code.

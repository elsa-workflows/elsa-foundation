# Tasks: Workflow Version Override

**Input**: Design documents from `specs/142-workflow-version-override/`

**Tests**: Required by the feature specification and Foundation agent contract. Write focused tests before implementation.

## Phase 1: Setup and shared assignment policy

- [x] T001 Finalize the HTTP/capability contract and ADR in `specs/142-workflow-version-override/contracts/workflow-version-override.openapi.yaml` and `docs/adr/0050-author-requested-forward-workflow-versions.md`
- [x] T002 [P] Add failing assignment/preflight policy tests under `tests/Elsa/Workflows/Design/Persistence/Core/Tests/`
- [x] T003 Implement one shared automatic/exact assignment assessment seam using `src/Elsa/Primitives/Primitives/Versioning/SemVer.cs` and `src/Elsa/Workflows/Design/Persistence/Core/Services/WorkflowVersionNumbering.cs`

## Phase 2: User Story 1 — Promote with an exact forward version (P1)

**Goal**: Promote a valid draft with a requested exact forward release or prerelease while preserving automatic next-major behavior.

**Independent Test**: Groundwork promotion produces the requested immutable label for forward release/prerelease inputs and unchanged labels for automatic requests.

- [x] T004 [US1] Add failing automatic/exact/prerelease Groundwork tests in `tests/Elsa/Workflows/Design/Persistence/Groundwork/Tests/GroundworkWorkflowDefinitionCommandTests.cs`
- [x] T005 [US1] Extend `PromoteDraft` and `IPromoteDraftToVersionCommand` with optional requested version in `src/Elsa/Workflows/Design/Api/Commands/WorkflowLifecycleCommands.cs` and `src/Elsa/Workflows/Design/Persistence/Core/Contracts/IPromoteDraftToVersionCommand.cs`
- [x] T006 [US1] Thread the requested version through `src/Elsa/Workflows/Design/Api/Handlers/WorkflowLifecycleHandlers.cs` and `src/Elsa/Workflows/Design/Api/Endpoints/Drafts/Promote.cs`
- [x] T007 [US1] Resolve and persist automatic/exact versions inside the existing locked atomic command in `src/Elsa/Workflows/Design/Persistence/Groundwork/Services/GroundworkPromoteDraftToVersionCommand.cs`

## Phase 3: User Story 2 — Reject unsafe requests before promotion (P1)

**Goal**: Provide a read-only authoritative assessment and atomically reject malformed, non-forward, duplicate, and racing requests.

**Independent Test**: Promotion preflight reports resolved automatic/exact candidate, baseline, readiness, and issues without writes; promotion repeats the checks under lock and creates no unsafe identity.

- [x] T008 [US2] Add failing preflight endpoint/service tests and no-write assertions in `tests/Elsa/Workflows/Design/Api/Tests/WorkflowDefinitionLifecycleContractTests.cs`
- [x] T009 [US2] Add failing malformed/equal/lower/build-metadata-duplicate/race tests in `tests/Elsa/Workflows/Design/Persistence/Groundwork/Tests/GroundworkWorkflowDefinitionCommandTests.cs`
- [x] T010 [US2] Implement the non-mutating promotion-version assessment contract/service in `src/Elsa/Workflows/Design/Persistence/Core/`
- [x] T011 [US2] Implement authorized `POST design/workflows/drafts/{draftId}/promotion-preflight` in `src/Elsa/Workflows/Design/Api/Endpoints/Drafts/`
- [x] T012 [US2] Map invalid-version and version-conflict outcomes to documented 400/409 responses in `src/Elsa/Workflows/Design/Api/`
- [x] T013 [US2] Reuse the assessment rules during locked Groundwork promotion and retain the unique store constraint as final race protection in `src/Elsa/Workflows/Design/Persistence/Groundwork/Services/GroundworkPromoteDraftToVersionCommand.cs`

## Phase 4: User Story 3 — Discover support and replay safely (P2)

**Goal**: Advertise additive support and bind retry identity to normalized assignment intent.

**Independent Test**: Capability discovery exposes both relations; identical replays return the committed version and mismatched mode/version material conflicts.

- [x] T014 [US3] Add failing capability tests in `tests/Elsa/Architecture/DomainApiCapabilityRegistrationTests.cs`
- [x] T015 [US3] Add failing identical/mismatched replay tests in `tests/Elsa/Workflows/Design/Persistence/Groundwork/Tests/GroundworkWorkflowDefinitionCommandTests.cs`
- [x] T016 [US3] Advertise templated `workflow-draft-promote-version-preflight` and `workflow-draft-promote-exact-version` relations in `src/Elsa/Workflows/Design/Api/Capabilities/WorkflowDesignApiCapabilities.cs`
- [x] T017 [US3] Include assignment mode and normalized requested version in `PromoteDraftRequestMaterial` in `src/Elsa/Workflows/Design/Persistence/Groundwork/Services/GroundworkPromoteDraftToVersionCommand.cs`
- [x] T018 [US3] Update the management OpenAPI promotion/preflight contract in `specs/092-domain-owned-apis/contracts/management-api.openapi.yaml`

## Phase 5: Verification and landing

- [x] T019 Run focused Persistence Core, Groundwork, Design API, and Architecture test projects
- [x] T020 Run the relevant rebuilt REST e2e workflow-design publication journey documented in `specs/142-workflow-version-override/quickstart.md`
- [x] T021 Audit tenant isolation, authorization, no-write preflight, automatic compatibility, stale-after-preflight rejection, and all ADR invariants

## Dependencies and execution order

- T001–T003 establish the one shared assignment rule and block both read and write paths.
- US1 and US2 are both P1: implement the mutation and preflight against the same policy seam before advertising support.
- US3 capability relations are published only after US1/US2 behavior and tests pass.
- Tests in each story are written first and observed failing before implementation.

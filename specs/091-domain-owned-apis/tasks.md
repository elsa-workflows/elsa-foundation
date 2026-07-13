# Tasks: Domain-Owned Management APIs

**Input**: Design documents from `specs/091-domain-owned-apis/`

**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/management-api.openapi.yaml`, `quickstart.md`

**Tests**: Required. Write or move the named tests before the corresponding implementation and confirm the new assertions fail for the expected reason.

**Organization**: Tasks are grouped by user story. Story phases follow the actual safety dependency order: retention (US3), publication authority (US2), canonical authoring (US4), capability discovery (US5), custom-host composition (US1), and coordinated Studio migration (US6).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel because it touches different files and has no dependency on an incomplete task
- **[Story]**: Maps the task to the specification user story
- Every task names its primary file or directory

## Phase 1: Setup and Durable Decisions

**Purpose**: Establish the decision record, terminology, projects, and companion worktree before behavior changes.

- [X] T001 Amend executable-retention roots and GC concurrency in `docs/adr/0040-one-artifact-store-with-reference-derived-lifetime.md`
- [X] T002 [P] Add the publication-slot authority, policy precedence, trigger cardinality, CAS, and projection reconciliation ADR in `docs/adr/0043-publication-slots-define-start-authority.md`
- [X] T003 [P] Distinguish client-visible API capabilities from composition features in `docs/glossary/elsa.md`
- [X] T004 [P] Scaffold `src/Elsa/Api/Capabilities/Elsa.Api.Capabilities.csproj` and `tests/Elsa/Api/Capabilities/Tests/Elsa.Api.Capabilities.Tests.csproj`, then add them to `Elsa.Server.slnx`
- [X] T005 [P] Scaffold `src/Elsa/Expressions/Api/Elsa.Expressions.Api.csproj` and `tests/Elsa/Expressions/Api/Tests/Elsa.Expressions.Api.Tests.csproj`, then add them to `Elsa.Server.slnx`
- [X] T006 Create the Studio worktree `/Users/sipke/.codex/worktrees/091-domain-owned-apis/elsa-foundation-studio` on branch `codex/091-domain-owned-apis` from current `origin/main`

---

## Phase 2: Foundational Contract and Persistence Preparation

**Purpose**: Prepare shared serialization, authorization, and contract-parity infrastructure used by multiple stories.

**⚠️ CRITICAL**: Complete this phase before changing stored Runtime or Publishing records.

- [X] T007 Inventory current Groundwork document versions, fixtures, indexes, and upcasters affected by publication identity in `src/Elsa/Persistence/Groundwork/` and record the migration matrix in `specs/091-domain-owned-apis/migration-matrix.md`
- [X] T008 [P] Add OpenAPI route/schema parity tests for `specs/091-domain-owned-apis/contracts/management-api.openapi.yaml` in `tests/Elsa/Architecture/ManagementApiContractTests.cs`
- [X] T009 [P] Verify canonical domain endpoints reuse the existing server-independent Problem Details mapping in `src/Elsa/Api/FastEndpoints/` and its integration coverage in `tests/Elsa/Api/FastEndpoints/Tests/`
- [X] T010 [P] Define and test reusable action-scoped management permission names for Design, Activity Design, Expressions, Publishing, Runtime, and capability discovery, and sweep every currently implemented management-domain endpoint source for `ConfigurePermissions` without `AllowAnonymous`, in `src/Elsa/Api/FastEndpoints/Constants/PermissionNames.cs` and `tests/Elsa/Architecture/EndpointSecurityTests.cs`; applying the names to new endpoints remains with T062 and the final all-slice/capability sweep remains with T105
- [X] T011 Record in `specs/091-domain-owned-apis/migration-matrix.md` that existing Runtime v1 fixtures are preserved and concrete version bumps/upcasters/current fixtures move with T034 while Publishing v1 fixtures move with implemented T042 store shapes; reject unsafe placeholder migration logic.

**Checkpoint**: Stored-shape changes have an explicit migration path and every new endpoint can use common security/error conventions.

---

## Phase 3: User Story 3 - Retain Executables Required by Workflow Executions (Priority: P1) 🎯 Safety MVP

**Goal**: Garbage collection never removes an executable pinned by a retained workflow execution and remains safe during concurrent publication.

**Independent Test**: Retire every source reference for an executable pinned by retained executions in every status, sweep GC, and resume/inspect successfully; after execution retention removes the final root, sweep and collect the artifact.

### Tests for User Story 3

- [X] T012 [P] [US3] Add failing retained-execution status and final-root collection cases in `tests/Elsa/Workflows/Runtime/Tests/WorkflowExecutableReferenceGarbageCollectorTests.cs`
- [X] T013 [P] [US3] Add failing GC-versus-new-root race and grace-period tests in `tests/Elsa/Workflows/Runtime/Tests/WorkflowExecutableReferenceGarbageCollectorConcurrencyTests.cs`
- [X] T014 [P] [US3] Add failing in-memory distinct pinned-artifact query tests in `tests/Elsa/Workflows/Runtime/Tests/WorkflowExecutionStateStoreRetentionTests.cs`
- [X] T015 [P] [US3] Add failing Groundwork distinct pinned-artifact query and restart tests in `tests/Elsa/Persistence/Groundwork/Tests/GroundworkWorkflowExecutionRetentionTests.cs`

### Implementation for User Story 3

- [X] T016 [US3] Add the retained executable-root query and execution removal/retention seam to `src/Elsa/Workflows/Runtime/Core/Contracts/IWorkflowExecutionStateStore.cs`
- [X] T017 [P] [US3] Implement distinct retained roots and retention removal in `src/Elsa/Workflows/Runtime/Services/InMemoryWorkflowExecutionStateStore.cs`
- [X] T018 [P] [US3] Implement provider-side retained-root projection/index querying in `src/Elsa/Persistence/Groundwork/Stores/GroundworkWorkflowExecutionStateStore.cs` and `src/Elsa/Persistence/Groundwork/ElsaRuntimeStorageManifest.cs`
- [X] T019 [US3] Add artifact creation/staging grace and final conditional root-check contracts to `src/Elsa/Workflows/Runtime/Core/Contracts/IWorkflowExecutableStore.cs` and `src/Elsa/Workflows/Runtime/ReferenceGarbageCollection/Options/WorkflowExecutableReferenceGarbageCollectionOptions.cs`
- [X] T020 [US3] Protect the live-reference plus retained-execution union and close check-delete races in `src/Elsa/Workflows/Runtime/Services/WorkflowExecutableReferenceGarbageCollector.cs`
- [X] T021 [US3] Wire the new retention dependencies in `src/Elsa/Workflows/Runtime/ReferenceGarbageCollection/WorkflowsRuntimeReferenceGarbageCollectionFeature.cs` and its registration tests
- [X] T022 [US3] Document the retained-root query and GC behavior in `src/Elsa/Workflows/Runtime/EXTENSION_POINTS.md` and the Runtime feature README
- [X] T023 [US3] Run the US3 commands from `specs/091-domain-owned-apis/quickstart.md` and record any provider-specific follow-up in `specs/091-domain-owned-apis/migration-matrix.md`

**Checkpoint**: US3 independently proves retained executions preserve executable availability.

---

## Phase 4: User Story 2 - Publish Safely to an Explicit Lifecycle Slot (Priority: P1)

**Goal**: Default publishing replaces authority safely; named slots enable deliberate coexistence; failed activation never disables the old publication.

**Independent Test**: Publish HTTP `/foo`, replace the default slot with `/bar`, verify only `/bar` starts new executions, then prove explicit named coexistence, exclusive conflicts, fan-out, CAS concurrency, and failed-candidate rollback.

### Tests for User Story 2

- [X] T024 [P] [US2] Add failing publication policy precedence and slot state-transition tests in `tests/Elsa/Workflows/Publishing/Api/Tests/PublicationPolicyTests.cs` and `PublicationSlotTests.cs`
- [X] T025 [P] [US2] Replace append-only expectations with failing default replacement, named coexistence, unpublish, and restore cases in `tests/Elsa/Workflows/Publishing/Api/Tests/PublishWorkflowRequestHandlerTests.cs`
- [X] T026 [P] [US2] Add failing preflight trigger-diff and cardinality cases in `tests/Elsa/Workflows/Publishing/Api/Tests/PublicationPreflightTests.cs`
- [X] T027 [P] [US2] Add failing concurrent CAS and projection-failure preservation tests in `tests/Elsa/Workflows/Publishing/Api/Tests/PublicationActivationTests.cs`
- [X] T028 [P] [US2] Add failing publication-scoped binding and schedule persistence tests in `tests/Elsa/Workflows/Runtime/Tests/WorkflowTriggerIndexerTests.cs` and `tests/Elsa/Workflows/Runtime/Scheduling/Tests/RecurringTriggerScheduleIndexerTests.cs`
- [X] T029 [P] [US2] Add failing HTTP exclusive-claim replacement and cross-slot collision tests in `tests/Elsa/Workflows/Runtime/Http/Tests/HttpEndpointRoutingUniquenessValidatorTests.cs`

### Implementation for User Story 2

- [X] T030 [P] [US2] Add publication policy, resolved intent, slot, record, trigger claim, projection intent, and lifecycle enums in `src/Elsa/Workflows/Publishing/Core/Models/`
- [X] T031 [P] [US2] Add CQS slot/publication/policy/preflight/activation contracts in `src/Elsa/Workflows/Publishing/Core/Contracts/`
- [X] T032 [US2] Implement policy resolution and validation in `src/Elsa/Workflows/Publishing/Api/Services/PublicationPolicyResolver.cs`
- [X] T033 [US2] Implement in-memory slot, publication, policy, and projection-intent stores in `src/Elsa/Workflows/Publishing/Api/Services/`
- [X] T034 [US2] Extend source references, trigger bindings, and recurring schedules with publication/slot identity in `src/Elsa/Workflows/Runtime/Core/Models/` and update Groundwork versions/upcasters/fixtures in `src/Elsa/Persistence/Groundwork/`
- [X] T035 [US2] Add trigger cardinality to extraction contracts and declare HTTP Exclusive in `src/Elsa/Workflows/Runtime/Core/Models/` and `src/Elsa/Activities/Http/`
- [X] T036 [US2] Add publication-scoped list/delete/prepare/activate operations to Runtime trigger and schedule stores in `src/Elsa/Workflows/Runtime/Core/Contracts/` and their in-memory/Groundwork implementations
- [X] T037 [US2] Implement candidate extraction, trigger diffing, and conflict validation in `src/Elsa/Workflows/Publishing/Api/Services/PublicationPreflight.cs`
- [X] T038 [US2] Implement CAS activation and old-authority preservation in `src/Elsa/Workflows/Publishing/Api/Services/PublicationActivator.cs`
- [X] T039 [US2] Implement durable idempotent projection reconciliation in `src/Elsa/Workflows/Publishing/Api/Services/PublicationProjectionReconciler.cs`
- [X] T040 [US2] Refactor `src/Elsa/Workflows/Publishing/Api/Handlers/PublishWorkflowRequestHandler.cs` to use preflight, policy resolution, slot activation, and publication-scoped references
- [X] T041 [US2] Add preflight, publish, slot list/detail/unpublish/restore, and workflow-policy endpoints under `src/Elsa/Workflows/Publishing/Api/Endpoints/`
- [X] T042 [US2] Add provider-neutral Publishing Groundwork persistence in `src/Elsa/Workflows/Publishing/Persistence/Groundwork/` with CAS, indexes, and restart tests in `tests/Elsa/Workflows/Publishing/Persistence/Groundwork/Tests/`
- [X] T043 [US2] Update HTTP route-table and recurring-schedule observers to switch only authoritative publication projections in `src/Elsa/Workflows/Runtime/Http/` and `src/Elsa/Workflows/Runtime/Scheduling/`
- [X] T044 [US2] Document publication extension points/tasks in `src/Elsa/Workflows/Publishing/Api/README.md` and `src/Elsa/Workflows/Publishing/Api/EXTENSION_POINTS.md`
- [X] T045 [US2] Run the US2 commands and `/foo` to `/bar` scenario from `specs/091-domain-owned-apis/quickstart.md`

**Checkpoint**: US2 independently proves safe replacement and explicit coexistence.

---

## Phase 5: User Story 4 - Author Workflows Through Canonical Domain Contracts (Priority: P2)

**Goal**: Studio-ready authoring, activity, expression, executable inspection, and runtime diagnostic contracts live in their canonical domains.

**Independent Test**: Create a definition with authored state, update/promote/discard a first-class draft, query real versions and aggregate summaries, soft-delete/restore, load one activity catalog, resolve analysis/options/descriptors, and inspect the pinned Runtime executable without Elsa.Server.

### Tests for User Story 4

- [X] T046 [P] [US4] Move and expand definition/draft/version lifecycle tests into `tests/Elsa/Workflows/Design/Api/Tests/`
- [X] T047 [P] [US4] Add failing aggregate definition projection query-count tests in `tests/Elsa/Workflows/Design/Api/Tests/WorkflowDefinitionProjectionTests.cs`
- [X] T048 [P] [US4] Move scoped-variable and contextual input-option route tests from `tests/Elsa/Modularity/Tests/` to `tests/Elsa/Workflows/Design/Api/Tests/`
- [X] T049 [P] [US4] Add failing normalized authoring catalog and availability-filter tests in `tests/Elsa/Activities/Design/Api/Tests/ActivityAuthoringCatalogTests.cs`
- [X] T050 [P] [US4] Move expression and variable-type projection tests into `tests/Elsa/Expressions/Api/Tests/ExpressionDescriptorEndpointTests.cs`
- [X] T051 [P] [US4] Move executable inspector tests from Publishing into `tests/Elsa/Workflows/Runtime/Api/Tests/WorkflowExecutableInspectorTests.cs`
- [X] T052 [P] [US4] Add canonical Runtime diagnostics route/security tests in `tests/Elsa/Workflows/Runtime/Api/Tests/RuntimeDiagnosticsEndpointTests.cs`

### Implementation for User Story 4

- [X] T053 [US4] Extend definition creation and list projections without concrete root kinds or N+1 reads in `src/Elsa/Workflows/Design/Api/`
- [ ] T054 [US4] Separate metadata patching from first-class draft GET/PUT/promote/discard endpoints in `src/Elsa/Workflows/Design/Api/Endpoints/`
- [ ] T055 [US4] Implement definition soft-delete/restore/permanent-delete and keep Publishing state untouched in `src/Elsa/Workflows/Design/Api/`
- [ ] T056 [US4] Reject synthetic draft identifiers and isolate direct version ingestion behind explicit authorization in `src/Elsa/Workflows/Design/Api/Endpoints/Versions/`
- [ ] T057 [US4] Move scoped-variable analysis and activity input-option resolution from the facade into `src/Elsa/Workflows/Design/Api/Endpoints/Authoring/`
- [X] T058 [US4] Implement the unified normalized authoring catalog in `src/Elsa/Activities/Design/Api/Endpoints/Catalog/` and retain canonical availability endpoints
- [X] T059 [US4] Implement `ExpressionsApiFeature`, descriptor endpoints, models, and route constants in `src/Elsa/Expressions/Api/`
- [ ] T060 [US4] Move `WorkflowExecutableInspector` and its views from Publishing into `src/Elsa/Workflows/Runtime/Api/` and add read-only provenance endpoints
- [ ] T061 [US4] Move runtime diagnostics settings to `/runtime/workflows/diagnostics/settings` in `src/Elsa/Workflows/Runtime/Api/Constants/RouteConstants.cs`
- [ ] T062 [US4] Add/update registration tests and endpoint security sweeps for all touched features in their `tests/Elsa/**/Api/Tests/` projects, asserting each endpoint applies its canonical action-scoped permission from `PermissionNames`
- [ ] T063 [US4] Update feature READMEs and domain extension-point catalogs in `src/Elsa/Workflows/Design/Api/`, `src/Elsa/Activities/Design/Api/`, `src/Elsa/Expressions/Api/`, and `src/Elsa/Workflows/Runtime/Api/`
- [ ] T064 [US4] Run the US4 domain API commands from `specs/091-domain-owned-apis/quickstart.md`

**Checkpoint**: US4 independently proves every authoring/read slice works without the facade.

---

## Phase 6: User Story 5 - Discover Client-Visible Capabilities Efficiently (Priority: P2)

**Goal**: One authenticated shell-scoped request advertises only explicit stable API contracts and canonical relative links.

**Independent Test**: Compose two shells with different API features, call `/capabilities` once per shell, and prove exact declarations, link isolation, optional link contribution, duplicate diagnostics, and permission-neutral output.

### Tests for User Story 5

- [ ] T065 [P] [US5] Add failing static declaration, dynamic Source, merge, ordering, and duplicate diagnostics tests in `tests/Elsa/Api/Capabilities/Tests/ApiCapabilityCatalogTests.cs`
- [ ] T066 [P] [US5] Add failing multi-shell route-base and omitted-domain tests in `tests/Elsa/Api/Capabilities/Tests/CapabilityEndpointTests.cs`
- [ ] T067 [P] [US5] Add failing secure-shell authentication and permission-neutral response tests in `tests/Elsa/Api/Capabilities/Tests/CapabilitySecurityTests.cs`
- [ ] T068 [P] [US5] Add failing feature dependency/declaration tests for all canonical domain API features in `tests/Elsa/Architecture/ApiCapabilityRegistrationTests.cs`

### Implementation for User Story 5

- [ ] T069 [P] [US5] Add API capability declaration, link, document, Source, event, and duplicate exception contracts in `src/Elsa/Api/Capabilities/`
- [ ] T070 [US5] Implement the single aggregating capability handler and deterministic catalog in `src/Elsa/Api/Capabilities/Services/ApiCapabilityCatalog.cs`
- [ ] T071 [US5] Implement `ApiCapabilitiesFeature` and authenticated `GET /capabilities` endpoint in `src/Elsa/Api/Capabilities/`
- [ ] T072 [US5] Add explicit static declarations and `ApiCapabilities` dependency metadata to Workflow Design, Activity Design, Expressions, Publishing, and Runtime API feature classes
- [ ] T073 [US5] Contribute conditional scoped-variable-analysis and other operational links through typed Sources in `src/Elsa/Workflows/Design/Api/Capabilities/` and other owning domain API capability folders
- [ ] T074 [US5] Document the capability contract and extension points in `src/Elsa/Api/Capabilities/README.md` and `src/Elsa/Api/Capabilities/EXTENSION_POINTS.md`
- [ ] T075 [US5] Run the multi-shell capability commands from `specs/091-domain-owned-apis/quickstart.md`

**Checkpoint**: US5 independently proves one-request discovery without feature-name inference.

---

## Phase 7: User Story 1 - Compose Management APIs Without Elsa.Server (Priority: P1)

**Goal**: A custom host installs selected domain APIs and completes representative management journeys with no copied server code.

**Independent Test**: Build a custom TestHost that references domain packages but not Elsa.Server, discover installed capabilities, and execute representative Design, Activity, Expressions, Publishing, Runtime, and optional-domain requests.

### Tests for User Story 1

- [ ] T076 [P] [US1] Add the custom-host composition fixture and representative journey tests in `tests/Elsa/Architecture/DomainManagementApiCompositionTests.cs`
- [ ] T077 [P] [US1] Add an architecture guard rejecting management endpoints and legacy route literals in `src/Apps/Elsa.Server` at `tests/Elsa/Architecture/ElsaServerReferenceCompositionTests.cs`
- [ ] T078 [P] [US1] Add contract inventory parity asserting every former facade operation has one canonical owner or removal rationale in `tests/Elsa/Architecture/ManagementApiOperationInventoryTests.cs`

### Implementation for User Story 1

- [ ] T079 [US1] Add the canonical API features to `src/Apps/Elsa.Server/shells.json` and `src/Apps/Elsa.Server/shells.baseline.json` as reference composition only
- [ ] T080 [US1] Remove `MapElsaWorkflowManagementApi` from `src/Apps/Elsa.Server/Program.cs` and delete `src/Apps/Elsa.Server/ElsaWorkflowManagementApi.cs`
- [ ] T081 [US1] Remove obsolete server project references and registrations from `src/Apps/Elsa.Server/Elsa.Server.csproj`
- [ ] T082 [US1] Delete or relocate all facade-specific tests from `tests/Elsa/Modularity/Tests/` after preserving their objectives in T048, T049, T050, T051, T076, and T078
- [ ] T083 [US1] Verify the custom-host and zero-legacy Foundation gates in `specs/091-domain-owned-apis/quickstart.md`

**Checkpoint**: US1 independently proves Elsa.Server is unnecessary for supported management clients.

---

## Phase 8: User Story 6 - Move Elsa Studio Without a Compatibility Facade (Priority: P2)

**Goal**: Studio uses capability-discovered canonical domain clients and the new lifecycle semantics, with no released broken interval.

**Independent Test**: Run all Workflows and Weaver journeys against Foundation with the facade removed; verify one cached capability bootstrap, no fallback/legacy request, correct publication UX, and Runtime-backed instance inspection.

### Tests for User Story 6

- [ ] T084 [P] [US6] Rewrite route and response mocks for canonical domain clients in `/Users/sipke/.codex/worktrees/091-domain-owned-apis/elsa-foundation-studio/src/Elsa.Studio.Workflows/Client/src/__tests__/`
- [ ] T085 [P] [US6] Add cached capability discovery and absent-capability UX tests in `/Users/sipke/.codex/worktrees/091-domain-owned-apis/elsa-foundation-studio/src/Elsa.Studio.Workflows/Client/src/__tests__/capabilities.test.tsx`
- [ ] T086 [P] [US6] Add publication preflight, default replacement, named-slot, unpublish, and restore UX tests in `/Users/sipke/.codex/worktrees/091-domain-owned-apis/elsa-foundation-studio/src/Elsa.Studio.Workflows/Client/src/__tests__/publicationSlots.test.tsx`
- [ ] T087 [P] [US6] Add Runtime-pinned instance/executable rendering tests in `/Users/sipke/.codex/worktrees/091-domain-owned-apis/elsa-foundation-studio/src/Elsa.Studio.Workflows/Client/src/__tests__/workflowInstances.test.tsx`
- [ ] T088 [P] [US6] Update Weaver executable-detail route tests in `/Users/sipke/.codex/worktrees/091-domain-owned-apis/elsa-foundation-studio/src/Elsa.Studio.Weaver.Workflows/Client/src/__tests__/module.test.tsx`

### Implementation for User Story 6

- [ ] T089 [P] [US6] Implement cached global discovery in `/Users/sipke/.codex/worktrees/091-domain-owned-apis/elsa-foundation-studio/src/Elsa.Studio.Workflows/Client/src/api/capabilities.ts`
- [ ] T090 [P] [US6] Split Workflow Design and Activity Design clients into `api/workflowDesign.ts` and `api/activityDesign.ts` under the Studio Workflows client
- [ ] T091 [P] [US6] Add Expressions and Runtime clients in `api/expressions.ts` and `api/runtime.ts` under the Studio Workflows client
- [ ] T092 [P] [US6] Add publication preflight/slot/policy/reference client in `api/publishing.ts` under the Studio Workflows client
- [ ] T093 [US6] Remove legacy base paths and fallback helper behavior from `api/workflows.ts`, preserving only a private barrel if needed
- [ ] T094 [US6] Replace `rootKind` creation with catalog-authored initial state in `workflow-editor/CreateWorkflowDialog.tsx`, `editorTypes.ts`, `editorHelpers.ts`, and Weaver workflow creation context
- [ ] T095 [US6] Consolidate editor bootstrap onto the authoring catalog and capability cache in `workflow-editor/useWorkflowEditorData.ts` and `workflow-editor/useWorkflowScope.ts`
- [ ] T096 [US6] Implement publication preflight, resolved policy/slot confirmation, explicit side-by-side naming, unpublish, and restore UX in `workflow-editor/useWorkflowOperations.ts`, `WorkflowEditor.tsx`, and `WorkflowExecutables.tsx`
- [ ] T097 [US6] Render instance state from the pinned Runtime executable instead of Design versions in `workflow-editor/WorkflowInstances.tsx` and `WorkflowExecutableInspector.tsx`
- [ ] T098 [US6] Move availability, expression, variable, input-option, and runtime-diagnostics screens/hooks to their domain clients in `workflow-editor/`
- [ ] T099 [US6] Update Weaver `getExecutableDetail` to the Runtime capability link in `/Users/sipke/.codex/worktrees/091-domain-owned-apis/elsa-foundation-studio/src/Elsa.Studio.Weaver.Workflows/Client/src/module.tsx`
- [ ] T100 [US6] Delete every legacy and demo fallback literal and update Studio contract documentation under `/Users/sipke/.codex/worktrees/091-domain-owned-apis/elsa-foundation-studio/specs/`
- [ ] T101 [US6] Run targeted Workflows/Weaver tests and full Studio typecheck/test/build/lint gates from `specs/091-domain-owned-apis/quickstart.md`

**Checkpoint**: US6 independently proves coordinated Studio compatibility with no facade.

---

## Phase 9: Polish and Cross-Cutting Completion

**Purpose**: Close documentation, generated-map, migration, security, performance, and release evidence across both repositories.

- [ ] T102 [P] Refresh the narrowest affected domain, extension-point, architecture-reference, and feature-dependency maps using `tools/maps/generate-*.sh` and review `docs/maps/manifest.json`
- [ ] T103 [P] Update root/package documentation and the old ADR 0041 disposition in `README.md`, `docs/adr/0041-workflow-management-advertises-optional-authoring-capabilities.md`, and affected feature READMEs
- [ ] T104 [P] Add definition-list bounded-query and capability-bootstrap request-count regression tests in `tests/Elsa/Workflows/Design/Api/Tests/` and Studio Workflows tests
- [ ] T105 [P] Complete endpoint authorization sweeps for every final domain slice (including Expressions and API Capabilities), assert the capability endpoint applies `PermissionNames.ApiCapabilitiesRead`, and add unauthenticated capability tests in `tests/Elsa/Architecture/EndpointSecurityTests.cs`
- [ ] T106 Verify all 74 functional requirements and 12 success criteria against code/tests and record evidence in `specs/091-domain-owned-apis/completion-audit.md`
- [ ] T107 Run every Foundation and Studio command in `specs/091-domain-owned-apis/quickstart.md` from clean worktrees and attach failures/evidence to `completion-audit.md`
- [ ] T108 Commit the coordinated Foundation and Studio changes, record both commit IDs in `specs/091-domain-owned-apis/completion-audit.md`, and confirm both worktrees are clean

---

## Dependencies and Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: Starts immediately.
- **Foundational (Phase 2)**: Depends on Phase 1 project/decision setup and blocks stored-record changes.
- **US3 Retention (Phase 3)**: Depends on Phase 2 and is the first safety prerequisite.
- **US2 Publication (Phase 4)**: Depends on US3 retention so replacement never destroys old execution artifacts.
- **US4 Canonical domains (Phase 5)**: Depends on US2 for the public Publishing contract; independent Design/Activity/Expressions/Runtime slices may begin after Phase 2 if they do not expose unsafe publication behavior.
- **US5 Capabilities (Phase 6)**: Depends on stable canonical links from US4 and Publishing routes from US2.
- **US1 Custom host/removal (Phase 7)**: Depends on US2, US4, and US5; facade removal occurs only after every operation has a canonical owner.
- **US6 Studio (Phase 8)**: Client tests and domain-client scaffolding may begin after the OpenAPI contract; final integration depends on US1 removal and all canonical endpoints.
- **Polish (Phase 9)**: Depends on all stories.

### User Story Dependency Graph

```text
US3 retained executable roots
  -> US2 publication authority
       -> US4 canonical domain APIs
            -> US5 capability discovery
                 -> US1 custom-host composition + facade removal
                      -> US6 coordinated Studio cutover
```

### Parallel Opportunities

- T001–T006 can proceed in parallel except Studio worktree creation must avoid an existing branch/path collision.
- T012–T015 are independent failing test groups; T017 and T018 implement separate stores.
- T024–T029 cover separate publication, trigger, schedule, and HTTP concerns.
- Within US4, Design, Activity Design, Expressions, and Runtime API slices can be implemented by separate workers after their tests exist.
- US5 unit tests can run in parallel with static declaration edits once the core capability contracts exist.
- Studio tests and domain client files T084–T092 are parallel by file area; integration tasks T093–T101 follow them.

## Parallel Execution Examples

### US3

```text
Worker A: T012, T014, T016, T017
Worker B: T015, T018
Worker C: T013, T019, T020
```

### US2

```text
Worker A: T024, T025, T030-T033, T040-T041
Worker B: T028, T034-T036, T043
Worker C: T026, T027, T029, T037-T039, T042
```

### US4 and US5

```text
Worker A: Workflow Design T046-T048, T053-T057
Worker B: Activity Design + Expressions T049-T050, T058-T059
Worker C: Runtime inspection/diagnostics T051-T052, T060-T061
Then: API Capabilities T065-T075
```

## Implementation Strategy

### Safety-First MVP

1. Complete Setup and Foundational phases.
2. Complete US3 retained executable roots and validate independently.
3. Complete US2 default-slot replacement and validate `/foo` to `/bar` independently.
4. Only then expose the corrected lifecycle through canonical APIs.

### Incremental Delivery

1. Retention correction + ADR 0040.
2. Publication slots/policies/atomic activation + ADR 0043.
3. Canonical domain API enrichment and Runtime inspector move.
4. Global capability aggregation and explicit declarations.
5. Custom-host proof and Elsa.Server facade deletion.
6. Studio cutover and cross-repository release validation.

## Notes

- No test is deleted unless its behavior objective has first been preserved in the named destination test and architect approval is recorded.
- `[P]` means file-level parallel safety, not permission to bypass phase dependencies.
- Physical executable deletion is never reintroduced as a normal management operation.
- Mark tasks complete only after their stated tests pass; update this file during implementation.

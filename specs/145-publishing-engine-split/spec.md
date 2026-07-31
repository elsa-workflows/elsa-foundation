# Feature Specification: Publishing engine / API split

**Feature Branch**: `145-publishing-engine-split`

**Created**: 2026-07-30

**Status**: Draft

**Input**: Extract the workflow publishing engine out of the `Elsa.Workflows.Publishing.Api` feature into a new endpoint-free engine feature, so the API feature becomes a pure endpoints/transport layer. Behaviour-preserving refactor; prerequisite to a later CatalogActivation feature.

## User Scenarios & Testing *(mandatory)*

The "users" of this refactor are **feature/shell composers** and **downstream feature authors** who consume the publishing capability, plus every **existing consumer** of the publishing API whose behaviour must not change.

### User Story 1 - Compose the publish engine without HTTP endpoints (Priority: P1)

A shell composer who is building a runtime-only node wants the full publishing capability — compile a definition version into a live executable (with its Published source reference and trigger index) — **without** mounting any publish HTTP endpoints. Today this is impossible: the engine is bundled inside the API feature, so enabling publishing always mounts the endpoints.

**Why this priority**: This is the core reason for the split and the thing that unblocks the CatalogActivation feature (PR2) — a runtime node must obtain the publish engine as a composable unit separate from the transport.

**Independent Test**: Compose a shell that enables the engine feature (`WorkflowsPublishing`) and does **not** enable the API feature; at the unit level resolve the publish command handler + executable compiler from DI, and assert no publish HTTP endpoint is mounted and `IActivityPublishingAuthorizationContext` is absent. The behavioural check — publish a version in-process and produce a live Published executable — is validated via `quickstart.md` (integration-flavoured, §2.23.6).

**Acceptance Scenarios**:

1. **Given** a shell with the engine feature enabled and the API feature disabled, **When** a caller sends the publish command in-process, **Then** the workflow version is compiled and a single live Published executable source reference is produced (identical to today's publish result).
2. **Given** that same shell, **When** the mounted HTTP route table is inspected, **Then** no publish/test-run endpoints are present.

---

### User Story 2 - Existing publishing API behaviour is unchanged (Priority: P1)

Every current consumer of the publishing API (Studio, integrators, existing tests) must observe **identical** behaviour after the split: the same endpoints, the same routes, the same publish results.

**Why this priority**: This is a behaviour-preserving refactor governed by the §2.21.1 golden rule. Regressing the public API surface would be an unacceptable side effect of an internal reorganisation.

**Independent Test**: Run the existing publishing test suite unchanged; enable the API feature in a shell and confirm the endpoint surface (routes + behaviour) matches the pre-split baseline.

**Acceptance Scenarios**:

1. **Given** the API feature enabled, **When** the publish endpoint is called, **Then** the outcome is identical to the pre-split behaviour.
2. **Given** the existing publishing unit/registration tests, **When** they run against the refactored code, **Then** their assertions pass unchanged; the only permitted change (§2.21.1) is test *wiring* — the registration test composes the `DependsOn`-activated engine feature alongside the API feature.

---

### User Story 3 - The API feature carries only transport (Priority: P2)

A feature author auditing the publishing domain expects the `.Api` feature to contain only endpoints/transport, with all orchestration and engine logic in the engine feature — matching the framework rule that a `.Api` package is transport, not a logic owner.

**Why this priority**: Correct layering is the durable value of the split; it makes the domain legible and prevents the API feature from re-accumulating logic.

**Independent Test**: Inspect the API feature's registration: beyond `base.ConfigureServices` (its `FastEndpointsFeatureBase` transport base) it adds only endpoints, API capabilities, the HttpContext authorization context, and activity-draft services; the workflow-publish orchestration handler and every workflow-publish engine collaborator resolve from the `DependsOn`-activated engine feature, not the API feature.

**Acceptance Scenarios**:

1. **Given** the refactored API feature, **When** its `ConfigureServices` is reviewed, **Then** it registers only endpoints, API capabilities, the HttpContext authorization context, and activity-draft services — with all workflow-publish engine registration coming from the `DependsOn`-activated engine feature.
2. **Given** a publish endpoint, **When** it handles a request, **Then** it only sends a mediator command and contains no orchestration logic.

---

### Edge Cases

- **Durable persistence override**: a durable persistence provider that overrides the in-memory publication/executable stores MUST continue to override them when the engine feature registers the in-memory defaults (same `TryAdd` seam, now owned by the engine feature).
- **API-only expectations**: any consumer that relied on the API feature to register engine *services* must still resolve them at runtime — because enabling the API feature transitively enables the engine feature via `DependsOn`. (Unit tests that call the Api feature's `ConfigureServices` *directly* — bypassing shell `DependsOn` resolution — must compose the engine feature too; a §2.21.1-permitted wiring change, see SC-001.)
- **Command relocation**: any code elsewhere in the repo that sends or handles the publish command must compile against the command's new `Publishing.Core` location.
- **Test-run and other publishing endpoints**: all publishing endpoints (not just publish) move to the transport layer together; none may be left registering engine services.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A new engine feature (ShellFeature `WorkflowsPublishing`, package `Elsa.Workflows.Publishing`) MUST register the **auth-free workflow-publish + compile core** currently registered by `WorkflowsPublishingApiFeature` — the executable compiler and its decomposition collaborators, the publication activator / projection reconciler / preflight, the publication slot/record/policy/intent stores and their in-memory defaults, the executable and source-reference stores, the layout/structure services, the deletion guard, and the **workflow**-publish orchestration request/command handler(s) — **except** endpoint and API-capability registration.
- **FR-001a**: The engine feature MUST NOT depend on any authorization/transport concern. `IActivityPublishingAuthorizationContext`, its HttpContext implementation, and the **activity-draft** publish/test-run services (`ActivityDefinitionPublisher`, `ActivityDraftTestRunService`, and their handlers/endpoints) that consume it MUST remain in the Api/transport layer. (Decoupling those activity-draft services' authorization so they can later move to an engine is a separate follow-up, out of scope here.)
- **FR-002**: The engine feature MUST NOT register or mount any HTTP endpoint or FastEndpoints transport, and MUST NOT depend on `ApiCapabilities`.
- **FR-003**: `WorkflowsPublishingApiFeature` MUST keep its `FastEndpointsFeatureBase` base and obtain the engine via composition, not inheritance: declare `DependsOn WorkflowsPublishing` (framework §2.11) so the shell activates the engine feature, and in its own `ConfigureServices` register ONLY the FastEndpoints endpoints, the API-capability declarations/sources, the HttpContext authorization context, and the activity-draft services.
- **FR-004**: The **workflow**-publish orchestration handler(s) MUST live in the engine feature's package. The workflow-publish API endpoint MUST become a pure sender that dispatches a mediator command and contains no orchestration logic (Variant 2).
- **FR-005**: The shared publish command/request contracts MUST live in `Elsa.Workflows.Publishing.Core`, referenced by both the API endpoint (sender) and the engine handler (receiver) so neither feature references the other's implementation package.
- **FR-006**: The refactor MUST be behaviour-preserving: enabling `WorkflowsPublishingApi` yields an identical endpoint surface and identical publish behaviour to the pre-split baseline, and all existing publishing tests pass without changes to the test cases (§2.21.1).
- **FR-007**: DependsOn MUST be assigned per responsibility — the engine feature declares `WorkflowsRuntimeTriggers` and `Events`; the API feature declares `WorkflowsPublishing` and `ApiCapabilities`.
- **FR-008**: The new engine feature MUST ship a §2.23.1 feature-registration test; §2.23.2 implementation tests for relocated logic MUST be preserved (moved, not rewritten) and continue to pass; both features MUST update §2.22 feature docs and the §2.22.1 `EXTENSION_POINTS` catalog; the new project MUST be added to `Elsa.Server.slnx`.
- **FR-009**: Relocating the publish command/request contract namespace to `Publishing.Core` MUST update every sender and handler reference across the repository (a complete blast-radius map is produced during planning) and be recorded as a MAJOR change for the affected package(s) per §4.2.
- **FR-010**: A shell composing the engine feature **without** the API feature MUST be able to publish a workflow version in-process and produce a live Published executable that the runtime can start (this is the capability PR2 will consume).

### Key Entities

- **WorkflowsPublishing feature (engine)**: the new endpoint-free activation unit that owns all publish engine services and the orchestration handler.
- **WorkflowsPublishingApi feature (transport)**: the slimmed activation unit that keeps its `FastEndpointsFeatureBase` base, `DependsOn`s the engine, and adds only endpoints + API capabilities + the HttpContext authorization context + activity-draft services.
- **Publish command/request contract**: the mediator message relocated to `Publishing.Core`, the seam between transport (sender) and engine (handler).
- **Publish orchestration handler**: the logic that compiles, persists, activates, and retires publications — relocated into the engine feature.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: All existing publishing tests preserve their subject and objective and pass. Assertions are unchanged; the one permitted change (framework §2.21.1) is test *wiring* — the registration test that today calls the Api feature's `ConfigureServices` directly and asserts engine services must now also compose the engine feature (since engine services arrive via `DependsOn`, not inheritance). Engine-service resolution assertions move to / are shared with the new engine registration test.
- **SC-002**: A shell composed with the engine feature and without the API feature mounts zero publish HTTP endpoints, yet can publish a version in-process and the resulting workflow is startable.
- **SC-003**: The API feature's `ConfigureServices` registers only endpoints/transport, API-capability sources, the HttpContext authorization context, and the activity-draft publish/test-run services — zero **workflow-publish** engine-service or orchestration-handler registrations remain in the API feature (those come from the engine feature via `DependsOn`).
- **SC-004**: With the API feature enabled, the public publishing endpoint surface (routes and behaviour) is identical to the pre-split baseline.
- **SC-005**: Every sender/handler of the relocated publish command compiles and passes against the command's new `Publishing.Core` location, with no orphaned references.

## Assumptions

- The **workflow-publish + compile** engine services are auth-free and self-contained and move into the engine feature. The **activity-draft** publish/test-run services (`ActivityDefinitionPublisher`, `ActivityDraftTestRunService`) and the authorization context stay in the API feature — they are a separate, transport-authorization-coupled concern (confirmed: no workflow-publish engine service consumes them).
- The publish command/request contracts (`PublishWorkflow` + `PublishedWorkflowView`) are the only cross-boundary contracts that must relocate to `Publishing.Core`; other publishing contracts already live in `Publishing.Core`.
- Consumers that need the engine enable it via `DependsOn` (the two downstream design features repoint to `WorkflowsPublishing`); the shell activates the engine whenever the API feature is enabled.
- Durable persistence providers continue to override the in-memory publication/executable stores through the same `RemoveAll`+`AddScoped` override seam, now owned by the engine feature.
- The split uses `DependsOn` composition (§2.11), not feature inheritance, between the engine and the API feature: the API feature keeps its `FastEndpointsFeatureBase` base and declares `DependsOn WorkflowsPublishing`. Both feature classes are `public`; the engine feature is non-sealed with `virtual ConfigureServices` per §2.23.3.

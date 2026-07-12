# Feature Specification: Domain-Owned Management APIs

**Feature Branch**: `598-domain-owned-apis`
**Created**: 2026-07-13
**Status**: Draft
**Input**: Replace the `Elsa.Server` workflow-management facade with capability-discovered, domain-owned APIs; correct executable retention and publication lifecycle semantics; and migrate Elsa Studio as a coordinated delivery.

## Scope and Authority

This specification is authoritative for the supported management-client API surface, its domain ownership, its observable lifecycle semantics, and the coordinated Elsa Studio migration. It deliberately does not preserve the existing `ElsaWorkflowManagementApi` facade as a compatibility layer.

The work currently has program-goal state `none/free-flow`. Its executable-retention and publication-lifecycle prerequisites relate to the existing Runtime Execution Seam program goal, but the cross-domain API initiative does not yet have a dedicated program-goal bucket.

The repository constitution is currently draft. This specification applies its Design/Runtime separation and modular-hosting direction as quality gates without treating unratified wording as immutable. Durable rationale for executable retention and publication slots MUST also be recorded in the relevant architecture decision records.

### Capability and Feature Distinction

A **feature** is an internal shell-composition unit that activates services, dependencies, and endpoints. A **capability** is a stable, client-visible promise that a supported contract is available. They are intentionally many-to-many: one feature may provide several capabilities, and one capability may be assembled from several features or an operational provider. Capabilities therefore cannot be derived reliably from feature names or mere feature presence; active features must declare their public promises explicitly.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Compose management APIs without Elsa.Server (Priority: P1)

As an Elsa integrator, I can install only the domain modules my application supports and expose their canonical management APIs without copying endpoint code from the reference application.

**Why this priority**: Custom hosting is a primary Elsa use case. A supported client API that remains owned by a reference host defeats modular composition.

**Independent Test**: Compose a custom host from selected domain API modules, omit `Elsa.Server`, and use the resulting APIs to discover capabilities, author a workflow, publish it, inspect its executable, and query its executions.

**Acceptance Scenarios**:

1. **Given** a host that installs Workflow Design, Activity Design, Expressions, Publishing, Runtime, and API Capabilities, **When** its management endpoints are mapped, **Then** the supported API is available without a reference to `Elsa.Server`.
2. **Given** a host that omits a domain API module, **When** the host starts, **Then** the omitted domain's endpoints and capabilities are absent while installed domains remain usable.
3. **Given** the Elsa.Server reference application, **When** its composition is inspected, **Then** it configures domain modules but contains no workflow-management endpoint implementation.

---

### User Story 2 - Publish safely to an explicit lifecycle slot (Priority: P1)

As a workflow author, I can update the default publication without unintentionally leaving the previous trigger active, while deliberately publishing side-by-side versions through named slots.

**Why this priority**: Publication semantics determine which workflows can start in production. Ambiguous append-only behavior can activate routes or triggers that users believe were replaced.

**Independent Test**: Publish an HTTP-triggered workflow at `/foo`, change it to `/bar`, republish to the default slot, and verify only `/bar` starts new executions; then publish another version to a named slot and verify intentional coexistence.

**Acceptance Scenarios**:

1. **Given** a live publication in the default slot, **When** a valid replacement is published to that slot, **Then** the replacement becomes the sole authority for new starts in that slot and the old publication is retired.
2. **Given** a live publication in a slot, **When** replacement validation, compilation, activation, or projection reconciliation fails, **Then** the old publication remains authoritative and the operation does not report a false success.
3. **Given** a workflow with a live default publication, **When** an author publishes another executable to an explicitly named slot, **Then** both slots can remain live subject to trigger-cardinality validation.
4. **Given** an exclusive HTTP trigger claimed by another active slot, **When** a candidate publication claims the same route, **Then** activation is rejected before authority changes.

---

### User Story 3 - Retain executables required by workflow executions (Priority: P1)

As an operator, I can resume or inspect a retained workflow execution even after all publication and test-run references to its executable have expired or been retired.

**Why this priority**: Deleting an executable still pinned by a retained execution breaks runtime correctness and historical inspection.

**Independent Test**: Retain a completed or suspended workflow execution, retire every live source reference to its executable, run garbage collection, and verify the executable remains available until the execution record itself leaves retention.

**Acceptance Scenarios**:

1. **Given** an executable referenced by a retained workflow execution but by no live source reference, **When** garbage collection runs, **Then** the executable is retained.
2. **Given** an executable referenced by neither a live source reference nor any retained workflow execution, **When** garbage collection runs after applicable grace periods, **Then** the executable is eligible for collection.
3. **Given** executions in completed, faulted, suspended, or running states, **When** their records remain retained, **Then** each pinned executable remains protected regardless of execution status.

---

### User Story 4 - Author workflows through canonical domain contracts (Priority: P2)

As an Elsa Studio user, I can create, edit, version, restore, and inspect workflows through domain-owned APIs whose resources reflect the actual Design model.

**Why this priority**: Studio needs a coherent authoring contract, but it depends on the lifecycle correctness established by the P1 stories.

**Independent Test**: From an empty Design store, create a definition with an initial authored state, update and promote its draft, list its real versions, soft-delete and restore it, and resolve context-sensitive input options.

**Acceptance Scenarios**:

1. **Given** a valid authored workflow state, **When** a definition is created with that state, **Then** Design creates the definition and its editable draft without requiring a concrete root activity kind.
2. **Given** an edited draft, **When** it is promoted, **Then** an immutable persisted Design version is created through the canonical version-creation flow.
3. **Given** a synthetic draft identifier, **When** a caller requests a persisted version by that identifier, **Then** the request is rejected rather than reconstructing a pseudo-version.
4. **Given** a soft-deleted definition with an active publication, **When** the definition is restored or remains deleted, **Then** its publication state is unchanged unless a separate Publishing operation changes it.

---

### User Story 5 - Discover client-visible capabilities efficiently (Priority: P2)

As a supported management client, I can make one authenticated request to learn which stable API capabilities the active shell exposes and follow canonical links without probing every domain.

**Why this priority**: Optional domain composition must be discoverable without coupling clients to internal feature names or creating a burst of bootstrap requests.

**Independent Test**: Start two shells with different module compositions, request `/capabilities` in each shell, and verify that each response contains only its explicit capability declarations and canonical relative links.

**Acceptance Scenarios**:

1. **Given** an active shell with several domain API features, **When** an authenticated client requests `/capabilities`, **Then** it receives one permission-neutral document containing coarse capability identifiers, contract versions, and canonical links.
2. **Given** a domain feature that is installed but does not explicitly declare a capability, **When** capabilities are aggregated, **Then** no capability is inferred from the feature's type or name.
3. **Given** a conditional operational capability provider, **When** its condition changes, **Then** its advertised capability changes without altering static feature declarations.
4. **Given** two shells in one host, **When** each shell requests capabilities, **Then** links and declarations resolve within the active shell and never assume a hard-coded `default` shell.

---

### User Story 6 - Move Elsa Studio without a compatibility facade (Priority: P2)

As an Elsa product maintainer, I can update Foundation and Studio together so Studio uses only canonical domain APIs when the old reference-app facade is removed.

**Why this priority**: Immediate removal is the cleanest architecture, but it is acceptable only as a coordinated change with no released broken interval.

**Independent Test**: Run Studio's management journeys against a host with no old workflow-management routes and verify that all journeys complete through discovered domain APIs.

**Acceptance Scenarios**:

1. **Given** the migrated Studio, **When** its management journeys run, **Then** it makes no request to the old `/_elsa/workflow-management` surface.
2. **Given** a host without an optional capability, **When** Studio loads, **Then** Studio suppresses or disables the corresponding experience instead of probing a missing route.
3. **Given** the coordinated release candidate, **When** compatibility is checked, **Then** the old facade and its routes are absent and no adapter re-exposes them.

### Edge Cases

- Two modules declare the same capability identifier and major contract version with incompatible canonical links.
- A dynamic capability provider fails or times out while the static capability document is being assembled.
- A caller can authenticate but lacks permission for one or more linked domain operations.
- A host contains multiple shells with different route bases or different domain compositions.
- Two publishers concurrently try to replace the same publication slot.
- Garbage collection overlaps a publication replacement or execution-retention update.
- The same executable is referenced by multiple live publication slots, test runs, and retained executions.
- A replacement publication reuses the old artifact but changes publication identity or policy.
- A definition is soft-deleted while one or more publication slots remain active.
- An HTTP trigger changes from `/foo` to `/bar` while another slot already claims `/bar`.
- A fan-out event or timer trigger is intentionally shared by multiple slots.
- Trigger projection storage cannot participate in the same transaction as publication authority.
- A Studio instance view refers to an executable whose source Design version has since been deleted.
- An authored workflow contains an activity that is no longer addable in the current environment.
- A direct-ingestion caller attempts to create an arbitrary persisted version through the normal authoring flow.

## Requirements *(mandatory)*

### Functional Requirements

#### Supported API and Domain Ownership

- **FR-001**: The system MUST expose one canonical supported management-client API assembled from domain-owned endpoint slices.
- **FR-002**: `Elsa.Server` MUST remain a reference composition application and MUST NOT own or implement workflow-management endpoints.
- **FR-003**: The `ElsaWorkflowManagementApi` facade and the old `/_elsa/workflow-management` routes MUST be removed without a permanent compatibility adapter.
- **FR-004**: Custom hosts MUST be able to expose the supported API by installing and configuring domain API modules, without copying code from `Elsa.Server`.
- **FR-005**: Workflow definitions, drafts, immutable Design versions, scoped-variable analysis, and context-sensitive activity input-option resolution MUST be owned by Workflow Design API.
- **FR-006**: The authoring activity catalog and activity availability diagnostics MUST be owned by Activity Design API.
- **FR-007**: Expression descriptors and variable-type descriptors MUST be owned by an Expressions API module.
- **FR-008**: Publishing, publication slots and policies, publish/unpublish/restore operations, and test-run source-reference mutation MUST be owned by Publishing API.
- **FR-009**: Executable artifacts, executable inspection and execution, workflow instances, and read-only source provenance MUST be owned by Runtime API.
- **FR-010**: A dedicated API Capabilities module MUST aggregate client-visible capabilities across the active shell.

#### Capability Discovery

- **FR-011**: The active shell MUST expose a single global `GET /capabilities` endpoint for management-client bootstrap.
- **FR-012**: The capability document MUST contain stable coarse-grained capability identifiers, independently versioned capability contracts, and canonical links.
- **FR-013**: Capability links MUST be relative to or correctly resolved for the active shell and MUST NOT assume a shell named `default`.
- **FR-014**: Static capabilities MUST be declared explicitly by active features through stable metadata; capability identifiers MUST NOT be inferred from implementation type names or feature names.
- **FR-015**: Operational or conditional capabilities MAY be contributed dynamically through a capability-provider contract.
- **FR-016**: Every supported domain API feature MUST activate or depend on the API Capabilities feature so capability aggregation cannot be accidentally omitted.
- **FR-017**: An unavailable domain MUST be represented by the absence of its capability, not by an advertised link that is expected to fail.
- **FR-018**: The capability document MUST be permission-neutral and caller-neutral; domain endpoint authorization remains authoritative.
- **FR-019**: Secure shells MUST require authentication for capability discovery, while any identity pre-authentication bootstrap MUST remain a separate concern.
- **FR-020**: Rich domain state, arbitrary domain JSON, per-user permissions, and detailed bootstrap models MUST remain in domain-owned APIs rather than the global capability document.
- **FR-021**: The system MUST define deterministic handling for duplicate or incompatible capability declarations and MUST surface configuration diagnostics rather than silently choosing one.

#### Workflow Design Contracts

- **FR-022**: Definition creation MUST accept an optional initial authored workflow state and MUST NOT require a concrete root activity kind such as Sequence or Flowchart.
- **FR-023**: Studio MUST compose initial authored state from the canonical activity catalog rather than depending on concrete server activity types.
- **FR-024**: Workflow drafts MUST be first-class resources supporting read, replace, promote, and discard operations.
- **FR-025**: Promoting a draft MUST be the canonical normal-authoring operation that creates an immutable persisted Design version.
- **FR-026**: Definition metadata changes MUST have a distinct operation from draft-state replacement.
- **FR-027**: If arbitrary version ingestion remains supported, it MUST use an explicit ingestion contract with separate authorization and MUST NOT masquerade as normal authoring.
- **FR-028**: Persisted-version endpoints MUST resolve only real stored Design versions and MUST reject synthetic draft identifiers.
- **FR-029**: Definition list projections MUST include the draft, latest-version, version-count, and deletion facts required by Studio without per-item follow-up requests.
- **FR-030**: Definition queries MUST support active, deleted, and all scopes plus client-relevant search.
- **FR-031**: Deleting a definition through the normal Design operation MUST soft-delete it; restoring it MUST be explicit.
- **FR-032**: Permanent definition deletion, if exposed, MUST be an explicit privileged operation and MUST require the definition to have already been soft-deleted.
- **FR-033**: Soft-deleting or restoring a Design definition MUST NOT implicitly unpublish or republish it.
- **FR-034**: Studio instance inspection MUST use the Runtime executable pinned to the instance rather than reconstructing execution state from the current Design model.

#### Activity and Expression Design Contracts

- **FR-035**: Activity Design MUST expose one canonical authoring catalog at `GET /design/activities/catalog` that replaces the legacy `/activities` and `/descriptors/activities` bootstrap calls.
- **FR-036**: By default, the authoring catalog MUST return activities that can be added in the active environment.
- **FR-037**: The catalog MUST provide normalized inputs, outputs, UI specifications, ports, container structure, and an authoring template sufficient for a client to create valid authored state.
- **FR-038**: Privileged availability queries MUST be able to explain activities that are installed but unavailable, without adding them to the default addable catalog.
- **FR-039**: Context-sensitive input-option resolution MUST remain in Workflow Design because it evaluates workflow state and node context.
- **FR-040**: Expressions API MUST expose canonical expression-descriptor and variable-type-descriptor resources independent of the reference server.

#### Publication Slots, Policies, and Trigger Authority

- **FR-041**: A publication slot MUST be uniquely scoped by workflow definition and slot name and MUST have at most one authoritative live publication.
- **FR-042**: Every definition MUST have a conventional slot named `default`; an ordinary publish with no explicit slot MUST target it.
- **FR-043**: Publishing to an occupied slot MUST replace its authoritative publication rather than append another authority for new starts.
- **FR-044**: Side-by-side live executables for one definition MUST require explicitly named distinct slots.
- **FR-045**: Publication replacement MUST validate and prepare the candidate before changing authority.
- **FR-046**: Publication replacement MUST be failure-safe: a failed candidate MUST leave the old publication authoritative.
- **FR-047**: Successful activation MUST atomically establish the new slot authority and retire the old source reference from authority.
- **FR-048**: Trigger projections MUST carry publication identity, not only definition or executable identity.
- **FR-049**: When trigger projections cannot share the authority transaction, the system MUST use durable reconciliation and MUST expose a pending or failed state rather than reporting premature success.
- **FR-050**: Publishing policy MUST support a host default, an optional per-workflow policy, and an explicit request override with precedence `request > workflow > host`.
- **FR-051**: The resolved publish action and slot MUST be visible to clients before confirmation; side-by-side publication MUST require a meaningful explicit slot name.
- **FR-052**: Trigger providers MUST declare whether a trigger is exclusive or fan-out capable.
- **FR-053**: Candidate activation MUST validate exclusive trigger claims against other authoritative slots while excluding the publication being replaced in the same slot.
- **FR-054**: HTTP endpoint triggers MUST be treated as exclusive; event or timer providers MAY declare fan-out semantics.
- **FR-055**: Publishing preflight MUST report triggers added, removed, retained, conflicting, and their cardinality semantics.
- **FR-056**: Existing workflow executions MUST continue using their pinned executable after a publication slot is replaced.
- **FR-057**: Normal unpublish and replacement operations MUST retire publication source references; physical executable deletion MUST remain garbage-collection or exceptional privileged administration behavior.

#### Runtime Artifacts, Provenance, and Retention

- **FR-058**: A workflow executable MUST be a Runtime-owned immutable artifact inspectable through Runtime API.
- **FR-059**: Runtime API MUST expose source references as read-only provenance; mutation of publication and test-run references MUST remain in Publishing API.
- **FR-060**: A source reference MUST identify its source as a persisted workflow definition version or workflow draft snapshot and its scope as at least Published or TestRun.
- **FR-061**: A source reference MUST be live only while it has not been deleted and has not expired.
- **FR-062**: Garbage collection MUST treat the retained executable set as the union of executable identifiers referenced by live source references and executable identifiers pinned by retained workflow execution records.
- **FR-063**: The existence of a retained workflow execution record MUST itself protect its executable; the system MUST NOT require a duplicate execution source-reference record.
- **FR-064**: Protection MUST apply to retained executions regardless of whether they are running, suspended, completed, canceled, or faulted.
- **FR-065**: An executable MAY become collectible only after no live source reference and no retained workflow execution points to it, subject to configured grace periods and concurrent-operation safety.
- **FR-066**: Runtime persistence MUST support determining distinct executable identifiers pinned by retained executions without loading every execution record into application memory.

#### Security, Migration, and Delivery

- **FR-067**: Every canonical domain operation MUST apply its domain authorization policy independently of capability advertisement.
- **FR-068**: API routes MAY remain unversioned, but capability contracts MUST declare a major contract version and packages MUST use semantic versioning to communicate compatibility.
- **FR-069**: Foundation and Studio changes MUST be coordinated so no supported release contains a Studio build that depends on removed routes.
- **FR-070**: Migrated Studio MUST use capability discovery for optional experiences and MUST call only canonical domain APIs.
- **FR-071**: The migration MUST inventory every operation previously exposed by `ElsaWorkflowManagementApi` and map it to a canonical owner or explicitly remove it with rationale.
- **FR-072**: Delivery MUST proceed in this dependency order: retention correction and its decision-record amendment; publication slots, policies, and atomic activation; domain API refactoring and enrichment; capability aggregation and Expressions API; Studio migration; facade deletion; coordinated release validation.
- **FR-073**: The executable-retention decision record MUST be amended to include workflow execution records as retention roots.
- **FR-074**: A publication-lifecycle decision record MUST define slot authority, policy precedence, trigger cardinality, replacement failure semantics, and projection reconciliation.

### Canonical Contract Allocation

The following route stems are public contract allocations, not an exhaustive endpoint design. Planning MAY refine resource names while preserving ownership and observable behavior.

| Contract area | Canonical owner | Canonical route stem or resource |
|---|---|---|
| Client capability bootstrap | API Capabilities | `/capabilities` |
| Definitions, drafts, Design versions, analysis | Workflow Design API | `/design/workflows/...` |
| Authoring activity catalog and availability | Activity Design API | `/design/activities/...` |
| Expression and variable-type descriptors | Expressions API | `/expressions/...` |
| Publication slots, policy, preflight, test runs | Publishing API | `/publishing/...` |
| Executables, provenance inspection, runs, instances | Runtime API | `/runtime/workflows/...` |

### Key Entities *(include if feature involves data)*

- **API Capability Declaration**: A feature-owned, stable statement that a shell provides a client-visible contract, including identifier, major version, and canonical link.
- **Capability Document**: The authenticated, shell-scoped aggregate of static declarations and valid dynamic capabilities; intentionally free of caller permissions and rich domain state.
- **Workflow Definition**: The Design-owned logical identity and metadata of an authored workflow, including soft-deletion state.
- **Workflow Draft**: The mutable authored state associated with a definition before promotion.
- **Workflow Definition Version**: An immutable persisted Design snapshot created by promotion or an explicit ingestion path.
- **Activity Authoring Catalog Entry**: The normalized client contract for adding and configuring an activity in authored state.
- **Workflow Executable**: The immutable Runtime-owned artifact used to start or continue executions.
- **Executable Source Reference**: Publishing-owned lifecycle provenance connecting an executable to a Design version or draft snapshot for a Published or TestRun purpose.
- **Publication Slot**: The Publishing-owned named authority boundary that selects at most one executable publication for new starts of a definition.
- **Publication Policy**: The resolved host, workflow, and request intent controlling replacement or explicit side-by-side publication.
- **Trigger Claim**: A publication-identified assertion over an exclusive or fan-out trigger key used during activation validation and projection reconciliation.
- **Workflow Execution Retention Root**: A retained workflow execution record whose pinned executable must remain available for resumption and inspection.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A custom sample host completes the supported authoring, publication, runtime inspection, and execution-management journeys with zero references to `Elsa.Server`.
- **SC-002**: Zero workflow-management endpoints are implemented in the Elsa.Server project after migration.
- **SC-003**: Every operation in the old facade inventory is either mapped to exactly one canonical domain owner or recorded as intentionally removed with rationale.
- **SC-004**: Studio's covered management journeys complete with zero requests to `/_elsa/workflow-management`.
- **SC-005**: A management client discovers all installed supported API areas with one `/capabilities` request and no per-domain existence probes.
- **SC-006**: In tests across multiple shell compositions, 100% of advertised capability links resolve within the requesting shell and no omitted capability is advertised.
- **SC-007**: Replacing a default-slot HTTP publication changes `/foo` to `/bar` with no interval in which a failed candidate displaces the old authority and with no successful state in which both routes remain authoritative for that slot.
- **SC-008**: Concurrent replacement tests establish at most one authoritative live publication per `(definition, slot)`.
- **SC-009**: Garbage-collection tests across every retained execution status delete zero executables pinned by retained execution records.
- **SC-010**: Once all source references and retained executions are removed, eligible executable artifacts are collected according to configured retention policy.
- **SC-011**: Definition list retrieval supplies Studio's required summary facts without issuing one additional server request per returned definition.
- **SC-012**: The coordinated Foundation and Studio release validation contains no supported interval in which Studio requires an API that Foundation has removed.

## Assumptions

- Immediate breaking removal is acceptable before the coordinated release; backward compatibility with the reference-app facade is not a goal.
- Existing Elsa authentication and domain authorization mechanisms remain the basis for securing the new endpoints.
- The lifetime of workflow execution records remains governed by existing retention policy; this feature defines their effect on executable retention, not the duration itself.
- Advanced traffic splitting, weighted routing, and automatic canary progression are outside this scope; explicit named slots provide intentional coexistence.
- Routine clients do not physically delete executable artifacts. Exceptional privileged administration may be designed separately.
- Foundation is the source of truth for the supported API contracts; Elsa Studio will carry a companion implementation plan and tests rather than a duplicate architectural specification.
- Exact endpoint shapes beyond the stated route stems will be finalized during planning and contract design, while the ownership and lifecycle invariants in this specification remain binding.

## Dependencies and Delivery Boundaries

- The retention correction and publication-slot lifecycle are prerequisites to migrating the management API because the new public contract must not canonize unsafe current behavior.
- The Runtime Execution Seam program goal is a related planning surface for retention and runtime boundaries; this umbrella initiative remains `none/free-flow` until explicitly assigned a durable cross-domain bucket.
- Elsa Studio is a coordinated downstream consumer. Its migration may land immediately after Foundation API changes, but both must be validated and released as one compatible work unit.
- ADR 0040 (executable retention) requires amendment, and publication slots require a dedicated ADR or an explicitly justified amendment to an existing publication decision.

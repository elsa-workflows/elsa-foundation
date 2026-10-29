# Feature Specification: Module-owned Weaver contributions

**Feature Branch**: `agent/codex/3437-weaver-contributions`

**Created**: 2026-10-29

**Status**: In progress — candidate implementation under #3437; independent package gate #3438 remains open

**Input**: Owner-authorized Program #3428 M1, candidate contract #3437 and independent package witness #3438. Modules own context, guidance, tools, validation, risk, result rendering and effects. A separately packaged module adds a complete journey without edits to shared Weaver, host or provider logic. This work follows the observed [M0 baseline](../../docs/reports/weaver-baseline.md).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Ask about the exact module context (Priority: P1)

A Studio user asks Weaver about the current resource or invokes a module's prompt action. The owning module supplies the selected resource, revision, relevant unsaved state and bounded supporting information. Weaver shows what it used and retains the caller's explicit attachments.

**Why this priority**: An assistant cannot give a grounded answer or target a safe change when resource identity or caller context is lost.

**Independent Test**: A workflow example and an unrelated module example each contribute their exact selected resource and caller attachment; changing route text alone cannot invent another resource.

**Acceptance Scenarios**:

1. **Given** a module-owned resource selection, **When** a user sends a prompt, **Then** the turn receives that resource's identity, revision, provenance and permitted context.
2. **Given** a module prompt action with explicit attachments, **When** the shell opens Weaver, **Then** the attachments and request correlation survive the handoff.
3. **Given** a list with filters or an unsaved editor selection, **When** the module contributes context, **Then** Weaver distinguishes the observed selection/snapshot from authoritative persisted state.
4. **Given** no participating context, **When** Weaver opens, **Then** it explains the available support and never invents a workflow identity from the route.

### User Story 2 - Review and observe a module-owned change (Priority: P1)

A user requests a supported change. The module validates the exact target and arguments and supplies the review content. The permitted execution reaches the owning module and returns evidence of what actually happened, with local edits distinguished from persisted changes.

**Why this priority**: Proposal creation and approval are useful only when the user can assess the exact change and observe its outcome.

**Independent Test**: Thin workflow and unrelated-resource examples inspect a resource, prepare a reviewable change and return an observed module-owned result. Missing execution support returns an explicit unsupported outcome.

**Acceptance Scenarios**:

1. **Given** a supported registered tool and permitted target, **When** the tool prepares a change, **Then** review identifies its owner, target, revision, arguments, risk and intended effect.
2. **Given** malformed arguments, denied authority or a stale target, **When** preparation or execution is attempted, **Then** no effect occurs and the result explains the refusal.
3. **Given** an approved exact change, **When** the owning module applies it, **Then** Weaver receives a correlated outcome and displays the module's result inside the common review controls.
4. **Given** an unsaved editor target, **When** a permitted local change succeeds, **Then** the editor's native transaction/validation/undo rules apply and the receipt identifies a local outcome rather than a durable save.
5. **Given** no concrete executor, **When** a change is requested, **Then** Weaver reports execution as unavailable rather than successful.

### User Story 3 - Extend Weaver with an independent module (Priority: P1)

A module author adds a resource, context, tool, review renderer and application behavior using the published extension contract. Shared Weaver, host and provider implementations do not learn that resource's domain concepts.

**Why this priority**: This is the Program's defining open/closed acceptance criterion and the gate before contract freeze.

**Independent Test**: The candidate supports a separately authored non-workflow module. The subsequent #3438 witness consumes real packages and proves the complete journey with zero shared-implementation edits.

**Acceptance Scenarios**:

1. **Given** an installed participating module, **When** its contributions are admitted, **Then** its context, tools and result support become available through the same path as the workflow example.
2. **Given** that module is removed, disabled or incompatible, **When** availability is refreshed, **Then** its contributions disappear or carry an explicit disabled reason and pending actions cannot run against a replacement target.
3. **Given** the same admitted tool and result, **When** either existing provider path is used, **Then** module meaning remains in the contribution and the typed result survives transport.
4. **Given** a duplicate identity or unsupported contract version, **When** the module is admitted, **Then** the conflict is diagnosed explicitly rather than silently choosing an implementation.

### User Story 4 - Preserve authority and incomplete-context visibility (Priority: P2)

An operator controls which contributions and data are available. A user sees a clear limitation when required context, permissions, a provider or the originating editor is unavailable.

**Why this priority**: New modules must preserve the existing authority boundary and avoid making partial information look complete.

**Independent Test**: Denied tools never reach their executors; optional context failure is reported as partial, required context failure prevents dependent actions, and instruction-shaped resource content cannot grant authority.

**Acceptance Scenarios**:

1. **Given** multiple applicable contributors, **When** context is collected, **Then** the admitted contributions are composed and failures follow declared required/optional semantics.
2. **Given** resource content containing instructions, **When** it reaches Weaver, **Then** it remains resource data and cannot replace module/host policy.
3. **Given** a client callback without a corresponding trusted backend contract, **When** it is advertised, **Then** it cannot become an executable authorized tool.
4. **Given** navigation, module removal or loss of the originating editor, **When** an outstanding local action is resolved, **Then** it cannot silently apply to another selection or tab.
5. **Given** an editor transaction committed before its acknowledgement was lost, **When** the originating context disappears, **Then** Weaver reports the available receipt or explicit uncertainty and does not silently replay the mutation or claim that cancellation undid it.

### Edge Cases

- Duplicate contribution or tool identities, incompatible schema versions and unknown resource types.
- One optional context source fails while required resource identity remains available; required context is absent.
- User permissions or tenant change between preparation, approval and application.
- Draft revision changes, selected execution occurrence changes, or an originating client context disappears.
- Duplicate/conflicting result acknowledgements or a late acknowledgement after cancellation/expiry.
- A provider is unavailable, returns malformed output or supplies only text for a structured outcome.
- A module is installed only on Studio or only on the backend and lacks its required counterpart.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Weaver MUST consume a coherent module contribution path for context, tool availability, invocation, review rendering and outcomes; successful registration into an unused parallel path is insufficient.
- **FR-002**: Participating modules MUST own resource identity, revision interpretation, context/guidance, domain validation/risk, result payload meaning and application semantics. Shared components MUST require no resource-specific branches for a new module.
- **FR-003**: Explicit prompt attachments, source attribution and request correlation MUST survive the shell-to-admission handoff. Only admitted/permitted content reaches the turn; exclusion or redaction MUST be explicit. Current route text MUST NOT establish resource identity or authority.
- **FR-004**: Context MUST identify its owner, target, provenance, sensitivity, schema version and availability. Unsaved client snapshots and trusted installed-module guidance MUST remain distinguishable from authoritative persisted facts and untrusted resource data.
- **FR-005**: Applicable context contributions MUST compose under declared required/optional failure rules, with duplicate identities and unsupported versions diagnosed explicitly.
- **FR-006**: Executable tool contracts MUST come from trusted installed backend contributions and be admitted under the current actor, tenant, permissions, scope, module availability and final host policy. Client metadata MUST NOT grant execution authority.
- **FR-007**: A mutation MUST be validated/prepared before review and bind to the exact registered tool, target/revision and arguments. Application MUST recheck authority and live target constraints.
- **FR-008**: Module results MUST retain their declared type/version and payload through either provider path. Provider adapters MUST transport the result without interpreting a workflow or other module's payload.
- **FR-009**: The common review controls MUST show module-owned result content and generic target/risk/status information. Missing renderers or executors MUST produce explicit supported fallbacks or unavailable outcomes.
- **FR-010**: Client-working-state invocation MUST be correlated to its originating session, turn and context generation, with defined expiry, cancellation and duplicate/conflicting acknowledgement behavior. Loss of that context MUST prevent silent retargeting or replay. After dispatch/application, a missing acknowledgement MUST produce a supported receipt or explicit uncertainty rather than a false zero-effect/cancellation claim.
- **FR-011**: Local working-state application and durable domain effects MUST have distinct completion semantics and evidence. A success-reporting no-op MUST NOT satisfy either outcome.
- **FR-012**: Module disable/removal, incompatible composition and changing authority MUST update availability and prevent invalid pending actions.
- **FR-013**: Candidate changes MUST preserve configured feature identities and existing authorized workflow validation, atomic transaction, undo, dirty-state and explicit save/publish behavior.
- **FR-014**: The candidate MUST expose enough supported behavior for thin workflow/non-workflow examples and the independent package witness; production signature freeze MUST wait for that witness and normal architecture review.

### Key Entities

- **Resource target**: module-owned resource type/identity and revision, with optional human-readable navigation hints.
- **Context attachment**: bounded target-related information with provenance, sensitivity, version, availability and trust classification.
- **Contribution**: an installed module's declared participation and lifecycle in the shared extension model.
- **Tool contract**: a registered operation's owner, input/target/result shapes, permissions, modes and supported execution location.
- **Prepared invocation**: the validated exact target/arguments and correlated authority/lifecycle identity used for review and application.
- **Proposal**: review state around a prepared mutation, using the existing resource-agnostic concept.
- **Outcome/receipt**: a typed module-owned result and evidence of refusal, local application or durable effect.
- **Active context generation**: the lifetime identity tying a local action to the originating module/editor/selection.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Both thin examples preserve admitted/permitted attachments, correlation and exact target/revision through prompt, preparation, review and result, with no invented route-derived identity. Rejected or redacted caller content has an explicit admission outcome rather than silent handoff loss.
- **SC-002**: Every denied, malformed, stale, missing-executor or vanished-context refusal before dispatch/application produces zero effects and an explicit outcome. After dispatch/application, the candidate returns a supported receipt or explicit uncertainty without silent replay or a false zero-effect claim.
- **SC-003**: A separately authored module can consume the candidate contract and add its supported journey with zero edits to shared Weaver, host or provider implementations; #3438 verifies package consumption and observed effects before freeze.
- **SC-004**: Both existing provider paths preserve the same admitted tool/result meaning, while evidence distinguishes deterministic/protocol tests from any opt-in live-provider run.
- **SC-005**: Required and optional contributor failures, module removal and duplicate identities have observable, deterministic outcomes; no partial context or approval is reported as a completed effect.

## Scope and Assumptions

This work owns the candidate and thin connected examples under #3437. #3438 separately proves independent package consumption. Existing approved Slots/Contributions, resource-agnostic review, working-state workflow apply and Elsa-owned risk decisions govern the candidate; this specification does not ratify final signatures.

The complete current-module and workflow coverage matrix belongs to M3/M4. Durable session/history/approval recovery belongs to M2 #3441; a candidate that cannot recover a vanished local target must fail explicitly rather than claim recovery. No new execution engine, provider default, unrestricted client code, universal domain schema, release/deployment, provider purchase or publisher activation is included. Existing server/domain authorization remains authoritative. Live-provider smoke testing remains opt-in; current development-provider missing-auth is diagnosed configuration state, not evidence of a successful turn.

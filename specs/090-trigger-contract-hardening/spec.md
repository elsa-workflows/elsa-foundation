# Feature Specification: Trigger Publication Contract Hardening

**Feature Branch**: `597-trigger-contract-hardening`

**Created**: 2026-07-11

**Status**: Draft

**Input**: User description: "Establish the canonical first-party trigger publication contract and preflight validation for Event, Timer, Cron, and HttpEndpoint. Preserve intentional non-start behavior and upgrade compatibility. Do not add diagnostics, composition changes, or implementation yet."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Reject Unroutable Start Triggers Before Index Mutation (Priority: P1)

A workflow publisher can trust that a successful publication has a usable first-party start-trigger registration. If an Event, Timer, Cron, or HttpEndpoint node is classified as a start trigger but its provider cannot recognize it, derive a valid stimulus identity, or materialize a required provider-owned publication projection, publication fails clearly before any existing trigger or schedule registrations are replaced.

**Why this priority**: Silent publication of an unroutable trigger is the failure this work exists to eliminate. Preventing partial trigger-index replacement is the smallest independently valuable hardening slice.

**Independent Test**: Publish representative valid and invalid workflows for each first-party trigger family, seed existing registrations before each invalid attempt, and verify that valid workflows produce all expected registrations while invalid attempts fail with the seeded registrations unchanged.

**Acceptance Scenarios**:

1. **Given** a classified first-party trigger with valid authored identity, **When** the workflow is published, **Then** its provider is identified and every required binding and provider-owned publication projection is validated before registration replacement begins.
2. **Given** a classified trigger that no available provider recognizes, **When** publication is attempted, **Then** publication fails with the artifact and executable-node identity and no trigger or schedule registration is mutated.
3. **Given** a provider recognizes a trigger but cannot derive a valid stimulus identity, **When** publication is attempted, **Then** publication fails before registration mutation.
4. **Given** a Cron start trigger with no future occurrence, **When** publication is attempted, **Then** publication fails instead of succeeding with no recurring schedule.
5. **Given** one node produces several valid bindings, **When** publication succeeds, **Then** all bindings are treated as one validated publication result and are replaced together according to the existing indexing semantics.

---

### User Story 2 - Preserve Explicit Non-Start Intent (Priority: P2)

A workflow author can use a trigger-capable activity in a non-start role without publication treating that deliberate choice as an error. Provider ownership remains observable even when the provider intentionally produces no start bindings.

**Why this priority**: HttpEndpoint legitimately supports mid-flow, non-start behavior. Hardening must distinguish that authored intent from a missing provider or broken stimulus.

**Independent Test**: Publish a workflow containing an explicitly non-starting HttpEndpoint and verify that the responsible provider is identified, publication succeeds, and no start binding or recurring projection is created for that node.

**Acceptance Scenarios**:

1. **Given** a provider recognizes a trigger-capable node and deliberately reports no start bindings, **When** publication is attempted, **Then** the result is accepted as intentionally non-starting.
2. **Given** an intentionally non-starting node, **When** preflight completes, **Then** the result identifies the recognizing provider and is not confused with an unrecognized trigger.
3. **Given** a non-starting HttpEndpoint used as a mid-flow suspension point, **When** its containing workflow is published, **Then** no start binding is created and existing resume behavior remains outside this unit's changes.

---

### User Story 3 - Republish Existing Definitions Safely (Priority: P3)

An operator upgrading an existing installation can start the application and republish existing workflows without same-version activity-catalog hash conflicts or unreadable published artifacts. Corrected trigger classification is applied through compilation and republishing rather than by mutating immutable catalog content.

**Why this priority**: PR #621 demonstrated that changing persisted same-version activity metadata can block upgrades. Compatibility is a release gate for any trigger-contract change.

**Independent Test**: Seed legacy catalog rows, executable artifacts, and trigger-binding documents from supported historical shapes; start the relevant services, read existing artifacts, and republish workflows while verifying catalog hashes remain stable and newly published artifacts use the approved trigger contract.

**Acceptance Scenarios**:

1. **Given** an existing CLR activity catalog row whose stored execution type predates compile-time trigger projection, **When** the catalog reconciles, **Then** its same-version identity and content hash remain unchanged.
2. **Given** a supported existing executable artifact, **When** it is loaded after the upgrade, **Then** it remains readable without requiring Design data at runtime.
3. **Given** an existing definition is republished, **When** compilation projects current trigger intent, **Then** the resulting artifact receives the correct behavioral identity without mutating the existing activity version.
4. **Given** the persisted trigger-binding shape changes as part of the approved design, **When** historical documents are loaded, **Then** they are upgraded explicitly and remain readable.

### Edge Cases

- A provider recognizes a node but returns blank stimulus identity or two descriptors that produce the same deterministic trigger-binding id; descriptors with distinct resulting binding ids remain valid fan-out.
- Multiple providers claim the same executable activity type.
- A trigger-capable CLR type cannot be resolved from its executable construction descriptor.
- A first-party provider produces several descriptors and only one is invalid.
- A recurring trigger binding is valid but its required schedule projection cannot be calculated.
- A failed preflight occurs while prior bindings and schedules already exist for the artifact.
- A workflow contains a mix of valid start triggers, intentional non-start nodes, and one invalid trigger.
- An old executable remains readable but only republishing can apply corrected trigger classification.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST define one canonical layered trigger contract: authored declaration or catalog fallback is projected into the runnable artifact, and provider recognition materializes the provider-specific start identity from that artifact.
- **FR-002**: Runtime trigger publication MUST use only the runnable artifact and configured runtime providers; it MUST NOT read Design-owned state.
- **FR-003**: Every trigger node in the runnable artifact MUST receive exactly one terminal recognition outcome: recognized with one or more materializable bindings, recognized as intentionally non-starting, or rejected.
- **FR-004**: A recognizing provider MUST expose a stable, non-secret provider identifier in the preflight outcome.
- **FR-005**: Trigger providers MUST be treated as a context-selected Strategy set, not as data contributors: a trigger node claimed by more than one strategy MUST be rejected as an ambiguous contract instead of relying on registration order.
- **FR-006**: A classified trigger recognized by no provider MUST fail publication with artifact and executable-node context.
- **FR-007**: Every produced stimulus descriptor MUST be validated for required identity and provider-owned publication requirements before any trigger or schedule registration for the artifact is deleted or saved. Two descriptors are duplicates only when they produce the same deterministic trigger-binding id; distinct binding ids remain valid provider-owned fan-out.
- **FR-008**: Validation MUST be all-or-nothing for the artifact: one invalid node or descriptor prevents mutation for every trigger in that publication attempt.
- **FR-009**: A provider-recognized empty binding result MUST remain a valid intentionally non-starting outcome and MUST NOT be treated as provider absence.
- **FR-010**: Event, Timer, Cron, and HttpEndpoint MUST each have a contract-matrix row covering authored intent, executable classification, provider recognition, binding cardinality, provider-owned projection, invalid identity, and intentional non-start behavior.
- **FR-011**: A Timer or Cron start trigger MUST validate its recurring schedule projection before trigger or schedule registration mutation.
- **FR-012**: A Cron start trigger with no future occurrence MUST fail publication clearly rather than publish with no runnable schedule.
- **FR-013**: Existing provider-specific uniqueness rules MUST remain provider-owned; the generic trigger contract MUST NOT impose global uniqueness on stimulus identities.
- **FR-014**: Existing same-version activity catalog content MUST NOT be mutated to correct runtime trigger classification.
- **FR-015**: Existing supported executable artifacts MUST remain readable; corrected classification MUST take effect through newly compiled publication artifacts unless a separately approved artifact migration exists.
- **FR-016**: Any changed durable trigger-binding shape MUST use explicit schema evolution and retain historical compatibility evidence.
- **FR-017**: Publication-wide transactionality, executable/source-reference persistence ordering, diagnostics APIs, persisted publication-status records, startup health checks, shell dependency changes, connected-host expansion, Studio changes, multi-node route invalidation, and stimulus-router or actor redesign MUST remain outside this work unit.

### Key Entities

- **Trigger Preflight Outcome**: The complete, immutable assessment of every trigger-capable executable node before registration mutation; contains artifact identity and per-node outcomes.
- **Trigger Node Outcome**: Associates one executable node with its classification, recognizing provider identifier, recognition status, produced bindings, and any required provider-owned projection.
- **Trigger Binding Candidate**: A validated, not-yet-persisted normalized stimulus binding derived from one executable node.
- **Recurring Schedule Candidate**: A validated, not-yet-persisted Timer/Cron schedule materialized locally by the scheduling module before registration mutation.
- **Trigger Contract Matrix**: The verification table defining expected behavior for Event, Timer, Cron, and HttpEndpoint.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: All four first-party trigger families have passing contract-matrix coverage for valid, invalid, and applicable non-start cases.
- **SC-002**: In every invalid-publication acceptance case, 100% of previously stored trigger bindings and recurring schedules remain unchanged.
- **SC-003**: Every successfully published first-party start trigger produces the complete expected set of usable bindings and required provider-owned projections; zero classified start triggers succeed with an unintentionally empty registration set.
- **SC-004**: Every preflight result identifies exactly one provider for each recognized node, including intentionally non-starting nodes.
- **SC-005**: All committed historical catalog, executable, and trigger-binding compatibility fixtures remain readable, and same-version catalog reconciliation produces zero hash conflicts.
- **SC-006**: Runtime package-boundary verification confirms that trigger publication and execution require no Design-domain dependency.

## Assumptions

- The existing executable trigger marker remains the runtime-side classification input; this unit clarifies and validates the contract rather than redesigning the executable tree.
- `Recognized([])` is the established representation of intentional non-start behavior and remains supported.
- Trigger-binding replacement remains scoped by artifact and retains its current provider-specific fan-out semantics.
- The recurring schedule is a required publication projection for Timer and Cron start triggers.
- Detailed publication diagnostics and shell-provider availability are separately owned by Units B and C.
- Existing databases may contain legacy activity catalog rows and executable artifacts; compatibility is proven without mutating their same-version authored content.

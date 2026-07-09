# Feature Specification: Runtime Value Evidence

**Feature Branch**: `[004-runtime-value-evidence]`

**Created**: 2026-07-09

**Status**: Draft

**Input**: User description: "Enable useful workflow runtime diagnostics by capturing bounded diagnostic snapshots of activity and workflow values by default, without storing unsafe raw payloads, with Studio-managed requested settings capped by Host Policy and future Runtime Payload Reference support."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Troubleshoot Activity Values Safely (Priority: P1)

As a workflow operator or builder, I can inspect a completed workflow activity and see the runtime input and output values as bounded Diagnostic Snapshots, so I can diagnose why the activity behaved the way it did without needing raw payload capture.

**Why this priority**: Missing runtime values blocks diagnosis of completed runs. Diagnostic Snapshots provide the minimum useful evidence while avoiding the default risk of storing full payloads.

**Independent Test**: Can be tested by running a workflow with a completed activity that receives inputs and produces outputs, then opening the activity inspector and verifying that the Activity tab renders diagnostic value evidence for the future run.

**Acceptance Scenarios**:

1. **Given** a fresh installation where Host Policy allows Diagnostic Snapshots, **When** an activity executes with input and output values, **Then** future inspection of that activity shows Diagnostic Snapshots for those values by default.
2. **Given** an activity value contains nested objects, arrays, long strings, or unsupported values, **When** the activity inspector renders its evidence, **Then** the UI shows a bounded tree with truncation and unsupported-value markers instead of raw full payloads.
3. **Given** an activity value contains sensitive data or a sensitive-name match, **When** evidence is captured and rendered, **Then** the sensitive value is replaced by a redaction marker that preserves useful shape without exposing the value.

---

### User Story 2 - Control Runtime Diagnostics In Studio (Priority: P2)

As a Studio administrator, I can configure the requested Runtime Value Evidence Level for workflow runtime diagnostics from the UI and see the effective level after Host Policy caps are applied.

**Why this priority**: Users need control without changing application configuration. The UI must expose what they can request and why the backend may cap it.

**Independent Test**: Can be tested by changing the Workflows runtime diagnostics setting in Studio, observing the requested and effective values, then running a new workflow instance to verify the new setting affects only future evidence.

**Acceptance Scenarios**:

1. **Given** Host Policy allows Diagnostic Snapshots, **When** an administrator opens Workflows runtime diagnostics settings, **Then** the requested default is Diagnostic Snapshots and the effective default is Diagnostic Snapshots.
2. **Given** Host Policy caps activity outputs at Metadata only, **When** an administrator requests Diagnostic Snapshots for activity outputs, **Then** Studio shows the requested level, the lower effective level, and the host-policy limitation reason.
3. **Given** an administrator changes the requested level, **When** existing workflow instances are inspected, **Then** their previously captured evidence remains unchanged.

---

### User Story 3 - Govern Unsafe Payloads And Sensitive Values (Priority: P3)

As a security administrator, I can allow diagnostics to be useful while preventing unsafe payload storage by requiring bounded snapshots, redaction, explicit permissions, and Host Policy approval before full payloads can be captured or shown.

**Why this priority**: Runtime diagnostics are valuable only if they do not silently become a data-exposure mechanism.

**Independent Test**: Can be tested by setting Host Policy below Full Payload, attempting to enable Full Payload in Studio, and verifying that the UI and API refuse or cap the request while still allowing safer Diagnostic Snapshots.

**Acceptance Scenarios**:

1. **Given** Host Policy does not allow Full Payload capture, **When** a user requests Full Payload capture, **Then** the backend rejects or caps the request and Studio explains the effective limitation.
2. **Given** a user lacks permission to manage runtime diagnostics, **When** they open the settings UI, **Then** they can view effective policy state as permitted but cannot change the requested setting.
3. **Given** a user lacks permission to view captured value evidence, **When** they inspect an activity execution, **Then** the UI shows permission-hidden evidence states instead of values.

---

### User Story 4 - Prepare Reference-Based Evidence (Priority: P4)

As a platform integrator, I have a documented Runtime Payload Reference shape that can later represent secrets, blobs, artifacts, or large values without storing them inline in activity evidence.

**Why this priority**: Secrets and large payloads need a different evidence model, but the first implementation should not pretend that generic snapshotting can safely solve reveal or download flows.

**Independent Test**: Can be tested by reviewing the API contract and verifying that the reserved reference leaf contains only safe display metadata, authorization state, and a stable reference identifier, while first-slice runtime capture does not emit reference leaves until providers and resolution endpoints exist.

**Acceptance Scenarios**:

1. **Given** the first Runtime Value Evidence slice is deployed, **When** Diagnostic Snapshots are captured generically, **Then** Runtime Payload Reference leaves are not emitted until a provider-backed reference and resolver slice exists.
2. **Given** a future Diagnostic Snapshot contains a Runtime Payload Reference leaf, **When** Studio renders it before resolution support is available, **Then** it displays safe metadata and an unavailable resolution state without provider internals.

### Edge Cases

- Host Policy caps the requested level below the Studio-managed setting.
- Runtime diagnostics settings change while workflow instances are already running.
- Existing runs were captured before Runtime Value Evidence existed or before a setting changed.
- Values contain names or metadata matching sensitive-data rules even if the value type itself is simple.
- Values are very large, deeply nested, cyclic, unserializable, lazily evaluated, or throw during snapshot generation.
- Snapshot generation reaches max depth, max property count, max array item count, max string preview length, or max total serialized size.
- A user can inspect activity metadata but not captured value evidence.
- Full Payload capture is requested when Host Policy or permissions do not allow it.
- Studio receives a malformed, partially missing, or future-version Diagnostic Snapshot.
- A Runtime Payload Reference leaf exists but no resolver, permission, or provider integration is available.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST define Runtime Value Evidence Levels ordered as Off, Metadata only, Diagnostic snapshots, and Full payloads.
- **FR-002**: Fresh installations MUST request Diagnostic snapshots for workflow and activity input/output evidence by default, subject to Host Policy caps.
- **FR-003**: Host Policy MUST define the maximum allowed Runtime Value Evidence Level globally and MAY define lower maximums per evidence subject.
- **FR-004**: The backend MUST compute an effective runtime diagnostics setting by applying Host Policy caps to the Studio-managed requested setting.
- **FR-005**: Studio MUST expose Workflows runtime diagnostics settings that show requested levels, effective levels, and host-policy limitation reasons.
- **FR-006**: Changes to runtime diagnostics settings MUST apply prospectively to future capture events and MUST NOT mutate evidence already captured for existing workflow runs.
- **FR-007**: Diagnostic Snapshot generation MUST enforce configurable limits for maximum depth, object property count, array item count, string preview length, and total serialized snapshot size.
- **FR-008**: Diagnostic Snapshot generation MUST redact values marked sensitive by source metadata, type metadata, or sensitive-name rules.
- **FR-009**: Diagnostic Snapshot generation MUST preserve useful value shape by rendering redaction, truncation, unsupported-value, and snapshot-error markers instead of failing the whole capture.
- **FR-010**: The activity execution inspection API MUST expose captured Diagnostic Snapshot evidence for activity inputs and outputs when the effective level allows it.
- **FR-011**: The workflow instance/run details UI MUST render Diagnostic Snapshots as compact expandable trees with clear redaction, truncation, unsupported, error, and permission-hidden states.
- **FR-012**: The UI MUST avoid excessive noise by summarizing empty, missing, metadata-only, redacted, or truncated evidence and expanding detailed trees only when useful.
- **FR-013**: Full Payload capture MUST remain unavailable unless Host Policy allows it and the user has explicit permission to request or view it.
- **FR-014**: Runtime Payload Reference leaves MUST expose only a reference identifier, reference kind, safe display metadata, and current resolution availability; provider internals and protected values MUST NOT be included inline.
- **FR-015**: The first implementation MUST reserve the Runtime Payload Reference leaf shape but MUST NOT emit reference leaves until provider-backed reference creation and resolution endpoints are implemented.
- **FR-016**: The backend MUST reject unknown evidence levels, unknown subject identifiers, invalid limit values, and requested settings the caller is not allowed to manage.
- **FR-017**: The system MUST expose separate permissions for managing runtime diagnostics, requesting Full Payload capture, viewing snapshot evidence, and future reveal/download of referenced payload material.
- **FR-018**: Runtime evidence APIs MUST remain backward-compatible for runs that have no captured value evidence.
- **FR-019**: Deferred follow-up work MUST be captured as issue seeds covering reference resolution endpoints, permissions, audit, secret-provider integration, blob/artifact storage integration, retention, per-workflow overrides, and Full Payload exposure policy.

### Key Entities

- **Runtime Value Evidence Level**: The ordered user-facing capture level: Off, Metadata only, Diagnostic snapshots, or Full payloads.
- **Workflow Runtime Diagnostics Setting**: The Studio-managed requested runtime diagnostics configuration for the Workflows module.
- **Effective Runtime Diagnostics Setting**: The backend-computed setting after applying Host Policy caps and permission constraints.
- **Host Policy**: Application-owned configuration that defines the maximum evidence level the UI may request and the backend may capture.
- **Runtime Evidence Subject**: A class of capturable evidence, such as workflow inputs, workflow outputs, activity inputs, activity outputs, incidents, diagnostics, container variables, or durable values.
- **Diagnostic Snapshot**: A bounded, policy-sanitized representation of a runtime value for troubleshooting; it is not the original payload and is not intended for replay.
- **Snapshot Marker**: A node that represents redaction, truncation, unsupported values, snapshot errors, or permission-hidden evidence.
- **Runtime Payload Reference**: A protected pointer to payload material not stored inline in Runtime Evidence; resolving it requires separate authorization and audit.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On a fresh installation where Host Policy allows Diagnostic Snapshots, a workflow run with simple activity inputs and outputs produces inspectable Diagnostic Snapshot evidence without any manual configuration.
- **SC-002**: 100% of Diagnostic Snapshots in automated tests honor configured bounds for depth, property count, array item count, string preview length, and total snapshot size.
- **SC-003**: 100% of test values marked sensitive by metadata or sensitive-name rules render as redaction markers rather than original values.
- **SC-004**: Studio settings display both requested and effective Runtime Value Evidence Levels, including at least one host-policy limitation reason when a request is capped.
- **SC-005**: Settings changes affect newly captured evidence only; existing run evidence remains byte-for-byte unchanged in regression tests.
- **SC-006**: Activity inspection renders simple, nested, redacted, truncated, missing, metadata-only, permission-hidden, and malformed snapshot states without blocking the rest of the inspector.
- **SC-007**: Attempts to enable or view Full Payload evidence without Host Policy approval and explicit permission are rejected or capped in API and UI tests.
- **SC-008**: The Runtime Payload Reference contract is documented and covered by rendering tests using fixture data, while production capture emits no reference leaves in the first slice.

## Assumptions

- The first slice is tenant/module-wide for Workflows runtime diagnostics; per-workflow and per-executable overrides are deferred.
- The backend owns effective policy resolution and evidence capture; Studio owns settings and rendering.
- Cross-repository backend work may be required outside this Studio repository to capture and expose evidence.
- Existing Studio authentication, permission, settings, and workflow instance inspection patterns will be reused.
- Diagnostic Snapshots are diagnostic evidence, not replay data, archival payload storage, or a replacement for domain-specific audit records.
- Runtime Payload Reference resolution, provider integrations, audit records for reveal/download, and retention policy are deferred follow-up work.

## Deferred Follow-Up Issue Seeds

- Define Runtime Payload Reference resolution endpoints for reveal and download flows.
- Define permissions for viewing snapshots, revealing protected values, downloading referenced blobs, and revealing secret-backed references.
- Design audit records for every Runtime Payload Reference resolution.
- Design secret-provider integration for protected secret references.
- Design blob and artifact storage integration for large payload references.
- Decide retention and deletion behavior for Diagnostic Snapshots and referenced payload material.
- Decide whether per-workflow or per-executable diagnostics overrides are needed after the tenant/module-wide setting ships.
- Decide how Full Payload capture is exposed, if at all, beyond development and explicit allow-list scenarios.

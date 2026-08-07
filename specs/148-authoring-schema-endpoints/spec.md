# Feature Specification: Authoring-Schema Endpoints for Headless Clients

**Feature Branch**: `1164-authoring-schema-endpoints`

**Created**: 2026-08-07

**Status**: Retrofit — documents behavior shipped in PR #1170 (issue #1164); source of truth for review and future evolution, not a driver of new implementation.

**Input**: User description: "Expose the authoring schema through the design API so headless clients (CI pipelines, code generators, AI agents) can author workflow definitions programmatically without embedding Studio's private knowledge: submit-body schema, structure-kind registry, and feature-to-activity mapping (GitHub issue #1164)."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Discover the submit-body contract (Priority: P1)

A headless authoring client (CI pipeline, code generator, AI agent) needs to construct valid workflow-definition submissions without reverse-engineering Studio traffic or reading server source. It fetches a machine-readable schema for the submission body, generates or validates payloads against it, and relies on the schema staying truthful to what the server actually accepts.

**Why this priority**: This is the core gap from issue #1164 — without it, every headless client maintains a hand-verified submit-envelope reference that silently rots. It alone delivers a viable authoring loop (author → validate locally → submit → fix from validation errors).

**Independent Test**: Fetch the schema endpoint; validate a known-good submission body against the returned schema; submit the same body and confirm the server accepts it.

**Acceptance Scenarios**:

1. **Given** a running server with the workflow design API composed, **When** a client requests the submit-body schema, **Then** it receives a versioned schema document with a stable content fingerprint.
2. **Given** a submission body the server accepts (including bodies omitting optional members such as the operation key), **When** the client validates that body against the served schema, **Then** validation passes — the schema never rejects a body the server accepts.
3. **Given** the served schema, **When** the client inspects property names and enumeration values, **Then** they match actual wire traffic exactly (same casing, same value spellings).
4. **Given** two requests to the schema endpoint against an unchanged server, **When** the fingerprints are compared, **Then** they are identical, and a fingerprint change always signals a contract change.

---

### User Story 2 - Discover composable structure kinds (Priority: P2)

A headless client composing container activities (sequences, flowcharts, branches, loops) needs to know which structure kinds the target server supports and what each kind's payload looks like, so it can compose nested definitions without reading source.

**Why this priority**: Structure payloads are deliberately opaque in the submit-body schema (they are owned by the activity modules), so this registry is the only discoverable source for their shape. Without it, clients can submit leaf-only workflows but cannot compose real ones.

**Independent Test**: Query the structure registry on a server composition with a known feature set; confirm the listed kinds match the composed features and each carries a usable payload schema.

**Acceptance Scenarios**:

1. **Given** a server composition, **When** a client requests the structure registry, **Then** it receives exactly the structure kinds registered in that composition — never a hardcoded list.
2. **Given** a listed structure kind, **When** the client reads its entry, **Then** the entry states the kind identifier, its schema version, whether the container owns scoped variable declarations, and a schema of the authored payload.
3. **Given** a composition without a given container feature, **When** the registry is requested, **Then** that feature's structure kinds are absent from the response.
4. **Given** a structure kind whose payload shape is not published by its owner, **When** the registry is requested, **Then** the kind is still listed and its payload schema is explicitly absent (opaque by choice, not by omission).

---

### User Story 3 - Map an activity type to its providing feature (Priority: P3)

An operator or headless client managing server composition needs to answer: "this definition uses activity type X — which feature must the target server enable?" It reads provenance on the activity catalog and version details instead of diffing catalogs across compositions.

**Why this priority**: Valuable for fleet/composition management and removes a whole class of operator surprise (activity availability differing per composition), but authoring itself works without it.

**Independent Test**: List the activity catalog on a known composition and confirm each entry's provenance identifies its source, and — for activities provided by code modules — the providing feature.

**Acceptance Scenarios**:

1. **Given** an activity definition contributed by a code (CLR) module, **When** the catalog or version details are read, **Then** the entry carries provenance naming the source kind, source identifier, and the providing feature.
2. **Given** an activity definition from a non-code source (file/JSON, design-authored), **When** provenance is read, **Then** source kind and source identifier are populated and the feature attribution is explicitly null.
3. **Given** a built-in engine intrinsic catalog entry, **When** the catalog is read, **Then** it carries no provenance (it has no persisted source).
4. **Given** an activity whose providing feature is currently disabled in the composition, **When** provenance is read, **Then** the feature is still named — this is the "enable feature X to use this activity" signal, not an error.

---

### Edge Cases

- Server composition registers no structure kinds → the registry returns an empty list with a valid fingerprint, not an error.
- Shell lacks the type registry or the runtime feature catalog → provenance degrades to null feature attribution; the catalog request still succeeds.
- Deliberately polymorphic members (argument values, activity-owned structure payloads) appear in the submit-body schema as unconstrained — clients must treat them as opaque and use the structure registry for per-kind shapes.
- Schema-validity is necessary but not sufficient: draft/publish validations still apply semantic rules (unknown activity versions, required inputs, variable uniqueness). Clients must not interpret schema validation as publish validation.
- Multiple features in one code module → attribution picks a single deterministic feature.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The design API MUST serve a machine-readable, versioned schema describing the workflow-definition submission body, discoverable via the API's capability document.
- **FR-002**: The served schema MUST be derived from the server's actual wire contract at serving time, such that it cannot drift from what the server deserializes — property names, casing, and enumeration value spellings MUST match wire traffic.
- **FR-003**: The schema MUST mark a member as required only when the server cannot accept its absence. Members whose absence the server tolerates (treated as null/unset) MUST be optional. *(Hardened after review: the initial version over-claimed nullable members as required.)*
- **FR-004**: The schema document MUST carry a content fingerprint that is stable across requests against an unchanged contract and changes when the contract changes.
- **FR-005**: The design API MUST serve a registry of composite-activity structure kinds discovered from the active server composition; the set MUST never be hardcoded.
- **FR-006**: Each registry entry MUST state the kind identifier, its schema version, whether the container owns scoped variable declarations, and a schema of the authored payload when the owning module publishes one; an unpublished payload shape MUST be represented as explicitly absent.
- **FR-007**: Activity catalog items and activity version details MUST carry provenance: the contributing source kind and source identifier, plus the providing feature for activities contributed by code modules.
- **FR-008**: Feature attribution MUST be best-effort and non-failing: null for non-code sources, null when the composition lacks the services needed to resolve it, and permitted to name a feature that is currently disabled.
- **FR-009**: Both new endpoints MUST be readable with the design API's read permissions and advertised as capability links.
- **FR-010**: All additions MUST be non-breaking: no existing route, field, permission, persisted shape, or content hash changes; new response fields are additive and optional.

### Key Entities

- **Submit-body schema document**: versioned (`schemaVersion`), fingerprinted (`fingerprint`), with a standard JSON Schema payload (`schema`) describing the submission body (name, description, operation key, and the definition state: variables, root activity tree, inputs, outputs, strategy options).
- **Structure-kind descriptor**: one per registered composite structure kind — kind identifier (e.g. `elsa.sequence.structure`), schema version, scoped-variable capability, optional authored-payload schema; the collection carries a fingerprint.
- **Provenance record**: source kind, source identifier, nullable providing-feature identifier; attached to activity catalog items and version details; absent on built-in intrinsics.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A headless client can construct and validate a workflow-definition submission using only served endpoints — zero reads of server source, Studio traffic captures, or pinned catalog dumps.
- **SC-002**: 100% of submission bodies the server accepts validate successfully against the served schema (no false rejections by schema-validating clients).
- **SC-003**: Every structure kind usable in a given server composition is discoverable through the registry, including its payload shape when published.
- **SC-004**: For any catalog activity contributed by a code module, a client can identify the providing feature from the API alone.
- **SC-005**: Existing API clients observe zero behavioral change: all pre-existing routes, fields, and validations behave identically.

## Assumptions

- The schema documents the server's *deserialization* contract; semantic draft/publish validations remain a separate, authoritative layer with their own error contract.
- The structure registry publishes the *authored* payload shape (what clients submit), not the compiled executable shape.
- Feature attribution naming a disabled feature is intentional signal, not a defect; catalogs may legitimately contain activities whose features are not composed.
- One deterministic feature per code module is an acceptable attribution granularity.
- This is a retrofit: the shipped behavior (PR #1170, including the review hardening of FR-003) is the baseline; future contract changes must update this spec first.

# Feature Specification: Authoring-only API

**Feature Branch**: `codex/2459-authoring-api-contract`

**Created**: 2026-10-06

**Status**: Draft

**Input**: Sipke's decision: "Do the decoupling first. We definitely want an authoring-only API." Program [#1959](https://github.com/elsa-workflows/elsa-foundation/issues/1959), parent [#1961](https://github.com/elsa-workflows/elsa-foundation/issues/1961), specification [#2459](https://github.com/elsa-workflows/elsa-foundation/issues/2459).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Publish without exposing execution (Priority: P1)

An operator hosts design and publication separately from workflow execution. An authorized developer can persist a design, review publication preflight, publish it, inspect its publication and slot, and download its executable artifact without enabling workflow execution in the authoring host.

**Why this priority**: This is the approved Authoring product boundary. Removing execution must leave a useful publication journey.

**Independent Test**: Against a rebuilt authenticated host, complete design → preflight → publish → publication/slot/artifact read → export and inspect all exposed operations. Repeat after restarting the host with its same durable configuration.

**Acceptance Scenarios**:

1. **Given** an authoring-only host and an authorized developer, **When** the developer saves a design, preflights and publishes it, **Then** the publication, artifact, source provenance, activation slot and trigger projections persist and can be read without executing the workflow.
2. **Given** that publication, **When** the host restarts, **Then** the same publication, artifact, slot and trigger projections remain available and the executable can be exported.
3. **Given** an authoring-only host, **When** its exposed operations and advertised capabilities are inspected, **Then** no runtime execution/inspection/operational API or workflow/activity test-run operation is present.
4. **Given** unauthenticated or insufficiently authorized requests, **When** an allowed publication operation is requested, **Then** the existing authentication and permission policy denies it; denied requests leave publication state unchanged.

### User Story 2 - Use publication tools with honest capabilities (Priority: P2)

A developer connects Studio to the authoring-only host. Publication review, artifact discovery and download work through publication-owned capabilities. Execution controls explain their unavailability when the host does not advertise the required execution operation.

**Why this priority**: A narrower server must remain usable through its real client, without exposing execution merely to populate publication screens.

**Independent Test**: Connect the current Studio client to the rebuilt authoring host, publish a workflow, inspect its slot and artifact, download it, and verify workflow draft Run, activity draft Test Run and published executable Run are unavailable. Repeat against a combined host as a compatibility control.

**Acceptance Scenarios**:

1. **Given** a published authoring workflow, **When** the developer opens publication review and artifacts, **Then** the views load through advertised publication-owned reads and preserve artifact/version/source identities needed for export.
2. **Given** an occupied slot owned by another source, **When** its publication view is read, **Then** the source identity is visible and the client does not present the slot as empty or as its own publication.
3. **Given** repeated publication of one version or retained retired history, **When** the developer exports a selected publication, **Then** its exact artifact and publication provenance is exported or the operation refuses; it does not choose a different publication.
4. **Given** no advertised execution capability, **When** the developer uses any of the three execution controls, **Then** the control is unavailable with an explanation and sends no execution request.
5. **Given** a combined host advertising those operations, **When** the same client performs its established publication and test-run journeys, **Then** the existing behavior remains available.

### User Story 3 - Select the reviewed Authoring composition (Priority: P3)

An operator chooses a versioned Authoring starting profile with shared persistence defaults and explicit feature overrides, reviews the resolved feature set, and generates a host configuration whose actual behavior matches the selection.

**Why this priority**: Dependency decoupling alone is not a published or verified starting profile.

**Independent Test**: Use the real planning/acceptance/generation tools to produce the Authoring configuration, start the intended host from that generated file, and repeat Story 1 and absence checks. Verify old accepted catalog/profile pins retain their original bytes and behavior.

**Acceptance Scenarios**:

1. **Given** a reviewed Authoring selection and a centrally named persistence resource, **When** configuration is accepted, generated and consumed by a new host process, **Then** design, publication and required internal artifact/activation persistence use that resource and the host exposes only the reviewed authoring operations.
2. **Given** an explicit feature persistence override, **When** the selection is resolved, **Then** existing supported-layout and transactional compatibility rules still apply; a forbidden split is refused before activation.
3. **Given** an older accepted selection, **When** the catalog gains the new Authoring profile, **Then** the older bytes/pins remain valid without silently changing their feature closure.

### Edge Cases

- An authenticated request to a prohibited route must establish absence, not merely receive an authorization denial for a route that exists.
- Required publication dependencies must resolve inside a real host scope; missing services cannot be repaired by enabling the full execution runtime.
- An operator explicitly selects conflicting narrow/full publication surfaces: refuse the ambiguous selection before exposing routes rather than silently choosing one or mapping duplicates.
- A slot has foreign occupancy, a retired publication, no active publication, or a journal record without its artifact, including legitimately pruned retired history: preserve the distinction and report existing lifecycle failures honestly.
- Provider registration is repeated or ordered differently, or a custom provider already owns required contracts: preserve current ownership/refusal rules and atomicity.
- Private persistence keys are missing or invalid: existing startup validation remains authoritative. No secret values enter public diagnostics.
- A client sees a publication capability without the required read relation: report the task unavailable; do not invent runtime URLs.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The Authoring composition MUST support authenticated persisted design, preflight, publication, publication/slot/artifact reads, publication policy, unpublish/restore and executable export without enabling execution.
- **FR-002**: The Authoring composition MUST expose no runtime HTTP operation and none of the six existing publication workflow/activity test-run operations; it MUST advertise no corresponding capability.
- **FR-003**: Compilation, requirement validation, trigger projection, publication activation and export MUST remain supported internal publication operations; they MUST NOT dispatch a workflow or implicitly start execution/redrive/alteration background work.
- **FR-004**: Authentication, permission denial, JSON shapes and error handling for retained operations MUST preserve existing contracts.
- **FR-005**: Publication slot reads MUST preserve slot revision, active identity, source kind/source ID and visible publication state, including foreign occupancy.
- **FR-006**: Publication-owned artifact discovery MUST identify this definition's published artifacts and retained publication provenance sufficiently for the current Studio artifact/export tasks, without exposing runtime execution records or global operational inspection.
- **FR-007**: Studio MUST discover publication reads and gate workflow draft Run, activity draft Test Run and published executable Run by their respective advertised execution capabilities. Absence MUST yield an honest unavailable state and no guessed endpoint request.
- **FR-008**: Explicit full Runtime and full Publishing API compositions MUST retain their established execution/test-run operations and client compatibility.
- **FR-009**: Required durable publication/artifact/activation/trigger state MUST retain the existing atomic backend, schema/migrations and centrally named persistence resource contract. The initial Authoring implementation MUST NOT invent a lighter persistence family or weaken private-key validation.
- **FR-010**: Dependency resolution MUST be deterministic, shared registration MUST be idempotent, and existing explicit provider ownership MUST be respected.
- **FR-011**: A new Authoring profile/catalog publication MUST occur only after the actual generated configuration, authenticated host and client journeys have been verified. Existing pinned catalog/profile bytes MUST remain immutable; changed closure requires explicit re-resolution.
- **FR-012**: Verification MUST reuse existing Authoring host and normal contract/route/provider fixtures. This work MUST NOT add a new EF suite, provider matrix or CI cadence.

- **FR-013**: Exporting a selected publication MUST preserve its exact artifact and source provenance or refuse; the established version-based export MUST retain its existing selection behavior.

### Key Entities

- **Authoring surface**: The reviewed non-executing design/publication operations and their advertised capabilities.
- **Publication support boundary**: Internal artifact, compilation, activation and trigger services required to publish without execution.
- **Publication slot view**: Slot identity/revision, occupancy source and joined publication status.
- **Published artifact summary**: Publication-owned artifact/version/source identities and lifecycle facts required for authoring discovery and export.
- **Versioned Authoring selection**: Immutable starting profile/catalog pin plus resolved granular feature set and resource bindings.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A real authorized actor completes every step of persisted design → preflight → publish → publication/slot/artifact read → export, before and after a process restart, without executing the workflow.
- **SC-002**: The reviewed prohibited-operation inventory has zero mapped runtime or publication test-run operations, zero advertised corresponding capabilities and successful absence controls for every prohibited family and all six test-run operations.
- **SC-003**: All three Studio execution controls report unavailable without sending execution requests on the authoring host, while publication review, artifact discovery and export succeed.
- **SC-004**: Existing combined-host publication and test-run actors pass; existing catalog pins remain byte-identical and their compatibility checks pass.
- **SC-005**: A fresh process consumes an actual tool-generated Authoring configuration and repeats the authenticated journey and absence controls on its configured durable resource.

## Assumptions

- The existing host authentication substrate and publication permissions remain in use; this is not an identity-provider redesign.
- Authoring-only means the composed host exposes no runtime or test-run API. Explicitly adding full execution surfaces creates a combined composition rather than an Authoring-only one.
- Internal Runtime-owned publication stores and activation contracts are necessary; their ownership does not imply an execution API.
- The existing complete Runtime persistence aggregate and its private key requirements remain the initial durable storage boundary; reducing its table footprint is separate work requiring evidence.
- The specification issue publishes a reviewed contract and proof plan. It does not itself implement decoupling, run the actors, change Studio or publish the Authoring profile.

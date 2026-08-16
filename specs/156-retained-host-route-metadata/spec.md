# Feature Specification: Retained Host Route Ownership and Security Metadata

**Feature Branch**: `codex/1365-host-route-metadata`

**Created**: 2026-08-16

**Status**: Draft

**Input**: Issue #1365, Foundation H: Complete ownership and security metadata for retained host routes.

## User Scenarios & Testing

### User Story 1 - Inspect retained host routes (Priority: P1)

As an operator or migration tool, I need every retained root-hosted endpoint to identify its host owner,
authoring model, and one security disposition so that route ownership and access boundaries are reviewable.

**Why this priority**: Missing metadata prevents the migration inventory from proving that retained host routes are safe.

**Independent Test**: Build each retained Workbench and Foundation Host route surface and generate a manifest; every entry has exactly one owner and disposition and the expected owner/count/method/route identity.

**Acceptance Scenarios**:

1. **Given** a retained host endpoint is published, **when** the runtime manifest is built, **then** it reports host ownership, Minimal API authoring, route, method, and exactly one typed security disposition.
2. **Given** an endpoint has missing, duplicate, or contradictory metadata, **when** the manifest is validated, **then** validation fails with route and owner context.

### User Story 2 - Preserve host security boundaries (Priority: P1)

As a host administrator, I need management-key, trusted-caller, named-policy, public-health, CORS, and SignalR behavior to remain unchanged while metadata is added.

**Why this priority**: Metadata must not weaken existing host-control or diagnostic protections.

**Independent Test**: Exercise authorized and unauthorized management, health, console-log, and SignalR requests and compare the status, headers, and content contracts to the reviewed behavior.

**Acceptance Scenarios**:

1. **Given** an anonymous or untrusted caller invokes host-control routes, **when** the request is processed, **then** it is rejected without exposing management operations.
2. **Given** a configured management key is supplied by a trusted caller, **when** a host-control request is processed, **then** the existing operation succeeds through the same server-side credential boundary.
3. **Given** a health or console-log request uses the established public/default-policy boundary, **when** it is processed, **then** the existing HTTP, CORS, SignalR, and authorization behavior remains intact.

### Edge Cases

- A host-control endpoint marked with a public disposition or missing its custom credential-enforcement marker is rejected by manifest validation.
- A route with more than one owner or security disposition is rejected rather than choosing an arbitrary metadata entry.
- CShells management remains inaccessible when the server-side management key is absent or invalid.
- Health endpoints remain root-hosted and excluded from CShells route resolution.
- Disabled Extension Builder and console-log switches publish no routes.

## Requirements

### Functional Requirements

- **FR-001**: Every retained Workbench and Foundation Host endpoint MUST publish exactly one immutable host ownership metadata record and the Minimal API authoring model.
- **FR-002**: Every retained endpoint MUST publish exactly one typed security disposition: host credential, named host policy, or intentional public access with category and reason.
- **FR-003**: Runtime manifest validation MUST reject missing, duplicate, conflicting, or contradictory ownership/security/authoring metadata.
- **FR-004**: Extension Builder, Workbench module management, Foundation Host module management, and CShells management MUST retain their existing management-key or trusted-caller enforcement.
- **FR-005**: Workbench CShells management MUST be protected by the server-side host-management-key boundary from ADR 0037; no Foundation user permission may be invented for this boundary.
- **FR-006**: Workbench and Foundation Host health endpoints MUST remain intentionally public with typed reasons and unchanged status payload behavior.
- **FR-007**: Workbench console-log HTTP and SignalR endpoints MUST retain default-policy authorization, CORS behavior, streaming paths, and content contracts.
- **FR-008**: The retained-host manifest MUST cover the exact surfaces and counts in issue #1365: Extension Builder 42; Workbench module management 9; Workbench root/readiness 3; Workbench console-log 3; Foundation Host module management 2; Foundation Host health 2; CShells management 6.

### Key Entities

- **Host ownership metadata**: Immutable host kind and stable host identifier attached to each endpoint.
- **Security disposition metadata**: One inspectable access classification with credential/policy reference or public category/reason.
- **Retained host endpoint manifest**: Deterministic route/method/owner/authoring/security record used by migration and architecture gates.

## Success Criteria

### Measurable Outcomes

- **SC-001**: 67 semantic retained root-host surfaces across Workbench, Foundation Host, and CShells management
  validate with exactly one owner and disposition; the physical ASP.NET Core manifest has 68 entries because
  SignalR publishes a negotiate endpoint alongside the conceptual hub route.
- **SC-002**: 100% of manifest validation fixtures for missing, duplicate, and contradictory metadata fail deterministically with route context.
- **SC-003**: Existing host-control negative and positive security scenarios retain their reviewed status outcomes, including CShells management fail-closed behavior.
- **SC-004**: Health, CORS, HTTP, OpenAPI, and SignalR route contracts remain byte/behavior compatible except for added inspectable metadata.

## Assumptions

- Host-control credentials remain server-side management keys; Foundation Identity user permissions are not substituted.
- Root-hosted surfaces are not collectible shell assemblies, so unloadability is explicitly not applicable to this work unit.
- The existing shared endpoint metadata and manifest infrastructure remains the canonical implementation seam.

# Feature Specification: Secrets API Minimal API Migration

**Feature Branch**: `codex/1348-secrets-minimal-api`

**Created**: 2026-08-15

**Status**: Draft

**Input**: User description: "Migrate the complete Secrets REST API as the representative CRUD and security proof while preserving its HTTP, OpenAPI, tenant-isolation, authorization, disclosure, coexistence, and unloadability contracts."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Discover secret metadata safely (Priority: P1)

An authenticated tenant user lists, reads, filters, and selects secret metadata and discovers supported secret types and stores without seeing secret values, configuration keys, provider payloads, or another tenant's records.

**Why this priority**: Read and discovery operations are the broadest client-facing surface and establish the non-disclosure and tenant-isolation boundary before any mutation is migrated.

**Independent Test**: Compare the current and replacement list, get, descriptor, and picker operations across success, filtering, paging, missing-record, authorization, tenant, and redaction cases with no unapproved observable differences.

**Acceptance Scenarios**:

1. **Given** two tenants with same-named secrets and different metadata, **When** either tenant lists, gets, or picks secrets, **Then** only that tenant's safe metadata is returned.
2. **Given** active, revoked, expired, and deleted secrets, **When** a caller uses the existing filters and paging inputs, **Then** visibility, ordering, totals, and paging retain the current behavior.
3. **Given** a caller with read permission, **When** supported type and store descriptors are requested, **Then** the existing descriptor contract is returned without introducing a tenant requirement that does not exist today.
4. **Given** any successful or failed discovery request, **When** the complete response is inspected, **Then** no submitted value, configuration key, protected payload, provider-private metadata, or other tenant identifier is disclosed beyond the current safe metadata contract.

---

### User Story 2 - Manage the secret lifecycle without disclosure (Priority: P2)

An authenticated tenant administrator creates, updates, rotates, revokes, deletes, and tests secrets through the existing public operations while retaining granular permissions, lifecycle rules, safe failures, and metadata-only responses.

**Why this priority**: These operations combine sensitive request material, independent privileges, lifecycle transitions, conflicts, and failure branches, making them the representative CRUD and security proof for the program.

**Independent Test**: Exercise every lifecycle operation before and after the migration with exact, implied, wildcard, denied, malformed, conflicting, missing-record, and cross-tenant cases, then verify response compatibility, state transitions, audit safety, and absence of secret material.

**Acceptance Scenarios**:

1. **Given** a caller with the operation's exact permission and a valid request, **When** a secret is created, updated, rotated, revoked, deleted, or tested, **Then** the existing status, response, state transition, and audit behavior is preserved.
2. **Given** a create or rotate request containing a unique sensitive marker, **When** the operation succeeds or fails, **Then** that marker is absent from response bodies, headers, errors, metadata projections, and audit records.
3. **Given** an anonymous caller, an authenticated caller lacking the required permission, or an untrusted principal, **When** any lifecycle operation is attempted, **Then** the request is rejected with the required authentication or forbidden outcome and no mutation occurs.
4. **Given** a missing, duplicate, invalid, revoked, expired, or deleted secret state, **When** a lifecycle operation is attempted, **Then** the current not-found, conflict, validation, or safe test-result behavior is retained.
5. **Given** a route name and any body-supplied name, **When** an operation targets an existing secret, **Then** the route identity and authenticated tenant remain authoritative and cannot be overridden by request content.

---

### User Story 3 - Operate the migrated module in transitional hosts (Priority: P3)

An operator enables the migrated Secrets module in a host that still contains unmigrated endpoints and observes one explicit owner for all ten routes, shared authorization semantics, stable API discovery, and no retained dynamically unloadable endpoint context.

**Why this priority**: The program cannot advance from its canary to migration waves until a representative CRUD module proves coexistence, catalog ownership, and lifecycle safety at materially larger scope.

**Independent Test**: Compose the replacement surface with a representative legacy-authored route, inspect the endpoint and permission inventories, exercise both authorization paths, and release materialized route, service, and serialization owners before bounded collectible-context verification.

**Acceptance Scenarios**:

1. **Given** a mixed host, **When** routes and API descriptions are produced, **Then** every Secrets operation appears exactly once with its module owner, authoring disposition, and required permission.
2. **Given** exact read, write-implies-read, retained wildcard, and denied grants, **When** callers use replacement and unmigrated routes, **Then** both use the shared permission evaluator and produce consistent outcomes.
3. **Given** all generation-owned route, service, documentation, and serialization references have been released, **When** bounded unload verification runs repeatedly, **Then** the dynamically loaded Secrets endpoint context is collected or any retained stage is identified honestly.
4. **Given** a new Secrets permission-protected route without a unique catalog owner or security disposition, **When** repository gates run, **Then** the change fails with an owner-readable diagnostic.

### Edge Cases

- The current deployed update method is authoritative even though an older design contract describes another method.
- Descriptor discovery currently does not require a tenant claim; migration must not silently add one.
- List inputs include singular and plural type/store filters, status, active-only, page, and page size; empty, repeated, and out-of-range inputs must retain current binding and clamping behavior.
- Picker results currently use a bounded page and report inline creation as available; migration preserves that observable behavior unless a separately approved change replaces it.
- Test-operation failures are safe success responses with a result code rather than transport errors.
- Domain validation and conflict exceptions currently pass through the host's configured error behavior; their exact status and problem representation must be captured before replacement.
- Revoked and expired metadata visibility differs from runtime value usability; transport migration must not collapse those two rules.
- Repeated evidence capture must be deterministic despite generated identifiers, clocks, descriptor registration order, and build artifacts.
- Materializing endpoint handlers and API descriptions must not be replaced by reflection-only counting in unload tests.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST retain exactly the existing ten Secrets operations, methods, route templates, and route parameter names.
- **FR-002**: The system MUST preserve query, route, and body binding for list, create, get, update, rotate, revoke, delete, test, descriptor, and picker requests.
- **FR-003**: The system MUST preserve successful response documents, status codes, content types, and absence of additional response headers.
- **FR-004**: The system MUST preserve existing not-found, forbidden, conflict, validation, malformed-request, and safe test-result behavior, including no mutation after rejected operations.
- **FR-005**: The authenticated tenant claim MUST remain the sole tenant authority for data operations, and cross-tenant requests MUST retain the current invisible/not-found behavior.
- **FR-006**: Descriptor discovery MUST retain its current authorization behavior without adding a tenant-claim requirement.
- **FR-007**: All responses and errors MUST exclude raw secret values, configuration keys, protected payloads, provider-private metadata, and unsafe provider exception details.
- **FR-008**: Create and rotate MUST accept the current sensitive inputs while returning only safe metadata.
- **FR-009**: List and picker MUST preserve current filtering, deleted-record exclusion, active-only handling, paging bounds, totals, item shapes, and inline-creation value.
- **FR-010**: Get, list, and picker MUST preserve the intentional distinction between metadata visibility and runtime secret usability for revoked or expired records.
- **FR-011**: Each route MUST retain its existing granular action permission and the administrative wildcard grant through the shared permission evaluator.
- **FR-012**: The Secrets module MUST uniquely contribute all stable Secrets permission names to the active permission catalog with stable owner and contributor provenance.
- **FR-013**: The catalog MUST declare `secrets:write` as implying `secrets:read`; no other cross-action implication may be introduced by this work unit.
- **FR-014**: Anonymous callers MUST receive an authentication challenge; authenticated callers without the required permission, callers with adjacent permissions only, and untrusted normalized principals MUST be forbidden.
- **FR-015**: The module MUST own one explicit registration for every Secrets route and MUST NOT retain a second legacy registration for any migrated operation.
- **FR-016**: The replacement routes MUST coexist with representative unmigrated first-party routes in one host without changing either surface's observable contract or authorization semantics.
- **FR-017**: The consumed API description for all ten operations MUST remain unchanged unless an exact approved-difference record identifies a separately reviewed delta.
- **FR-018**: Before-and-after route manifests, HTTP observations, and consumed API descriptions MUST fail on every unapproved route, method, binding, payload, status, header, error, authorization, tenant, redaction, or documentation difference.
- **FR-019**: Repeated unchanged-surface captures MUST produce byte-stable compatibility evidence after normalizing only reviewed volatile values and separately requiring those values to remain present and valid.
- **FR-020**: Materialized route, service, serialization, and documentation references MUST be released before bounded weak-reference verification of a dynamically unloadable module context.
- **FR-021**: Existing Secrets domain, audit, tenant-isolation, persistence-provider, and module-composition tests MUST remain green.
- **FR-022**: The production Secrets API module MUST no longer register legacy-framework endpoints or require the legacy endpoint framework after migration.
- **FR-023**: This work unit MUST remain limited to the Secrets API and shared migration evidence required to prove it; Structured Logs and broad REST migration remain separate units.

### Key Entities

- **Secret Metadata**: The tenant-scoped, non-sensitive projection of a named secret, including lifecycle, type, store, scope, tags, version, and timestamps but never retrievable value material.
- **Secret Lifecycle Request**: A create, metadata update, rotation, revocation, deletion, or test request whose authenticated tenant and route name determine its target.
- **Secret Descriptor**: Safe discovery metadata for a supported secret type or store.
- **Secret Picker Result**: A bounded, filtered set of safe secret metadata used by authoring clients.
- **Permission Ownership Record**: The unique module-owned catalog entry for a stable Secrets permission and its explicit implication set.
- **Compatibility Evidence**: The reviewed endpoint inventory, HTTP observations, consumed API description, and exact approved differences used to prove contract preservation.
- **Unload Evidence**: Weak-reference and lifecycle-stage observations for materialized routes, services, serialization, and documentation generation.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: All ten operations complete before-and-after route, HTTP, and consumed API-description comparison with zero unapproved differences.
- **SC-002**: Every data operation passes two-tenant same-name isolation cases, and no observation contains another tenant's record or sensitive value marker.
- **SC-003**: The authorization matrix passes for anonymous, missing permission, adjacent permission, exact permission, write-implies-read, retained wildcard, untrusted principal, and resource denial on every applicable operation.
- **SC-004**: All stable Secrets permissions appear exactly once in the active catalog with the Secrets module as owner and only the reviewed write-to-read implication.
- **SC-005**: Ten consecutive unchanged-surface evidence captures are byte-identical after reviewed volatility normalization.
- **SC-006**: A mixed host exposes each reviewed Secrets and representative unmigrated route exactly once with one owner and one security disposition.
- **SC-007**: Repeated materialized-route and service release verification supplies collectible-context evidence and identifies any serialization or documentation retention stage rather than treating aggregate memory as proof.
- **SC-008**: Every existing Secrets behavior, audit, tenant, persistence, and composition test remains green after migration.
- **SC-009**: The final report records compatibility, authorization, tenant, disclosure, coexistence, and unload evidence plus a reviewable proceed, revise, or stop recommendation for subsequent migration waves.

## Assumptions

- Current production route registrations and observed wire behavior are authoritative where older design documentation differs.
- Existing authentication and external-provider claim normalization remain outside the Secrets module.
- The administrative wildcard remains a grant only and is not cataloged as a module permission.
- All eight existing Secrets permission constants are stable module-owned names; only read, write, update-value, delete, and test are currently consumed by HTTP routes.
- Write-to-read is the only intended implication added by the module catalog; update-value, delete, test, use, import, and export remain independently grantable.
- Compatibility differences are rejected by default; intentional contract redesign requires a separate approval and is not inferred from this authoring migration.
- Secret persistence/provider migration, user-interface changes, import/export endpoint design, dynamic collision publication, Structured Logs migration, and broad legacy-framework retirement remain out of scope.

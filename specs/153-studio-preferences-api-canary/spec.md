# Feature Specification: Studio Preferences API Canary

**Feature Branch**: `codex/1347-studio-preferences-minimal-api`

**Created**: 2026-08-15

**Status**: Draft

**Input**: User description: "Migrate the complete Studio Preferences REST surface as the first production canary while preserving its complete HTTP, OpenAPI, authorization, coexistence, and unloadability contracts."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Read preferences without contract drift (Priority: P1)

An authenticated Studio user reads a preference document through the existing public route and receives exactly the same document, status, content type, and revision header behavior as before the migration.

**Why this priority**: Reading preferences is the lowest-risk, highest-frequency path and proves that the first production canary can change its internal authoring model without affecting Studio clients.

**Independent Test**: Capture the reviewed route, request binding, response, headers, errors, and consumed API description before and after the migration, then verify that there are no unapproved differences.

**Acceptance Scenarios**:

1. **Given** an authenticated caller with read permission and an existing preference document, **When** the caller reads that namespace, **Then** the document and its quoted revision header are returned with the existing success contract.
2. **Given** an authenticated caller with read permission and no document for the requested namespace, **When** the caller reads that namespace, **Then** the existing not-found contract is returned.
3. **Given** an anonymous caller, **When** the caller reads a preference namespace, **Then** authentication is required.
4. **Given** an authenticated caller without read permission, **When** the caller reads a preference namespace, **Then** access is forbidden.

---

### User Story 2 - Write preferences with concurrency protection (Priority: P2)

An authenticated Studio user creates or updates a preference document through the existing public route while retaining the current schema validation, quota, revision, and conditional-write behavior.

**Why this priority**: Preference writes carry more error and concurrency branches than reads and therefore provide the canary's strongest proof that observable behavior is preserved.

**Independent Test**: Exercise unconditional and conditional writes, invalid content, unknown namespaces, quota failures, and stale revisions against before-and-after evidence and verify that every observable result remains approved.

**Acceptance Scenarios**:

1. **Given** an authenticated caller with write permission and a valid document, **When** the caller writes the preference, **Then** the stored document and quoted revision header are returned with the existing success contract.
2. **Given** a caller whose conditional-write headers do not match the current revision, **When** the caller writes the preference, **Then** the existing precondition-failed contract is returned and the stored document is unchanged.
3. **Given** a write that violates schema or quota rules, **When** the request is handled, **Then** the existing validation or payload-size error contract is returned.
4. **Given** an anonymous caller or an authenticated caller without write permission, **When** the caller writes a preference, **Then** the request produces the required authentication or forbidden outcome without mutation.

---

### User Story 3 - Operate the canary alongside unmigrated modules (Priority: P3)

An operator enables Studio Preferences in a host that still contains unmigrated REST modules and observes one unambiguous route owner, stable API discovery, shared authorization behavior, and no retained dynamically unloadable endpoint context.

**Why this priority**: The program cannot proceed to broader migration waves until the first production module proves coexistence and lifecycle safety in a realistic mixed host.

**Independent Test**: Compose the migrated module with representative unmigrated modules, inspect the endpoint manifest and API description, exercise both authoring models, and run the collectible-context harness after releasing all owned references.

**Acceptance Scenarios**:

1. **Given** a mixed host with Studio Preferences and representative unmigrated modules, **When** the host starts, **Then** all routes are available once with their owning module and security disposition identified.
2. **Given** exact, implied, and retained wildcard permission grants, **When** callers use both migrated and unmigrated endpoints, **Then** both paths use the shared permission semantics.
3. **Given** a released dynamically unloadable endpoint context, **When** bounded collection verification runs, **Then** the module's route and service graph do not keep the context alive.

### Edge Cases

- A malformed or missing Studio host identifier must retain its existing authentication or client-error outcome.
- An unknown preference namespace must retain its current not-found behavior for both read and write paths.
- Simultaneous writes using stale revision preconditions must not silently overwrite the winning document.
- Empty, malformed, or oversized request content must retain its current binding, validation, and payload-size behavior.
- Route registration must fail deterministically if the legacy and replacement registrations are both active.
- Repeated evidence capture must remain stable regardless of build artifacts already present in the repository tree.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST expose exactly the existing Studio Preferences read and write routes with their existing methods and route parameter names.
- **FR-002**: The system MUST preserve request binding for the namespace, Studio host identifier, conditional-write headers, schema version, and preference value.
- **FR-003**: The system MUST preserve successful response documents, status codes, content type, and quoted revision headers.
- **FR-004**: The system MUST preserve the current not-found, authentication, forbidden, validation, payload-size, and precondition-failed outcomes, including the absence of mutation on rejected writes.
- **FR-005**: The read and write routes MUST retain their existing public permission names and shared permission-evaluation semantics, including exact grants, write-implies-read, and the retained wildcard grant.
- **FR-006**: Anonymous callers MUST receive an authentication challenge and authenticated callers lacking the required permission MUST receive a forbidden response.
- **FR-007**: The module MUST own one explicit registration for each Studio Preferences route and MUST NOT retain a second legacy registration for the same route.
- **FR-008**: The migrated routes MUST coexist with representative unmigrated first-party routes in one host without changing either surface's observable contract.
- **FR-009**: The consumed API description for both routes MUST remain unchanged unless an explicit approved-difference record identifies the exact intended delta.
- **FR-010**: Before-and-after route manifests and HTTP evidence MUST report every unapproved route, method, binding, response, header, authorization, or API-description difference as a failing result.
- **FR-011**: Repeated captures of the unchanged surface MUST produce byte-stable evidence.
- **FR-012**: After owned references are released, the route and service graph MUST NOT retain a dynamically unloadable module context.
- **FR-013**: Existing Studio Preferences domain behavior, storage behavior, and module composition tests MUST continue to pass.
- **FR-014**: The work unit MUST remain limited to Studio Preferences; migration of other REST modules requires their own program work units.

### Key Entities

- **Studio Preference Document**: A user-, tenant-, host-, and namespace-scoped preference value with a schema version and revision used for conditional writes.
- **Preference Scope**: The resolved caller, tenant, Studio host, and namespace identity that selects one preference document.
- **Conditional Write**: The caller's expectation about whether a document exists or which revision may be replaced.
- **Compatibility Evidence**: The reviewed manifest, HTTP observations, API description, and approved-difference records used to prove contract preservation.
- **Route Ownership Record**: The module, route, method, and security disposition that make endpoint registration unambiguous in a mixed host.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Both Studio Preferences routes complete before-and-after comparison with zero unapproved manifest, HTTP, or consumed API-description differences.
- **SC-002**: The authorization matrix passes for anonymous, missing-permission, exact read, exact write, implied read, and retained wildcard callers on every applicable route.
- **SC-003**: Ten consecutive unchanged-surface captures produce byte-identical compatibility evidence.
- **SC-004**: A mixed host exposes each reviewed Studio Preferences and representative unmigrated route exactly once with no ambiguous owner or security disposition.
- **SC-005**: Every already-covered Studio Preferences behavior and storage scenario remains green after the migration.
- **SC-006**: The bounded collectible-context verification succeeds after route and service references are released.
- **SC-007**: The canary report records enough evidence to make a reviewable proceed, revise, or stop decision for the next migration wave.

## Assumptions

- The two existing Studio Preferences routes and their wire contracts are the authoritative public baseline.
- The existing Foundation Identity permission catalog and evaluator remain the authorization source of truth.
- Write permission intentionally implies read permission; this existing relationship is preserved rather than redesigned.
- Existing authentication and external-provider claim normalization remain outside the Studio Preferences module.
- Compatibility differences are rejected by default; any intentional public contract change requires a separate approval record and is not inferred from this migration.
- Broader REST migration, public route redesign, identity-provider changes, and dynamic collision-policy production work remain outside this canary.

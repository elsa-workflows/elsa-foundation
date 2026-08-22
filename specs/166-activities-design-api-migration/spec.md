# Feature Specification: Activities Design API Minimal API Migration

**Feature Branch**: `codex/1373-wave7-activities-design`

**Created**: 2026-08-17

**Status**: Draft

**Input**: Issue #1373 — migrate the complete 38-registration `Elsa.Activities.Design.Api` owner slice from FastEndpoints to the program's consistent Minimal API authoring model while preserving the established REST contract.

## User Scenarios & Testing

### User Story 1 - Preserve every Activities Design REST contract (Priority: P1)

As an activity-authoring client, I can continue to browse the catalog, inspect availability, create and manage reusable activity definitions and drafts, compare and manage versions, resolve dependencies, and plan or apply upgrades through the same routes and observable HTTP contracts.

**Why this priority**: Studio and headless authoring clients depend on the current wire behavior; a nominal route migration is not successful if binding, response, error, paging, filtering, concurrency, or upgrade semantics drift.

**Independent Test**: Capture immutable evidence from the pre-migration service for all 38 method/path registrations, then replay a representative success and failure corpus against the migrated service and compare HTTP and API-description output.

**Acceptance Scenarios**:

1. **Given** the frozen pre-migration evidence and the migrated owner, **when** every registration is exercised, **then** route, method, status, body, header, media type, redirect, and error behavior match unless a two-sided approved difference is recorded.
2. **Given** missing, empty, malformed, null, conflicting route/body, and unsupported-content requests, **when** they are sent to authoring and upgrade operations, **then** binding precedence and failure contracts remain unchanged.
3. **Given** catalog filters, availability settings, version/dependency cursors, draft lifecycle conflicts, and upgrade-plan outcomes, **when** clients exercise them, **then** paging, filtering, conflict, not-found, validation, and cancellation behavior remain unchanged.

---

### User Story 2 - Preserve one framework-neutral permission contract (Priority: P1)

As a host operator, I can secure all Activities Design routes through the shared permission evaluator, with activity-design and provider-payload actions remaining distinct and tenant/resource constraints applied consistently regardless of endpoint authoring style.

**Why this priority**: A migration that broadens route metadata, trusts an external identity incorrectly, or bypasses resource policy can expose authoring or provider payloads.

**Independent Test**: Exercise anonymous, trusted-denied, exact, implied, evaluator-wildcard, normalized external, untrusted external, ambiguous identity, missing/mismatched tenant, and resource-specific callers against representative migrated routes and a retained FastEndpoints canary using the same evaluator.

**Acceptance Scenarios**:

1. **Given** an anonymous or untrusted external caller, **when** a protected route is called, **then** authentication fails closed and no permission/resource decision is treated as a grant.
2. **Given** a trusted caller without the required action, **when** a protected route is called, **then** access is denied; exact, implied, and evaluator-wildcard grants succeed only when tenant/resource constraints also succeed.
3. **Given** activity-design and provider-payload routes, **when** their endpoint metadata is inspected, **then** each names only its catalog-owned action and never owns the wildcard compatibility grant.
4. **Given** a retained FastEndpoints route in the same host, **when** the same principals are used, **then** both authoring models produce the same evaluator outcome.

---

### User Story 3 - Compose, reload, and retire the owner safely (Priority: P2)

As a modular host maintainer, I can load, map, replace, and unload Activities Design without endpoint-framework coupling, leaked generations, stale API metadata, or retained provider and serializer state.

**Why this priority**: Activities Design is a large owner with rich provider, store, authorization, and API-description seams; shallow route tests cannot prove that hot-reload and owner retirement remain safe.

**Independent Test**: Run at least three complete owner generations through route mapping, real authorization, representative authoring/availability/upgrade providers and stores, request binding, response serialization, API-description generation, disposal, and weak-reference collection.

**Acceptance Scenarios**:

1. **Given** three successive owner generations, **when** each serves representative catalog, authoring, availability, dependency, lifecycle, and upgrade requests and is then replaced, **then** the old generation drains and becomes collectible after disposal.
2. **Given** the combined Workbench host, **when** the owner is composed with already-migrated and retained transitional modules, **then** every Activities Design route is mapped exactly once with stable ownership, operation, security, and request/response metadata.
3. **Given** the final migration diff, **when** transition inventory and dependency gates run, **then** exactly 38 owner registrations and only unused production FastEndpoints dependencies are removed.

### Edge Cases

- A route identifier conflicts with an identifier in the JSON body; historical route-over-body precedence must be preserved per operation.
- A request body is missing, zero-length, JSON `null`, malformed JSON, valid JSON with wrong property casing, or sent with no/incorrect content type.
- A cursor is missing, empty, malformed, signed with another key, expired, or refers to a version that changed after capture.
- A draft or version is missing, already retired/revoked/restored/discarded, concurrently updated, or rejected by validation.
- An upgrade plan is stale, refreshed, partially applicable, rejected, canceled, or fails in a provider/store boundary.
- A principal carries exact, implied, or wildcard grants but lacks or mismatches tenant/resource claims.
- An external principal is authenticated but untrusted, or trusted and untrusted identities coexist ambiguously.
- API-description or JSON metadata falls back to reflection over a collectible owner type.
- An approval entry is unused, duplicated, one-sided, stale, or approves values that do not match the frozen before and current after documents.

## Requirements

### Functional Requirements

- **FR-001**: The system MUST replace exactly the reviewed 38 Activities Design endpoint registrations with one explicit owner-local route mapping surface.
- **FR-002**: The system MUST preserve all reviewed method/path registrations and observable successful and failed HTTP behavior, including binding precedence, JSON options, status, body, ProblemDetails, headers, media types, paging, filtering, concurrency, and cancellation; any difference MUST be explicit, two-sided, consumed, and approved.
- **FR-003**: The system MUST preserve catalog, availability, reusable-authoring, draft lifecycle, version/dependency/diff, provider-payload, and upgrade-plan domain semantics, including provider/store non-invocation on denied or invalid requests.
- **FR-004**: Each protected route MUST expose only its catalog-owned permission action; wildcard and implication compatibility MUST remain evaluator behavior.
- **FR-005**: The system MUST prove anonymous and untrusted callers fail closed, trusted callers without permission are denied, exact/implied/wildcard grants behave correctly, tenant/resource constraints are enforced, and a retained FastEndpoints route shares the same evaluator.
- **FR-006**: Every route MUST publish stable operation identity, Activities Design ownership/tag/authoring metadata, public or protected disposition, typed request/response metadata, and expected authentication/authorization responses.
- **FR-007**: All owner request and response contracts used for binding, serialization, and API-description generation MUST be covered by owner-controlled serialization metadata that does not retain a collectible owner implementation.
- **FR-008**: At least three complete load/reload/unload cycles MUST execute mapped delegates, authorization, configured providers/stores/adapters, serialization, real API-description generation, disposal, and weak-reference collection without cleanup hacks or hidden global cache resets.
- **FR-009**: The migration MUST retain immutable pre-migration HTTP and API-description fixtures, a reproducible capture receipt and runner, a bite-proof differential comparer, an exact 38-route manifest, and explicit approval records for any accepted difference.
- **FR-010**: The final owner MUST no longer require FastEndpoints in production; retained FastEndpoints test references MAY remain only for the immutable oracle and same-evaluator coexistence canary until the program's final retirement wave.
- **FR-011**: The combined host MUST map every Activities Design registration exactly once and preserve its dependencies on stable Workflows Design contracts without reintroducing endpoint-framework coupling.
- **FR-012**: Completion MUST include affected authoring/upgrade backend end-to-end scenarios against a rebuilt host and fresh database, owner tests, full Architecture tests, full solution build, transition ratchet, generated maps, formatting, diff review, a migration report, and exact-main post-merge gates.
- **FR-013**: Existing tests whose subject and objective cover Activities Design behavior MUST remain or be replaced with equally specific evidence; removal requires explicit recorded approval.

### Key Entities

- **Route contract case**: one reviewed method/path/input scenario with exact request, response, status, body, headers, media type, and source provenance.
- **API-description operation**: one stable operation identity with tags, parameters, request body, responses, schemas, and security requirements consumed by the comparer.
- **Authorization matrix case**: principal identity/trust, permission relationship, tenant/resource context, expected transport status, and evaluator/provider invocation outcome.
- **Owner generation**: one mapped Activities Design endpoint set plus its route metadata, providers, stores, adapters, authorization services, serializer metadata, API-description state, disposal state, and collectible weak references.
- **Approved difference**: one exact before/after facet pair, rationale, review state, and proof that both values occur in their respective real artifacts.

## Success Criteria

### Measurable Outcomes

- **SC-001**: All 38 reviewed registrations have frozen pre-migration and real post-migration evidence, with zero unexplained HTTP or API-description differences.
- **SC-002**: The representative compatibility corpus covers every registration and includes successful behavior plus binding or domain-failure behavior for every endpoint family; comparer mutation tests prove stale or unused evidence cannot pass.
- **SC-003**: The complete permission matrix passes for representative activity-design and provider-payload routes plus a retained FastEndpoints canary, with exact authentication/denial and non-invocation outcomes.
- **SC-004**: Three consecutive real owner generations complete routing, authorization, provider/store work, serialization, API-description generation, disposal, and weak-reference collection without retaining owner state.
- **SC-005**: Exactly 38 FastEndpoints registrations are removed, the first-party transition ratchet decreases by 38, and no unrelated owner registration is removed.
- **SC-006**: A rebuilt combined host passes the affected authoring, lifecycle, dependency, and upgrade backend end-to-end scenarios against a fresh database.
- **SC-007**: Owner tests, full Architecture tests, full solution build, generated-map freshness, changed-file formatting, and diff review pass before merge; CI, HTTP performance, maps, package, and code-quality gates pass on the exact merged main commit.

## Assumptions

- Existing public HTTP/JSON route contracts are the compatibility baseline; this work does not redesign them.
- FastEndpoints remains available for unrelated transitional owners and for retained before/coexistence test oracles until the program's retirement wave.
- Foundation Identity remains the authority for principal normalization, permission implication/wildcard evaluation, and resource handling.
- Workflows Design contracts migrated in Wave 6 are stable dependencies; Activities Design does not duplicate or pull endpoint-authoring concerns across that boundary.
- The activity catalog remains the single source of truth for picker visibility, and this migration does not alter reconciliation, versioning, lifecycle, dependency, or upgrade domain policy.
- The owner is unloadability-required; a downstream runtime/API-description retention issue is treated as a release gate or isolated with a proven stable-contract boundary, not waived by omitting real generation.

## Out of Scope

- Migrating any API owner other than `Elsa.Activities.Design.Api`.
- Redesigning public routes, replacing HTTP/JSON, or changing Studio workflows.
- Reworking activity reconciliation, persistence, authoring policy, lifecycle rules, dependency algorithms, or upgrade semantics.
- Removing the shared FastEndpoints package before all approved owner migrations and the final retirement gate are complete.
- Building a new identity provider, permission evaluator, or tenant model.

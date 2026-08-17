# Feature Specification: Publishing API Minimal API Migration

**Feature Branch**: `codex/1374-wave8-publishing`

**Created**: 2026-08-17

**Status**: Draft

**Input**: Issue #1374 — migrate the complete 23-registration `Elsa.Workflows.Publishing.Api` owner slice from FastEndpoints to the program's consistent Minimal API authoring model while preserving the established REST contract.

## User Scenarios & Testing

### User Story 1 - Preserve every Publishing REST contract (Priority: P1)

As a workflow or activity publishing client, I can continue to inspect publishing capabilities, run preflight and snapshot reviews, manage publication policies and slots, publish workflow versions and activity drafts, construct publishable activities, and start or inspect test runs through the same routes and observable HTTP contracts.

**Why this priority**: Publishing is the bridge from authored definitions to executable artifacts. A transport migration is unsafe if it changes validation, idempotency, concurrency, compensation, test-run, or publication-state behavior.

**Independent Test**: Capture immutable evidence from the pre-migration service for all 23 method/path registrations, then replay successful, binding-failure, domain-failure, and cancellation cases against the migrated service and compare HTTP and API-description output.

**Acceptance Scenarios**:

1. **Given** the frozen pre-migration evidence and the migrated owner, **when** every registration is exercised, **then** route, method, status, body, header, media type, redirect, and error behavior match unless a two-sided approved difference is recorded.
2. **Given** missing, empty, malformed, null, unsupported-content, or conflicting route/body input, **when** it is sent to preflight, policy, slot, publication, construction, or test-run operations, **then** binding precedence and failure contracts remain unchanged.
3. **Given** duplicate idempotency keys, concurrent publication or policy changes, stale review snapshots, missing source artifacts, cancellations, and activation failures, **when** clients exercise the API, **then** state transitions, compensation, non-invocation, and terminal outcomes remain unchanged.

---

### User Story 2 - Preserve one framework-neutral permission contract (Priority: P1)

As a host operator, I can secure every Publishing route through the shared Foundation Identity evaluator, with read and manage actions remaining distinct and tenant, resource, and provider-payload constraints applied consistently regardless of endpoint authoring style.

**Why this priority**: Publishing creates executable artifacts and mutates live publication state. A metadata or identity-normalization regression could expose design payloads, reissue publications, or broaden a management action.

**Independent Test**: Exercise anonymous, trusted-denied, exact, implied, evaluator-wildcard, normalized external, untrusted external, ambiguous identity, missing or mismatched tenant, and resource-specific callers against representative migrated routes and a retained FastEndpoints canary using the same evaluator.

**Acceptance Scenarios**:

1. **Given** an anonymous or untrusted external caller, **when** a protected route is called, **then** authentication fails closed and no store, compiler, publisher, or test-run operation executes.
2. **Given** a trusted caller without the required action, **when** a protected route is called, **then** access is denied; exact, implied, and evaluator-wildcard grants succeed only when tenant and resource constraints also succeed.
3. **Given** read and manage routes, **when** their endpoint metadata is inspected, **then** each names only its catalog-owned action and never owns the wildcard compatibility grant.
4. **Given** a retained FastEndpoints route in the same host, **when** the same principals are used, **then** both authoring models produce the same evaluator outcome.

---

### User Story 3 - Compose, reload, and retire the owner safely (Priority: P2)

As a modular host maintainer, I can load, map, replace, and unload Publishing without endpoint-framework coupling, leaked generations, stale API metadata, or retained test-run, serializer, provider, and background state.

**Why this priority**: Publishing holds rich service graphs and long-lived test-run/background resources. Route-only tests cannot prove the owner remains safe under dynamic replacement.

**Independent Test**: Run at least three complete owner generations through route mapping, real authorization, representative publishing stores, compilers, authorizers, test-run resources, request binding, response serialization, API-description generation, disposal, and weak-reference collection.

**Acceptance Scenarios**:

1. **Given** three successive owner generations, **when** each serves representative read, preflight, publication, slot-policy, and test-run requests and is then replaced, **then** the old generation drains, cancels or disposes owned background resources, and becomes collectible.
2. **Given** the combined Workbench host, **when** Publishing is composed with migrated Workflows and Activities Design contracts plus retained transitional test infrastructure, **then** every Publishing route is mapped exactly once with stable ownership, operation, security, and request/response metadata.
3. **Given** the final migration diff, **when** transition inventory and dependency gates run, **then** exactly 23 owner registrations and only unused production FastEndpoints dependencies are removed.

### Edge Cases

- A route identifier conflicts with the same identifier in a JSON body; historical route-over-body precedence must be preserved per operation.
- A request body is missing, zero-length, JSON `null`, malformed JSON, valid JSON with alternate property casing, or sent with no or incorrect content type.
- A publication request is retried with the same idempotency key but a different fingerprint, or two requests race for the same slot, policy, source version, draft, or test run.
- A preflight or snapshot review is stale, incomplete, canceled, or references a source artifact changed after review.
- Compilation, activation, trigger indexing, conversion planning, store persistence, or compensation fails after an earlier step has succeeded.
- A publication slot is missing, already inactive, concurrently restored or unpublished, or contains a publication whose source has changed.
- A workflow or activity test run is missing, already terminal, canceled concurrently, or retains a scope/background resource while its owner unloads.
- A principal carries exact, implied, or wildcard grants but lacks or mismatches tenant/resource claims; trusted and untrusted identities may coexist ambiguously.
- API-description or JSON metadata falls back to reflection over a collectible owner type.
- An approval entry is unused, duplicated, one-sided, stale, or approves values absent from the real before or after documents.

## Requirements

### Functional Requirements

- **FR-001**: The system MUST replace exactly the reviewed 23 Publishing endpoint registrations with one explicit owner-local route mapping surface.
- **FR-002**: The system MUST preserve all reviewed method/path registrations and observable successful and failed HTTP behavior, including binding precedence, JSON options, status, body, ProblemDetails, headers, media types, redirects, concurrency, idempotency, cancellation, and compensation; any difference MUST be explicit, two-sided, consumed, and approved.
- **FR-003**: The system MUST preserve preflight, publication-review, policy, slot, workflow publication, activity publication, construction, runtime-requirement, conversion-profile, incident-strategy, and workflow/activity test-run semantics, including collaborator non-invocation on denied or invalid requests.
- **FR-004**: Each protected route MUST expose only its catalog-owned read or manage action; wildcard and implication compatibility MUST remain evaluator behavior.
- **FR-005**: The system MUST prove anonymous and untrusted callers fail closed, trusted callers without permission are denied, exact/implied/wildcard grants behave correctly, tenant/resource constraints are enforced, and a retained FastEndpoints route shares the same evaluator.
- **FR-006**: Every route MUST publish stable operation identity, Publishing ownership/tag/authoring metadata, public or protected disposition, typed request/response metadata, and expected authentication/authorization responses.
- **FR-007**: All owner request and response contracts used for binding, serialization, and API-description generation MUST live at an unload-safe stable contract boundary and be covered by owner-controlled serialization metadata without reflection fallback over a collectible implementation.
- **FR-008**: At least three complete load/reload/unload cycles MUST execute mapped delegates, authorization, configured stores/compilers/publishers/test-run resources, serialization, real API-description generation, disposal, and weak-reference collection without cleanup hacks, sleeps, or hidden global cache resets.
- **FR-009**: The migration MUST retain immutable pre-migration HTTP and API-description fixtures, a reproducible capture receipt and runner, a bite-proof differential comparer, an exact 23-route manifest, and exact approval records for any accepted difference.
- **FR-010**: The final owner MUST no longer require FastEndpoints in production; retained FastEndpoints test references MAY remain only for the immutable oracle and same-evaluator coexistence canary until the program's final retirement unit.
- **FR-011**: The combined host MUST map every Publishing registration exactly once and preserve integration with migrated Workflows Design, Activities Design, and Runtime contracts without reintroducing endpoint-framework coupling.
- **FR-012**: Completion MUST include affected publishing/preflight/test-run backend end-to-end scenarios against a rebuilt host and fresh database, owner tests, full Architecture tests, full solution build, transition ratchet, generated maps, formatting, diff review, a migration report, and exact-main post-merge gates.
- **FR-013**: Existing tests whose subject and objective cover Publishing behavior MUST remain or be replaced with equally specific evidence; removal requires explicit recorded approval.

### Key Entities

- **Route contract case**: one reviewed method/path/input scenario with exact request, response, status, body, headers, media type, and source provenance.
- **API-description operation**: one stable operation identity with tags, parameters, request body, responses, schemas, and security requirements consumed by the comparer.
- **Publication transaction**: the requested source, review or preflight evidence, idempotency and concurrency identity, slot/policy state, persistence and activation steps, compensation state, and terminal result.
- **Test-run resource**: one workflow or activity test-run identity, compatibility scope, cancellation state, retained execution resource, and disposal outcome.
- **Authorization matrix case**: principal identity/trust, permission relationship, tenant/resource context, expected transport status, and evaluator/provider invocation outcome.
- **Owner generation**: one mapped Publishing endpoint set plus its route metadata, stores, publishers, compilers, authorizers, test-run resources, serializer metadata, API-description state, disposal state, and collectible weak references.
- **Approved difference**: one exact before/after facet pair, rationale, review state, and proof that both values occur in their respective real artifacts.

## Success Criteria

### Measurable Outcomes

- **SC-001**: All 23 reviewed registrations have frozen pre-migration and real post-migration evidence, with zero unexplained HTTP or API-description differences.
- **SC-002**: The compatibility corpus covers every registration and includes successful behavior plus binding or domain-failure behavior for every endpoint family; comparer mutations prove stale, unused, duplicate, one-sided, and false-valued evidence cannot pass.
- **SC-003**: The complete permission matrix passes for representative read, manage, tenant/resource-sensitive, and provider-payload routes plus a retained FastEndpoints canary, with exact authentication/denial and collaborator non-invocation outcomes.
- **SC-004**: Three consecutive real owner generations complete routing, authorization, publication/test-run work, serialization, API-description generation, disposal, and weak-reference collection without retaining owner state.
- **SC-005**: Exactly 23 FastEndpoints registrations are removed, the first-party transition ratchet reaches zero, and no unrelated owner registration is removed.
- **SC-006**: A rebuilt combined host passes the affected publishing, preflight, slot/policy, workflow/activity publication, and test-run backend end-to-end scenarios against a fresh database.
- **SC-007**: Owner tests, full Architecture tests, full solution build, generated-map freshness, changed-file formatting, and diff review pass before merge; CI, HTTP performance, maps, package, and code-quality gates pass on the exact merged main commit.

## Assumptions

- Existing public HTTP/JSON route contracts are the compatibility baseline; this work does not redesign them.
- FastEndpoints remains available only for retained before/coexistence test oracles until final retirement issue #1376.
- Foundation Identity remains the authority for principal normalization, permission implication/wildcard evaluation, and resource handling.
- The endpoint-free Publishing engine and migrated Workflows Design, Activities Design, and Runtime contracts remain stable dependencies; the API migration does not relocate domain orchestration back into transport.
- Existing publication, policy, slot, compiler, activation, conversion, test-run, and compensation rules are domain behavior to preserve, not simplify.
- Publishing is unloadability-required; a downstream API-description or background-resource retention issue is a release gate, not a reason to omit real generation or weaken schemas.

## Out of Scope

- Migrating or redesigning any API owner other than `Elsa.Workflows.Publishing.Api`.
- Retiring the shared FastEndpoints package, endpoint bases, discovery infrastructure, or historical test oracle; issue #1376 owns final retirement after the owner reaches zero.
- Redesigning public routes, replacing HTTP/JSON, or changing Studio workflows.
- Reworking publication, compilation, activation, trigger, slot, policy, conversion, or test-run domain rules.
- Building a new identity provider, permission evaluator, tenant model, or general endpoint DSL.

> # SUPERSEDED — 2026-08-04
>
> **This work unit is closed without completion, by product decision.** OpenIddict ships its own EF Core
> and MongoDB persistence packages, which are adequate for anyone enabling OpenIddict, so Elsa will not
> maintain a Groundwork adapter for it. `src/Elsa/Foundation/Identity/OpenIddict/Groundwork/` and its
> tests have been removed.
>
> What was built and then removed: four stores (145 members), an atomic-mutation receipt wrapper, and a
> registration extension, all verified issuing tokens end to end before removal.
>
> **Two things outlived it and are retained:** a `ResultOperation` defect fix in query construction, and
> hardened test doubles that now validate `ResultOperation` — the doubles had accepted a query every real
> provider rejects while 146 tests stayed green.
>
> **Consequence for the zero-EF programme:** `Microsoft.EntityFrameworkCore*` is now permanent in this
> repository via `OpenIddict.EntityFrameworkCore`. See [ADR 0042](../../docs/adr/0042-elsa-foundation-ships-only-groundwork-persistence-implementations.md),
> whose completion criterion this contradicts and which needs a targeted amendment.

# Feature Specification: OpenIddict Groundwork Stores

**Feature Branch**: `codex/106-openiddict-groundwork-stores`

**Created**: 2026-07-20

**Status**: Draft

**Input**: User description: "Replace Elsa Foundation's EF-backed OpenIddict persistence with one Groundwork-backed first-party implementation while preserving provider-neutral core contracts, complete application/authorization/scope/token store semantics, four-provider correctness and performance evidence, and final zero-EF cleanup."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Issue, Refresh, Validate, and Revoke Tokens Reliably (Priority: P1)

As an application operator, I can issue, refresh, validate, and revoke access and refresh tokens without token loss, replay acceptance, or changed security outcomes after the persistence implementation changes.

**Why this priority**: Token lifecycle correctness is the security-critical behavior Elsa exercises today. A replacement is unusable if it permits a refresh-token replay, validates a revoked token, or loses durable token state after restart.

**Independent Test**: Compose the identity feature with durable storage, issue access and refresh tokens through the public service, validate a protected request, redeem the refresh token concurrently, revoke each token kind, restart the host, and verify the same externally observable outcomes.

**Acceptance Scenarios**:

1. **Given** a valid token issue request, **when** access and refresh tokens are issued, **then** both entries have the required identity, status, expiry, subject, payload, and reference information and survive disposal and restart.
2. **Given** two callers redeem the same refresh token concurrently, **when** both requests reach the service, **then** exactly one redemption succeeds and the other is rejected without issuing a second successor token.
3. **Given** an access or refresh token that is revoked, redeemed, expired, unknown, or malformed, **when** it is presented for validation or refresh, **then** the request fails closed and does not disclose unrelated token data.

---

### User Story 2 - Manage the Complete Authorization Registry (Priority: P1)

As an OpenID Connect/OAuth application administrator, I can create, update, find, list, revoke, prune, and delete application, authorization, scope, and token records with the documented OpenIddict behavior.

**Why this priority**: Elsa must provide a complete and dependable OpenIddict store surface, not a token-only implementation that breaks a host when it begins to use registered applications, grants, or scopes.

**Independent Test**: Run one black-box store-contract suite covering every named operation for applications, authorizations, scopes, and tokens, including uniqueness, relationship changes, pages, counts, filters, cleanup, cancellation, optimistic concurrency, and restart.

**Acceptance Scenarios**:

1. **Given** an administrator creates records with unique client identifiers, scope names, or token reference identifiers, **when** another caller attempts the same unique value, **then** the duplicate is rejected and the original record remains unchanged.
2. **Given** applications, authorizations, scopes, and tokens with overlapping subjects, clients, statuses, types, resources, redirect values, and scopes, **when** a caller uses any supported named find, filter, count, ordering, or page operation, **then** it receives the correct deterministic result without reading the full logical collection into application memory.
3. **Given** an authorization or token set that is eligible for revocation or age-based cleanup, **when** the administrator performs that operation or cancels it partway through, **then** the reported count is exact and the durable state is either the complete allowed outcome or the pre-operation outcome after recovery.

---

### User Story 3 - Choose and Operate One Durable Provider (Priority: P2)

As an app host and deployment operator, I can select any supported database provider once for the OpenIddict feature, validate or apply its storage declaration in deployment automation, and receive truthful readiness diagnostics before token-serving traffic starts.

**Why this priority**: Provider choice must remain a host decision while each feature keeps one first-party concrete implementation. Correctness cannot depend on implicit provider selection, undocumented topology, or application-side fallbacks.

**Independent Test**: Independently compose the reference host with each supported provider, use the same schema readiness path that deployment automation uses, execute the public token and registry scenarios, close and reopen storage, and compare outcomes and evidence.

**Acceptance Scenarios**:

1. **Given** a host selects any one of the four required provider offerings in a topology that satisfies the feature's guarantees, **when** it enables OpenIddict, **then** every required store resolves and passes the same public behavior suite.
2. **Given** missing, stale, conflicting, or unsupported storage declarations or provider capabilities, **when** startup or deployment validation runs, **then** it reports the actionable incompatibility before serving token traffic and never substitutes an unbounded or in-memory path.
3. **Given** a host naming policy transforms physical names, **when** OpenIddict storage is resolved, **then** the resulting names are deterministic, collision-checked, and consistently used by runtime and deployment validation.

---

### User Story 4 - Maintain One First-Party Persistence Path (Priority: P3)

As an Elsa maintainer, I can evolve OpenIddict and identity-facing contracts without maintaining EF-specific contexts, migrations, registrations, packages, test lanes, or host composition in this repository.

**Why this priority**: Removing the parallel implementation family is the maintenance benefit of the transition, but only after all behavioral, provider, schema, and performance gates prove the replacement safe.

**Independent Test**: Audit source, projects, package graphs, host composition, schema declarations, tests, and generated dependency evidence after the migration; prove that retained core contracts are concrete-provider-free and that no first-party EF OpenIddict artifacts remain.

**Acceptance Scenarios**:

1. **Given** all completion gates for this feature pass, **when** the transition is finalized, **then** EF-specific OpenIddict contexts, initializers, migrations, factories, registrations, packages, tests, and host settings are removed without retaining compatibility aliases.
2. **Given** an identity or OpenIddict-facing core contract, **when** its direct and transitive dependencies are inspected, **then** it contains no dependency on Groundwork, EF Core, or another concrete persistence provider.
3. **Given** a future change reintroduces EF Core into the repository dependency graph, **when** repository validation runs, **then** it fails and identifies the violating dependency path.

### Edge Cases

- A token write, refresh redemption, revocation, prune, or bulk revoke is interrupted before the durable decision, during it, after it but before acknowledgement, or during recovery.
- Two callers create the same client identifier, scope name, or obfuscated token reference identifier at the same time.
- A stale application, authorization, scope, or token update/delete races a newer change.
- A token is refreshed concurrently, is revoked while it is redeemed, or expires at the validation boundary.
- A cleanup/revoke predicate matches more records than its declared operation bound, receives cancellation, or is retried after an ambiguous acknowledgement.
- Redirect URI, post-logout URI, resource, permission, requirement, or scope collections are empty, duplicated, reordered, contain boundary-length values, or include a value equal to another record's value.
- An application, authorization, or token relationship is deleted, revoked, or changed while a dependent operation is in flight.
- A generic caller supplies a query or projection that is outside the explicitly supported bounded query capability.
- A provider restarts, loses its connection, reuses a failed unit of work, or lacks the transaction topology needed for a multi-record decision.
- A host naming policy maps two logical storage names to the same physical name after provider normalization.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Elsa identity and OpenIddict-facing core modules MUST retain provider-neutral contracts, models, and invariants and MUST NOT directly or transitively depend on Groundwork, EF Core, or another concrete persistence provider.
- **FR-002**: The completed repository state MUST ship exactly one first-party concrete durable implementation for every retained OpenIddict application, authorization, scope, and token contract; that implementation MUST use the repository's designated persistence family.
- **FR-003**: The implementation MUST support complete create, read, update, delete, instantiate, getter, and setter behavior for OpenIddict application, authorization, scope, and token records, including their documented scalar and collection-valued information.
- **FR-004**: The implementation MUST preserve every named OpenIddict lookup, filter, count, ordering, page, prune, and revoke operation required by the supported store contracts, with deterministic identity ordering wherever an operation uses offset paging or needs a tie-breaker.
- **FR-005**: Unique client identifiers, scope names, and obfuscated token reference identifiers MUST be enforced atomically. A duplicate create MUST leave the pre-existing record unchanged and return the contract's duplicate outcome.
- **FR-006**: Application, authorization, scope, and token updates and deletes MUST use optimistic concurrency. A stale caller MUST receive the OpenIddict concurrency outcome, MUST NOT overwrite current state, and a successful update MUST issue a new opaque concurrency value.
- **FR-007**: Token issue, refresh redemption, validation, revocation, expiry handling, and replay prevention MUST preserve the existing public security behavior. In particular, concurrent refresh redemption MUST have exactly one durable winner.
- **FR-008**: Token validation MUST fail closed for revoked, redeemed, expired, unknown, malformed, or otherwise invalid token entries and MUST NOT reveal unrelated record information.
- **FR-009**: Application-to-authorization-to-token relationships and their documented dependent revocation/deletion behavior MUST remain valid across create, update, delete, failure, retry, and restart. No operation may leave orphaned or contradictory related state.
- **FR-010**: Multi-record operations that promise one logical outcome MUST make all required changes durable together or leave no durable partial result. Retrying after failure or a lost acknowledgement MUST converge on the same authoritative outcome without duplicate grants, tokens, or cleanup effects.
- **FR-011**: Prune and bulk-revoke operations MUST evaluate their date, relationship, status, subject, and type criteria at the storage boundary; they MUST have a finite declared operation bound, return an exact affected-record count, preserve cancellation, and never enumerate a whole logical collection in application memory.
- **FR-012**: Every supported find, filter, collection-membership, count, order, page, and cleanup query MUST use a declared bounded storage route. Unsupported query shapes MUST fail before provider work; production composition MUST NOT use client-side filtering, sorting, pagination, or load-all fallback.
- **FR-013**: Generic caller-supplied query or projection delegates MUST be accepted only when they can be proven to map to an explicitly declared bounded capability; all other delegates MUST fail immediately with a stable documented capability outcome and MUST NOT trigger general-purpose query emulation or unbounded materialization.
- **FR-014**: Redirect URI, post-logout redirect URI, resource, scope, permission, requirement, and other collection-valued lookups that the supported contracts expose MUST preserve their documented matching behavior and deterministic results without treating serialized opaque content as the searchable authority.
- **FR-015**: OpenIddict storage MUST be explicitly classified as globally addressed for this release because its public store contracts do not carry a tenant argument. It MUST NOT claim ambient tenant isolation it cannot enforce; a future tenant-partitioned mode requires a separately specified, claim-validated storage boundary before it can be advertised.
- **FR-016**: Each OpenIddict record kind MUST declare the physical entity form, logical storage identity, canonical content, searchable fields, unique and multi-value relationships, physical indexes, version, and provider requirements needed by its supported workload. Logical storage identity remains stable while app-host naming policy controls deterministic physical names.
- **FR-017**: Runtime startup and deployment automation MUST consume the same complete OpenIddict storage declaration and support deterministic planning, validation, status, and authorized application. Validation and status MUST not mutate storage.
- **FR-018**: Missing, stale, invalid, colliding, or capability-incompatible storage MUST produce a blocking, actionable readiness diagnostic before token-serving traffic starts. An unsupported provider topology MUST not be advertised as satisfying an atomic contract.
- **FR-019**: Each of the four required provider offerings MUST pass the same black-box OpenIddict contract suite for every capability Elsa advertises. Provider evidence MUST use real persistent storage, independent clients where relevant, disposal/reopen, restart, cancellation, declared failure windows, and a transaction-capable topology for multi-record scenarios.
- **FR-020**: A production-shaped host MUST pass an end-to-end identity integration suite that seeds authorized access, signs in, issues and validates access/refresh tokens, rejects replay, revokes both token kinds, and verifies the result after restart through public seams rather than direct store classes.
- **FR-021**: The feature MUST submit reproducible correctness and performance evidence for token issue, token lookup/validation, refresh redemption, token revocation, expiry/prune, authorization filter/revoke, application client lookup, and scope/resource lookup to the shared physical-form benchmark program. Each workload MUST declare its dataset, payload, concurrency, warm/cold behavior, result digest, provider, and storage form before timing begins.
- **FR-022**: At the required acceptance scale, every benchmarked OpenIddict workload MUST meet the shared performance program's accepted same-provider comparison and physical-form selection policy, or remain explicitly blocked for redesign. Passing functional tests MUST NOT substitute for a performance verdict.
- **FR-023**: Existing relevant behavioral test objectives MUST remain present and passing. Tests may be moved or rewired, but removal requires the repository's recorded approval process.
- **FR-024**: EF Core may be used only as a temporary behavior and performance oracle while this work is in transition. No new EF migration, context, registration, or dependency may be added.
- **FR-025**: After the correctness, provider, readiness, and performance exit gates pass, the repository MUST remove all first-party EF OpenIddict code, contexts, migrations, factories, initializers, registrations, package references, tests, and reference-host composition. This greenfield feature MUST NOT retain an EF compatibility alias or database-data migration path.
- **FR-026**: Repository validation MUST reject a direct or transitive `Microsoft.EntityFrameworkCore*` dependency after final cleanup and report the complete violating dependency path.
- **FR-027**: Documentation for operators and extenders MUST describe the supported OpenIddict capabilities, provider selection, topology prerequisites, storage readiness workflow, naming-policy behavior, generic-query capability boundary, failure behavior, and the absence of a first-party EF OpenIddict implementation.

### Key Entities

- **Application**: A registered client and its identifiers, credentials, consent settings, redirect information, permissions, requirements, settings, properties, and display information.
- **Authorization**: A durable grant connecting a subject and optionally an application to status, type, creation information, properties, and scopes.
- **Scope**: A named authorization scope with resources, localized display/description information, and properties.
- **Token**: A durable access, refresh, or other token entry with status, subject, type, creation/expiry/redemption information, optional application/authorization relationship, payload, properties, and an obfuscated reference identifier where applicable.
- **Concurrency Value**: An opaque value proving the caller is changing the version of a record it previously observed.
- **Bounded Storage Route**: A declared finite operation that defines the allowed predicate, ordering, continuation, result shape, and physical access path before data reaches application memory.
- **Storage Declaration**: The versioned, host-composable description of logical record kinds, physical forms, names, indexes, provider prerequisites, and readiness behavior.
- **Provider Evidence Record**: Reproducible proof that one advertised provider satisfies a public behavior, restart, failure, bounded-execution, and performance obligation.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of the shared OpenIddict black-box contract scenarios pass on SQLite, SQL Server, PostgreSQL, and MongoDB, including CRUD, named queries, uniqueness, relationship behavior, concurrency, cancellation, failure recovery, disposal/reopen, and restart.
- **SC-002**: In at least 100 concurrent refresh-redemption races across each supported provider, exactly one request succeeds in every race and 0 replayed refresh tokens produce a second valid successor.
- **SC-003**: 100% of scale-bearing OpenIddict operations have inspectable evidence of bounded storage-side filtering, ordering, paging, counting, and mutation; 0 operations use full-collection application-memory fallback.
- **SC-004**: The production-shaped reference host completes 100% of the end-to-end sign-in, protected-request, issue, refresh, replay-rejection, revoke, and restart scenarios on each supported provider without changing the public caller workflow.
- **SC-005**: Every submitted OpenIddict workload has a reproducible correctness digest and a shared benchmark verdict; 0 workload is marked ready without a passing verdict or an explicitly recorded redesign/blocker decision.
- **SC-006**: Before final cleanup, each physical entity-table selection demonstrates the shared benchmark program's accepted repeatable benefit over alternative physical forms at the required workload scale; otherwise the selection is changed or remains blocked.
- **SC-007**: After final cleanup, the OpenIddict source, project, package, host, test, and resolved dependency graphs contain 0 EF Core artifacts, while all retained identity and OpenIddict core projects contain 0 concrete-provider dependencies.
- **SC-008**: A deliberate reintroduction of a direct or transitive EF Core dependency is rejected automatically and reports the full violating dependency path.

## Assumptions

- The accepted zero-EF ADR governs this feature: `elsa-foundation` ships Groundwork as its only first-party concrete durable persistence implementation family, while core contracts remain provider-neutral.
- The product is greenfield and unreleased. Historical EF database conversion, EF compatibility aliases, and a separately maintained external EF implementation repository are outside this feature.
- The current published Groundwork package family provides the physical entity forms, declared bounded query routes, schema readiness operations, naming-policy bridge, optimistic concurrency, atomic unit-of-work support, and integrations for SQLite, SQL Server, PostgreSQL, and MongoDB required by this feature; implementation will consume one binary-compatible package/tool family.
- OpenIddict entries are globally addressed in this release because the relevant public store contracts have no tenant input. This is an explicit storage classification, not an authorization decision or a claim of cross-tenant querying.
- The supported first release includes the four OpenIddict store families. It does not promise arbitrary caller-defined query execution; unsupported generic query delegates fail as defined in FR-013.
- Existing OpenIddict server, validation, scheme selection, key/lifetime configuration, and public token-service behavior remain authoritative unless a separately approved product change alters them.
- The architecture constitution is draft/provisional. Its applicable provider-neutral contract and test-preservation gates are treated as quality constraints, while ADR 0042 is the accepted repository product decision.

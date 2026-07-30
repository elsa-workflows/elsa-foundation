# Feature Specification: Groundwork ASP.NET Core Identity

**Feature Branch**: `codex/095-groundwork-aspnetcore-identity`

**Created**: 2026-07-15

**Status**: In Progress — issue #1106 cursor-recertification amendment

**Input**: User description: "Make Groundwork the only target first-party concrete persistence implementation for ASP.NET Core Identity, keep Elsa's core identity contracts provider-neutral, preserve one authoritative user/role/external-login model, pass four-provider correctness gates, and prepare the frozen EF oracle for deletion only after the shared performance gate."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Authenticate And Manage Identities Reliably (Priority: P1)

An application host enables Elsa identity and receives the same complete password, cookie, claims, role, login, token, lockout, email, phone, two-factor, and recovery-code behavior without depending on the retired persistence implementation.

**Why this priority**: Authentication and authorization are security-critical host capabilities. The replacement is useful only when framework managers and Elsa's highest user-facing seams preserve their observable behavior.

**Independent Test**: Configure one supported storage option, provision an administrator, exercise password login, failed-login lockout, claims and role expansion, external-login and token lifecycles, cookie refresh, and a protected endpoint, then close and reopen storage and repeat the relevant reads.

**Acceptance Scenarios**:

1. **Given** a configured administrator with a valid password, **When** the user signs in, **Then** a valid principal is created and the protected endpoint authorizes the expected claims and roles.
2. **Given** a user subject to lockout, **When** concurrent failed sign-ins reach the configured threshold, **Then** the failure count and lockout state converge without a lost update.
3. **Given** claims, roles, external logins, and authentication tokens for a user, **When** those relationships are added, replaced, removed, or queried, **Then** framework managers and Elsa identity ports observe the same authoritative state.
4. **Given** persisted identity state, **When** the application and storage are restarted, **Then** sign-in, lookup, authorization, token, and lockout behavior remains unchanged.

---

### User Story 2 - Isolate Tenants And Resolve Concurrency Safely (Priority: P1)

A multi-tenant host can reuse normalized usernames, email addresses where allowed, and role names in different tenants while preventing every ordinary operation from discovering or mutating another tenant's identity state.

**Why this priority**: Tenant isolation and optimistic concurrency are correctness and security boundaries, not optional enhancements.

**Independent Test**: Create equal normalized identities in two tenants, race duplicate creation and stale update/delete operations from independent clients, and verify tenant-local uniqueness, one-winner outcomes, non-disclosure, and absence of orphaned relationship records.

**Acceptance Scenarios**:

1. **Given** two tenants, **When** each creates the same normalized username and role name, **Then** both creations succeed and ordinary lookups return only the current tenant's records.
2. **Given** one tenant, **When** independent clients race to create the same normalized username, role name, external-login identity, membership, or token identity, **Then** exactly one authoritative record wins and every loser receives the documented conflict outcome.
3. **Given** two clients holding the same identity revision, **When** one commits an update or delete before the other, **Then** the stale operation is rejected and cannot overwrite or resurrect successor state.
4. **Given** a user or role with dependent relationships, **When** it is deleted or a multi-record mutation fails, **Then** the operation is atomic and no claims, logins, memberships, roles, or tokens are orphaned.
5. **Given** an ordinary session for one tenant, **When** it addresses an identifier owned by another tenant, **Then** the operation reveals no existence, count, value, or timing-classification detail beyond the ordinary not-found outcome.

---

### User Story 3 - Select Any Supported Host Database (Priority: P2)

An application host chooses one supported relational or document database and uses the same identity feature, contracts, naming policy, deployment-time schema workflow, and observable results.

**Why this priority**: The zero-EF goal depends on host choice remaining broad without multiplying first-party persistence implementations or migrations.

**Independent Test**: Run the same identity scenario catalog against SQLite, SQL Server, PostgreSQL, and a transaction-capable MongoDB topology using independent clients, close/reopen, process restart, failure injection, and native bounded-query evidence.

**Acceptance Scenarios**:

1. **Given** any one supported database, **When** the host enables identity, **Then** the complete public identity scenario catalog produces the same result digest.
2. **Given** an unsupported database topology or incomplete schema, **When** the host starts, **Then** readiness fails before any public identity write occurs and does not silently apply schema changes or fall back.
3. **Given** host-level physical naming transforms, **When** identity storage is planned and applied, **Then** runtime and deployment tooling resolve the same collision-free target names.
4. **Given** a bounded normalized lookup or relationship query, **When** it executes at representative scale, **Then** native evidence proves tenant, predicate, ordering, and limit are applied before materialization.

---

### User Story 4 - Operate And Prove Replacement Readiness (Priority: P2)

DevOps can validate or apply identity schema changes in a pipeline, administrators can seed the configured initial account safely under concurrent startup, and maintainers receive the correctness evidence needed by the shared performance and final EF-removal work units.

**Why this priority**: A technically correct store is not releasable until deployment, seeding, regression protection, and removal are repeatable and auditable.

**Independent Test**: Run offline and live schema validation/status/plan/apply, race two initializers, execute the correctness workload used by the performance handoff, compose Groundwork Identity without the EF feature, and prove architecture audits reject dual authority or EF-surface growth.

**Acceptance Scenarios**:

1. **Given** an unapplied or drifted identity schema, **When** deployment tooling validates or reports status, **Then** it returns a deterministic machine-readable result without changing the database.
2. **Given** two application instances starting concurrently with the same administrator configuration, **When** initialization runs, **Then** exactly one user and required role state exists, both instances converge successfully, and no credential is logged.
3. **Given** correctness-equivalent benchmark inputs, **When** the normalized lookup and update workload is compared with the temporary oracle, **Then** the agreed ordinary-store latency, throughput, tail, and native-plan gates pass before removal.
4. **Given** the Groundwork replacement is selected, **When** the host and repository are audited, **Then** no EF Identity feature is composed simultaneously, no new EF surface exists, and the remaining frozen oracle is explicitly owned by the shared performance and final-removal work units.

---

### User Story 5 - Traverse Large Identity Relationships Without Provider-Specific Limits (Priority: P1)

An application host receives complete, deterministically ordered Identity relationship results even
when the result spans multiple bounded storage pages, without relying on an offset shape that cannot
be represented safely by every supported provider.

**Why this priority**: Groundwork `0.0.1-preview.100` correctly rejects the existing twelve
scale-bearing Identity routes. Their offset form requires an identity comparison tail that exceeds
SQL Server's key-width limit. The replacement must preserve public Identity behavior while keeping
every physical request finite and provider-neutral.

**Independent Test**: Populate every affected relationship route beyond one page, enumerate the
framework and Elsa store results through repeated continuation pages, and prove exact ordering,
complete membership, cancellation, bounded provider requests, and identical public results on all
four supported providers.

**Acceptance Scenarios**:

1. **Given** more records than one route page can contain, **When** a framework or Elsa store
   requests the complete relationship result, **Then** it returns every expected record exactly
   once in deterministic order through a finite continuation sequence.
2. **Given** a repeated, non-advancing, malformed, or over-limit continuation, **When** traversal
   evaluates the next page, **Then** it fails closed without looping, client-evaluating, or issuing
   an unbounded provider request.
3. **Given** a normalized-name/email/role exact lookup or the ordered expired-receipt cleanup,
   **When** it executes after the amendment, **Then** its existing direct first-page or 64-record
   bounded behavior is unchanged.
4. **Given** any supported provider, **When** the amended manifest is composed and exercised at the
   acceptance dataset, **Then** the provider admits the lookup-key identity tail and native evidence
   proves the declared cursor index performs predicate, order, and limit before materialization.
5. **Given** a previous Identity provider-evidence generation, **When** the manifest or package
   family changes, **Then** that generation remains historical and cannot be relabeled as current.

### Edge Cases

- Normalized username, role name, provider key, claim type/value, token name, and tenant identifiers at empty, maximum, Unicode, case-folding, and provider identifier-limit boundaries.
- Two users with the same normalized email when unique email is disabled, and deterministic conflict behavior if a host enables unique email.
- A tenant context changing or disappearing between manager construction and store I/O.
- Duplicate external-login linkage across users or tenants, including a provider subject that is tenant-local versus globally authoritative.
- Security or concurrency stamps that are absent, malformed, stale, replayed, or changed between multi-record operations.
- Cancellation before staging, during the atomic decision, after the decision but before acknowledgement, and during reconciliation.
- User or role deletion while a concurrent relationship mutation, token redemption, lockout update, or password/security-stamp rotation is in flight.
- Recovery codes redeemed concurrently and authenticator/token values replaced while a stale caller is active.
- Schema/naming collisions introduced by host transformations or provider identifier normalization.
- MongoDB standalone topology, loss of writable primary, or transactions unavailable after admission.
- Seeder configuration that is missing, unsafe for the environment, or supplies a password that fails policy.
- A result count exactly equal to one page, one greater than a page, or spanning several pages.
- A continuation that repeats the prior token, points to a different route/scope/order, or yields a
  non-empty page without forward progress.
- Cancellation before the first page, between pages, and while provider I/O for a later page is in
  flight.
- Duplicate sort values whose total order depends on the provider identity lookup-key tail.
- SQL Server's 1,700-byte index-key boundary for every amended route.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The first-party ASP.NET Core Identity persistence implementation MUST use Groundwork while Elsa's core identity contracts remain free of Groundwork dependencies.
- **FR-002**: The feature MUST maintain exactly one authoritative representation for users, roles, and external logins; Elsa identity ports and framework managers MUST adapt to that same authority rather than persist parallel copies.
- **FR-003**: The user store MUST completely support create, update, delete, identity and normalized-name lookup, password hashes, security stamps, email and confirmation, phone and confirmation, two-factor state, lockout state, claims, roles, external logins, authentication tokens, authenticator keys, and recovery codes.
- **FR-004**: The role store MUST completely support create, update, delete, identity and normalized-name lookup, and role claims.
- **FR-005**: The feature MUST advertise only optional framework store capabilities it implements completely; general queryable stores, passkeys, and protected-personal-data markers MUST remain unregistered unless a later approved work unit supplies and proves them.
- **FR-006**: Every ordinary identity load, query, write, delete, and unit of work MUST be bound to one immutable tenant scope before provider I/O; cross-tenant operations MUST require an explicit privileged purpose and produce bounded audit outcomes.
- **FR-007**: Normalized username and role-name uniqueness MUST be tenant-local. Normalized email uniqueness MUST follow the host's configured Identity policy without relying on database collation.
- **FR-008**: User, role, external-login, user-role, claim, token, and tenant-membership identities MUST have deterministic create-only conflict behavior under independent-client races.
- **FR-009**: Mutable user and role operations MUST enforce optimistic concurrency, rotate the public concurrency token after successful mutation, and translate stale operations into the framework's documented concurrency failure.
- **FR-010**: Lockout counters, token/recovery-code transitions, relationship changes, security-stamp changes, and deletes with dependents MUST use atomic conditional transitions; partial state and orphans are forbidden.
- **FR-011**: Lost acknowledgement MUST reconcile to the committed result when it can be proved; otherwise the caller MUST receive a bounded uncertain-commit outcome and MUST NOT be told the operation failed definitively.
- **FR-012**: All scale-bearing lookups and relationship listings MUST execute through declared finite routes with deterministic ordering and limits; load-all and client-evaluated production fallbacks are forbidden.
- **FR-013**: SQLite, SQL Server, PostgreSQL, and transaction-capable MongoDB MUST pass one provider-independent public contract suite covering tenancy, concurrency, atomicity, cancellation, failure windows, dispose/reopen, and process restart.
- **FR-014**: Unsupported topology, missing route/capability, schema drift, and naming collision MUST fail readiness before a public identity write or service resolution succeeds.
- **FR-015**: Runtime and deployment tooling MUST consume the same selected identity schema definition, naming policy, resolved names, and deterministic target fingerprint.
- **FR-016**: Runtime startup MUST validate readiness without automatically applying schema changes. DevOps MUST be able to plan, validate, inspect status, and apply identity schema changes explicitly in build or deployment pipelines.
- **FR-017**: Configured administrator initialization MUST be safe under concurrent application startup, idempotent, policy-validating, environment-safe, and secret-safe in logs.
- **FR-018**: Existing cookie sign-in, token exchange, claims/role permission expansion, lockout, logout, refresh, and protected-endpoint behavior MUST remain observable through the highest existing application seams.
- **FR-019**: Every still-valid test objective from the EF Core Identity lane MUST be preserved in a provider-neutral, shared, or Groundwork contract test. Removing a test requires an exact architect-approved objective ledger entry and replacement evidence.
- **FR-020**: Correctness and result equivalence MUST pass before performance is timed. This feature MUST publish the normalized identity lookup/update workload, deterministic result digest, provider prerequisites, and native-plan evidence consumed by the shared performance work unit.
- **FR-021**: Host composition MUST select exactly one Identity persistence authority. Groundwork Identity MUST compose through the existing single-provider substrate, while the temporary EF oracle MAY remain selectable but MUST NOT be active in the same host.
- **FR-022**: The feature MUST NOT add EF Core source, migrations, packages, registrations, projects, or test dependencies. It MUST produce the behavioral-objective ledger and replacement evidence needed for the final removal work unit; actual deletion of the frozen oracle remains outside this feature.
- **FR-023**: OpenIddict persistence, migration of released EF-backed installations, a separate optional EF implementation repository, generic map/reduce, general LINQ support, and new passkey support are outside this feature.
- **FR-024**: The twelve ratified scale-bearing Identity routes MUST use cursor paging and the
  provider identity lookup-key tail; none may retain an offset/skip execution shape.
- **FR-025**: Every exhaustive framework and Elsa reader over those routes MUST follow a finite
  continuation protocol with explicit page and progress guards; client evaluation, load-all, and
  unbounded retry are forbidden.
- **FR-026**: The amendment MUST preserve the existing public result set, deterministic order,
  tenant boundary, cancellation, and advertised framework capability behavior.
- **FR-027**: Direct normalized user-name, normalized email, normalized role-name, deterministic-ID,
  and ordered 64-record expired-receipt operations MUST retain their existing bounded first-page or
  exact-load behavior.
- **FR-028**: The `IDENTITY-UNBOUNDED-QUERY` architecture guard MUST recognize only the reviewed
  bounded continuation mechanism and MUST continue to reject arbitrary `QueryAllAsync`, load-all,
  or client-evaluated production paths.
- **FR-029**: The amended manifest, schema version, composition fingerprint, resolved target
  fingerprint, and provider-native plan evidence MUST be regenerated from one exact source and
  Groundwork package-family tuple.
- **FR-030**: SQLite, SQL Server, PostgreSQL, and transaction-capable MongoDB MUST pass the complete
  retained public Identity catalog and all amended native routes before the new generation becomes
  current.
- **FR-031**: Previous Identity evidence generations MUST remain immutable historical records.
  Promotion, relabeling, copying, or hand-editing them as current is forbidden.
- **FR-032**: This amendment MUST NOT change the frozen EF oracle, execute performance timing,
  select a physical form, or claim #646/#647 completion.

### Key Entities

- **Identity User**: The authoritative account, including tenant ownership, names, normalized lookup values, credentials, stamps, contact confirmation, lockout, and multi-factor state.
- **Identity Role**: A tenant-owned named authorization grouping with optimistic concurrency and claims.
- **User Claim / Role Claim**: A typed authorization fact attached to one user or role.
- **External Login**: A unique link between an external provider subject and one authoritative user.
- **User Role Membership**: The unique association between one user and one role in the same tenant authority.
- **Authentication Token**: A unique provider/name value owned by one user, including authenticator and recovery-code state where applicable.
- **Tenant Membership**: Elsa-owned tenant projection for one authoritative user, including tenant-local roles and direct permissions without duplicating the user aggregate.
- **Identity Revision**: The opaque optimistic-concurrency evidence presented by a caller and rotated after a successful mutation.
- **Identity Storage Definition**: The selected physical units, uniqueness constraints, bounded routes, and naming evidence used by both runtime and deployment tooling.
- **Identity Continuation**: An opaque route/scope/order-bound token that advances one finite
  deterministic page without exposing provider-specific state to Identity callers.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of the required user and role capability scenarios pass through framework managers and the highest existing sign-in/authorization seams.
- **SC-002**: Across at least 100 independent-client duplicate and stale-revision races per representative identity type, every run produces exactly one allowed winner, no lost update, and no orphaned dependent record.
- **SC-003**: The complete scenario catalog yields identical public result digests on all four supported database choices across fresh start, close/reopen, and process restart.
- **SC-004**: Cross-tenant ordinary-operation tests report zero disclosed or mutated records across every load, query, write, delete, relationship, and unit-of-work path.
- **SC-005**: Native query evidence for every normalized lookup and scale-bearing relationship route shows no unbounded scan or post-materialization tenant filtering at the 100,000-record acceptance dataset.
- **SC-006**: Concurrent administrator initialization converges to one account and required role state in 100 consecutive two-instance runs and emits zero password or credential values in captured logs.
- **SC-007**: The representative normalized lookup/update workload produces reproducible correctness digests and native-plan evidence accepted by the shared performance harness, which will apply the program's p95, throughput, and p99 thresholds before final deletion.
- **SC-008**: The repository audit reports zero new Identity-owned EF Core files, migrations, package references, registrations, projects, tests, or dependency edges, and identifies every frozen oracle artifact with an approved replacement objective.
- **SC-009**: An intentional reintroduction of an Identity EF reference, duplicate authority document, load-all query, or unsupported capability registration fails an automated architecture gate with the exact offending path.
- **SC-010**: Runtime and deployment tooling produce one identical target fingerprint for each supported provider and every validation/status operation is demonstrably read-only.
- **SC-011**: For each of the twelve amended routes, datasets at zero, one, exact-page, page-plus-one,
  and multi-page cardinalities produce no missing or duplicate public records and terminate within
  the declared page bound.
- **SC-012**: Intentional repeated, malformed, cross-route, or non-advancing continuation evidence
  fails an automated test without a second unbounded traversal or provider fallback.
- **SC-013**: SQL Server composition accepts every amended index within the 1,700-byte key budget,
  and all four providers retain one exact native-plan record for every amended route.
- **SC-014**: The complete retained Identity scenario catalog yields the same public result digest
  before and after the paging amendment on all four providers.

## Assumptions

- The zero-EF provider boundary, Groundwork-only first-party implementation decision, greenfield/no-data-migration scope, three physical table forms, deployment-owned schema application, and Model B Git workflow are already ratified.
- Groundwork's released physical identity, bounded query, unit-of-work, optimistic-concurrency, scope, and four-provider capabilities are dependencies; a proven missing generic capability is implemented and released upstream before Elsa consumes it.
- Specification 094 owns the shared composition, scoped session, provider fixture, coverage ledger, and broader IAM/secrets hardening. This feature owns only the #644 authoritative ASP.NET Core Identity seam and supplies evidence back to those rows.
- Issue #646 owns the shared benchmark harness and final physical-shape verdict. This feature supplies the correctness-proven Identity workload; issue #647 consumes the accepted verdict and deletes the frozen EF oracle.
- The existing ASP.NET Core Identity manager, cookie, principal, token-provider, and application endpoint behavior is the compatibility contract. EF-specific mechanics are not.
- "Frozen EF oracle" means no new EF behavior, schema, migration, package, dependency edge, or test objective. Compile-preserving moves or refactors needed to share provider-neutral inputs/results are allowed only when the complete pre/post oracle result digest is identical and the EF feature remains separately selectable and never coenabled with Groundwork.
- Username and role uniqueness are tenant-local. Email remains non-unique by default and follows the host's existing Identity option if configured otherwise.
- External provider subjects are scoped according to the selected login tenant unless a provider-specific policy explicitly declares them global; the physical uniqueness definition must encode that policy rather than infer it from collation.
- Existing software is greenfield and unreleased, so no legacy EF or earlier Groundwork identity data migration is required.
- Issue #1106 is a ratified follow-up to closed #644. It owns production route/pager changes and
  correctness recertification; #646 remains the only owner of live EF equality, timing, and the
  performance verdict.

## Dependencies

- [Zero-EF parent PRD #629](https://github.com/elsa-workflows/elsa-foundation/issues/629)
- [ASP.NET Core Identity implementation issue #644](https://github.com/elsa-workflows/elsa-foundation/issues/644)
- [Identity cursor-recertification follow-up #1106](https://github.com/elsa-workflows/elsa-foundation/issues/1106)
- [Groundwork store-family hardening spec 094](../094-harden-groundwork-stores/spec.md)
- [Identity/OpenIddict contract inventory](../../docs/reports/identity-openiddict-groundwork-contract-inventory.md)
- [Performance evidence issue #646](https://github.com/elsa-workflows/elsa-foundation/issues/646)
- [Final zero-EF removal issue #647](https://github.com/elsa-workflows/elsa-foundation/issues/647)
- [Provider-boundary ADR 0042](../../docs/adr/0042-elsa-foundation-ships-only-groundwork-persistence-implementations.md)

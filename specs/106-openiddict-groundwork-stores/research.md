# Research: OpenIddict Groundwork Stores

## Decision 1: Replace all four OpenIddict Core stores

**Decision**: Register concrete Groundwork implementations for application, authorization, scope, and token stores through OpenIddict Core's four supported store-replacement registrations.

**Rationale**: OpenIddict 7.5's current EF registration supplies all four. The exact interface denominator is 42 application members, 32 authorization members, 28 scope members, and 43 token members. Elsa directly exercises token-manager operations, but a token-only replacement would leave the host with an incomplete registered OpenIddict Core surface.

**Alternatives considered**:

- Keep the EF Core integration for unused stores: rejected; it preserves an EF dependency and split authority.
- Register only token storage: rejected; it makes the feature's advertised Core capability incomplete.
- Replace OpenIddict managers/resolvers: rejected; store replacement is the external framework's intended extension seam and retains its validation/cache semantics.

## Decision 2: Preserve external server behavior; replace storage only

**Decision**: Retain `OpenIddictTokenService`, custom first-party grant, local validation with token-entry validation, scheme selection, issuer/key/lifetime handling, and public identity abstractions. Replace only persistence registration, EF initialization, and EF schema ownership.

**Rationale**: Current issue/refresh/validate/revoke behavior is the preserved public objective. Its token-manager operations are Create/GetId, FindByReferenceId/FindById, HasStatus, GetExpirationDate/GetSubject/GetPayload, TryRedeem, and TryRevoke.

**Alternatives considered**:

- Rewrite token issuance around provider APIs: rejected; would conflate an already-working public flow with persistence migration risk.
- Keep EF initializer beside a Groundwork store: rejected; creates two readiness authorities.

## Decision 3: Use four global physical entity tables

**Decision**: Declare applications, authorizations, scopes, and tokens as four distinct global physical entity tables with canonical JSON authoritative, stable logical identities, provider-normalized physical names, and only workload-proven projected fields/indexes.

**Rationale**: They have different uniqueness, compound, date/range, relationship, and multivalue workloads. The stores do not accept a tenant parameter, so these units are explicitly global for this release.

**Alternatives considered**:

- One shared document table: rejected for this high-value identity/security workload unless #646 proves it is the selected physical form.
- Ambient tenant filtering: rejected because token validation has no tenant argument and must not claim isolation it cannot enforce.
- Copy the current EF table schema mechanically: rejected; it lacks direct proof for several multivalue and bounded-mutation routes.

## Decision 4: Admit only named bounded queries and mutations

**Decision**: Every named find/list/count/prune/revoke operation maps to a declared finite route. Generic query/projection delegates run only when a restricted translator proves they map to an admitted bounded route; unsupported delegates fail immediately with a stable documented capability outcome.

**Rationale**: Elsa has no current caller of the generic delegate overloads. General `IQueryable` is the one fundamental mismatch with the closed Groundwork query model and cannot be emulated with materialization.

**Alternatives considered**:

- General LINQ provider: rejected; no portable finite-plan proof.
- Client-side generic fallback: rejected; violates bounded execution and may disclose more records.
- Silently omit generic methods: rejected; the implementation must satisfy the external interface and communicate its capability boundary deterministically.

## Decision 5: CAS/UoW is the only correctness mechanism for races and relationships

**Decision**: Groundwork expected-version CAS is authoritative; stale update/delete maps to the OpenIddict concurrency outcome. Refresh redeem, dependent cleanup, prune/revoke, and other multi-record logical decisions use one declared unit of work plus deterministic operation identity/recovery inspection.

**Rationale**: OpenIddict's EF stores rotate opaque concurrency tokens and map stale writes to `OpenIddictExceptions.ConcurrencyException`; refresh reuse must have exactly one winner. Relational foreign keys cannot be assumed across all providers.

**Alternatives considered**:

- Unconditional upsert: rejected; it permits stale overwrite and refresh replay.
- Provider-native cascades: rejected; not a portable public contract.
- Read-then-mutate bulk loops: rejected; not bounded or interruption-safe.

## Decision 6: Preview.76 public capability verification is a blocking prerequisite

**Decision**: Before implementation, verify an exact public `0.0.1-preview.76` package/tool family and executable probes for codec admission, physical entity definitions, schema CLI/readiness, typed compound/multivalue/range routes, bounded mutation with native mutation plans, and four-provider CAS/UoW.

**Rationale**: The audit worktree pinned preview.72 when this prerequisite was recorded; a local or historical capability claim cannot establish the public integration contract. The repository now consumes preview.76, but linked multivalue projections remain permitted only after that public family proves the declaration/execution surface.

**Alternatives considered**:

- Assume prior Groundwork work covers all requirements: rejected; package/API drift and provider behavior are load-bearing.
- Implement local provider extensions: rejected; it splits Groundwork's public portability contract and weakens evidence.

## Decision 7: EF is an oracle until, and only until, the exit gate

**Decision**: Preserve relevant behavior tests and use the current EF lane for parity/performance evidence; then remove its DbContext, initializer, factory, migrations, registrations, packages, test setup, shell settings, and architecture-ratchet entries after all gates pass.

**Rationale**: The product is greenfield, so no data migration or compatibility alias is required, but test-objective preservation remains mandatory.

**Alternatives considered**:

- Delete EF first: rejected; removes the oracle before correctness/performance evidence exists.
- Keep EF as an optional in-repository provider: rejected by ADR 0042 and repository-wide zero-EF target.

## Evidence Baseline

- OpenIddict packages are currently version 7.5.0; current OpenIddict project directly references EF Design, InMemory, Sqlite, and EntityFrameworkCore.
- Current source has one EF registration method, one DbContext, one initializer, one SQLite factory, and three migration/snapshot files.
- Current direct OpenIddict tests contain 23 Fact/Theory methods; they preserve token flow, bearer validation, scheme composition, and development/demo guard objectives but do not yet cover full stores, four providers, restart, races, prune/revoke, or generic delegate capability.
- Existing EF schema provides unique client id/scope name/reference id and common compound routes; it is a behavior reference, not the new physical design authority.

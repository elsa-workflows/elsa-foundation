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

## Decision 3: Keep four global logical units; block on collection-element storage

**Decision**: Declare applications, authorizations, scopes, and tokens as four distinct global logical record units with canonical JSON authoritative and stable logical identities. Do not scaffold their physical form until valence-works/Groundwork#128 supplies bounded collection-element projection and membership execution and #646 can evaluate executable candidates.

**Rationale**: They have different uniqueness, compound, date/range, relationship, and multivalue workloads. The stores do not accept a tenant parameter, so these units are explicitly global for this release. Preview.81 linked storage is scalar and maintains one sidecar row per canonical document. It cannot expand redirect URIs, post-logout URIs, authorization scopes, or scope resources into element rows, and its ordinary query API has neither exact array membership nor owner-contains-all semantics. `PhysicalEntityTable` also has no linked declaration. The original four-entity-table decision and the apparent shared/dedicated alternative therefore both fail required storage-bound routes.

**Alternatives considered**:

- Shared/dedicated document forms with preview.81 linked projections: rejected; the linked projection is scalar, not collection-element storage.
- Four entity units plus adapter-owned membership units: rejected; authorization minimal-scope lookup still requires storage-side grouping/contains-all. Adapter-side intersection is forbidden client evaluation.
- Groundwork#128 collection-element projections and exact membership routes: selected prerequisite. The clean prototype `e4e88c0` proves declaration vocabulary only and is not provider runtime.
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

## Decision 6: Preview.81 public capability verification is a blocking prerequisite

**Decision**: Before production store implementation, verify the exact public `0.0.1-preview.81` package/tool family and executable probes for codec admission, physical definitions, schema CLI/readiness, typed compound/multivalue/range routes, bounded mutation with native mutation plans, and four-provider CAS/UoW.

**Rationale**: Current Elsa `main` aligns all seven libraries and `groundwork.tool` on preview.81. The focused probe is checked into the existing Groundwork conformance project so it compiles against the same restored libraries and provider packages as the shared evidence infrastructure. It proves scalar shape, codec/CAS/UoW/readiness surface, and static provider declarations. It does not prove collection-element maintenance or membership execution on any physical form, and static reports are not real provider evidence. Groundwork#128 is therefore a blocking gate, not permission to invent a provider-specific fallback.

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
- The retained denominator contains 54 objectives: 23 direct OpenIddict methods, 7 guard/shell methods, 9 shared token-endpoint-host methods, 12 mixed Groundwork Identity/EF OpenIddict HTTP methods, and 3 provider-module methods. They do not yet cover full stores, four providers, restart, races, prune/revoke, or generic delegate capability.
- Existing EF schema provides unique client id/scope name/reference id and common compound routes; it is a behavior reference, not the new physical design authority.

# Elsa Foundation Ships Only Groundwork Persistence Implementations

Status: accepted (2026-07-12; ratified through the maintainer grilling and PR #630 review; the targeted constitution amendment remains separately pending consensus and compliance evidence).

Tracking: [Elsa PRD #629](https://github.com/elsa-workflows/elsa-foundation/issues/629) and [Groundwork PRD #25](https://github.com/valence-works/Groundwork/issues/25).

## Context

`elsa-foundation` currently carries two concrete persistence implementation families for several store contracts: EF Core and Groundwork. Keeping EF Core creates a migration set per feature and relational database provider, while Groundwork already provides the intended provider-neutral substrate for relational and document databases.

Core modules already express persistence through domain-owned store/repository contracts. Those contracts must remain independent of both EF Core and Groundwork; selecting one concrete implementation family in this repository must not leak the provider into domain contracts.

The zero-EF target is greenfield. No released EF-backed installation needs an export/import or in-place data migration path. A separately maintained EF implementation repository may be considered later, but it is not part of this decision or work unit.

## Decision

`elsa-foundation` will ship Groundwork as its only concrete durable persistence implementation family.

- Core modules own provider-neutral persistence contracts, persistence-facing models, and invariants. They do not reference Groundwork.
- Concrete durable implementations shipped from this repository implement those contracts using Groundwork. The rule standardizes the implementation family, not every workload on one universal document-store API; specialized Groundwork primitives remain appropriate for operational and time-ordered workloads.
- ASP.NET Core Identity and OpenIddict persistence are mandatory parts of the transition. Their Groundwork-backed stores must land before the repository can claim zero EF Core.
- EF Core implementations may remain temporarily as contract-parity and benchmark oracles during vertical store-family migrations. They are removed after all mandatory correctness, provider, and performance gates pass.
- Completion means no direct or transitive `Microsoft.EntityFrameworkCore*` dependency remains in `elsa-foundation`, its reference hosts, or its test graph. An architecture test will enforce that boundary.
- No EF-to-Groundwork production-data migration is required because the product is greenfield.

## Consequences

Feature authors maintain one provider-neutral store contract and one first-party concrete implementation rather than multiplying feature migrations across EF Core database providers. App hosts retain a provider choice through Groundwork's SQLite, SQL Server, PostgreSQL, and MongoDB providers.

Groundwork capabilities needed by Elsa become explicit upstream dependencies: physical document/entity tables, bounded query planning, provider-neutral schema evolution and CLI operations, pooled sessions, tenant enforcement, diagnostic stream storage, and executable provider conformance. Elsa consumes those capabilities through versioned Groundwork releases.

The existing EF-specific constitution text in §E2.5 becomes obsolete when implementation completes. It must be replaced through a targeted amendment, not silently reinterpreted. Framework §§2.9 and 2.20 continue to govern provider-neutral contracts and provider module boundaries; this ADR makes the narrower repository-product choice of which concrete implementation family Elsa Foundation ships.

If an EF Core compatibility repository is created later, it is independently versioned and maintained and must implement the same provider-neutral Elsa contracts. Its existence does not reintroduce EF Core into `elsa-foundation`.

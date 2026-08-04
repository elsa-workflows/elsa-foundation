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

## Amendment — 2026-08-04, decided

**Decision (maintainer): OpenIddict persistence is the host application's choice, and Elsa defaults to the
EF Core provider.** OpenIddict ships its own EF Core and MongoDB persistence packages; a host that wants
MongoDB selects `OpenIddict.MongoDb`, and one that wants something else selects that. Elsa does not
maintain a first-party Groundwork adapter for a third-party component's storage. The adapter built under
[spec 106](../../specs/106-openiddict-groundwork-stores/) is removed and that spec is superseded.

**Consequent narrowing of the completion criterion.** "Completion means no direct or transitive
`Microsoft.EntityFrameworkCore*` dependency remains" is narrowed to **first-party persistence**: Elsa
ships no EF-backed store of its own. A third-party component persisting its own data through its own
vendor package, selected by the host, is out of scope.

This is precise rather than a loophole, because OpenIddict's EF usage is fully self-contained:
`OpenIddictIdentityDbContext` derives from plain `DbContext` rather than Elsa's base, and
`Elsa.Foundation.Identity.OpenIddict.csproj` references **no Elsa EF project at all**. Every Elsa-authored
EF store still goes, including `src/Elsa/Persistence/EFCore/` itself.

**What this obliges.** The EF-surface ratchet must stop targeting zero and instead hold a small explicit
allowlist — `OpenIddict.EntityFrameworkCore` plus the `Microsoft.EntityFrameworkCore.*` packages its
DbContext requires — so that any *new* EF edge outside that list still fails the gate. Without the
allowlist the ratchet either fails forever or has to be switched off, and switching it off would lose the
protection the whole programme depends on.

## Superseded framing (retained for provenance)

### Amendment required — 2026-08-04

**The completion criterion below is no longer achievable as written.** A product decision was taken that
OpenIddict keeps its own vendor persistence packages (`OpenIddict.EntityFrameworkCore`, or
`OpenIddict.MongoDb`) rather than gaining a first-party Groundwork adapter, on the grounds that they are
adequate for anyone enabling OpenIddict. The Groundwork OpenIddict adapter built under
[spec 106](../../specs/106-openiddict-groundwork-stores/) has been removed.

With `OpenIddict.EntityFrameworkCore` referenced, **a transitive `Microsoft.EntityFrameworkCore*`
dependency is permanent**, so "Completion means no direct or transitive `Microsoft.EntityFrameworkCore*`
dependency remains in `elsa-foundation`, its reference hosts, or its test graph" cannot hold.

**The containment is better than it first appears, and it makes option 1 concrete.** OpenIddict's EF
usage is entirely self-contained: `OpenIddictIdentityDbContext` derives from plain `DbContext` rather
than Elsa's `ElsaDbContextBase`, and `Elsa.Foundation.Identity.OpenIddict.csproj` references **no Elsa EF
project at all** — only `OpenIddict.EntityFrameworkCore` and the `Microsoft.EntityFrameworkCore.*`
packages it needs. So Elsa's own EF infrastructure (`src/Elsa/Persistence/EFCore/`) remains deletable,
and what survives is exactly one third-party component persisting its own data with its own vendor
package.

Two ways to reconcile, both needing ratification rather than silent reinterpretation:

1. **Narrow the criterion** to *first-party* persistence: Elsa ships no EF-backed store of its own, and
   a third-party component persisting its own data with its own vendor package is out of scope. Given the
   containment above this is precise rather than a loophole — every Elsa-authored EF store still goes,
   including `src/Elsa/Persistence/EFCore/`. The EF-surface ratchet would keep a small explicit
   allowlist (`OpenIddict.EntityFrameworkCore` plus the EF packages its DbContext needs) instead of
   shrinking to zero.
2. **Adopt `OpenIddict.MongoDb` instead**, which preserves literal zero-EF at the cost of requiring
   MongoDB for the identity lane. Not currently referenced.

Until one is ratified, the ADR overstates what the programme will deliver. The rest of the decision —
that Elsa's own durable stores are Groundwork-only — is unaffected.

## Consequences

Feature authors maintain one provider-neutral store contract and one first-party concrete implementation rather than multiplying feature migrations across EF Core database providers. App hosts retain a provider choice through Groundwork's SQLite, SQL Server, PostgreSQL, and MongoDB providers.

Groundwork capabilities needed by Elsa become explicit upstream dependencies: physical document/entity tables, bounded query planning, provider-neutral schema evolution and CLI operations, pooled sessions, tenant enforcement, diagnostic stream storage, and executable provider conformance. Elsa consumes those capabilities through versioned Groundwork releases.

The existing EF-specific constitution text in §E2.5 becomes obsolete when implementation completes. It must be replaced through a targeted amendment, not silently reinterpreted. Framework §§2.9 and 2.20 continue to govern provider-neutral contracts and provider module boundaries; this ADR makes the narrower repository-product choice of which concrete implementation family Elsa Foundation ships.

If an EF Core compatibility repository is created later, it is independently versioned and maintained and must implement the same provider-neutral Elsa contracts. Its existence does not reintroduce EF Core into `elsa-foundation`.

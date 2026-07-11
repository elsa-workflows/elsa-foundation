# Zero-EF Persistence

Status: active.

Area: Elsa persistence-provider consolidation / Groundwork adoption.

Steward(s): Sipke plus active architects/agents.

## Purpose

Make Groundwork the only concrete persistence implementation family shipped from `elsa-foundation`, while keeping every core module's persistence contracts and invariants independent of Groundwork. Coordinate the Elsa work with the upstream Groundwork capabilities required to remove all direct and transitive EF Core dependencies from this repository.

This is the successor to the completed [Groundwork Persistence Readiness](groundwork-persistence-readiness.md) goal. The earlier goal established and validated the provider-neutral foundation; this goal completes product adoption and removal of the parallel EF Core implementation lane.

## In Scope

- Ratify the Elsa provider boundary and the narrow constitution amendment it requires.
- Track upstream Groundwork dependencies through the [Zero-EF Groundwork decision map](../decision-maps/zero-ef-groundwork.md).
- Replace scale-bearing in-memory query fallbacks with bounded, server-side Groundwork queries.
- Add Groundwork implementations for structured logs, OpenTelemetry, ASP.NET Core Identity, and OpenIddict persistence.
- Validate SQLite, SQL Server, PostgreSQL, and MongoDB against the Elsa-used contracts, including tenancy, concurrency, restart, and migration behavior.
- Compare EF Core with Groundwork physical entity tables using the agreed correctness and performance gates while EF remains available as a temporary oracle.
- Switch reference hosts to Groundwork and remove EF projects, migrations, registrations, package references, tests, and transitive dependencies.
- Add an architecture guard that prevents EF Core from returning to this repository.

## Out Of Scope

- Creating or maintaining a separate repository for optional EF Core implementations.
- Migrating data from an already-released EF-backed Elsa installation; this software is greenfield.
- Adding Groundwork dependencies to core modules or their persistence contracts.
- Reproducing general `IQueryable` or arbitrary LINQ support in Groundwork.
- Generic map/reduce without a concrete Elsa workload that proves the need.

## Active Objectives

1. Merge the decision-only ADR and targeted constitution review before implementation slices begin.
2. Resolve the unblocked vocabulary/API, session/concurrency, Identity/OpenIddict, and diagnostics-storage tickets in the decision map.
3. Keep the cross-linked Groundwork and Elsa parent PRDs synchronized; publish implementation slice issues only after their material decision-map dependencies are resolved.
4. Consume versioned Groundwork releases that satisfy the physical-storage, query, migration/CLI, session, diagnostic-stream, tenancy, and provider-conformance gates.
5. Migrate each Elsa store family vertically, using EF only as a temporary parity and performance oracle.
6. Remove EF Core only after every mandatory correctness, provider, and performance exit gate passes.

## Linked Surfaces

- [Zero-EF Persistence PRD](https://github.com/elsa-workflows/elsa-foundation/issues/629)
- [Groundwork upstream PRD](https://github.com/valence-works/Groundwork/issues/25)
- [Delivery project](https://github.com/orgs/elsa-workflows/projects/33) (private organization board)
- [Zero-EF provider-boundary ADR](../adr/0042-elsa-foundation-ships-only-groundwork-persistence-implementations.md)
- [Zero-EF Groundwork decision map](../decision-maps/zero-ef-groundwork.md)
- [Targeted §E2.5 constitution review](../reports/zero-ef-constitution-review.md)
- [Groundwork Persistence Readiness](groundwork-persistence-readiness.md)
- [Constitution Readiness](constitution-readiness.md)
- [Diagnostics Observability Readiness](diagnostics-observability-readiness.md)
- [Runtime Execution Seam](runtime-execution-seam.md)

## Current Roadmap Notes

- Use this task as a lightweight control room; assign one bounded decision-map ticket or implementation issue to each fresh worker.
- Groundwork work lands in its own repository and is consumed here through versioned releases; do not coordinate the repositories through one long-lived cross-repository branch.
- Stop adding EF migrations. Existing EF implementations remain only until the corresponding Groundwork family passes parity and benchmark gates.
- Diagnostics domain behavior remains owned by Diagnostics Observability Readiness; this bucket owns replacing its EF persistence implementation.
- Runtime hot paths remain subject to Runtime Execution Seam correctness and performance gates.

## Drift / Review Notes

- If work changes general Groundwork vocabulary, public APIs, providers, or migration mechanics, implement it upstream and link the released dependency here.
- If work changes diagnostic capture/query semantics rather than its persistence substrate, route it through Diagnostics Observability Readiness.
- If a proposed rule is durable and enforceable across Elsa work units, route only that gate through Constitution Readiness; keep plans and sequencing here.

## Removal or Completion Conditions

Complete this bucket only when `elsa-foundation` main has no direct or transitive `Microsoft.EntityFrameworkCore*` dependency, every Elsa-owned persistence contract has a Groundwork implementation where durability is required, ASP.NET Core Identity and OpenIddict use Groundwork-backed stores, all four mandatory providers pass the Elsa conformance gates, the performance policy passes, reference hosts use Groundwork, and an architecture test prevents EF Core from returning.

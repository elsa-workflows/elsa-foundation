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
- Compare EF Core with all three Groundwork physical forms using the agreed correctness and performance gates while EF remains available as a temporary oracle, and require physical entity tables to demonstrate a repeatable benefit over the shared and dedicated-document forms for workloads that select them.
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
3. Keep generic version-aware codec mechanics in Groundwork and Elsa-specific codec policies/upcasters behind Elsa provider markers; coordinate the boundary through Groundwork PR #88 and the decision map.
4. Keep the cross-linked Groundwork and Elsa parent PRDs synchronized; publish implementation slice issues only after their material decision-map dependencies are resolved.
5. Consume versioned Groundwork releases that satisfy the physical-storage, query, migration/CLI, session, diagnostic-stream, tenancy, and provider-conformance gates.
6. Migrate each Elsa store family vertically, using EF only as a temporary parity and performance oracle.
7. Remove EF Core only after every mandatory correctness, provider, and performance exit gate passes.

## Linked Surfaces

- [Zero-EF Persistence PRD](https://github.com/elsa-workflows/elsa-foundation/issues/629)
- [Groundwork upstream PRD](https://github.com/valence-works/Groundwork/issues/25)
- [Delivery project](https://github.com/orgs/elsa-workflows/projects/33) (private organization board)
- [Zero-EF provider-boundary ADR](../adr/0042-elsa-foundation-ships-only-groundwork-persistence-implementations.md)
- [Zero-EF Groundwork decision map](../decision-maps/zero-ef-groundwork.md)
- [Targeted §E2.5 constitution review](../reports/zero-ef-constitution-review.md)
- [Identity/OpenIddict Groundwork contract inventory](../reports/identity-openiddict-groundwork-contract-inventory.md)
- [Temporary EF Core surface ratchet](../../tests/Elsa/Architecture/Baselines/README.md)
- [Groundwork Persistence Readiness](groundwork-persistence-readiness.md)
- [Constitution Readiness](constitution-readiness.md)
- [Diagnostics Observability Readiness](diagnostics-observability-readiness.md)
- [Runtime Execution Seam](runtime-execution-seam.md)

## Current Roadmap Notes

- Use this task as a lightweight control room; assign one bounded decision-map ticket or implementation issue to each fresh worker.
- The Identity/OpenIddict inventory is resolved. Its upstream prerequisites are compound/typed/multi-value indexes, range and bounded bulk operations, storage-boundary tenancy, and four-provider UoW/OCC conformance; the OpenIddict generic-query boundary remains an explicit implementation capability decision rather than permission for client evaluation.
- Groundwork work lands in its own repository and is consumed here through versioned releases; do not coordinate the repositories through one long-lived cross-repository branch.
- Stop adding EF migrations. Existing EF implementations remain only until the corresponding Groundwork family passes parity and benchmark gates.
- ASP.NET Core Identity #644 has an implemented and remediated Groundwork candidate through spec 095. The provider-neutral contracts remain Groundwork-free, and the v1.1 `iam-normalized-lookup-update` contract now has accepted exact-candidate evidence against Groundwork `0.0.1-preview.60` and Identity storage manifest v1.0.4 for SQLite, SQL Server, PostgreSQL, and a MongoDB replica set. The checked-in EF artifact is a non-executed contract baseline; #646 owns real same-provider EF execution, equality, and timing. Earlier `preview.55`-`preview.59` artifacts remain historical provenance, and the accepted Groundwork correctness evidence does not complete OpenIddict replacement, host switching, or #647 EF-family deletion.
- Groundwork PR #88 owns the generic version-aware codec contract consumed by the Identity candidate. Elsa owns marker-gated per-kind version policies, legacy-stamp parsing, JSON options, and concrete upcasters in its provider packages; no such policy belongs in Groundwork or an Elsa core module.
- Diagnostics domain behavior remains owned by Diagnostics Observability Readiness; this bucket owns replacing its EF persistence implementation.
- Structured Logs multi-writer replay hardening is implemented by [spec 091](../../specs/091-structured-logs-replay-cursors/spec.md): Core remains Groundwork-neutral, while the first-party adapter consumes Groundwork preview.33 diagnostic records. The temporary EF adapter received no migration or schema expansion.
- Runtime hot paths remain subject to Runtime Execution Seam correctness and performance gates.

## Drift / Review Notes

- If work changes general Groundwork vocabulary, public APIs, providers, or migration mechanics, implement it upstream and link the released dependency here.
- If work changes diagnostic capture/query semantics rather than its persistence substrate, route it through Diagnostics Observability Readiness.
- If a proposed rule is durable and enforceable across Elsa work units, route only that gate through Constitution Readiness; keep plans and sequencing here.

## Removal or Completion Conditions

Complete this bucket only when `elsa-foundation` main has no direct or transitive `Microsoft.EntityFrameworkCore*` dependency, every Elsa-owned persistence contract has a Groundwork implementation where durability is required, ASP.NET Core Identity and OpenIddict use Groundwork-backed stores, all four mandatory providers pass the Elsa conformance gates, the performance policy passes, reference hosts use Groundwork, and an architecture test prevents EF Core from returning.

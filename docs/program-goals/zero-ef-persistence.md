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

1. Finish Spec 094 current-head provider evidence publication and consume linked diagnostics evidence without advancing incomplete rows.
2. Preserve ALL32 as the immutable historical floor, consume #646 accepted-shape verdicts for every current ALL32 + DIAGNOSTICS2 lane, and remediate every Redesign or Blocked result.
3. Complete the production-shaped four-provider host matrix, operational documentation, and generated-map refresh.
4. Complete diagnostics #642's remaining preview.103 provider-evidence, performance, and EF-removal gates; replay checkpoints #1048 and #1072 are on `main`, and stale PR #660 is closed as historical reference.
5. Complete #647 only after every correctness, provider, performance, reference-host, and transitive-dependency gate passes.

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
- Groundwork vocabulary/API #28, pooled-session #27/#34, physical-table #31, tenancy #32, schema-diff #44, query-planner #45, provider-parity #47/#48, migration-CLI #49, and bounded-bulk #51 slices are closed upstream. Groundwork #63 is the deliberately retained post-delivery public-API review, and #50 remains the physical-form performance lane.
- The Identity/OpenIddict inventory is resolved. Its upstream prerequisites are compound/typed/multi-value indexes, range and bounded bulk operations, storage-boundary tenancy, and four-provider UoW/OCC conformance; the OpenIddict generic-query boundary remains an explicit implementation capability decision rather than permission for client evaluation.
- Groundwork work lands in its own repository and is consumed here through versioned releases; do not coordinate the repositories through one long-lived cross-repository branch.
- Stop adding EF migrations. Existing EF implementations remain only until the corresponding Groundwork family passes parity and benchmark gates.
- The workflow/activity **design-persistence lane is complete and #641 is closed**. Spec 093 merged US1–US4 (PRs [#907](https://github.com/elsa-workflows/elsa-foundation/pull/907), [#919](https://github.com/elsa-workflows/elsa-foundation/pull/919), [#933](https://github.com/elsa-workflows/elsa-foundation/pull/933), [#934](https://github.com/elsa-workflows/elsa-foundation/pull/934)): Groundwork is the only design provider across all four mandatory databases, the EF design implementation family and its in-memory query fallback are deleted, and design reads/writes run as bounded server-side Groundwork queries. The EF-core surface ratchet holds zero design-persistence entries, and the design gate-5 performance criterion was replaced by the ratified 2026-07-22 absolute-budget amendment (19/19 rows pass; see [design persistence performance report](../reports/groundwork-design-persistence-performance.md)). Parent PRD #629 stays open pending the remaining lanes #642, #643, #646, and the final EF-removal audit #647.
- **SQL Server design-lane search is a known gap, and SQL Server is not currently claimable as fully supported.** [#1185](https://github.com/elsa-workflows/elsa-foundation/issues/1185): six design-conformance tests fail on `main` because the rendered query carries an index hint SQL Server cannot plan. The affected routes are the design catalog's search and substring paths, which a Studio user hits directly, so this qualifies the "all four mandatory databases" claim in the note above. Ruled 2026-08-09: a release blocker for SQL Server's supported status, but it does not gate other lanes. Elsa authors no SQL index hints — the text is rendered by the Groundwork SQL Server dialect — so the fix is expected upstream in the `Groundwork.*` family, and must not be bought by weakening `Every_scale_bearing_design_route_uses_indexed_access_with_no_full_scan`, which exists to stop a silent fall back to a full scan.
- ASP.NET Core Identity #644 is closed. Its `preview.60` artifacts and Spec 094's `preview.76`/`preview.77`/`preview.80`/`preview.81`/`preview.86`/`preview.88` attachments are immutable historical provenance. The active package family is Groundwork `0.0.1-preview.103` from exact upstream merge `b9ba0249eed0a00da9b6d37575f39383c22ae2c9`; it includes the reviewed-but-incomplete #141 and #50 checkpoints from Groundwork PRs #155/#156 and the MongoDB fixed-assignment reopen repair from PR #157. The latest retained 36-record checkpoint/fence slice is still preview.88 and remains partial; preview.102 never produced a retained generation. Preview.103 requires an exact clean-source four-provider publication and tuple-keyed mechanical import before any current evidence claim; no row status or performance verdict advances through version alignment alone. The remaining declared provider obligations and all performance verdicts still belong to #646. The checked-in EF artifact remains a non-executed contract baseline; #646 owns real same-provider EF execution, equality, timing, and physical-shape verdicts. Identity completion does not by itself complete OpenIddict, diagnostics #642, host switching, or #647 EF-family deletion.
- Groundwork PR #88 owns the generic version-aware codec contract, and the current package family is `0.0.1-preview.103`. Elsa owns marker-gated per-kind version policies, legacy-stamp parsing, JSON options, and concrete upcasters in its provider packages; no such policy belongs in Groundwork or an Elsa core module.
- Diagnostics domain behavior remains owned by Diagnostics Observability Readiness; this bucket owns replacing its EF persistence implementation.
- Structured Logs multi-writer replay hardening is implemented by [spec 091](../../specs/091-structured-logs-replay-cursors/spec.md): Core remains Groundwork-neutral, while the first-party adapter consumes Groundwork preview.33 diagnostic records. The temporary EF adapter received no migration or schema expansion.
- Runtime hot paths remain subject to Runtime Execution Seam correctness and performance gates.

## Drift / Review Notes

- If work changes general Groundwork vocabulary, public APIs, providers, or migration mechanics, implement it upstream and link the released dependency here.
- If work changes diagnostic capture/query semantics rather than its persistence substrate, route it through Diagnostics Observability Readiness.
- If a proposed rule is durable and enforceable across Elsa work units, route only that gate through Constitution Readiness; keep plans and sequencing here.

## Removal or Completion Conditions

Complete this bucket only when `elsa-foundation` main has no direct or transitive `Microsoft.EntityFrameworkCore*` dependency, every Elsa-owned persistence contract has a Groundwork implementation where durability is required, ASP.NET Core Identity and OpenIddict use Groundwork-backed stores, all four mandatory providers pass the Elsa conformance gates, the performance policy passes, reference hosts use Groundwork, and an architecture test prevents EF Core from returning.

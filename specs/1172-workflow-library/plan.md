# Implementation Plan: Workflow Library Organization

**Branch**: `agent/codex/3500-workflow-library` | **Date**: 2026-10-31 | **Spec**: [spec.md](spec.md)
**Program**: #3499 | **Implementation leaf**: #3500

## Summary and technical context

Restore organization in the normal EF-backed Workflow Design host and extend existing Studio browsing with marker/controlled tags, combined queries, private/shared views and favorites. Preserve the legacy all-results route and existing folder UI relations. Fix shared persistence/wire contracts before independent persistence, API and Studio tasks execute in parallel.

- .NET 10/C#, TypeScript/React; existing NativeEndpoints, Studio SDK, identity/auth session, distributed locking and EF Core. No new production project/package.
- Existing Workflow Design EF module and provider-derived SQLite/PostgreSQL/SQL Server/MySQL contexts; regenerate Initial migrations under current pre-GA policy.
- Existing API TestServer, SQLite durable/native-provider suites, whole Workflows frontend suite, fixture browsers, owned normal-host live journey and package consumers.
- Scale: 10,000 workflows, bounded queries and deterministic database-work budgets; timing benchmarks remain retired.
- Bounds: pages and explicit bulk targets 1–100; <=16 tag clauses; <=32 values per clause; folder depth <=16. Validate before provider work.

## Constitution check

Loaded framework v6.1.0 and Elsa v6.3.0, both Gate indexes and relevant review rules. Core contracts remain provider-neutral; EF stays in its existing module; API consumes contracts; Runtime has no organization dependency. New types are internal unless module boundaries require public surface. No universal metadata/view framework or new root domain is introduced: workflow-specific vocabulary stays in Workflow Design until another target earns extraction. Provisional authored-state sections do not ratify this work.

Risk-based tests follow ADR 0080 D1. The approved delivery includes behavioral tests without adopting a TDD mandate: pure validation cheaply, durable concurrency/query invariants at persistence/public API, and browser-only interactions at the browser. Architecture/maps/public API/catalog gates remain. No tenant/source/persistence exception is requested. Post-design check: the same boundaries hold.

## Design and file ownership

- Provider-neutral models/store port under `Essentials/src/Workflows/Design/Persistence/Core/{Models,Stores}/`; optional `IWorkflowLibraryStore` controls capability availability.
- Entities/configuration/store, create/lifecycle integration and migrations under `.../Persistence/EntityFrameworkCore/`. Separate organization rows preserve source-owned content and cascade on permanent deletion. Physical scope and identity hashes follow current EF conventions.
- A tenant hierarchy fence serializes hierarchy edits, placement and create-in-folder; transaction-time read-committed ancestry checks alone permit races.
- New SQL page projection selects latest/draft/count without legacy all-version enrichment. Legacy unbounded list remains compatible.
- API owns explicit folder/organization/tag/view/favorite routes, action permissions, server-derived personal subject and source-generated wire metadata under `.../Design/Api/`.
- Studio owns a separate organization API/dialog/filter/view surface and integrates every query dimension into URL, request identity, cursor/selection and stale-response boundaries under `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/`.
- Views store bounded schema-versioned query/columns; source-owned content can remain read-only while organization is independently authorized. Soft-deleted targets retain metadata but reject organization edits.
- Bounded moves are full-selection atomic; tag bulk writes are per-target atomic with safe retries and outcomes. Attributable facts commit with the mutation.

## Validation and delivery

Prove SQLite/API behavior and frontend contract interactions first. Run affected whole suites and compare discovery/execution counts, supported providers/migrations, architecture/maps and package consumption. Use an owned disposable normal host for create/folder/tag/filter/view/favorite/bulk journeys, record unchanged authored/version/publication identities, restart and repeat readback. Review independently for correctness, standards and spec conformance, fix verified findings and repeat on the new head. Draft PR, exact-head required CI and resulting-main qualification precede program closure.

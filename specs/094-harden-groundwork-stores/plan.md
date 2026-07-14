# Implementation Plan: Harden Groundwork Store Families

**Branch**: `codex/645-groundwork-store-hardening` | **Date**: 2026-07-14 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/094-harden-groundwork-stores/spec.md`

## Summary

Harden every already-landed Elsa Groundwork runtime, IAM, secrets, and distributed-runtime store until it can serve as the only first-party implementation family. Begin with an executable coverage ledger and a host-selected storage composition, then bind every operation to an explicit tenant/global storage scope and separately declared ordinary/privileged access policy, replace process-local and read-check-write coordination with provider-atomic compare-and-swap or unit-of-work decisions, remove unbounded client evaluation, and run one black-box contract suite against SQLite, SQL Server, PostgreSQL, and MongoDB.

The implementation reuses correct adapters and serializers. It adds provider-specific leaves only where the repository lacks them, delegates authoritative user/role/external-login documents to #644, delegates diagnostics settings to #660, and supplies representative workloads to #646 rather than creating a second benchmark harness. EF remains a temporary oracle and receives no new surface; a lane is ready for the later zero-EF deletion only after its correctness, restart, bounded-query, capability, and performance evidence is complete.

## Technical Context

**Language/Version**: C# 14 on .NET 10 (`net10.0`)

**Primary Dependencies**: Elsa provider-neutral runtime, IAM, secrets, and distributed-runtime contracts; one binary-compatible Groundwork Core/Documents/SQLite/SQL Server/PostgreSQL/MongoDB/Tool release containing storage-boundary scoping, compiled query routes, durable applied schema state, physical storage forms, and bounded provider mutations; Microsoft.Extensions.DependencyInjection and hosting lifecycle abstractions

**Storage**: Groundwork canonical documents, dedicated document tables/collections, physical entity tables/collections, linked indexes, and provider-atomic operational transitions; SQLite, SQL Server, PostgreSQL, and MongoDB are mandatory

**Testing**: xUnit, Microsoft.NET.Test.Sdk, Testcontainers for server providers, a shared public-contract conformance fixture, independent clients, disposal/reopen and process-restart tests, deterministic concurrency/failure injection, provider-native plan inspection, schema CLI validation, and architecture/dependency ratchets

**Target Platform**: Cross-platform .NET application hosts and CI/CD runners; container-capable CI for SQL Server, PostgreSQL, and MongoDB; transaction-capable MongoDB topology for multi-document atomic operations

**Project Type**: Modular .NET libraries with provider-specific implementation leaves, a reference host composition, shared conformance infrastructure, and deployment-schema manifest sources

**Performance Goals**: #646 owns measurement and verdicts. Provisional acceptance is runtime hot-path p95 no worse than 1.10x the same-provider EF oracle with throughput at least 90%; ordinary-store p95 no worse than 1.25x with throughput at least 80%; Groundwork p99 no worse than 2x, unless a reviewed workload-specific gate replaces these values

**Constraints**: No Groundwork dependency or provider behavior in core modules; no unbounded client fallback; scope enforced at the persistence boundary; no duplicate identity authority; no process-local correctness claims; no configuration-only capabilities; no new EF migrations, providers, or implementation surface; existing behavioral test objectives remain covered; provider/topology prerequisites must be truthful and fail at startup

**Scale/Scope**: Every baseline durable contract and inseparable internal state machine across runtime, IAM, secrets, and distributed runtime, including explicit diagnostic and publication-projection ownership boundaries; four providers; production-shaped combined-host restart; all required FR-030 workloads; ten dependency-ordered delivery boundaries

## Constitution Check

*GATE: Passed before Phase 0 research. Re-checked after Phase 1 design.*

The Elsa and framework constitutions remain draft quality-gate documents. Their draft status matters because Elsa §E2.5 still describes the temporary EF base-context option. [ADR 0042](../../docs/adr/0042-elsa-foundation-ships-only-groundwork-persistence-implementations.md) and the accepted [zero-EF critical review](../../docs/reports/zero-ef-constitution-review.md) ratify the target boundary; this slice follows that decision without prematurely rewriting the constitution.

| Gate | Result | Evidence / Plan Consequence |
|---|---|---|
| Framework §§2.1, 2.7, and 2.9: provider-neutral domain contracts | PASS | Core store contracts and entities remain free of Groundwork. Provider sessions, manifests, routes, and exception translation stay in `*.Persistence.Groundwork*` projects. |
| Framework §2.6: sanctioned cross-feature composition | PASS | The application composition root selects manifest sources explicitly; no concrete store depends on another store's side effects. If DI fan-in is required, it uses the named startup-contribution event plus one aggregating handler prescribed by §2.6.1. |
| Framework §2.10: CQS persistence boundary | PASS | Existing read/store and mutation seams are preserved. New atomic operational behavior is expressed as bounded commands or specialized primitives rather than `IQueryable` or provider handles. |
| Framework §2.20: provider module decomposition | PASS | Shared Elsa-to-Groundwork logic remains in domain-owned Groundwork projects; SQLite, SQL Server, PostgreSQL, and MongoDB materialization/composition are real provider leaves. |
| Framework §§2.21.1 and 2.23: test continuity and direct coverage | PASS | Existing test objectives are frozen in the ledger, migrated to shared fixtures, and may not be removed without recorded approval. Every new feature/implementation class receives registration and direct branch coverage. |
| Framework §2.5.1: scoped logic-bearing services | PASS | Store adapters, aggregators, handlers, access-context selectors, and session/unit-of-work consumers are scoped by default. Registration tests fail undocumented singleton or transient exceptions and prove that tenant/access context and mutable operation state do not cross request scopes. Static immutable provider resources may use a documented longer lifetime. |
| Framework §2.23.5: infrastructure exception boundary | PASS | Provider and Groundwork failures are translated to domain-scoped conflict, readiness, or persistence exceptions at public Elsa seams. |
| Framework §2.22: documentation and extension-point parity | PASS | Groundwork feature docs, extension-point catalogs, deployment prerequisites, the coverage ledger, and generated maps change with their implementation seams. |
| Elsa §§E2.1–E2.2: bounded-context ownership | PASS | Runtime, IAM, secrets, diagnostics, Identity, and distributed ownership remain separate. #644 is the sole authority for framework user/role/external-login documents; #660 owns diagnostics settings. |
| Elsa §E2.4 and accepted zero-EF boundary | PASS | `elsa-foundation` retains provider-neutral contracts and ships only Groundwork concrete stores after the program exit; this slice does not create the out-of-scope external EF repository. |
| Elsa §E6 naming | PASS | New role names follow the existing store/command/source/feature vocabulary; the separate vocabulary/API review remains a tracked follow-up rather than an ad-hoc rename in this slice. |
| No historical EF data migration | PASS | The product is greenfield and unreleased; Groundwork schema evolution covers only Groundwork data and future schema versions. |

**Post-design re-check**: PASS. The design artifacts make scope, ownership, query bounds, operational transitions, provider evidence, and #646 verdicts explicit; no provider type crosses a core boundary and no unsupported composition can become ready.

## Project Structure

### Documentation (this feature)

```text
specs/094-harden-groundwork-stores/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── coverage-ledger.json
├── contracts/
│   ├── coverage-ledger.md
│   ├── coverage-ledger.schema.json
│   ├── performance-handoff.md
│   ├── provider-conformance.md
│   └── storage-composition.md
└── tasks.md
```

### Source Code (repository root)

```text
src/Elsa/
├── Persistence/Core/                         # Provider-neutral persistence scope/access contract
├── Persistence/Groundwork/
│   ├── Stores/                       # Runtime adapters and operational transitions
│   ├── Querying/                     # Closed Elsa query -> bound Groundwork route translation
│   ├── Scoping/                      # Elsa scope/access -> Groundwork session mapping
│   ├── Unified/                      # Host-selected manifest composition and schema source
│   ├── Sqlite{,/Unified}/
│   ├── SqlServer{,/Unified}/         # New provider leaf
│   ├── PostgreSql{,/Unified}/
│   └── MongoDb{,/Unified}/           # New provider leaf
├── Foundation/Identity/Persistence/Groundwork/
│   └── Stores/                       # #644 adapters plus remaining Elsa IAM outcomes
├── Secrets/Persistence/Groundwork/
│   └── Stores/
└── Workflows/Runtime/Distributed/Persistence/Groundwork/
    └── Stores/

tests/Elsa/
├── Persistence/Groundwork/
│   ├── Testing/                      # Shared provider driver and black-box scenario fixtures
│   ├── Conformance/Tests/            # Provider-independent public-contract matrix
│   ├── UnifiedHost/Tests/
│   ├── Sqlite/Tests/
│   ├── SqlServer/Tests/              # New provider evidence leaf
│   ├── PostgreSql/Tests/
│   └── MongoDb/Tests/                # New provider evidence leaf
├── Foundation/Identity/Persistence/Groundwork/Tests/
├── Secrets/Tests/
├── Workflows/Runtime/Distributed/Persistence/Groundwork/Tests/
└── Architecture/                     # Core/provider, capability, EF-surface, and ledger ratchets
```

**Structure Decision**: Keep each domain's Groundwork adapter in its existing provider-suffixed implementation project. Add SQL Server and MongoDB only as provider materialization/composition leaves and centralize reusable test mechanics in `Elsa.Persistence.Groundwork.Testing`; do not centralize domain behavior or create a provider-neutral persistence umbrella. The reference host explicitly selects the desired family manifests and one provider.

## Delivery Sequencing

1. **Freeze the coverage denominator**: commit the coverage ledger, baseline commit, test-objective inventory, EF-surface ratchet, core-dependency ratchet, and explicit #644/#660/#646 ownership rows.
2. **Make composition truthful**: replace the static partial union with host-selected manifest composition, stable fingerprinting, duplicate/missing/capability diagnostics, one schema source, and CLI `validate/plan/status/apply` coverage.
3. **Build the shared provider fixture**: define one black-box scenario vocabulary and real SQLite/SQL Server/PostgreSQL/MongoDB drivers supporting independent clients, restart, cancellation, failure windows, native-plan evidence, and topology checks. Run the same observable scenarios against the temporary EF oracle wherever a baseline EF implementation exists, without adding EF surface.
4. **Bind storage scope**: classify every ledger row, change manifests from blanket global declarations, map authorized Elsa context to immutable Groundwork access sessions, and prove wrong-scope, privileged-audit, disposal, and reuse behavior.
5. **Close fencing and checkpoint atomicity**: move execution-token allocation and checkpoint admission from process-local locks/read-check-write into provider-atomic transitions; include fencing validation in the same durable decision as checkpoint state and idempotency marker publication.
6. **Harden IAM and secrets**: adapt user/role/external-login operations to #644, implement the missing Elsa IAM outcomes, and enforce create-only uniqueness plus revision-aware update/delete for IAM, membership, and secrets.
7. **Eliminate scale-bearing client evaluation**: inventory each query shape, compile it to a versioned Groundwork route with deterministic order and finite bound, add provider-native execution evidence, and make unsupported routes a startup failure.
8. **Harden runtime operational stores**: implement durable poison state and provider-atomic queue, outbox, timer, recurring-schedule, incident, liveness, hold, and publication-projection transitions with named failure-window recovery tests.
9. **Harden distributed takeover and delivery**: make placement and command claim/renew/takeover/acknowledgement bounded and provider-atomic while preserving durable execution fencing as the final checkpoint authority.
10. **Consume performance and close readiness**: submit all FR-030 workloads and correctness hashes to #646, consume its per-lane verdicts, remediate redesign outcomes, run the combined-host/provider/architecture audit, and mark only passing lanes ready for the final zero-EF deletion program.

Each boundary ends with a local commit, an independently reviewed checkpoint, and the narrowest relevant test/provider matrix. Later boundaries may begin in parallel only when they do not change the same contracts or manifests.

## Complexity Tracking

No constitutional violation requires an exception. Four provider leaves represent independently selectable deployment integrations, while one shared conformance fixture prevents behavioral duplication. Specialized operational transitions are justified by existing public queue, outbox, fencing, acknowledgement, and schedule semantics; they do not replace ordinary document storage where ordinary CAS is sufficient.

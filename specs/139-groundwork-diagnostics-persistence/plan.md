# Implementation Plan: Durable Diagnostics Persistence

**Branch**: `139-groundwork-diagnostics-persistence` | **Date**: 2026-07-13 | **Spec**: [spec.md](spec.md)

**Input**: Replace the Structured Logs and OpenTelemetry EF Core implementations with Groundwork implementations while preserving provider-neutral diagnostics contracts.

## Summary

Deliver one first-party Groundwork persistence family for Structured Logs and OpenTelemetry. High-volume immutable history uses Groundwork diagnostic-record streams; mutable resource and instrument catalogs use bounded Groundwork document storage. Elsa continues to own capture, retry, overload, shutdown, and live-delivery policy through a shared diagnostics drain component. The work proves exact behavior and bounded provider execution on SQLite, SQL Server, PostgreSQL, and MongoDB before removing every diagnostics EF Core project, registration, package, context, entity, configuration, and migration.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`)

**Primary Dependencies**: Elsa diagnostics core contracts, Groundwork diagnostic-record and document packages, Microsoft.Extensions dependency injection/options/hosting

**Storage**: Groundwork diagnostic-record streams plus ordinary documents on SQLite, SQL Server, PostgreSQL, and MongoDB

**Testing**: xUnit, shared provider-neutral conformance suites, provider integration fixtures/containers, architecture and dependency tests, deterministic performance oracle

**Target Platform**: Cross-platform ASP.NET Core hosts and pre-start deployment pipelines

**Project Type**: Modular .NET libraries with host-composed persistence adapters

**Performance Goals**: Consume the ratified issue #646/spec-094 diagnostics verdict and retained artifacts; this lane does not own a private benchmark gate

**Constraints**: Core diagnostics assemblies remain free of Groundwork; all scale-bearing operations execute provider-side; explicit scope on every operation; non-recursive, payload-free instrumentation; bounded queues and shutdown; no production EF data migration

**Scale/Scope**: Structured Logs plus OpenTelemetry resources, traces, spans, metric instruments, metric points, and log records across four providers, including schema validation/application and complete diagnostics EF removal

## Constitution Check

The constitution is still draft/provisional. This plan treats it as the current quality gate without claiming ratification.

| Gate | Result | Design consequence |
|---|---|---|
| Domain core, default behavior, and infrastructure stay distinct | PASS | Existing `.Core` contracts remain provider-neutral; Groundwork references live only in persistence projects. |
| Domain-owned vocabulary and extension seams | PASS | Elsa exposes diagnostics contracts and lifecycle policy; Groundwork supplies infrastructure primitives rather than Elsa-specific business contracts. |
| One contract, one replacement path | PASS | Each Groundwork adapter replaces the existing store contract through existing composition seams; registration tests reject ambiguous defaults. |
| Provider-independent invariants | PASS | A single highest-seam suite defines ordering, scope, restart, idempotency, retention, and failure semantics for all providers. |
| Provider decomposition | PASS | Elsa adapters depend on provider-neutral Groundwork capabilities; hosts supply provider sessions/factories and schema tooling. |
| CQS and explicit operation semantics | PASS | Reads do not silently mutate; append, catalog upsert, and retention are explicit commands with authoritative outcomes. |
| Golden-test continuity before replacement | PASS | EF and in-memory behavior are retained as temporary oracles until conformance and performance gates pass. |
| Feature and branch-covered tests | PASS | Composition, disabled/enabled branches, readiness, shutdown, and provider selection receive explicit tests. |
| Public feature/nonsealed and logic/sealed guidance | PASS | Activatable feature types remain extensible; concrete infrastructure logic is sealed unless a documented seam requires otherwise. |
| Wrapped infrastructure failures | PASS | Operational failures remain visible through domain-facing exceptions; cursor invalidity is the only unavailable-cursor path. |
| Singleton exception requires ownership and lifecycle proof | PASS WITH DOCUMENTED EXCEPTION | The shared Elsa drain is singleton per host because it owns a bounded channel and host lifecycle. Tests prove one instance, explicit start/stop, drain-before-dispose, and complete acknowledgements. |

Post-design re-check: the proposed contracts preserve the same boundaries. No constitution violation requires a waiver.

## Project Structure

### Documentation (this feature)

```text
specs/139-groundwork-diagnostics-persistence/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── diagnostics-persistence.md
└── tasks.md
```

### Source Code (repository root)

```text
src/Elsa/Diagnostics/
├── Persistence/
│   └── Elsa.Diagnostics.Persistence.csproj
├── StructuredLogs/
│   └── Persistence/Groundwork/
└── OpenTelemetry/
    ├── Endpoints/OpenTelemetry/Logs/
    └── Persistence/Groundwork/

tests/Elsa/Diagnostics/
├── Persistence/Tests/
├── StructuredLogs/Persistence/Groundwork/Tests/
├── StructuredLogs/Persistence/Conformance/Tests/
├── OpenTelemetry/Persistence/Groundwork/Tests/
└── OpenTelemetry/Persistence/Conformance/Tests/
```

The exact conformance-project split may reuse existing test projects when that keeps references acyclic; the invariant is one reusable behavior corpus exercised by every supported provider.

**Structure Decision**: Add a small Groundwork-free diagnostics-owned persistence helper project for shared capture/drain lifecycle, keep each Groundwork schema declaration and concrete adapter under its diagnostics domain, and keep core projects independent. Provider fixtures and conformance tests remain in `tests/Elsa/Diagnostics` so EF oracles can be removed without deleting the behavior authority.

## Delivery Phases

1. Extract and certify the Elsa-owned bounded drain/lifecycle component without changing store behavior.
2. Harden the existing Structured Logs Groundwork adapter against the full replay, scope, retention, restart, and failure contract.
3. Implement OpenTelemetry record streams and bounded resource/instrument catalogs, including the missing logs endpoint.
4. Add schema declaration, validation/application, readiness, composition, and four-provider conformance.
5. Consume the #646-owned performance verdict and resolve any diagnostics regressions it identifies.
6. Remove diagnostics EF implementations and prove zero EF dependencies inside the diagnostics surface.

## Complexity Tracking

No constitution violation is accepted. The extra shared persistence project is a domain-owned lifecycle component used by two diagnostics adapters, not a new storage abstraction; it prevents the current EF-namespaced drain implementation from being copied into each Groundwork adapter.

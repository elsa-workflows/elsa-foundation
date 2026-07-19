# Implementation Plan: Groundwork ASP.NET Core Identity

**Branch**: `codex/095-groundwork-aspnetcore-identity` | **Date**: 2026-07-15 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/095-groundwork-aspnetcore-identity/spec.md`

## Summary

Deliver issue #644 as one explicit ASP.NET Core Identity Groundwork provider feature backed by the existing host-selected Groundwork session and schema composition. Evolve the current Foundation Identity Groundwork manifest into the sole user/role/external-login authority, implement every advertised framework store capability over explicit physical tables and scalar link documents, and adapt Elsa IAM ports to those same documents. Enforce tenant scope before provider I/O, use Groundwork envelope versions as concurrency authority, and make dependent mutations/deletes atomic through CAS-protected owner child registries because Groundwork units of work do not query.

The unit lands red-first contract tests, a shared four-provider correctness/restart implementation, concurrent seeding, bounded native plans, and a correctness workload contract for #646. T083-T085 retain accepted preview.60 exact-candidate provider evidence for all four supported topologies. The unit removes the current activation-order-dependent dual authority but retains the behaviorally frozen EF implementation as a selectable, never-coenabled input for #646. "Frozen" forbids new EF behavior, schema, migrations, packages, dependency edges, and test objectives. The checked-in EF contract baseline is deliberately non-executed; only #646 may produce live EF/Groundwork equality evidence. Issue #647 deletes the EF implementation only after #646 accepts the performance verdict.

## Technical Context

**Language/Version**: C# on .NET 10 (`net10.0`; SDK-default C# language version)

**Primary Dependencies**: ASP.NET Core Identity 10.0.8 shared framework; Groundwork Core/Documents/SQLite/SQL Server/PostgreSQL/MongoDB `0.0.1-preview.60`; CShells lifecycle/features; Microsoft.Extensions.DependencyInjection

**Storage**: Host-selected Groundwork physical entity tables on SQLite, SQL Server, PostgreSQL, or transaction-capable MongoDB; deployment-owned schema lifecycle through Groundwork Tool

**Testing**: xUnit, shared Groundwork provider drivers, Testcontainers, architecture/coverage ratchets, deterministic fault injection, a non-executed EF contract baseline, and a frozen EF source-tree ratchet

**Target Platform**: Cross-platform ASP.NET Core server hosts; Linux CI containers; local SQLite development

**Project Type**: Modular .NET library/provider feature plus reference-host integration and CLI/schema contracts

**Performance Goals**: This unit supplies the `iam-normalized-lookup-update` contract and exact-head Groundwork inputs/digests/native plans to #646 after the preview.60 provider evidence gate passes. #646 owns live EF execution, equality, and timing. The downstream ordinary-store gate is p95 <= 1.25x EF, throughput >= 80% of EF, and p99 <= 2x EF unless a reviewed workload-specific gate supersedes it.

**Constraints**: Provider-neutral Identity abstractions remain Groundwork-free; revision-aware IAM evolution is additive and compatibility-preserving; exactly one active identity authority; no general `IQueryable`, passkey, or protected-personal-data capability claim; no client evaluation/load-all; runtime startup never applies schema; EF behavior/schema/dependencies are frozen and EF is never coenabled with Groundwork; OpenIddict remains out of scope

**Scale/Scope**: Full required ASP.NET Core Identity user/role capability set; twelve explicit physical units at Identity manifest version `1.0.4`—seven entity tables plus dedicated document tables for primary-ID-only user tokens, tenant memberships, and name/email reservations; exactly 10 live application routes plus one bounded expiry-maintenance route; 100 independent-client race iterations per representative transition; 100,000-record bounded-query acceptance; four real providers; close/reopen and process restart

## Constitution Check

*GATE: Passed before Phase 0 research and rechecked after Phase 1 design. Both constitutions are draft documents, so only their ratified/provider-boundary rules and non-conflicting quality gates are treated as authoritative; no provisional rule changes are introduced here.*

| Gate | Result | Plan evidence |
|---|---|---|
| Framework §§2.1, 2.7, 2.20: dependency envelope and provider leaf | PASS | `Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork` isolates Groundwork and ASP.NET Core store adapters; Identity Abstractions stays provider-neutral. Existing Groundwork authority mechanics remain in the Foundation Identity Groundwork provider family. |
| Framework §2.5 / §2.23.3: feature classes and overridable registration | PASS | The new feature is `public` and non-sealed with virtual registration. Logic-bearing stores/seeders are `public sealed`. No new sealed feature class is introduced. |
| Framework §§2.6.2, 2.11: single-implementation conflicts fail clearly | PASS | Composition tests reject EF plus Groundwork authority and duplicate framework/Elsa store registrations before public store resolution. |
| Framework §2.9 and Elsa ADR 0042: provider-independent invariants | PASS | Tenant scope, uniqueness, revision, atomicity, and conflict outcomes are defined in spec/contracts; Groundwork is only their concrete enforcement mechanism. |
| Framework §2.10: CQS persistence boundary | PASS | Internal authority operations separate reads from conditional writes. ASP.NET Core's externally owned store interfaces are adapted as required and do not redefine Elsa core contracts. |
| Framework §§2.21.1, 2.23.4: test-objective continuity | PASS | Existing EF tests remain while the oracle is retained. Every valid objective is moved into shared/Groundwork contracts before #647; obsolete upsert semantics require an exact architect-approved removal row before deletion. |
| Framework §§2.23.1–2.23.2: registration and branch coverage | PASS | Tasks require direct registration tests plus branch-complete unit tests for stores, mappers, atomic coordinator, seeder, tenant binding, and failure translation. |
| Framework §2.23.5: infrastructure exception boundary | PASS | Groundwork/provider/serialization exceptions are translated to Identity failures or Foundation Identity-scoped exceptions; cancellation is preserved. |
| Framework §§2.22–2.22.2: documentation/catalog parity | PASS | The Foundation Identity Groundwork extension-point catalog, feature README, root index/maps, CLI operations, capabilities, and lifecycle tasks update with the feature. |
| Elsa §E1: golden refactor rule | PASS | Subjects and objectives are preserved; setup/fixtures may move. No EF test is deleted in this #644 unit. |
| Elsa §E2.3 / §E2.4: Foundation and Primitives boundaries | PASS | No shared primitives change. Foundation Identity remains the owner; generic provider capability gaps would land upstream in Groundwork. |
| Elsa §E2.5 and zero-EF ADR | PASS | No new EF surface or migration is added. The frozen EF implementation remains available only to #646 and cannot coactivate with the Groundwork feature; the checked-in contract baseline does not execute EF. |
| Elsa §E6 naming | PASS | New Elsa-owned types stay within the component budget and use `...Store`, `...Source`, `...Coordinator`, or provider prefixes consistently. External Identity names remain exempt. |
| Groundwork-neutral core boundary | PASS | `Elsa.Foundation.Identity.Abstractions` gains no Groundwork/project/package reference. The optional provider-neutral tenant binding uses `Elsa.Persistence.Core`, not Groundwork. |
| Greenfield/data migration boundary | PASS | Golden fixtures and manifests may be replaced without data conversion; no compatibility migration or runtime auto-upgrade is created. |

### Post-Design Recheck

The data model uses catalogued Adapter/Bridge, provider-module, physical-document, replacement-contract, and CQS patterns only. Separate link documents are required by query shapes, while owner registries make transaction-time dependent enumeration possible without adding a new Groundwork pattern or transactional-query API. The new project is well above the minimum-project guidance and has an independent dependency envelope. No constitution violation requires Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/095-groundwork-aspnetcore-identity/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── checklists/
│   └── requirements.md
└── contracts/
    ├── identity-store-contract.md
    ├── identity-storage-manifest.md
    └── test-objective-ledger.md
```

### Source Code (repository root)

```text
src/Elsa/Foundation/Identity/
├── Abstractions/                         # unchanged provider-neutral Elsa contracts
├── AspNetCoreIdentity/                   # provider-neutral Identity integration
│   ├── Models/
│   ├── Seeding/                          # provider-neutral seed options/coordinator
│   ├── Services/                         # tenant-bound sign-in and claims projection
│   ├── EntityFrameworkCore/              # frozen temporary oracle; no new surface
│   └── Groundwork/                       # new provider feature / framework stores
│       ├── DependencyInjection/
│       ├── Seeding/
│       ├── Stores/
│       └── Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.csproj
└── Persistence/Groundwork/               # sole Groundwork authority + Elsa adapters
    ├── Documents/
    ├── Stores/
    ├── IdentityStorageManifest.cs
    └── IdentityGroundworkStorageManifestSource.cs

src/Elsa/Persistence/Groundwork/{Sqlite,SqlServer,PostgreSql,MongoDb}/Unified/
└── DependencyInjection/                  # substrate only; no unconditional Identity replacement

tests/Elsa/Foundation/Identity/
├── Tests/AspNetCoreIdentity/             # preserved highest-seam/shared behavior
└── AspNetCoreIdentity/Groundwork/Tests/
    ├── Fixtures/
    ├── AspNetCoreIdentity*ContractTests.cs
    └── Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.csproj

tests/Elsa/Persistence/Groundwork/Conformance/Tests/
├── GroundworkProviderDriver*.cs          # generalized identity physical/restart seam
├── AspNetCoreIdentityProviderContractTests.cs
└── AspNetCoreIdentityNativePlanTests.cs

tests/Elsa/Architecture/
├── GroundworkPersistenceCoverageTests.cs
├── GroundworkBehavioralBaselineTests.cs
├── GroundworkPersistenceLifetimeTests.cs
└── EfCoreSurfaceRatchetTests.cs
```

**Structure Decision**: Keep the full authority document model and Elsa IAM adapters in the existing `Elsa.Foundation.Identity.Persistence.Groundwork` provider family, because that package already owns Foundation Identity Groundwork storage and can remain independent of ASP.NET Core. Add `Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork` as the consumption-shape/provider leaf that implements external framework interfaces over the same authority. This avoids forcing ASP.NET Core dependencies on non-framework IAM consumers, avoids a second authority, and keeps Groundwork out of Abstractions.

## Complexity Tracking

No constitution violations or unratified patterns are required.

## Delivery Boundaries

1. **Executable denominator and red contracts**: freeze exact EF/Groundwork test objectives, add the provider project/test shell, and prove missing capability, dual authority, tenant leak, stale revision, and atomicity failures.
2. **Authority manifest and scalar link model**: replace legacy duplicate user/role/external shapes with physical entity definitions, bounded routes, deterministic link identities, and CAS owner registries.
3. **Framework user/role stores**: implement the complete advertised Identity capability set, error mapping, tenant-scoped lookup, concurrency tokens, and relationship atomicity.
4. **Elsa adapters and composition**: adapt Elsa IAM ports to the same documents, remove unconditional unified-provider replacement, register one explicit Groundwork feature, and fail dual-provider composition.
5. **Seeder and highest seam**: split schema application from seeding, make concurrent startup idempotent and secret-safe, then pass login/cookie/claims/lockout/protected-endpoint scenarios.
6. **Four-provider and handoff evidence**: pass SQLite, SQL Server, PostgreSQL, and MongoDB replica-set correctness/reopen/restart/native-plan matrices, update spec 094 authority rows without claiming incomplete gates, and publish the #646 correctness workload.
7. **Review and landing**: run independent requirement/test-objective review, full solution/build/pack/architecture checks, Model B draft PR, required CI, merge, and verify `main`. The EF oracle stays frozen for #646/#647.

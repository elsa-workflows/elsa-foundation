# Implementation Plan: Zero-EF Final Removal

**Branch**: `779-zero-ef-final-removal` | **Date**: 2026-07-26 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/141-zero-ef-final-removal/spec.md`

## Summary

Complete issue #647 as the final dependency-ordered zero-EF lane after #642, #643, #646, and #932 pass. First freeze the exact removal and test-retention ledgers. Then prove each reference host composes one Groundwork provider across all enabled durable lanes; delete diagnostics, OpenIddict, Identity-oracle, and shared EF families in that order; remove every package/configuration/transitive edge; and replace the temporary shrink-only baseline with a fail-closed absolute-zero scanner over every restored repository project. Finish by refreshing governance/docs/maps, running the complete verification matrix, passing three exact-range adversarial reviews, merging through Model B, and closing #647/#629 only after remote `main` contains the result.

## Technical Context

**Language/Version**: .NET 10 (`net10.0`) and the repository-pinned current C# toolchain

**Primary Dependencies**: Groundwork versioned preview family containing the final #50/#141/#143 capabilities; OpenIddict 7.5; ASP.NET Core Identity; Nuplane/CShells composition; Microsoft.Extensions abstractions. `Microsoft.EntityFrameworkCore*`, `Microsoft.AspNetCore.Identity.EntityFrameworkCore`, and `OpenIddict.EntityFrameworkCore` are removal targets, not retained dependencies.

**Storage**: Groundwork over SQLite, SQL Server, PostgreSQL, and a transaction-capable MongoDB topology; no production EF data migration because the product is greenfield

**Testing**: xUnit; repository-wide architecture/dependency scanning; feature registration and behavioral tests; Groundwork four-provider conformance; dashboard-enabled host matrix; build, pack, startup, schema-validation, and benchmark-evidence gates

**Target Platform**: Cross-platform .NET server, libraries, tests, tools, and maintained reference-host configurations

**Project Type**: Multi-project modular application foundation with provider-specific persistence leaves and a reference server

**Performance Goals**: Every current coverage-ledger row has an accepted #646 verdict; runtime hot paths, ordinary stores, diagnostics, and physical-form selection meet their ratified workload-specific gates before their EF oracle is removed

**Constraints**:

- Core contracts remain Groundwork-free and provider-neutral.
- No arbitrary `IQueryable`, client evaluation, fallback scan, in-memory durable substitute, or silent feature omission.
- EF deletion follows the prerequisite DAG and cannot destroy a still-required oracle.
- Existing test subjects/objectives survive; deletion requires recorded architect approval.
- The permanent guard scans every project and refuses incomplete restore evidence.
- Only one lane at a time edits `Directory.Packages.props`, `Elsa.Server.slnx`, `shells*.json`, or `coverage-ledger.json`.
- Shared files are changed only in the final serialized integration slice.
- No compatibility repository or production EF data migration is created.

**Scale/Scope**: Current intake spans 8 EF projects, 24 direct package references, 9 central versions, 19 direct EF project references, 22 static transitive project consumers, 57 static transitive package consumers, 103 restored EF package consumers, 11 migration files, 11 `DbContext` files, 41 registration occurrences, and 3 host-configuration occurrences. The guard covers every source, test, benchmark, tool, and app project in the repository, including projects omitted from `Elsa.Server.slnx`.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

The framework and Elsa constitutions are draft quality gates; their draft status matters because §E2.5 still describes the temporary EF infrastructure being removed. The accepted ADR and zero-EF program goal govern the product boundary, while the final work unit must submit the targeted constitution update instead of silently reinterpreting §E2.5.

| Gate | Pre-design result | Post-design result | Evidence / plan response |
|---|---|---|---|
| Framework §2.1 / §2.9 provider-neutral Core | PASS | PASS | No Core contract gains Groundwork or EF references; only implementation leaves and host composition change. |
| Framework §2.20 provider decomposition | PASS | PASS | Groundwork provider leaves remain isolated; reference hosts select one coherent provider family. |
| Framework §2.21.1 and Elsa §E1 golden rule | PASS WITH LEDGER | PASS WITH LEDGER | `test-retention-ledger.md` inventories direct-token and shared-host test reachability before deletion; every row requires preservation or architect approval. |
| Framework §2.23 unit-test obligations | PASS | PASS | Permanent guard, composition changes, and logic-bearing replacements receive focused behavioral/registration coverage; existing objectives are rehosted. |
| Framework §2.22 documentation | PASS | PASS | Feature docs, extension-point catalogs, operational schema workflow, program goal, decision map, and generated maps update in the same work unit. |
| Framework §2.24 sanctioned patterns | PASS | PASS | Existing adapter/provider-module patterns remain; no new persistence abstraction or ad-hoc fallback is introduced. |
| Elsa §E2.2 deployment shapes | PASS | PASS | Design-only, runtime-only, and combined shapes remain; this lane changes persistence implementations, not Design↔Runtime ownership. |
| Elsa §E2.5 temporary EF text | TARGETED AMENDMENT REQUIRED | PLANNED | Replace obsolete EF-specific guidance with provider-neutral invariant guidance and link ADR 0042; constitution ratification remains a distinct recorded gate. |
| User-approved zero-EF completion policy | PASS | PASS | OpenIddict and #932 are mandatory; all direct/transitive EF surfaces and temporary ratchets reach zero/removal. |

No constitutional violation is justified. The targeted §E2.5 amendment is a required output, not an exception.

## Project Structure

### Documentation (this feature)

```text
specs/141-zero-ef-final-removal/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── test-retention-ledger.md
├── ef-removal-inventory.md
├── checklists/
│   └── requirements.md
├── contracts/
│   ├── completion-evidence.md
│   ├── reference-host-matrix.md
│   └── zero-ef-certification.md
└── tasks.md
```

### Source Code (repository root)

```text
src/Apps/Elsa.Server/
├── Elsa.Server.csproj
├── appsettings.json
├── shells.json
└── shells.Production.json

src/Elsa/Diagnostics/
├── OpenTelemetry/Persistence/{EFCore,Groundwork}/
└── StructuredLogs/Persistence/{EFCore,Groundwork}/

src/Elsa/Foundation/Identity/
├── AspNetCoreIdentity/{EntityFrameworkCore,Groundwork}/
└── OpenIddict/

src/Elsa/Persistence/
├── EFCore/
└── Groundwork/

src/Elsa/Workflows/Dashboard/Persistence/Groundwork/

benchmarks/Elsa.Groundwork.StorePerformance.Benchmarks/

tests/Elsa/
├── Architecture/
│   ├── Baselines/
│   ├── EfCoreSurfaceScanner.cs
│   ├── EfCoreSurfaceRatchetTests.cs
│   └── FrozenAspNetCoreIdentityEfOracleRatchetTests.cs
├── Diagnostics/
├── Foundation/Identity/
├── Modularity/
├── Persistence/
└── Groundwork/

Directory.Packages.props
Elsa.Server.slnx
docker/compose/elsa-server.shells.json
docs/{adr,decision-maps,maps,program-goals,reports}/
EXTENSION_POINTS.md
```

**Structure Decision**: This is a deletion/integration work unit across existing provider leaves, tests, host composition, governance, and architecture guards. It creates no new production project. New durable artifacts are limited to the owning spec and the permanent architecture guard/tests retained in `tests/Elsa/Architecture`.

## Phase 0: Research Decisions

Research is consolidated in [research.md](research.md). The load-bearing decisions are:

1. Treat #642, #643, #646, and #932 as hard prerequisites and retain each EF oracle until its last evidence consumer passes.
2. Use the current ratchet as intake inventory only; the end-state guard has no baseline, exception list, or update switch.
3. Inventory tests by direct reference and transitive/shared-host reachability, then use the Spec 093 ledger plus addendum pattern.
4. Delete in the dependency order diagnostics → OpenIddict → Identity oracle → shared EF substrate → packages/configuration.
5. Require one provider choice across every enabled lane; fail closed on absent capability/schema.
6. Keep #932 inside the host exit gate.
7. Preserve complete restored-graph evidence and reject missing/stale assets.
8. Reconcile OpenIddict wording by distinguishing delivery ownership from program completion membership.
9. Keep the temporary benchmark harness/oracles only until evidence import is complete, then remove them in the final slice.
10. Serialize all shared package/solution/host/coverage-ledger edits.

## Phase 1: Design & Contracts

- [data-model.md](data-model.md) defines the removal inventory, test-retention row, prerequisite gate, provider composition, certification result, review record, and closure record.
- [contracts/zero-ef-certification.md](contracts/zero-ef-certification.md) defines the fail-closed absolute-zero contract and bypass tests.
- [contracts/reference-host-matrix.md](contracts/reference-host-matrix.md) defines provider/lane composition and #932 expectations.
- [contracts/completion-evidence.md](contracts/completion-evidence.md) defines the retained evidence bundle required before merge and closure.
- [quickstart.md](quickstart.md) is the runnable validation guide.
- `AGENTS.md` is updated by the Speckit agent-context step to reference this plan.

## Delivery Sequence

1. **Intake freeze**: record current `origin/main`, ratchet counts, exact file/project/test inventory, prerequisite issue/PR/package heads, and shared-file serialization owner.
2. **Prerequisite admission**: verify #642, #643, #646, and #932 evidence is on remote `main`; reject deletion if any retained oracle still has a consumer.
3. **Host parity**: compose and test SQLite, SQL Server, PostgreSQL, and MongoDB with dashboard and all enabled durable features over one Groundwork provider.
4. **Test preservation**: complete the direct-token inventory and shared-host addendum; convert/rehost valid objectives and record architect dispositions.
5. **Vertical deletion**: remove diagnostics EF, OpenIddict EF, and Identity EF oracle families only after their gates; shrink the temporary baseline in the same commits.
6. **Shared substrate deletion**: remove `Elsa.Persistence.EFCore{,.Sqlite}`, EF-only tests/tools, solution entries, host settings, and package versions after all dependents are gone.
7. **Permanent guard**: delete temporary baselines/update switch and require every scanner category to be empty from a complete restored repository graph.
8. **Docs/maps**: update constitution/program goal/decision map/ADR-linked docs, schema operations, feature docs/catalogs, and generated maps.
9. **Verification/review**: run complete restore/build/test/pack/startup/provider/performance evidence checks, freeze the candidate, run three exact-range adversarial reviewers, remediate and re-verify.
10. **Model B closeout**: merge commit, verify remote `main`, update #647/#629 and Project 33 with merge/evidence records.

## Complexity Tracking

No constitution violations require justification.

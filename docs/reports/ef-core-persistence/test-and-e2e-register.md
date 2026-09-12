# EF Core persistence test and end-to-end register

Status: active completion evidence for [#1671](https://github.com/elsa-workflows/elsa-foundation/issues/1671).

Snapshot: `main` at `7a952efcf8d53472d7d4e7e3fd7b51d7a808c1c8` (2026-09-12).
This register covers 55 relevant test/evidence projects (51 snapshot projects plus two merged
Studio Preferences EF projects and two Structured Logs EF projects pending merge under #1695): the original 36 Groundwork/Mongo-named
projects, eight omitted direct-package consumers, two project-reference-only consumers, two host
consumers, and three EF-only Secrets destinations. The base Secrets EF test project is one of the
eight omitted direct-package consumers because it still exercises a dual Groundwork/EF topology.

`Relational + retire Mongo` means port the provider-neutral contract to SQLite, SQL Server,
PostgreSQL, and MySQL, then remove only the Mongo-specific fixture/topology. A project or suite is
not deletable until its replacement PR and evidence are recorded. Performance measurements are
retired by owner policy; correctness does not retire with them.

For every T-row, the `Providers and disposition` cell is its current/final-disposition slot, the
two PR cells are blank until exact merged PRs exist, and the default-flip PR is the matching
production/host row in the parent ledger. The dependency mapping below makes blockers and the
default-flip owner explicit without repeating the same text 51 times.

| Test rows | Dependencies / blockers | Default-flip PR owner |
|---|---|---|
| T01-T04, T15-T20, T32-T35, T42, T44, T46 | #1677 worker-ready children after MySQL, transaction and migration spikes; Runtime replacements where named | Owning Activities/Workflows Design, Publishing, Dashboard or import production row; blank pending merge |
| T05-T08, T39, T54-T55 | #1681 after shared EF foundation, MySQL and migration lifecycle | Diagnostics production/host rows; blank pending merge |
| T09-T13, T40 | #1682 after shared EF foundation, MySQL, transaction and migration lifecycle | Identity production/host rows; blank pending merge |
| T14, T21-T25, T28, T45, T47-T48, T51 | #1670 after the relevant replacements; #1678/#1669 where test-kit or migration behavior is involved | Host/default-flip row or N/A for a pure tool/guard; blank pending merge |
| T26-T27, T36-T38, T43 | #1672/#1676 after MySQL, transaction and migration spikes | Runtime/distributed Runtime host row; blank pending merge |
| T29-T30, T41, T49-T50 | #1679 after MySQL and migration-lifecycle spikes | Secrets production/host row; blank pending merge |
| T31, T52-T53 | #1680 after shared EF foundation, MySQL and migration lifecycle | Studio Preferences production/host row; replacement merged in #1693; default flip remains pending |

## Project register

| ID | Current project | Owner | Contract suites / required evidence | Providers and disposition | Replacement PR | Deletion PR |
|---|---|---|---|---|---|---|
| T01 | `tests/Elsa/Activities/Design/Persistence/Groundwork/TemporalProjectionTests/Elsa.Activities.Design.Persistence.Groundwork.TemporalProjectionTests.csproj` | #1677 child | Temporal definition and management-mutation projections | SQLite; port to EF | | |
| T02 | `tests/Elsa/Activities/Design/Persistence/Groundwork/Tests/Elsa.Activities.Design.Persistence.Groundwork.Tests.csproj` | #1677 child | Activity definition/version/draft/availability/upgrade CRUD, registration and manifest | SQLite; port to EF | | |
| T03 | `tests/Elsa/Activities/Design/Persistence/Groundwork/V2/ProviderMatrix/Tests/Elsa.Activities.Design.Persistence.Groundwork.V2.ProviderMatrix.Tests.csproj` | #1677 child | Activities Design provider contract matrix | Relational + retire Mongo; add MySQL | | |
| T04 | `tests/Elsa/Activities/Design/Persistence/Groundwork/V2/Tests/Elsa.Activities.Design.Persistence.Groundwork.V2.Tests.csproj` | #1677 child | Atomicity and concurrency | SQLite; port to EF | | |
| T05 | `tests/Elsa/Diagnostics/OpenTelemetry/Persistence/Groundwork/V2/Tests/Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.V2.Tests.csproj` | #1681 | Trace/log/metric round-trip, filtering, retention, HTTP boundary and session disposal | Relational + retire Mongo; add MySQL | | |
| T06 | `tests/Elsa/Diagnostics/Persistence/Groundwork/Tests/Elsa.Diagnostics.Persistence.Groundwork.Tests.csproj` | #1681 | Diagnostics umbrella feature composition | Rebind to EF | | |
| T07 | `tests/Elsa/Diagnostics/Persistence/Groundwork/V2/Consumer/Elsa.Diagnostics.Persistence.Groundwork.V2.Consumer.csproj` | #1681 | Consumer build/launch boundary | Rewire to EF or explicitly retire when no consumer boundary remains | | |
| T08 | `tests/Elsa/Diagnostics/StructuredLogs/Persistence/Groundwork/V2/Tests/Elsa.Diagnostics.StructuredLogs.Persistence.Groundwork.V2.Tests.csproj` | #1681 | Append, order/high-water, retry, retention, restart and disposal | Relational + retire Mongo; add MySQL | | |
| T09 | `tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/ProcessProbe/Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.ProcessProbe.csproj` | #1682 | Process restart, duplicate handling and normalized identity protocol | Relational + retire Mongo; add MySQL | | |
| T10 | `tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.csproj` | #1682 | ASP.NET Identity user/role/relationship, tenant, atomicity, concurrency, failure mapping, reconciliation and seeding | SQLite; port to EF | | |
| T11 | `tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/V2/ProviderMatrix/Tests/Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.V2.ProviderMatrix.Tests.csproj` | #1682 | Relationship, tenant, schema, reopen and process matrix | Relational + retire Mongo; add MySQL | | |
| T12 | `tests/Elsa/Foundation/Identity/Persistence/Groundwork/Tests/Elsa.Foundation.Identity.Persistence.Groundwork.Tests.csproj` | #1682 | IAM row/revision/manifest/conformance contracts | SQLite; port to EF | | |
| T13 | `tests/Elsa/Foundation/Identity/Persistence/Groundwork/V2/ProviderMatrix/Tests/Elsa.Foundation.Identity.Persistence.Groundwork.V2.ProviderMatrix.Tests.csproj` | #1682 | IAM CRUD, normalized lookup, revision, isolation and restart | Relational + retire Mongo; add MySQL | | |
| T14 | `tests/Elsa/Groundwork/ProviderEvidenceImporter/Tests/Elsa.Groundwork.ProviderEvidenceImporter.Tests.csproj` | #1670 | Checkpoint/fence evidence mapping | Preserve neutral evidence mapping; retire Groundwork-only importer | | |
| T15 | `tests/Elsa/Persistence/Groundwork/DesignConformance/MongoDb/Tests/Elsa.Persistence.Groundwork.DesignConformance.MongoDb.Tests.csproj` | #1677 child / #1670 | Provider-neutral Design CRUD/query/atomicity/isolation/restart suites plus Mongo topology | Move neutral suites; retire Mongo topology | | |
| T16 | `tests/Elsa/Persistence/Groundwork/DesignConformance/PostgreSql/Tests/Elsa.Persistence.Groundwork.DesignConformance.PostgreSql.Tests.csproj` | #1672 and #1677 children | Activities Design, Runtime, distributed Runtime, Publishing and Workflows Design conformance | Port to EF PostgreSQL; ownership spans P08-P12 | | |
| T17 | `tests/Elsa/Persistence/Groundwork/DesignConformance/SqlServer/Tests/Elsa.Persistence.Groundwork.DesignConformance.SqlServer.Tests.csproj` | #1672 and #1677 children | Activities Design, Runtime, distributed Runtime, Publishing and Workflows Design conformance | Port to EF SQL Server; ownership spans P08-P12 | | |
| T18 | `tests/Elsa/Persistence/Groundwork/DesignConformance/Sqlite/Tests/Elsa.Persistence.Groundwork.DesignConformance.Sqlite.Tests.csproj` | #1672 and #1677 children | Atomicity, query, workflow-design lifecycle, WAL/busy/locking-relevant behavior | Port to EF SQLite | | |
| T19 | `tests/Elsa/Persistence/Groundwork/DesignConformance/Target/Elsa.Persistence.Groundwork.DesignConformance.Target.csproj` | #1672 and #1677 children | Provider-neutral fault/event injection target | Preserve as EF-neutral harness or retarget to EF | | |
| T20 | `tests/Elsa/Persistence/Groundwork/DesignConformance/Tests/Elsa.Persistence.Groundwork.DesignConformance.Tests.csproj` | #1672 and #1677 children | Activity/workflow Design CRUD/query/atomicity/isolation/restart/scale suite shape | Preserve and rebind to EF; retire measurement-only query-plan criteria | | |
| T21 | `tests/Elsa/Persistence/Groundwork/MongoDb/UnifiedHost/Tests/Elsa.Persistence.Groundwork.MongoDb.UnifiedHost.Tests.csproj` | #1670 | Unified host composition and restart journey | Extract neutral host journey; retire replica-set/standalone topology | | |
| T22 | `tests/Elsa/Persistence/Groundwork/PostgreSql/UnifiedHost/Tests/Elsa.Persistence.Groundwork.PostgreSql.UnifiedHost.Tests.csproj` | #1670 | PostgreSQL container, unified composition and restart | Port to EF PostgreSQL | | |
| T23 | `tests/Elsa/Persistence/Groundwork/SqlServer/UnifiedHost/Tests/Elsa.Persistence.Groundwork.SqlServer.UnifiedHost.Tests.csproj` | #1670 | SQL Server container and unified composition | Port to EF SQL Server | | |
| T24 | `tests/Elsa/Persistence/Groundwork/Testing/Elsa.Persistence.Groundwork.Testing.csproj` | #1678 | Provider/session/schema test kit and neutral fault/recording doubles | Replace with EF four-provider kit; preserve neutral doubles | | |
| T25 | `tests/Elsa/Persistence/Groundwork/UnifiedHost/Tests/Elsa.Persistence.Groundwork.UnifiedHost.Tests.csproj` | #1670 | Target isolation, activation/restart, publication-lane and SQLite composition | Port to EF host | | |
| T26 | `tests/Elsa/Persistence/Groundwork/V2/Runtime/Tests/Elsa.Persistence.Groundwork.V2.Runtime.Tests.csproj` | #1672 / #1676 | All 29 Runtime units: checkpoint/outbox, claims/fences, queues/poison, timers, bookmarks/triggers/schedules, durable values, dispatch, recovery, incidents/alterations/holds/test scope and materialization | Relational + retire Mongo; add MySQL | | |
| T27 | `tests/Elsa/Persistence/Groundwork/V2/Testing/Elsa.Persistence.Groundwork.V2.Testing.csproj` | #1678 / #1672 | Runtime persistence test infrastructure | Replace with EF infrastructure | | |
| T28 | `tests/Elsa/Persistence/Groundwork/V2/Tests/Elsa.Persistence.Groundwork.V2.Tests.csproj` | #1670 / #1678 | Manifest, provider connections, storage access audit and release boundary | Port neutral evidence; rewrite final no-Groundwork guard; retire Mongo | | |
| T29 | `tests/Elsa/Secrets/Persistence/Groundwork/V2/ProviderMatrix/Tests/Elsa.Secrets.Persistence.Groundwork.V2.ProviderMatrix.Tests.csproj` | #1679 | Secrets CRUD/revision/query/tenant/restart matrix | Relational + retire Mongo; add MySQL | | |
| T30 | `tests/Elsa/Secrets/Persistence/Groundwork/V2/Tests/Elsa.Secrets.Persistence.Groundwork.V2.Tests.csproj` | #1679 | Secret repository and registration contracts | SQLite; replace with EF evidence | | |
| T31 | `tests/Elsa/Studio/Preferences/Persistence/Groundwork/Tests/Elsa.Studio.Preferences.Persistence.Groundwork.Tests.csproj` | #1680 | Preference scope/read/write/concurrency | SQLite; port to EF and add relational providers | | |
| T32 | `tests/Elsa/Workflows/Dashboard/Persistence/Groundwork/V2/Tests/Elsa.Workflows.Dashboard.Persistence.Groundwork.V2.Tests.csproj` | #1677 child | Portfolio and run-health bounded projections | SQLite; port to EF | | |
| T33 | `tests/Elsa/Workflows/Design/Persistence/Groundwork/Tests/Elsa.Workflows.Design.Persistence.Groundwork.Tests.csproj` | #1677 child | Definition/version/draft/layout/list projection, commands, registration and schema | SQLite; port to EF | | |
| T34 | `tests/Elsa/Workflows/Publishing/Api/GroundworkTests/Elsa.Workflows.Publishing.Api.GroundworkTests.csproj` | #1677 child | Activity test-run receipts, publication commits, upgrades and published-deletion guard | Relational + retire Mongo; add MySQL | | |
| T35 | `tests/Elsa/Workflows/Publishing/Persistence/Groundwork/Tests/Elsa.Workflows.Publishing.Persistence.Groundwork.Tests.csproj` | #1677 child | Six Publishing units, lifetime and provider matrix | Relational + retire Mongo; add MySQL | | |
| T36 | `tests/Elsa/Workflows/Runtime/Distributed/Persistence/Groundwork/Tests/Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Tests.csproj` | #1672 / #1676 | Placement/fencing, stream-head/command ordering, lease/redelivery/ack and restart | Relational + retire Mongo; add MySQL | | |
| T37 | `tests/Elsa/Activities/Http/IntegrationTests/Elsa.Activities.Http.IntegrationTests.csproj` | #1672 / #1670 | HTTP endpoint status/body/content type, workflow completion and artifact persistence | Port to EF; split and retire only commit-count/write-amplification assertions | | |
| T38 | `tests/Elsa/Activities/Scheduling/Tests/Elsa.Activities.Scheduling.Tests.csproj` | #1672 | Durable timer, bookmark, crash/restart and idempotent resume | Port to EF | | |
| T39 | `tests/Elsa/Diagnostics/Persistence/Tests/Elsa.Diagnostics.Persistence.Tests.csproj` | #1681 | Diagnostics lifecycle/order/loss/retry/observability, drain and provider fixtures | Relational + retire Mongo; `DiagnosticsDrainLoadTests` remains correctness | | |
| T40 | `tests/Elsa/Foundation/Identity/Tests/Elsa.Foundation.Identity.Tests.csproj` | #1682 | Shell/sign-in/IAM/normalized lookup and OpenIddict composition boundary | Port to EF; retain separate vendor context | | |
| T41 | `tests/Elsa/Secrets/Persistence/EntityFrameworkCore/Tests/Elsa.Secrets.Persistence.EntityFrameworkCore.Tests.csproj` | #1679 | EF migration/composition/shell reload/search-key/concurrency/repository plus current dual-provider compatibility | Retain EF destination; remove dual Groundwork compatibility only after flip | | |
| T42 | `tests/Elsa/Workflows/Design/Tests/Elsa.Workflows.Design.Tests.csproj` | #1677 child | Provider-neutral draft/tree/checkpoint-cadence behavior with Groundwork host | Preserve; replace host with EF | | |
| T43 | `tests/Elsa/Workflows/Runtime/Distributed/Tests/Elsa.Workflows.Runtime.Distributed.Tests.csproj` | #1672 / #1676 | Distributed actor/placement/transport and two-node acceptance | Preserve; skipped checkpoint/outbox/dispatch restart case is a gap, not evidence | | |
| T44 | `tests/Elsa3/Mapping/Tests/Elsa3.Mapping.Tests.csproj` | #1677 child | Import manifest, idempotent/atomic apply, restart and registration | Port to EF or neutralize harness | | |
| T45 | `tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj` | #1670 / #1678 | Architecture/security/dependency/composition guards | Rewrite Groundwork-positive assertions to EF-positive and no-Groundwork guards | | |
| T46 | `tests/Elsa/Workflows/Publishing/Api/Tests/Elsa.Workflows.Publishing.Api.Tests.csproj` | #1677 child | Publishing API, preflight, activation, projection reconciliation and cross-module transaction semantics | Preserve and rebind host to EF | | |
| T47 | `tests/Elsa/Modularity/Tests/Elsa.Modularity.Tests.csproj` | #1670 | Provider registration, connection ownership, schema/startup/refusal and Workbench composition | Preserve in EF composition; remove Mongo branch | | |
| T48 | `tests/Elsa/Workbench/Tests/Elsa.Workbench.Tests.csproj` | #1670 | Workbench process, shell activation and restart | Replace Groundwork settings with EF | | |
| T49 | `tests/Elsa/Secrets/Persistence/EntityFrameworkCore/PostgreSql/Tests/Elsa.Secrets.Persistence.EntityFrameworkCore.PostgreSql.Tests.csproj` | #1679 | PostgreSQL repository, concurrency, shell/container and migration proof | Retain and extend through final flip | | N/A |
| T50 | `tests/Elsa/Secrets/Persistence/EntityFrameworkCore/SqlServer/Tests/Elsa.Secrets.Persistence.EntityFrameworkCore.SqlServer.Tests.csproj` | #1679 | SQL Server repository, concurrency and container proof | Retain and extend through final flip | | N/A |
| T51 | `tests/Elsa/Secrets/Persistence/EntityFrameworkCore/PostgreSql/PackageFeedProbe/Elsa.Secrets.Persistence.EntityFrameworkCore.PostgreSql.PackageFeedProbe.csproj` | #1679 / #1670 | PostgreSQL package-feed process probe | Retain only while it proves an active package boundary | | N/A |
| T52 | `tests/Elsa/Studio/Preferences/Persistence/EntityFrameworkCore/Tests/Elsa.Studio.Preferences.Persistence.EntityFrameworkCore.Tests.csproj` | #1680 | SQLite behavioral contract: scope isolation, CRUD, canonical revisions, optimistic concurrency, restart and opt-in DI | SQLite; retained EF destination, merged in #1693 | #1693 | N/A |
| T53 | `tests/Elsa/Studio/Preferences/Persistence/EntityFrameworkCore/ProviderTests/Elsa.Studio.Preferences.Persistence.EntityFrameworkCore.ProviderTests.csproj` | #1680 | Live provider smoke: PostgreSQL/SQL Server/MySQL model binding, representative CRUD, uniqueness and concurrency | PostgreSQL/SQL Server/MySQL; retained EF destination, merged in #1693 | #1693 | N/A |
| T54 | `tests/Elsa/Diagnostics/StructuredLogs/Persistence/EntityFrameworkCore/Tests/Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore.Tests.csproj` | #1695 | SQLite behavioral contract: append/order/high-water/idempotency, retention, restart, cursor/binding and opt-in DI | SQLite; retained EF destination pending merge under #1695 | | |
| T55 | `tests/Elsa/Diagnostics/StructuredLogs/Persistence/EntityFrameworkCore/ProviderTests/Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore.ProviderTests.csproj` | #1695 | Live provider smoke: PostgreSQL/SQL Server/MySQL model binding plus representative append/read/CAS and rollback or stale-CAS outcome | PostgreSQL/SQL Server/MySQL; Docker-skippable retained EF destination pending merge under #1695 | | |

## Correctness hidden inside retiring performance surfaces

| Current file or suite | Correctness that must move before deletion | Retired material | Owner | Replacement PR | Disposition |
|---|---|---|---|---|---|
| `tests/Elsa/Activities/Http/IntegrationTests/HttpEndpointRuntimePerformanceTests.cs` | HTTP status, response body/content type, workflow completion and persisted artifact | Commit-count and write-amplification comparisons | #1672/#1670 and #1668 | | Split pending |
| `tests/Elsa/Foundation/Identity/Tests/AspNetCoreIdentity/Performance/IamNormalizedLookupSqliteCorrectnessTests.cs` | Timing-free normalized-name/email lookup and duplicate behavior | Native-plan/measurement acceptance | #1682 and #1668 | | Move/split pending |
| `benchmarks/Elsa/Activities/Runtime/Benchmarks/ActivationScopeSemanticTests.cs` | Activation-scope correctness | Benchmark project and measurement harness | #1672 and #1668 | | Move pending |
| `tools/ledger/StorePerformance/AdapterHost/Tests` | Adapter protocol, provider binding and failure correctness not expressed elsewhere | Timing, budget and native-plan evidence | #1678/#1670 and #1668 | | Map/move pending |
| `tools/ledger/StorePerformance/Benchmarks/Tests` | Workflow completion, checkpoint/outbox, duplicate and recovery assertions not expressed elsewhere | Measurement admission, comparison and fingerprints | #1672 and #1668 | | Map/move pending |
| `tests/Elsa/Diagnostics/Persistence/Tests/DiagnosticsDrainLoadTests.cs` | Drain ordering, bounded batches, no-loss/retry behavior | None merely because the class name contains `Load` | #1681 | | Preserve |

No benchmark, timing measurement, timing budget, or performance workflow is required or permitted as
replacement evidence. Historical results may remain archived and must be described as historical.

## Backend end-to-end journey register

These are timing-independent host-boundary journeys. Run only the narrow relevant suite against a
rebuilt host and fresh database when its owner changes. The Groundwork lifecycle sentinel must be
rewritten as an EF-only/no-Groundwork lifecycle proof or explicitly retired after equivalent coverage
exists.

For E01-E13, blockers are the owning module replacement and rebuilt/fresh-database host; the
replacement and default-flip PRs are inherited from that owner, and the deletion PR applies only
when the old script is replaced or retired. All are blank pending exact merges.

| ID | Current script | Owner | Required EF evidence | Replacement PR | Disposition |
|---|---|---|---|---|---|
| E01 | `e2e-tests/durability/Test-RestartRecovery.ps1` | #1672 | Suspend, restart and recover persisted execution | | Preserve/rebind |
| E02 | `e2e-tests/durability/Test-VariableSurvivesSuspend.ps1` | #1672 | Durable variable survives suspension and restart | | Preserve/rebind |
| E03 | `e2e-tests/resilience/Test-PoisonedWork.ps1` | #1672 | Poison classification, retry and recovery | | Preserve/rebind |
| E04 | `e2e-tests/composition/Test-DispatchWorkflowOutcomes.ps1` | #1672/#1677 | Dispatch outcome durability and parent/child behavior | | Preserve/rebind |
| E05 | `e2e-tests/runtime-alterations/Test-AlterationPlans.ps1` | #1672 | Alteration plan lifecycle and idempotency | | Preserve/rebind |
| E06 | `e2e-tests/runtime-alterations/Test-AlterationReplayAndRestart.ps1` | #1672 | Alteration replay and restart recovery | | Preserve/rebind |
| E07 | `e2e-tests/scheduling/Test-Delay.ps1` | #1672 | Durable delay resume | | Preserve/rebind |
| E08 | `e2e-tests/scheduling/Test-Timer.ps1` | #1672 | Durable timer scheduling and resume | | Preserve/rebind |
| E09 | `e2e-tests/orchestration-controls/Test-StimulusRouting.ps1` | #1672 | Bookmark/trigger stimulus routing | | Preserve/rebind |
| E10 | `e2e-tests/reusable-activities/Test-ActivityDraftTestRun.ps1` | #1677 child | Activity draft test-run receipt and isolated execution | | Preserve/rebind |
| E11 | `e2e-tests/reusable-activities/Test-DraftTestRun.ps1` | #1677 child | Workflow draft test-run dispatch and isolation | | Preserve/rebind |
| E12 | `e2e-tests/diagnostics/Test-OpenTelemetryApiMigration.ps1` | #1681 | OpenTelemetry API persistence and migration boundary | | Preserve/rebind |
| E13 | `e2e-tests/groundwork/Test-GroundworkReleaseLifecycle.ps1` | #1670 | Final EF-only lifecycle and active-code/dependency no-Groundwork proof | | Rewrite or retire after equivalent proof |

## Proof rules

- SQLite, SQL Server, PostgreSQL, and MySQL evidence must name the exact current test and commit.
- Mongo-specific topology never counts as relational parity, but neutral contracts extracted from it
  remain requirements.
- Skipped tests, including the two-node checkpoint/outbox/dispatch acceptance gap, do not count as
  proof.
- A passing build does not replace migration, provider, restart, transaction, or e2e evidence.
- Every deleted project or suite must point to a merged replacement or to an owner-authorized
  retirement that contains no surviving correctness requirement.

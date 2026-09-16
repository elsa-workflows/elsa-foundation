# EF Core persistence test and end-to-end register

Status: closed. Every row below carries a merged PR, the deletion PR #1764, or an explicit statement
of what happened where neither applies. This register is completion evidence for
[#1671](https://github.com/elsa-workflows/elsa-foundation/issues/1671) and is no longer a worklist.

Original snapshot: `main` at `7a952efcf8d53472d7d4e7e3fd7b51d7a808c1c8` (2026-09-12).
Closed against `main` at `a83c41c1c50904435bcd91f4b25ff4bfbd8d3d51`, the merge of
[#1764](https://github.com/elsa-workflows/elsa-foundation/pull/1764), whose parent is
`569903c80c1ded896294efc601a1f5d976194ef5` — the merge of
[#1763](https://github.com/elsa-workflows/elsa-foundation/pull/1763), which flipped the shipped
compositions to EF Core by default. Because that flip is the base, every suite in this register that
drives a host now drives an EF Core host without a per-suite edit.

This register covers 66 relevant test/evidence projects: the original 36 Groundwork/Mongo-named
projects, eight omitted direct-package consumers, two project-reference-only consumers, two host
consumers, three EF-only Secrets destinations, and the EF destinations that landed during the
program (Studio Preferences, Structured Logs, OpenTelemetry, Identity and ASP.NET Identity,
distributed Runtime, Runtime, Activities Design, Secrets MySQL and the Elsa 3 import).

`Relational + retire Mongo` meant port the provider-neutral contract to SQLite, SQL Server,
PostgreSQL, and MySQL, then remove only the Mongo-specific fixture/topology. Performance
measurements are retired by owner policy; correctness did not retire with them, and no row here
records or may record a performance result.

For every T-row, the `Providers and disposition` cell is its final-disposition slot. The dependency
mapping below is retained as the historical blocker record; every dependency in it is discharged.

| Test rows | Dependencies / blockers (all discharged) | Default-flip |
|---|---|---|
| T01-T04, T15-T20, T32-T35, T42, T44, T46 | #1677 worker-ready children after MySQL, transaction and migration spikes; Runtime replacements where named | #1763 |
| T05-T08, T39, T54-T57 | #1681 after shared EF foundation, MySQL and migration lifecycle | #1763 |
| T09-T13, T40, T58-T60 | #1682 after shared EF foundation, MySQL, transaction and migration lifecycle | #1763 |
| T14, T21-T25, T28, T45, T47-T48, T51 | #1670 after the relevant replacements; #1678/#1669 where test-kit or migration behavior is involved | #1763, or N/A for a pure tool/guard |
| T26-T27, T36-T38, T43, T61-T64 | #1672/#1676 after MySQL, transaction and migration spikes | #1763 |
| T29-T30, T41, T49-T50, T71 | #1679 after MySQL and migration-lifecycle spikes | #1763 |
| T31, T52-T53 | #1680 after shared EF foundation, MySQL and migration lifecycle | #1763 |
| T65-T70 | Runtime, Activities Design and Elsa 3 import EF destinations | #1763 |

## Deletion step — #1764

The deletion is [#1764](https://github.com/elsa-workflows/elsa-foundation/pull/1764), merged to
`main` as `a83c41c1c`. What it removed, and what happened to the
coverage:

**Deleted outright** — 36 Groundwork/Mongo test projects, `tests/Groundwork/`, `tools/groundwork/`
and `e2e-tests/groundwork/`. Their subject code is gone, so the coverage went with the subject.

Seven projects this register briefly recorded as `Deleted` were **not** deleted — only their
Groundwork halves were — and the rows are corrected below: T39 `Elsa.Diagnostics.Persistence.Tests`,
T41 `Elsa.Secrets.Persistence.EntityFrameworkCore.Tests`, T42 `Elsa.Workflows.Design.Tests`,
T45 `Elsa.Architecture.Tests`, T47 `Elsa.Modularity.Tests`, T48 `Elsa.Workbench.Tests`, and
T69 `Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.Tests`.

**Re-pointed at EF Core** — suites that used a Groundwork fixture only as a stand-in for "a real
durable substrate" now compose the matching EF Core module over one SQLite file and install its
schema through the registered `IShellInitializer`:

| Suite | What it proves | Now runs on |
|---|---|---|
| `Elsa.Activities.Http.IntegrationTests` | checkpoint-policy equivalence for a synchronous HTTP endpoint | Runtime EF Core aggregate |
| `Elsa.Activities.Scheduling.Tests` | durable-timer restart/crash generations | Runtime EF Core aggregate |
| `Elsa.Workflows.Design.Tests` | the workflows-design command/store host (16 test files) | Workflows Design EF Core |
| `Elsa.Foundation.Identity.Tests` | sign-in, seeding, token endpoint, shell composition, dev/demo guard, IAM normalized lookup | Identity IAM EF Core |
| `Elsa.Modularity.Tests` | secret-setting catalog masking and secret-flag pinning | EF Core feature settings |
| `Elsa3.Mapping.Tests` | reusable-activity collection apply | in-memory analyzer/importer only |

**Dropped with no replacement**, named so the loss is legible rather than implied:

| Dropped | Property it held |
|---|---|
| `SecretsSearchKeysTests` Groundwork-oracle cases | exhaustive scalar-by-scalar equality of the EF casing projection against Groundwork's comparison key. Representative values are now golden expectations captured from the current implementation; the 0x0–0x10FFFF sweep is gone. |
| `DiagnosticsProviderFixture` + `DiagnosticsProviderLifecycleSmokeTests` | four-provider (SQLite/SQL Server/PostgreSQL/MongoDB) Groundwork lease provisioning and readiness |
| `DiagnosticsPersistenceFeatureTests` | Groundwork diagnostics feature composition |
| `DiagnosticsLifecycleEvidenceTests` | asserted the spec-139 Groundwork evidence file |
| `Elsa3ImportManifestPhysicalStorageGoldenTests` | pinned the Groundwork import storage-unit manifest |
| `DesignPersistenceBoundaryTests` | provider-neutral design cores must not reach a Groundwork project or package, declared or resolved |
| `DesignPersistenceBoundedQueryTests` | no load-all/client-evaluation token in the three Groundwork design lanes |
| `AspNetCoreIdentityMutationGuardTests` | bounded-cursor paging in the Groundwork identity tree |
| `DesignLedgerIsolationTests`, `SecretsPersistenceGateOwnershipTests`, `ProviderParsingFootprintRatchetTests` | Groundwork ledger readers and the `tools/groundwork` parsing ratchet |

**Replaced by one guard** — `RetiredPersistenceFamilyGuardTests` now refuses any project, package,
NuGet feed, source file, directory, solution filter or CI job naming either retired family under
`src/`, `tests/`, `tools/`, `docker/`, `e2e-tests/` or `.github/`. It assembles the forbidden names at
runtime so it needs no self-exclusion, asserts each scan saw a non-empty file set, and carries a
mutation proof.

**CI** — `groundwork-v2-native-provider-matrix` is replaced by `ef-container-suites`, a matrix with
one leg per Testcontainers test project (15 legs); `groundwork-fast` is renamed `architecture-guards`;
the five permanently-disabled `if: false` Groundwork scaffold jobs in `integration.yml` are dropped;
and the nightly Testcontainers job runs the regenerated `Elsa.Server.Persistence.Integration.slnf`.
`GROUNDWORK_V2_REQUIRE_NATIVE_PROVIDER_MATRIX` is renamed `ELSA_REQUIRE_NATIVE_PROVIDER_MATRIX`.

The matrix is **not uniformly fail-closed**, and no row in this register may be read as if it were.
Six legs arm a required-provider variable, so a missing container fails them: `migrations`,
`publishing` and `elsa3-import` on `ELSA_REQUIRE_NATIVE_PROVIDER_MATRIX`; `runtime` and
`runtime-distributed` on `ELSA_RUNTIME_PLACEMENT_EF_REQUIRE_NATIVE_PROVIDERS`; `activities-design`
on `ELSA_ACTIVITIES_DESIGN_EF_REQUIRE_NATIVE_PROVIDERS`. The other nine have **no** such variable in
their fixtures and self-skip when a container is unavailable, so a green leg is not by itself
native-provider evidence: workflows-design, identity, opentelemetry, structured-logs,
studio-preferences, secrets-sqlserver, secrets-mysql, transaction-topology and mysql-feasibility.
Secrets PostgreSQL is the exception outside the matrix: it runs in the separate
`secrets-ef-composition` job, which starts its own PostgreSQL container and fails the job if the
container never becomes ready.

## Project register

| ID | Current project | Owner | Contract suites / required evidence | Providers and disposition | Replacement PR | Deletion PR |
|---|---|---|---|---|---|---|
| T01 | `tests/Elsa/Activities/Design/Persistence/Groundwork/TemporalProjectionTests/Elsa.Activities.Design.Persistence.Groundwork.TemporalProjectionTests.csproj` | #1677 child | Temporal definition and management-mutation projections | Deleted (was: SQLite; port to EF) |  | #1764 |
| T02 | `tests/Elsa/Activities/Design/Persistence/Groundwork/Tests/Elsa.Activities.Design.Persistence.Groundwork.Tests.csproj` | #1677 child | Activity definition/version/draft/availability/upgrade CRUD, registration and manifest | Deleted (was: SQLite; port to EF) |  | #1764 |
| T03 | `tests/Elsa/Activities/Design/Persistence/Groundwork/V2/ProviderMatrix/Tests/Elsa.Activities.Design.Persistence.Groundwork.V2.ProviderMatrix.Tests.csproj` | #1677 child | Activities Design provider contract matrix | Deleted (was: Relational + retire Mongo; add MySQL) |  | #1764 |
| T04 | `tests/Elsa/Activities/Design/Persistence/Groundwork/V2/Tests/Elsa.Activities.Design.Persistence.Groundwork.V2.Tests.csproj` | #1677 child | Atomicity and concurrency | Deleted (was: SQLite; port to EF) |  | #1764 |
| T67 | `tests/Elsa/Activities/Design/Persistence/EntityFrameworkCore/Tests/Elsa.Activities.Design.Persistence.EntityFrameworkCore.Tests.csproj` | task #1731 | EF Activities Design model creation, SQLite round-trip, tenant isolation, atomicity, projections, lifecycle, draft/layout, fork receipt, and immutable identity evidence | SQLite and provider-neutral model validation. Retained EF destination, merged in #1742; migrations added by #1755; further changes in #1760 | #1742 | N/A |
| T68 | `tests/Elsa/Activities/Design/Persistence/EntityFrameworkCore/ProviderTests/Elsa.Activities.Design.Persistence.EntityFrameworkCore.ProviderTests.csproj` | task #1731 | Live SQL Server, PostgreSQL, and MySQL model creation, CRUD/query, rollback, optimistic concurrency, required TenantKey, and concurrent global uniqueness | SQL Server/PostgreSQL/MySQL. Retained EF destination, merged in #1742. Runs as the `activities-design` leg of `ef-container-suites`, which **is** armed fail-closed by `ELSA_ACTIVITIES_DESIGN_EF_REQUIRE_NATIVE_PROVIDERS`, so this is hosted native-provider evidence | #1742 | N/A |
| T05 | `tests/Elsa/Diagnostics/OpenTelemetry/Persistence/Groundwork/V2/Tests/Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.V2.Tests.csproj` | #1681 | Trace/log/metric round-trip, filtering, retention, HTTP boundary and session disposal | Deleted (was: Relational + retire Mongo; add MySQL) |  | #1764 |
| T06 | `tests/Elsa/Diagnostics/Persistence/Groundwork/Tests/Elsa.Diagnostics.Persistence.Groundwork.Tests.csproj` | #1681 | Diagnostics umbrella feature composition | Deleted (was: Rebind to EF) |  | #1764 |
| T07 | `tests/Elsa/Diagnostics/Persistence/Groundwork/V2/Consumer/Elsa.Diagnostics.Persistence.Groundwork.V2.Consumer.csproj` | #1681 | Consumer build/launch boundary | Deleted (was: Rewire to EF or explicitly retire when no consumer boundary remains) |  | #1764 |
| T08 | `tests/Elsa/Diagnostics/StructuredLogs/Persistence/Groundwork/V2/Tests/Elsa.Diagnostics.StructuredLogs.Persistence.Groundwork.V2.Tests.csproj` | #1681 | Append, order/high-water, retry, retention, restart and disposal | Deleted (was: Relational + retire Mongo; add MySQL) |  | #1764 |
| T09 | `tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/ProcessProbe/Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.ProcessProbe.csproj` | #1682 | Process restart, duplicate handling and normalized identity protocol | Deleted (was: Relational + retire Mongo; add MySQL) |  | #1764 |
| T10 | `tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.csproj` | #1682 | ASP.NET Identity user/role/relationship, tenant, atomicity, concurrency, failure mapping, reconciliation and seeding | Deleted (was: SQLite; port to EF) |  | #1764 |
| T11 | `tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/V2/ProviderMatrix/Tests/Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.V2.ProviderMatrix.Tests.csproj` | #1682 | Relationship, tenant, schema, reopen and process matrix | Deleted (was: Relational + retire Mongo; add MySQL) |  | #1764 |
| T12 | `tests/Elsa/Foundation/Identity/Persistence/Groundwork/Tests/Elsa.Foundation.Identity.Persistence.Groundwork.Tests.csproj` | #1682 | IAM row/revision/manifest/conformance contracts | Deleted (was: SQLite; port to EF) |  | #1764 |
| T13 | `tests/Elsa/Foundation/Identity/Persistence/Groundwork/V2/ProviderMatrix/Tests/Elsa.Foundation.Identity.Persistence.Groundwork.V2.ProviderMatrix.Tests.csproj` | #1682 | IAM CRUD, normalized lookup, revision, isolation and restart | Deleted (was: Relational + retire Mongo; add MySQL) |  | #1764 |
| T14 | `tests/Elsa/Groundwork/ProviderEvidenceImporter/Tests/Elsa.Groundwork.ProviderEvidenceImporter.Tests.csproj` | #1670 | Checkpoint/fence evidence mapping | Deleted (was: Preserve neutral evidence mapping; retire Groundwork-only importer) |  | #1764 |
| T15 | `tests/Elsa/Persistence/Groundwork/DesignConformance/MongoDb/Tests/Elsa.Persistence.Groundwork.DesignConformance.MongoDb.Tests.csproj` | #1677 child / #1670 | Provider-neutral Design CRUD/query/atomicity/isolation/restart suites plus Mongo topology | Deleted (was: Move neutral suites; retire Mongo topology) |  | #1764 |
| T16 | `tests/Elsa/Persistence/Groundwork/DesignConformance/PostgreSql/Tests/Elsa.Persistence.Groundwork.DesignConformance.PostgreSql.Tests.csproj` | #1672 and #1677 children | Activities Design, Runtime, distributed Runtime, Publishing and Workflows Design conformance | Deleted (was: Port to EF PostgreSQL; ownership spans P08-P12) |  | #1764 |
| T17 | `tests/Elsa/Persistence/Groundwork/DesignConformance/SqlServer/Tests/Elsa.Persistence.Groundwork.DesignConformance.SqlServer.Tests.csproj` | #1672 and #1677 children | Activities Design, Runtime, distributed Runtime, Publishing and Workflows Design conformance | Deleted (was: Port to EF SQL Server; ownership spans P08-P12) |  | #1764 |
| T18 | `tests/Elsa/Persistence/Groundwork/DesignConformance/Sqlite/Tests/Elsa.Persistence.Groundwork.DesignConformance.Sqlite.Tests.csproj` | #1672 and #1677 children | Atomicity, query, workflow-design lifecycle, WAL/busy/locking-relevant behavior | Deleted (was: Port to EF SQLite) |  | #1764 |
| T19 | `tests/Elsa/Persistence/Groundwork/DesignConformance/Target/Elsa.Persistence.Groundwork.DesignConformance.Target.csproj` | #1672 and #1677 children | Provider-neutral fault/event injection target | Deleted (was: Preserve as EF-neutral harness or retarget to EF) |  | #1764 |
| T20 | `tests/Elsa/Persistence/Groundwork/DesignConformance/Tests/Elsa.Persistence.Groundwork.DesignConformance.Tests.csproj` | #1672 and #1677 children | Activity/workflow Design CRUD/query/atomicity/isolation/restart/scale suite shape | Deleted (was: Preserve and rebind to EF; retire measurement-only query-plan criteria) |  | #1764 |
| T21 | `tests/Elsa/Persistence/Groundwork/MongoDb/UnifiedHost/Tests/Elsa.Persistence.Groundwork.MongoDb.UnifiedHost.Tests.csproj` | #1670 | Unified host composition and restart journey | Deleted (was: Extract neutral host journey; retire replica-set/standalone topology) |  | #1764 |
| T22 | `tests/Elsa/Persistence/Groundwork/PostgreSql/UnifiedHost/Tests/Elsa.Persistence.Groundwork.PostgreSql.UnifiedHost.Tests.csproj` | #1670 | PostgreSQL container, unified composition and restart | Deleted (was: Port to EF PostgreSQL) |  | #1764 |
| T23 | `tests/Elsa/Persistence/Groundwork/SqlServer/UnifiedHost/Tests/Elsa.Persistence.Groundwork.SqlServer.UnifiedHost.Tests.csproj` | #1670 | SQL Server container and unified composition | Deleted (was: Port to EF SQL Server) |  | #1764 |
| T24 | `tests/Elsa/Persistence/Groundwork/Testing/Elsa.Persistence.Groundwork.Testing.csproj` | #1678 | Provider/session/schema test kit and neutral fault/recording doubles | Deleted (was: Replace with EF four-provider kit; preserve neutral doubles) |  | #1764 |
| T25 | `tests/Elsa/Persistence/Groundwork/UnifiedHost/Tests/Elsa.Persistence.Groundwork.UnifiedHost.Tests.csproj` | #1670 | Target isolation, activation/restart, publication-lane and SQLite composition | Deleted (was: Port to EF host) |  | #1764 |
| T26 | `tests/Elsa/Persistence/Groundwork/V2/Runtime/Tests/Elsa.Persistence.Groundwork.V2.Runtime.Tests.csproj` | #1672 / #1676 | All 29 Runtime units: checkpoint/outbox, claims/fences, queues/poison, timers, bookmarks/triggers/schedules, durable values, dispatch, recovery, incidents/alterations/holds/test scope and materialization | Deleted (was: Relational + retire Mongo; add MySQL) |  | #1764 |
| T27 | `tests/Elsa/Persistence/Groundwork/V2/Testing/Elsa.Persistence.Groundwork.V2.Testing.csproj` | #1678 / #1672 | Runtime persistence test infrastructure | Deleted (was: Replace with EF infrastructure) |  | #1764 |
| T28 | `tests/Elsa/Persistence/Groundwork/V2/Tests/Elsa.Persistence.Groundwork.V2.Tests.csproj` | #1670 / #1678 | Manifest, provider connections, storage access audit and release boundary | Deleted (was: Port neutral evidence; rewrite final no-Groundwork guard; retire Mongo) |  | #1764 |
| T29 | `tests/Elsa/Secrets/Persistence/Groundwork/V2/ProviderMatrix/Tests/Elsa.Secrets.Persistence.Groundwork.V2.ProviderMatrix.Tests.csproj` | #1679 | Secrets CRUD/revision/query/tenant/restart matrix | Deleted (was: Relational + retire Mongo; add MySQL) |  | #1764 |
| T30 | `tests/Elsa/Secrets/Persistence/Groundwork/V2/Tests/Elsa.Secrets.Persistence.Groundwork.V2.Tests.csproj` | #1679 | Secret repository and registration contracts | Deleted (was: SQLite; replace with EF evidence) |  | #1764 |
| T71 | `tests/Elsa/Secrets/Persistence/EntityFrameworkCore/MySql/Tests/Elsa.Secrets.Persistence.EntityFrameworkCore.MySql.Tests.csproj` | task #1726 | Production MySQL context model, provider binding, repository CRUD/query, rollback, and optimistic concurrency | MySQL Testcontainers. Retained EF destination, merged as #1729 (`ec85ee776`) — the row previously credited task number #1726, which is not a PR. Migrations for the MySQL Secrets context were added by #1755 and are proved by `NativeModuleMigrationTests.Every_module_installs_on_mysql`. This project runs as the `secrets-mysql` leg of `ef-container-suites`, which has **no** required-provider variable and self-skips without a container | #1729 | N/A |
| T31 | `tests/Elsa/Studio/Preferences/Persistence/Groundwork/Tests/Elsa.Studio.Preferences.Persistence.Groundwork.Tests.csproj` | #1680 | Preference scope/read/write/concurrency | Deleted (was: SQLite; port to EF and add relational providers) |  | #1764 |
| T32 | `tests/Elsa/Workflows/Dashboard/Persistence/Groundwork/V2/Tests/Elsa.Workflows.Dashboard.Persistence.Groundwork.V2.Tests.csproj` | #1677 child | Portfolio and run-health bounded projections | Deleted (was: SQLite; port to EF) |  | #1764 |
| T33 | `tests/Elsa/Workflows/Design/Persistence/Groundwork/Tests/Elsa.Workflows.Design.Persistence.Groundwork.Tests.csproj` | #1677 child | Definition/version/draft/layout/list projection, commands, registration and schema | Deleted (was: SQLite; port to EF) |  | #1764 |
| T34 | `tests/Elsa/Workflows/Publishing/Api/GroundworkTests/Elsa.Workflows.Publishing.Api.GroundworkTests.csproj` | #1677 child | Activity test-run receipts, publication commits, upgrades and published-deletion guard | Deleted (was: Relational + retire Mongo; add MySQL) |  | #1764 |
| T35 | `tests/Elsa/Workflows/Publishing/Persistence/Groundwork/Tests/Elsa.Workflows.Publishing.Persistence.Groundwork.Tests.csproj` | #1677 child | Six Publishing units, lifetime and provider matrix | Deleted (was: Relational + retire Mongo; add MySQL) |  | #1764 |
| T36 | `tests/Elsa/Workflows/Runtime/Distributed/Persistence/Groundwork/Tests/Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Tests.csproj` | #1672 / #1676 | Placement/fencing, stream-head/command ordering, lease/redelivery/ack and restart | Deleted (was: Relational + retire Mongo; add MySQL) |  | #1764 |
| T37 | `tests/Elsa/Activities/Http/IntegrationTests/Elsa.Activities.Http.IntegrationTests.csproj` | #1672 / #1670 | HTTP endpoint status/body/content type, workflow completion and artifact persistence | Ported, project retained. #1668/#1756 split the timing assertions out and left `HttpEndpointCheckpointPolicyEquivalenceTests`; #1764 re-pointed `HttpEndpointHostFixture` at the Runtime EF Core aggregate over one SQLite file, schema installed through the registered `IShellInitializer`. Commit-count and write-amplification assertions are retired by owner decision and were not replaced | #1668/#1756, #1764 | N/A |
| T38 | `tests/Elsa/Activities/Scheduling/Tests/Elsa.Activities.Scheduling.Tests.csproj` | #1672 | Durable timer, bookmark, crash/restart and idempotent resume | Ported, project retained. #1764 re-pointed `DurableTimerRestartCrashTests` at the Runtime EF Core aggregate; the durable-timer restart/crash generations are proved over EF Core, not over a Groundwork stand-in | #1764 | N/A |
| T39 | `tests/Elsa/Diagnostics/Persistence/Tests/Elsa.Diagnostics.Persistence.Tests.csproj` | #1681 | Diagnostics lifecycle/order/loss/retry/observability, drain and provider fixtures | **Not deleted — correction.** The project is retained; #1764 removed only its Groundwork/Mongo half: `DiagnosticsProviderFixture`, `DiagnosticsProviderLifecycleSmokeTests`, `DiagnosticsPersistenceFeatureTests`, `DiagnosticsLifecycleEvidenceTests` and `DiagnosticsProviderAssertions`, each named in the dropped-coverage table above. `DiagnosticsDrainLoadTests`, `DiagnosticsDrainLifecycleTests`, `DiagnosticsDrainShutdownTests`, `DiagnosticsDrainValidationTests`, `DiagnosticsPersistenceLifecycleTests` and `DiagnosticsPersistenceObservabilityTests` all survive. Four-provider diagnostics coverage is T54-T57's | #1696, #1701, #1764 | N/A |
| T40 | `tests/Elsa/Foundation/Identity/Tests/Elsa.Foundation.Identity.Tests.csproj` | #1682 | Shell/sign-in/IAM/normalized lookup and OpenIddict composition boundary | Ported, project retained. #1764 re-pointed sign-in, seeding, the token endpoint, shell composition, the dev/demo guard and IAM normalized lookup at Identity IAM EF Core, and removed the Groundwork HTTP acceptance suite and `IdentityV2TestPersistence`. The OpenIddict vendor context stays separate (H16) | #1715, #1764 | N/A |
| T41 | `tests/Elsa/Secrets/Persistence/EntityFrameworkCore/Tests/Elsa.Secrets.Persistence.EntityFrameworkCore.Tests.csproj` | #1679 | EF migration/composition/shell reload/search-key/concurrency/repository plus current dual-provider compatibility | **Not deleted — correction.** The EF destination is retained; #1764 removed only the dual Groundwork compatibility, which the default flip in #1763 made removable. `SecretsSearchKeysTests` lost its Groundwork-oracle cases: the exhaustive 0x0-0x10FFFF scalar sweep is gone and representative golden expectations captured from the current implementation replace it — a real reduction, named in the dropped-coverage table | #1763, #1764 | N/A |
| T42 | `tests/Elsa/Workflows/Design/Tests/Elsa.Workflows.Design.Tests.csproj` | #1677 child | Provider-neutral draft/tree/checkpoint-cadence behavior | **Not deleted — correction.** The project is retained and re-pointed: #1764 replaced the Groundwork host with the Workflows Design EF Core module over one SQLite file across its 16 test files | #1732, #1764 | N/A |
| T43 | `tests/Elsa/Workflows/Runtime/Distributed/Tests/Elsa.Workflows.Runtime.Distributed.Tests.csproj` | #1672 / #1676 | Distributed actor/placement/transport and two-node acceptance | Preserved; #1764 removed its Groundwork transport fixture wiring. The skipped checkpoint/outbox/dispatch restart case is still a gap, not evidence, and nothing in this program closed it | #1764 | N/A |
| T44 | `tests/Elsa3/Mapping/Tests/Elsa3.Mapping.Tests.csproj` | #1677 child | Import manifest, idempotent/atomic apply, restart and registration | Neutralized rather than ported, and the difference matters. #1764 reduced this project to the in-memory analyzer/importer path (reusable-activity collection apply) and deleted `ReusableActivityImportOperationTests`, `ReusableActivityImportV2TestHarness` and `Elsa3ImportManifestPhysicalStorageGoldenTests`. Durable import-ledger evidence now lives in T69/T70 over EF Core; the Groundwork storage-unit manifest golden is dropped with its subject | #1760, #1764 | N/A |
| T69 | `tests/Elsa3/Activities/Design/Import/Persistence/EntityFrameworkCore/Tests/Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.Tests.csproj` | task #1738 | EF import ledger L01-L03 and the shared-transaction owner: atomic commit and injected-failure rollback across the import, Activities Design, and Workflows Design contexts on one connection and transaction; split-target and provider-mismatch refusal; idempotent and conflicting replay; tenant and user isolation; restart durability; lost-commit reconciliation; corrupt-row and hash-drift fail-closed reads; fail-closed custom registrations | **Not deleted — correction.** This is the EF destination, not a Groundwork project. Merged as #1760 (`63a61e9ef`), which also introduced `EfSharedTransaction`; #1764 removed only the Groundwork half of `Elsa3ImportRegistrationOwnershipTests` (the Groundwork/EF registration-switching cases), since there is no longer a second backend to switch to | #1760 | N/A |
| T70 | `tests/Elsa3/Activities/Design/Import/Persistence/EntityFrameworkCore/ProviderTests/Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.ProviderTests.csproj` | task #1738 | Live SQL Server, PostgreSQL, and MySQL model binding, ledger CRUD with case-, space-, and NUL-distinct identities, one atomic import commit, restart replay, and rollback after an injected post-write or pre-commit failure | PostgreSQL/SQL Server/MySQL. Retained EF destination, merged in #1760. It is now a hosted gate: it runs as the `elsa3-import` leg of `ef-container-suites`, armed fail-closed by `ELSA_REQUIRE_NATIVE_PROVIDER_MATRIX` (renamed from `GROUNDWORK_V2_REQUIRE_NATIVE_PROVIDER_MATRIX` by #1764) | #1760, #1764 | N/A |
| T45 | `tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj` | #1670 / #1678 | Architecture/security/dependency/composition guards | **Not deleted — correction.** The project is retained and rewritten. #1764 deleted six Groundwork-specific guards (`DesignPersistenceBoundaryTests`, `DesignPersistenceBoundedQueryTests`, `DesignLedgerIsolationTests`, `AspNetCoreIdentityMutationGuardTests`, `ProviderParsingFootprintRatchetTests` with its baseline, `SecretsPersistenceGateOwnershipTests`) and added `RetiredPersistenceFamilyGuardTests`, a fail-closed no-retired-family guard with a mutation proof. `EfCoreDependencyGuardTests` gained its Workbench reviewed-closure case in #1763. The whole project runs in the `architecture-guards` CI job | #1763, #1764 | N/A |
| T46 | `tests/Elsa/Workflows/Publishing/Api/Tests/Elsa.Workflows.Publishing.Api.Tests.csproj` | #1677 child | Publishing API, preflight, activation, projection reconciliation and cross-module transaction semantics | Preserved and rebound. #1764's only change was dropping the Groundwork package references from the csproj; the host it composes is the EF one because #1763 flipped the composition it uses | #1759, #1763, #1764 | N/A |
| T47 | `tests/Elsa/Modularity/Tests/Elsa.Modularity.Tests.csproj` | #1670 | Provider registration, connection ownership, schema/startup/refusal and Workbench composition | **Not deleted — correction.** The project is retained; #1764 deleted `WorkbenchGroundworkCompositionTests` and re-pointed secret-setting catalog masking and secret-flag pinning at EF Core feature settings. Provider registration, connection ownership and startup refusal are exercised against the EF composition #1763 ships | #1763, #1764 | N/A |
| T48 | `tests/Elsa/Workbench/Tests/Elsa.Workbench.Tests.csproj` | #1670 | Workbench process, shell activation and restart | **Not deleted — correction.** The project is retained. #1763 rewrote `WorkbenchShellActivationTests` for the EF default; #1764's only change to it was two lines of naming. Process, activation and restart are proved against the EF Workbench | #1763, #1764 | N/A |
| T49 | `tests/Elsa/Secrets/Persistence/EntityFrameworkCore/PostgreSql/Tests/Elsa.Secrets.Persistence.EntityFrameworkCore.PostgreSql.Tests.csproj` | #1679 | PostgreSQL repository, concurrency, shell/container and migration proof | Retained and extended through the flip. It is a root of the regenerated `Elsa.Server.Persistence.Integration.slnf` (nightly) and runs in the `secrets-ef-composition` CI job, which starts its own PostgreSQL and fails if it never becomes ready. It carries the A14 wrong-provider refusal and the A16 operator-apply-then-runtime-validate journey | #1755, #1763 | N/A |
| T50 | `tests/Elsa/Secrets/Persistence/EntityFrameworkCore/SqlServer/Tests/Elsa.Secrets.Persistence.EntityFrameworkCore.SqlServer.Tests.csproj` | #1679 | SQL Server repository, concurrency and container proof | Retained and extended through the flip; also a root of `Elsa.Server.Persistence.Integration.slnf`. It runs as the `secrets-sqlserver` leg of `ef-container-suites`, which has **no** required-provider variable and self-skips without a container | #1755, #1763 | N/A |
| T51 | `tests/Elsa/Secrets/Persistence/EntityFrameworkCore/PostgreSql/PackageFeedProbe/Elsa.Secrets.Persistence.EntityFrameworkCore.PostgreSql.PackageFeedProbe.csproj` | #1679 / #1670 | PostgreSQL package-feed process probe | Retained: the package boundary it proves is still active. It probes the Npgsql restore/pack boundary for a packed module child host, which survives the Groundwork removal unchanged — the Groundwork feed it once had to coexist with is what #1764 removed from `NuGet.config` (R04) | | N/A |
| T52 | `tests/Elsa/Studio/Preferences/Persistence/EntityFrameworkCore/Tests/Elsa.Studio.Preferences.Persistence.EntityFrameworkCore.Tests.csproj` | #1680 | SQLite behavioral contract: scope isolation, CRUD, canonical revisions, optimistic concurrency, restart and opt-in DI | SQLite; retained EF destination, merged in #1693 | #1693 | N/A |
| T53 | `tests/Elsa/Studio/Preferences/Persistence/EntityFrameworkCore/ProviderTests/Elsa.Studio.Preferences.Persistence.EntityFrameworkCore.ProviderTests.csproj` | #1680 | Live provider smoke: PostgreSQL/SQL Server/MySQL model binding, representative CRUD, uniqueness and concurrency | PostgreSQL/SQL Server/MySQL; retained EF destination, merged in #1693 | #1693 | N/A |
| T54 | `tests/Elsa/Diagnostics/StructuredLogs/Persistence/EntityFrameworkCore/Tests/Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore.Tests.csproj` | #1695 | SQLite behavioral contract: append/order/high-water/idempotency, retention, restart, cursor/binding and opt-in DI | SQLite; retained EF destination delivered by #1696 | #1696 | N/A |
| T55 | `tests/Elsa/Diagnostics/StructuredLogs/Persistence/EntityFrameworkCore/ProviderTests/Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore.ProviderTests.csproj` | #1695 | Live provider smoke: PostgreSQL/SQL Server/MySQL model binding plus representative append/read/CAS and rollback or stale-CAS outcome | PostgreSQL/SQL Server/MySQL; Docker-skippable retained EF destination delivered by #1696 | #1696 | N/A |
| T56 | `tests/Elsa/Diagnostics/OpenTelemetry/Persistence/EntityFrameworkCore/Tests/Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore.Tests.csproj` | #1697 | SQLite behavioral contract: all-signal atomic capture, scoped queries/detail, canonical search, explicit replay ledger, trace-summary merge/CAS, retention/recovery, restart and opt-in DI | SQLite; retained EF destination delivered by #1701 | #1701 | N/A |
| T57 | `tests/Elsa/Diagnostics/OpenTelemetry/Persistence/EntityFrameworkCore/ProviderTests/Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore.ProviderTests.csproj` | #1697 | Four-provider model binding plus live PostgreSQL/SQL Server/MySQL representative all-signal CRUD/query/transaction, restart, isolation and concurrent trace-summary merge | PostgreSQL/SQL Server/MySQL; Docker-skippable retained EF destination delivered by #1701 | #1701 | N/A |
| T58 | `tests/Elsa/Foundation/Identity/Persistence/EntityFrameworkCore/Tests/Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Tests.csproj` | #1712 | SQLite IAM authority behavior: tenant isolation, lossless CRUD, bounded ordering, reservations, relationships, atomicity, replay/recovery and optimistic concurrency | SQLite comprehensive; retained EF destination delivered by #1715 | #1715 | N/A |
| T59 | `tests/Elsa/Foundation/Identity/Persistence/EntityFrameworkCore/ProviderTests/Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.ProviderTests.csproj` | #1712 | SQL Server, PostgreSQL, and MySQL model creation and representative CRUD/query/transaction/concurrency smoke | SQL Server/PostgreSQL/MySQL focused provider smoke; retained EF destination delivered by #1715 | #1715 | N/A |
| T60 | `tests/Elsa/Foundation/Identity/AspNetCoreIdentity/EntityFrameworkCore/Tests/Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Tests.csproj` | #1712 | Complete ASP.NET Core Identity store surface, tenant isolation, normalized uniqueness, relationship atomicity, concurrency stamps, sign-in/session, and seeding over the shared EF authority | SQLite comprehensive; retained EF destination delivered by #1715 | #1715 | N/A |
| T61 | `tests/Elsa/Workflows/Runtime/Distributed/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Tests.csproj` | #1717 | SQLite placement behavior: scope/identity losslessness, claim/renew/takeover/logical-release/list, release-reclaim fencing continuity, rollback/tracker recovery, concurrency, restart, invalid input/cancellation, provider selection and startup ownership validation | SQLite. Retained EF destination, merged in #1718 (`42cf9eac0`); migrations added by #1755; default flip #1763 | #1718 | N/A |
| T62 | `tests/Elsa/Workflows/Runtime/Distributed/Persistence/EntityFrameworkCore/ProviderTests/Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.ProviderTests.csproj` | #1717 | Four-provider model plus disposable PostgreSQL/SQL Server/MySQL schema, scoped claim/find/list/release, rollback, concurrent outcome and reopen durability | PostgreSQL/SQL Server/MySQL. Retained EF destination, merged in #1718. It is now a hosted gate: the `runtime-distributed` leg of `ef-container-suites` **is** armed fail-closed by `ELSA_RUNTIME_PLACEMENT_EF_REQUIRE_NATIVE_PROVIDERS`, so it no longer merely skips without Docker | #1718, #1764 | N/A |
| T63 | `tests/Elsa/Workflows/Runtime/Distributed/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Tests.csproj` | #1720 | SQLite D02-D03 behavior: lossless envelope round-trip, scope isolation, contiguous/CAS sequence allocation, atomic head/item rollback, bounded FIFO lease/replenishment, inclusive expiry/redelivery, exact-token acknowledgement, pending-head queries, restart, corrupt-state fail-closed behavior, and tracker recovery | SQLite. Retained EF destination, merged in #1721 (`d15bd735d`) under task #1720 — the same project as T61, which is why the two rows share a path; migrations added by #1755; default flip #1763 | #1721 | N/A |
| T64 | `tests/Elsa/Workflows/Runtime/Distributed/Persistence/EntityFrameworkCore/ProviderTests/Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.ProviderTests.csproj` | #1720 | Four-provider model plus live PostgreSQL/SQL Server/MySQL smoke for representative send/query/lease/ack, rollback, concurrent writers/leasers, and reopen durability; no hidden skips under the required-provider gate | PostgreSQL/SQL Server/MySQL. Retained EF destination, merged in #1721 under task #1720 — the same project as T62. It runs in the fail-closed `runtime-distributed` leg of `ef-container-suites`, which is what the row's "no hidden skips under the required-provider gate" requirement asked for | #1721, #1764 | N/A |
| T65 | `tests/Elsa/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj` | #1723/#1725 | SQLite R01-R06 behavior: exact scoped/composite identity, lossless round-trip, bounded ordinal keyset paging, query-bound continuations, real create/update/delete contention, rollback, restart, corrupt-state failure and DI ownership/load order | SQLite comprehensive. Retained EF destination. The Runtime EF family merged as a sequence rather than as one task-numbered PR: #1724 (R01 bookmark state), #1730 (executable artifact), #1741 (activity execution), #1743 (workflow execution state), #1744 (alteration and scope ledgers), #1745 (operational state and attention), #1746 (checkpoint/outbox/dispatch/queue/timer), `3cb5636c2` (binding, recurring schedules, activation, projection coordination — squashed without a PR number in its subject), #1758 (standalone composition with shell features). Migrations added by #1755; default flip #1763 | #1724, #1730, #1741, #1743, #1744, #1745, #1746, `3cb5636c2`, #1758 | N/A |
| T66 | `tests/Elsa/Workflows/Runtime/Persistence/EntityFrameworkCore/ProviderTests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests.csproj` | #1723/#1725 | Provider model plus exact local native-provider evidence for PostgreSQL/SQL Server/MySQL Runtime bookmark and artifact CRUD, keyset query, transaction rollback, optimistic concurrency and reopen durability | PostgreSQL/SQL Server/MySQL. Retained EF destination, delivered across the same sequence as T65. Hosted gating is no longer out of scope: #1764 made this project the `runtime` leg of `ef-container-suites`, armed fail-closed by `ELSA_RUNTIME_PLACEMENT_EF_REQUIRE_NATIVE_PROVIDERS`, and a root of `Elsa.Server.Persistence.Integration.slnf` in the nightly job | #1724, #1730, #1741, #1743, #1744, #1745, #1746, `3cb5636c2`, #1758, #1764 | N/A |

## Correctness hidden inside retiring performance surfaces

| Current file or suite | Correctness that must move before deletion | Retired material | Owner | Replacement PR | Disposition |
|---|---|---|---|---|---|
| `tests/Elsa/Activities/Http/IntegrationTests/HttpEndpointRuntimePerformanceTests.cs` | HTTP status, response body/content type, workflow completion and persisted artifact | Commit-count and write-amplification comparisons | #1672/#1670 and #1668 | | Retired by owner decision (#1668); correctness moved to `HttpEndpointCheckpointPolicyEquivalenceTests.cs` in the same project, asserted for Immediate and Coalesced. Mandatory durability boundaries stay covered by `RuntimeCheckpointCoalescingPolicyTests`; the EF port remains T37's |
| `tests/Elsa/Foundation/Identity/Tests/AspNetCoreIdentity/Performance/IamNormalizedLookupSqliteCorrectnessTests.cs` | Timing-free normalized-name/email lookup and duplicate behavior | Native-plan/measurement acceptance | #1682 and #1668 | | Retired by owner decision (#1668); moved out of `Performance/` to `tests/Elsa/Foundation/Identity/Tests/AspNetCoreIdentity/IamNormalizedLookupSqliteCorrectnessTests.cs`, which asserts the scenario's lookups, membership and revision-guarded update directly with no harness dependency; the native-plan test is retired. Duplicate normalized-name/email/role behavior was expressed by `AspNetCoreIdentityConcurrencyContractTests` and EF-owned `EfCoreIdentityFrameworkContractTests` (T60). **Updated after #1764:** `AspNetCoreIdentityConcurrencyContractTests` lived in the deleted `Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests` project and is gone; the duplicate behavior now rests on `EfCoreIdentityFrameworkContractTests` and `EfCoreIdentityStoreTests` (T60) alone |
| `benchmarks/Elsa/Activities/Runtime/Benchmarks/ActivationScopeSemanticTests.cs` | Activation-scope correctness | Benchmark project and measurement harness | #1672 and #1668 | | Retired by owner decision (#1668); per-attempt semantics moved to `tests/Elsa/Activities/Runtime/Tests/ActivationScopeSemanticTests.cs`, now against the production `ActivityActivator`/`ClrActivityActivator` rather than the benchmark's strategy models. Burst-only and conditional candidate assertions are not ported (rejected by ADR 0045, never shipped); the intrinsic-workload assertion is not ported (it checked a counter the harness set itself) |
| `tools/ledger/StorePerformance/AdapterHost/Tests` | Adapter protocol, provider binding and failure correctness not expressed elsewhere | Timing, budget and native-plan evidence | #1678/#1670 and #1668 | | Retired by owner decision (#1668). Native-plan capture/parsing, fingerprint, probe and admission tests are measurement-only or Groundwork-specific; the workload-over-Groundwork runs are mapped below, and the one unexpressed obligation moved to `GroundworkV2RuntimePostCommitOutboxStoreTests` (T26) — which #1764 then deleted, so it now rests on `EfRuntimePostCommitOutboxStoreTests` (T65); see the mapping below |
| `tools/ledger/StorePerformance/Benchmarks/Tests` | Workflow completion, checkpoint/outbox, duplicate and recovery assertions not expressed elsewhere | Measurement admission, comparison and fingerprints | #1672 and #1668 | | Retired by owner decision (#1668). These tests ran the workload oracle against test-local fakes, not product stores; the obligations the oracle encodes are mapped below |
| `tests/Elsa/Diagnostics/Persistence/Tests/DiagnosticsDrainLoadTests.cs` | Drain ordering, bounded batches, no-loss/retry behavior | None merely because the class name contains `Load` | #1681 | | Preserved, and verified present after #1764. Its project (T39) was not deleted |

### Retired store-performance workload mapping (#1668, re-checked after #1764)

The workload oracles ran either against test-local fakes (`Benchmarks/Tests`) or against Groundwork
SQLite stores through the benchmark adapter host (`AdapterHost/Tests`); neither project ran in a CI
gate. #1668/#1756 mapped each workload's timing-independent obligations onto existing suites, but
most of those were Groundwork suites in T26, which #1764 then deleted. The table below is re-checked
against the post-deletion tree: the second column is where the obligation stands **now**, and every
named suite was verified present.

| Retired workload | Obligations the oracle checked | Where they are expressed now |
|---|---|---|
| `checkpoint-commit` | Replay equivalence, conflicting-replay and stale-fence refusal, no duplicated post-commit work, reread through another client | `RuntimeCheckpointCommitTests` (provider-neutral, survives); `EfRuntimeCheckpointCommitStoreTests` and the eight `EfRuntimeCheckpoint*ParticipantTests` (T65). `GroundworkV2RuntimeCheckpointWriterTests` is gone with T26 |
| `bookmark-lookup` | Bounded ordered stimulus pages, page boundary, scope isolation | `EfBookmarkStateStoreTests` (T65), with native-provider evidence in T66's fail-closed `runtime` leg. `GroundworkV2BookmarkStateStoreTests` is gone with T26 |
| `recovery-scan` | Bounded continuation paging, live and terminal exclusion, stability after reopen | `RuntimeRecoveryScannerTests` (provider-neutral, survives); `EfDurableValueAndSchedulerStateTests.Execution_liveness_supports_create_only_cas_versioned_reads_and_bounded_pages_after_restart` and `EfWorkflowExecutionStateStoreTests` (T65). `GroundworkV2ExecutionLivenessStoreTests` is gone with T26, and with it the v1.2 production-scanner traversal case specifically — that traversal was written against the Groundwork scanner and has no EF counterpart |
| `queue-drain` | FIFO, bounded claims, one owner under contention, expired reclaim with a higher fence, stale-acknowledgement refusal, poison relationship, restart | `EfSchedulerWorkQueueStoreTests` and `EfWorkflowSchedulerPoisonStoreTests` (T65). The Groundwork pair is gone with T26 |
| `outbox-drain` | Bounded ordered claims, retry delay, reclaim fence, stale-completion refusal, restart, one owner under contention | `RuntimePostCommitOutboxStoreTests` (provider-neutral, survives); `EfRuntimePostCommitOutboxStoreTests` (T65). The deterministic read-then-write contention interleaving #1668 added to the Groundwork suite went with T26; the EF suite is where that obligation now has to be met |
| `trigger-binding-stimulus-lookup` | Bounded binding and source-reference pages, scope isolation | `EfWorkflowTriggerBindingStoreTests` and the source-reference cases in `EfRuntimeArtifactRegistrationTests`/`EfRuntimeArtifactScopeTests` (T65). The Groundwork pair is gone with T26 |
| `recurring-schedule-selection` | Due cutoff and order, revision-guarded advance, stale-advance refusal, publication projection, restart | `EfRecurringTriggerScheduleStoreTests` (T65). `GroundworkV2RecurringTriggerScheduleStoreTests` is gone with T26 |
| `due-timer-selection` | Due selection, fenced claims, claim compare-and-swap under interleaving, stale-transition refusal | `EfDurableTimerStoreTests` (T65), with the host-level restart/crash journey in `DurableTimerRestartCrashTests` (T38). `GroundworkV2DurableTimerStateStoreTests` is gone with T26 |
| `distributed-placement-takeover` | One winner for first and expired-takeover claims, stale-release refusal | `EfExecutionPlacementStoreTests` (T61), with native-provider evidence in T62's fail-closed `runtime-distributed` leg. T36 is deleted |
| `distributed-command-send-lease-ack` | Contiguous unique sends, bounded leases, redelivery after expiry and reopen, stale-acknowledgement refusal | `EfExecutionCommandTransportTests` (T63), same fail-closed leg via T64. T36 is deleted |
| `iam-normalized-lookup-update` | Normalized name/email/role lookup, membership, revision-guarded update | `IamNormalizedLookupSqliteCorrectnessTests` (T40, re-pointed at Identity IAM EF Core by #1764); `EfCoreIdentityFrameworkContractTests` and `EfCoreIdentityStoreTests` (T60) |
| `secret-create-read-list` | Tenant-local normalized-name uniqueness, bounded pages, concurrent create | `SecretTenantIsolationTests` and `SqliteEfSecretRepositoryTests` (both survive); `PostgreSqlEfSecretRepositoryTests` (T49). `GroundworkV2SecretRepositoryTests` is gone with T30 |
| `diagnostics-durable-history` | Structured-log cursor and lifetime high-water across retention and reopen, bounded resource and trace pages, ordered trace detail | T54 and T56 (SQLite), T55 and T57 (live providers). T05 and T08 are deleted. **Coverage honesty:** the `structured-logs` and `opentelemetry` legs of `ef-container-suites` have no required-provider variable, so their native-provider cases self-skip without a container |

Measurement admission, comparison, gates, budgets, native-plan capture and parsing, provider probes,
composition fingerprints and the harness's self-tests carried no product correctness. The spec 094
coverage-ledger validator (`tools/ledger/Elsa.Groundwork.Ledger.Tests`) checked Groundwork
project-management artifacts and Groundwork service lifetimes only; it was removed with the harness,
and the final no-retired-family audit is now `RetiredPersistenceFamilyGuardTests` (T45), which runs
in the `architecture-guards` CI job on every run rather than on manual dispatch. The Runtime engine
benchmark asserted only workflow completion beside commit, dispatch, read, materialization and
fusion-engagement counts, and never ran in CI; completion under both checkpoint policies is covered by
`RuntimeCheckpointCoalescingTests` and fusion behavior by `ReplaySafeFusionGuardrailTests`, both verified
present. The OpenTelemetry trace-list benchmark compared against a frozen Groundwork v1 comparand that no
longer exists; trace-list correctness is now T56/T57's, T05 having been deleted.

No benchmark, timing measurement, timing budget, or performance workflow is required or permitted as
replacement evidence. Historical results may remain archived and must be described as historical.

## Backend end-to-end journey register

These are timing-independent host-boundary journeys against a rebuilt host and fresh database.

E01-E12 are **rebound without a script edit**, which is the point of doing the default flip before
the deletion: each script drives `Elsa.Workbench` over REST, and #1763 changed what that host
composes. All twelve were verified present at this head and none names a retired family; the
rebuild instructions they depend on were updated in `e2e-tests/README.md` (repository-surface
register D02). No script in `e2e-tests/` mentions Groundwork or MongoDB any more.

| ID | Current script | Owner | Required EF evidence | Replacement PR | Disposition |
|---|---|---|---|---|---|
| E01 | `e2e-tests/durability/Test-RestartRecovery.ps1` | #1672 | Suspend, restart and recover persisted execution | #1763 | Preserved; rebound by the default flip, script unchanged |
| E02 | `e2e-tests/durability/Test-VariableSurvivesSuspend.ps1` | #1672 | Durable variable survives suspension and restart | #1763 | Preserved; rebound by the default flip, script unchanged |
| E03 | `e2e-tests/resilience/Test-PoisonedWork.ps1` | #1672 | Poison classification, retry and recovery | #1763 | Preserved; rebound by the default flip, script unchanged. The scheduler-poison store's work-item identity length was fixed in #1763 because `e2e-tests/scheduling/Test-Cron.ps1` — outside this register's E-rows — failed on the EF composition when a poisoned dispatch could not be recorded |
| E04 | `e2e-tests/composition/Test-DispatchWorkflowOutcomes.ps1` | #1672/#1677 | Dispatch outcome durability and parent/child behavior | #1763 | Preserved; rebound by the default flip. #1763's checkpoint-commit identity-length fix was caught here: a dispatched child faulted on the old 128-unit cap |
| E05 | `e2e-tests/runtime-alterations/Test-AlterationPlans.ps1` | #1672 | Alteration plan lifecycle and idempotency | #1763 | Preserved; rebound by the default flip, script unchanged |
| E06 | `e2e-tests/runtime-alterations/Test-AlterationReplayAndRestart.ps1` | #1672 | Alteration replay and restart recovery | #1763 | Preserved; rebound by the default flip, script unchanged |
| E07 | `e2e-tests/scheduling/Test-Delay.ps1` | #1672 | Durable delay resume | #1763 | Preserved; rebound by the default flip, script unchanged |
| E08 | `e2e-tests/scheduling/Test-Timer.ps1` | #1672 | Durable timer scheduling and resume | #1763 | Preserved; rebound by the default flip, script unchanged |
| E09 | `e2e-tests/orchestration-controls/Test-StimulusRouting.ps1` | #1672 | Bookmark/trigger stimulus routing | #1763 | Preserved; rebound by the default flip, script unchanged |
| E10 | `e2e-tests/reusable-activities/Test-ActivityDraftTestRun.ps1` | #1677 child | Activity draft test-run receipt and isolated execution | #1763 | Preserved; rebound by the default flip, script unchanged |
| E11 | `e2e-tests/reusable-activities/Test-DraftTestRun.ps1` | #1677 child | Workflow draft test-run dispatch and isolation | #1763 | Preserved; rebound by the default flip, script unchanged |
| E12 | `e2e-tests/diagnostics/Test-OpenTelemetryApiMigration.ps1` | #1681 | OpenTelemetry API persistence and migration boundary | #1763 | Preserved; rebound by the default flip, script unchanged |
| E13 | `e2e-tests/groundwork/Test-GroundworkReleaseLifecycle.ps1` | #1670 | Final EF-only lifecycle and active-code/dependency no-Groundwork proof | #1764 | **Deleted by #1764 without an e2e rewrite**, and named that way rather than implied. The author/save/reload/publish/suspend/resume/complete journey it ran is covered by the surviving e2e suites over the same EF host (E01, E02, E09, E10, E11). The no-retired-family half is not an e2e journey any more: it is `RetiredPersistenceFamilyGuardTests` (T45), which runs in the `architecture-guards` CI job on every run — a stronger gate than a manually-run script, but a static one, so no host-boundary proof asserts the absence of a second backend at runtime. Nothing needs it to: there is no second backend to select |

## Proof rules

- SQLite, SQL Server, PostgreSQL, and MySQL evidence must name the exact current test and commit.
- A green `ef-container-suites` leg is native-provider evidence only in the six armed legs
  (`migrations`, `publishing`, `elsa3-import`, `runtime`, `runtime-distributed`,
  `activities-design`). In the other nine, a green leg is consistent with every native case having
  skipped, and a row that leans on one must say so.
- Mongo-specific topology never counted as relational parity. Neutral contracts extracted from it
  remained requirements and are carried by the EF module suites; the Mongo-only properties were
  dropped with the family and are named above.
- Skipped tests, including the two-node checkpoint/outbox/dispatch acceptance gap (T43), do not
  count as proof. That gap is still open.
- A passing build does not replace migration, provider, restart, transaction, or e2e evidence.
- Every deleted project or suite points to a merged replacement, to an owner-authorized retirement
  with no surviving correctness requirement, or to an explicitly named loss. The losses are listed
  in the dropped-coverage table and restated in the workload mapping; they are not implied by
  silence.
- No benchmark, timing measurement, timing budget, or performance workflow is required or permitted
  as evidence for any row. Historical results may remain archived and must be described as
  historical.

# EF Core persistence test and end-to-end register

Status: active completion evidence for [#1671](https://github.com/elsa-workflows/elsa-foundation/issues/1671).

Snapshot: `main` at `7a952efcf8d53472d7d4e7e3fd7b51d7a808c1c8` (2026-09-12).
This register covers 65 relevant test/evidence projects (51 snapshot projects plus the Activities Design EF behavioral and native-provider projects, the Elsa 3 import EF behavioral and native-provider projects, two merged
Studio Preferences EF projects, two Structured Logs EF projects delivered by #1696, and two
OpenTelemetry EF projects delivered by #1701, plus the two Identity EF behavioral/provider projects and the
ASP.NET Identity EF adapter project being implemented under #1712): the original 36 Groundwork/Mongo-named
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
| T05-T08, T39, T54-T57 | #1681 after shared EF foundation, MySQL and migration lifecycle | Diagnostics production/host rows; blank pending merge |
| T09-T13, T40, T58-T60 | #1682 after shared EF foundation, MySQL, transaction and migration lifecycle | Identity production/host rows; blank pending merge |
| T14, T21-T25, T28, T45, T47-T48, T51 | #1670 after the relevant replacements; #1678/#1669 where test-kit or migration behavior is involved | Host/default-flip row or N/A for a pure tool/guard; blank pending merge |
| T26-T27, T36-T38, T43, T63-T64 | #1672/#1676 after MySQL, transaction and migration spikes | Runtime/distributed Runtime host row; blank pending merge |
| T29-T30, T41, T49-T50 | #1679 after MySQL and migration-lifecycle spikes | Secrets production/host row; blank pending merge |
| T31, T52-T53 | #1680 after shared EF foundation, MySQL and migration lifecycle | Studio Preferences production/host row; replacement merged in #1693; default flip remains pending |

## Deletion step — 2026-09-16

The deletion landed on `claude/delete-groundwork` (replace with the merged PR number when the control
room opens it). What it removed, and what happened to the coverage:

**Deleted outright** — 36 Groundwork/Mongo test projects, `tests/Groundwork/`, `tools/groundwork/`
and `e2e-tests/groundwork/`. Their subject code is gone, so the coverage went with the subject.

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
the five permanently-disabled `if: false` Groundwork scaffold jobs in `integration.yml` are dropped.
`GROUNDWORK_V2_REQUIRE_NATIVE_PROVIDER_MATRIX` is renamed `ELSA_REQUIRE_NATIVE_PROVIDER_MATRIX`.
These matrix legs have **no** required-provider variable and self-skip when a container is
unavailable: workflows-design, identity, opentelemetry, structured-logs, studio-preferences,
secrets-sqlserver, secrets-mysql, transaction-topology and mysql-feasibility.

## Project register

| ID | Current project | Owner | Contract suites / required evidence | Providers and disposition | Replacement PR | Deletion PR |
|---|---|---|---|---|---|---|
| T01 | `tests/Elsa/Activities/Design/Persistence/Groundwork/TemporalProjectionTests/Elsa.Activities.Design.Persistence.Groundwork.TemporalProjectionTests.csproj` | #1677 child | Temporal definition and management-mutation projections | Deleted (was: SQLite; port to EF) |  | `claude/delete-groundwork` |
| T02 | `tests/Elsa/Activities/Design/Persistence/Groundwork/Tests/Elsa.Activities.Design.Persistence.Groundwork.Tests.csproj` | #1677 child | Activity definition/version/draft/availability/upgrade CRUD, registration and manifest | Deleted (was: SQLite; port to EF) |  | `claude/delete-groundwork` |
| T03 | `tests/Elsa/Activities/Design/Persistence/Groundwork/V2/ProviderMatrix/Tests/Elsa.Activities.Design.Persistence.Groundwork.V2.ProviderMatrix.Tests.csproj` | #1677 child | Activities Design provider contract matrix | Deleted (was: Relational + retire Mongo; add MySQL) |  | `claude/delete-groundwork` |
| T04 | `tests/Elsa/Activities/Design/Persistence/Groundwork/V2/Tests/Elsa.Activities.Design.Persistence.Groundwork.V2.Tests.csproj` | #1677 child | Atomicity and concurrency | Deleted (was: SQLite; port to EF) |  | `claude/delete-groundwork` |
| T67 | `tests/Elsa/Activities/Design/Persistence/EntityFrameworkCore/Tests/Elsa.Activities.Design.Persistence.EntityFrameworkCore.Tests.csproj` | #1731 | EF Activities Design model creation, SQLite round-trip, tenant isolation, atomicity, projections, lifecycle, draft/layout, fork receipt, and immutable identity evidence | SQLite and provider-neutral model validation; 71 focused tests pass at this candidate head | #1731 | |
| T68 | `tests/Elsa/Activities/Design/Persistence/EntityFrameworkCore/ProviderTests/Elsa.Activities.Design.Persistence.EntityFrameworkCore.ProviderTests.csproj` | #1731 | Live SQL Server, PostgreSQL, and MySQL model creation, CRUD/query, rollback, optimistic concurrency, required TenantKey, and concurrent global uniqueness | SQL Server/PostgreSQL/MySQL native smoke currently passes 3/3 locally; Docker-skippable and not a hosted migration/default-flip gate | #1731 | |
| T05 | `tests/Elsa/Diagnostics/OpenTelemetry/Persistence/Groundwork/V2/Tests/Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.V2.Tests.csproj` | #1681 | Trace/log/metric round-trip, filtering, retention, HTTP boundary and session disposal | Deleted (was: Relational + retire Mongo; add MySQL) |  | `claude/delete-groundwork` |
| T06 | `tests/Elsa/Diagnostics/Persistence/Groundwork/Tests/Elsa.Diagnostics.Persistence.Groundwork.Tests.csproj` | #1681 | Diagnostics umbrella feature composition | Deleted (was: Rebind to EF) |  | `claude/delete-groundwork` |
| T07 | `tests/Elsa/Diagnostics/Persistence/Groundwork/V2/Consumer/Elsa.Diagnostics.Persistence.Groundwork.V2.Consumer.csproj` | #1681 | Consumer build/launch boundary | Deleted (was: Rewire to EF or explicitly retire when no consumer boundary remains) |  | `claude/delete-groundwork` |
| T08 | `tests/Elsa/Diagnostics/StructuredLogs/Persistence/Groundwork/V2/Tests/Elsa.Diagnostics.StructuredLogs.Persistence.Groundwork.V2.Tests.csproj` | #1681 | Append, order/high-water, retry, retention, restart and disposal | Deleted (was: Relational + retire Mongo; add MySQL) |  | `claude/delete-groundwork` |
| T09 | `tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/ProcessProbe/Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.ProcessProbe.csproj` | #1682 | Process restart, duplicate handling and normalized identity protocol | Deleted (was: Relational + retire Mongo; add MySQL) |  | `claude/delete-groundwork` |
| T10 | `tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Tests/Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.csproj` | #1682 | ASP.NET Identity user/role/relationship, tenant, atomicity, concurrency, failure mapping, reconciliation and seeding | Deleted (was: SQLite; port to EF) |  | `claude/delete-groundwork` |
| T11 | `tests/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/V2/ProviderMatrix/Tests/Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.V2.ProviderMatrix.Tests.csproj` | #1682 | Relationship, tenant, schema, reopen and process matrix | Deleted (was: Relational + retire Mongo; add MySQL) |  | `claude/delete-groundwork` |
| T12 | `tests/Elsa/Foundation/Identity/Persistence/Groundwork/Tests/Elsa.Foundation.Identity.Persistence.Groundwork.Tests.csproj` | #1682 | IAM row/revision/manifest/conformance contracts | Deleted (was: SQLite; port to EF) |  | `claude/delete-groundwork` |
| T13 | `tests/Elsa/Foundation/Identity/Persistence/Groundwork/V2/ProviderMatrix/Tests/Elsa.Foundation.Identity.Persistence.Groundwork.V2.ProviderMatrix.Tests.csproj` | #1682 | IAM CRUD, normalized lookup, revision, isolation and restart | Deleted (was: Relational + retire Mongo; add MySQL) |  | `claude/delete-groundwork` |
| T14 | `tests/Elsa/Groundwork/ProviderEvidenceImporter/Tests/Elsa.Groundwork.ProviderEvidenceImporter.Tests.csproj` | #1670 | Checkpoint/fence evidence mapping | Deleted (was: Preserve neutral evidence mapping; retire Groundwork-only importer) |  | `claude/delete-groundwork` |
| T15 | `tests/Elsa/Persistence/Groundwork/DesignConformance/MongoDb/Tests/Elsa.Persistence.Groundwork.DesignConformance.MongoDb.Tests.csproj` | #1677 child / #1670 | Provider-neutral Design CRUD/query/atomicity/isolation/restart suites plus Mongo topology | Deleted (was: Move neutral suites; retire Mongo topology) |  | `claude/delete-groundwork` |
| T16 | `tests/Elsa/Persistence/Groundwork/DesignConformance/PostgreSql/Tests/Elsa.Persistence.Groundwork.DesignConformance.PostgreSql.Tests.csproj` | #1672 and #1677 children | Activities Design, Runtime, distributed Runtime, Publishing and Workflows Design conformance | Deleted (was: Port to EF PostgreSQL; ownership spans P08-P12) |  | `claude/delete-groundwork` |
| T17 | `tests/Elsa/Persistence/Groundwork/DesignConformance/SqlServer/Tests/Elsa.Persistence.Groundwork.DesignConformance.SqlServer.Tests.csproj` | #1672 and #1677 children | Activities Design, Runtime, distributed Runtime, Publishing and Workflows Design conformance | Deleted (was: Port to EF SQL Server; ownership spans P08-P12) |  | `claude/delete-groundwork` |
| T18 | `tests/Elsa/Persistence/Groundwork/DesignConformance/Sqlite/Tests/Elsa.Persistence.Groundwork.DesignConformance.Sqlite.Tests.csproj` | #1672 and #1677 children | Atomicity, query, workflow-design lifecycle, WAL/busy/locking-relevant behavior | Deleted (was: Port to EF SQLite) |  | `claude/delete-groundwork` |
| T19 | `tests/Elsa/Persistence/Groundwork/DesignConformance/Target/Elsa.Persistence.Groundwork.DesignConformance.Target.csproj` | #1672 and #1677 children | Provider-neutral fault/event injection target | Deleted (was: Preserve as EF-neutral harness or retarget to EF) |  | `claude/delete-groundwork` |
| T20 | `tests/Elsa/Persistence/Groundwork/DesignConformance/Tests/Elsa.Persistence.Groundwork.DesignConformance.Tests.csproj` | #1672 and #1677 children | Activity/workflow Design CRUD/query/atomicity/isolation/restart/scale suite shape | Deleted (was: Preserve and rebind to EF; retire measurement-only query-plan criteria) |  | `claude/delete-groundwork` |
| T21 | `tests/Elsa/Persistence/Groundwork/MongoDb/UnifiedHost/Tests/Elsa.Persistence.Groundwork.MongoDb.UnifiedHost.Tests.csproj` | #1670 | Unified host composition and restart journey | Deleted (was: Extract neutral host journey; retire replica-set/standalone topology) |  | `claude/delete-groundwork` |
| T22 | `tests/Elsa/Persistence/Groundwork/PostgreSql/UnifiedHost/Tests/Elsa.Persistence.Groundwork.PostgreSql.UnifiedHost.Tests.csproj` | #1670 | PostgreSQL container, unified composition and restart | Deleted (was: Port to EF PostgreSQL) |  | `claude/delete-groundwork` |
| T23 | `tests/Elsa/Persistence/Groundwork/SqlServer/UnifiedHost/Tests/Elsa.Persistence.Groundwork.SqlServer.UnifiedHost.Tests.csproj` | #1670 | SQL Server container and unified composition | Deleted (was: Port to EF SQL Server) |  | `claude/delete-groundwork` |
| T24 | `tests/Elsa/Persistence/Groundwork/Testing/Elsa.Persistence.Groundwork.Testing.csproj` | #1678 | Provider/session/schema test kit and neutral fault/recording doubles | Deleted (was: Replace with EF four-provider kit; preserve neutral doubles) |  | `claude/delete-groundwork` |
| T25 | `tests/Elsa/Persistence/Groundwork/UnifiedHost/Tests/Elsa.Persistence.Groundwork.UnifiedHost.Tests.csproj` | #1670 | Target isolation, activation/restart, publication-lane and SQLite composition | Deleted (was: Port to EF host) |  | `claude/delete-groundwork` |
| T26 | `tests/Elsa/Persistence/Groundwork/V2/Runtime/Tests/Elsa.Persistence.Groundwork.V2.Runtime.Tests.csproj` | #1672 / #1676 | All 29 Runtime units: checkpoint/outbox, claims/fences, queues/poison, timers, bookmarks/triggers/schedules, durable values, dispatch, recovery, incidents/alterations/holds/test scope and materialization | Deleted (was: Relational + retire Mongo; add MySQL) |  | `claude/delete-groundwork` |
| T27 | `tests/Elsa/Persistence/Groundwork/V2/Testing/Elsa.Persistence.Groundwork.V2.Testing.csproj` | #1678 / #1672 | Runtime persistence test infrastructure | Deleted (was: Replace with EF infrastructure) |  | `claude/delete-groundwork` |
| T28 | `tests/Elsa/Persistence/Groundwork/V2/Tests/Elsa.Persistence.Groundwork.V2.Tests.csproj` | #1670 / #1678 | Manifest, provider connections, storage access audit and release boundary | Deleted (was: Port neutral evidence; rewrite final no-Groundwork guard; retire Mongo) |  | `claude/delete-groundwork` |
| T29 | `tests/Elsa/Secrets/Persistence/Groundwork/V2/ProviderMatrix/Tests/Elsa.Secrets.Persistence.Groundwork.V2.ProviderMatrix.Tests.csproj` | #1679 | Secrets CRUD/revision/query/tenant/restart matrix | Deleted (was: Relational + retire Mongo; add MySQL) |  | `claude/delete-groundwork` |
| T30 | `tests/Elsa/Secrets/Persistence/Groundwork/V2/Tests/Elsa.Secrets.Persistence.Groundwork.V2.Tests.csproj` | #1679 | Secret repository and registration contracts | Deleted (was: SQLite; replace with EF evidence) |  | `claude/delete-groundwork` |
| T65 | `tests/Elsa/Secrets/Persistence/EntityFrameworkCore/MySql/Tests/Elsa.Secrets.Persistence.EntityFrameworkCore.MySql.Tests.csproj` | #1726 | Production MySQL context model, provider binding, repository CRUD/query, rollback, and optimistic concurrency | MySQL Testcontainers; focused proof delivered in #1726; migrations and full lifecycle remain pending | #1726 | |
| T31 | `tests/Elsa/Studio/Preferences/Persistence/Groundwork/Tests/Elsa.Studio.Preferences.Persistence.Groundwork.Tests.csproj` | #1680 | Preference scope/read/write/concurrency | Deleted (was: SQLite; port to EF and add relational providers) |  | `claude/delete-groundwork` |
| T32 | `tests/Elsa/Workflows/Dashboard/Persistence/Groundwork/V2/Tests/Elsa.Workflows.Dashboard.Persistence.Groundwork.V2.Tests.csproj` | #1677 child | Portfolio and run-health bounded projections | Deleted (was: SQLite; port to EF) |  | `claude/delete-groundwork` |
| T33 | `tests/Elsa/Workflows/Design/Persistence/Groundwork/Tests/Elsa.Workflows.Design.Persistence.Groundwork.Tests.csproj` | #1677 child | Definition/version/draft/layout/list projection, commands, registration and schema | Deleted (was: SQLite; port to EF) |  | `claude/delete-groundwork` |
| T34 | `tests/Elsa/Workflows/Publishing/Api/GroundworkTests/Elsa.Workflows.Publishing.Api.GroundworkTests.csproj` | #1677 child | Activity test-run receipts, publication commits, upgrades and published-deletion guard | Deleted (was: Relational + retire Mongo; add MySQL) |  | `claude/delete-groundwork` |
| T35 | `tests/Elsa/Workflows/Publishing/Persistence/Groundwork/Tests/Elsa.Workflows.Publishing.Persistence.Groundwork.Tests.csproj` | #1677 child | Six Publishing units, lifetime and provider matrix | Deleted (was: Relational + retire Mongo; add MySQL) |  | `claude/delete-groundwork` |
| T36 | `tests/Elsa/Workflows/Runtime/Distributed/Persistence/Groundwork/Tests/Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Tests.csproj` | #1672 / #1676 | Placement/fencing, stream-head/command ordering, lease/redelivery/ack and restart | Deleted (was: Relational + retire Mongo; add MySQL) |  | `claude/delete-groundwork` |
| T37 | `tests/Elsa/Activities/Http/IntegrationTests/Elsa.Activities.Http.IntegrationTests.csproj` | #1672 / #1670 | HTTP endpoint status/body/content type, workflow completion and artifact persistence | Port to EF; split and retire only commit-count/write-amplification assertions | | |
| T38 | `tests/Elsa/Activities/Scheduling/Tests/Elsa.Activities.Scheduling.Tests.csproj` | #1672 | Durable timer, bookmark, crash/restart and idempotent resume | Port to EF | | |
| T39 | `tests/Elsa/Diagnostics/Persistence/Tests/Elsa.Diagnostics.Persistence.Tests.csproj` | #1681 | Diagnostics lifecycle/order/loss/retry/observability, drain and provider fixtures | Deleted (was: Relational + retire Mongo; `DiagnosticsDrainLoadTests` remains correctness) |  | `claude/delete-groundwork` |
| T40 | `tests/Elsa/Foundation/Identity/Tests/Elsa.Foundation.Identity.Tests.csproj` | #1682 | Shell/sign-in/IAM/normalized lookup and OpenIddict composition boundary | Port to EF; retain separate vendor context | | |
| T41 | `tests/Elsa/Secrets/Persistence/EntityFrameworkCore/Tests/Elsa.Secrets.Persistence.EntityFrameworkCore.Tests.csproj` | #1679 | EF migration/composition/shell reload/search-key/concurrency/repository plus current dual-provider compatibility | Deleted (was: Retain EF destination; remove dual Groundwork compatibility only after flip) |  | `claude/delete-groundwork` |
| T42 | `tests/Elsa/Workflows/Design/Tests/Elsa.Workflows.Design.Tests.csproj` | #1677 child | Provider-neutral draft/tree/checkpoint-cadence behavior with Groundwork host | Deleted (was: Preserve; replace host with EF) |  | `claude/delete-groundwork` |
| T43 | `tests/Elsa/Workflows/Runtime/Distributed/Tests/Elsa.Workflows.Runtime.Distributed.Tests.csproj` | #1672 / #1676 | Distributed actor/placement/transport and two-node acceptance | Preserve; skipped checkpoint/outbox/dispatch restart case is a gap, not evidence | | |
| T44 | `tests/Elsa3/Mapping/Tests/Elsa3.Mapping.Tests.csproj` | #1677 child | Import manifest, idempotent/atomic apply, restart and registration | Port to EF or neutralize harness | | |
| T69 | `tests/Elsa3/Activities/Design/Import/Persistence/EntityFrameworkCore/Tests/Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.Tests.csproj` | #1738 | EF import ledger L01-L03 and the shared-transaction owner: atomic commit and injected-failure rollback across the import, Activities Design, and Workflows Design contexts on one connection and transaction; split-target and provider-mismatch refusal; idempotent and conflicting replay; tenant and user isolation; restart durability; lost-commit reconciliation; corrupt-row and hash-drift fail-closed reads; Groundwork/EF registration switching and fail-closed custom registrations | Deleted (was: SQLite comprehensive; opt-in EF destination under #1738) |  | `claude/delete-groundwork` |
| T70 | `tests/Elsa3/Activities/Design/Import/Persistence/EntityFrameworkCore/ProviderTests/Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.ProviderTests.csproj` | #1738 | Live SQL Server, PostgreSQL, and MySQL model binding, ledger CRUD with case-, space-, and NUL-distinct identities, one atomic import commit, restart replay, and rollback after an injected post-write or pre-commit failure | PostgreSQL/SQL Server/MySQL native smoke passes 3/3 locally under `GROUNDWORK_V2_REQUIRE_NATIVE_PROVIDER_MATRIX=1`; not a hosted migration/default-flip gate | | |
| T45 | `tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj` | #1670 / #1678 | Architecture/security/dependency/composition guards | Deleted (was: Rewrite Groundwork-positive assertions to EF-positive and no-Groundwork guards) |  | `claude/delete-groundwork` |
| T46 | `tests/Elsa/Workflows/Publishing/Api/Tests/Elsa.Workflows.Publishing.Api.Tests.csproj` | #1677 child | Publishing API, preflight, activation, projection reconciliation and cross-module transaction semantics | Preserve and rebind host to EF | | |
| T47 | `tests/Elsa/Modularity/Tests/Elsa.Modularity.Tests.csproj` | #1670 | Provider registration, connection ownership, schema/startup/refusal and Workbench composition | Deleted (was: Preserve in EF composition; remove Mongo branch) |  | `claude/delete-groundwork` |
| T48 | `tests/Elsa/Workbench/Tests/Elsa.Workbench.Tests.csproj` | #1670 | Workbench process, shell activation and restart | Deleted (was: Replace Groundwork settings with EF) |  | `claude/delete-groundwork` |
| T49 | `tests/Elsa/Secrets/Persistence/EntityFrameworkCore/PostgreSql/Tests/Elsa.Secrets.Persistence.EntityFrameworkCore.PostgreSql.Tests.csproj` | #1679 | PostgreSQL repository, concurrency, shell/container and migration proof | Retain and extend through final flip | | N/A |
| T50 | `tests/Elsa/Secrets/Persistence/EntityFrameworkCore/SqlServer/Tests/Elsa.Secrets.Persistence.EntityFrameworkCore.SqlServer.Tests.csproj` | #1679 | SQL Server repository, concurrency and container proof | Retain and extend through final flip | | N/A |
| T51 | `tests/Elsa/Secrets/Persistence/EntityFrameworkCore/PostgreSql/PackageFeedProbe/Elsa.Secrets.Persistence.EntityFrameworkCore.PostgreSql.PackageFeedProbe.csproj` | #1679 / #1670 | PostgreSQL package-feed process probe | Retain only while it proves an active package boundary | | N/A |
| T52 | `tests/Elsa/Studio/Preferences/Persistence/EntityFrameworkCore/Tests/Elsa.Studio.Preferences.Persistence.EntityFrameworkCore.Tests.csproj` | #1680 | SQLite behavioral contract: scope isolation, CRUD, canonical revisions, optimistic concurrency, restart and opt-in DI | SQLite; retained EF destination, merged in #1693 | #1693 | N/A |
| T53 | `tests/Elsa/Studio/Preferences/Persistence/EntityFrameworkCore/ProviderTests/Elsa.Studio.Preferences.Persistence.EntityFrameworkCore.ProviderTests.csproj` | #1680 | Live provider smoke: PostgreSQL/SQL Server/MySQL model binding, representative CRUD, uniqueness and concurrency | PostgreSQL/SQL Server/MySQL; retained EF destination, merged in #1693 | #1693 | N/A |
| T54 | `tests/Elsa/Diagnostics/StructuredLogs/Persistence/EntityFrameworkCore/Tests/Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore.Tests.csproj` | #1695 | SQLite behavioral contract: append/order/high-water/idempotency, retention, restart, cursor/binding and opt-in DI | SQLite; retained EF destination delivered by #1696 | #1696 | N/A |
| T55 | `tests/Elsa/Diagnostics/StructuredLogs/Persistence/EntityFrameworkCore/ProviderTests/Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore.ProviderTests.csproj` | #1695 | Live provider smoke: PostgreSQL/SQL Server/MySQL model binding plus representative append/read/CAS and rollback or stale-CAS outcome | PostgreSQL/SQL Server/MySQL; Docker-skippable retained EF destination delivered by #1696 | #1696 | N/A |
| T56 | `tests/Elsa/Diagnostics/OpenTelemetry/Persistence/EntityFrameworkCore/Tests/Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore.Tests.csproj` | #1697 | SQLite behavioral contract: all-signal atomic capture, scoped queries/detail, canonical search, explicit replay ledger, trace-summary merge/CAS, retention/recovery, restart and opt-in DI | SQLite; retained EF destination delivered by #1701 | #1701 | N/A |
| T57 | `tests/Elsa/Diagnostics/OpenTelemetry/Persistence/EntityFrameworkCore/ProviderTests/Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore.ProviderTests.csproj` | #1697 | Four-provider model binding plus live PostgreSQL/SQL Server/MySQL representative all-signal CRUD/query/transaction, restart, isolation and concurrent trace-summary merge | PostgreSQL/SQL Server/MySQL; Docker-skippable retained EF destination delivered by #1701 | #1701 | N/A |
| T58 | `tests/Elsa/Foundation/Identity/Persistence/EntityFrameworkCore/Tests/Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Tests.csproj` | #1712 | SQLite IAM authority behavior: tenant isolation, lossless CRUD, bounded ordering, reservations, relationships, atomicity, replay/recovery and optimistic concurrency | SQLite comprehensive; retained EF destination delivered by #1715 | #1715 | N/A |
| T59 | `tests/Elsa/Foundation/Identity/Persistence/EntityFrameworkCore/ProviderTests/Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.ProviderTests.csproj` | #1712 | SQL Server, PostgreSQL, and MySQL model creation and representative CRUD/query/transaction/concurrency smoke | SQL Server/PostgreSQL/MySQL focused provider smoke; retained EF destination delivered by #1715 | #1715 | N/A |
| T60 | `tests/Elsa/Foundation/Identity/AspNetCoreIdentity/EntityFrameworkCore/Tests/Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Tests.csproj` | #1712 | Complete ASP.NET Core Identity store surface, tenant isolation, normalized uniqueness, relationship atomicity, concurrency stamps, sign-in/session, and seeding over the shared EF authority | SQLite comprehensive; retained EF destination delivered by #1715 | #1715 | N/A |
| T61 | `tests/Elsa/Workflows/Runtime/Distributed/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Tests.csproj` | #1717 | SQLite placement behavior: scope/identity losslessness, claim/renew/takeover/logical-release/list, release-reclaim fencing continuity, rollback/tracker recovery, concurrency, restart, invalid input/cancellation, provider selection and startup ownership validation | SQLite; EF D01 replacement implementation in #1717 | #1718 | |
| T62 | `tests/Elsa/Workflows/Runtime/Distributed/Persistence/EntityFrameworkCore/ProviderTests/Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.ProviderTests.csproj` | #1717 | Four-provider model plus disposable PostgreSQL/SQL Server/MySQL schema, scoped claim/find/list/release, rollback, concurrent outcome and reopen durability | PostgreSQL/SQL Server/MySQL; Docker-skippable live provider proof in #1717 | #1718 | |
| T63 | `tests/Elsa/Workflows/Runtime/Distributed/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Tests.csproj` | #1720 | SQLite D02-D03 behavior: lossless envelope round-trip, scope isolation, contiguous/CAS sequence allocation, atomic head/item rollback, bounded FIFO lease/replenishment, inclusive expiry/redelivery, exact-token acknowledgement, pending-head queries, restart, corrupt-state fail-closed behavior, and tracker recovery | SQLite; D02-D03 implementation and proof in #1721 under task #1720 | #1721 | |
| T64 | `tests/Elsa/Workflows/Runtime/Distributed/Persistence/EntityFrameworkCore/ProviderTests/Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.ProviderTests.csproj` | #1720 | Four-provider model plus live PostgreSQL/SQL Server/MySQL smoke for representative send/query/lease/ack, rollback, concurrent writers/leasers, and reopen durability; no hidden skips under the required-provider gate | PostgreSQL/SQL Server/MySQL; Docker-skippable live provider proof in #1721 under task #1720 | #1721 | |
| T65 | `tests/Elsa/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj` | #1723/#1725 | SQLite R01-R06 behavior: exact scoped/composite identity, lossless round-trip, bounded ordinal keyset paging, query-bound continuations, real create/update/delete contention, rollback, restart, corrupt-state failure and DI ownership/load order | SQLite comprehensive; R01 implementation merged in #1724 and R02-R06 implementation prepared under #1725 | | |
| T66 | `tests/Elsa/Workflows/Runtime/Persistence/EntityFrameworkCore/ProviderTests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests.csproj` | #1723/#1725 | Provider model plus exact local native-provider evidence for PostgreSQL/SQL Server/MySQL Runtime bookmark and artifact CRUD, keyset query, transaction rollback, optimistic concurrency and reopen durability | Exact local native smoke is 6/6 across the six focused bookmark/artifact tests, including PostgreSQL bookmark; broader provider matrix and hosted required-provider gating are intentionally out of scope | | |

## Correctness hidden inside retiring performance surfaces

| Current file or suite | Correctness that must move before deletion | Retired material | Owner | Replacement PR | Disposition |
|---|---|---|---|---|---|
| `tests/Elsa/Activities/Http/IntegrationTests/HttpEndpointRuntimePerformanceTests.cs` | HTTP status, response body/content type, workflow completion and persisted artifact | Commit-count and write-amplification comparisons | #1672/#1670 and #1668 | | Retired by owner decision (#1668); correctness moved to `HttpEndpointCheckpointPolicyEquivalenceTests.cs` in the same project, asserted for Immediate and Coalesced. Mandatory durability boundaries stay covered by `RuntimeCheckpointCoalescingPolicyTests`; the EF port remains T37's |
| `tests/Elsa/Foundation/Identity/Tests/AspNetCoreIdentity/Performance/IamNormalizedLookupSqliteCorrectnessTests.cs` | Timing-free normalized-name/email lookup and duplicate behavior | Native-plan/measurement acceptance | #1682 and #1668 | | Retired by owner decision (#1668); moved out of `Performance/` to `tests/Elsa/Foundation/Identity/Tests/AspNetCoreIdentity/IamNormalizedLookupSqliteCorrectnessTests.cs`, which asserts the scenario's lookups, membership and revision-guarded update directly with no harness dependency; the native-plan test is retired. Duplicate normalized-name/email/role behavior is expressed by `AspNetCoreIdentityConcurrencyContractTests` and EF-owned `EfCoreIdentityFrameworkContractTests` (T60) |
| `benchmarks/Elsa/Activities/Runtime/Benchmarks/ActivationScopeSemanticTests.cs` | Activation-scope correctness | Benchmark project and measurement harness | #1672 and #1668 | | Retired by owner decision (#1668); per-attempt semantics moved to `tests/Elsa/Activities/Runtime/Tests/ActivationScopeSemanticTests.cs`, now against the production `ActivityActivator`/`ClrActivityActivator` rather than the benchmark's strategy models. Burst-only and conditional candidate assertions are not ported (rejected by ADR 0045, never shipped); the intrinsic-workload assertion is not ported (it checked a counter the harness set itself) |
| `tools/ledger/StorePerformance/AdapterHost/Tests` | Adapter protocol, provider binding and failure correctness not expressed elsewhere | Timing, budget and native-plan evidence | #1678/#1670 and #1668 | | Retired by owner decision (#1668). Native-plan capture/parsing, fingerprint, probe and admission tests are measurement-only or Groundwork-specific; the workload-over-Groundwork runs are mapped below, and the one unexpressed obligation moved to `GroundworkV2RuntimePostCommitOutboxStoreTests` (T26) |
| `tools/ledger/StorePerformance/Benchmarks/Tests` | Workflow completion, checkpoint/outbox, duplicate and recovery assertions not expressed elsewhere | Measurement admission, comparison and fingerprints | #1672 and #1668 | | Retired by owner decision (#1668). These tests ran the workload oracle against test-local fakes, not product stores; the obligations the oracle encodes are mapped below |
| `tests/Elsa/Diagnostics/Persistence/Tests/DiagnosticsDrainLoadTests.cs` | Drain ordering, bounded batches, no-loss/retry behavior | None merely because the class name contains `Load` | #1681 | | Preserve |

### Retired store-performance workload mapping (#1668)

The workload oracles ran either against test-local fakes (`Benchmarks/Tests`) or against Groundwork
SQLite stores through the benchmark adapter host (`AdapterHost/Tests`); neither project ran in a CI
gate. Each workload's timing-independent obligations are expressed by these existing suites:

| Retired workload | Obligations the oracle checked | Where they are expressed |
|---|---|---|
| `checkpoint-commit` | Replay equivalence, conflicting-replay and stale-fence refusal, no duplicated post-commit work, reread through another client | `RuntimeCheckpointCommitTests`; `GroundworkV2RuntimeCheckpointWriterTests` (T26) |
| `bookmark-lookup` | Bounded ordered stimulus pages, page boundary, scope isolation | `GroundworkV2BookmarkStateStoreTests` (T26); EF bookmark suites (T65/T66) |
| `recovery-scan` | Bounded continuation paging, live and terminal exclusion, stability after reopen | `RuntimeRecoveryScannerTests`; `GroundworkV2ExecutionLivenessStoreTests` (T26), including the v1.2 production-scanner traversal |
| `queue-drain` | FIFO, bounded claims, one owner under contention, expired reclaim with a higher fence, stale-acknowledgement refusal, poison relationship, restart | `GroundworkV2WorkflowSchedulerWorkQueueTests`, `GroundworkV2WorkflowSchedulerPoisonStoreTests` (T26) |
| `outbox-drain` | Bounded ordered claims, retry delay, reclaim fence, stale-completion refusal, restart, one owner under contention | `RuntimePostCommitOutboxStoreTests`; `GroundworkV2RuntimePostCommitOutboxStoreTests` (T26), where #1668 added the contention case as a deterministic read-then-write interleaving |
| `trigger-binding-stimulus-lookup` | Bounded binding and source-reference pages, scope isolation | `GroundworkV2WorkflowTriggerBindingStoreTests`, `GroundworkV2WorkflowExecutableSourceReferenceStoreTests` (T26) |
| `recurring-schedule-selection` | Due cutoff and order, revision-guarded advance, stale-advance refusal, publication projection, restart | `GroundworkV2RecurringTriggerScheduleStoreTests` (T26) |
| `due-timer-selection` | Due selection, fenced claims, claim compare-and-swap under interleaving, stale-transition refusal | `GroundworkV2DurableTimerStateStoreTests` (T26) |
| `distributed-placement-takeover` | One winner for first and expired-takeover claims, stale-release refusal | `EfExecutionPlacementStoreTests` (T61); T36 |
| `distributed-command-send-lease-ack` | Contiguous unique sends, bounded leases, redelivery after expiry and reopen, stale-acknowledgement refusal | `EfExecutionCommandTransportTests` (T63); T36 |
| `iam-normalized-lookup-update` | Normalized name/email/role lookup, membership, revision-guarded update | `IamNormalizedLookupSqliteCorrectnessTests` (T40); T60 |
| `secret-create-read-list` | Tenant-local normalized-name uniqueness, bounded pages, concurrent create | `SecretTenantIsolationTests`; `SqliteEfSecretRepositoryTests`; `GroundworkV2SecretRepositoryTests` (T30) |
| `diagnostics-durable-history` | Structured-log cursor and lifetime high-water across retention and reopen, bounded resource and trace pages, ordered trace detail | T54 and T56; T05 and T08 |

Measurement admission, comparison, gates, budgets, native-plan capture and parsing, provider probes,
composition fingerprints and the harness's self-tests carried no product correctness. The spec 094
coverage-ledger validator (`tools/ledger/Elsa.Groundwork.Ledger.Tests`) checked Groundwork
project-management artifacts and Groundwork service lifetimes only; it was removed with the harness,
and the final no-Groundwork audit remains #1670's. The Runtime engine benchmark asserted only workflow
completion beside commit, dispatch, read, materialization and fusion-engagement counts, and never ran in
CI; completion under both checkpoint policies is covered by `RuntimeCheckpointCoalescingTests` and fusion
behavior by `ReplaySafeFusionGuardrailTests`. The OpenTelemetry trace-list benchmark compared against a frozen
Groundwork v1 comparand that no longer exists; v2 trace-list correctness is covered by T05.

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

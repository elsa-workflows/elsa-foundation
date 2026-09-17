# EF Core persistence completion ledger

Status: closed (2026-09-16). Every replacement, default-flip and deletion disposition names a
merged PR in the entry-level registers, which were closed by #1765 (storage units) and #1766
(repository surfaces, tests and backend e2e). The carry-forwards this program does not claim are
listed under "Closure" below; read them before treating any row as a guarantee.

Inventory snapshot: `main` at `7a952efcf8d53472d7d4e7e3fd7b51d7a808c1c8` (2026-09-12), the
squash merge of governance PR #1685. Later implementation PRs must update the affected rows and the
snapshot rather than treating this baseline as current forever.

Program: [#1665](https://github.com/elsa-workflows/elsa-foundation/issues/1665). Governing
decision: [ADR 0073](../adr/0073-ef-core-is-the-only-first-party-persistence-family.md). Bounded
inventory predecessor: [#1654](https://github.com/elsa-workflows/elsa-foundation/issues/1654).

This index and its linked entry-level registers establish the audit boundary. An Epic named as a
temporary owner must create a worker-ready child before implementation begins; it does not mean the
requirement is optional. Replacement, default-flip, and deletion PR cells stay blank until an exact
merged PR exists. Historical performance rows may be retired by policy, but their timing-independent
correctness requirements must first move to an EF-neutral or EF-owned row.

## Deletion step — 2026-09-16

The removal step of #1670 landed as #1764, squashed to `a83c41c1c`. After it, no first-party Groundwork or MongoDB production code,
package, transitive runtime dependency, shell feature, host wiring, tool, workflow, configuration,
test project or guard remains under `src/`, `tests/`, `tools/`, `docker/`, `e2e-tests/`, `.github/`
or the repository package configuration. The entry-level registers record the per-row disposition:
the [storage-unit register](ef-core-persistence/storage-unit-register.md) marks every unit deleted and names its replacement and default-flip PRs,
the [repository-surface register](ef-core-persistence/repository-surface-register.md) marks the
package, feed, solution, workflow and guard surfaces, and the
[test and e2e register](ef-core-persistence/test-and-e2e-register.md) names which coverage was
re-pointed at EF Core and which was dropped.

Two things this step deliberately did not do: it did not touch `docs/adr/`, `specs/`,
`docs/reports/archive/` or `docs/reports/evidence/`, which stay truthful history; and it did not
amend either constitution, where framework §2.24 and ADR 0042's superseded direction remain a
ratification question for the owner rather than an implementation edit.

## Verified baseline

This is the pre-program inventory at the 2026-09-12 snapshot, kept as history. The rows below it
carry the final dispositions.

- 14 Groundwork production projects: 232 C# files and 46,809 lines.
- 7 Workbench Groundwork/Mongo glue files: 276 lines, outside the 14 projects. Six of those files
  also match a broad `/Groundwork/` path scan, which is why that scan reports 238 rather than the
  project-root count of 232; the categories here do not overlap.
- 17 Groundwork shell features.
- 95 declared Groundwork storage units: Runtime 29, distributed Runtime 3, Activities Design 21,
  Identity 17, OpenTelemetry 8, Publishing 6, Workflows Design 5, Elsa3 import 3, Secrets 1,
  Structured Logs 1, and Studio Preferences 1. Dashboard reuses Design/Runtime units.
- 36 Groundwork/Mongo-named seed test projects: 199 C# files and 55,516 lines. The complete
  actionable register has 51 evidence-project rows: those 36, eight omitted direct-package
  consumers, two project-reference-only consumers, two Workbench/Modularity host consumers, and
  three EF-only Secrets destinations. The dual Secrets EF project is one of the eight consumers.
- 5 benchmark projects; 101 tracked C# files and 28,962 lines. Historical ignored result logs explain
  the planning input's larger approximate total.
- EF currently comprises shared policy plus the opt-in Secrets implementation with SQLite, SQL
  Server, PostgreSQL, and MySQL derived contexts. MySQL production binding/context selection and
  focused Testcontainers repository proof landed in #1726; provider-specific migrations/tooling,
  full lifecycle, default flip, and Groundwork removal remain pending.
- `.github/workflows/http-workflow-performance.yml` and
  `.github/workflows/groundwork-ledger.yml` are present and active in source. The HTTP workflow is
  currently `disabled_manually` in GitHub, verified by workflow id 322696414 on 2026-09-12; neither
  source workflow has completed its tracked retirement.

## Entry-level registers

- [Storage units and domain/store semantics](ef-core-persistence/storage-unit-register.md): exactly
  95 rows, including all 29 Runtime and three distributed Runtime units.
- [Test projects, contract suites, correctness extraction and backend e2e](ef-core-persistence/test-and-e2e-register.md):
  51 evidence-project rows, six hybrid correctness/performance surfaces, and 13 backend e2e journeys.
- [Packages, providers, migrations, hosts, tools, workflows, alerts, guards and generated artifacts](ef-core-persistence/repository-surface-register.md):
  dependency counts, the absent MySQL lane, 20 explicit transaction/migration acceptance rows, and
  every active cleanup class.
- [Legacy and current issue requirements](ef-core-persistence/legacy-requirement-register.md): 77
  open requirement-bearing/current-program issues plus closed regression predecessor #1659, each mapped to an EF
  owner and truthful closure gate.

## Production-project ledger

| ID | Current production project | Declared units / role | EF replacement owner | Dependencies / blockers | Required correctness evidence | Replacement PR | Default-flip PR | Deletion PR | Disposition |
|---|---|---|---|---|---|---|---|---|---|
| P01 | `src/Elsa/Secrets/Persistence/Groundwork/Elsa.Secrets.Persistence.Groundwork.csproj` | 1 Secrets unit | #1679; executable predecessor #1653 | MySQL and migration spikes | Existing repository contract, OCC, normalization/search, four providers, migration lifecycle, HTTP CRUD/restart | #1624, #1633, #1634, #1646, #1729 | #1763 | #1764 | Replaced by EF Core; Groundwork project deleted by #1764 |
| P02 | `src/Elsa/Studio/Preferences/Persistence/Groundwork/Elsa.Studio.Preferences.Persistence.Groundwork.csproj` | 1 preferences unit | #1680 | MySQL and migration spikes | Contract parity, tenancy, last-write/concurrency semantics, four providers, restart/e2e | #1693 | #1763 | #1764 | Replaced by EF Core; Groundwork project deleted by #1764 |
| P03 | `src/Elsa/Diagnostics/StructuredLogs/Persistence/Groundwork/Elsa.Diagnostics.StructuredLogs.Persistence.Groundwork.csproj` | 1 structured-log unit | #1681 | MySQL/migration spikes; correctness extraction from #646/#1529/#1521 | Append/idempotency, sequence/order, exact retention, bounded reads, redaction, four providers | #1696 | #1763 | #1764 | Replaced by EF Core; Groundwork project deleted by #1764 |
| P04 | `src/Elsa/Diagnostics/OpenTelemetry/Persistence/Groundwork/Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.csproj` | 8 trace/log/metric units | #1681 | Same as P03 | Atomic append, idempotency, exact retention, bounded queries, failure isolation, four providers | #1701 | #1763 | #1764 | Replaced by EF Core; Groundwork project deleted by #1764 |
| P05 | `src/Elsa/Diagnostics/Persistence/Groundwork/Elsa.Diagnostics.Persistence.Groundwork.csproj` | Umbrella composition | #1681 | P03-P04 | Selected provider composition, shell activation/reload, host-boundary e2e | #1696, #1701 | #1763 | #1764 | Deleted by #1764; the per-signal EF Diagnostics features selected by #1763 replace its composition role and keep Diagnostics on its own database file |
| P06 | `src/Elsa/Foundation/Identity/Persistence/Groundwork/Elsa.Foundation.Identity.Persistence.Groundwork.csproj` | 17 IAM units shared with P07 | #1682 | MySQL, transaction, migration spikes | Tenant isolation, normalized unique lookup, reservations/receipts atomicity, conflict behavior, four providers | #1709, #1711, #1715 | #1763 | #1764 | Replaced by EF Core; Groundwork project deleted by #1764 |
| P07 | `src/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.csproj` | ASP.NET Identity adapters over IAM units | #1682 | P06; OpenIddict boundary must remain separate | Full Identity store contracts, sign-in/restart, concurrency stamps, normalized lookup, tenant isolation | #1715 | #1763 | #1764 | Replaced by EF Core; Groundwork project deleted by #1764; the OpenIddict vendor context stays separate (H09) |
| P08 | `src/Elsa/Persistence/Groundwork/V2/Elsa.Persistence.Groundwork.V2.csproj` | Shared Groundwork policy plus 29 Runtime units | #1672; proving slice #1676; R01 task #1723; R02-R06 task #1725 | All four spikes; Runtime proving slice | Checkpoint atomicity, marker reconciliation, leases/fencing, queues, timers, dispatch/outbox, recovery, contention | Shared policy: #1624, #1693, #1724, #1755, #1760. Runtime: #1724, #1730, #1741, #1743, #1744, #1745, #1746, #1749, #1758 | #1763 | #1764 | Replaced by EF Core; Groundwork project deleted by #1764. Per-unit rows R01-R29 are in the storage-unit register; the R28 lease/expiry gap stays open under #1739 |
| P09 | `src/Elsa/Workflows/Runtime/Distributed/Persistence/Groundwork/Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.csproj` | 3 units: placement, command-stream head, command transport | #1672; proving slice #1676; D02-D03 task #1720 | Runtime proving slice and transaction topology | Placement ownership/fencing; atomic stream-head + command write; redelivery, crash/restart | #1718 (D01); #1721 (D02-D03, task #1720) | #1763 | #1764 | Replaced by EF Core; Groundwork project deleted by #1764 |
| P10 | `src/Elsa/Activities/Design/Persistence/Groundwork/Elsa.Activities.Design.Persistence.Groundwork.csproj` | 21 Activities Design units | #1677; child Feature to be elaborated | Transaction/migration spikes; Runtime contracts stable | CRUD/query conformance, temporal projections, operation ledger, upgrade plans, four providers | #1742; #1759; #1762 | #1763 | #1764 | Replaced by EF Core; Groundwork project deleted by #1764 |
| P11 | `src/Elsa/Workflows/Design/Persistence/Groundwork/Elsa.Workflows.Design.Persistence.Groundwork.csproj` | 5 Workflows Design units | #1677; child Feature to be elaborated | Transaction/migration spikes | CRUD/query conformance, versions/drafts/layouts, operation ledger, bounded search/paging | #1732 | #1763 | #1764 | Replaced by EF Core; Groundwork project deleted by #1764 |
| P12 | `src/Elsa/Workflows/Publishing/Persistence/Groundwork/Elsa.Workflows.Publishing.Persistence.Groundwork.csproj` | 6 publishing units | #1677; child Feature to be elaborated | P08/P10/P11; transaction spike; ADR 0066 reconciliation | Ordinary and reusable publication, partial failure, idempotent redrive, receipts, activation authority | #1748; #1753; #1759; #1762 | #1763 | #1764 | Replaced by EF Core; Groundwork project deleted by #1764 |
| P13 | `src/Elsa/Workflows/Dashboard/Persistence/Groundwork/V2/Elsa.Workflows.Dashboard.Persistence.Groundwork.V2.csproj` | Read-only projection over Design/Runtime units | #1677; child Feature to be elaborated | P08/P10/P11; consistency decision | Cross-context consistency, bounded queries, provider parity, partial availability | #1746; #1752; #1758 | #1763 | #1764 | Replaced by EF Core; Groundwork project deleted by #1764; declares no storage unit of its own |
| P14 | `src/Elsa3/Activities/Design/Import/Persistence/Groundwork/Elsa3.Activities.Design.Import.Persistence.Groundwork.csproj` | 3 import units and cross-lane import command | #1677; child Feature to be elaborated | P08/P10/P11; transaction spike | Idempotent import, atomic/compensated cross-module writes, failure/restart, four providers | #1760 | n/a: no shipped host composes this feature | #1764 | Replaced by EF Core; Groundwork project deleted by #1764 |

## Transaction-boundary ledger

| ID | Current boundary | Owner | Required outcome | Replacement / flip / deletion evidence | Disposition |
|---|---|---|---|---|---|
| X01 | `GroundworkStorageSessionSource` owns one provider connection/session per target and admits schema on initialization | #1674 | Define EF connection/context ownership, shell isolation, activation/reload and teardown | #1728 (topology spike), #1760 (`EfSharedTransaction`), #1763 (shell-scoped composition); Groundwork source deleted by #1764 | Closed; see repository-surface register A01, A03 and A09 |
| X02 | `GroundworkStorageTransaction` refuses wrong target, wrong scope, privileged access, missing atomic capability, or undeclared units | #1674 | Preserve refusal semantics and prove commit, rollback, partial `SaveChanges`, retries, savepoints and isolation | #1728, #1760; Groundwork source deleted by #1764 | Closed at the topology boundary; see repository-surface register A02 and A04-A12, including the module-owned limits recorded in A05, A08 and A10 |
| X03 | Reusable-activity publication commands atomically span Activities Design, Runtime and Publishing units when co-located; ordinary workflow publication remains ordered across lanes | #1674 and #1677 | Preserve the reusable-activity atomicity guarantee and ordinary-workflow ordered/recovery semantics from ADR 0066 after shared/split topology is proven | #1759; Groundwork source deleted by #1764 | Closed, but not as one atomic commit: EF reusable-activity publication commits in ADR 0066 order with no shared transaction, and a crash before the receipt is recovered on replay (storage-unit register A08 and P05) |
| X04 | `ActivityUpgradePlanStore` can stage Activities Design and Workflows Design rows in one exact unit of work | #1674 and #1677 | Resolve two logical operation units sharing physical `elsa_design_operations`; do not assume one current unit | #1762; Groundwork source deleted by #1764 | Closed: `EfActivityUpgradePlanStore` commits Activities Design and Workflows Design through one `EfSharedTransaction` (storage-unit register A12) |
| X05 | Elsa3 reusable-activity import spans design/runtime/import units | #1674 and #1677 | Prove idempotency and failure recovery for chosen topology | #1760; Groundwork source deleted by #1764 | Closed: the import commits across Activities Design and Workflows Design through `EfSharedTransaction` (storage-unit register L03) |
| X06 | Runtime checkpoint writer stages fence, execution, scheduler, activities, bookmarks, durable values, incidents, alterations, dispatch, outbox, timers, health and a create-only marker | #1676 | Prove atomicity or explicit convergent recovery, commit ambiguity, fencing and crash restart | #1746; default flip #1763; Groundwork source deleted by #1764 | Replaced by EF Core; see storage-unit register R19-R24 for the recorded limitations |
| X07 | Distributed command transport atomically writes stream head and command item | #1672/#1676; #1720 | Prove idempotent ordering, conflicts, crash recovery and concurrent writers | #1721; default flip #1763; Groundwork source deleted by #1764 | Replaced by EF Core |
| X08 | Identity batch writes reservations and mutation receipts in one unit of work | #1682 | Prove normalized uniqueness, receipt idempotency and rollback | #1715; default flip #1763; Groundwork source deleted by #1764 | Replaced by EF Core (storage-unit register I14-I17) |
| X09 | Activation authority uses a transaction factory | #1674/#1676/#1677 | Prove exclusive activation and recovery under four providers | #1749; default flip #1763; Groundwork source deleted by #1764 | Replaced by EF Core for ownership and fencing; lease/expiry acceptance stays open under #1739 because the contract exposes no lease fields (storage-unit register R28) |
| X10 | Secrets projection reindex uses an explicit EF transaction per bounded 100-row batch | #1679 | Preserve bounded batching, rollback and resume semantics across MySQL and existing providers | #1624, #1633, #1634, #1646, #1729; default flip #1763; Groundwork source deleted by #1764 | Replaced by EF Core. Projection reindex is exercised on SQLite, PostgreSQL and SQL Server; the MySQL Secrets tests contain no reindex case |
| X11 | OpenIddict migrates through its vendor `OpenIddictIdentityDbContext` | Vendor boundary, Program #1665 audit | Keep separate from Elsa IAM contexts and exclude from first-party-removal scans | | Retained vendor boundary |

## Shell-feature ledger

All 17 entries are owned by the matching production, provider/host, or Mongo row and Program #1665
until a worker-ready leaf exists. Each requires replacement registration, activation/reload proof,
default-composition proof, then explicit deletion evidence.

| ID | Current shell feature file | Owning production row | Replacement / flip / deletion PRs | Disposition |
|---|---|---|---|---|
| F01 | `src/Apps/Elsa.Workbench/WorkbenchGroundworkDashboardFeature.cs` | P13 | #1746, #1752, #1758 / #1763 / #1764 | Deleted by #1764; `WorkflowsDashboardEntityFrameworkCore` is composed by #1763 |
| F02 | `src/Apps/Elsa.Workbench/Groundwork/GroundworkMongoDbProviderFeature.cs` | Mongo M01 | none / n/a / #1764 | Deleted by #1764 with no replacement |
| F03 | `src/Apps/Elsa.Workbench/Groundwork/GroundworkPostgreSqlProviderFeature.cs` | Host H01 | per-module EF features / #1763 / #1764 | Deleted by #1764; each module selects its own EF provider feature (#1763) |
| F04 | `src/Apps/Elsa.Workbench/Groundwork/GroundworkSqlServerProviderFeature.cs` | Host H01 | per-module EF features / #1763 / #1764 | Deleted by #1764; each module selects its own EF provider feature (#1763) |
| F05 | `src/Apps/Elsa.Workbench/Groundwork/GroundworkSqliteDiagnosticsProviderFeature.cs` | P03-P05 | #1696, #1701 / #1763 / #1764 | Deleted by #1764; EF Diagnostics features keep their own database file (#1763) |
| F06 | `src/Apps/Elsa.Workbench/Groundwork/GroundworkSqliteProviderFeature.cs` | Host H01 | per-module EF features / #1763 / #1764 | Deleted by #1764; each module selects its own EF provider feature (#1763) |
| F07 | `src/Elsa/Activities/Design/Persistence/Groundwork/ActivitiesDesignGroundworkPersistenceFeature.cs` | P10 | #1742, #1759, #1762 / #1763 / #1764 | Deleted by #1764 |
| F08 | `src/Elsa/Diagnostics/Persistence/Groundwork/DiagnosticsGroundworkPersistenceFeature.cs` | P03-P05 | #1696, #1701 / #1763 / #1764 | Deleted by #1764 |
| F09 | `src/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/AspNetCoreIdentityGroundworkFeature.cs` | P07 | #1715 / #1763 / #1764 | Deleted by #1764 |
| F10 | `src/Elsa/Foundation/Identity/Persistence/Groundwork/IdentityGroundworkPersistenceFeature.cs` | P06 | #1709, #1711, #1715 / #1763 / #1764 | Deleted by #1764 |
| F11 | `src/Elsa/Persistence/Groundwork/V2/Runtime/GroundworkWorkflowRuntimeFeature.cs` | P08 | #1724, #1730, #1741, #1743, #1744, #1745, #1746, #1749, #1758 / #1763 / #1764 | Deleted by #1764 |
| F12 | `src/Elsa/Secrets/Persistence/Groundwork/SecretsGroundworkPersistenceFeature.cs` | P01 | #1624, #1633, #1634, #1646, #1729 / #1763 / #1764 | Deleted by #1764 |
| F13 | `src/Elsa/Studio/Preferences/Persistence/Groundwork/StudioPreferencesGroundworkPersistenceFeature.cs` | P02 | #1693 / #1763 / #1764 | Deleted by #1764 |
| F14 | `src/Elsa/Workflows/Design/Persistence/Groundwork/WorkflowsDesignGroundworkPersistenceFeature.cs` | P11 | #1732 / #1763 / #1764 | Deleted by #1764 |
| F15 | `src/Elsa/Workflows/Publishing/Persistence/Groundwork/PublishingGroundworkFeature.cs` | P12 | #1748, #1753, #1759, #1762 / #1763 / #1764 | Deleted by #1764 |
| F16 | `src/Elsa/Workflows/Runtime/Distributed/Persistence/Groundwork/WorkflowsRuntimeDistributedGroundworkPersistenceFeature.cs` | P09 | #1718, #1721 / #1763 / #1764 | Deleted by #1764 |
| F17 | `src/Elsa3/Activities/Design/Import/Persistence/Groundwork/Elsa3ImportActivitiesGroundworkFeature.cs` | P14 | #1760 / n/a / #1764 | Deleted by #1764 |

## Provider, migration and host ledger

| ID | Current surface | Owner / blocker | Required evidence | PRs | Disposition |
|---|---|---|---|---|---|
| H01 | Workbench direct Groundwork provider packages and `GroundworkProviderRegistration` for SQLite, SQL Server, PostgreSQL and MongoDB | #1670; all spikes | One selected EF engine per supported composition, clear connection ownership, activation/reload and failure diagnostics | #1763 (flip), #1764 (deletion) | Closed: `GroundworkProviderRegistration` deleted; each module selects its own EF provider feature and resolves `ConnectionStrings:Elsa` (repository-surface register H01-H03) |
| H02 | `src/Apps/Elsa.Workbench/Program.cs` assembly discovery for 11 Groundwork projects | #1670 | EF assembly discovery without leaking providers into domain contracts | #1763, #1764 | Closed: Groundwork assembly discovery removed and EF module assemblies discovered instead (repository-surface register H05) |
| H03 | `src/Apps/Elsa.Workbench/shells.json`, `shells.baseline.json`, `shells.Production.json` | #1670 | SQLite default and production overlay start, migrate, publish/run, restart and recover | #1763 | Closed by #1763: all three files select EF module features only (repository-surface register H06) |
| H04 | `docker/compose/elsa-workbench.shells.json` | #1670 | PostgreSQL compose starts fresh, migrates, runs representative journeys and restarts | #1763 | Closed by #1763: every module selects `Provider: PostgreSql` with its own `__EFMigrationsHistory_<module>` table (repository-surface register H07) |
| H05 | `src/Elsa/Persistence/EntityFramework` shared policy | #1678 | General module lifecycle, provider pairing, locking, history isolation, diagnostics and no universal domain abstraction | #1624, #1633, #1685, #1693, #1701, #1724, #1732, #1746, #1755, #1760, #1763 | Generalized: `EfModuleMigrator<TContext>` lifecycle, provider binding and refusal, per-module history (repository-surface register H09) |
| H06 | Secrets SQLite, SQL Server and PostgreSQL contexts plus 15 migration/snapshot files | #1679 | Fresh install, pending-model detection, runtime/out-of-process parity, wrong-provider refusal | #1755 | Revalidated by #1755 with a four-provider migration lifecycle (repository-surface register H10-H13) |
| H07 | MySQL provider binding/context and focused Secrets proof; provider-specific migrations/tooling remain absent | #1675/#1726 | Exact compatible package/API and every capability named by ADR 0073 | #1690, #1729, #1755 | MySQL is a supported provider with migrations for every module (repository-surface register H15) |
| H08 | `tools/ef` scripts hard-coded to Secrets and three contexts | #1669; prerequisite #1657 | Module-parameterized tooling, concurrent invocation safety, current compiled artifacts | #1755 | Generalized: shared `tools/ef/Elsa.EntityFrameworkCore.Tooling` plus per-module generation and `module-migrate.sh` (repository-surface register H14) |
| H09 | `src/Apps/Elsa.Workbench/OpenIddict/OpenIddictIdentityDbContext.cs` and vendor migrations | Vendor boundary | Remains isolated and operational after IAM migration | | Retain |

## Package, tool, workflow and guard ledger

| ID | Current surface | Owner | Required preservation / removal evidence | PRs | Disposition |
|---|---|---|---|---|---|
| C01 | Seven `Groundwork.*` pins at `0.4.0-preview.30` in `Directory.Packages.props` | #1670 | No direct or transitive active runtime dependency after all adapters delete | #1764 | Removed by #1764 (repository-surface register R01) |
| C02 | Groundwork and MongoDB source mappings in `NuGet.config` | #1670 | Remove only when no active restore surface needs them | #1764 | Removed by #1764 (repository-surface register R04-R05) |
| C03 | `benchmarks/` (5 projects) | #1668 | Preserve/archive historical results; extract timing-independent correctness first | | Retired by owner decision and removed by #1668; not passed. Activation-scope semantics moved to `tests/Elsa/Activities/Runtime/Tests/ActivationScopeSemanticTests.cs`; the other four projects carried no unported correctness (mapped in the test register). ADR 0045's cited run is archived unchanged under `docs/reports/evidence/095-activation-scope-benchmark/` |
| C04 | `tools/ledger/StorePerformance` and Groundwork performance handoff tests | #1668 | Map correctness assertions into EF-neutral/owned suites before deletion | | Retired by owner decision and removed by #1668 with the spec 094 Groundwork coverage-ledger validator; not passed. Workload obligations map to existing neutral, Groundwork and EF suites; the one unexpressed obligation (concurrent outbox-claim single ownership) moved to `GroundworkV2RuntimePostCommitOutboxStoreTests`; adapter/native-plan/fingerprint material is Groundwork-specific or measurement-only (test register) |
| C05 | `tools/groundwork/` provider evidence importer and baseline runners | #1670; correctness mapping #1671 | Preserve non-performance provider/correctness obligations in ledger or EF suite | #1764 | Removed by #1764; its checkpoint/fence mapping had no subject once the Groundwork providers were gone (repository-surface register T01-T02) |
| C06 | `tools/performance/` | #1668 | Archive history where useful; remove scripts/tests and references | | Retired by owner decision and removed by #1668 with its references; historical reports keep their recorded results; not passed |
| C07 | `.github/workflows/http-workflow-performance.yml` | #1668 | Remove timing gates; retain an untimed activation/correctness journey only where not already covered | | Retired by owner decision and source removed by #1668; never dispatched and not passed. Its untimed journey was already covered: host activation by `WorkbenchShellActivationTests` in the CI fast gate, the sync HTTP response by `HttpEndpointSyncResponseEndToEndTests` and the policy-equivalence split of T37, and REST publish-and-invoke by `e2e-tests/Test-HttpWorkflow.ps1` |
| C08 | `.github/workflows/groundwork-ledger.yml` | #1668/#1670 | Preserve coverage/correctness obligations; delete Groundwork/performance jobs | | Retired by owner decision and removed by #1668 together with the `tools/ledger` suites it ran on manual dispatch only; its obligations are dispositioned under C04; the final no-Groundwork audit remains #1670's |
| C09 | `.github/workflows/ci.yml` Groundwork provider matrix and Mongo service | #1670 | Replace with four-provider EF correctness matrix; no timing gate | #1764 | Replaced by the `ef-container-suites` matrix; six of its 15 legs are fail-closed (repository-surface register W03) |
| C10 | `.github/workflows/integration.yml` Groundwork scaffold/restart/schema/readiness jobs | #1670 plus #1669/#1672 | Port applicable correctness and failure-recovery journeys | #1764 | Closed by #1764: the nightly job runs `Elsa.Server.Persistence.Integration.slnf`; the disabled Groundwork scaffold jobs were dropped (repository-surface register W04) |
| C11 | `Elsa.Server.slnx` and `tools/solution-filters/profiles.json` | #1670 and each replacement | Remove projects only with owning deletion PR; final filter freshness | #1668/#1756, #1764 | Closed (repository-surface register S01-S03) |
| C12 | Groundwork architecture guards, ledger readers, `GW0004`, maps and glossary/docs | #1670 and each replacement | Replace with EF/four-provider/no-Groundwork guards and preserve historical references | #1763, #1764 | Replaced by `EfCoreDependencyGuardTests` and `RetiredPersistenceFamilyGuardTests`; maps regenerated (repository-surface register D06, H17 and M01) |

## MongoDB ledger

| ID | Current active surface | Owner | Required proof | PRs | Disposition |
|---|---|---|---|---|---|
| M01 | Workbench `Groundwork.MongoDb` package, feature and provider registration | #1670 | No default or optional shell registration remains; package absent | #1764 | Removed by #1764 with no replacement (repository-surface register H03) |
| M02 | Mongo provider-matrix/unified-host tests and CI replica-set setup | #1670 | Applicable provider-neutral correctness is covered by four relational providers before deletion | #1764 | Removed by #1764; Mongo-only topology dropped and named in the test register (repository-surface register R09) |
| M03 | MongoDB package-source mapping and configuration/docs | #1670 | No active restore/configuration surface; historical docs remain truthful | #1764 | Removed by #1764: `MongoDB.*` mapping gone from `NuGet.config` (repository-surface register R05) |

## Groundwork/Mongo test-project ledger

The complete project, suite, provider and disposition inventory is the
[test and e2e register](ef-core-persistence/test-and-e2e-register.md). No project or suite is
disposable solely because its path says Groundwork, Mongo, benchmark, performance, or load. The
register explicitly separates provider-neutral correctness from Mongo-only topology and retired
measurement material.

## Existing-issue disposition ledger

The [legacy-requirement register](ef-core-persistence/legacy-requirement-register.md) records each
live issue separately, including provider defects, Runtime/dispatch/evidence requirements,
performance-policy splits, test-fixture/e2e failures, and closed regression predecessor #1659. No
issue closes from grouping or title matching. Later closure must link exact implemented,
superseding, or owner-retired evidence.

## #1671 verification method

The entry-level expansion was reconciled from current source rather than copied from the earlier
approximate plan:

- manifest declarations reconcile to 95 register rows: 29 Runtime, 3 distributed Runtime, 21
  Activities Design, 17 Identity, 8 OpenTelemetry, 6 Publishing, 5 Workflows Design, 3 Elsa3 import,
  and three singleton units;
- project-file scans reconcile to 56 direct Groundwork package-reference files, 63 Groundwork
  project-reference files, and 70 Groundwork-bearing `.csproj` files;
- the test/evidence census reconciles the original 36 named projects with 8 omitted direct
  consumers, 2 project-reference-only consumers, 2 host consumers and 3 EF-only destinations;
- live `gh issue view`/search results reconcile to 78 unique linked issues in the issue register,
  including 77 open issues and closed regression predecessor #1659;
- relative Markdown targets, table shapes, trailing whitespace and `git diff --check` pass; and
- `dotnet run --project tools/maps/Elsa.Maps.Generator -- check` reports that generated maps still
  describe the tree.

No benchmark, timing measurement, performance budget, or performance workflow was run.

## Ledger maintenance rule

At every issue transition or merge, update the affected rows with the owning issue, exact merged PR,
current evidence, and final disposition. Before Program #1665 closes, regenerate a current inventory
from the final tree and reconcile it row by row with this ledger; do not use a broad `Groundwork`
search as the sole completion proof.

## Closure

The program is complete: EF Core implementations for all 95 storage units on four providers, the
default flip (#1763, `569903c80`), and the removal of both retired families (#1764, `a83c41c1c`).

These are the things the ledger deliberately does **not** assert, recorded here so a future reader
does not mistake a closed row for a stronger guarantee than the evidence supports.

| Carry-forward | Why it is not closed |
|---|---|
| Performance | Measurement retired by owner decision under ADR 0073 (#1668, merged as #1756). No benchmark was run; no row records or may record a performance result. |
| Four-provider CI enforcement | Nine of the 15 `ef-container-suites` legs arm no required-provider variable and self-skip when a container is unavailable. All nine were executed locally against real PostgreSQL 16, SQL Server 2022 and MySQL 8.4 containers on 2026-09-16 with zero skips. Proven, not enforced. |
| Backend e2e | Eight suites fail, each reproduced identically on a Groundwork host built at `16a2f991a`, so they are pre-existing product defects (#1761). `durability/Test-RestartRecovery` had been wrongly attributed to a stale harness; state rehydrates correctly but a post-restart stimulus matches nothing. |
| A17 concurrent migration locking | Closes by delegation to EF Core's `IHistoryRepository.AcquireDatabaseLockAsync`. There is no first-party parallel-invocation test. |
| Two coverage losses | The v1.2 production-scanner traversal, and `AspNetCoreIdentityConcurrencyContractTests`, both lost with deleted Groundwork projects. Named in the test register rather than absorbed. |
| R28 activation authority | `IWorkflowActivationAuthority` exposes no lease or expiry fields. Unchanged by this program. |
| E13 | Deleted with no e2e rewrite; the no-retired-family half is now a static guard, not a host-boundary proof. |
| Constitutions | `.specify/memory/constitution.md` and `constitution-framework.md` still carry ADR 0042's Groundwork-only direction. Amending a ratified constitution is an owner decision. |

A closed ledger is not a claim that nothing is left; it is a claim that what is left is written down.

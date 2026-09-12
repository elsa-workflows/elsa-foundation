# EF Core persistence completion ledger

Status: active auditable ledger index; #1671 completed the entry-level baseline expansion, but no
replacement, default flip, or removal disposition is complete merely because it is inventoried.

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

## Verified baseline

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
  Server, and PostgreSQL derived contexts. MySQL has no package pin, binding, context, migration,
  fixture, or test.
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
| P01 | `src/Elsa/Secrets/Persistence/Groundwork/Elsa.Secrets.Persistence.Groundwork.csproj` | 1 Secrets unit | #1679; executable predecessor #1653 | MySQL and migration spikes | Existing repository contract, OCC, normalization/search, four providers, migration lifecycle, HTTP CRUD/restart | | | | Opt-in EF exists for 3 providers; Groundwork remains default |
| P02 | `src/Elsa/Studio/Preferences/Persistence/Groundwork/Elsa.Studio.Preferences.Persistence.Groundwork.csproj` | 1 preferences unit | #1680 | MySQL and migration spikes | Contract parity, tenancy, last-write/concurrency semantics, four providers, restart/e2e | | | | Pending |
| P03 | `src/Elsa/Diagnostics/StructuredLogs/Persistence/Groundwork/Elsa.Diagnostics.StructuredLogs.Persistence.Groundwork.csproj` | 1 structured-log unit | #1681 | MySQL/migration spikes; correctness extraction from #646/#1529/#1521 | Append/idempotency, sequence/order, exact retention, bounded reads, redaction, four providers | | | | Pending |
| P04 | `src/Elsa/Diagnostics/OpenTelemetry/Persistence/Groundwork/Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.csproj` | 8 trace/log/metric units | #1681 | Same as P03 | Atomic append, idempotency, exact retention, bounded queries, failure isolation, four providers | | | | Pending |
| P05 | `src/Elsa/Diagnostics/Persistence/Groundwork/Elsa.Diagnostics.Persistence.Groundwork.csproj` | Umbrella composition | #1681 | P03-P04 | Selected provider composition, shell activation/reload, host-boundary e2e | | | | Pending |
| P06 | `src/Elsa/Foundation/Identity/Persistence/Groundwork/Elsa.Foundation.Identity.Persistence.Groundwork.csproj` | 17 IAM units shared with P07 | #1682 | MySQL, transaction, migration spikes | Tenant isolation, normalized unique lookup, reservations/receipts atomicity, conflict behavior, four providers | | | | Pending |
| P07 | `src/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.csproj` | ASP.NET Identity adapters over IAM units | #1682 | P06; OpenIddict boundary must remain separate | Full Identity store contracts, sign-in/restart, concurrency stamps, normalized lookup, tenant isolation | | | | Pending; do not merge with OpenIddict context |
| P08 | `src/Elsa/Persistence/Groundwork/V2/Elsa.Persistence.Groundwork.V2.csproj` | Shared Groundwork policy plus 29 Runtime units | #1672; proving slice #1676 | All four spikes; Runtime proving slice | Checkpoint atomicity, marker reconciliation, leases/fencing, queues, timers, dispatch/outbox, recovery, contention | | | | Critical path; pending |
| P09 | `src/Elsa/Workflows/Runtime/Distributed/Persistence/Groundwork/Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.csproj` | 3 units: placement, command-stream head, command transport | #1672; proving slice #1676 | Runtime proving slice and transaction topology | Placement ownership/fencing; atomic stream-head + command write; redelivery, crash/restart | | | | Pending |
| P10 | `src/Elsa/Activities/Design/Persistence/Groundwork/Elsa.Activities.Design.Persistence.Groundwork.csproj` | 21 Activities Design units | #1677; child Feature to be elaborated | Transaction/migration spikes; Runtime contracts stable | CRUD/query conformance, temporal projections, operation ledger, upgrade plans, four providers | | | | Pending |
| P11 | `src/Elsa/Workflows/Design/Persistence/Groundwork/Elsa.Workflows.Design.Persistence.Groundwork.csproj` | 5 Workflows Design units | #1677; child Feature to be elaborated | Transaction/migration spikes | CRUD/query conformance, versions/drafts/layouts, operation ledger, bounded search/paging | | | | Pending |
| P12 | `src/Elsa/Workflows/Publishing/Persistence/Groundwork/Elsa.Workflows.Publishing.Persistence.Groundwork.csproj` | 6 publishing units | #1677; child Feature to be elaborated | P08/P10/P11; transaction spike; ADR 0066 reconciliation | Ordinary and reusable publication, partial failure, idempotent redrive, receipts, activation authority | | | | Pending |
| P13 | `src/Elsa/Workflows/Dashboard/Persistence/Groundwork/V2/Elsa.Workflows.Dashboard.Persistence.Groundwork.V2.csproj` | Read-only projection over Design/Runtime units | #1677; child Feature to be elaborated | P08/P10/P11; consistency decision | Cross-context consistency, bounded queries, provider parity, partial availability | | | | Pending |
| P14 | `src/Elsa3/Activities/Design/Import/Persistence/Groundwork/Elsa3.Activities.Design.Import.Persistence.Groundwork.csproj` | 3 import units and cross-lane import command | #1677; child Feature to be elaborated | P08/P10/P11; transaction spike | Idempotent import, atomic/compensated cross-module writes, failure/restart, four providers | | | | Pending |

## Transaction-boundary ledger

| ID | Current boundary | Owner | Required outcome | Replacement / flip / deletion evidence | Disposition |
|---|---|---|---|---|---|
| X01 | `GroundworkStorageSessionSource` owns one provider connection/session per target and admits schema on initialization | #1674 | Define EF connection/context ownership, shell isolation, activation/reload and teardown | | Pending |
| X02 | `GroundworkStorageTransaction` refuses wrong target, wrong scope, privileged access, missing atomic capability, or undeclared units | #1674 | Preserve refusal semantics and prove commit, rollback, partial `SaveChanges`, retries, savepoints and isolation | | Pending |
| X03 | Reusable-activity publication commands atomically span Activities Design, Runtime and Publishing units when co-located; ordinary workflow publication remains ordered across lanes | #1674 and #1677 | Preserve the reusable-activity atomicity guarantee and ordinary-workflow ordered/recovery semantics from ADR 0066 after shared/split topology is proven | | Pending |
| X04 | `ActivityUpgradePlanStore` can stage Activities Design and Workflows Design rows in one exact unit of work | #1674 and #1677 | Resolve two logical operation units sharing physical `elsa_design_operations`; do not assume one current unit | | Pending |
| X05 | Elsa3 reusable-activity import spans design/runtime/import units | #1674 and #1677 | Prove idempotency and failure recovery for chosen topology | | Pending |
| X06 | Runtime checkpoint writer stages fence, execution, scheduler, activities, bookmarks, durable values, incidents, alterations, dispatch, outbox, timers, health and a create-only marker | #1676 | Prove atomicity or explicit convergent recovery, commit ambiguity, fencing and crash restart | | Pending |
| X07 | Distributed command transport atomically writes stream head and command item | #1672/#1676 | Prove idempotent ordering, conflicts, crash recovery and concurrent writers | | Pending |
| X08 | Identity batch writes reservations and mutation receipts in one unit of work | #1682 | Prove normalized uniqueness, receipt idempotency and rollback | | Pending |
| X09 | Activation authority uses a transaction factory | #1674/#1676/#1677 | Prove exclusive activation and recovery under four providers | | Pending |
| X10 | Secrets projection reindex uses an explicit EF transaction per bounded 100-row batch | #1679 | Preserve bounded batching, rollback and resume semantics across MySQL and existing providers | | Existing opt-in evidence; four-provider/default proof pending |
| X11 | OpenIddict migrates through its vendor `OpenIddictIdentityDbContext` | Vendor boundary, Program #1665 audit | Keep separate from Elsa IAM contexts and exclude from first-party-removal scans | | Retained vendor boundary |

## Shell-feature ledger

All 17 entries are owned by the matching production, provider/host, or Mongo row and Program #1665
until a worker-ready leaf exists. Each requires replacement registration, activation/reload proof,
default-composition proof, then explicit deletion evidence.

| ID | Current shell feature file | Owning production row | Replacement / flip / deletion PRs | Disposition |
|---|---|---|---|---|
| F01 | `src/Apps/Elsa.Workbench/WorkbenchGroundworkDashboardFeature.cs` | P13 | | Pending |
| F02 | `src/Apps/Elsa.Workbench/Groundwork/GroundworkMongoDbProviderFeature.cs` | Mongo M01 | | Remove; no EF Mongo replacement |
| F03 | `src/Apps/Elsa.Workbench/Groundwork/GroundworkPostgreSqlProviderFeature.cs` | Host H01 | | Pending EF provider composition |
| F04 | `src/Apps/Elsa.Workbench/Groundwork/GroundworkSqlServerProviderFeature.cs` | Host H01 | | Pending EF provider composition |
| F05 | `src/Apps/Elsa.Workbench/Groundwork/GroundworkSqliteDiagnosticsProviderFeature.cs` | P03-P05 | | Pending EF diagnostics composition |
| F06 | `src/Apps/Elsa.Workbench/Groundwork/GroundworkSqliteProviderFeature.cs` | Host H01 | | Pending EF provider composition |
| F07 | `src/Elsa/Activities/Design/Persistence/Groundwork/ActivitiesDesignGroundworkPersistenceFeature.cs` | P10 | | Pending |
| F08 | `src/Elsa/Diagnostics/Persistence/Groundwork/DiagnosticsGroundworkPersistenceFeature.cs` | P03-P05 | | Pending |
| F09 | `src/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/AspNetCoreIdentityGroundworkFeature.cs` | P07 | | Pending |
| F10 | `src/Elsa/Foundation/Identity/Persistence/Groundwork/IdentityGroundworkPersistenceFeature.cs` | P06 | | Pending |
| F11 | `src/Elsa/Persistence/Groundwork/V2/Runtime/GroundworkWorkflowRuntimeFeature.cs` | P08 | | Pending |
| F12 | `src/Elsa/Secrets/Persistence/Groundwork/SecretsGroundworkPersistenceFeature.cs` | P01 | | Pending |
| F13 | `src/Elsa/Studio/Preferences/Persistence/Groundwork/StudioPreferencesGroundworkPersistenceFeature.cs` | P02 | | Pending |
| F14 | `src/Elsa/Workflows/Design/Persistence/Groundwork/WorkflowsDesignGroundworkPersistenceFeature.cs` | P11 | | Pending |
| F15 | `src/Elsa/Workflows/Publishing/Persistence/Groundwork/PublishingGroundworkFeature.cs` | P12 | | Pending |
| F16 | `src/Elsa/Workflows/Runtime/Distributed/Persistence/Groundwork/WorkflowsRuntimeDistributedGroundworkPersistenceFeature.cs` | P09 | | Pending |
| F17 | `src/Elsa3/Activities/Design/Import/Persistence/Groundwork/Elsa3ImportActivitiesGroundworkFeature.cs` | P14 | | Pending |

## Provider, migration and host ledger

| ID | Current surface | Owner / blocker | Required evidence | PRs | Disposition |
|---|---|---|---|---|---|
| H01 | Workbench direct Groundwork provider packages and `GroundworkProviderRegistration` for SQLite, SQL Server, PostgreSQL and MongoDB | #1670; all spikes | One selected EF engine per supported composition, clear connection ownership, activation/reload and failure diagnostics | | Pending |
| H02 | `src/Apps/Elsa.Workbench/Program.cs` assembly discovery for 11 Groundwork projects | #1670 | EF assembly discovery without leaking providers into domain contracts | | Pending |
| H03 | `src/Apps/Elsa.Workbench/shells.json`, `shells.baseline.json`, `shells.Production.json` | #1670 | SQLite default and production overlay start, migrate, publish/run, restart and recover | | Pending |
| H04 | `docker/compose/elsa-workbench.shells.json` | #1670 | PostgreSQL compose starts fresh, migrates, runs representative journeys and restarts | | Pending |
| H05 | `src/Elsa/Persistence/EntityFramework` shared policy | #1678 | General module lifecycle, provider pairing, locking, history isolation, diagnostics and no universal domain abstraction | | Existing Secrets-specific foundation; generalization pending |
| H06 | Secrets SQLite, SQL Server and PostgreSQL contexts plus 15 migration/snapshot files | #1679 | Fresh install, pending-model detection, runtime/out-of-process parity, wrong-provider refusal | | Existing opt-in evidence; revalidate current head |
| H07 | Missing MySQL provider, binding, context, migrations, fixture and tests | #1675 | Exact compatible package/API and every capability named by ADR 0073 | | Absent; critical blocker |
| H08 | `tools/ef` scripts hard-coded to Secrets and three contexts | #1669; prerequisite #1657 | Module-parameterized tooling, concurrent invocation safety, current compiled artifacts | | Pending |
| H09 | `src/Apps/Elsa.Workbench/OpenIddict/OpenIddictIdentityDbContext.cs` and vendor migrations | Vendor boundary | Remains isolated and operational after IAM migration | | Retain |

## Package, tool, workflow and guard ledger

| ID | Current surface | Owner | Required preservation / removal evidence | PRs | Disposition |
|---|---|---|---|---|---|
| C01 | Seven `Groundwork.*` pins at `0.4.0-preview.30` in `Directory.Packages.props` | #1670 | No direct or transitive active runtime dependency after all adapters delete | | Pending |
| C02 | Groundwork and MongoDB source mappings in `NuGet.config` | #1670 | Remove only when no active restore surface needs them | | Pending |
| C03 | `benchmarks/` (5 projects) | #1668 | Preserve/archive historical results; extract timing-independent correctness first | | Retirement selected; correctness mapping and removal pending; not passed |
| C04 | `tools/ledger/StorePerformance` and Groundwork performance handoff tests | #1668 | Map correctness assertions into EF-neutral/owned suites before deletion | | Measurement retirement selected; correctness mapping and removal pending |
| C05 | `tools/groundwork/` provider evidence importer and baseline runners | #1670; correctness mapping #1671 | Preserve non-performance provider/correctness obligations in ledger or EF suite | | Pending |
| C06 | `tools/performance/` | #1668 | Archive history where useful; remove scripts/tests and references | | Retirement selected; removal pending; not passed |
| C07 | `.github/workflows/http-workflow-performance.yml` | #1668 | Remove timing gates; retain an untimed activation/correctness journey only where not already covered | | Disabled remotely; source removal pending |
| C08 | `.github/workflows/groundwork-ledger.yml` | #1668/#1670 | Preserve coverage/correctness obligations; delete Groundwork/performance jobs | | Active today; pending reconciliation |
| C09 | `.github/workflows/ci.yml` Groundwork provider matrix and Mongo service | #1670 | Replace with four-provider EF correctness matrix; no timing gate | | Pending |
| C10 | `.github/workflows/integration.yml` Groundwork scaffold/restart/schema/readiness jobs | #1670 plus #1669/#1672 | Port applicable correctness and failure-recovery journeys | | Pending |
| C11 | `Elsa.Server.slnx` and `tools/solution-filters/profiles.json` | #1670 and each replacement | Remove projects only with owning deletion PR; final filter freshness | | Pending |
| C12 | Groundwork architecture guards, ledger readers, `GW0004`, maps and glossary/docs | #1670 and each replacement | Replace with EF/four-provider/no-Groundwork guards and preserve historical references | | Pending |

## MongoDB ledger

| ID | Current active surface | Owner | Required proof | PRs | Disposition |
|---|---|---|---|---|---|
| M01 | Workbench `Groundwork.MongoDb` package, feature and provider registration | #1670 | No default or optional shell registration remains; package absent | | Remove without replacement |
| M02 | Mongo provider-matrix/unified-host tests and CI replica-set setup | #1670 | Applicable provider-neutral correctness is covered by four relational providers before deletion | | Remove after mapping |
| M03 | MongoDB package-source mapping and configuration/docs | #1670 | No active restore/configuration surface; historical docs remain truthful | | Pending |

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

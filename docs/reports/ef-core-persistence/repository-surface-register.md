# EF Core persistence repository-surface register

Status: active completion evidence for [#1671](https://github.com/elsa-workflows/elsa-foundation/issues/1671).

Snapshot: `main` at `7a952efcf8d53472d7d4e7e3fd7b51d7a808c1c8` (2026-09-12).
This register covers package and project references, provider configuration, migration/schema
artifacts, Workbench/host wiring, tools, workflows/alerts, solution membership, generated maps,
architecture guards, MongoDB surfaces, and performance infrastructure. Historical ADRs, reports,
specs, and evidence remain truthful historical records unless a row explicitly identifies an active
claim that needs qualification.

Every row must end with a merged replacement/deletion PR or an owner-authorized retained/historical
disposition. `Retire` means retired by owner policy and never means performance passed.

Where a table combines replacement/deletion evidence into one PR cell, record all applicable PRs
there explicitly. Default-flip is `N/A` for packages, tools, workflows, alerts, generated artifacts,
transaction questions and historical documents; host/composition rows carry a distinct default-flip
cell. Dependencies are the named owner plus any prerequisite stated in the required-outcome cell.

## Dependency and restore surfaces

| ID | Current surface | Exact inventory | Owner | Required outcome / evidence | Replacement PR | Deletion PR | Disposition |
|---|---|---|---|---|---|---|---|
| R01 | Central Groundwork versions | Seven pins in `Directory.Packages.props`: `Groundwork.Kernel`, `Groundwork.Query.Model`, `Groundwork.Store`, `Groundwork.MongoDb`, `Groundwork.PostgreSql`, `Groundwork.Sqlite`, `Groundwork.SqlServer`, all `0.4.0-preview.30` | #1670 | Remove after no active direct/transitive runtime dependency remains | | | Pending |
| R02 | Mongo direct test packages | `MongoDB.Driver` and `Testcontainers.MongoDb` pins in `Directory.Packages.props` | #1670 | Remove after neutral tests move and Mongo fixtures/topology are deleted | | | Pending |
| R03 | Benchmark package | `BenchmarkDotNet` pin in `Directory.Packages.props` | #1668 | Remove after all benchmark projects are deleted and correctness is extracted | | | Retire; pending extraction/removal |
| R04 | Groundwork package source | `Groundwork Preview` source and `Groundwork.*` mapping in `NuGet.config` | #1670 | Remove after no restore surface needs it | | | Pending |
| R05 | Mongo/benchmark mappings | `MongoDB.*` and `BenchmarkDotNet*` mappings in `NuGet.config` | #1670/#1668 | Remove with their last active package | | | Pending |
| R06 | Direct Groundwork package references | Exactly 56 project files: 3 benchmark rows B01-B03 below; 13 source projects (Workbench plus production rows P01-P04, P06-P12 and P14 in the parent ledger); and test rows T01-T05, T07-T13, T15-T18 and T21-T44 in `test-and-e2e-register.md` | Owning replacement plus #1670/#1668 | Zero active `PackageReference Include="Groundwork.*"`; restore graph remains valid | | | Pending |
| R07 | Groundwork project references | Exactly 63 project files at snapshot; their complete owners are the production ledger, test rows T01-T46 where applicable, B01-B03, and tools rows L01-L03 below | Owning replacement plus #1670/#1668 | Zero active first-party project reference to a deleted Groundwork project; build graph and solution filters remain valid | | | Pending |
| R08 | Groundwork-bearing project files | 70 `.csproj` files contain a Groundwork package/project reference or Groundwork-bearing path | #1670 | Final active-code scan returns zero after owned deletions; historical docs are excluded | | | Pending |
| R09 | Mongo package/test surface | 18 files carry `MongoDB.Driver`, `Testcontainers.MongoDb`, or direct Mongo-provider dependencies; the 14 test projects are identified by provider-matrix/Mongo dispositions in the test register | #1670 | Neutral contracts cover four relational providers before Mongo artifacts disappear | | | Pending |

The R06 count is reproducible with
`rg -l -g '*.csproj' '<PackageReference[^>]+Include="Groundwork\.' . | sort`. R07 uses the same
query with `ProjectReference` and a Groundwork path. These commands inventory dependencies only;
they are not completion proof without restore/build and runtime dependency evidence.

## Provider, migration, schema, host, and composition surfaces

| ID | Current surface | Owner | Required outcome / evidence | Replacement PR | Default-flip PR | Deletion PR | Disposition |
|---|---|---|---|---|---|---|---|
| H01 | `src/Apps/Elsa.Workbench/Elsa.Workbench.csproj` | #1670 | EF SQLite/SQL Server/PostgreSQL/MySQL packages only; no Groundwork or Mongo runtime dependency | | | | Pending |
| H02 | `src/Apps/Elsa.Workbench/Groundwork/GroundworkProviderRegistration.cs` | #1670 | One selected EF engine per shell; explicit connection/context ownership and actionable failure diagnostics | | | | Pending |
| H03 | Five Workbench provider features under `src/Apps/Elsa.Workbench/Groundwork/`: MongoDB, PostgreSQL, SQL Server, SQLite diagnostics, and SQLite | #1670 | Replace relational features with EF composition; delete Mongo feature without replacement | | | | Pending |
| H04 | `src/Apps/Elsa.Workbench/WorkbenchGroundworkDashboardFeature.cs` | #1670/#1677 child | EF-backed dashboard registration and partial-availability/consistency proof | | | | Pending |
| H05 | `src/Apps/Elsa.Workbench/Program.cs` Groundwork assembly discovery | #1670 | EF module discovery without provider types leaking into domain contracts | | | | Pending |
| H06 | `src/Apps/Elsa.Workbench/shells.json`, `shells.baseline.json`, `shells.Production.json` | #1670 | SQLite default and production overlay: migrate, activate, publish/run, restart/recover | | | | Pending |
| H07 | `docker/compose/elsa-workbench.shells.json` | #1670 | EF PostgreSQL compose shell starts fresh, migrates, runs representative journeys and restarts | | | | Pending |
| H08 | `docker/compose/docker-compose.yml`, `docker-compose.images.yml`, and `README.md` | #1670 | Remove Groundwork/Mongo services, variables and instructions; retain supported relational services | | | | Pending |
| H09 | `src/Elsa/Persistence/EntityFramework/**` shared policy | #1678 | General module lifecycle, provider binding, history isolation, locking and diagnostics; no universal domain abstraction | | | N/A | Existing Secrets-shaped foundation; generalization pending |
| H10 | Secrets SQLite migrations: initial, widen-lookup-keys, two designers and model snapshot under `Migrations/Sqlite` | #1679/#1669 | Isolated SQLite history, pending-model, runtime/out-of-process apply, concurrent lock and fresh install | | | N/A | Existing opt-in; revalidation pending |
| H11 | Secrets SQL Server migrations: initial, widen-lookup-keys, two designers and model snapshot under `Migrations/SqlServer` | #1679/#1669 | Same lifecycle evidence for SQL Server | | | N/A | Existing opt-in; revalidation pending |
| H12 | Secrets PostgreSQL migrations: initial, widen-lookup-keys, two designers and model snapshot under `Migrations/PostgreSql` | #1679/#1669 | Same lifecycle evidence for PostgreSQL | | | N/A | Existing opt-in; revalidation pending |
| H13 | Secrets provider contexts `SecretsSqliteDbContext`, `SecretsSqlServerDbContext`, `SecretsPostgreSqlDbContext` and base `SecretsDbContext` | #1679/#1678 | Correct provider/artifact pairing, context ownership/disposal and provider-neutral repository boundary | | | N/A | Existing opt-in; revalidation pending |
| H14 | `SecretsEfMigrationHostedService`, EF design-time factories, tooling project and `tools/ef/**` | #1669 after #1657 | Module-parameterized current-build tooling, concurrent invocation safety, failure before feature activation | | | | Secrets-only today; generalization pending |
| H15 | MySQL package, provider binding/API, derived contexts, migrations, fixtures and host configuration | #1675 then #1678/#1679 and module owners | Prove exact compatible provider, `DateTimeOffset`, collation/search keys, JSON, tokens/conflicts, conditional DML, transactions/enlistment, Testcontainers, fresh migrate and pending-model | | | N/A | Entire lane absent; critical blocker |
| H16 | `src/Apps/Elsa.Workbench/OpenIddict/OpenIddictIdentityDbContext.cs` and vendor migrations | Program #1665 audit | Keep vendor-owned context isolated and operational; never merge with Elsa IAM contexts | | | N/A | Retained boundary |
| H17 | `src/Elsa/Directory.Build.targets` rule `GW0004` | #1670/#1678 | Replace Groundwork-positive dependency policy with EF layering and final no-Groundwork guard | | | | Pending |
| H18 | `src/Elsa/Studio/Preferences/Persistence/EntityFrameworkCore/**` and its focused test projects | #1680 | Provider-neutral EF Core + Relational production closure; provider packages only in SQLite/live-provider tests; scope isolation, canonical revisions, optimistic concurrency, DI replacement, restart and four-provider binding remain module-owned; no migration artifacts in this slice | #1693 | | | Opt-in implementation merged; Groundwork remains default and migration/default-flip/deletion gates are deferred |
| H19 | `src/Elsa/Diagnostics/StructuredLogs/Persistence/EntityFrameworkCore/**` and its focused test projects | #1695 | Provider-neutral EF Core + Relational production closure; provider packages only in SQLite/live-provider tests; scoped binding, high-water, append ledger, cursor and DI replacement remain module-owned; no migration artifacts in this slice | #1696 | | | Opt-in implementation delivered by #1696; Groundwork remains default and migration/default-flip/deletion gates are deferred |

## Transaction and migration acceptance register

These rows make architecture-spike closure explicit; broad module implementation may not infer them
from a single happy-path transaction test.

| ID | Required decision/proof | Owner | Evidence required | Spike/implementation PR | Disposition |
|---|---|---|---|---|---|
| A01 | Shared `DbConnection` ownership | #1674 | Named owner, open/close rules, provider behavior and failure teardown | | Pending |
| A02 | Shared `DbTransaction` ownership and enlistment | #1674 | Same physical connection/transaction proof across participating contexts; wrong-target refusal | | Pending |
| A03 | Context construction, factories and disposal | #1674/#1678 | Factory behavior inside/outside a transaction, no double-dispose/leak, shell-scoped lifetime | | Pending |
| A04 | Transaction lifetime, commit and rollback | #1674 | Success, explicit rollback, exception rollback and commit ambiguity | | Pending |
| A05 | Partial `SaveChanges` failure | #1674 | Earlier context writes do not escape; retry/reconciliation behavior is explicit | | Pending |
| A06 | Retries and execution strategies | #1674 | Provider-specific compatible strategy; no replay of unsafe side effects | | Pending |
| A07 | Savepoints | #1674 | Supported/unsupported provider behavior and rollback-to-savepoint semantics | | Pending |
| A08 | Isolation levels | #1674 | Chosen level per topology and concurrency anomaly coverage | | Pending |
| A09 | Shell isolation, activation, teardown and reload | #1674/#1670 | Per-shell resources, deterministic teardown, reactivation and config change | | Pending |
| A10 | Tenant boundaries | #1674 | No cross-tenant enlistment/read/write; identity included in transaction scope | | Pending |
| A11 | Migration ordering | #1669/#1674 | Deterministic module order and explicit dependency/failure behavior | | Pending |
| A12 | Split-database refusal | #1674 | Cross-module atomic operation refuses incompatible targets before writes | | Pending |
| A13 | SQLite WAL, busy handling and locking | #1674 | Contending callers, busy timeout/retry policy, rollback and no deadlock/hang | | Pending |
| A14 | Provider/migration-artifact pairing | #1669 | Wrong-provider refusal before schema mutation | | Pending |
| A15 | One migration set and history table per module/provider context | #1669/#1678 | Isolated histories; no context consumes another provider/module snapshot | | Pending |
| A16 | Runtime and out-of-process migration apply/validate | #1669 | Same current artifacts and pending-model result in both modes | | Pending |
| A17 | Concurrent migration locking | #1669 after #1657 | Parallel invocation has one safe winner or serialized success; no corrupted/false-current state | | Pending |
| A18 | Fresh install and pending-model detection | #1669/#1678 | Empty database reaches current model; drift fails validation | | Pending |
| A19 | Failure before feature activation | #1669/#1670 | Shell feature does not activate on migration failure | | Pending |
| A20 | No cross-module schema marking | #1669/#1678 | One module cannot mark another module/provider history current | | Pending |

## Tools, benchmarks, workflows, alerts, solutions, and maps

| ID | Current surface | Exact scope | Owner | Required outcome / evidence | Replacement/deletion PR | Disposition |
|---|---|---|---|---|---|---|
| B01 | `benchmarks/Elsa.Diagnostics.OpenTelemetry.TraceListBenchmark` | One benchmark project | #1668/#1681 | Extract any bounded-query correctness; preserve historical receipts; delete project | | Retire pending extraction |
| B02 | `benchmarks/Elsa.Groundwork.StorePerformance.AdapterHost` | One Groundwork adapter-host benchmark project | #1668/#1670 | Extract adapter/protocol correctness; delete project | | Retire pending extraction |
| B03 | `benchmarks/Elsa/Workflows/Runtime/Benchmarks` | One Runtime benchmark project | #1668/#1672 | Move semantic activation/recovery correctness; delete project | | Retire pending extraction |
| B04 | `benchmarks/Elsa/Activities/Runtime/Benchmarks` | One activity-runtime benchmark project | #1668/#1672 | Move `ActivationScopeSemanticTests`; archive truthful results; delete project | | Retire pending extraction |
| B05 | `benchmarks/Elsa.Groundwork.StorePerformance.Benchmarks` | One StorePerformance benchmark project | #1668 | Extract timing-independent workload assertions; delete project | | Retire pending extraction |
| T01 | `tools/groundwork/Elsa.Groundwork.ProviderEvidenceImporter/**` | 4 tracked files | #1670 | Preserve neutral checkpoint/fence mapping if still needed; delete Groundwork importer | | Pending |
| T02 | `tools/groundwork/generate-e3-baseline.py`, `run-e3-medium-baseline.py`, `verify-e3-baseline.sh` | 3 tracked files | #1670/#1668 | Preserve correctness-only obligations elsewhere; delete Groundwork/timing baseline tooling | | Pending |
| T03 | `tools/performance/**` | 6 tracked scripts/tests | #1668 | Remove measurement tooling and references; no replacement timing gate | | Retire pending removal |
| T04 | `tools/ledger/StorePerformance/**` | 63 tracked files | #1668 plus relevant module owners | Move timing-independent correctness identified in the test register, preserve historical fixtures where useful, then remove harness | | Retire pending extraction/removal |
| L01 | `tools/ledger/Elsa.Groundwork.Ledger.Tests/Elsa.Groundwork.Ledger.Tests.csproj` | Groundwork ledger project reference | #1670/#1668 | Preserve final no-Groundwork correctness audit; remove obsolete ledger reader | | Pending |
| L02 | `tools/ledger/StorePerformance/AdapterHost/Tests/Elsa.Groundwork.StorePerformance.AdapterHost.Tests.csproj` | Groundwork project reference | #1668 | Extract correctness then remove | | Retire pending extraction |
| L03 | `tools/ledger/StorePerformance/Benchmarks/Tests/Elsa.Groundwork.StorePerformance.Benchmarks.Tests.csproj` | Groundwork project reference | #1668 | Extract correctness then remove | | Retire pending extraction |
| W01 | `.github/workflows/http-workflow-performance.yml` | Active source; remote workflow id 322696414 is disabled manually | #1668 | Delete timing/p95/Mongo/Groundwork workflow; keep only non-duplicated untimed correctness elsewhere; never dispatch | | Retire pending source removal |
| W02 | `.github/workflows/groundwork-ledger.yml` | Groundwork ledger/provider evidence workflow | #1668/#1670 | Preserve final correctness/dependency audit, remove performance/Groundwork jobs | | Pending |
| W03 | `.github/workflows/ci.yml` | Groundwork provider matrices and Mongo service | #1670 | Replace with minimum unavoidable four-provider EF correctness coverage | | Pending |
| W04 | `.github/workflows/integration.yml` | Groundwork scaffold/restart/schema/readiness jobs | #1670/#1669/#1672 | Port applicable migration, restart and failure correctness | | Pending |
| W05 | `.github/workflows/main-red-alert.yml` | Alert target invoked by CI, Integration and HTTP performance workflow | #1668/#1670 | Remove performance alert path; keep active correctness gate alerts coherent | | Pending |
| W06 | `.github/workflows/docker.yml`, `maps.yml`, `packages.yml`, `solution-filters.yml` | Active non-performance gates | #1670 | Update inputs after removals; retain only gates that describe final tree | | Retain/update |
| S01 | `Elsa.Server.slnx` | Includes five benchmark projects, Groundwork production/tests and three Groundwork tools | #1670 and owning replacements | Remove only after owned replacements/deletions; solution build graph coherent | | Pending |
| S02 | `tools/solution-filters/profiles.json` | Groundwork profile and Groundwork integration roots | #1670 | Replace roots/profile with EF equivalents | | Pending |
| S03 | Generated solution filters | `Elsa.Server.Foundation.Identity.slnf`, `Elsa.Server.Persistence.Groundwork.Integration.slnf`, `Elsa.Server.Persistence.Groundwork.slnf`, `Elsa.Server.Workbench.slnf`, `Elsa.Server.Workflows.Design.slnf`, `Elsa.Server.Workflows.Publishing.slnf`, `Elsa.Server.Workflows.Runtime.slnf` | #1670 | Regenerate and pass solution-filter freshness | | Pending |
| M01 | Generated maps containing Groundwork/Mongo | `architecture-reference-map.md`, `domain-map.md`, `extension-point-map.md`, `feature-dependency-map.md`, `feature-map.md`, `package-map.md`, `project-reference-map.md`, `spec-status-map.md`, `test-map.md` | #1670 and owning replacements | Regenerate all maps and `manifest.json` after source changes; pass authoritative freshness check | | Pending |

## Active documentation and guard surfaces

| ID | Current surface | Owner | Required disposition | PR | Status |
|---|---|---|---|---|---|
| D01 | `AGENTS.md` Groundwork/e2e and performance guidance | #1670/#1668 | Describe EF-only final gates; remove commands that invoke retired performance work; retain historical explanation only where useful | | Pending |
| D02 | `e2e-tests/README.md` | #1670 | Replace Groundwork setup/lifecycle instructions with EF provider/migration setup | | Pending |
| D03 | `docs/reference/developer-solution-filters.md` | #1670 | Replace Groundwork profile documentation after generated filters change | | Pending |
| D04 | Authentication/identity reference docs containing current Groundwork composition (`authentication-architecture.md`, `identity-configuration.md`, `identity-generators.md`) | #1682/#1670 | Update active composition while retaining OpenIddict vendor boundary | | Pending |
| D05 | Worked-example reference docs (`elsa-worked-examples.md`, `framework-examples.md`) | Owning module/#1670 | Replace current examples; preserve explicitly historical examples as historical | | Pending |
| D06 | `tests/Elsa/Architecture/EfCoreDependencyGuardTests.cs` and related architecture guards | #1678/#1670 | Enforce domain neutrality, provider isolation, correct EF layering and zero active Groundwork dependencies | | Pending |
| D07 | Historical ADRs 0042, 0065 and 0072; historical reports/specs/evidence | ADR 0073 / Program #1665 | Retain history, supersession and unpassed obligations truthfully; do not rewrite or purge merely to make a search empty | | Retain historical |
| D08 | Current program goal, ADR 0073, completion ledger and these registers | #1665/#1671 | Update at every transition/merge; final requirement-by-requirement audit | | Active |

## Final active-surface audit

Completion requires a scoped scan of active source, project graphs, restore assets, Workbench and
Docker configuration, tools, workflows, solution membership, generated maps and current docs. The
audit must distinguish active references from truthful historical provenance. It must also verify
that no Groundwork package arrives transitively at runtime and that no MongoDB composition remains.
A broad text search is a useful input, never the sole conclusion.

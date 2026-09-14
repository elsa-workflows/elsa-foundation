# EF Core transaction topology spike (#1674)

Status: bounded test-only evidence on `codex/ef-transaction-topology`, rebased
onto `origin/main` at `4d5347241f59d45706667dfd25306c5f754b04a7`
(2026-09-14). The rebase-validation head before the documentation-only
provenance correction was `943674d278239bb02b3b0620f09a2027e5885945`;
the live PR head is the authoritative exact candidate identity.

This spike proves the mechanics needed before Runtime, Design, and Publishing
contexts are multiplied. It intentionally contains no production transaction
abstraction, `TransactionScope`, module adapter, migration artifact, or
Groundwork dependency.

## Executable shape

`TopologyOperation` is a test-only operation-scoped owner. It opens one
physical `DbConnection`, begins one `DbTransaction` at the requested isolation
level, explicitly enlists three independently constructed borrower contexts
with `Database.UseTransaction`, and owns commit, rollback, and final disposal.
Connection ownership transfers to the operation at `BeginAsync`; callers do
not dispose the connection separately. Borrowers never commit or dispose the
owner resources. Enlistment refuses a different physical connection, target,
or tenant before a borrower can write. Idempotent operation disposal is tested
to roll back abandoned work and close the transferred connection once.

SQLite uses a temporary file database. Its proof covers three-lane commit,
explicit rollback, forced failure after an earlier `SaveChanges`, borrower
disposal before owner commit, savepoint rollback, target/tenant/split-connection
refusal, WAL and positive `busy_timeout`, and a contending writer that observes
SQLite busy/locked behavior. The contention assertion is state-based; no
elapsed-time, throughput, or performance claim is made.

PostgreSQL, SQL Server, and MySQL use the repository's pinned EF Core/provider
packages and Testcontainers fixtures. Each focused smoke creates a fresh
database, proves model creation, same-connection enlistment, commit, explicit
rollback, forced partial failure rollback, borrower disposal, and provider
savepoint rollback syntax. Tests are assembly-serial so native providers are
never started concurrently by this project. MySQL uses the pinned
`mysql:8.4.11` digest from the feasibility spike.

## A01-A13 acceptance mapping

The SQLite statements below are verified on the exact rebased candidate head.
The PostgreSQL, SQL Server, and MySQL statements are evidence from pre-rebase
head `d173d6d01251a51125f824c0acb27989ee3feef0`; the native-provider test paths
are unchanged by the rebase, but those three suites were not rerun on
the rebased candidate and are not exact-head evidence.

| Row | Evidence and disposition |
| --- | --- |
| A01 shared connection ownership | **Exercised in SQLite and all three native smokes.** Ownership transfers to the operation; callers do not independently dispose the connection. SQLite additionally asserts idempotent operation disposal closes the connection exactly once; native smokes do not assert a disposal count or closed state. |
| A02 transaction ownership/enlistment | **Proved in all providers.** Three borrowers explicitly call `UseTransaction`. Split-connection refusal before writes is explicitly asserted in SQLite; native smokes use one shared connection but do not duplicate that refusal assertion. |
| A03 construction/disposal | **Proved for the test topology.** Runtime, Design, and Publishing contexts are independently constructed and can be disposed before owner commit. Production factories and shell lifetime remain #1678/#1670 work. |
| A04 lifetime/commit/rollback | **Proved.** Commit makes all three rows visible; successful explicit rollback and owner teardown rollback leave no rows. Explicit rollback is terminal and idempotent after success; a provider rollback failure is treated as an unknown outcome and the operation refuses reuse. Commit exceptions are classified as unknown outcome, never success, and a second commit attempt is refused after the first attempt. A network-induced ambiguous commit was not injected. |
| A05 partial `SaveChanges` failure | **Proved for an exception after an earlier save** in SQLite and native smokes. SQLite additionally proves savepoint-scoped rollback. A provider-specific duplicate-key failure matrix remains module/provider work. |
| A06 retries/execution strategies | **Policy fixed, not a production implementation.** Retry ownership is the whole operation with fresh owner and borrowers; per-context replay is forbidden. EF provider execution strategies must wrap the whole operation before manual enlistment, or be explicitly disabled/ adapted. |
| A07 savepoints | **Executable for all four providers.** SQLite, PostgreSQL, and MySQL use `SAVEPOINT`/`ROLLBACK TO SAVEPOINT`; SQL Server uses `SAVE TRANSACTION`/`ROLLBACK TRANSACTION`. The syntax proof is topology evidence, not a claim that every module's savepoint/retry interaction is settled; #1678/provider owners retain the matrix. |
| A08 isolation | **Topology-boundary selection only.** The SQLite three-borrower commit test explicitly selects `Serializable`; the SQLite contention test and remaining SQLite operations use `ReadCommitted`. Native smokes use `ReadCommitted`. This does not prove module-level anomaly freedom. Each module owner must select its invariant's isolation level and provide its own concurrency-anomaly matrix downstream. |
| A09 shell isolation/teardown/reload | **Owner teardown is proved** and target identity is fixed at operation creation. Actual shell activation, reload, and configuration-change orchestration remains #1670. |
| A10 tenant boundaries | **Topology-boundary evidence only.** Tenant identity is captured by the owner and a mismatch is refused before enlistment/writes in SQLite. Module tenant filters, identity predicates, cross-tenant read/write refusal, and tenant/concurrency matrices remain mandatory downstream work for #1678 and each owning module. |
| A11 migration ordering | **Explicitly outside this transaction.** This project has no migrations; deterministic module ordering, history isolation, and fail-before-activation belong to #1669. |
| A12 split-database refusal | **Proved in SQLite** by target mismatch and physical-connection mismatch before any row is written. Split-target publication must use ADR 0066's ordered recovery path unless all lanes resolve to one target. |
| A13 SQLite WAL/busy/locking | **Proved without timing claims.** Temporary-file WAL mode, positive busy timeout, an in-flight writer, and a second writer's busy/locked refusal are asserted; a 10-second cancellation watchdog bounds liveness if provider behavior regresses; rollback leaves the database empty. |

## Recommendation and reconciliation

Admit one operation-scoped owner at the EF-internal composition boundary, with
Runtime/Design/Publishing contexts as borrowers. The owner must bind a single
physical target and tenant, explicitly enlist each context, own commit/rollback
and resource disposal, and expose no EF type through domain contracts. A
provider execution strategy may only retry the complete operation from a clean
owner/context set; it must not replay an individual context after an unsafe
side effect. A commit transport error is an unknown outcome requiring durable
marker/outbox inspection and recovery, not a reported failure that is blindly
replayed.

This does not replace ADR 0066 for split targets. When all three lanes resolve
to one target, a future production path may fold the operation back into one
atomic commit through an explicit co-located path. When targets differ, retain
ADR 0066's runtime-first, design-linearization, publishing-receipt-last
ordered sequence and recovery/redrive semantics. The transaction owner must
refuse split targets before writes rather than silently placing a lane in the
wrong database.

Before production implementation, #1678 should turn these findings into
provider-neutral EF-internal seams and provider-specific admissions; #1669
must settle migration ordering/history/locking independently; Runtime and
publishing owners must prove their own concurrency, recovery, and idempotency
contracts. No default flip or Groundwork deletion is implied here.

## Local verification

Commands run serially across the development and final-rebase validation
passes (the native commands were last run on the pre-rebase head identified
above):

```text
dotnet build tests/Elsa/Persistence/EntityFrameworkCore/TransactionTopology/Tests/Elsa.Persistence.EntityFrameworkCore.TransactionTopology.Tests.csproj --configuration Release --nologo --no-restore
dotnet test tests/Elsa/Persistence/EntityFrameworkCore/TransactionTopology/Tests/Elsa.Persistence.EntityFrameworkCore.TransactionTopology.Tests.csproj --configuration Release --no-build --no-restore --filter FullyQualifiedName~SqliteTopologyTests --logger 'console;verbosity=minimal'
dotnet test tests/Elsa/Persistence/EntityFrameworkCore/TransactionTopology/Tests/Elsa.Persistence.EntityFrameworkCore.TransactionTopology.Tests.csproj --configuration Release --no-build --no-restore --filter FullyQualifiedName~PostgreSqlTopologyTests --logger 'console;verbosity=minimal'
dotnet test tests/Elsa/Persistence/EntityFrameworkCore/TransactionTopology/Tests/Elsa.Persistence.EntityFrameworkCore.TransactionTopology.Tests.csproj --configuration Release --no-build --no-restore --filter FullyQualifiedName~SqlServerTopologyTests --logger 'console;verbosity=minimal'
dotnet test tests/Elsa/Persistence/EntityFrameworkCore/TransactionTopology/Tests/Elsa.Persistence.EntityFrameworkCore.TransactionTopology.Tests.csproj --configuration Release --no-build --no-restore --filter FullyQualifiedName~MySqlTopologyTests --logger 'console;verbosity=minimal'
```

Exact rebased-head results: SQLite 13/13 passed, maps and solution filters were
fresh, and the diff check was clean. Pre-rebase results on the unchanged native
test paths: build passed with 0 warnings/0 errors; PostgreSQL 1/1 passed; SQL
Server 1/1 passed; MySQL 1/1 passed. The native results were not rerun on the
rebased head. No hosted CI, benchmark, timing measurement, migration command,
or performance check was run.

`dotnet run --project tools/maps/Elsa.Maps.Generator -- all` followed by
`-- check` passed after staging all six genuinely changed map/findings files.
`dotnet run --project tools/maps/Elsa.Maps.Generator -- solution-filters-check`
also passed.

Architecture admission is intentionally narrow: the project is under
`tests/`, has package references only, has no `ProjectReference` to `src/`,
contains no migrations, and references no Groundwork or domain adapter. It is
included in `Elsa.Server.slnx` under the EF transaction-topology test folder.

# EF Core transaction topology spike (#1674)

Status: bounded test-only evidence on `codex/ef-transaction-topology`, based on
`origin/main` at `d15bd735df5df101d5e8f546a653b65c70881c2f` (2026-09-14).

This spike proves the mechanics needed before Runtime, Design, and Publishing
contexts are multiplied. It intentionally contains no production transaction
abstraction, `TransactionScope`, module adapter, migration artifact, or
Groundwork dependency.

## Executable shape

`TopologyOperation` is a test-only operation-scoped owner. It opens one
physical `DbConnection`, begins one `DbTransaction` at the requested isolation
level, explicitly enlists three independently constructed borrower contexts
with `Database.UseTransaction`, and owns commit, rollback, and final disposal.
Borrowers never commit or dispose the owner resources. Enlistment refuses a
different physical connection, target, or tenant before a borrower can write.

SQLite uses a temporary file database. Its proof covers three-lane commit,
explicit rollback, forced failure after an earlier `SaveChanges`, borrower
disposal before owner commit, savepoint rollback, target/tenant/split-connection
refusal, WAL and positive `busy_timeout`, and a contending writer that observes
SQLite busy/locked behavior. The contention assertion is state-based; no
elapsed-time, throughput, or performance claim is made.

PostgreSQL, SQL Server, and MySQL use the repository's pinned EF Core/provider
packages and Testcontainers fixtures. Each focused smoke creates a fresh
database, proves model creation, same-connection enlistment, commit, explicit
rollback, forced partial failure rollback, and borrower disposal. Tests are
assembly-serial so native providers are never started concurrently by this
project. MySQL uses the pinned `mysql:8.4.11` digest from the feasibility spike.

## A01-A13 acceptance mapping

| Row | Evidence and disposition |
| --- | --- |
| A01 shared connection ownership | **Proved in SQLite and all three native smokes.** The operation owns the opened connection and disposes it exactly once after transaction teardown. |
| A02 transaction ownership/enlistment | **Proved in all providers.** Three borrowers explicitly call `UseTransaction`; a different connection is refused before writes. |
| A03 construction/disposal | **Proved for the test topology.** Runtime, Design, and Publishing contexts are independently constructed and can be disposed before owner commit. Production factories and shell lifetime remain #1678/#1670 work. |
| A04 lifetime/commit/rollback | **Proved.** Commit makes all three rows visible; explicit rollback and owner teardown rollback leave no rows. Commit exceptions are classified as unknown outcome, never success. A network-induced ambiguous commit was not injected. |
| A05 partial `SaveChanges` failure | **Proved for an exception after an earlier save** in SQLite and native smokes. SQLite additionally proves savepoint-scoped rollback. A provider-specific duplicate-key failure matrix remains module/provider work. |
| A06 retries/execution strategies | **Policy fixed, not a production implementation.** Retry ownership is the whole operation with fresh owner and borrowers; per-context replay is forbidden. EF provider execution strategies must wrap the whole operation before manual enlistment, or be explicitly disabled/ adapted. |
| A07 savepoints | **SQLite supported and executable** with provider save/rollback-to-savepoint APIs. Native savepoint API/semantics are not generalized by this spike; #1678/provider owners must record support or refusal per provider. |
| A08 isolation | **Chosen and executable.** SQLite uses `Serializable` for the WAL/locking proof; native smokes use `ReadCommitted`. Production modules must select and document the isolation level for their invariant, not infer it from this spike. |
| A09 shell isolation/teardown/reload | **Owner teardown is proved** and target identity is fixed at operation creation. Actual shell activation, reload, and configuration-change orchestration remains #1670. |
| A10 tenant boundaries | **Proved in SQLite.** Tenant identity is captured by the owner and a mismatch is refused before enlistment/writes. |
| A11 migration ordering | **Explicitly outside this transaction.** This project has no migrations; deterministic module ordering, history isolation, and fail-before-activation belong to #1669. |
| A12 split-database refusal | **Proved in SQLite** by target mismatch and physical-connection mismatch before any row is written. Split-target publication must use ADR 0066's ordered recovery path unless all lanes resolve to one target. |
| A13 SQLite WAL/busy/locking | **Proved without timing claims.** Temporary-file WAL mode, positive busy timeout, an in-flight writer, and a second writer's busy/locked refusal are asserted; rollback leaves the database empty. |

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

Commands run serially:

```text
dotnet build tests/Elsa/Persistence/EntityFrameworkCore/TransactionTopology/Tests/Elsa.Persistence.EntityFrameworkCore.TransactionTopology.Tests.csproj --configuration Release --nologo --no-restore
dotnet test tests/Elsa/Persistence/EntityFrameworkCore/TransactionTopology/Tests/Elsa.Persistence.EntityFrameworkCore.TransactionTopology.Tests.csproj --configuration Release --no-build --no-restore --filter FullyQualifiedName~SqliteTopologyTests --logger 'console;verbosity=minimal'
dotnet test tests/Elsa/Persistence/EntityFrameworkCore/TransactionTopology/Tests/Elsa.Persistence.EntityFrameworkCore.TransactionTopology.Tests.csproj --configuration Release --no-build --no-restore --filter FullyQualifiedName~PostgreSqlTopologyTests --logger 'console;verbosity=minimal'
dotnet test tests/Elsa/Persistence/EntityFrameworkCore/TransactionTopology/Tests/Elsa.Persistence.EntityFrameworkCore.TransactionTopology.Tests.csproj --configuration Release --no-build --no-restore --filter FullyQualifiedName~SqlServerTopologyTests --logger 'console;verbosity=minimal'
dotnet test tests/Elsa/Persistence/EntityFrameworkCore/TransactionTopology/Tests/Elsa.Persistence.EntityFrameworkCore.TransactionTopology.Tests.csproj --configuration Release --no-build --no-restore --filter FullyQualifiedName~MySqlTopologyTests --logger 'console;verbosity=minimal'
```

Results: build passed with 0 warnings/0 errors; SQLite 6/6 passed;
PostgreSQL 1/1 passed; SQL Server 1/1 passed; MySQL 1/1 passed. No hosted CI,
benchmark, timing measurement, migration command, or performance check was
run.

Architecture admission is intentionally narrow: the project is under
`tests/`, has package references only, has no `ProjectReference` to `src/`,
contains no migrations, and references no Groundwork or domain adapter. It is
included in `Elsa.Server.slnx` under the EF transaction-topology test folder.

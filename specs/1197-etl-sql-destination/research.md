# Research: Transactional keyed SQL delivery

Facts are from `main` at `19b063e76`. Paths are relative to `Extensions/Etl/src/`. An independent design review checked the first draft against the code; each decision below cites what it found.

## R1 — Exactly once per effect

- **The effect identity is stable.**
  - Retained output is never rebuilt (`EtlConnectorOutputProcessor` keeps the owed output).
  - `EtlRuntimeIdentity.ConnectorEffectId` covers the invocation, batch, route, destination pin and content hash.
  - A redelivery therefore carries the same identity and body hash.
- **The guarantee holds.** The ledger primary key and the upserted rows commit in one transaction:
  - Two overlapping lease holders serialize on that key, and the loser rolls back.
  - A lost commit acknowledgement resolves to `Replay` on the next attempt.

## R2 — Error classification

- **The problem.** Non-HTTP routes have no attempt counter. `DispatchAsync` catches only `EtlEffectException` and `EtlBindingException`, and anything else reaches the worker's catch-all, which retries every 2 s without limit (`Hosting/EtlRuntimeWorker`).
- **Decision:** the writer wraps every driver exception at the boundary, as framework §2.19 requires:
  - **Deterministic rejections** become `EtlEffectException("etl.sql-write-rejected")`, which fails the run through the existing dispatch-failure path.
  - **Transient errors** become a yield result: the route stays unrecorded, and the worker releases and retries later.
  - **Who classifies:** each engine package, because only it knows its error codes.

## R3 — Repeated keys and collation

- **The engine limits.** PostgreSQL `ON CONFLICT DO UPDATE` refuses to touch one row twice in a statement, and SQL Server `MERGE` refuses duplicate source matches.
- **Why the client cannot dedupe.** Client-side deduplication uses .NET equality, not the database's collation.
- **Decision:** one single-row upsert statement per record, in order, several to a command. The database applies its own collation, and the later record wins. The cost is one statement per record, which a batch of at most `MaxBatchRecords` bounds.

## R4 — Key constraint detection

- **PostgreSQL** reports a missing conflict target (42P10).
- **SQL Server** `MERGE … HOLDLOCK` succeeds without any unique index, and would quietly allow duplicates.
- **Decision:** a catalog query per engine checks for a unique constraint or index on exactly the key columns, before the first upsert in each transaction.

## R5 — Identifiers and types

- **The compiler does not check identifiers.** `ValidateMapping` checks only that output names are non-blank and ordinally unique, so `order id` or `Id` alongside `ID` compile today.
- **Decision:** a `sql` destination adds an identifier rule and requires names to be distinct ignoring case.
- **Types.** `EtlColumnType` is String, Int64, Decimal or Boolean, and JSON cannot tell `5` (Int64) from `5` (Decimal). So the writer reads the column types from the pinned pipeline's mapping.
- **Keys** must be mapped from required source fields: a non-required field can produce null (`EtlRowTransformer`), and a null key never conflicts.

## R6 — Packages and wiring

- **The constitution forbids one package for both drivers** (§2.1, §2.20): each provider gets its own package, and contracts stay free of heavy dependencies.
- **The handle needs the driver.** Parsing host, port, database and user out of a connection string needs the driver's parser. So the engine provides `DescribeTarget`, and the runtime hashes its result.
- **Credentials:**
  - HTTP bindings take only a connection name, but the Connections domain has no SQL connection type, and adding one is out of scope.
  - SQL bindings therefore take a connection string from host configuration, like `EtlRuntimeEntityFrameworkCoreFeature.ConnectionString`.
  - The handle never includes the password.
- **The engine must be injected.** The output processor is created with `new` in `EtlConnectorExecution`, so the engines are injected into the execution and passed on.

## R7 — Publication sites

The publication paths special-case the file kinds at these sites:
- the acceptance store's publication check, and its delivered and not-before helpers;
- the output store's `EffectMechanism` (persisted and indexed), and its kind check;
- the runtime inspector's effect state;
- the runtime API's effect states;
- the record codec's destination kinds;
- the output processor's first-row bound, route format, kind maps and dispatch.

Every one admits `Sql`. `EffectMechanism` records how delivery is tracked, not the format: 1 is an HTTP journal, 2 a recorded publication. A SQL write is recorded through the same publication transition, so it shares 2. A new value would also need a table rebuild, because the batches table's CHECK admits only 1 and 2. Missing any site throws `InvalidDataException`.

## R8 — Ledger operations

- **Concurrent creation** can lose a race: on PostgreSQL a duplicate table or a pg_type unique violation, on SQL Server error 2714. Such a loss counts as created.
- **A duplicate ledger insert**, including one that waited on the key lock, resolves to `Replay` once the hashes match.
- **Retention:** the runtime never deletes ledger rows (the README states this).
- **SQL Server** runs each transaction under `SET XACT_ABORT ON`, so a failing statement cannot leave a transaction open.

## R9 — Ordering, mixed versions and CI

- **Ordering.** File partitions and HTTP shards run concurrently, so a key repeated across them has no defined winner. That is stated, and the tests use keys unique across partitions.
- **Mixed versions.** An old worker fails a `sql` run with `etl.executable.invalid-pipeline` (`EtlConnectorExecutableReader`).
- **CI.** Container suites run only in the full and nightly tiers (`tools/ci/test_tiers.py`). The new suite needs a `containerSuites` entry with a `require` flag and an `ef-N` shard slot.
- **Kill points.** The worker host's connector gate store can pause at the publication record, which gives SC-002 its kill point.

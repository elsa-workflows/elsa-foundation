# Implementation Plan: Transactional keyed SQL delivery

**Branch**: `agent/claude/2908-sql-destination` | **Spec**: [spec.md](spec.md) | **Research**: [research.md](research.md)

## Summary

The plan adds a main-route `sql` destination:
- **Contract.** The engine contract lives in Core, with no driver.
- **Writer.** A shared ADO.NET writer in the runtime performs the ledger and upsert protocol in one transaction.
- **Engines.** Two engine packages, PostgreSQL and SQL Server, each register one engine.
- **Recording.** The route-publication transition records SQL deliveries like file publications.

## Technical context

- .NET 10 and ADO.NET (`System.Data.Common`), with Npgsql 10 and Microsoft.Data.SqlClient 7 in the engine packages only.
- Tests use xUnit with Testcontainers PostgreSQL and MsSql 4.15, plus Vitest and Playwright.
- The unit tests drive the writer with a SQLite test engine (`Microsoft.Data.Sqlite`, already a test dependency), so the protocol is tested without containers. The container suite proves the two real engines.

## Constitution check

- **Packages and dependencies** (§2.1, §2.7, §2.20):
  - the contract carries no external dependency;
  - each driver stays in its own provider package;
  - no umbrella package, because the shared logic is in the runtime, over BCL types;
  - the provider packages reference only `Elsa.Etl.Core`.
- **Wrapping** (§2.19): infrastructure exceptions are wrapped at the writer boundary.
- **Compatibility:**
  - existing artifacts and identities are byte-identical (`Sql` is appended);
  - SQL shares the persisted recorded-publication `EffectMechanism` value (research R7);
  - no migration, because the batch publication flags are reused.
- **Extension points.**
  - The Core `EXTENSION_POINTS.md` gains `IEtlSqlEngine`, an adapter keyed by provider name (not a fan-in contributor), of which the runtime resolves one per binding.
  - Each engine package gets a README.
- **Tests.**
  - Behaviour tests cover the compiler, bindings, writer, publication and journeys.
  - Each engine package's registration is plain, so the §2.23.1 composition test covers it.
  - The new container project is listed in `test-tiers.json` and `containerSuites`.
- **Naming.** The new names are `EtlSqlDestination`, `IEtlSqlEngine`, `EtlHostSqlBindingOptions`, `EtlSqlPostgreSqlFeature` and `EtlSqlServerFeature`, each four components or fewer.

## Changes by project

### Core (`src/Core`)
- `EtlSqlDestination` and its JSON kind; `EtlConnectorKind.Sql`, appended.
- `IEtlSqlEngine` and its small models (`EtlSqlTarget`, `EtlSqlErrorKind`) in `Core/Runtime`.
- `EtlRuntimeIdentity.ConnectorSqlEffectKey`.
- PublicAPI.

### Design (`src/Design`)
- `EtlArtifactCompiler.V2`: the `sql` rules (main route only, identifiers, key) and the size budget.

### Runtime (`src/Runtime/Elsa.Etl.Runtime`)
- **Bindings:**
  - `Bindings/EtlHostSqlBindingOptions`;
  - `Bindings/HostSqlBindings`, which resolves bindings, pins handles, allows tables and resolves engines;
  - `HostConnectorBindings` for the `sql` kind;
  - `EtlRuntimeFeature.SqlBindings`.
- **Writing:**
  - `Output/EtlSqlRouteWriter`, the protocol;
  - `EtlConnectorOutputProcessor`, for the route format and dispatch;
  - `EtlConnectorExecution`, which injects the engines;
  - `EtlConnectorExecutableReader`, for `sql`.
- **API:** the `EtlRuntimeOperations.Connectors` effect state.

### Persistence (`src/Persistence/EntityFrameworkCore`)
- The acceptance, output and runtime-inspector stores, and `EtlConnectorRecordCodec`, admit `Sql` at every publication site (R7).

### New engine packages
- `src/Sql/PostgreSql/Elsa.Etl.Sql.PostgreSql.csproj`: `EtlSqlPostgreSqlFeature` and `PostgreSqlEtlSqlEngine` (Npgsql).
- `src/Sql/SqlServer/Elsa.Etl.Sql.SqlServer.csproj`: `EtlSqlServerFeature` and `SqlServerEtlSqlEngine` (Microsoft.Data.SqlClient, under `XACT_ABORT`).

### Studio
- `etlTypes.ts`, the pipeline editor's destination picker, and the table and key controls, with tests. Inspection gets a SQL label, which is not tested.

### Tests
- **`Elsa.Etl.Tests`:**
  - the compiler, bindings and publication sites;
  - the writer against a SQLite test engine (ledger, replay, conflict, rejection, transient yield, repeated keys);
  - a CSV-to-SQL journey on the test engine;
  - real worker processes killed between the commit and the delivery record, on the test engine.
- **New `tests/Sql/ProviderTests/Elsa.Etl.Sql.ProviderTests.csproj`** (container suite), on PostgreSQL 16 and SQL Server 2022: the writer protocol, missing-key detection and error classification per engine.
- **The host journey on a real engine** is the Workbench journey against PostgreSQL.

### CI and maps
- `tools/ci/test-tiers.json`: the new project, as integration with Testcontainers, plus its `containerSuites` entry.
- Solution filters, regenerated.

### Docs
- The ETL README, the Core README, the Core `EXTENSION_POINTS.md` and the engine package READMEs.
- The evidence README, the bite-proofs and the Workbench journey.

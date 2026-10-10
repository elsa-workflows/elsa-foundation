# Quickstart: Groundwork SQLite Document Store

## Build

```bash
dotnet build src/Groundwork/Sqlite/Groundwork.Sqlite.csproj
```

## Test Provider

```bash
dotnet test tests/Groundwork/Groundwork.Sqlite.Tests/Groundwork.Sqlite.Tests.csproj
```

Expected result:

- Materialization creates document, index, and schema-history tables.
- Save/load/update/delete pass.
- Declared-index equality query passes.
- Undeclared query and stale expected-version operations fail clearly.

## Solution Validation

```bash
dotnet test Elsa.Server.slnx --no-restore
```

Run restore first if assets are missing.

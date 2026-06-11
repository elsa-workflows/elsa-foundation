# Quickstart: Groundwork Physicalization And Performance

## Validate Planner And Projection Selection

```bash
dotnet test tests/Groundwork/Groundwork.Tests/Groundwork.Tests.csproj
```

Expected result: planner tests show optimized units produce physicalization operations and portable units do not.

## Validate SQLite Optimized Physicalization

```bash
dotnet test tests/Groundwork/Groundwork.Sqlite.Tests/Groundwork.Sqlite.Tests.csproj
```

Expected result: SQLite creates optimized projection tables for optimized units, maintains projected values on save/update/delete, and returns the same query results through `IDocumentStore`.

## Validate MongoDB Optimized Physicalization

```bash
dotnet test tests/Groundwork/Groundwork.MongoDb.Tests/Groundwork.MongoDb.Tests.csproj
```

Expected result: MongoDB stores `physicalized` values, creates provider-native indexes over them, and returns the same query results through `IDocumentStore`.

## Full Regression

```bash
dotnet test Elsa.Server.slnx --no-restore
```

Expected result: all Groundwork and Elsa validation tests pass.

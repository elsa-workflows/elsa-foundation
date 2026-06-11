# Quickstart: Groundwork SQL Server And PostgreSQL Providers

## SQL Server

```csharp
await new SqlServerGroundworkMaterializer(connection)
    .MaterializeAsync(manifest, new ProviderIdentity("groundwork-sqlserver", "1.0.0"));

var store = new SqlServerDocumentStore(connection, manifest);
```

## PostgreSQL

```csharp
await new PostgreSqlGroundworkMaterializer(connection)
    .MaterializeAsync(manifest, new ProviderIdentity("groundwork-postgresql", "1.0.0"));

var store = new PostgreSqlDocumentStore(connection, manifest);
```

## Validation Commands

Docker must be available for provider integration tests.

```bash
dotnet test tests/Groundwork/Groundwork.RelationalProviders.Tests/Groundwork.RelationalProviders.Tests.csproj
dotnet test tests/Groundwork/Groundwork.Sqlite.Tests/Groundwork.Sqlite.Tests.csproj
dotnet test Elsa.Server.slnx --no-restore
```

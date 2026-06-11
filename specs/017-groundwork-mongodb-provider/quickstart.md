# Quickstart: Groundwork MongoDB Provider

## Register Materialization

```csharp
var database = mongoClient.GetDatabase("groundwork");

await new MongoDbGroundworkMaterializer(database)
    .MaterializeAsync(manifest, new ProviderIdentity("groundwork-mongodb", "1.0.0"));

var store = new MongoDbDocumentStore(database, manifest);
```

## Validation Commands

Docker must be available for provider integration tests.

```bash
dotnet test tests/Groundwork/Groundwork.MongoDb.Tests/Groundwork.MongoDb.Tests.csproj
dotnet test tests/Groundwork/Groundwork.Sqlite.Tests/Groundwork.Sqlite.Tests.csproj
dotnet test tests/Groundwork/Groundwork.RelationalProviders.Tests/Groundwork.RelationalProviders.Tests.csproj
dotnet test Elsa.Server.slnx --no-restore
```

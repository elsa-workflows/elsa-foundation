# Quickstart: Groundwork Elsa Bridge

## Register The Bridge

1. Add `Elsa.Persistence.Groundwork`.
2. Register the bridge with one or more manifests:

```csharp
services.AddElsaGroundworkPersistence(options =>
{
    options.Manifests.Add(SecretsGroundworkManifestFactory.Create());
});
```

3. Register one or more `IGroundworkPersistenceProvider` adapters in the application composition.
4. Run Elsa startup tasks. The bridge startup task materializes the configured manifests against registered providers when `MaterializeOnStartup` is `true`.

## Inspect Diagnostics

Resolve `GroundworkPersistenceDiagnostics` and request a snapshot. The snapshot reports:

- registered manifest identities
- registered provider identities
- materialization records and statuses

## Validation Commands

```bash
dotnet test tests/Elsa/Persistence/Groundwork/Tests/Elsa.Persistence.Groundwork.Tests.csproj
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj
dotnet test Elsa.Server.slnx --no-restore
```

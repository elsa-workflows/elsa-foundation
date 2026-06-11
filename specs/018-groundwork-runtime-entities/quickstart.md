# Quickstart: Groundwork Runtime-Defined Entities

```csharp
var definition = new RuntimeEntityDefinition(
    "customerProfile",
    "Customer Profile",
    [
        new RuntimeEntityIndex("by-email", "email", IndexValueKind.Keyword, IsUnique: true),
        new RuntimeEntityIndex("by-segment", "segment", IndexValueKind.Keyword)
    ]);

var manifest = RuntimeEntityManifestFactory.Create(definition);
await materializer.MaterializeAsync(manifest, provider);

var store = new GroundworkRuntimeEntityStore(documentStore);
await store.SaveDefinitionAsync(definition);
await store.SaveInstanceAsync(definition, "customer-1", """{"email":"a@example.com","segment":"vip"}""");
```

## Validation Commands

```bash
dotnet test tests/Elsa/Persistence/Groundwork/Tests/Elsa.Persistence.Groundwork.Tests.csproj
dotnet test Elsa.Server.slnx --no-restore
```

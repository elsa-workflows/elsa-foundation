# Elsa.Integrations.Sdk.Testing

In-memory test fakes for `Elsa.Integrations.Sdk`. Elsa 4 only. Preview pin: not contract-stable until GA (ADR 0080 D4).

This package is for connector test projects. Production connectors reference `Elsa.Integrations.Sdk` only.

## Usage

```csharp
await using var harness = ConnectorHarness.Create(configure: services => services.AddMyConnector());
harness.Secrets.Add(harness.TenantId, "api-key", "secret");
harness.Connections.Add("my-http", "Bearer", "token");
harness.Http.EnqueueJson("""{"ok":true}""");

var activity = ActivatorUtilities.CreateInstance<MyActivity>(harness.Services);
var transition = await activity.ExecuteAsync(context);
```

`harness.Secrets.Add(...)` and `harness.Connections.Add(name, scheme, credential)` register the in-memory stores. Pass `Create(configure: ...)` to replace the applicator or to register the connector under test.

Scaffold a connector with `dotnet new elsa-connector`. Its test project references this package.

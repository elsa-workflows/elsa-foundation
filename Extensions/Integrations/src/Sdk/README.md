# Elsa.Integrations.Sdk

Elsa Integrations SDK v1. Elsa 4 only. Preview pin: base types are not contract-stable until GA (ADR 0080 D4).

One NuGet package per connector contains `IActivity` types. Foundation CLR reconciliation discovers them from `[Activity]`, `[ActivityInput]`, `[Version]`, and `[TriggerActivity]`. This package does not add a second catalog.

## Contract

1. **No duplicate manifest.** `ConnectorManifest.FromAssembly` writes a packaging copy of those attributes. Do not read `connector.json` at runtime for activity metadata. Alternatively, supply an `IActivityReconciliationSource`; the built-in CLR source already scans `IActivity` types.
2. **Versions** come from `[Version]`, then informational, then assembly version. The package does not declare a competing version.
3. **Field tiers** use `[ActivityInput(Tier = InputTier.Essential|Optional|Setting)]` on the activity descriptor contract.
4. **Icons** use `[Activity(Icon = …)]` (lucide name, `asset:<package>/<path>`, `ext:<name>`).
5. **Secrets** use `[ActivityInput(IsCredential = true)]` and `[RefusesSecretBinding]`. Runtime values resolve per activation, tenant-scoped and uncached, through `ISecretValueResolver`. This package does not wrap or cache that resolver.
6. **Service connections** are `Elsa.Connections`: `[ActivityConnection]`, `ConnectionReference`, `SensitiveValue`, `ConnectionUnavailable`, and the `http` type (api-key, basic, bearer). This SDK does not ship those types. Administration uses `ConnectionsPermissions` (`connections:manage`, `connections:bind-secret`, `connections:test`); this package adds no extra right. Host named credentials (`Elsa:Credentials:<name>`, #2810) are also not this package.
7. **Authenticated HTTP client.** HTTP connectors take `IHttpClientFactory` and `IHttpConnectionApplicator` and call `ApplyConnectionAsync` on the outbound `HttpRequestMessage` before send. `AddElsaIntegrationsRuntime` registers the named `Elsa.Activities.Http` client (`HttpActivityConstants.HttpClientName`) with `RemoveAllLoggers` + `HttpActivityClientLogger`, and the unbound applicator when Connections is not composed. It does not add an auth handler on that client. `ConnectionsWorkflows` replaces the applicator and already owns the named client's redirect handler.
8. **Design/runtime split.** Connection-type descriptors and the connection catalog live on Connections. This package has no design-time connection schema for the runtime to load.
9. **Loading.** Activities only. This package registers no `IShellFeature`, hosted service, or EF context. Packages are intended to be collectible; collectible loading is unproven (ADR 0070/0071).
10. **No shims.** Elsa 4 only. No Elsa 3 compatibility layer.

## Usage

```csharp
services.AddElsaIntegrationsRuntime(); // plus the host's ISecretValueResolver
// Compose AddConnectionsWorkflows when bound http connections must resolve.
```

```csharp
[Activity(Icon = "plug")]
[Version("1.0.0")]
public sealed class PingRemote : ConnectorActivity<string>
{
    public PingRemote(IHttpClientFactory httpClientFactory, IHttpConnectionApplicator connectionApplicator)
        : base(httpClientFactory, connectionApplicator)
    {
    }

    [ActivityInput(Key = nameof(Connection), DisplayName = "Connection", Tier = InputTier.Essential)]
    [ActivityConnection(ConnectionType = "http")]
    public ConnectionReference? Connection { get; set; }

    [ActivityInput(Tier = InputTier.Essential)]
    public string Url { get; set; } = "https://example.com/health";

    protected override async ValueTask<ActivityTransition<string>> ExecuteAsync(ActivityExecutionContext context)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Url);
        await ApplyConnectionAsync(request, Connection, context, context.CancellationToken);
        using var response = await CreateHttpClient().SendAsync(request, context.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(context.CancellationToken);
        return ActivityTransition.Complete(body);
    }
}
```

Scaffold with `dotnet new elsa-connector`. Test with `ConnectorHarness`: `harness.Secrets.Add(...)` and `harness.Connections.Add(name, scheme, credential)` register the in-memory stores. Pass `Create(configure: ...)` to replace the applicator.

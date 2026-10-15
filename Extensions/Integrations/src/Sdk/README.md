# Elsa.Integrations.Sdk

Elsa Integrations SDK v1. Elsa 4 only. Preview pin: base types are not contract-stable until GA (ADR 0080 D4).

One NuGet package per connector contains `IActivity` types. Foundation CLR reconciliation discovers them from `[Activity]`, `[ActivityInput]`, `[Version]`, and `[TriggerActivity]`. This package does not add a second catalog.

## Contract

1. **No duplicate manifest.** `ConnectorManifest.FromAssembly` writes a packaging copy of those attributes. Do not read `connector.json` at runtime for activity metadata. Alternatively, supply an `IActivityReconciliationSource`; the built-in CLR source already scans `IActivity` types.
2. **Versions** come from `[Version]`, then informational, then assembly version. The package does not declare a competing version.
3. **Field tiers** use `[ActivityInput(Tier = InputTier.Essential|Optional|Setting)]` on the activity descriptor contract.
4. **Icons** use `[Activity(Icon = …)]` (lucide name, `asset:<package>/<path>`, `ext:<name>`).
5. **Secrets** use `[ActivityInput(IsCredential = true)]` and `[RefusesSecretBinding]`. Runtime values resolve per activation, tenant-scoped and uncached, through `ISecretValueResolver`. `IConnectionContext` sits on that path and has no cache of its own.
6. **Service connections** follow `docs/plans/connections-and-secrets-model.md`. OAuth is one scheme, not the model (`IConnectionAuthenticator`). Owner/run-as is reserved; only tenant is valid in v1. Persistence is `Elsa.Connections`, not this SDK. Missing connections fault as `ConnectionUnavailableException`.
7. **Authenticated HTTP client.** `AddElsaIntegrationsRuntime` adds `ConnectionAuthDelegatingHandler` to the named `Elsa.Activities.Http` `IHttpClientFactory` client (`HttpActivityConstants.HttpClientName`). Logging stays that client's header-free `HttpActivityClientLogger` (spec 188 FR-019), owned by `ActivitiesHttpFeature`. Enable that feature (or copy its `RemoveAllLoggers` + `AddLogger<HttpActivityClientLogger>` registration) so outbound logs never carry headers.
8. **Design/runtime split.** `Elsa.Integrations.Sdk.Design` holds connection-type descriptors. Runtime resolution never loads them to execute.
9. **Loading.** Activities only. This package registers no `IShellFeature`, hosted service, or EF context. Packages are intended to be collectible; collectible loading is unproven (ADR 0070/0071).
10. **No shims.** Elsa 4 only. No Elsa 3 compatibility layer.

## Usage

```csharp
services.AddElsaIntegrationsRuntime(); // plus IConnectionLookup and ISecretValueResolver
services.AddSingleton<IConnectionContext>(sp =>
    sp.GetRequiredService<IConnectionContextFactory>().Create(tenantId));
```

```csharp
[Activity(Icon = "plug")]
[Version("1.0.0")]
public sealed class PingRemote : ConnectorActivity<string>
{
    public PingRemote(IConnectionContext connectionContext) : base(connectionContext) { }

    [ActivityInput(Tier = InputTier.Essential)]
    public ConnectionReference Connection { get; set; } = new("demo");

    protected override async ValueTask<ActivityTransition<string>> ExecuteAsync(ActivityExecutionContext context)
    {
        await using var handle = await ConnectionContext.ResolveAsync(Connection, context.CancellationToken);
        var body = await handle.HttpClient.GetStringAsync("/health", context.CancellationToken);
        return ActivityTransition.Complete(body);
    }
}
```

Scaffold with `dotnet new elsa-connector`. Test with `ConnectorHarness`.

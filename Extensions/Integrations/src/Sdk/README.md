# Elsa.Integrations.Sdk

Elsa Integrations SDK v1. Elsa 4 only. Preview pin: base types are not contract-stable until GA (ADR 0080 D4).

One NuGet package per connector contains `IActivity` types. Foundation CLR reconciliation discovers them from `[Activity]`, `[ActivityInput]`, `[Version]`, and `[TriggerActivity]`. This package does not add a second catalog.

## Contract

1. **No duplicate manifest.** `ConnectorManifest.FromAssembly` writes a packaging copy of those attributes. Do not read `connector.json` at runtime for activity metadata. Alternatively, supply an `IActivityReconciliationSource`; the built-in CLR source already scans `IActivity` types.
2. **Versions** come from `[Version]`, then informational, then assembly version. The package does not declare a competing version.
3. **Field tiers** use `[ActivityInput(Tier = InputTier.Essential|Optional|Setting)]` on the activity descriptor contract.
4. **Icons** use `[Activity(Icon = …)]` (lucide name, `asset:<package>/<path>`, `ext:<name>`).
5. **Secrets** use `[ActivityInput(IsCredential = true)]` and `[RefusesSecretBinding]`. Runtime values resolve per activation, tenant-scoped and uncached, through `ISecretValueResolver`. This package does not wrap or cache that resolver.
6. **Service connections** are #2729 (`Elsa.Connections`): `[ActivityConnection]`, `ConnectionReference`, `SensitiveValue`, `ConnectionUnavailable`, and the `http` type (api-key, basic, bearer). This SDK does not ship those types. Host named credentials (`Elsa:Credentials:<name>`, #2810) are also not this package.
7. **Authenticated HTTP client.** `AddElsaIntegrationsRuntime` adds `IntegrationsHttpAuthHandler` to the named `Elsa.Activities.Http` `IHttpClientFactory` client (`HttpActivityConstants.HttpClientName`). `IntegrationsHttpAuthScope.Push` applies per-request headers for the duration of the scope. Logging stays that client's header-free `HttpActivityClientLogger` (spec 188 FR-019), owned by `ActivitiesHttpFeature`. Enable that feature (or copy its `RemoveAllLoggers` + `AddLogger<HttpActivityClientLogger>` registration) so outbound logs never carry headers.
8. **Design/runtime split.** Connection-type descriptors and the connection catalog live on #2729. This package has no design-time connection schema for the runtime to load.
9. **Loading.** Activities only. This package registers no `IShellFeature`, hosted service, or EF context. Packages are intended to be collectible; collectible loading is unproven (ADR 0070/0071).
10. **No shims.** Elsa 4 only. No Elsa 3 compatibility layer.

## Usage

```csharp
services.AddElsaIntegrationsRuntime(); // plus the host's ISecretValueResolver
```

```csharp
[Activity(Icon = "plug")]
[Version("1.0.0")]
public sealed class PingRemote : ConnectorActivity<string>
{
    public PingRemote(IHttpClientFactory httpClientFactory) : base(httpClientFactory) { }

    [ActivityInput(IsCredential = true)]
    public string? Authorization { get; set; }

    [ActivityInput(Tier = InputTier.Essential)]
    public string Url { get; set; } = "https://example.com/health";

    protected override async ValueTask<ActivityTransition<string>> ExecuteAsync(ActivityExecutionContext context)
    {
        using (PushHttpAuth(request =>
        {
            if (!string.IsNullOrWhiteSpace(Authorization))
                request.Headers.TryAddWithoutValidation("Authorization", Authorization);
        }))
        {
            var body = await CreateHttpClient().GetStringAsync(Url, context.CancellationToken);
            return ActivityTransition.Complete(body);
        }
    }
}
```

Scaffold with `dotnet new elsa-connector`. Test with `ConnectorHarness`.

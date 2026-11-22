# Copilot SDK adapter extension points

The [adapter guide](README.md) owns options, server credentials, SDK state and provider lifecycle. This optional package implements the existing provider-neutral Weaver contracts; no Dashboard/model branch is added.

## Provider contribution

`WeaverGitHubCopilotProvider` contributes `IWeaverProvider` / `IWeaverHarness` through the feature's scoped `TryAddEnumerable` registration. The [owning Weaver catalog](../Elsa.Weaver/EXTENSION_POINTS.md) defines selection, streaming, guarded tool callbacks and receipt-continuation contracts. Exactly one active provider is selected by the existing registry.

## Adapter-local transport seam

`IGitHubCopilotClientFactory.Create(GitHubCopilotClientRequest)` returns the SDK-owned client abstraction. The default singleton `GitHubCopilotSdkClientFactory` is replaceable inside this adapter through DI; it builds pinned SDK configuration in empty mode, with only currently installed/allowlisted custom callbacks. Client/session instances are owned and disposed by the provider's session, turn or diagnostics operation. This is an adapter-local seam, not a cross-domain `.Core` extension or an alternate model loop.

The trusted request's `TenantId` is set from the server session/harness owner. Session state uses its hash beneath `BaseDirectory/tenants`; diagnostics use a distinct directory. A missing owner refuses the fallback turn rather than entering diagnostics state. Consumers replacing the factory must preserve tenant partitioning, current callback/session binding, native invoker policy, cancellation and redacted errors across setup/iteration/disposal. External runtime operators own equivalent state isolation; local directory configuration cannot partition an already-running remote service.

This slice introduces no event or contributor registry. Backend options/appsettings/environment select credentials and model; browser requests never supply tokens. SDK permission approval admits a known callback once, then the existing Weaver invoker independently authorizes its product operation.

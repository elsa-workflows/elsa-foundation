# Studio extension points

Studio API is the composition root for Studio.Core contracts. Frontend lifecycle contracts are published by the Hosting SDK. See [connection extensions](../../../docs/connection-extensions.md) for usage and lifecycle guidance.

## Overridable contracts

### `IStudioBackendAuthoritySource` (Core — Elsa.Studio.Core)

- Signature: `StudioBackendAuthority Resolve(StudioBackendAuthorityContext context)`.
- Default: with no replacement, `StudioBackendAuthorityAccessor` in Hosting uses configured browser/server addresses. The optional Backends host adapter supplies `BackendsAuthoritySource`.
- Register: `services.AddSingleton<IStudioBackendAuthoritySource, MyAuthoritySource>()` in the root host before Build. Declare host feature assemblies with `CShellsBuilder.WithStudioStartupAssemblies(params Assembly[])` (Hosting); it also registers them for ordinary discovery, while leaving the runtime catalog cold. At most one replacement is accepted; multiple registrations fail startup.
- Override when initial endpoint authority comes from another configuration provider. Hosting copies the result into its own immutable snapshot, exposed by `IStudioBackendAuthorityAccessor.Value`; browser authentication, introspection and the management bridge depend on that snapshot. Runtime selection cannot retarget it.

### Frontend connection provider (SDK — Elsa.Studio.Hosting)

- Signature: `initialize(host: StudioModuleHostContext): StudioModuleInstance`; the instance optionally supplies `connection: StudioConnectionController`, `shell: readonly StudioShellContribution[]`, and `dispose(): void` alongside `register(api)`.
- Default: `ConfiguredBackendConnection` in Hosting supplies a real fixed-endpoint session and capability discovery.
- Register: export `initialize` from a discovered Studio module and advertise `host-initialization` in its manifest. The document loader runs factories before backend-bound registration; zero custom controllers uses the default, one replaces it, multiple fail explicitly.
- Override for another connection-selection policy. Backend registration, session-bound requests and qualified navigation depend on the controller. Its optional `dispose()` must retire pending work. `StudioBackendConnection` is the shared immutable transport/auth/capability lifetime implementation.
- Known implementation: optional Backends module's `BackendConnections` (intra-domain). Legacy `register(api)` modules remain supported.

## Implementable contributor interfaces

### `IStudioRuntimeConfigurationSource` (Core — Elsa.Studio.Core)

- Kind: Source; additive public runtime configuration.
- Signature: `IEnumerable<StudioRuntimeConfigurationEntry> GetConfiguration(string studioPathBase)`.
- Register: `services.TryAddEnumerable(ServiceDescriptor.Scoped<IStudioRuntimeConfigurationSource, MySource>())` in the owning shell feature.
- Single aggregating handler: `CollectStudioRuntimeConfiguration` in Studio.Api, registered idempotently through `TryAddEventHandler<OnStudioRuntimeConfigurationCollecting, CollectStudioRuntimeConfiguration>()`.
- Known implementations: `BackendsRuntimeConfigurationSource` (intra-domain, optional). Third-party shell features contribute independently named namespaces through the same interface.
- Namespace collisions, invalid names and projection failures fail the read with 503. Values must be public allowlisted data. Serialization completes within the borrowed shell scope using per-read metadata; module DTOs do not enter a permanent serializer cache.

### Frontend shell contributions (SDK — Elsa.Studio.Hosting)

- Kind: Contributor; additive shell UI.
- Signature: `StudioShellContribution { id, placement: "connection" | "notice" | "recovery", component, order?, requiresBackend? }`.
- Register: return `shell` from `StudioModuleInstance`, or call `api.shell.add(contribution)` during registration. `registerStudioModuleInstances` aggregates instance contributions into each fresh registry.
- Known implementations: Backends switcher, connection notice and recovery page (intra-domain, optional). Backend admission applies to normal contributions; recovery uses only original-host services and is available before a selected connection exists.

### Workflow run related links (SDK — Elsa.Studio.Hosting)

- Kind: Contributor; optional `workflow.run.related-links` slot (`api.workflowRunRelatedLinks`).
- Signature: `StudioWorkflowRunRelatedLinkContribution { id, order?, activityTypes, anyActivityType?, viewKind, viewVersion, requiresBackend?, getLinks(context) }` returning `StudioWorkflowRunRelatedLink { id, label, path }`. `anyActivityType` matches every activity type, for user-defined activity families whose type keys are unknown in advance, so the view kind alone decides.
- Register: call `api.workflowRunRelatedLinks?.add(contribution)` during registration; hosts without the slot show no links.
- Consumer: `WorkflowRunRelatedLinks` in native run details and attached Test Run inspection. It reads the authorized activity state view matching `viewKind`/`viewVersion`, and keeps only same-origin Studio paths on the selected backend. The target route reauthorizes its own reads.
- Known implementations: `etl-native-run` (`Elsa.Studio.Etl`, cross-domain; see its [catalog](../../../../Extensions/Etl/studio/Elsa.Studio.Etl/EXTENSION_POINTS.md#cross-domain-contributions)) and `agent-invocation` (`Elsa.Studio.Agent.Specialists`, cross-domain, `anyActivityType` over view `elsa.specialist.invocation/1`).
- An any-type contribution shows nothing for an activity until a matching view yields a link. Its links appear under "Related" when no listed contribution matches that activity type, and alongside a listed contribution's links under "Related runs" when one does.

### Frontend route workspace options (SDK — Elsa.Studio.Hosting)

- Kind: Contributor; optional metadata on a route contribution.
- Signature: `StudioRouteContribution.workspace?: { resourceKey?(location: URL): string | null | undefined }`, with `useStudioWorkspaceTitle(title, detail?)`, `useStudioRouteLocation()`, `useStudioWorkspaceWhenActive()` and `openStudioWorkspaceTab(path, { activate? })` for screens.
- Register: set `workspace` on `api.routes.add(...)` or a feature-area route. Navigation guards read `StudioNavigationRequest.intent` (`navigate`, `close`, `probe`).
- Known implementations: Workflows definitions, templates, Activity Definitions and upgrade plans (intra-domain). The workspace tab host itself is not replaceable. See spec [1188](../../../../specs/1188-studio-workspace-tabs/spec.md).

## Events

| Event | Delivery | Meaning | Payload | Contributor | Publication site |
|---|---|---|---|---|---|
| `OnStudioRuntimeConfigurationCollecting` (Studio.Core; canonical `IEvent`) | `IInlineEventPublisher`, Sequential and awaited | Collect public settings from the selected shell | `StudioPathBase`, mutable `Configuration` collection of namespace/value entries | `IStudioRuntimeConfigurationSource` through the single aggregator above | Hosting `StudioRuntimeEndpoint.ReadAsync`, inside its borrowed selected-shell scope |
| `OnStudioModuleManifestsCollecting` (Studio.Core; existing `IStudioEvent`) | Existing Studio-specific sequential compatibility dispatch | Collect enabled client module manifests | Mutable `Manifests` collection | Existing `IStudioEventHandler<OnStudioModuleManifestsCollecting>` registrations via `AddStudioEventHandler` | Studio.Api `StudioModuleManifestProvider` |

Independent canonical subscriptions and Events pipeline middleware may observe runtime collection. They do not replace the one source aggregator. The stable event type belongs to Studio.Core; optional module types belong only to scoped contributors/payloads.

Studio module Apply uses the SDK module-reset lifecycle described in [connection extensions](../../../docs/connection-extensions.md). Backend capability refresh uses `studioBackendCapabilitiesChangedEvent` with the exact session key; it rebuilds admission without reinitializing document-owned controllers.

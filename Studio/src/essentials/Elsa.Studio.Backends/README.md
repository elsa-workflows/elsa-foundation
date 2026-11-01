# Optional Studio Backends

`BackendsStudio` owns backend profiles, management UI, connection selection and the profile API. Reference the package and enable the feature in the Studio shell; omit/disable it to retain a single configured backend with normal authentication and capability discovery.

The reference Web host explicitly installs `AddStudioBackendsHostConfiguration` for profiles-only startup compatibility. This optional adapter supplies a replacement initial authority source. Hosting composes the selected shell blueprint, then copies its result into a fresh host-owned snapshot before requests/shell activation; personal selection and later reload cannot retarget Studio identity or server bridge credentials. Explicit browser/server URL settings retain precedence. Custom authority sources are exclusive and duplicate registrations fail.

Public module settings are contributed through the Studio runtime configuration collection event under the `backends` namespace. Configuration is allowlisted and contains no credentials. Profile endpoints belong to the enabled Studio feature and are scoped by the original Studio user, tenant and HostId. They disappear with the feature; client-only module suppression is a separate policy.

The default metadata store is associated weakly with the immutable host authority snapshot. It preserves data across ordinary shell reload in the same package generation and isolates hosts, even with identical HostIds. Configure the existing Preferences persistence adapter for restart/package-upgrade durability. Base Hosting references neither this package nor Preferences.

## Cross-domain contributions

- Implements `IStudioBackendAuthoritySource` only when its explicit host adapter is installed.
- Implements `IStudioRuntimeConfigurationSource` while its Studio feature is active.
- Registers the governed `backend-profiles` Preferences namespace.

See [connection extensions](../../../docs/connection-extensions.md) for the shared connection/session and module bootstrap contracts.

## Configuration and behavior

Settings → Backends provides separate list, details and create/edit views. The details tabs are Connection and
Capabilities. Capabilities describe the API contracts advertised by the selected backend; they are read-only.
Manage features opens the existing Features module for the connected backend, including per-feature settings,
dependency cascades, revision review and Apply. Enabled configuration, runtime descriptor availability, operational
readiness and user permissions remain separate states. Older feature catalogs display unknown readiness as Not reported.

```json
{
  "Studio": {
    "Backends": {
      "DefaultId": "development",
      "AllowPersonalProfiles": true,
      "Profiles": [
        { "Id": "development", "Name": "Development", "BaseUrl": "https://api.example/development/", "Environment": "Development" },
        { "Id": "production", "Name": "Production", "BaseUrl": "https://api.example/production/", "Environment": "Production", "AuthenticationProfile": "corporate" }
      ],
      "AuthenticationProfiles": { "corporate": { "Enabled": true } }
    }
  }
}
```

Configured profiles are source-owned. Their environment is Development, Test or Production; connecting to Production
requires an explicit acknowledgement. URLs include the full API shell path and must not embed credentials, query or
fragment. Authentication profiles contain public provider settings only: Enabled and optional shell-relative
TokenEndpoint/RefreshEndpoint. They are references to the existing backend identity protocol, not secret stores.
Each backend authenticates independently; selected-backend bearers and SignalR token factories never inherit another
backend's credentials. An anonymous probe omits cookies. Backends must allow the exact Studio origin through CORS
and their identity return-URL policy. For cookie-based identity, the browser must also be permitted to send that
backend's session cookie: use the same site, or configure the identity host for cross-site cookies over HTTPS.
Studio does not override browser cookie policy or transfer cookies/tokens between backends.
Anonymous sessions also omit cookies on Agent and Structured Logs SSE. Structured Logs uses the SDK response
transport for anonymous streaming; a custom transport without `requestResponse` retains recent reads and reports
live streaming unavailable. Anonymous backend and Studio-host Console connections use cookie-free long polling because browser-native
WebSockets and EventSource cannot suppress same-origin cookies. Authenticated stream transports retain their existing contracts.

Studio:BackendBaseUrl remains supported as the browser-facing **stable Studio-host identity authority**. When absent and the module is enabled, the explicit `AddStudioBackendsHostConfiguration` host adapter lets
the configured default profile supply that authority at startup. Studio:BackendServerBaseUrl may independently
provide its server-side address. Selecting a different backend never changes the host identity authority or redirects
the host management bridge. Browser configuration contains neither server addresses nor management keys.

Personal profile metadata uses the governed backend-profiles preference namespace through feature-owned
`GET/PUT <studio-shell-prefix>/_elsa/studio/backends/profiles`. The server derives user, tenant and Studio HostId; caller-supplied scope is
ignored. Read/write require studio/preferences:read and studio/preferences:write respectively. Writes require
If-None-Match: * or an exact If-Match revision. Limits are 32 profiles, 32 KiB namespace value and a bounded request body.
Unknown fields and secrets are rejected. Authenticated sessions without a verified subject and tenant cannot persist
profiles. Anonymous development uses origin/host/session-scoped browser metadata, never a shared anonymous server record.

The default module store is in memory and survives browser reloads within the running host, **not a host restart**.
For durable profiles, replace IStudioPreferenceStore with the existing Preferences persistence adapter in the root
container; for example, reference Elsa.Studio.Preferences.Persistence.EntityFrameworkCore and a selected EF provider,
register StudioPreferencesSqliteDbContext as StudioPreferencesDbContext and EfStudioPreferenceStore as a scoped
IStudioPreferenceStore. The host owns connection strings, schema initialization and migrations. The Backends package requires Preferences.Core; base Hosting does not. BackendProfilesTests exercises the public feature API across a real SQLite host restart.

Selection is tab-local and resource links carry backend=<profile-id>. A resource URL without that reference resolves
to the configured default, never another tab's last choice. Studio validates a candidate before leaving the current
backend and checks draft/write guards both before and after discovery. Committing a switch navigates to Home in a new
document; this resets module globals, query caches, editors, streams and subscriptions together. A cancelled or failed
switch retains the previous usable lifetime. Profiles never persist access tokens, refresh tokens or API keys.

Module authors add requiresBackend to route, navigation, widget, editor, panel or agent contributions. Each requirement
names a capability id, supported major contract version and optional required link relations. The shared SDK validates
and caches discovery per immutable endpoint context. Unsupported contributions are omitted; direct routes explain why
they are unavailable. Permission policies still run independently. Optional actions inside a supported page must check
their own required relations, as the workflow Publish and Test Run controls do. After Server feature Apply, emit
studioBackendCapabilitiesChangedEvent with the exact backendSession.key to refresh discovery for that lifetime.
See [API capability composition](../../../../Essentials/src/Api/Capabilities/README.md) for server declarations.

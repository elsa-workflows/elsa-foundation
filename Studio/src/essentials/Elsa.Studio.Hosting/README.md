# Elsa.Studio.Hosting

Server composition and the prebuilt standard browser for a Studio host. References Studio Api/Core and
CShells.AspNetCore; Nuplane, Theme Store and distribution modules are separate choices. The package owns
the standard application, shared SDK/UI sources and generated `/studio` payload. Package consumers need
no frontend source copy or Node/pnpm build.

```csharp
using CShells.DependencyInjection;
using Elsa.Studio.Hosting;

var builder = WebApplication.CreateBuilder(args);
var studio = builder.AddElsaStudio()
    .ConfigureShells(shells => shells.WithAssemblies(typeof(MyStudioFeature).Assembly));
var app = builder.Build();
app.UseElsaStudio();
app.MapElsaStudio();
app.Run();
```

Supply application-owned `shells.json` with the selected shell explicitly enabling `StudioApi` and the
chosen feature IDs. Default `ShellName` is `Default`. Set `ShellsFile = null` for host-owned configuration
rather than another automatically selected writable file. Relative file paths resolve against ContentRoot.
Hosting options bind `Studio:Hosting`, then the explicit callback wins. Installed settings are copied at
registration; changing shell/file/static/security choices requires restart. `IndexFile` defaults to
`studio/index.html`; an application may select its own separately named document within the static provider.

ConfigureShells uses the actual CShells builder after defaults. Assembly selection retains mandatory Api
assembly discovery. Replacing framework discovery/configuration/resolver registrations is an intentional
caller override; the caller must retain the selected shell and mandatory API contract. Options and services
must be registered before Build.

UseElsaStudio installs selected-shell activation **before** routing/authentication/authorization. Call it before
other explicit routing/auth middleware. It then installs revalidating static files and CShells middleware;
MapElsaStudio maps the runtime allowlist, prioritized manifest routes, unknown API404 and fallback document.
Matched host endpoints remain in the root scope. A small resolver decorator delegates shell selection to
CShells; middleware resolution caching is disabled because its host/path/method key cannot preserve endpoint
ownership across reload. Actual shell endpoints retain the framework's exact-generation handling.

Async manifest reads borrow tracked shell scopes, retry an acquisition/disposal race once and return 503
if a scope remains unavailable. Request cancellation propagates; handler failures are not retried. Framework
drain deadlines remain authoritative. Best-effort activation leaves real static files and root runtime/fallback
requests available when feature activation fails.

`StudioBuilder.ConfigurationMutationGate` exposes only `AcquireAsync(CancellationToken)` and an async lease.
Root/addon writers must hold that same lease across the entire read/revision/modify/atomic-write/reload cycle.
Dispose the lease; consumers cannot release or dispose the underlying coordinator. It coordinates hosts in one process,
not arbitrary external processes writing the same files.

AddElsaStudio also owns the Studio bearer/permission gate and backend-management bridge. Existing `Studio:Auth`
keys retain their meanings: `Enabled` (false/absent is the anonymous demo posture), `SessionCacheSeconds`
(default 30; 0 disables caching), and `SessionCacheMaxEntries` (default 1000; non-positive values use the default).
Enabled Studio policies validate the backend bearer even when the surrounding app has another identity.
401 means no validated session;403 means a valid session lacks the required permission. Manage satisfies
existing read policies locally. Permission changes/revocation follow cache expiry; no distributed invalidation
or single-flight guarantee is added. Definitive anonymous sessions have a shorter TTL; transport/HTTP/parse
failures remain uncached.

Configure `Studio:BackendBaseUrl` for the browser and optionally `Studio:BackendServerBaseUrl` for server calls.
`Studio:BackendModuleManagementApiKey` is server configuration (for example the standard environment key
`Studio__BackendModuleManagementApiKey`), never browser runtime state. Status/registry return unconfigured
without privileged calls when URL/key is absent. Ordinary bearer-session introspection never carries that key.
Configure these values before AddElsaStudio; installed security choices require restart.

Set `StudioHostingOptions.QueryTokenPathPrefixes` before Build for explicit trusted hub paths. It defaults empty;
regular endpoints accept bearer by header only. Prefixes are segment-aware application paths, excluding root,
ambiguous leading double slashes and wildcards. Web supplies its distribution-owned Console Stream hub path;
Hosting has no Console Stream dependency.

An application's sole implicit authentication scheme remains its default when Studio is added, including
when application authentication is registered afterward. Explicit defaults take precedence; multiple application
schemes still need explicit defaults. ASP.NET Core's `Microsoft.AspNetCore.Authentication.SuppressAutoDefaultScheme`
switch remains effective. Studio policies always select their own backend bearer scheme, so an application
cookie does not authorize Studio management.

Module authors use Api's `StudioBridgeAuthorization` policies or
`RequireStudioBridgePermission`, without referencing private Hosting handlers.

Use normal root DI/HttpClientFactory APIs to customize clocks, handlers and HTTP behavior before Build.
Root cache, clock, backend options and a private session transport are borrowed by shells through CShells.
General HTTP factories remain shell-owned so feature-local named-client configuration is preserved. The
mandatory Api feature restores standard local auth/authorization infrastructure; installed scheme/policy
callbacks flow from root. No private scheme provider or closed options-monitor override is shared globally.

The standard payload reserves `/studio`: index, application bundles, CSS, images/fonts, SDK and import-map
vendors. Independently selected modules retain their `/_content/<package-id>/...` assets. A separately named
fallback document can use the standard bundle through IndexFile. For a fully custom frontend, the final
application's direct Hosting PackageReference can set `ExcludeAssets="build;buildTransitive"`; its own frontend
then owns SDK/vendor compatibility, while Hosting's .NET APIs and independent module assets remain available.
Same-path overlays with the standard bundle and arbitrary application PathBase are not supported contracts.

Source contributors build the Studio workspace frontend before .NET build/pack. The sole standard source
trees are Client, StudioSdk and StudioUi beside this project; output is wwwroot/studio. Packing fails clearly
when required index/application/stylesheet/SDK/vendor output is missing. This is a producer check, with no Node consumer build hook.
Browser source/tool inputs are excluded from .NET content and packages.
The reference Web project removes its obsolete generated wwwroot/studio subtree during build so existing
checkouts adopt Hosting's payload without retaining a conflicting copy.

Optional administration/themes are separate public packages; the thin reference distribution composes the same APIs.
See the [standalone package-host recipe and migration guide](../../../docs/custom-hosting.md) for explicit candidate versions/preview sources, module selection and supported limits.
The package declares no domain events, contributor contracts or scheduled tasks; its
private resolver implementations use existing CShells contracts. Public composition/options/coordination and
Api policy surface are tracked. Tests use public APIs/HTTP without a production friend; see
[spec1008](https://github.com/elsa-workflows/elsa-foundation/blob/main/specs/1008-studio-base-hosting/quickstart.md)
and [spec1009](https://github.com/elsa-workflows/elsa-foundation/blob/main/specs/1009-studio-auth-bridge/quickstart.md).

When a host replaces the core `IShellBlueprintProvider` or `IShellRegistry` registration, applied
`IOptions<StudioHostingOptions>.Value.ShellsFile` is null. Optional file administration must then treat the
shell configuration as host-owned/read-only, even if a previously loaded file still exists. The selected shell
and other installed options remain unchanged. Addons must also check actual configuration-provider ownership
before writing; a file path alone does not establish that its values control the running shell.


## Branding

The standard browser supports public host branding and independent Linen/palette/appearance defaults.
See the [integrator guide](../../../docs/branding.md) for configuration, image-led login layouts,
custom artwork, replacement presentation and shared module styling.


## Backend connections

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

Studio:BackendBaseUrl remains supported as the browser-facing **stable Studio-host identity authority**. When absent,
the configured default profile supplies that authority at startup. Studio:BackendServerBaseUrl may independently
provide its server-side address. Selecting a different backend never changes the host identity authority or redirects
the host management bridge. Browser configuration contains neither server addresses nor management keys.

Personal profile metadata uses the governed backend-profiles preference namespace through root-host
`GET/PUT /_elsa/studio/backends/profiles`. The server derives user, tenant and Studio HostId; caller-supplied scope is
ignored. Read/write require studio/preferences:read and studio/preferences:write respectively. Writes require
If-None-Match: * or an exact If-Match revision. Limits are 32 profiles, 32 KiB namespace value and a bounded request body.
Unknown fields and secrets are rejected. Authenticated sessions without a verified subject and tenant cannot persist
profiles. Anonymous development uses origin/host/session-scoped browser metadata, never a shared anonymous server record.

The default Hosting store is in memory and survives browser reloads within the running host, **not a host restart**.
For durable profiles, replace IStudioPreferenceStore with the existing Preferences persistence adapter in the root
container; for example, reference Elsa.Studio.Preferences.Persistence.EntityFrameworkCore and a selected EF provider,
register StudioPreferencesSqliteDbContext as StudioPreferencesDbContext and EfStudioPreferenceStore as a scoped
IStudioPreferenceStore. The host owns connection strings, schema initialization and migrations. Base Hosting deliberately
requires only Preferences.Core. BackendProfilesTests exercises the public root API across a real SQLite host restart.

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

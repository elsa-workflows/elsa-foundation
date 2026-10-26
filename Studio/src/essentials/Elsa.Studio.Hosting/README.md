# Elsa.Studio.Hosting

Base server composition for a Studio host. References Studio Api/Core and CShells.AspNetCore; Nuplane,
Theme Store and distribution modules are separate choices. Prebuilt browser source/assets are moved in
program #3340's #3361; this server extraction alone does not claim the final browser/package journey.

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

Optional administration/themes, prebuilt browser ownership and final thin distribution remain their separately
scoped program leaves. The package declares no domain events, contributor contracts or scheduled tasks; its
private resolver implementations use existing CShells contracts. Public composition/options/coordination and
Api policy surface are tracked. Tests use public APIs/HTTP without a production friend; see
[spec1008](https://github.com/elsa-workflows/elsa-foundation/blob/main/specs/1008-studio-base-hosting/quickstart.md)
and [spec1009](https://github.com/elsa-workflows/elsa-foundation/blob/main/specs/1009-studio-auth-bridge/quickstart.md).

When a host replaces the core `IShellBlueprintProvider` or `IShellRegistry` registration, applied
`IOptions<StudioHostingOptions>.Value.ShellsFile` is null. Optional file administration must then treat the
shell configuration as host-owned/read-only, even if a previously loaded file still exists. The selected shell
and other installed options remain unchanged. Addons must also check actual configuration-provider ownership
before writing; a file path alone does not establish that its values control the running shell.


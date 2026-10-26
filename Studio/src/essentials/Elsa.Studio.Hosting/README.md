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
Dispose the lease; consumers cannot release or dispose the underlying coordinator. It coordinates one host,
not arbitrary external processes writing the same files.

This leaf preserves existing Web Studio bearer/bridge registration; #3358 moves those internals into Hosting.
Generic host-supplied authentication/policies work through the common pipeline. Trusted query-token prefixes
(default empty) are validated registration input for that security integration. #3359/#3360 extract optional
administration/themes and complete their selected-shell/storage cases; #3362 completes the thin distribution.

There are no registered domain events, contributor interfaces or scheduled tasks in this package. The startup
service validates the selected provided blueprint without activating optional features. Public composition,
options and coordination surface are tracked in PublicAPI files. Behavioral tests use public APIs and HTTP,
without a production friend grant; see [spec 1008](https://github.com/elsa-workflows/elsa-foundation/blob/main/specs/1008-studio-base-hosting/quickstart.md).

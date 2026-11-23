# Elsa Foundation Hosting

Reference `Elsa.Foundation.Hosting` to opt an existing ASP.NET Core application into Foundation's shell activation, retry, and Nuplane feature discovery. You do not need to reference `Elsa.Foundation.Host`.

```csharp
using CShells.AspNetCore.Extensions;
using Elsa.Foundation.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("shells.json", optional: true, reloadOnChange: true);

builder.AddFoundationNuplane();
builder.AddFoundationShellHosting();

var app = builder.Build();
// Keep the application's existing middleware, authentication, and routes.
app.MapShells(); // Only needed if you want to serve shell HTTP endpoints.
app.Run();
```

Configure the package feeds and assembly loading under `Nuplane`, and shell blueprints under `CShells:Shells`. Relative directory feed paths use the application's content root. The `AddFoundationShellHosting` callback lets the application configure CShells, including routing and feature discovery from host assemblies.

Hosts that want the shared Nuplane feed and loading setup but own their own CShells profile can call `AddNuplaneHostIntegration(configuration)` and configure `NuplaneIntegrationOptions` separately. The optional configuration argument selects the settings root used for Nuplane feeds and reload notifications; when omitted, the builder's configuration is used.

Eager activation and automatic shell reload after package changes are enabled by default. To opt out, set `Elsa:Boot:EagerShellActivation:Enabled` or `Elsa:Shells:ReloadOnPackageChange` to `false`, respectively. The library does not map health or module-management endpoints or configure the application's authentication, cluster membership, or key storage.
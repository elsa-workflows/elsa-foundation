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

Eager activation and automatic shell reload after package changes are enabled by default. To opt out, set `Elsa:Boot:EagerShellActivation:Enabled` or `Elsa:Shells:ReloadOnPackageChange` to `false`, respectively. The library configures none of the application's authentication, cluster membership, or key storage. It maps nothing on its own; a host that wants the Foundation host surface opts in to each piece:

```csharp
const string HostOwner = "My.Host";

var shellActivation = builder.AddFoundationShellHosting(shells => shells
    .WithWebRouting(options => options.ExcludePaths = ["/health/live", "/health/ready"]));

// The shell-activation Attention item, served by every active shell's Attention endpoint.
builder.Services.AddSingleton<IAttentionContributor>(new ShellActivationAttentionContributor(shellActivation));

var app = builder.Build();
app.MapFoundationHealth(HostOwner); // /health/live and /health/ready

// Reconcile and reload behind a static API key; read once at startup.
var moduleManagement = ModuleManagementOptions.Read(builder.Configuration);
if (moduleManagement.Enabled)
    app.MapModuleManagementApi(moduleManagement, HostOwner);
```

The module-management endpoints need `AddNuplaneAdmin()` and the host's `ManualReconcileCoordinator` and `INuplaneAdminOperations` shared with shells, as `Elsa.Foundation.Host` composes them.

## Cross-domain contributions

This package implements a contributor contract from another domain:

- **`IAttentionContributor`** *(Core, `Elsa.Attention.Core`)*: `ShellActivationAttentionContributor` adds one Attention item for each shell whose activation failed and that is not active now: a warning for a fault, critical when an EF module refused activation. The host registers it on its root container and CShells copies it into each shell, so every active shell's Attention endpoint lists it.
  - Known impl: `ShellActivationAttentionContributor`.
  - Catalog: none yet; the Attention domain has no `EXTENSION_POINTS.md`.

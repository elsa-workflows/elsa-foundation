using CShells.AspNetCore.Configuration;
using CShells.AspNetCore.Extensions;
using CShells.DependencyInjection;
using Elsa.Foundation.Host.Feed;
using Elsa.Foundation.Host.Health;
using Elsa.Foundation.Host.ModuleManagement;
using Elsa.Foundation.Host.Shells;
using Nuplane;
using Nuplane.Admin;
using Nuplane.Loading.Hosting.Builder;
using Nuplane.Sources.Directory.Configuration;

var builder = WebApplication.CreateBuilder(args);

// Shell composition (which features each shell enables) is authored in shells.json, layered under the
// standard appsettings files. reloadOnChange lets CShells re-read a shell's blueprint on reload.
builder.Configuration.AddJsonFile("shells.json", optional: true, reloadOnChange: true);

var configuration = builder.Configuration;
var nuplaneConfiguration = configuration.GetSection("Nuplane");

// ---------------------------------------------------------------------------------------------------------
// Nuplane — the package feed and runtime assembly loader that supplies feature assemblies to CShells.
// ---------------------------------------------------------------------------------------------------------
// * AddDirectoryFeedsFromConfiguration reads Nuplane:Setup:Feeds. A feed with Directory:Watch=true installs
//   the folder listener that reconciles the package set whenever the drop-folder changes.
// * AutoloadPackages installs the assembly-loading subsystem (IPackageAssemblyCatalog) that
//   NuplaneAssemblyProvider hands to CShells for feature discovery.
builder.Services.AddNuplane(nuplaneConfiguration, nuplane =>
{
    nuplane.AddDirectoryFeedsFromConfiguration(nuplaneConfiguration);
    nuplane.AutoloadPackages(nuplaneConfiguration.GetSection("Loading"));

    // Optional extra #2 — hot reload: when the feed applies a package change (folder listener or a manual
    // reconcile), the observer reloads the active shells so a running server picks up new assemblies without
    // a restart. It is always registered but no-ops unless Elsa:Shells:ReloadOnPackageChange is true.
    nuplane.OnPackagesChanged<ShellReloadOnPackagesChanged>();
});

// The bridge that hands Nuplane-loaded assemblies to CShells feature discovery.
builder.Services.AddSingleton<NuplaneAssemblyProvider>();
builder.Services.AddSingleton<ShellReloadOnPackagesChanged>();

// ---------------------------------------------------------------------------------------------------------
// CShells — activate shells, map them, own per-shell middleware.
// ---------------------------------------------------------------------------------------------------------
builder.Services.AddCShellsAspNetCore(shells => shells
    // Feature assemblies come ONLY from the Nuplane feed; the host compiles in no features of its own.
    .WithAssemblyProvider<NuplaneAssemblyProvider>()
    // Shell composition is read from shells.json (default section name "CShells").
    .WithConfigurationProvider(configuration)
    // Path-based shell routing; the health probes below bypass shell resolution.
    .WithWebRouting(options =>
    {
        options.EnablePathRouting = true;
        options.ExcludePaths = ["/health/live", "/health/ready"];
    }));

// Optional — eager activation: activate the configured shell(s) at boot so shell-lifetime work (most notably
// the feed's Tasks feature: startup/background/recurring tasks) starts without waiting for the first request.
// Gated by Elsa:Boot:EagerShellActivation:Enabled (default off). Uses only CShells; no Elsa dependency.
if (EagerShellActivationHostedService.IsEnabled(configuration))
    builder.Services.AddHostedService<EagerShellActivationHostedService>();

// Optional extra #1 — module-management surface (Nuplane reconcile + CShells shell reload). Compiled in but
// only wired when Elsa:ModuleManagement:Enabled is true at startup: the decision is read once here, so
// turning it on or off requires a restart, by design.
var moduleManagement = ModuleManagementOptions.Read(configuration);
if (moduleManagement.Enabled)
    builder.Services.AddNuplaneAdmin();

var app = builder.Build();

// Host-level probes: liveness (process up) + readiness (configured shells activated — reflects eager load).
app.MapHostHealth();

if (moduleManagement.Enabled)
    app.MapModuleManagementApi(moduleManagement);

// Maps the shell resolution middleware + the dynamic shell endpoint source. Each shell's own middleware and
// endpoints are composed by CShells on activation and re-composed per generation on reload.
app.MapShells();

app.Run();

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
// * AddNuplane itself reads Nuplane:Setup:Feeds and registers every entry carrying a ServiceIndex as a remote
//   NuGet feed. Remote feeds need no call here; see docs/foundation-host-feeds.md. Note that a remote feed's
//   IncludePatterns must name package ids literally — a wildcard contributes nothing and does so silently.
// * AddDirectoryFeedsFromConfiguration translates the entries carrying a DirectoryPath instead. It is a separate
//   call only because the directory source ships in its own package, which AddNuplane cannot reach. A feed with
//   Directory:Watch=true installs the folder listener that reconciles the package set when the drop-folder changes.
// * AutoloadPackages installs the assembly-loading subsystem (IPackageAssemblyCatalog) that
//   NuplaneAssemblyProvider hands to CShells for feature discovery.
builder.Services.AddNuplane(nuplaneConfiguration, nuplane =>
{
    // The content root, which is where this host's own appsettings.json was just read from. A module-owned
    // builder extension resolves its relative configured paths against NuplaneBuilder.BasePath when
    // something sets one, and against the process's current directory otherwise — and those two are the
    // same place only when the host happens to be started from its own directory. Without this line a host
    // whose content root is set independently of where it was launched (ASPNETCORE_CONTENTROOT, a systemd
    // unit, a container WORKDIR) reads "DirectoryPath": "packages" out of one directory and then looks for
    // that folder under another, which presents as an empty feed with nothing saying why. Nuplane's core
    // package never reads IHostEnvironment itself, which is why this stays the host's own line to write.
    nuplane.UseBasePath(builder.Environment.ContentRootPath);
    nuplane.AddDirectoryFeedsFromConfiguration(nuplaneConfiguration);
    nuplane.AutoloadPackages(nuplaneConfiguration.GetSection("Loading"));

    // Optional extra #2 — hot reload: when the feed applies a package change (folder listener or a manual
    // reconcile), the observer refreshes the CShells runtime feature catalog and reloads the active shells so a
    // running server picks up new assemblies without a restart. Always registered; no-ops unless
    // Elsa:Shells:ReloadOnPackageChange is true and a shell is already active.
    nuplane.OnPackagesChanged<ShellReloadOnPackagesChanged>();
});

// The bridge that hands Nuplane-loaded assemblies to CShells feature discovery.
builder.Services.AddSingleton<NuplaneAssemblyProvider>();
builder.Services.AddSingleton<ShellReloadOnPackagesChanged>();

// ---------------------------------------------------------------------------------------------------------
// CShells — activate shells, map them, own per-shell middleware.
// ---------------------------------------------------------------------------------------------------------
builder.Services.AddCShellsAspNetCore(shells => shells
    // Domain feature assemblies come from the Nuplane feed; the host compiles in no Elsa features of its own.
    .WithAssemblyProvider<NuplaneAssemblyProvider>()
    // Also scan the host's own referenced assemblies so the FastEndpoints runtime seam is available to every
    // shell without each feed feature declaring DependsOn "FastEndpoints". The only host assembly that carries
    // a [ShellFeature] is CShells.FastEndpoints (its built-in "FastEndpoints" feature, which scans a shell's
    // active IFastEndpointsShellFeature assemblies and maps their endpoints). A shell enables it once via
    // shells.json ("Features": { "FastEndpoints": {} }); feed features then only reference
    // CShells.FastEndpoints.Abstractions. CShells.FastEndpoints.Abstractions is a shared assembly (see
    // Nuplane:Loading:SharedAssemblies) so a feed feature's IFastEndpointsShellFeature is the same type here.
    .WithHostAssemblies()
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

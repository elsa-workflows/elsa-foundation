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

// CORS — the Elsa Studio SPA runs on a separate origin and calls this host's API from the browser, so the
// host must emit CORS headers or every cross-origin request fails preflight. The host composes no Elsa
// features, so this is a host-level concern (as in Elsa.Workbench). Origins come from Cors:AllowedOrigins;
// a dev default covers the local Studio port.
const string studioCorsPolicy = "ElsaStudio";
var studioCorsOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
if (studioCorsOrigins is null || studioCorsOrigins.Length == 0)
    studioCorsOrigins = ["http://localhost:14000"];
builder.Services.AddCors(options =>
{
    options.AddPolicy(studioCorsPolicy, policy => policy
        .WithOrigins(studioCorsOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        // AllowAnyHeader covers the *request* side only. Content-Disposition is not a CORS-safelisted response
        // header, so without this the executable-export download (FR-B-010a) reaches Studio with its filename
        // unreadable -- the browser strips the header from the JS-visible response and the client silently falls
        // back to a name the server never chose. This host is the one Studio actually calls, so the export
        // endpoint's filename contract only holds in production because of this line. Workbench carries the same
        // line for the same reason; they must not drift apart.
        .WithExposedHeaders("Content-Disposition")
        .AllowCredentials());
});

var app = builder.Build();

// CORS runs before shell resolution/endpoints so the policy applies to every shell-mapped API route.
app.UseCors(studioCorsPolicy);

// Host-level probes: liveness (process up) + readiness (configured shells activated — reflects eager load).
app.MapHostHealth();

if (moduleManagement.Enabled)
    app.MapModuleManagementApi(moduleManagement);

// Maps the shell resolution middleware + the dynamic shell endpoint source. Each shell's own middleware and
// endpoints are composed by CShells on activation and re-composed per generation on reload.
app.MapShells();

app.Run();

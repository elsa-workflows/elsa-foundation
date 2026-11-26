using CShells.AspNetCore.Configuration;
using CShells.AspNetCore.Extensions;
using CShells.DependencyInjection;
using Elsa.Attention.Core;
using Elsa.Cluster.Core.Extensions;
using Elsa.Cluster.Hosting;
using Elsa.Cluster.Readability;
using Elsa.Foundation.DataProtection;
using Elsa.Foundation.Hosting;
using Elsa.Foundation.Identity.OpenIddict.EntityFrameworkCore;
using Elsa.Modularity.Api.ModuleManagement;
using Elsa.Modularity.EntityFramework.Extensions;
using Nuplane.Admin;
using Nuplane.Reconciliation;

// The owner every endpoint this host maps is attributed to.
const string HostOwner = "Elsa.Foundation.Host";
const string StudioCorsPolicy = "ElsaStudio";

var builder = WebApplication.CreateBuilder(args);

// Shell configuration is layered after appsettings, with environment variables and arguments taking precedence.
builder.Configuration
    .AddJsonFile("shells.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"shells.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args);

var configuration = builder.Configuration;
var shellAuthenticationEnabled = configuration.GetValue<bool>("Elsa:Authentication:Enabled");
if (configuration.GetValue<bool>("Elsa:Identity:OpenIddict:EntityFrameworkCore:Enabled"))
    builder.Services.AddOpenIddictEntityFrameworkCoreStore(configuration);

var studioOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
if (studioOrigins.Length > 0)
{
    builder.Services.AddCors(options => options.AddPolicy(StudioCorsPolicy, policy => policy
        .WithOrigins(studioOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()
        .WithExposedHeaders("Content-Disposition", "Retry-After")));
}

// ---------------------------------------------------------------------------------------------------------
// Nuplane — the package feed and runtime assembly loader that supplies feature assemblies to CShells.
// ---------------------------------------------------------------------------------------------------------
builder.AddFoundationNuplane();

// ---------------------------------------------------------------------------------------------------------
// Cluster membership — selected once per host, on this container, never per shell (#2143).
// ---------------------------------------------------------------------------------------------------------
// Exactly as Elsa.Workbench composes it: the readability report of every schema family loaded in any load context,
// the in-process membership default (a cluster of one, writing nothing durable), the finalization gate's view of the
// fleet, and the dormancy check over what each shell's gates observed. CShells copies these registrations into every
// shell, and the types they are registered under come from Elsa.Cluster.Core and Elsa.Persistence.Schema, which
// Nuplane:Loading:SharedAssemblies shares with every package: so an EF module package finds this fleet and finalizes
// its schema versions, and a feed-loaded feature leaves dormancy once the version it needs is
// finalized. Without it, every gate here would stay at the version its record was created at, and
// every declared requirement would read "not observed".
builder.Services.AddEfSchemaReadability();
// Profiles with a distributed runtime can report each shell's runnability through the host's membership provider.
if (configuration.GetValue<bool>("Elsa:Cluster:RunnabilityReport:Enabled"))
    builder.Services.AddSharedRunnabilityReport();
// The durable EF provider replaces that default only when the Elsa:Cluster:Membership section enables it, and then
// refuses to start without an explicit Elsa:Cluster:Membership:HostId (#2151). Hosts that
// share one database this way count each other, so a feed-loaded EF module's new schema version is finalized only once
// every live host can read it. Provider settings without the enabling switch are refused, never ignored. This line
// names no EF type: the extension lives in a provider-neutral namespace, and the host's EF closure exists for it and for
// the Data Protection key store below, nothing else.
builder.Services.AddConfiguredClusterMembership(configuration);

// The Data Protection key ring every shell protects its cookies and antiforgery tokens with, composed once per host as
// membership is; the Elsa:DataProtection section names it and enables its shared key store (#2191).
builder.Services.AddConfiguredDataProtection(configuration);

// A shell runs the ValidateOnStart checks of its own container as it activates, and a failing one refuses the
// activation: the generic host runs them for the host container alone, and CShells runs none (#2331).
builder.Services.AddShellStartupValidation();
if (configuration.GetValue<bool>("Elsa:Persistence:EntityFramework:Migrate:GuardFeatureActivation"))
    builder.Services.AddEfPendingMigrationActivationGuard();

// ---------------------------------------------------------------------------------------------------------
// CShells — activate shells, map them, own per-shell middleware.
// ---------------------------------------------------------------------------------------------------------
var shellActivation = builder.AddFoundationShellHosting(shells =>
{
    // The host carries CShells' FastEndpoints feature. Shells opt in by enabling FastEndpoints in shells.json.
    shells.WithHostAssemblies()
        .WithWebRouting(options =>
        {
            options.EnablePathRouting = true;
            options.ExcludePaths = ["/health/live", "/health/ready"];
        });

    if (shellAuthenticationEnabled)
        shells.WithAuthenticationAndAuthorization();
});

// Each shell's Attention endpoint can read the same tracker as the host's readiness probe.
builder.Services.AddSingleton<IAttentionContributor>(new ShellActivationAttentionContributor(shellActivation));

// Register ASP.NET Core auth after CShells so its per-shell scheme and policy providers stay in place.
if (shellAuthenticationEnabled)
{
    builder.Services.AddAuthentication();
    builder.Services.AddAuthorization();
}

var moduleManagement = ModuleManagementOptions.Read(configuration);
var packageApiEnabled = moduleManagement.Enabled || configuration.GetValue<bool>("Elsa:ModuleManagement:PackageApiEnabled");
if (packageApiEnabled)
{
    builder.Services.AddNuplaneAdmin();
    builder.Services.AddElsaModuleManagement(options => options.HostOwner = HostOwner);
    // The registry and shell endpoints share the host's Nuplane coordinator.
    builder.Services.ShareWithShells<ManualReconcileCoordinator>().ShareWithShells<INuplaneAdminOperations>();
}

var app = builder.Build();

if (studioOrigins.Length > 0)
    app.UseCors(StudioCorsPolicy);

app.MapFoundationHealth(HostOwner);

if (packageApiEnabled)
    app.MapElsaModuleManagement();

if (moduleManagement.Enabled)
    app.MapModuleManagementApi(moduleManagement, HostOwner);

// Activate the shell scope first so auth uses the shell's schemes and policies.
app.MapShells();

if (shellAuthenticationEnabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

// A host its cluster member stopped, because another process holds its host id, ends with exit code 1, so a supervisor
// that restarts failed processes restarts it.
app.RunWithMembershipExitCode();

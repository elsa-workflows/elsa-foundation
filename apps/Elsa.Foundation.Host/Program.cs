using CShells.AspNetCore.Configuration;
using CShells.AspNetCore.Extensions;
using CShells.DependencyInjection;
using Elsa.Attention.Core;
using Elsa.Cluster.Hosting;
using Elsa.Cluster.Readability;
using Elsa.Foundation.DataProtection;
using Elsa.Foundation.Host.Health;
using Elsa.Foundation.Host.ModuleManagement;
using Elsa.Foundation.Host.Shells;
using Elsa.Foundation.Hosting;
using Elsa.Modularity.Api.ModuleManagement;
using Nuplane.Admin;
using Nuplane.Reconciliation;

var builder = WebApplication.CreateBuilder(args);

// Shell composition (which features each shell enables) is authored in shells.json, layered under the
// standard appsettings files. reloadOnChange lets CShells re-read a shell's blueprint on reload.
builder.Configuration.AddJsonFile("shells.json", optional: true, reloadOnChange: true);

var configuration = builder.Configuration;

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

// ---------------------------------------------------------------------------------------------------------
// CShells — activate shells, map them, own per-shell middleware.
// ---------------------------------------------------------------------------------------------------------
var shellActivation = builder.AddFoundationShellHosting(shells => shells
    // Also scan the host's own referenced assemblies so the FastEndpoints runtime seam is available to every
    // shell without each feed feature declaring DependsOn "FastEndpoints". The only host assembly that carries
    // a [ShellFeature] is CShells.FastEndpoints (its built-in "FastEndpoints" feature, which scans a shell's
    // active IFastEndpointsShellFeature assemblies and maps their endpoints). A shell enables it once via
    // shells.json ("Features": { "FastEndpoints": {} }); feed features then only reference
    // CShells.FastEndpoints.Abstractions. CShells.FastEndpoints.Abstractions is a shared assembly (see
    // Nuplane:Loading:SharedAssemblies) so a feed feature's IFastEndpointsShellFeature is the same type here.
    .WithHostAssemblies()
    // Path-based shell routing; the health probes below bypass shell resolution.
    .WithWebRouting(options =>
    {
        options.EnablePathRouting = true;
        options.ExcludePaths = ["/health/live", "/health/ready"];
    }));

// Each shell's Attention endpoint can read the same tracker as the host's readiness probe.
builder.Services.AddSingleton<IAttentionContributor>(new ShellActivationAttentionContributor(shellActivation));

// Optional extra #1 — module-management surface (Nuplane reconcile + CShells shell reload). Compiled in but
// only wired when Elsa:ModuleManagement:Enabled is true at startup: the decision is read once here, so
// turning it on or off requires a restart, by design.
var moduleManagement = ModuleManagementOptions.Read(configuration);
if (moduleManagement.Enabled)
    builder.Services.AddNuplaneAdmin();
if (moduleManagement.Enabled)
    builder.Services.AddElsaModuleManagement(options => options.HostOwner = "Elsa.Foundation.Host");

// Admin operations use the same coordinator as the host, not one created in a shell's container (#2159).
if (moduleManagement.Enabled)
    builder.Services.ShareWithShells<ManualReconcileCoordinator>().ShareWithShells<INuplaneAdminOperations>();

var app = builder.Build();

// Host-level probes: liveness (process up) + readiness (configured shells are settled and current).
app.MapHostHealth();

if (moduleManagement.Enabled)
{
    app.MapElsaModuleManagement();
    app.MapModuleManagementApi(moduleManagement);
}

// Maps the shell resolution middleware + the dynamic shell endpoint source. Each shell's own middleware and
// endpoints are composed by CShells on activation and re-composed per generation on reload.
app.MapShells();

// A host its cluster member stopped, because another process holds its host id, ends with exit code 1, so a supervisor
// that restarts failed processes restarts it.
app.RunWithMembershipExitCode();

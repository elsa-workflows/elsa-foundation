using CShells.AspNetCore.Configuration;
using CShells.AspNetCore.Extensions;
using CShells.DependencyInjection;
using Elsa.Attention.Core;
using Elsa.Cluster.Hosting;
using Elsa.Cluster.Readability;
using Elsa.Foundation.DataProtection;
using Elsa.Foundation.Hosting;
using Elsa.Foundation.Identity.OpenIddict.EntityFrameworkCore;
using Elsa.Modularity.Api.ModuleManagement;
using Elsa.Modularity.EntityFramework.Extensions;
using Elsa.Workflows.Runtime.Distributed.Placement;
using Nuplane.Admin;
using Nuplane.Reconciliation;

// The owner every endpoint this host maps is attributed to.
const string HostOwner = "Elsa.Essentials.Host";
const string StudioCorsPolicy = "ElsaStudio";

var builder = WebApplication.CreateBuilder(args);

// The curated shell composition, and the environment overlay that hardens it (shells.Production.json blanks the demo keys and
// seeded admin password shells.json ships for local development).
builder.Configuration.AddJsonFile("shells.json", optional: true, reloadOnChange: true);
builder.Configuration.AddJsonFile($"shells.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true);
// CreateBuilder adds environment variables and the command line before the shell files; re-add them so a container's
// environment overrides shells.json and the command line still wins.
builder.Configuration.AddEnvironmentVariables().AddCommandLine(args);

var configuration = builder.Configuration;

// ---------------------------------------------------------------------------------------------------------
// Host-composed services: one per host, on this container, never per shell. CShells copies them into every shell.
// ---------------------------------------------------------------------------------------------------------

// The OpenIddict token store every shell issues and validates tokens against.
builder.Services.AddOpenIddictEntityFrameworkCoreStore(configuration);

// Nuplane: the feed and assembly loader that brings add-on packages beside the compiled-in essentials.
builder.AddFoundationNuplane();

// Cluster membership (a cluster of one unless Elsa:Cluster:Membership enables the durable provider), the schema readability
// report the EF modules finalize their versions against, and the Data Protection key ring (#2143, #2151, #2191).
builder.Services.AddEfSchemaReadability();
// What each shell's workflow runtime can activate, so this member's report carries a runnability entry per shell that runs the
// distributed runtime; without it, peers read a durable-membership member as able to run nothing.
builder.Services.AddWorkflowRuntimeRunnabilityReport();
builder.Services.AddConfiguredClusterMembership(configuration);
builder.Services.AddConfiguredDataProtection(configuration);

// A shell runs its own ValidateOnStart checks as it activates (#2331), and refuses activation while an EF module it enables has a
// pending migration the configured policy does not allow. The guard must be registered before CShells.
builder.Services.AddShellStartupValidation();
builder.Services.AddEfPendingMigrationActivationGuard();

// Studio's module screens read the host's module registry; reconcile and reload go through the host's own Nuplane services.
builder.Services.AddNuplaneAdmin();
builder.Services.AddElsaModuleManagement(options => options.HostOwner = HostOwner);
builder.Services.ShareWithShells<ManualReconcileCoordinator>().ShareWithShells<INuplaneAdminOperations>();

// Browser access for Elsa Studio. Content-Disposition carries the server-owned download name of an exported artifact (#1794) and
// Retry-After the retry hint of a refused request; neither is a CORS-safelisted response header.
var studioOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddPolicy(StudioCorsPolicy, policy => policy
    .WithOrigins(studioOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()
    .WithExposedHeaders("Content-Disposition", "Retry-After")));

// ---------------------------------------------------------------------------------------------------------
// CShells: the compiled-in essentials are discovered from this host's own assemblies, add-ons from the feed.
// ---------------------------------------------------------------------------------------------------------
var shellActivation = builder.AddFoundationShellHosting(shells => shells
    .WithHostAssemblies()
    // Each shell's Identity composition (schemes, permission policies) resolves at request time from the shell scope.
    .WithAuthenticationAndAuthorization()
    .WithWebRouting(options =>
    {
        options.EnablePathRouting = true;
        options.ExcludePaths = ["/health/live", "/health/ready"];
    }));

// Each shell's Attention endpoint lists the shells this host could not activate.
builder.Services.AddSingleton<IAttentionContributor>(new ShellActivationAttentionContributor(shellActivation));

// Root authentication and authorization, registered after CShells so its shell-delegating providers stay in place.
builder.Services.AddAuthentication();
builder.Services.AddAuthorization();

var app = builder.Build();

app.UseCors(StudioCorsPolicy);
app.MapFoundationHealth(HostOwner);
app.MapElsaModuleManagement();

// Reconcile and reload behind a static API key, off unless Elsa:ModuleManagement:Enabled is set at startup.
var moduleManagement = ModuleManagementOptions.Read(configuration);
if (moduleManagement.Enabled)
    app.MapModuleManagementApi(moduleManagement, HostOwner);

app.MapShells();

// After MapShells, whose middleware swaps the request services to the shell scope first.
app.UseAuthentication();
app.UseAuthorization();

// A host whose cluster member was stopped because another process holds its host id exits with code 1, so a supervisor restarts it.
app.RunWithMembershipExitCode();

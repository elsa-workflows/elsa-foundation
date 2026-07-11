using ConsoleLogStreaming.AspNetCore.DependencyInjection;
using ConsoleLogStreaming.Core.DependencyInjection;
using CShells.AspNetCore.Configuration;
using CShells.AspNetCore.Extensions;
using CShells.DependencyInjection;
using CShells.Management.Api;
using Elsa.Api.FastEndpoints;
using Elsa.Server;
using Elsa.Activities.Composition.Design;
using Elsa.Activities.Composition.Runtime;
using Elsa.Activities.Design.Api;
using Elsa.Activities.Design.Core.Options;
using Elsa.Activities.Design.Reconciliation;
using Elsa.Activities.Design.Reconciliation.Clr;
using Elsa.Activities.Flowchart;
using Elsa.Activities.Http;
using Elsa.Activities.Primitives;
using Elsa.Activities.Runtime;
using Elsa.Activities.Sequence;
using Elsa.Agent.Api;
using Elsa.Agent.Core;
using Elsa.Agent.GitHubCopilot;
using Elsa.Agent.Workflows;
using Elsa.Caching.Memory;
using Elsa.Diagnostics.ConsoleLogStreaming;
using Elsa.Diagnostics.OpenTelemetry;
using Elsa.Diagnostics.StructuredLogs;
using Elsa.Diagnostics.StructuredLogs.Persistence.EFCore.Sqlite;
using Elsa.Events;
using Elsa.Expressions;
using Elsa.Foundation.Identity.Abstractions;
using Elsa.Foundation.Identity.Api;
using Elsa.Foundation.Identity.AspNetCoreIdentity;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore;
using Elsa.Foundation.Identity.Oidc;
using Elsa.Foundation.Identity.OpenIddict;
using Elsa.Locking.FileSystem;
using Elsa.Mediator;
using Elsa.Modularity.Api;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Elsa.Modularity.Core.Contracts;
using Elsa.Modularity.Nuplane.Extensions;
using Elsa.Modularity.Nuplane.Services;
using Elsa.Persistence.Groundwork.Sqlite.Unified;
using Elsa.Persistence.Groundwork.PostgreSql.Unified;
using Elsa.Primitives.Hosting;
using Elsa.Serialization.Newtonsoft;
using Elsa.Serialization.SystemText;
using Elsa.Tasks;
using Elsa.Workflows.Design.Api;
using Elsa.Workflows.Publishing.Api;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.ReferenceGarbageCollection;
using Elsa.Workflows.Runtime.Resumption;
using Elsa.Workflows.Runtime.Http;
using Nuplane;
using Nuplane.Admin;
using Nuplane.Loading.Hosting.Builder;
using Nuplane.Sources.Directory.Configuration;
using Elsa.Modularity.ExtensionBuilder;
using Elsa.Modularity.ExtensionBuilder.Extensions;

ConsoleLogStreamingSetup.InstallConsoleStreamHookIfEnabled(args);

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("shells.json", optional: true, reloadOnChange: true);
// Environment overlay (e.g. shells.Production.json), layered on top of the dev/demo defaults in shells.json.
// This keeps `git clone && dotnet run` (Development) working out of the box on in-memory stores + ephemeral
// keys + a seeded well-known admin, while Production hardens to durable stores, a persistent signing key
// (secret), and a configured initial admin (password supplied as a secret — never committed).
builder.Configuration.AddJsonFile($"shells.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true);
var configuration = builder.Configuration;

// Console log streaming is a process-global, host-level diagnostic (not a shell feature): capture is a static tee on
// Console.Out and the live stream is a long-lived SignalR connection, so it is composed once on the application root
// rather than per-shell — a shell-hosted hub captures the shell container's IServiceScopeFactory and throws
// ObjectDisposedException on disconnect when that shell is recycled. The host owns the whole stack (console hook,
// capture options, recent/sources HTTP endpoints, hub), gated by the Elsa:Diagnostics:ConsoleLogStreaming:Enabled
// config switch (defaults to off). Because this is composed once at startup, toggling it takes effect only after a
// restart. The enabled check + hook install are done once here and reused.
var consoleLogStreamingEnabled = ConsoleLogStreamingSetup.IsEnabled(configuration);
if (consoleLogStreamingEnabled)
{
    ConsoleLogStreamingSetup.InstallConsoleStreamHook();
    builder.Services.AddConsoleLogStreamingHost(ConsoleLogStreamingSetup.ConfigureHost);
    builder.Services.AddConsoleLogStreamingAspNetCore(ConsoleLogStreamingSetup.ConfigureEndpoints);
}
var nuplaneConfiguration = configuration.GetSection("Nuplane");

const string studioCorsPolicy = "ElsaStudio";

var studioCorsOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
if (studioCorsOrigins is null || studioCorsOrigins.Length == 0)
{
    studioCorsOrigins =
    [
        "https://localhost:7030",
        "http://localhost:5089",
        "http://localhost:5092"
    ];
}

builder.Services.AddCors(options =>
{
    options.AddPolicy(studioCorsPolicy, policy => policy
        .WithOrigins(studioCorsOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});

builder.Services.AddNuplaneAdmin();
builder.Services.Configure<ActivityAvailabilityOptions>(configuration.GetSection(ActivityAvailabilityOptions.SectionName));

// ExtensionBuilder is a root-hosted subsystem (root singletons + a background build worker + management
// endpoints mapped on the root route builder below), not a shell feature — its process-global state and
// hosted worker cannot live in a shell container. It lives in the Elsa.Modularity.ExtensionBuilder module
// and is composed here at the application root, gated by a plain host config switch (defaults to on;
// endpoints are additionally gated by the management API key). Both the root composition and the endpoint
// mapping (see MapElsaExtensionBuilderApi below) honor the switch, so setting it to false genuinely stops
// the subsystem — effective on the next startup.
var extensionBuilderEnabled = !bool.TryParse(configuration["Elsa:ExtensionBuilder:Enabled"], out var ebEnabled) || ebEnabled;
if (extensionBuilderEnabled)
    builder.Services.AddElsaExtensionBuilder(configuration);

builder.Services.AddNuplane(nuplaneConfiguration, nuplane =>
{
    nuplane.AddDirectoryFeedsFromConfiguration(nuplaneConfiguration);
    nuplane.AutoloadPackages(nuplaneConfiguration.GetSection("Loading"));
});
builder.Services.AddSingleton<NuplaneAssemblyProvider>();
builder.Services.AddNuplaneFeatureCatalog();
builder.Services.TryAddScoped<IModuleRegistryService, ModuleRegistryService>();
builder.Services.TryAddScoped<IShellFeatureConfigurationStore, NullShellFeatureConfigurationStore>();
builder.Services.TryAddScoped<IShellReloader, NullShellReloader>();

builder.Services.AddCShellsAspNetCore(shells =>
{
    shells
        .WithHostAssemblies()
        .WithAssemblies(
            typeof(ActivitiesHttpFeature).Assembly,
            typeof(WorkflowsRuntimeHttpFeature).Assembly)
        .WithAssemblyProvider<NuplaneAssemblyProvider>()

        // Delegates authentication-scheme and authorization-policy resolution to the shell scope at
        // request time, so each shell's Identity composition (schemes, permission policies) is honored
        // by the root UseAuthentication/UseAuthorization middleware.
        .WithAuthenticationAndAuthorization()

        .WithConfigurationProvider(configuration)
        .WithWebRouting(options =>
        {
            options.EnablePathRouting = true;
        })
        .ConfigureAllShells(shell => shell
            .WithFeature<ModularityApiFeature>()
            // Binding an absent section is a no-op, so the feature's opt-in default stands unless an
            // operator sets Elsa:Workflows:Runtime:FaultCapture:CaptureStackTrace.
            .WithFeature<RuntimeFaultStackTraceFeature>(feature =>
                configuration.GetSection(RuntimeFaultCaptureOptions.SectionName).Bind(feature)));
});

// Root authentication/authorization services. Registered after AddCShellsAspNetCore so the shell
// delegating scheme/policy providers (WithAuthenticationAndAuthorization) stay in place — both
// AddAuthentication and AddAuthorization use TryAdd semantics for those services.
builder.Services.AddAuthentication();
builder.Services.AddAuthorization();

var app = builder.Build();

app.UseCors(studioCorsPolicy);

app.MapGet("/", () => Results.Ok(new { status = "Healthy", service = "elsa-server" }));
app.MapElsaModuleManagementApi();
if (extensionBuilderEnabled)
    app.MapElsaExtensionBuilderApi();
app.MapElsaWorkflowManagementApi();
app.MapShells();

// Explicit auth middleware placed after MapShells: ShellMiddleware (added by MapShells) swaps
// HttpContext.RequestServices to the shell scope first, so authentication/authorization resolve
// each shell's schemes and permission policies. Explicit calls also suppress WebApplication's
// automatic insertion of these middleware earlier in the pipeline.
app.UseAuthentication();
app.UseAuthorization();

app.MapShellManagementApi("/_admin/shells");

// Root-hosted console log streaming: recent/sources HTTP endpoints + the live SignalR hub (see the registration
// note above). Mapped after UseCors so the Studio cross-origin policy applies, and behind RequireAuthorization so
// the captured console output is not readable anonymously — these are root-mapped endpoints that bypass the
// per-shell ApiSecurity, so they must carry their own authorization. The empty-prefix group keeps the absolute
// routes and applies the convention to every endpoint the mapper adds, including the hub.
if (consoleLogStreamingEnabled)
{
    var consoleLogEndpoints = app.MapGroup("");
    consoleLogEndpoints.RequireAuthorization();
    consoleLogEndpoints.MapConsoleLogStreaming();
}
app.Run();

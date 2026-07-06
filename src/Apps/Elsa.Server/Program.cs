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
using Elsa.Workflows.Runtime.Resumption;
using Nuplane;
using Nuplane.Admin;
using Nuplane.Loading.Hosting.Builder;
using Nuplane.Sources.Directory.Configuration;
using Elsa.Server.ExtensionBuilder;

ConsoleLogStreamingFeature.InstallConsoleStreamHookIfEnabled(args);

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("shells.json", optional: true, reloadOnChange: true);
// Environment overlay (e.g. shells.Production.json), layered on top of the dev/demo defaults in shells.json.
// This keeps `git clone && dotnet run` (Development) working out of the box on in-memory stores + ephemeral
// keys + a seeded well-known admin, while Production hardens to durable stores, a persistent signing key
// (secret), and a configured initial admin (password supplied as a secret — never committed).
builder.Configuration.AddJsonFile($"shells.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true);
var configuration = builder.Configuration;

// Console log streaming is a process-global, host-level diagnostic: capture is a static tee on Console.Out and the
// live stream is a long-lived SignalR connection. It is therefore composed once on the application root rather than
// per-shell — a shell-hosted hub captures the shell container's IServiceScopeFactory and throws
// ObjectDisposedException on disconnect when that shell is recycled on feature enable. The shell feature is only the
// enable/discover surface (see ConsoleLogStreamingFeature); the host owns the console hook, capture options, the
// recent/sources HTTP endpoints, and the hub. Because this is composed once at startup, enabling the feature at
// runtime takes effect only after a restart. The enabled check + hook install are done once here and reused.
var consoleLogStreamingEnabled = ConsoleLogStreamingFeature.IsFeatureEnabled(configuration);
if (consoleLogStreamingEnabled)
{
    ConsoleLogStreamingFeature.InstallConsoleStreamHook();
    builder.Services.AddConsoleLogStreamingHost(ConsoleLogStreamingFeature.ConfigureHost);
    builder.Services.AddConsoleLogStreamingAspNetCore(ConsoleLogStreamingFeature.ConfigureEndpoints);
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
builder.Services.Configure<ExtensionBuilderOptions>(configuration.GetSection("Elsa:ExtensionBuilder"));
builder.Services.AddSingleton<IExtensionBuilderTemplateCatalog, ExtensionBuilderTemplateCatalog>();
builder.Services.AddSingleton<IExtensionBuilderStorage, ExtensionBuilderStorage>();
builder.Services.AddSingleton<ExtensionBuilderBackgroundBuildQueue>();
builder.Services.AddSingleton<IExtensionBuilderBuildQueue>(sp => sp.GetRequiredService<ExtensionBuilderBackgroundBuildQueue>());
builder.Services.AddHostedService<ExtensionBuilderBuildWorker>();
builder.Services.AddScoped<IExtensionBuilderBuildRunner, ExtensionBuilderBuildRunner>();
builder.Services.AddScoped<IExtensionBuilderBuildExecutor, ExtensionBuilderBuildExecutor>();
builder.Services.AddScoped<IExtensionBuilderPromotionService, ExtensionBuilderPromotionService>();
builder.Services.AddScoped<IExtensionBuilderService, ExtensionBuilderService>();

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
        .WithAssemblyProvider<NuplaneAssemblyProvider>()

        // Delegates authentication-scheme and authorization-policy resolution to the shell scope at
        // request time, so each shell's Identity composition (schemes, permission policies) is honored
        // by the root UseAuthentication/UseAuthorization middleware.
        .WithAuthenticationAndAuthorization()

        .WithAssemblies(
            typeof(PrimitivesFeature).Assembly,
            typeof(FileSystemLockingFeature).Assembly,
            typeof(SerializationFeature).Assembly,
            typeof(NewtonsoftSerializationFeature).Assembly,
            typeof(TasksFeature).Assembly,
            typeof(MemoryCacheFeature).Assembly,
            typeof(MediatorFeature).Assembly,
            typeof(EventsFeature).Assembly,
            typeof(ExpressionsFeature).Assembly,

            // JavaScript expression + activity feature assemblies. Listing them here makes their features
            // discoverable by the runtime feature catalog (so they surface as "available" in the modularity UI)
            // and enablable via shell configuration.
            typeof(Elsa.Expressions.JavaScript.JavaScriptFeature).Assembly,
            typeof(Elsa.Expressions.JavaScript.Jint.JintFeature).Assembly,
            typeof(Elsa.Expressions.JavaScript.Libraries.JavaScriptLibrariesFeature).Assembly,
            typeof(Elsa.Expressions.JavaScript.Rendering.JavaScriptRenderingFeature).Assembly,
            typeof(Elsa.Http.JavaScript.HttpJavaScriptFeature).Assembly,
            typeof(Elsa.Workflows.Design.JavaScript.JavaScriptWorkflowsDesignFeature).Assembly,
            typeof(Elsa.Workflows.Runtime.JavaScript.JavaScriptActivitiesFeature).Assembly,

            typeof(SqliteGroundworkUnifiedPersistenceShellFeature).Assembly,
            typeof(PostgreSqlGroundworkUnifiedPersistenceShellFeature).Assembly,
            typeof(WorkflowsDesignApiFeature).Assembly,
            typeof(ActivitiesDesignApiFeature).Assembly,

            // Construction seam (Runtime side): the dispatch factory + registry, the CLR kind, and the
            // Workflow kind. These populate the constructor registry the bridge dispatches through.
            typeof(ActivitiesRuntimeFeature).Assembly,
            typeof(ActivitiesPrimitivesFeature).Assembly,
            typeof(ActivitiesSequenceFeature).Assembly,
            typeof(ActivitiesFlowchartFeature).Assembly,
            typeof(ActivitiesCompositionRuntimeFeature).Assembly,

            // Reconciliation (Design side): the universal pass + the CLR assembly scanner source, which
            // populate the catalog with WriteLine + WorkflowDefinitionActivity as CLR rows at startup.
            typeof(ActivitiesDesignReconciliationFeature).Assembly,
            typeof(ClrActivityReconciliationFeature).Assembly,
            // Workflow kind (Design side): catalogs usable-as-activity workflow versions as WorkflowIdentity rows.
            typeof(ActivitiesCompositionDesignFeature).Assembly,

            // The bridge: publishing endpoints that construct a live activity from a catalog row.
            typeof(WorkflowsPublishingApiFeature).Assembly,

            // Runtime vertical slice: execute published WorkflowExecutable artifacts.
            typeof(WorkflowsRuntimeApiFeature).Assembly,

            // Durable-resumption pump. Every Groundwork persistence provider DependsOn this feature, so
            // its assembly must be in the catalog for CShells to auto-enable it when a durable store is
            // composed; without it the shell fails to activate with a FeatureNotFoundException.
            typeof(WorkflowsRuntimeResumptionFeature).Assembly,

            // Agent surface: provider-neutral endpoints, workflow context/proposals, and provider facade.
            typeof(FoundationAgentAbstractionsFeature).Assembly,
            typeof(FoundationAgentApiFeature).Assembly,
            typeof(FoundationWorkflowsAgentFeature).Assembly,
            typeof(GitHubCopilotAgentFeature).Assembly,

            // Identity surface. The authentication stack secures the API: FoundationIdentityAbstractions
            // (provider-agnostic auth/IAM contracts) plus the OIDC authentication provider module, which
            // registers the external JWT bearer scheme, and — now that Workstream D is landed — the
            // first-party token stack: the identity API endpoints (login/session/token exchange), the
            // ASP.NET Core Identity substrate (cookie sign-in, EF stores, dev seeding), and the OpenIddict
            // token service (JWT issuance + local bearer validation). Together their composite scheme
            // selector becomes the default authenticate/challenge scheme, so an unauthenticated call is
            // rejected with 401. All of these are enabled in the default shell (see shells.json) with
            // IsDevelopmentOrDemo set for local dev (in-memory stores, ephemeral keys, seeded admin).
            // The per-shell ApiSecurity feature is the only way to opt a shell out of endpoint security,
            // and it is never enabled here (and is neutralized in Production regardless — see below).
            //
            // W18 note (resolved): the earlier guard kept the token-issuance endpoints out of the default
            // shell because enabling them without an ITokenService would fault endpoint registration. The
            // OpenIddict module now supplies that service, so the fault condition no longer exists and the
            // features are enabled.
            typeof(FoundationIdentityAbstractionsFeature).Assembly,
            typeof(FoundationIdentityApiFeature).Assembly,
            typeof(OidcAuthenticationFeature).Assembly,
            typeof(AspNetCoreIdentityFeature).Assembly,

            // The EF Core-backed ASP.NET Core Identity substrate (durable stores, SignInManager cookie
            // sign-in, login endpoints/page, dev seeding), enabled in the default shell via shells.json.
            typeof(AspNetCoreIdentityEntityFrameworkCoreFeature).Assembly,

            typeof(OpenIddictIdentityFeature).Assembly,
            typeof(ApiSecurityFeature).Assembly,

            typeof(ModularityApiFeature).Assembly,
            typeof(ConsoleLogStreamingFeature).Assembly,
            typeof(StructuredLogsFeature).Assembly,
            typeof(SqliteStructuredLogsPersistenceShellFeature).Assembly,
            typeof(OpenTelemetryFeature).Assembly
        )

        .WithConfigurationProvider(configuration)
        .WithWebRouting(options =>
        {
            options.EnablePathRouting = true;
        })
        .ConfigureAllShells(shell => shell.WithFeature<ModularityApiFeature>());
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
// note above). Mapped after UseCors so the Studio cross-origin policy applies.
if (consoleLogStreamingEnabled)
    app.MapConsoleLogStreaming();
app.Run();

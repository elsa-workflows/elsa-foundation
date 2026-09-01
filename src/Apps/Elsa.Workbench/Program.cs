using ConsoleLogStreaming.AspNetCore.DependencyInjection;
using ConsoleLogStreaming.Core.DependencyInjection;
using CShells.AspNetCore.Configuration;
using CShells.AspNetCore.Extensions;
using CShells.DependencyInjection;
using CShells.Lifecycle;
using CShells.Management.Api;
using Elsa.Activities.Design.Api;
using Elsa.Activities.Design.Core.Options;
using Elsa.Activities.Design.Reconciliation;
using Elsa.Activities.Design.Reconciliation.Clr;
using Elsa.Activities.Flowchart;
using Elsa.Activities.Graph.Design;
using Elsa.Activities.Graph.Runtime;
using Elsa.Activities.Http;
using Elsa.Activities.Primitives;
using Elsa.Activities.Runtime;
using Elsa.Activities.Sequence;
using Elsa.Agent.Api;
using Elsa.Agent.Core;
using Elsa.Agent.GitHubCopilot;
using Elsa.Agent.Workflows;
using Elsa.Api.AspNetCore;
using Elsa.Api.Capabilities;
using Elsa.Attention.Api;
using Elsa.Caching.Memory;
using Elsa.Diagnostics.ConsoleLogStreaming;
using Elsa.Diagnostics.OpenTelemetry;
using Elsa.Diagnostics.Persistence.Groundwork;
using Elsa.Diagnostics.StructuredLogs;
using Elsa.Events;
using Elsa.Expressions;
using Elsa.Expressions.Api;
using Elsa.Foundation.Identity.Abstractions;
using Elsa.Foundation.Identity.Api;
using Elsa.Foundation.Identity.AspNetCoreIdentity;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork;
using Elsa.Foundation.Identity.Oidc;
using Elsa.Foundation.Identity.OpenIddict;
using Elsa.Foundation.Identity.Persistence.Groundwork;
using Elsa.Locking.FileSystem;
using Elsa.Mediator;
using Elsa.Modularity.Api;
using Elsa.Modularity.Attention;
using Elsa.Modularity.Core.Contracts;
using Elsa.Modularity.ExtensionBuilder;
using Elsa.Modularity.ExtensionBuilder.Extensions;
using Elsa.Modularity.Nuplane.Extensions;
using Elsa.Modularity.Nuplane.Services;
using Elsa.Persistence.Groundwork.PostgreSql.Unified;
using Elsa.Persistence.Groundwork.Sqlite.Unified;
using Elsa.Primitives.Hosting;
using Elsa.Secrets.Attention;
using Elsa.Serialization.Newtonsoft;
using Elsa.Serialization.SystemText;
using Elsa.Studio.Preferences.Api;
using Elsa.Studio.Preferences.Core;
using Elsa.Studio.Preferences.Persistence.Groundwork;
using Elsa.Tasks;
using Elsa.Workbench;
using Elsa.Workbench.Boot;
using Elsa.Workbench.Readiness;
using Elsa.Workflows.Dashboard;
using Elsa.Workflows.Design.Api;
using Elsa.Workflows.Design.Reconciliation;
using Elsa.Workflows.Design.Reconciliation.Json;
using Elsa.Workflows.ExecutionEvidence;
using Elsa.Workflows.Publishing.Api;
using Elsa.Workflows.Publishing.Persistence.Groundwork;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Attention;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Http;
using Elsa.Workflows.Runtime.ReferenceGarbageCollection;
using Elsa.Workflows.Runtime.Resumption;
using Elsa.Workflows.Runtime.Tracing;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NativeEndpoints;
using Nuplane;
using Nuplane.Admin;
using Nuplane.Loading.Hosting.Builder;
using Nuplane.Sources.Directory.Configuration;
using System.Diagnostics;

// Boot phase-timing stopwatch (spec 129). Started at process entry so the opt-in cold-start instrument can
// attribute host-build and first-request wall time. The Stopwatch itself is negligible; nothing is recorded
// unless the Elsa:Boot:PhaseTiming:Enabled switch turns the timeline on below.
var bootStopwatch = Stopwatch.StartNew();

ConsoleLogStreamingSetup.InstallConsoleStreamHookIfEnabled(args);

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("shells.json", optional: true, reloadOnChange: true);
// Environment overlay (e.g. shells.Production.json), layered on top of the dev/demo defaults in shells.json.
// This keeps `git clone && dotnet run` (Development) working out of the box on in-memory stores + ephemeral
// keys + a seeded well-known admin, while Production hardens to durable stores, a persistent signing key
// (secret), and a configured initial admin (password supplied as a secret — never committed).
builder.Configuration.AddJsonFile($"shells.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true);
// WebApplication.CreateBuilder adds environment variables before these shell files. Re-add the environment and
// command-line providers after the shell layers so container environment variables override shells.json, while
// explicit command-line arguments retain the highest precedence.
builder.Configuration
    .AddEnvironmentVariables()
    .AddCommandLine(args);
var configuration = builder.Configuration;

// OpenIddict's protocol behavior is composed by the shell feature, but its vendor EF store is host-owned. Bind the
// default shell's persistence settings at the root so the host-level initializer and every copied shell service
// provider select the same demo in-memory store or durable SQLite store. The behavior composite deliberately does not
// register a DbContext, an EF store, or an initializer; this is the Workbench's explicit vendor choice.
builder.Services.AddWorkbenchOpenIddictVendor(configuration);

// Opt-in cold-start phase instrument (spec 129). Null unless Elsa:Boot:PhaseTiming:Enabled is set, so the host
// registers no boot services and pays nothing when the switch is off. When on, the timeline is a root singleton
// consumed by the first-request middleware, the shell-activation observer, and the ApplicationStarted hook.
var bootTimeline = BootPhaseTimeline.CreateIfEnabled(configuration, bootStopwatch);
if (bootTimeline is not null)
{
    bootTimeline.Mark("config-ready");
    builder.Services.AddSingleton(bootTimeline);
}

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
builder.Services
    .AddOptions<ShellReadinessOptions>()
    .Bind(configuration.GetSection(ShellReadinessOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.DefaultShellName), "A non-empty default shell name is required.")
    .ValidateOnStart();
builder.Services.AddSingleton(new ShellReadinessState(TimeProvider.System));
builder.Services.AddSingleton<DefaultShellWarmup>();
builder.Services.AddHostedService(services => services.GetRequiredService<DefaultShellWarmup>());

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
builder.Services.AddDynamicEndpointApiExplorerRefresh();

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
            typeof(ApiCapabilitiesFeature).Assembly,
            typeof(ExpressionsApiFeature).Assembly,

            // JavaScript expression + activity feature assemblies. Listing them here makes their features
            // discoverable by the runtime feature catalog (so they surface as "available" in the modularity UI)
            // and enablable via shell configuration.
            typeof(Elsa.Expressions.JavaScript.JavaScriptFeature).Assembly,
            typeof(Elsa.Expressions.JavaScript.Jint.JintFeature).Assembly,
            typeof(Elsa.Expressions.JavaScript.Rendering.JavaScriptRenderingFeature).Assembly,
            typeof(Elsa.Http.JavaScript.HttpJavaScriptFeature).Assembly,
            typeof(Elsa.Workflows.Design.JavaScript.JavaScriptWorkflowsDesignFeature).Assembly,
            typeof(Elsa.Workflows.Runtime.JavaScript.JavaScriptActivitiesFeature).Assembly,

            typeof(SqliteGroundworkUnifiedPersistenceShellFeature).Assembly,
            typeof(PostgreSqlGroundworkUnifiedPersistenceShellFeature).Assembly,
            typeof(WorkflowsDesignApiFeature).Assembly,
            typeof(ActivitiesDesignApiFeature).Assembly,

            // Construction seam (Runtime side): the dispatch factory and stable CLR/graph consumers.
            typeof(ActivitiesRuntimeFeature).Assembly,
            typeof(ActivitiesPrimitivesFeature).Assembly,
            typeof(ActivitiesSequenceFeature).Assembly,
            typeof(ActivitiesFlowchartFeature).Assembly,
            // Design-side graph provider. Kept separate from Graph Runtime so authoring-only and runtime-only
            // custom hosts can compose exactly the plane they need.
            typeof(GraphActivitiesDesignFeature).Assembly,
            typeof(GraphActivitiesRuntimeFeature).Assembly,
            // HTTP endpoint authoring + serving. ActivitiesHttp mounts the inbound middleware and depends on
            // WorkflowsRuntimeHttp, whose route-table projection keeps published and waiting endpoints reachable.
            // Both assemblies are explicit because the dependency is feature-name based; it cannot make an assembly
            // absent from a clean host deployment discoverable.
            typeof(ActivitiesHttpFeature).Assembly,
            typeof(WorkflowsRuntimeHttpFeature).Assembly,
            // Reconciliation (Design side): the universal pass + the CLR assembly scanner source, which
            // publish source-owned CLR activity definitions through the stable provider/runtime seam.
            typeof(ActivitiesDesignReconciliationFeature).Assembly,
            typeof(ClrActivityReconciliationFeature).Assembly,
            // Reconciliation (Design side, workflows): the workflow-version pass + the JSON file source, so
            // shells can deploy mounted workflow-definition files at startup (spec 147, #1157). Opt-in — not
            // enabled in any default shell because the feature requires a SourceId and a file/folder path.
            typeof(WorkflowsDesignReconciliationFeature).Assembly,
            typeof(JsonWorkflowReconciliationFeature).Assembly,

            // The bridge: publishing endpoints that construct a live activity from a catalog row.
            typeof(WorkflowsPublishingApiFeature).Assembly,
            typeof(PublishingGroundworkFeature).Assembly,

            // Runtime vertical slice: execute published WorkflowExecutable artifacts.
            typeof(WorkflowsRuntimeApiFeature).Assembly,

            // Durable-resumption pump. Every Groundwork persistence provider DependsOn this feature, so
            // its assembly must be in the catalog for CShells to auto-enable it when a durable store is
            // composed; without it the shell fails to activate with a FeatureNotFoundException.
            typeof(WorkflowsRuntimeResumptionFeature).Assembly,

            // Reference GC pump (ADR 0040). Opt-in like resumption; its assembly is in the catalog so the feature can
            // be enabled to periodically prune expired/retired references and the artifacts no live reference points at.
            typeof(WorkflowsRuntimeReferenceGarbageCollectionFeature).Assembly,

            // Agent surface: provider-neutral endpoints, workflow context/proposals, and provider facade.
            typeof(FoundationAgentAbstractionsFeature).Assembly,
            typeof(FoundationAgentApiFeature).Assembly,
            typeof(FoundationWorkflowsAgentFeature).Assembly,
            typeof(GitHubCopilotAgentFeature).Assembly,

            // Identity surface. The authentication stack secures the API: FoundationIdentityAbstractions
            // (provider-agnostic auth/IAM contracts) plus the OIDC authentication provider module, which
            // registers the external JWT bearer scheme, and — now that Workstream D is landed — the
            // first-party token stack: the identity API endpoints (login/session/token exchange), the
            // ASP.NET Core Identity substrate (cookie sign-in, Groundwork stores, dev seeding), and the OpenIddict
            // token service (JWT issuance + local bearer validation). Together their composite scheme
            // selector becomes the default authenticate/challenge scheme, so an unauthenticated call is
            // rejected with 401. All of these are enabled in the default shell (see shells.json) with
            // IsDevelopmentOrDemo set for local dev (in-memory stores, ephemeral keys, seeded admin).
            // W18 note (resolved): the earlier guard kept the token-issuance endpoints out of the default
            // shell because enabling them without an ITokenService would fault endpoint registration. The
            // OpenIddict module now supplies that service, so the fault condition no longer exists and the
            // features are enabled.
            typeof(FoundationIdentityAbstractionsFeature).Assembly,
            typeof(FoundationIdentityApiFeature).Assembly,
            typeof(OidcAuthenticationFeature).Assembly,
            typeof(AspNetCoreIdentityFeature).Assembly,

            // The Groundwork-backed ASP.NET Core Identity substrate (durable stores, SignInManager cookie
            // sign-in, login endpoints/page, dev seeding), enabled in the default shell via shells.json.
            typeof(IdentityGroundworkPersistenceFeature).Assembly,
            typeof(AspNetCoreIdentityGroundworkFeature).Assembly,

            typeof(OpenIddictIdentityFeature).Assembly,
            typeof(AttentionApiFeature).Assembly,
            typeof(StudioPreferencesFeature).Assembly,
            typeof(StudioPreferencesApiFeature).Assembly,
            typeof(StudioPreferencesGroundworkPersistenceFeature).Assembly,
            typeof(WorkflowsDashboardFeature).Assembly,
            // WorkflowsDashboard DependsOn WorkflowDesignValidations — its assembly must be in the catalog or the
            // dependency resolver fails shell activation with FeatureNotFoundException.
            typeof(Elsa.Workflows.Design.Validations.WorkflowDesignValidationsFeature).Assembly,

            typeof(ModularityApiFeature).Assembly,
            typeof(ModularityAttentionFeature).Assembly,
            typeof(SecretsAttentionFeature).Assembly,
            typeof(WorkflowsRuntimeAttentionFeature).Assembly,
            typeof(StructuredLogsFeature).Assembly,
            typeof(DiagnosticsGroundworkPersistenceFeature).Assembly,
            typeof(OpenTelemetryFeature).Assembly,

            // Engine self-instrumentation (MS-9): puts the WorkflowsRuntimeTracing feature in the catalog so it can be
            // enabled via shells.json, replacing the no-op tracer with the ActivitySource-backed one. The host-local
            // OpenTelemetryEngineTracingBridge feature (below, in WithHostAssemblies) subscribes that source and forwards
            // the spans into the OpenTelemetry ingestion store so Studio's timing view is populated.
            typeof(WorkflowsRuntimeTracingFeature).Assembly,

            // Test-host execution evidence: puts the WorkflowsExecutionEvidence feature in the catalog so an automated
            // test suite can query what a workflow actually did over HTTP. Process-local and non-durable by design.
            typeof(WorkflowsExecutionEvidenceFeature).Assembly
        )

        .WithConfigurationProvider(configuration)
        .WithWebRouting(options =>
        {
            options.EnablePathRouting = true;
            options.ExcludePaths = ["/health/live", "/health/ready"];
        })
        .ConfigureAllShells(shell => shell
            .WithFeature<ModularityApiFeature>()
            // Binding an absent section is a no-op, so the feature's opt-in default stands unless an
            // operator sets Elsa:Workflows:Runtime:FaultCapture:CaptureStackTrace.
            .WithFeature<RuntimeFaultStackTraceFeature>(feature =>
                configuration.GetSection(RuntimeFaultCaptureOptions.SectionName).Bind(feature)));
});

// Opt-in eager shell activation (spec 132, First-Request/Cold-Start Readiness unit 4). Default OFF. When
// Elsa:Boot:EagerShellActivation:Enabled is set, a host-level IHostedService activates the configured shell(s)
// at boot through the same IShellRegistry.GetOrActivateAsync path the first request would (byte-identical shell
// state, just earlier), so the activation cliff — and the mid-activation contention tail — is paid before the
// first user request instead of during it. The trigger lives at the host level because CShells does not run
// shell-scoped hosted services and the eager trigger must sit outside any shell (it activates the shells).
// Registered only when the switch is on, so an unset host constructs nothing and pays nothing.
if (EagerShellActivationOptions.IsEnabled(configuration))
    builder.Services.AddHostedService<EagerShellActivationHostedService>();

// Root authentication/authorization services. Registered after AddCShellsAspNetCore so the shell
// delegating scheme/policy providers (WithAuthenticationAndAuthorization) stay in place — both
// AddAuthentication and AddAuthorization use TryAdd semantics for those services.
builder.Services.AddAuthentication();
builder.Services.AddAuthorization();

var app = builder.Build();

if (bootTimeline is not null)
{
    // Phase A: everything from process entry to a built host — feature catalog discovery, Nuplane package-ALC
    // loads and DI container construction.
    bootTimeline.Measure("host-build", 0d, "CreateBuilder → Build (feature catalog + package ALC + DI)");
    var hostBuiltMs = bootTimeline.ElapsedMs;

    // Phase B: Kestrel accepting connections. Shell activation is still lazy at this point.
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        bootTimeline.Measure("kestrel-startup", hostBuiltMs, "Build → listening");
        bootTimeline.Mark("kestrel-ready");
    });

    // Phase B/C: observe the lazy shell-activation cliff (Initializing→Active wall) via the one host-observable
    // CShells seam. Per-initializer attribution is not host-observable — see BootShellActivationObserver.
    app.Services.GetService<IShellRegistry>()?.Subscribe(new BootShellActivationObserver(bootTimeline));

    // Time the first request end-to-end (it triggers activation) and print the phase table when it completes.
    app.UseMiddleware<BootFirstRequestMiddleware>();
}

app.UseCors(studioCorsPolicy);

app.MapGet("/", () => Results.Ok(new { status = "Healthy", service = "elsa-workbench" }))
    .WithHostOwner("Elsa.Workbench")
    .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
    .AllowPublic("health", "Reports whether the Workbench root host is responding.");
app.MapShellReadiness();
app.MapElsaModuleManagementApi();
if (extensionBuilderEnabled)
    app.MapElsaExtensionBuilderApi();
app.MapShells();

// Explicit auth middleware placed after MapShells: ShellMiddleware (added by MapShells) swaps
// HttpContext.RequestServices to the shell scope first, so authentication/authorization resolve
// each shell's schemes and permission policies. Explicit calls also suppress WebApplication's
// automatic insertion of these middleware earlier in the pipeline.
app.UseAuthentication();
app.UseAuthorization();

// ADR 0037: CShells host-control remains server-to-server management-key traffic. It is not a Foundation
// user permission and the key is never exposed to browser clients.
app.MapShellManagementApi("/_admin/shells")
    .WithHostOwner("Elsa.Workbench")
    .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
    .WithSecurityDisposition(EndpointSecurityDispositionMetadata.HostCredential(
        ManagementApiKeyAuthentication.HeaderName,
        "Elsa.Workbench"))
    .WithHostCredentialEnforcement(ManagementApiKeyAuthentication.HeaderName, "Elsa.Workbench")
    .AddEndpointFilter(ManagementApiKeyAuthentication.RequireAsync);

// Root-hosted console log streaming: recent/sources HTTP endpoints + the live SignalR hub (see the registration
// note above). Mapped after UseCors so the Studio cross-origin policy applies, and behind RequireAuthorization so
// the captured console output is not readable anonymously — these are root-mapped endpoints that bypass the
// per-shell endpoint pipeline, so they must carry their own authorization. The empty-prefix group keeps the absolute
// routes and applies the convention to every endpoint the mapper adds, including the hub.
if (consoleLogStreamingEnabled)
{
    var consoleLogEndpoints = app.MapGroup("");
    consoleLogEndpoints.RequireAuthorization();
    consoleLogEndpoints.WithHostOwner("Elsa.Workbench")
        .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
        .WithSecurityDisposition(EndpointSecurityDispositionMetadata.NamedPolicy("Default", "Elsa.Workbench"));
    consoleLogEndpoints.MapConsoleLogStreaming();
}
app.Run();

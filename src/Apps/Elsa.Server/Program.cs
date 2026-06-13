using CShells.AspNetCore.Configuration;
using CShells.AspNetCore.Extensions;
using CShells.DependencyInjection;
using CShells.Management.Api;
using ConsoleLogStreaming.AspNetCore.DependencyInjection;
using ConsoleLogStreaming.Core.Capture;
using ConsoleLogStreaming.Core.DependencyInjection;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Server;
using Elsa.Admin.Api;
using Elsa.Admin.Api.Extensions;
using Elsa.Admin.Samples.Dashboard;
using Elsa.Admin.Samples.WeatherForecast;
using Elsa.Admin.Samples.WeatherForecast.Extensions;
using Elsa.Admin.Web;
using Elsa.Admin.Web.Extensions;
using Elsa.Activities.Composition.Runtime;
using Elsa.Activities.Design.Api;
using Elsa.Activities.Design.Persistence.EFCore.Sqlite;
using Elsa.Activities.Design.Reconciliation;
using Elsa.Activities.Design.Reconciliation.Clr;
using Elsa.Activities.Primitives;
using Elsa.Activities.Runtime;
using Elsa.Activities.Sequence;
using Elsa.Caching.Memory;
using Elsa.Events;
using Elsa.Expressions;
using Elsa.Locking.FileSystem;
using Elsa.Mediator;
using Elsa.Primitives.Hosting;
using Elsa.Serialization.Newtonsoft;
using Elsa.Serialization.SystemText;
using Elsa.Tasks;
using Elsa.Workflows.Design.Api;
using Elsa.Workflows.Design.Persistence.EFCore.Sqlite;
using Elsa.Workflows.Publishing.Api;
using Elsa.Workflows.Runtime.Api;
using Nuplane;
using Nuplane.Admin;
using Nuplane.Loading.Hosting.Builder;
using Nuplane.Sources.Directory.Configuration;

ConsoleStreamHook.Install();

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets();
builder.Configuration.AddJsonFile("shells.json", optional: true, reloadOnChange: true);
var configuration = builder.Configuration;
var nuplaneConfiguration = configuration.GetSection("Nuplane");

EndpointSecurityOptions.DisableSecurity();

builder.Services.AddConsoleLogStreamingHost(options =>
{
    options.ServiceName = "elsa-server";
    options.SourceDisplayName = "Elsa.Server";
    options.RecentCapacity = 2_000;
    options.MaxRecentQuerySize = 2_000;
    options.PreserveAnsi = true;
});
builder.Services.AddConsoleLogStreamingAspNetCore(options =>
{
    options.RecentPath = "/diagnostics/console-logs/recent";
    options.SourcesPath = "/diagnostics/console-logs/sources";
    options.HubPath = "/diagnostics/console-logs/hub";
});
builder.Services.AddNuplaneAdmin();
builder.Services.AddSingleton<DemoPackageEventStore>();
builder.Services.AddSingleton<DemoNuplaneObserver>();

builder.Services.AddNuplane(nuplaneConfiguration, nuplane =>
{
    nuplane.AddDirectoryFeedsFromConfiguration(nuplaneConfiguration);
    nuplane.AutoloadPackages(nuplaneConfiguration.GetSection("Loading"));
    nuplane.OnPackagesChanged<DemoNuplaneObserver>();
});
builder.Services.AddSingleton<NuplaneAssemblyProvider>();

new EventsFeature().ConfigureServices(builder.Services);
new AdminApiFeature().ConfigureServices(builder.Services);
new DashboardAdminSampleFeature().ConfigureServices(builder.Services);
new WeatherForecastAdminSampleFeature().ConfigureServices(builder.Services);

builder.Services.AddCShellsAspNetCore(shells =>
{
    shells
        .WithHostAssemblies()
        .WithAssemblyProvider<NuplaneAssemblyProvider>()

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
            typeof(AdminApiFeature).Assembly,
            typeof(AdminWebFeature).Assembly,
            typeof(DashboardAdminSampleFeature).Assembly,
            typeof(WeatherForecastAdminSampleFeature).Assembly,
            typeof(SqliteWorkflowsDesignPersistenceShellFeature).Assembly,
            typeof(WorkflowsDesignApiFeature).Assembly,
            typeof(SqliteActivitiesDesignPersistenceShellFeature).Assembly,
            typeof(ActivitiesDesignApiFeature).Assembly,

            // Construction seam (Runtime side): the dispatch factory + registry, the CLR kind, and the
            // Workflow kind. These populate the constructor registry the bridge dispatches through.
            typeof(ActivitiesRuntimeFeature).Assembly,
            typeof(ActivitiesPrimitivesFeature).Assembly,
            typeof(ActivitiesSequenceFeature).Assembly,
            typeof(ActivitiesCompositionRuntimeFeature).Assembly,

            // Reconciliation (Design side): the universal pass + the CLR assembly scanner source, which
            // populate the catalog with WriteLine + WorkflowDefinitionActivity as CLR rows at startup.
            typeof(ActivitiesDesignReconciliationFeature).Assembly,
            typeof(ClrActivityReconciliationFeature).Assembly,

            // The bridge: publishing endpoints that construct a live activity from a catalog row.
            typeof(WorkflowsPublishingApiFeature).Assembly,

            // Runtime vertical slice: execute published WorkflowExecutable artifacts.
            typeof(WorkflowsRuntimeApiFeature).Assembly
        )

        .WithConfigurationProvider(configuration)
        .WithWebRouting(options =>
        {
            options.EnablePathRouting = true;
        });
});

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapConsoleLogStreaming();
app.MapElsaDemoApi();
app.MapShells();
app.MapShellManagementApi("/_admin/shells");
app.MapElsaAdminApi();
app.MapWeatherForecastAdminSample();
app.MapElsaAdminWeb();
app.MapFallbackToFile("/demo", "index.html");
app.MapFallbackToFile("/demo/{*path:nonfile}", "index.html");
app.MapFallbackToFile("index.html");
app.Run();

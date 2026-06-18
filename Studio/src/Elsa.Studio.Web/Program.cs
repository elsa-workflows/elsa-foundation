using CShells.AspNetCore.Configuration;
using CShells.AspNetCore.Extensions;
using CShells.DependencyInjection;
using Elsa.Studio.Api.Features;
using Elsa.Studio.ConsoleStream;
using Elsa.Studio.FeatureManagement;
using Elsa.Studio.Workflows;
using Elsa.Studio.Samples.Dashboard;
using Elsa.Studio.Web;
using Nuplane;
using Nuplane.Admin;
using Nuplane.Loading.Hosting.Builder;
using Nuplane.Sources.Directory.Configuration;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("shells.json", optional: false, reloadOnChange: true);
builder.Configuration.AddJsonFile("nuplane-management.json", optional: true, reloadOnChange: true);
var configuration = builder.Configuration;
var nuplaneConfiguration = configuration.GetSection("Nuplane");

builder.WebHost.UseStaticWebAssets();

builder.Services.AddSingleton<IConfigurationRoot>(configuration);
builder.Services.AddNuplaneAdmin();
builder.Services.AddNuplane(nuplaneConfiguration, nuplane =>
{
    nuplane.AddDirectoryFeedsFromConfiguration(nuplaneConfiguration);
    nuplane.AutoloadPackages(nuplaneConfiguration.GetSection("Loading"));
    nuplane.OnPackagesChanged<StudioNuplaneShellReloadObserver>();
});
builder.Services.AddSingleton<StudioRuntimeFeatureCatalogRefresher>();
builder.Services.AddSingleton<StudioShellFeatureConfigurationStore>();
builder.Services.AddSingleton<StudioNuplaneAssemblyProvider>();

builder.Services.AddCShellsAspNetCore(shells =>
{
    shells
        .WithAssemblyProvider<StudioNuplaneAssemblyProvider>()
        .WithAssemblies(
            typeof(StudioApiFeature).Assembly,
            typeof(ConsoleStreamStudioFeature).Assembly,
            typeof(FeatureManagementStudioFeature).Assembly,
            typeof(WorkflowsStudioFeature).Assembly,
            typeof(DashboardStudioFeature).Assembly)
        .WithConfigurationProvider(configuration)
        .WithWebRouting(options =>
        {
            options.EnablePathRouting = true;
        });
});

var app = builder.Build();

app.MapGet("/studio-runtime.js", () =>
{
    var runtimeConfig = new
    {
        backendBaseUrl = configuration["Studio:BackendBaseUrl"] ?? string.Empty,
        backendModuleManagementApiKey = configuration["Studio:BackendModuleManagementApiKey"] ?? string.Empty
    };

    return Results.Content(
        $"window.__ELSA_STUDIO_RUNTIME__ = {JsonSerializer.Serialize(runtimeConfig)};",
        "application/javascript");
});

app.UseStaticFiles();

app.MapElsaModuleManagementApi();
app.MapElsaFeatureManagementApi();
app.MapShells();
app.MapActiveShellStudioApi();
app.MapNuplaneStaticWebAssets();
app.MapFallbackToFile("studio/index.html");

app.Run();

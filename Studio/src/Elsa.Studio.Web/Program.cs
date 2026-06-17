using CShells.AspNetCore.Configuration;
using CShells.AspNetCore.Extensions;
using CShells.DependencyInjection;
using Elsa.Studio.Api.Features;
using Elsa.Studio.ConsoleStream;
using Elsa.Studio.FeatureManagement;
using Elsa.Studio.Samples.Dashboard;
using Elsa.Studio.Samples.WeatherForecast;
using Elsa.Studio.Web;
using Nuplane;
using Nuplane.Admin;
using Nuplane.Loading.Hosting.Builder;
using Nuplane.Sources.Directory.Configuration;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("nuplane-management.json", optional: true, reloadOnChange: true);
var configuration = builder.Configuration;
var nuplaneConfiguration = configuration.GetSection("Nuplane");

builder.WebHost.UseStaticWebAssets();

builder.Services.AddNuplaneAdmin();
builder.Services.AddNuplane(nuplaneConfiguration, nuplane =>
{
    nuplane.AddDirectoryFeedsFromConfiguration(nuplaneConfiguration);
    nuplane.AutoloadPackages(nuplaneConfiguration.GetSection("Loading"));
});
builder.Services.AddSingleton<StudioNuplaneAssemblyProvider>();

builder.Services.AddCShellsAspNetCore(shells =>
{
    shells
        .WithAssemblyProvider<StudioNuplaneAssemblyProvider>()
        .WithAssemblies(
            typeof(StudioApiFeature).Assembly,
            typeof(ConsoleStreamStudioFeature).Assembly,
            typeof(FeatureManagementStudioFeature).Assembly,
            typeof(DashboardStudioFeature).Assembly,
            typeof(WeatherForecastStudioFeature).Assembly)
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
        backendBaseUrl = configuration["Studio:BackendBaseUrl"] ?? string.Empty
    };

    return Results.Content(
        $"window.__ELSA_STUDIO_RUNTIME__ = {JsonSerializer.Serialize(runtimeConfig)};",
        "application/javascript");
});

app.UseStaticFiles();

app.MapElsaModuleManagementApi();
app.MapElsaFeatureManagementApi();
app.MapShells();
app.MapFallbackToFile("studio/index.html");

app.Run();

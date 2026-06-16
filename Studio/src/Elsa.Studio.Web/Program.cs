using CShells.AspNetCore.Configuration;
using CShells.AspNetCore.Extensions;
using CShells.DependencyInjection;
using Elsa.Studio.Api.Features;
using Elsa.Studio.ConsoleStream;
using Elsa.Studio.FeatureManagement;
using Elsa.Studio.Samples.Dashboard;
using Elsa.Studio.Samples.WeatherForecast;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

builder.WebHost.UseStaticWebAssets();

builder.Services.AddCShellsAspNetCore(shells =>
{
    shells
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

app.MapShells();
app.MapFallbackToFile("studio/index.html");

app.Run();

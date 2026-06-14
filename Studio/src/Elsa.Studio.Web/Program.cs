using CShells.AspNetCore.Configuration;
using CShells.AspNetCore.Extensions;
using CShells.DependencyInjection;
using Elsa.Studio.Api.Features;
using Elsa.Studio.ConsoleStream;
using Elsa.Studio.Samples.Dashboard;
using Elsa.Studio.Samples.WeatherForecast;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

builder.WebHost.UseStaticWebAssets();

builder.Services.AddCShellsAspNetCore(shells =>
{
    shells
        .WithAssemblies(
            typeof(StudioApiFeature).Assembly,
            typeof(ConsoleStreamStudioFeature).Assembly,
            typeof(DashboardStudioFeature).Assembly,
            typeof(WeatherForecastStudioFeature).Assembly)
        .WithConfigurationProvider(configuration)
        .WithWebRouting(options =>
        {
            options.EnablePathRouting = true;
        });
});

var app = builder.Build();

app.UseStaticFiles();

app.MapShells();
app.MapFallbackToFile("studio/index.html");

app.Run();

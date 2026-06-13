using Elsa.Admin.Api;
using Elsa.Admin.Api.Contracts;
using Elsa.Admin.Core.Models;
using Elsa.Admin.Samples.Dashboard;
using Elsa.Admin.Samples.WeatherForecast;
using Elsa.Events.Core.Contracts;
using Elsa.Events.Services;
using Elsa.Events.Strategies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Admin.Tests;

public sealed class AdminModuleManifestProviderTests
{
    [Fact]
    public async Task Provider_returns_dashboard_and_weather_manifests()
    {
        await using var provider = BuildProvider();

        var response = await provider.GetRequiredService<IAdminModuleManifestProvider>().GetModules(CancellationToken.None);

        Assert.Equal("1.0.0", response.HostVersion);
        Assert.Equal("1.0.0", response.SdkVersion);
        Assert.Contains(response.Modules, x => x.Id == "Elsa.Admin.Samples.Dashboard");
        Assert.Contains(response.Modules, x => x.Id == "Elsa.Admin.Samples.WeatherForecast");
        Assert.All(response.Modules, x => Assert.StartsWith("/_content/", x.Entry));
    }

    [Fact]
    public async Task Provider_reports_disabled_module_and_excludes_it_from_active_modules()
    {
        var adminFeature = new AdminApiFeature();
        adminFeature.Options.DisabledModuleIds.Add("Elsa.Admin.Samples.WeatherForecast");

        await using var provider = BuildProvider(adminFeature);

        var response = await provider.GetRequiredService<IAdminModuleManifestProvider>().GetModules(CancellationToken.None);

        Assert.DoesNotContain(response.Modules, x => x.Id == "Elsa.Admin.Samples.WeatherForecast");
        Assert.Contains(response.Diagnostics, x =>
            x.ModuleId == "Elsa.Admin.Samples.WeatherForecast" &&
            x.Status == AdminModuleDiagnosticStatuses.Disabled);
    }

    private static ServiceProvider BuildProvider(AdminApiFeature? adminApiFeature = null)
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddScoped<IEventPublisher, EventPublisher>();
        services.AddSingleton<IEventPipeline, EventPipeline>();
        services.AddSingleton<IEventPublishingStrategy>(EventPublishingStrategy.Sequential);

        (adminApiFeature ?? new AdminApiFeature()).ConfigureServices(services);
        new DashboardAdminSampleFeature().ConfigureServices(services);
        new WeatherForecastAdminSampleFeature().ConfigureServices(services);

        return services.BuildServiceProvider();
    }
}

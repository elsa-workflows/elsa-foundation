using Elsa.Studio.Api.Contracts;
using Elsa.Studio.Api.Extensions;
using Elsa.Studio.Api.Options;
using Elsa.Studio.ConsoleStream;
using Elsa.Studio.Core.Models;
using Elsa.Studio.Samples.Dashboard;
using Elsa.Studio.Samples.WeatherForecast;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elsa.Studio.Tests;

public sealed class StudioModuleManifestProviderTests
{
    [Fact]
    public async Task GetModules_ReturnsHostSdkVersionsAndCollectedModuleManifests()
    {
        var provider = CreateProvider();

        var response = await provider.GetRequiredService<IStudioModuleManifestProvider>().GetModules(CancellationToken.None);

        Assert.Equal("1.0.0", response.HostVersion);
        Assert.Equal("1.0.0", response.SdkVersion);
        Assert.Contains(response.Modules, x => x.Id == "Elsa.Studio.ConsoleStream");
        Assert.Contains(response.Modules, x => x.Id == "Elsa.Studio.Samples.Dashboard");
        Assert.Contains(response.Modules, x => x.Id == "Elsa.Studio.Samples.WeatherForecast");
        Assert.Contains(response.Diagnostics, x => x.Status == StudioModuleDiagnosticStatuses.Available);
    }

    [Fact]
    public async Task GetModules_FiltersDisabledModulesAndReportsDiagnostic()
    {
        var provider = CreateProvider(options => options.DisabledModuleIds.Add("Elsa.Studio.Samples.WeatherForecast"));

        var response = await provider.GetRequiredService<IStudioModuleManifestProvider>().GetModules(CancellationToken.None);

        Assert.DoesNotContain(response.Modules, x => x.Id == "Elsa.Studio.Samples.WeatherForecast");
        Assert.Contains(response.Diagnostics, x =>
            x.ModuleId == "Elsa.Studio.Samples.WeatherForecast" &&
            x.Status == StudioModuleDiagnosticStatuses.Disabled);
    }

    [Fact]
    public async Task GetModules_FiltersConsoleStreamModuleAndReportsDiagnostic()
    {
        using var provider = CreateProvider(options => options.DisabledModuleIds.Add("Elsa.Studio.ConsoleStream"));

        var response = await provider.GetRequiredService<IStudioModuleManifestProvider>().GetModules(CancellationToken.None);

        Assert.DoesNotContain(response.Modules, x => x.Id == "Elsa.Studio.ConsoleStream");
        Assert.Contains(response.Diagnostics, x =>
            x.ModuleId == "Elsa.Studio.ConsoleStream" &&
            x.Status == StudioModuleDiagnosticStatuses.Disabled);
    }

    [Fact]
    public async Task GetModules_CanCollectMultipleContributors()
    {
        var provider = CreateProvider();

        var response = await provider.GetRequiredService<IStudioModuleManifestProvider>().GetModules(CancellationToken.None);

        Assert.True(response.Modules.Count >= 2);
    }

    private static ServiceProvider CreateProvider(Action<StudioApiOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddElsaStudioApi();
        services.AddConsoleStreamStudio();
        services.AddDashboardStudioSample();
        services.AddWeatherForecastStudioSample();

        if (configure is not null)
            services.PostConfigure(configure);

        return services.BuildServiceProvider();
    }
}

using Elsa.Studio.Api.Contracts;
using Elsa.Studio.Api.Extensions;
using Elsa.Studio.Api.Options;
using Elsa.Studio.ConsoleStream;
using Elsa.Studio.Core.Events;
using Elsa.Studio.Core.Models;
using Elsa.Studio.Core.Services;
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
        using var provider = CreateProvider();

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
        using var provider = CreateProvider(options => options.DisabledModuleIds.Add("Elsa.Studio.Samples.WeatherForecast"));

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
        using var provider = CreateProvider();

        var response = await provider.GetRequiredService<IStudioModuleManifestProvider>().GetModules(CancellationToken.None);

        Assert.True(response.Modules.Count >= 2);
    }

    [Fact]
    public async Task GetModules_FiltersModulesWhenBackendCapabilitiesAreMissing()
    {
        using var provider = CreateProvider(services =>
            services.AddStudioEventHandler<OnStudioModuleManifestsCollecting, ContributeMissingBackendModule>());

        var response = await provider.GetRequiredService<IStudioModuleManifestProvider>().GetModules(CancellationToken.None);

        Assert.DoesNotContain(response.Modules, x => x.Id == MissingBackendModuleId);
        Assert.Contains(response.Diagnostics, x =>
            x.ModuleId == MissingBackendModuleId &&
            x.Status == StudioModuleDiagnosticStatuses.MissingBackendCapability &&
            x.Reason.Contains(MissingBackendCapability, StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetModules_ReturnsModulesWhenConfiguredBackendCapabilitiesSatisfyRequirements()
    {
        using var provider = CreateProvider(
            services => services.AddStudioEventHandler<OnStudioModuleManifestsCollecting, ContributeMissingBackendModule>(),
            options => options.BackendCapabilityIds.Add(MissingBackendCapability));

        var response = await provider.GetRequiredService<IStudioModuleManifestProvider>().GetModules(CancellationToken.None);

        Assert.Contains(response.Modules, x => x.Id == MissingBackendModuleId);
        Assert.Contains(response.Diagnostics, x =>
            x.ModuleId == MissingBackendModuleId &&
            x.Status == StudioModuleDiagnosticStatuses.Available);
    }

    [Fact]
    public async Task GetModules_ModuleOverrideDisabledWinsEvenWhenBackendCapabilitiesArePresent()
    {
        using var provider = CreateProvider(options =>
        {
            options.BackendCapabilityIds.Add(MissingBackendCapability);
            options.Modules[MissingBackendModuleId] = "false";
        }, services => services.AddStudioEventHandler<OnStudioModuleManifestsCollecting, ContributeMissingBackendModule>());

        var response = await provider.GetRequiredService<IStudioModuleManifestProvider>().GetModules(CancellationToken.None);

        Assert.DoesNotContain(response.Modules, x => x.Id == MissingBackendModuleId);
        Assert.Contains(response.Diagnostics, x =>
            x.ModuleId == MissingBackendModuleId &&
            x.Status == StudioModuleDiagnosticStatuses.Disabled);
    }

    [Fact]
    public async Task GetModules_ModuleOverrideEnabledWinsOverLegacyDisabledModuleIds()
    {
        using var provider = CreateProvider(options =>
        {
            options.BackendCapabilityIds.Add(MissingBackendCapability);
            options.DisabledModuleIds.Add(MissingBackendModuleId);
            options.Modules[MissingBackendModuleId] = "true";
        }, services => services.AddStudioEventHandler<OnStudioModuleManifestsCollecting, ContributeMissingBackendModule>());

        var response = await provider.GetRequiredService<IStudioModuleManifestProvider>().GetModules(CancellationToken.None);

        Assert.Contains(response.Modules, x => x.Id == MissingBackendModuleId);
        Assert.Contains(response.Diagnostics, x =>
            x.ModuleId == MissingBackendModuleId &&
            x.Status == StudioModuleDiagnosticStatuses.Available);
    }

    [Fact]
    public async Task GetModules_ForcedModuleWithMissingBackendCapabilitiesReportsIncompatible()
    {
        using var provider = CreateProvider(options => options.Modules[MissingBackendModuleId] = "true", services =>
            services.AddStudioEventHandler<OnStudioModuleManifestsCollecting, ContributeMissingBackendModule>());

        var response = await provider.GetRequiredService<IStudioModuleManifestProvider>().GetModules(CancellationToken.None);

        Assert.DoesNotContain(response.Modules, x => x.Id == MissingBackendModuleId);
        Assert.Contains(response.Diagnostics, x =>
            x.ModuleId == MissingBackendModuleId &&
            x.Status == StudioModuleDiagnosticStatuses.Incompatible &&
            x.Reason.Contains(MissingBackendCapability, StringComparison.Ordinal));
    }

    private const string MissingBackendModuleId = "Elsa.Studio.Tests.MissingBackend";
    private const string MissingBackendCapability = "elsa.tests.missing";

    private static ServiceProvider CreateProvider(Action<StudioApiOptions>? configure = null, Action<IServiceCollection>? configureServices = null)
    {
        return CreateProvider(configureServices, configure);
    }

    private static ServiceProvider CreateProvider(Action<IServiceCollection>? configureServices, Action<StudioApiOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddElsaStudioApi();
        services.AddConsoleStreamStudio();
        services.AddDashboardStudioSample();
        services.AddWeatherForecastStudioSample();
        configureServices?.Invoke(services);

        if (configure is not null)
            services.PostConfigure(configure);

        return services.BuildServiceProvider();
    }
    private sealed class ContributeMissingBackendModule : IStudioEventHandler<OnStudioModuleManifestsCollecting>
    {
        public Task Handle(OnStudioModuleManifestsCollecting @event, CancellationToken cancellationToken)
        {
            @event.Manifests.Add(new StudioModuleManifest(
                MissingBackendModuleId,
                "Missing backend",
                "1.0.0",
                "/missing/module.js",
                [],
                "^1.0.0",
                "^1.0.0",
                ["tests"],
                [MissingBackendCapability]));

            return Task.CompletedTask;
        }
    }
}

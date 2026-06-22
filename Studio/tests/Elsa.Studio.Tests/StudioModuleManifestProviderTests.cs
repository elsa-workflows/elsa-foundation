using CShells;
using Elsa.Studio.Api.Contracts;
using Elsa.Studio.Api.Extensions;
using Elsa.Studio.Api.Options;
using Elsa.Studio.ConsoleStream;
using Elsa.Studio.Core.Models;
using Elsa.Studio.Diagnostics.OpenTelemetry;
using Elsa.Studio.Diagnostics.StructuredLogs;
using Elsa.Studio.FeatureManagement;
using Elsa.Studio.Samples.Dashboard;
using Elsa.Studio.Samples.WeatherForecast;
using Elsa.Studio.Workflows;
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
        Assert.Contains(response.Modules, x => x.Id == "Elsa.Studio.Diagnostics.OpenTelemetry");
        Assert.Contains(response.Modules, x => x.Id == "Elsa.Studio.Diagnostics.StructuredLogs");
        Assert.Contains(response.Modules, x => x.Id == "Elsa.Studio.FeatureManagement");
        Assert.Contains(response.Modules, x => x.Id == "Elsa.Studio.Workflows");
        Assert.Contains(response.Modules, x => x.Id == "Elsa.Studio.Samples.Dashboard");
        Assert.Contains(response.Modules, x => x.Id == "Elsa.Studio.Samples.WeatherForecast");
        Assert.Contains(response.Diagnostics, x => x.Status == StudioModuleDiagnosticStatuses.Available);
    }

    [Fact]
    public async Task GetModules_ReturnsWorkflowsManifestAssetsAndCapabilities()
    {
        var provider = CreateProvider();

        var response = await provider.GetRequiredService<IStudioModuleManifestProvider>().GetModules(CancellationToken.None);

        var module = Assert.Single(response.Modules, x => x.Id == "Elsa.Studio.Workflows");
        Assert.Equal("Workflows", module.DisplayName);
        Assert.StartsWith("/_content/Elsa.Studio.Workflows/studio/modules/workflows/module.js", module.Entry, StringComparison.Ordinal);
        Assert.Contains(module.Styles, x => x.StartsWith("/_content/Elsa.Studio.Workflows/studio/modules/workflows/module.css", StringComparison.Ordinal));
        Assert.Contains("navigation", module.Capabilities);
        Assert.Contains("routes", module.Capabilities);
        Assert.Contains("workflow-designer", module.Capabilities);
    }

    [Fact]
    public async Task GetModules_ReturnsFeatureManagementManifestAssetsAndCapabilities()
    {
        var provider = CreateProvider();

        var response = await provider.GetRequiredService<IStudioModuleManifestProvider>().GetModules(CancellationToken.None);

        var module = Assert.Single(response.Modules, x => x.Id == "Elsa.Studio.FeatureManagement");
        Assert.Equal("Feature management", module.DisplayName);
        Assert.StartsWith("/_content/Elsa.Studio.FeatureManagement/studio/modules/features/module.js", module.Entry, StringComparison.Ordinal);
        Assert.Contains($"v={module.Version}", module.Entry);
        Assert.Contains(module.Styles, x => x.StartsWith("/_content/Elsa.Studio.FeatureManagement/studio/modules/features/module.css", StringComparison.Ordinal));
        Assert.Contains("navigation", module.Capabilities);
        Assert.Contains("routes", module.Capabilities);
        Assert.Contains("setting-editors", module.Capabilities);
    }

    [Fact]
    public async Task GetModules_ReturnsStructuredLogsManifestAssetsAndCapabilities()
    {
        var provider = CreateProvider();

        var response = await provider.GetRequiredService<IStudioModuleManifestProvider>().GetModules(CancellationToken.None);

        var module = Assert.Single(response.Modules, x => x.Id == "Elsa.Studio.Diagnostics.StructuredLogs");
        Assert.Equal("Structured logs", module.DisplayName);
        Assert.StartsWith("/_content/Elsa.Studio.Diagnostics.StructuredLogs/studio/modules/structured-logs/module.js", module.Entry, StringComparison.Ordinal);
        Assert.Contains($"v={module.Version}", module.Entry);
        Assert.Contains(module.Styles, x => x.StartsWith("/_content/Elsa.Studio.Diagnostics.StructuredLogs/studio/modules/structured-logs/module.css", StringComparison.Ordinal));
        Assert.Contains("navigation", module.Capabilities);
        Assert.Contains("routes", module.Capabilities);
        Assert.Contains("panels", module.Capabilities);
        Assert.Contains("http", module.Capabilities);
        Assert.Contains("sse", module.Capabilities);
        Assert.Contains("diagnostics", module.Capabilities);
    }

    [Fact]
    public async Task GetModules_ReturnsOpenTelemetryManifestAssetsAndCapabilities()
    {
        var provider = CreateProvider();

        var response = await provider.GetRequiredService<IStudioModuleManifestProvider>().GetModules(CancellationToken.None);

        var module = Assert.Single(response.Modules, x => x.Id == "Elsa.Studio.Diagnostics.OpenTelemetry");
        Assert.Equal("OpenTelemetry", module.DisplayName);
        Assert.StartsWith("/_content/Elsa.Studio.Diagnostics.OpenTelemetry/studio/modules/open-telemetry/module.js", module.Entry, StringComparison.Ordinal);
        Assert.Contains($"v={module.Version}", module.Entry);
        Assert.Contains(module.Styles, x => x.StartsWith("/_content/Elsa.Studio.Diagnostics.OpenTelemetry/studio/modules/open-telemetry/module.css", StringComparison.Ordinal));
        Assert.Contains("navigation", module.Capabilities);
        Assert.Contains("routes", module.Capabilities);
        Assert.Contains("http", module.Capabilities);
        Assert.Contains("diagnostics", module.Capabilities);
        Assert.Contains("otel", module.Capabilities);
        Assert.Contains("traces", module.Capabilities);
        Assert.Contains("metrics", module.Capabilities);
        Assert.Contains("logs", module.Capabilities);
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
    public async Task GetModules_FiltersStructuredLogsModuleAndReportsDiagnostic()
    {
        using var provider = CreateProvider(options => options.DisabledModuleIds.Add("Elsa.Studio.Diagnostics.StructuredLogs"));

        var response = await provider.GetRequiredService<IStudioModuleManifestProvider>().GetModules(CancellationToken.None);

        Assert.DoesNotContain(response.Modules, x => x.Id == "Elsa.Studio.Diagnostics.StructuredLogs");
        Assert.Contains(response.Diagnostics, x =>
            x.ModuleId == "Elsa.Studio.Diagnostics.StructuredLogs" &&
            x.Status == StudioModuleDiagnosticStatuses.Disabled);
    }

    [Fact]
    public async Task GetModules_FiltersOpenTelemetryModuleAndReportsDiagnostic()
    {
        using var provider = CreateProvider(options => options.DisabledModuleIds.Add("Elsa.Studio.Diagnostics.OpenTelemetry"));

        var response = await provider.GetRequiredService<IStudioModuleManifestProvider>().GetModules(CancellationToken.None);

        Assert.DoesNotContain(response.Modules, x => x.Id == "Elsa.Studio.Diagnostics.OpenTelemetry");
        Assert.Contains(response.Diagnostics, x =>
            x.ModuleId == "Elsa.Studio.Diagnostics.OpenTelemetry" &&
            x.Status == StudioModuleDiagnosticStatuses.Disabled);
    }

    [Fact]
    public async Task GetModules_FiltersModulesOwnedByInactiveShellFeatures()
    {
        using var provider = CreateProvider(
            shellFeatures:
            [
                "ConsoleStream",
                "FeatureManagement",
                "DashboardSample",
                "StudioApi"
            ]);

        var response = await provider.GetRequiredService<IStudioModuleManifestProvider>().GetModules(CancellationToken.None);

        Assert.DoesNotContain(response.Modules, x => x.Id == "Elsa.Studio.Samples.WeatherForecast");
        Assert.Contains(response.Diagnostics, x =>
            x.ModuleId == "Elsa.Studio.Samples.WeatherForecast" &&
            x.Status == StudioModuleDiagnosticStatuses.Disabled);
    }

    [Fact]
    public async Task GetModules_CanCollectMultipleContributors()
    {
        var provider = CreateProvider();

        var response = await provider.GetRequiredService<IStudioModuleManifestProvider>().GetModules(CancellationToken.None);

        Assert.True(response.Modules.Count >= 2);
    }

    [Fact]
    public async Task GetModuleRegistry_ReturnsBackendAwareModuleMetadata()
    {
        var provider = CreateProvider();

        var response = await provider.GetRequiredService<IStudioModuleManifestProvider>().GetModuleRegistry(CancellationToken.None);

        var featureManagement = Assert.Single(response.Modules, x => x.Id == "Elsa.Studio.FeatureManagement");
        Assert.Equal("full-stack", featureManagement.Scope);
        Assert.Equal("compatible", featureManagement.Compatibility);
        Assert.Equal("available", featureManagement.Status);
        Assert.Contains(featureManagement.Contributions, x => x.Type == "http" && x.Label == "HTTP endpoints");
        Assert.Contains(featureManagement.Contributions, x => x.Type == "setting-editors");
        Assert.Contains(featureManagement.Diagnostics, x => x.Status == StudioModuleDiagnosticStatuses.Available);
    }

    [Fact]
    public async Task GetModuleRegistry_IncludesDisabledModulesForInspection()
    {
        var provider = CreateProvider(options => options.DisabledModuleIds.Add("Elsa.Studio.Samples.WeatherForecast"));

        var response = await provider.GetRequiredService<IStudioModuleManifestProvider>().GetModuleRegistry(CancellationToken.None);

        var weather = Assert.Single(response.Modules, x => x.Id == "Elsa.Studio.Samples.WeatherForecast");
        Assert.Equal(StudioModuleDiagnosticStatuses.Disabled, weather.Status);
        Assert.Contains(weather.Diagnostics, x =>
            x.ModuleId == "Elsa.Studio.Samples.WeatherForecast" &&
            x.Status == StudioModuleDiagnosticStatuses.Disabled);
    }

    private static ServiceProvider CreateProvider(Action<StudioApiOptions>? configure = null, IReadOnlyList<string>? shellFeatures = null)
    {
        var services = new ServiceCollection();
        services.AddElsaStudioApi();
        services.AddConsoleStreamStudio();
        services.AddDiagnosticsOpenTelemetryStudio();
        services.AddDiagnosticsStructuredLogsStudio();
        services.AddFeatureManagementStudio();
        services.AddWorkflowsStudio();
        services.AddDashboardStudioSample();
        services.AddWeatherForecastStudioSample();

        if (configure is not null)
            services.PostConfigure(configure);

        if (shellFeatures is not null)
            services.AddSingleton(new ShellSettings(new("Default"), shellFeatures));

        return services.BuildServiceProvider();
    }
}

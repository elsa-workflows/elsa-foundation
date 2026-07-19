using System.Reflection;
using CShells;
using CShells.Features;
using Elsa.Studio.Api.Contracts;
using Elsa.Studio.Api.Extensions;
using Elsa.Studio.Api.Options;
using Elsa.Studio.ConsoleStream;
using Elsa.Studio.Core.Models;
using Elsa.Studio.Diagnostics.OpenTelemetry;
using Elsa.Studio.Diagnostics.StructuredLogs;
using Elsa.Studio.ExpressionEditors.JavaScript;
using Elsa.Studio.ExpressionEditors.Liquid;
using Elsa.Studio.FeatureManagement;
using Elsa.Studio.Attention;
using Elsa.Studio.Dashboard;
using Elsa.Studio.Samples.WeatherForecast;
using Elsa.Studio.Weaver.Workflows;
using Elsa.Studio.Workflows;
using Elsa.Studio.Workflows.Dashboard;
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
        Assert.Contains(response.Modules, x => x.Id == "Elsa.Studio.ExpressionEditors.JavaScript");
        Assert.Contains(response.Modules, x => x.Id == "Elsa.Studio.ExpressionEditors.Liquid");
        Assert.Contains(response.Modules, x => x.Id == "Elsa.Studio.FeatureManagement");
        Assert.Contains(response.Modules, x => x.Id == "Elsa.Studio.Workflows");
        Assert.Contains(response.Modules, x => x.Id == "Elsa.Studio.Dashboard");
        Assert.Contains(response.Modules, x => x.Id == "Elsa.Studio.Attention");
        Assert.Contains(response.Modules, x => x.Id == "Elsa.Studio.Workflows.Dashboard");
        Assert.Contains(response.Modules, x => x.Id == "Elsa.Studio.Samples.WeatherForecast");
        Assert.Contains(response.Modules, x => x.Id == "Elsa.Studio.Weaver.Workflows");
        Assert.Contains(response.Diagnostics, x => x.Status == StudioModuleDiagnosticStatuses.Available);
    }

    [Fact]
    public async Task GetModules_ReturnsWeaverManifestsAndCapabilities()
    {
        var provider = CreateProvider();

        var response = await provider.GetRequiredService<IStudioModuleManifestProvider>().GetModules(CancellationToken.None);

        var workflows = Assert.Single(response.Modules, x => x.Id == "Elsa.Studio.Weaver.Workflows");
        Assert.Equal("Weaver workflows", workflows.DisplayName);
        Assert.Equal("WeaverWorkflowsStudio", workflows.ShellFeatureName);
        Assert.Contains("weaver-workflows", workflows.Capabilities);
        Assert.Contains("ai-context-providers", workflows.Capabilities);
        Assert.Contains("ai-prompt-actions", workflows.Capabilities);
        Assert.Contains("ai-proposal-renderers", workflows.Capabilities);
        Assert.Contains("ai-tools", workflows.Capabilities);
    }

    [Fact]
    public async Task GetModules_ReturnsWorkflowsManifestAssetsAndCapabilities()
    {
        var provider = CreateProvider();

        var response = await provider.GetRequiredService<IStudioModuleManifestProvider>().GetModules(CancellationToken.None);

        var module = Assert.Single(response.Modules, x => x.Id == "Elsa.Studio.Workflows");
        Assert.Equal("Workflows", module.DisplayName);
        Assert.Equal("1.0.7", module.Version);
        Assert.StartsWith("/_content/Elsa.Studio.Workflows/studio/modules/workflows/module.js", module.Entry, StringComparison.Ordinal);
        Assert.Contains($"v={module.Version}", module.Entry);
        Assert.Contains(module.Styles, x => x.StartsWith("/_content/Elsa.Studio.Workflows/studio/modules/workflows/module.css", StringComparison.Ordinal));
        Assert.All(module.Styles, style => Assert.Contains($"v={module.Version}", style));
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
    public async Task GetModules_ReturnsJavaScriptExpressionEditorManifestAssetsAndCapabilities()
    {
        var provider = CreateProvider();

        var response = await provider.GetRequiredService<IStudioModuleManifestProvider>().GetModules(CancellationToken.None);

        var module = Assert.Single(response.Modules, x => x.Id == "Elsa.Studio.ExpressionEditors.JavaScript");
        Assert.Equal("JavaScript expression editor", module.DisplayName);
        Assert.Equal("JavaScriptExpressionEditorStudio", module.ShellFeatureName);
        Assert.StartsWith("/_content/Elsa.Studio.ExpressionEditors.JavaScript/studio/modules/expression-editors/javascript/module.js", module.Entry, StringComparison.Ordinal);
        Assert.Contains(module.Styles, x => x.StartsWith("/_content/Elsa.Studio.ExpressionEditors.JavaScript/studio/modules/expression-editors/javascript/module.css", StringComparison.Ordinal));
        Assert.Contains("expression-editors", module.Capabilities);
        Assert.Contains("javascript", module.Capabilities);
    }

    [Fact]
    public async Task GetModules_ReturnsLiquidExpressionEditorManifestAssetsAndCapabilities()
    {
        var provider = CreateProvider();

        var response = await provider.GetRequiredService<IStudioModuleManifestProvider>().GetModules(CancellationToken.None);

        var module = Assert.Single(response.Modules, x => x.Id == "Elsa.Studio.ExpressionEditors.Liquid");
        Assert.Equal("Liquid expression editor", module.DisplayName);
        Assert.Equal("LiquidExpressionEditorStudio", module.ShellFeatureName);
        Assert.StartsWith("/_content/Elsa.Studio.ExpressionEditors.Liquid/studio/modules/expression-editors/liquid/module.js", module.Entry, StringComparison.Ordinal);
        Assert.Contains(module.Styles, x => x.StartsWith("/_content/Elsa.Studio.ExpressionEditors.Liquid/studio/modules/expression-editors/liquid/module.css", StringComparison.Ordinal));
        Assert.Contains("expression-editors", module.Capabilities);
        Assert.Contains("liquid", module.Capabilities);
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
                "DashboardStudio",
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

        var weaver = Assert.Single(response.Modules, x => x.Id == "Elsa.Studio.Weaver.Workflows");
        Assert.Contains(weaver.Contributions, x => x.Type == "ai-context-providers" && x.Label == "AI context providers");
        Assert.Contains(weaver.Contributions, x => x.Type == "ai-prompt-actions" && x.Label == "AI prompt actions");
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

        // Register a fake runtime feature catalog that surfaces all feature types so the
        // StudioModuleManifestProvider discovers [StudioModule] attributes via reflection.
        services.AddSingleton<IRuntimeFeatureCatalog>(new FakeRuntimeFeatureCatalog(
            typeof(ConsoleStreamStudioFeature),
            typeof(DiagnosticsOpenTelemetryStudioFeature),
            typeof(DiagnosticsStructuredLogsStudioFeature),
            typeof(JavaScriptExpressionEditorStudioFeature),
            typeof(LiquidExpressionEditorStudioFeature),
            typeof(FeatureManagementStudioFeature),
            typeof(WeaverWorkflowsStudioFeature),
            typeof(WorkflowsStudioFeature),
            typeof(DashboardStudioFeature),
            typeof(AttentionStudioFeature),
            typeof(WorkflowsDashboardStudioFeature),
            typeof(WeatherForecastStudioFeature)));

        // Pin the host/SDK version so these tests are independent of the ambient build version.
        // StudioApiOptions defaults HostVersion/SdkVersion to the Studio API assembly's informational
        // version, which the Packages CI workflow stamps via /p:Version (e.g. 4.0.0-preview.N) — that
        // would otherwise break the exact-version and major-compatibility assertions below.
        services.PostConfigure<StudioApiOptions>(options =>
        {
            options.HostVersion = "1.0.0";
            options.SdkVersion = "1.0.0";
        });

        if (configure is not null)
            services.PostConfigure(configure);

        if (shellFeatures is not null)
            services.AddSingleton(new ShellSettings(new("Default"), shellFeatures));

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Returns a snapshot whose feature descriptors carry the <see cref="ShellFeatureDescriptor.StartupType"/>
    /// of each supplied type so that <see cref="Elsa.Studio.Api.Services.StudioModuleManifestProvider"/>
    /// can reflect <c>[StudioModule]</c> attributes from them.
    /// </summary>
    private sealed class FakeRuntimeFeatureCatalog : IRuntimeFeatureCatalog
    {
        private readonly RuntimeFeatureCatalogSnapshot _snapshot;

        public FakeRuntimeFeatureCatalog(params Type[] featureTypes)
        {
            var descriptors = featureTypes
                .Select(type =>
                {
                    // Extract the feature name from the [ShellFeature] attribute if present, otherwise use the type name.
                    var shellFeatureAttr = type.GetCustomAttributesData()
                        .FirstOrDefault(a => a.AttributeType.Name == "ShellFeatureAttribute");

                    var featureName = shellFeatureAttr?.ConstructorArguments.FirstOrDefault().Value as string ?? type.Name;

                    return new ShellFeatureDescriptor { Id = featureName, StartupType = type };
                })
                .ToArray();

            _snapshot = new RuntimeFeatureCatalogSnapshot(
                1,
                Array.Empty<Assembly>(),
                descriptors,
                new Dictionary<string, ShellFeatureDescriptor>(),
                DateTimeOffset.UtcNow);
        }

        public Task<RuntimeFeatureCatalogSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_snapshot);

        public Task<RuntimeFeatureCatalogSnapshot> RefreshAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_snapshot);
    }
}

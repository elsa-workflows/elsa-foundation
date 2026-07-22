using System.Reflection;
using CShells;
using CShells.Features;
using Elsa.Studio.Api.Contracts;
using Elsa.Studio.Api.Extensions;
using Elsa.Studio.Api.Models;
using Elsa.Studio.Api.Options;
using Elsa.Studio.ConsoleStream;
using Elsa.Studio.Core.Attributes;
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
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace Elsa.Studio.Tests;

public sealed class StudioModuleManifestProviderTests
{
    private static readonly string[] ExpectedModuleIds =
    [
        "Elsa.Studio.ConsoleStream",
        "Elsa.Studio.Diagnostics.OpenTelemetry",
        "Elsa.Studio.Diagnostics.StructuredLogs",
        "Elsa.Studio.ExpressionEditors.JavaScript",
        "Elsa.Studio.ExpressionEditors.Liquid",
        "Elsa.Studio.FeatureManagement",
        "Elsa.Studio.Workflows",
        "Elsa.Studio.Dashboard",
        "Elsa.Studio.Attention",
        "Elsa.Studio.Workflows.Dashboard",
        "Elsa.Studio.Samples.WeatherForecast",
        "Elsa.Studio.Weaver.Workflows"
    ];

    [Fact]
    public async Task GetModules_ReturnsHostSdkVersionsAndCollectsEveryModule()
    {
        var response = await GetModulesAsync();

        Assert.Equal("1.0.0", response.HostVersion);
        Assert.Equal("1.0.0", response.SdkVersion);
        Assert.All(ExpectedModuleIds, id => Assert.Contains(response.Modules, x => x.Id == id));
        Assert.Contains(response.Diagnostics, x => x.Status == StudioModuleDiagnosticStatuses.Available);
    }

    public static TheoryData<ModuleExpectation> ModuleManifests => new()
    {
        new("Elsa.Studio.Workflows", "Workflows",
            AssetPath: "/_content/Elsa.Studio.Workflows/studio/modules/workflows/",
            Version: "1.0.8",
            Capabilities: ["navigation", "routes", "workflow-designer"]),
        new("Elsa.Studio.FeatureManagement", "Feature management",
            AssetPath: "/_content/Elsa.Studio.FeatureManagement/studio/modules/features/",
            Capabilities: ["navigation", "routes", "setting-editors"]),
        new("Elsa.Studio.ExpressionEditors.JavaScript", "JavaScript expression editor",
            AssetPath: "/_content/Elsa.Studio.ExpressionEditors.JavaScript/studio/modules/expression-editors/javascript/",
            ShellFeatureName: "JavaScriptExpressionEditorStudio",
            Capabilities: ["expression-editors", "javascript"]),
        new("Elsa.Studio.ExpressionEditors.Liquid", "Liquid expression editor",
            AssetPath: "/_content/Elsa.Studio.ExpressionEditors.Liquid/studio/modules/expression-editors/liquid/",
            ShellFeatureName: "LiquidExpressionEditorStudio",
            Capabilities: ["expression-editors", "liquid"]),
        new("Elsa.Studio.Diagnostics.StructuredLogs", "Structured logs",
            AssetPath: "/_content/Elsa.Studio.Diagnostics.StructuredLogs/studio/modules/structured-logs/",
            Capabilities: ["navigation", "routes", "panels", "http", "sse", "diagnostics"]),
        new("Elsa.Studio.Diagnostics.OpenTelemetry", "OpenTelemetry",
            AssetPath: "/_content/Elsa.Studio.Diagnostics.OpenTelemetry/studio/modules/open-telemetry/",
            Capabilities: ["navigation", "routes", "http", "diagnostics", "otel", "traces", "metrics", "logs"]),
        // Weaver is a backend-only AI module: it carries capabilities but ships no frontend bundle, so AssetPath stays null.
        new("Elsa.Studio.Weaver.Workflows", "Weaver workflows",
            ShellFeatureName: "WeaverWorkflowsStudio",
            Capabilities: ["weaver-workflows", "ai-context-providers", "ai-prompt-actions", "ai-proposal-renderers", "ai-tools"])
    };

    [Theory]
    [MemberData(nameof(ModuleManifests))]
    public async Task GetModules_ProjectsModuleManifest(ModuleExpectation expected)
    {
        var response = await GetModulesAsync();

        var module = Assert.Single(response.Modules, x => x.Id == expected.Id);
        Assert.Equal(expected.DisplayName, module.DisplayName);
        Assert.All(expected.Capabilities, capability => Assert.Contains(capability, module.Capabilities));

        if (expected.ShellFeatureName is not null)
            Assert.Equal(expected.ShellFeatureName, module.ShellFeatureName);

        if (expected.Version is not null)
            Assert.Equal(expected.Version, module.Version);

        if (expected.AssetPath is not null)
        {
            Assert.StartsWith(expected.AssetPath + "module.js", module.Entry, StringComparison.Ordinal);
            Assert.Contains($"v={module.Version}", module.Entry);
            Assert.Contains(module.Styles, style => style.StartsWith(expected.AssetPath + "module.css", StringComparison.Ordinal));
            Assert.All(module.Styles, style => Assert.Contains($"v={module.Version}", style));
        }
    }

    [Fact]
    public async Task GetModules_ListsEveryEmittedStylesheetInNumericOrder()
    {
        // Vite cssCodeSplit builds emit module.css, module2.css, … — the manifest must link them all,
        // in emission order, and ignore unrelated assets.
        var response = await GetModulesAsync(webAssets: new Dictionary<string, string[]>
        {
            ["_content/Elsa.Studio.Workflows/studio/modules/workflows"] =
                ["module3.css", "module.css", "module10.css", "module2.css", "module.js", "chunk-abc.js", "styles.txt"]
        });

        var module = Assert.Single(response.Modules, x => x.Id == "Elsa.Studio.Workflows");
        Assert.Equal(
        [
            $"/_content/Elsa.Studio.Workflows/studio/modules/workflows/module.css?v={module.Version}",
            $"/_content/Elsa.Studio.Workflows/studio/modules/workflows/module2.css?v={module.Version}",
            $"/_content/Elsa.Studio.Workflows/studio/modules/workflows/module3.css?v={module.Version}",
            $"/_content/Elsa.Studio.Workflows/studio/modules/workflows/module10.css?v={module.Version}"
        ], module.Styles);
    }

    [Fact]
    public async Task GetModules_FallsBackToConventionalStylesheetWhenAssetsCannotBeEnumerated()
    {
        // A web host environment is registered but the module's asset directory is not served from it.
        var response = await GetModulesAsync(webAssets: new Dictionary<string, string[]>());

        var module = Assert.Single(response.Modules, x => x.Id == "Elsa.Studio.Workflows");
        Assert.Equal([$"/_content/Elsa.Studio.Workflows/studio/modules/workflows/module.css?v={module.Version}"], module.Styles);
    }

    [Fact]
    public async Task GetModules_OmitsStylesForModulesWithoutStylesheets()
    {
        var response = await GetModulesAsync(extraFeatureTypes: [typeof(StylelessStudioFeature)]);

        var module = Assert.Single(response.Modules, x => x.Id == typeof(StylelessStudioFeature).Assembly.GetName().Name);
        Assert.Empty(module.Styles);
    }

    [StudioModule("tests/styleless", "Styleless", "1.0.0", HasStyles = false)]
    private sealed class StylelessStudioFeature;

    [Theory]
    [InlineData("Elsa.Studio.Samples.WeatherForecast")]
    [InlineData("Elsa.Studio.ConsoleStream")]
    [InlineData("Elsa.Studio.Diagnostics.StructuredLogs")]
    [InlineData("Elsa.Studio.Diagnostics.OpenTelemetry")]
    public async Task GetModules_FiltersDisabledModuleAndReportsDiagnostic(string moduleId)
    {
        var response = await GetModulesAsync(options => options.DisabledModuleIds.Add(moduleId));

        Assert.DoesNotContain(response.Modules, x => x.Id == moduleId);
        AssertDisabled(response.Diagnostics, moduleId);
    }

    [Fact]
    public async Task GetModules_FiltersModulesOwnedByInactiveShellFeatures()
    {
        var response = await GetModulesAsync(shellFeatures: ["ConsoleStream", "FeatureManagement", "DashboardStudio", "StudioApi"]);

        Assert.DoesNotContain(response.Modules, x => x.Id == "Elsa.Studio.Samples.WeatherForecast");
        AssertDisabled(response.Diagnostics, "Elsa.Studio.Samples.WeatherForecast");
    }

    [Fact]
    public async Task GetModuleRegistry_ReturnsBackendAwareModuleMetadata()
    {
        var response = await GetRegistryAsync();

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
        var response = await GetRegistryAsync(options => options.DisabledModuleIds.Add("Elsa.Studio.Samples.WeatherForecast"));

        var weather = Assert.Single(response.Modules, x => x.Id == "Elsa.Studio.Samples.WeatherForecast");
        Assert.Equal(StudioModuleDiagnosticStatuses.Disabled, weather.Status);
        AssertDisabled(weather.Diagnostics, "Elsa.Studio.Samples.WeatherForecast");
    }

    private static void AssertDisabled(IEnumerable<StudioModuleDiagnostic> diagnostics, string moduleId) =>
        Assert.Contains(diagnostics, x => x.ModuleId == moduleId && x.Status == StudioModuleDiagnosticStatuses.Disabled);

    private static async Task<StudioModulesResponse> GetModulesAsync(
        Action<StudioApiOptions>? configure = null,
        IReadOnlyList<string>? shellFeatures = null,
        IReadOnlyDictionary<string, string[]>? webAssets = null,
        Type[]? extraFeatureTypes = null)
    {
        await using var provider = CreateProvider(configure, shellFeatures, webAssets, extraFeatureTypes);
        return await provider.GetRequiredService<IStudioModuleManifestProvider>().GetModules(CancellationToken.None);
    }

    private static async Task<StudioModuleRegistryResponse> GetRegistryAsync(Action<StudioApiOptions>? configure = null)
    {
        await using var provider = CreateProvider(configure);
        return await provider.GetRequiredService<IStudioModuleManifestProvider>().GetModuleRegistry(CancellationToken.None);
    }

    private static ServiceProvider CreateProvider(
        Action<StudioApiOptions>? configure = null,
        IReadOnlyList<string>? shellFeatures = null,
        IReadOnlyDictionary<string, string[]>? webAssets = null,
        Type[]? extraFeatureTypes = null)
    {
        var services = new ServiceCollection();
        services.AddElsaStudioApi();

        if (webAssets is not null)
            services.AddSingleton<IWebHostEnvironment>(new FakeWebHostEnvironment(webAssets));

        // Register a fake runtime feature catalog that surfaces all feature types so the
        // StudioModuleManifestProvider discovers [StudioModule] attributes via reflection.
        services.AddSingleton<IRuntimeFeatureCatalog>(new FakeRuntimeFeatureCatalog(
        [
            .. extraFeatureTypes ?? [],
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
            typeof(WeatherForecastStudioFeature)
        ]));

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

    /// <summary>Expected projection of a single module manifest. <paramref name="AssetPath"/> is null for backend-only modules that ship no frontend bundle.</summary>
    public sealed record ModuleExpectation(
        string Id,
        string DisplayName,
        string[] Capabilities,
        string? AssetPath = null,
        string? ShellFeatureName = null,
        string? Version = null);

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

    /// <summary>
    /// Web host environment whose web root serves the supplied directory-path → file-names map,
    /// mimicking how static web assets expose module bundles under <c>_content/…</c>.
    /// </summary>
    private sealed class FakeWebHostEnvironment(IReadOnlyDictionary<string, string[]> webAssets) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Tests";
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = "/";
        public string WebRootPath { get; set; } = "/wwwroot";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider WebRootFileProvider { get; set; } = new FakeFileProvider(webAssets);
    }

    private sealed class FakeFileProvider(IReadOnlyDictionary<string, string[]> directories) : IFileProvider
    {
        public IDirectoryContents GetDirectoryContents(string subpath) =>
            directories.TryGetValue(subpath.Trim('/'), out var files)
                ? new FakeDirectoryContents(files)
                : NotFoundDirectoryContents.Singleton;

        public IFileInfo GetFileInfo(string subpath) => new NotFoundFileInfo(subpath);

        public IChangeToken Watch(string filter) => NullChangeToken.Singleton;
    }

    private sealed class FakeDirectoryContents(string[] files) : IDirectoryContents
    {
        public bool Exists => true;

        public IEnumerator<IFileInfo> GetEnumerator() => files.Select(IFileInfo (name) => new FakeFileInfo(name)).GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class FakeFileInfo(string name) : IFileInfo
    {
        public bool Exists => true;
        public long Length => 0;
        public string? PhysicalPath => null;
        public string Name => name;
        public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;
        public bool IsDirectory => false;

        public Stream CreateReadStream() => Stream.Null;
    }
}

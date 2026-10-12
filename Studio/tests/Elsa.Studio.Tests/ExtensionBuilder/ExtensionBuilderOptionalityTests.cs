using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CShells;
using CShells.AspNetCore.Configuration;
using CShells.AspNetCore.Extensions;
using CShells.DependencyInjection;
using CShells.Features;
using Elsa.Studio.Api.Contracts;
using Elsa.Studio.Api.Extensions;
using Elsa.Studio.Api.Models;
using Elsa.Studio.Api.Options;
using Elsa.Studio.Core.Models;
using Elsa.Studio.ExtensionBuilder;
using Elsa.Studio.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using static Elsa.Studio.Tests.ExtensionBuilder.ExtensionBuilderBridgeHost;
using static Elsa.Studio.Tests.RecordingBackend;

namespace Elsa.Studio.Tests.ExtensionBuilder;

/// <summary>
/// Pins Extension Builder's optionality (#535): a default Studio carries no trace of it — no bridge endpoints, no
/// module manifest, no feature key in <c>shells.json</c> — and enabling the <c>ExtensionBuilderStudio</c> feature is
/// what brings the module and its bridge back, with the host's authorization gate intact on the shell-mapped routes.
/// </summary>
public sealed class ExtensionBuilderOptionalityTests : IAsyncDisposable
{
    private const string FeatureName = "ExtensionBuilderStudio";
    private const string ModuleId = "Elsa.Studio.ExtensionBuilder";
    private const string CapabilitiesRoute = StudioExtensionBuilderBridge.RouteGroup + StudioExtensionBuilderBridge.CapabilitiesRoute;
    private const string RelayRoute = StudioExtensionBuilderBridge.RouteGroup + "/workspaces";
    private const string StatusRoute = StudioBackendManagementRoutes.RouteGroup + "/status";
    private const string StaticAssetRoute = "/studio/asset.txt";
    private const string StaticAssetContent = "asset";

    // The feature keys the shipped shells.json enables for the Default shell — the default Studio composition.
    private static readonly string[] DefaultShellFeatures = ReadDefaultShellFeatures();

    private WebApplication? _app;

    // ---- The host's own bridge carries no Extension Builder route -------------------------------------------------

    [Theory]
    [InlineData(CapabilitiesRoute)]
    [InlineData(RelayRoute)]
    public async Task HostBridgeAloneAnswers404ForExtensionBuilderRoutes(string route)
    {
        var client = await StartHostBridgeOnlyAsync();

        var response = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        // The host's own bridge surface is live on the same host, so the 404 is the route's absence, not a dead host.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(StatusRoute)).StatusCode);
    }

    // ---- The default composition does not enable the feature --------------------------------------------------------

    [Fact]
    public void DefaultShellDoesNotEnableTheFeature()
    {
        Assert.NotEmpty(DefaultShellFeatures);
        Assert.DoesNotContain(FeatureName, DefaultShellFeatures);
    }

    [Fact]
    public async Task ManifestProviderExcludesTheModuleForTheDefaultShell()
    {
        var response = await GetModulesAsync(DefaultShellFeatures);

        Assert.DoesNotContain(response.Modules, x => x.Id == ModuleId);
        var diagnostic = Assert.Single(response.Diagnostics, x => x.ModuleId == ModuleId);
        Assert.Equal(StudioModuleDiagnosticStatuses.Disabled, diagnostic.Status);
        Assert.Contains(FeatureName, diagnostic.Reason);
    }

    [Fact]
    public async Task ManifestProviderIncludesTheModuleWhenTheFeatureIsEnabled()
    {
        var response = await GetModulesAsync([.. DefaultShellFeatures, FeatureName]);

        var module = Assert.Single(response.Modules, x => x.Id == ModuleId);
        Assert.Equal("Extension Builder", module.DisplayName);
        Assert.Equal(FeatureName, module.ShellFeatureName);
        Assert.StartsWith("/_content/Elsa.Studio.ExtensionBuilder/studio/modules/extension-builder/module.js", module.Entry, StringComparison.Ordinal);
        Assert.Contains(module.Styles, x => x.StartsWith("/_content/Elsa.Studio.ExtensionBuilder/studio/modules/extension-builder/module.css", StringComparison.Ordinal));
        Assert.Equal(["navigation", "routes", "http"], module.Capabilities);
    }

    // ---- The composed shell: feature off → no routes; feature on → bridge with the host's gate ----------------------

    [Theory]
    [InlineData(CapabilitiesRoute)]
    [InlineData(RelayRoute)]
    public async Task ComposedShellWithoutTheFeatureAnswers404(string route)
    {
        var client = await StartComposedHostAsync(RespondingWith(_ => JsonOk("{}")), featureEnabled: false);

        var response = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ComposedShellWithTheFeatureRelaysThroughTheHostBridgeInfrastructure()
    {
        // The feature's clients resolve the HOST's backend options (base URL + management key) from inside the shell:
        // the relay forwards with the key and the capabilities read answers through the shared probe.
        var backend = RespondingWith(_ => JsonOk("""{ "canCreateWorkspace": true, "canEditFiles": true, "canBuild": true, "canPromote": true, "canRollback": true }"""));
        var client = await StartComposedHostAsync(backend, featureEnabled: true);

        var relay = await client.GetAsync(RelayRoute);
        var capabilities = await client.GetFromJsonAsync<JsonElement>(CapabilitiesRoute);

        Assert.Equal(HttpStatusCode.OK, relay.StatusCode);
        Assert.Equal(StudioBackendManagementStatus.Available, capabilities.GetProperty("status").GetString());
        Assert.Equal(2, backend.ManagementRequests.Count);
        Assert.All(backend.ManagementRequests, recorded => Assert.Equal(BridgeTestHost.ManagementKey, recorded.ManagementKey));
        Assert.Equal(StudioExtensionBuilderBridge.BackendRoot + "/workspaces", backend.ManagementRequests[0].PathAndQuery);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData(ModuleReadBearer, HttpStatusCode.Forbidden)]
    [InlineData(ExtensionBuilderReadBearer, HttpStatusCode.OK)]
    public async Task ComposedShellKeepsTheHostAuthorizationGateOnTheFirstAndEveryRequest(string? bearer, HttpStatusCode expected)
    {
        // The very first request to the shell-mapped bridge — the shell is still cold — must be gated exactly like every
        // later one: 401 unauthenticated, 403 without extension-builder.read, relayed otherwise. A backend management
        // call is issued only for the allowed caller.
        var backend = RespondingWith(WithSessionEndpoint(BearerPermissions, _ => JsonOk("""[ ]""")));
        var client = await StartComposedHostAsync(backend, featureEnabled: true, authEnabled: true);
        if (bearer is not null)
            client.DefaultRequestHeaders.Authorization = new("Bearer", bearer);

        var cold = await client.GetAsync(RelayRoute);
        var warm = await client.GetAsync(RelayRoute);

        Assert.Equal(expected, cold.StatusCode);
        Assert.Equal(expected, warm.StatusCode);
        Assert.Equal(expected == HttpStatusCode.OK ? 2 : 0, backend.ManagementRequests.Count);
    }

    [Fact]
    public async Task ComposedShellWhoseActivationFailsStillServesHostRequests()
    {
        // A feature that throws while the shell activates must not turn every host request into a 500: the eager
        // activation logs the failure and the request continues, so a static file is still served.
        var client = await StartComposedHostAsync(RespondingWith(_ => JsonOk("{}")), featureEnabled: true, withFailingFeature: true);

        var response = await client.GetAsync(StaticAssetRoute);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(StaticAssetContent, await response.Content.ReadAsStringAsync());
    }

    // ---- Harness -----------------------------------------------------------------------------------------------------

    private static string[] ReadDefaultShellFeatures()
    {
        // Elsa.Studio.Web copies its shells.json to the output directory, so the test reads the shipped composition.
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Join(AppContext.BaseDirectory, "shells.json")));
        return document.RootElement
            .GetProperty("CShells").GetProperty("Shells").GetProperty("Default").GetProperty("Features")
            .EnumerateObject()
            .Select(feature => feature.Name)
            .ToArray();
    }

    private static async Task<StudioModulesResponse> GetModulesAsync(IReadOnlyList<string> shellFeatures)
    {
        var services = new ServiceCollection();
        services.AddElsaStudioApi();
        services.AddSingleton<IRuntimeFeatureCatalog>(new FakeRuntimeFeatureCatalog(typeof(ExtensionBuilderStudioFeature)));
        services.AddSingleton(new ShellSettings(new("Default"), shellFeatures));
        await using var provider = services.BuildServiceProvider();
        return await provider.GetRequiredService<IStudioModuleManifestProvider>().GetModules(CancellationToken.None);
    }

    private async Task<HttpClient> StartHostBridgeOnlyAsync()
    {
        var backend = RespondingWith(_ => JsonOk("""{ "modules": [] }"""));
        var builder = BridgeTestHost.CreateBuilder();
        builder.Services.AddHttpClient(nameof(StudioBackendManagementClient)).ConfigurePrimaryHttpMessageHandler(() => backend);

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapStudioBackendManagementBridge();
        MapFallbacks(app);

        return await StartAsync(app);
    }

    // A host composed like Program.cs: the host's bridge auth + options on the root, the Extension Builder feature
    // assembly offered to CShells, and the Default shell configured from (shells.json-shaped) configuration with or
    // without the feature enabled (optionally alongside a feature whose activation throws).
    private async Task<HttpClient> StartComposedHostAsync(RecordingBackend backend, bool featureEnabled, bool authEnabled = false, bool withFailingFeature = false)
    {
        var shellSettings = new Dictionary<string, string?>
        {
            ["CShells:Shells:Default:Configuration:WebRouting:Path"] = ""
        };
        if (featureEnabled)
            shellSettings[$"CShells:Shells:Default:Features:{FeatureName}"] = "true";
        if (withFailingFeature)
            shellSettings[$"CShells:Shells:Default:Features:{FailingShellFeature.Name}"] = "true";

        var builder = BridgeTestHost.CreateBuilder(authEnabled: authEnabled, extraSettings: shellSettings);
        builder.Services.RouteBackendClientsThrough(backend);
        builder.Services.AddCShellsAspNetCore(shells =>
        {
            shells
                .WithAssemblies(typeof(ExtensionBuilderStudioFeature).Assembly, typeof(FailingShellFeature).Assembly)
                .WithConfigurationProvider(builder.Configuration)
                .WithWebRouting(options => options.EnablePathRouting = true);
        });

        var app = builder.Build();
        app.UseStudioDefaultShellActivation();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        // Program.cs's UseStudioStaticFiles sits here, ahead of the shell middleware MapShells adds.
        app.Map(StaticAssetRoute, asset => asset.Run(context => context.Response.WriteAsync(StaticAssetContent)));
        app.MapStudioBackendManagementBridge();
        app.MapShells();
        MapFallbacks(app);

        return await StartAsync(app);
    }

    // Program.cs's fallbacks: unknown API routes stay real 404s instead of being swallowed by the SPA fallback.
    private static void MapFallbacks(WebApplication app)
    {
        app.MapFallback("/_elsa/{**path}", () => Results.NotFound());
        app.MapFallback(() => Results.Text("<html>studio</html>", "text/html"));
    }

    private async Task<HttpClient> StartAsync(WebApplication app)
    {
        await app.StartAsync();
        _app = app;
        return app.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
            await _app.DisposeAsync();
    }
}

/// <summary>A shell feature whose service registration throws, so activating a shell that enables it fails.</summary>
[ShellFeature(Name)]
public sealed class FailingShellFeature : IShellFeature
{
    public const string Name = "FailingStudioTestFeature";

    public void ConfigureServices(IServiceCollection services) =>
        throw new InvalidOperationException("Feature registration failed.");
}

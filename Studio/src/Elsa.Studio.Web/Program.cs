using CShells.AspNetCore.Configuration;
using CShells.AspNetCore.Extensions;
using CShells.DependencyInjection;
using Elsa.Studio.Api.Features;
using Elsa.Studio.ConsoleStream;
using Elsa.Studio.ConsoleStream.Extensions;
using Elsa.Studio.Diagnostics.OpenTelemetry;
using Elsa.Studio.Diagnostics.StructuredLogs;
using Elsa.Studio.FeatureManagement;
using Elsa.Studio.Workflows;
using Elsa.Studio.Samples.Dashboard;
using Elsa.Studio.Weaver.Workflows;
using Elsa.Studio.Web;
using Nuplane;
using Nuplane.Admin;
using Nuplane.Loading.Hosting.Builder;
using Nuplane.Sources.Directory.Configuration;
using System.Text.Json;

// Install the console stream capture hook before the host is created so that, when the ConsoleStream
// feature is enabled in shells.json, all process stdout is captured from startup instead of only from
// the point the feature is activated during host build.
ConsoleStreamHookInstaller.InstallConsoleStreamHookIfEnabled(args);

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("shells.json", optional: false, reloadOnChange: true);
builder.Configuration.AddJsonFile("nuplane-management.json", optional: true, reloadOnChange: true);
var configuration = builder.Configuration;
ConsoleStreamHookInstaller.InstallConsoleStreamHookIfEnabled(configuration);
var nuplaneConfiguration = configuration.GetSection("Nuplane");

builder.WebHost.UseStaticWebAssets();

builder.Services.AddSingleton<IConfigurationRoot>(configuration);
builder.Services.AddNuplaneAdmin();
builder.Services.AddNuplane(nuplaneConfiguration, nuplane =>
{
    nuplane.AddDirectoryFeedsFromConfiguration(nuplaneConfiguration);
    nuplane.AutoloadPackages(nuplaneConfiguration.GetSection("Loading"));
    nuplane.OnPackagesChanged<StudioNuplaneShellReloadObserver>();
});
builder.Services.AddSingleton<StudioRuntimeFeatureCatalogRefresher>();
builder.Services.AddSingleton<StudioShellFeatureConfigurationStore>();
builder.Services.AddSingleton<StudioNuplaneAssemblyProvider>();

builder.Services.AddCShellsAspNetCore(shells =>
{
    shells
        .WithAssemblyProvider<StudioNuplaneAssemblyProvider>()
        .WithAssemblies(
            typeof(StudioApiFeature).Assembly,
            typeof(ConsoleStreamStudioFeature).Assembly,
            typeof(DiagnosticsOpenTelemetryStudioFeature).Assembly,
            typeof(DiagnosticsStructuredLogsStudioFeature).Assembly,
            typeof(FeatureManagementStudioFeature).Assembly,
            typeof(WeaverWorkflowsStudioFeature).Assembly,
            typeof(WorkflowsStudioFeature).Assembly,
            typeof(DashboardStudioFeature).Assembly,
            typeof(ThemeStoreCoreStudioFeature).Assembly)
        .WithConfigurationProvider(configuration)
        .WithWebRouting(options =>
        {
            options.EnablePathRouting = true;
        });
});

// Host the console-log-streaming server (capture host + SignalR hub + HTTP endpoints) on the application
// root rather than inside the ConsoleStream shell feature. Shell route scopes are recycled at runtime on
// package changes (see StudioNuplaneShellReloadObserver), which would dispose the service provider a live
// hub connection captured and throw ObjectDisposedException in OnDisconnectedAsync. The root container's
// lifetime matches the connection's. Gated by the same feature-enablement check as the capture hook above.
builder.Services.AddConsoleStreamStudioHostIfEnabled(configuration);

// Gate the Studio management surface (module management, feature management, console-stream) behind an
// authentication/authorization policy. The built-in scheme validates Studio:BackendModuleManagementApiKey; a host
// can register its own authentication and reuse ModuleManagementAuth.PolicyName as the seam. The console-stream
// hub path is allowed to carry the key as an access_token query parameter (browsers cannot set headers on the
// WebSocket/SSE handshake); every other endpoint takes the key by header only, keeping it out of access logs.
builder.Services.AddModuleManagementAuth(configuration, builder.Environment, ConsoleStreamStudioServiceCollectionExtensions.HubPath);

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/studio-runtime.js", () =>
{
    var runtimeConfig = new Dictionary<string, object?>
    {
        ["backendBaseUrl"] = configuration["Studio:BackendBaseUrl"] ?? string.Empty,
        ["backendModuleManagementApiKey"] = configuration["Studio:BackendModuleManagementApiKey"] ?? string.Empty,
        // Surface the user-auth seam so the shell can attach real bearer tokens (and 401-refresh-retry)
        // against the backend identity endpoints. Omitted endpoints fall back to the SDK defaults
        // (`/_elsa/identity/token`); when Enabled is false the shell keeps booting anonymously.
        ["auth"] = BuildAuthRuntimeConfig(configuration),
        ["workflows"] = BuildWorkflowsRuntimeConfig(configuration)
    };

    return Results.Content(
        $"window.__ELSA_STUDIO_RUNTIME__ = {JsonSerializer.Serialize(runtimeConfig)};",
        "application/javascript");
});

static Dictionary<string, object?> BuildAuthRuntimeConfig(IConfiguration configuration)
{
    var auth = new Dictionary<string, object?>
    {
        ["enabled"] = configuration.GetValue("Studio:Auth:Enabled", defaultValue: false)
    };

    var tokenEndpoint = configuration["Studio:Auth:TokenEndpoint"];
    if (!string.IsNullOrWhiteSpace(tokenEndpoint))
        auth["tokenEndpoint"] = tokenEndpoint;

    var refreshEndpoint = configuration["Studio:Auth:RefreshEndpoint"];
    if (!string.IsNullOrWhiteSpace(refreshEndpoint))
        auth["refreshEndpoint"] = refreshEndpoint;

    return auth;
}

static Dictionary<string, object?> BuildWorkflowsRuntimeConfig(IConfiguration configuration)
{
    return new Dictionary<string, object?>
    {
        ["autosaveEnabledByDefault"] = configuration.GetValue("Studio:Workflows:AutosaveEnabledByDefault", defaultValue: true)
    };
}

app.UseStaticFiles();

app.MapElsaModuleManagementApi();
app.MapElsaFeatureManagementApi();
app.MapConsoleStreamStudioIfEnabled(configuration, ModuleManagementAuth.PolicyName);
app.MapShells();
app.MapActiveShellStudioApi();
app.MapNuplaneStaticWebAssets();
app.MapFallbackToFile("studio/index.html");

app.Run();

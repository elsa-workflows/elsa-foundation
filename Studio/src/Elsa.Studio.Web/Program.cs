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

// Studio management bridge (ADR 0037): a Studio-owned server-side surface that reports backend management availability
// so the browser stops probing backend host-control endpoints directly. The bridge — and every other browser-facing
// Studio host-control endpoint (module management, feature management, theme management, console-stream) — is gated by
// a single coarse user-session gate (StudioBridgeAuth). The browser never carries an Elsa host management key; Studio
// attaches the server-side management key only on its own backend calls (StudioBackendManagementClient). The
// console-stream hub path is allowed to carry the browser bearer as an access_token query parameter (browsers cannot
// set headers on the WebSocket/SSE handshake); every other endpoint takes the bearer by header only, keeping it out of
// access logs. When Studio auth is disabled the gate allows anonymously so the demo shell keeps working.
builder.Services.AddStudioBridgeAuth(configuration, ConsoleStreamStudioServiceCollectionExtensions.HubPath);
builder.Services.AddStudioBackendManagementBridge(configuration);

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// Browser runtime config (window.__ELSA_STUDIO_RUNTIME__). The body is built by StudioRuntimeScript, which
// deliberately omits the backend host management key so no server-side secret reaches the browser (ADR 0037).
app.MapGet("/studio-runtime.js", () =>
    Results.Content(StudioRuntimeScript.Render(configuration), "application/javascript"));

app.UseStaticFiles();

app.MapElsaModuleManagementApi();
app.MapStudioBackendManagementBridge();
app.MapElsaFeatureManagementApi();
app.MapConsoleStreamStudioIfEnabled(configuration, StudioBridgeAuth.PolicyName);
app.MapShells();
app.MapActiveShellStudioApi();
app.MapNuplaneStaticWebAssets();
app.MapFallbackToFile("studio/index.html");

app.Run();

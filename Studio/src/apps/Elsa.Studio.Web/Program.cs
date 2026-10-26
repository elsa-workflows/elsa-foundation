using Elsa.Studio.Api.Authorization;
using Elsa.Studio.ConsoleStream;
using Elsa.Studio.ConsoleStream.Extensions;
using Elsa.Studio.Web;
using Elsa.Studio.Hosting;
using Nuplane;
using Nuplane.Admin;
using Nuplane.Loading.Hosting.Builder;
using Nuplane.Sources.Directory.Configuration;

// Install the console stream capture hook before the host is created so that, when the ConsoleStream
// feature is enabled in shells.json, all process stdout is captured from startup instead of only from
// the point the feature is activated during host build.
ConsoleStreamHookInstaller.InstallConsoleStreamHookIfEnabled(args);

var builder = WebApplication.CreateBuilder(args);
var studio = builder.AddElsaStudio(options => options.QueryTokenPathPrefixes = [ConsoleStreamStudioServiceCollectionExtensions.HubPath]);
builder.Configuration.AddJsonFile("nuplane-management.json", optional: true, reloadOnChange: true);
var configuration = builder.Configuration;
ConsoleStreamHookInstaller.InstallConsoleStreamHookIfEnabled(configuration);
var nuplaneConfiguration = configuration.GetSection("Nuplane");

// Theme configuration. Bound from the "Themes" section, which the Configuration API populates from
// appsettings.json, environment variables (Themes__ThemesDirectory, etc.), and any other registered source.
var themeConfig = new ThemeConfiguration();
configuration.GetSection("Themes").Bind(themeConfig);
_ = themeConfig.GetThemesMode();
builder.Services.AddSingleton(themeConfig);
builder.Services.AddSingleton<ThemeConfigurationService>();

builder.Services.AddNuplaneAdmin();
builder.Services.AddNuplane(nuplaneConfiguration, nuplane =>
{
    nuplane.AddDirectoryFeedsFromConfiguration(nuplaneConfiguration);
    nuplane.AutoloadPackages(nuplaneConfiguration.GetSection("Loading"));
});
builder.Services.AddSingleton<StudioShellFeatureConfigurationStore>();

studio.ConfigureShells(shells => shells.WithStudioFeatureAssemblies());

// Host the console-log-streaming server (capture host + SignalR hub + HTTP endpoints) on the application
// root rather than inside the ConsoleStream shell feature. Shell route scopes are recycled at runtime on
// package changes (through the shared CShells.Nuplane adapter), which would dispose the service provider a live
// hub connection captured and throw ObjectDisposedException in OnDisconnectedAsync. The root container's
// lifetime matches the connection's. Gated by the same feature-enablement check as the capture hook above.
builder.Services.AddConsoleStreamStudioHostIfEnabled(configuration);

var app = builder.Build();

app.UseElsaStudio();
app.MapElsaStudio();

app.MapElsaModuleManagementApi();
app.MapElsaFeatureManagementApi();
app.MapConsoleStreamStudioIfEnabled(configuration, StudioBridgeAuthorization.PolicyName);
app.MapNuplaneStaticWebAssets();

app.Run();

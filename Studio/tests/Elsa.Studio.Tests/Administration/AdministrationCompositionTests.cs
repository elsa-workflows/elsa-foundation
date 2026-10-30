using System.Net;
using Elsa.Studio.Hosting;
using Elsa.Studio.Hosting.Nuplane;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nuplane.Admin;
using Nuplane.Builder;
using Nuplane.Reconciliation.LockFile;
using Nuplane.Feeds.Configuration;
using Nuplane.Sources.Directory.Builder;

namespace Elsa.Studio.Tests.Administration;

public sealed class AdministrationCompositionTests
{
    [Fact]
    public async Task Base_host_does_not_install_administration_services_or_routes()
    {
        await using var host = await AdministrationHost.StartAsync(addAdministration: false);
        Assert.Null(host.App.Services.GetService<INuplaneAdminOperations>());
        Assert.Null(host.App.Services.GetService<INuplanePackageStoreMaintenanceOperations>());
        using var response = await host.Client.GetAsync("/_elsa/module-management/registry");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var modules = await host.Client.GetAsync("/_elsa/studio/modules");
        modules.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Ordinary_optional_host_starts_with_a_registered_content_root_fallback()
    {
        await using var host = await AdministrationHost.StartAsync();
        var feeds = host.App.Services.GetRequiredService<IOptions<FeedResolutionOptions>>().Value.Feeds;
        var selected = Assert.Single(feeds, feed => feed.ServiceIndex.IsFile);
        Assert.Equal(Path.Combine(host.Root, "packages"), Path.TrimEndingDirectorySeparator(selected.ServiceIndex.LocalPath));
        Directory.CreateDirectory(host.App.Services.GetRequiredService<IOptions<FeedResolutionOptions>>().Value.PackageInstallRoot!);
        var maintenance = host.App.Services.GetRequiredService<INuplanePackageStoreMaintenanceOperations>();
        var preview = await maintenance.PruneAsync(new());
        Assert.False(preview.InventoryComplete);
        Assert.Equal("Unknown", preview.ProtectionKnowledge);
        Assert.Contains(preview.Diagnostics, diagnostic => diagnostic.Code == "EnrollmentRequired");
    }

    [Fact]
    public async Task Callback_directory_feed_replaces_the_inactive_fallback()
    {
        await using var host = await AdministrationHost.StartAsync(nuplane => nuplane.AddDirectoryFeed("application-feed", "custom-feed", feed => { feed.Watch = false; feed.IncludeAll(); }));
        var feeds = host.App.Services.GetRequiredService<IOptions<FeedResolutionOptions>>().Value.Feeds;
        var selected = Assert.Single(feeds, feed => feed.ServiceIndex.IsFile);
        Assert.Equal("application-feed", selected.Name);
        Assert.Equal(Path.Combine(host.Root, "custom-feed"), Path.TrimEndingDirectorySeparator(selected.ServiceIndex.LocalPath));
    }

    [Fact]
    public async Task Multiple_callback_directory_feeds_require_explicit_selection()
    {
        await Assert.ThrowsAsync<StudioHostingException>(async () =>
        {
            await using var host = await AdministrationHost.StartAsync(nuplane =>
            {
                nuplane.AddDirectoryFeed("one", "one", feed => feed.Watch = false);
                nuplane.AddDirectoryFeed("two", "two", feed => feed.Watch = false);
            });
        });
    }
}

internal sealed class AdministrationHost : IAsyncDisposable
{
    private readonly bool _keepFiles;
    private WebApplicationBuilder? _builder;
    private AdministrationHost(string? root, bool keepFiles)
    {
        Root = root ?? Directory.CreateTempSubdirectory("studio-admin-").FullName;
        _keepFiles = keepFiles;
    }
    public string Root { get; }
    public WebApplication App { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;
    public string ShellsFile => Path.Combine(Root, "config", "studio-shells.json");
    public const string Credential = "private-feature-credential-sentinel";

    public static async Task<AdministrationHost> StartAsync(Action<NuplaneBuilder>? configureNuplane = null, bool addAdministration = true, bool managedFeatures = false, Action<WebApplicationBuilder>? beforeStudio = null, Action<WebApplicationBuilder>? afterStudio = null, bool nativeStore = false, bool loadPackages = false, Action<WebApplication>? configurePipeline = null, string? root = null, bool keepFiles = false, Action<StudioNuplaneOptions>? configureAdministration = null, Action<IEndpointRouteBuilder>? mapAdministration = null)
    {
        var host = new AdministrationHost(root, keepFiles);
        try
        {
            var builder = host._builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { ContentRootPath = host.Root, EnvironmentName = Environments.Production });
            builder.Logging.ClearProviders();
            builder.WebHost.UseTestServer();
            builder.Configuration.Sources.Clear();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Nuplane:Setup:AutomaticReconciliation"] = "false",
                ["Nuplane:Loading:Enabled"] = loadPackages.ToString()
            });
            if (managedFeatures)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(host.ShellsFile)!);
                await File.WriteAllTextAsync(host.ShellsFile, """{"preserve":"unrelated","CShells":{"Shells":{"Authoring":{"Configuration":{"Widget":{"Limit":7}},"Features":{"StudioApi":{},"Configured":{"Credential":"private-feature-credential-sentinel"}}},"Default":{"Features":{"StudioApi":{}}}}}}""");
            }
            else builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["CShells:Shells:Authoring:Features:StudioApi"] = "true" });
            beforeStudio?.Invoke(builder);
            var studio = builder.AddElsaStudio(options => { options.ShellName = "Authoring"; options.ShellsFile = managedFeatures ? "config/studio-shells.json" : null; });
            if (addAdministration) studio.AddElsaStudioNuplane(configureAdministration, configureNuplane: nuplane =>
            {
                if (nativeStore)
                {
                    var state = Path.Combine(host.Root, "native-store", "state.json");
                    nuplane.WithStateFile(state).UseCoordinatedPackageStore([new("default", "studio", state)], true);
                    nuplane.Services.Configure<FeedResolutionOptions>(options => options.PackageInstallRoot = Path.Combine(host.Root, "native-store", "installs"));
                    nuplane.Services.Configure<LockFileOptions>(options => options.Path = Path.Combine(host.Root, "native-store", "nuplane.lock.json"));
                    nuplane.AddDirectoryFeed("native-feed", "feed", feed => { feed.Watch = false; feed.IncludeAll(); });
                }
                configureNuplane?.Invoke(nuplane);
            });
            afterStudio?.Invoke(builder);
            host.App = builder.Build();
            configurePipeline?.Invoke(host.App);
            host.App.UseElsaStudio();
            host.App.MapElsaStudio();
            if (addAdministration)
            {
                if (mapAdministration is null) host.App.MapElsaStudioNuplane();
                else mapAdministration(host.App);
            }
            if (nativeStore)
            {
                Directory.CreateDirectory(Path.Combine(host.Root, "native-store", "installs"));
                var maintenance = host.App.Services.GetRequiredService<INuplanePackageStoreMaintenanceOperations>();
                var preview = await maintenance.PreviewEnrollmentAsync();
                if (preview.Membership == "Unenrolled")
                {
                    var enrolled = await maintenance.EnrollAsync(new("default", preview.RootIdentity!, 1, true, ["studio"], []));
                    Assert.Equal("Complete", enrolled.Status);
                }
                else Assert.Equal("Complete", preview.Membership);
            }
            await host.App.StartAsync();
            host.Client = host.App.GetTestClient();
            return host;
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Client?.Dispose();
        if (App is not null)
        {
            try { await App.StopAsync(); }
            finally { await App.DisposeAsync(); }
        }
        else if (_builder is not null)
        {
            // Build transfers these resources to the application. A refused composition
            // never makes that transfer, so the fixture must release both owners.
            _builder.Configuration.Dispose();
            (_builder.Environment.ContentRootFileProvider as IDisposable)?.Dispose();
        }
        if (!_keepFiles) Directory.Delete(Root, recursive: true);
    }
}

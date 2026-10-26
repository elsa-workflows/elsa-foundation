using System.Net;
using Elsa.Studio.Hosting;
using Elsa.Studio.Hosting.Nuplane;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Nuplane.Admin;
using Nuplane.Builder;
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
    public string Root { get; } = Directory.CreateTempSubdirectory("studio-admin-").FullName;
    public WebApplication App { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;
    public string ShellsFile => Path.Combine(Root, "config", "studio-shells.json");
    public const string Credential = "private-feature-credential-sentinel";

    public static async Task<AdministrationHost> StartAsync(Action<NuplaneBuilder>? configureNuplane = null, bool addAdministration = true, bool managedFeatures = false, Action<WebApplicationBuilder>? beforeStudio = null, Action<WebApplicationBuilder>? afterStudio = null)
    {
        var host = new AdministrationHost();
        try
        {
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { ContentRootPath = host.Root, EnvironmentName = Environments.Production });
            builder.WebHost.UseTestServer();
            builder.Configuration.Sources.Clear();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Nuplane:Setup:AutomaticReconciliation"] = "false",
                ["Nuplane:Loading:Enabled"] = "false"
            });
            if (managedFeatures)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(host.ShellsFile)!);
                await File.WriteAllTextAsync(host.ShellsFile, """{"preserve":"unrelated","CShells":{"Shells":{"Authoring":{"Configuration":{"Widget":{"Limit":7}},"Features":{"StudioApi":{},"Configured":{"Credential":"private-feature-credential-sentinel"}}},"Default":{"Features":{"StudioApi":{}}}}}}""");
            }
            else builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["CShells:Shells:Authoring:Features:StudioApi"] = "true" });
            beforeStudio?.Invoke(builder);
            var studio = builder.AddElsaStudio(options => { options.ShellName = "Authoring"; options.ShellsFile = managedFeatures ? "config/studio-shells.json" : null; });
            if (addAdministration) studio.AddElsaStudioNuplane(configureNuplane: configureNuplane);
            afterStudio?.Invoke(builder);
            host.App = builder.Build();
            host.App.UseElsaStudio();
            host.App.MapElsaStudio();
            if (addAdministration) host.App.MapElsaStudioNuplane();
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
        if (App is not null) await App.DisposeAsync();
        Directory.Delete(Root, recursive: true);
    }
}

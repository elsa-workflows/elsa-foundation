using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using CShells.Features;
using Elsa.Api.AspNetCore;
using Elsa.Api.Compatibility.Testing.Manifests;
using Elsa.Modularity.Api.ModuleManagement;
using Elsa.Modularity.Core.Contracts;
using Elsa.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Nuplane.Admin;
using Xunit;

namespace Elsa.Modularity.Tests;

public sealed class ModuleManagementApiTests : IDisposable
{
    private const string ApiKey = "custom-host-management-key";
    private const string HostOwner = "CustomElsaHost";
    private const string Api = "/_elsa/module-management";
    private readonly DirectoryInfo _contentRoot = Directory.CreateTempSubdirectory("elsa-module-management-");
    private string SettingsPath => Path.Combine(_contentRoot.FullName, "appsettings.json");

    public void Dispose() => _contentRoot.Delete(recursive: true);

    [Fact]
    public async Task Custom_host_can_read_registry_and_routes_use_configured_owner()
    {
        // Host settings files commonly carry comments (Foundation.Host's does); the registry must read them as ASP.NET Core does.
        await using var app = await StartAsync("""
            {
              // A commented host settings file.
              "Nuplane": {
                "Setup": {
                  "Feeds": [],
                },
              },
            }
            """);

        var registryEndpoint = new EndpointManifestBuilder(app.Services.GetServices<EndpointDataSource>())
            .Build()
            .Entries
            .Single(entry => entry.Route.Value == Api + "/registry");
        Assert.Equal(HostOwner, registryEndpoint.Owner);

        using var client = app.GetTestClient();
        using var authorizedRequest = new HttpRequestMessage(HttpMethod.Get, Api + "/registry");
        authorizedRequest.Headers.Add(ManagementApiKeyAuthentication.HeaderName, ApiKey);
        using var authorizedResponse = await client.SendAsync(authorizedRequest);
        Assert.Equal(HttpStatusCode.OK, authorizedResponse.StatusCode);
        using var body = JsonDocument.Parse(await authorizedResponse.Content.ReadAsStringAsync());
        Assert.Equal(HostOwner, body.RootElement.GetProperty("host").GetProperty("runtime").GetString());

        using var rejectedRequest = new HttpRequestMessage(HttpMethod.Get, Api + "/registry");
        rejectedRequest.Headers.Add(ManagementApiKeyAuthentication.HeaderName, "wrong-key");
        using var rejectedResponse = await client.SendAsync(rejectedRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, rejectedResponse.StatusCode);
    }

    [Fact]
    public async Task Cache_feed_configuration_is_read_only_and_ordinary_feeds_remain_editable()
    {
        // Configuration keys and enum values are case-insensitive in the host.
        const string configuration = """
            { "nuplane": { "setup": { "feeds": [
              { "name": "baked", "directoryPath": "cache", "directory": { "role": "cAcHe", "watch": false } }
            ] } } }
            """;
        await using var app = await StartAsync(configuration);
        using var client = AuthorizedClient(app);
        var feed = new { name = "editable", directoryPath = "drop-zone", includePatterns = new[] { "*" }, directory = new { watch = true } };

        using var updateCache = await client.PutAsJsonAsync(Api + "/feeds/BAKED", feed);
        Assert.Equal(HttpStatusCode.Conflict, updateCache.StatusCode);
        using var deleteCache = await client.DeleteAsync(Api + "/feeds/baked");
        Assert.Equal(HttpStatusCode.Conflict, deleteCache.StatusCode);
        Assert.Equal(configuration, await File.ReadAllTextAsync(SettingsPath));

        using var addEditable = await client.PostAsJsonAsync(Api + "/feeds", feed);
        Assert.Equal(HttpStatusCode.OK, addEditable.StatusCode);
        using var updateEditable = await client.PutAsJsonAsync(Api + "/feeds/editable", new
        {
            feed.name,
            directoryPath = "new-drop-zone",
            feed.includePatterns,
            feed.directory
        });
        Assert.Equal(HttpStatusCode.OK, updateEditable.StatusCode);
        using var registry = await client.GetFromJsonAsync<JsonDocument>(Api + "/registry");
        var feeds = registry!.RootElement.GetProperty("feeds").EnumerateArray().ToArray();
        Assert.Equal("cache", feeds.Single(item => item.GetProperty("name").GetString() == "baked").GetProperty("directoryPath").GetString());
        Assert.Equal("new-drop-zone", feeds.Single(item => item.GetProperty("name").GetString() == "editable").GetProperty("directoryPath").GetString());
        using var deleteEditable = await client.DeleteAsync(Api + "/feeds/editable");
        Assert.Equal(HttpStatusCode.OK, deleteEditable.StatusCode);
        using var saved = JsonDocument.Parse(await File.ReadAllTextAsync(SettingsPath));
        var retained = Assert.Single(saved.RootElement.GetProperty("nuplane").GetProperty("setup").GetProperty("feeds").EnumerateArray());
        Assert.Equal("cAcHe", retained.GetProperty("directory").GetProperty("role").GetString());
    }

    [Fact]
    public async Task Keyed_feeds_are_listed_and_feed_mutations_preserve_the_settings_file()
    {
        const string configuration = """
            {
              // The host accepts keyed declarations without a Name property.
              "nuplane": { "setup": { "feeds": {
                "0": { "Name": "EDITABLE", "DirectoryPath": "shadowed" },
                "baked": { "directoryPath": "cache", "directory": { "role": "Cache" } },
                "editable": { "directoryPath": "drop-zone" },
              } } }
            }
            """;
        await using var app = await StartAsync(configuration);
        using var client = AuthorizedClient(app);
        var newFeed = new { name = "new-feed", directoryPath = "new-folder", includePatterns = new[] { "*" }, directory = new { watch = true } };

        using var registry = await client.GetFromJsonAsync<JsonDocument>(Api + "/registry");
        using var update = await client.PutAsJsonAsync(Api + "/feeds/baked", newFeed);
        using var deletion = await client.DeleteAsync(Api + "/feeds/editable");
        using var add = await client.PostAsJsonAsync(Api + "/feeds", newFeed);

        Assert.Equal(configuration, await File.ReadAllTextAsync(SettingsPath));
        Assert.Equal(HttpStatusCode.Conflict, update.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, deletion.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, add.StatusCode);
        var root = registry!.RootElement;
        Assert.False(root.GetProperty("capabilities").GetProperty("canManageFeeds").GetBoolean());
        Assert.True(root.GetProperty("capabilities").GetProperty("canUploadPackages").GetBoolean());
        var feeds = root.GetProperty("feeds").EnumerateArray().ToArray();
        Assert.Equal(["baked", "editable"], feeds.Select(feed => feed.GetProperty("name").GetString()));
        Assert.Equal("drop-zone", feeds[1].GetProperty("directoryPath").GetString());
    }

    [Theory]
    [InlineData(true, "drop-zone")]
    [InlineData(false, "packages")]
    public async Task Upload_and_drop_folder_deletion_use_an_editable_directory_or_the_default(bool hasEditableFeed, string expectedDirectory)
    {
        const string fileName = "Fixture.1.0.0.nupkg";
        var cache = _contentRoot.CreateSubdirectory("cache");
        var cachedPackage = Path.Combine(cache.FullName, fileName);
        await File.WriteAllTextAsync(cachedPackage, "baked");
        var editable = hasEditableFeed ? """, { "Name": "editable", "DirectoryPath": "drop-zone" }""" : "";
        await using var app = await StartAsync($$"""
            { "Nuplane": { "Setup": { "Feeds": [
              { "Name": "baked", "DirectoryPath": "cache", "Directory": { "Role": "Cache", "Watch": false } }{{editable}}
            ] } } }
            """);
        using var client = AuthorizedClient(app);
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("uploaded"), "package", fileName);
        using var upload = await client.PostAsync(Api + "/packages/upload", form);
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);

        var staged = Path.Combine(_contentRoot.FullName, expectedDirectory, fileName);
        Assert.Equal("uploaded", await File.ReadAllTextAsync(staged));
        Assert.Equal("baked", await File.ReadAllTextAsync(cachedPackage));
        using var deletion = await client.DeleteAsync(Api + "/packages/drop-folder/" + fileName);
        Assert.Equal(HttpStatusCode.OK, deletion.StatusCode);
        Assert.False(File.Exists(staged));
        Assert.Equal("baked", await File.ReadAllTextAsync(cachedPackage));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("drop-zone", false)]
    [InlineData(null, true)]
    [InlineData("drop-zone", true)]
    public async Task Drop_folder_mutations_refuse_a_directory_also_owned_by_a_cache_feed(string? primaryFolder, bool keyed)
    {
        const string fileName = "Fixture.1.0.0.nupkg";
        var directory = primaryFolder ?? "packages";
        var cache = _contentRoot.CreateSubdirectory(directory);
        var cachedPackage = Path.Combine(cache.FullName, fileName);
        await File.WriteAllTextAsync(cachedPackage, "baked");
        var cacheSettings = new JsonObject
        {
            ["DirectoryPath"] = Path.Combine(_contentRoot.FullName, "unused", "..", directory) + Path.DirectorySeparatorChar,
            ["Directory"] = new JsonObject { ["Role"] = "Cache" }
        };
        var primarySettings = new JsonObject { ["DirectoryPath"] = primaryFolder };
        JsonNode feeds;
        if (keyed)
        {
            feeds = primaryFolder is null
                ? new JsonObject { ["baked"] = cacheSettings }
                : new JsonObject { ["baked"] = cacheSettings, ["editable"] = primarySettings };
        }
        else
        {
            cacheSettings["Name"] = "baked";
            primarySettings["Name"] = "editable";
            feeds = primaryFolder is null ? new JsonArray(cacheSettings) : new JsonArray(cacheSettings, primarySettings);
        }
        await using var app = await StartAsync($$"""{ "Nuplane": { "Setup": { "Feeds": {{feeds}} } } }""");
        using var client = AuthorizedClient(app);
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("uploaded"), "package", fileName);
        using var upload = await client.PostAsync(Api + "/packages/upload", form);
        using var deletion = await client.DeleteAsync(Api + "/packages/drop-folder/" + fileName);

        Assert.Equal(HttpStatusCode.Conflict, upload.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, deletion.StatusCode);
        Assert.Equal("baked", await File.ReadAllTextAsync(cachedPackage));
        using var registry = await client.GetFromJsonAsync<JsonDocument>(Api + "/registry");
        Assert.False(registry!.RootElement.GetProperty("capabilities").GetProperty("canUploadPackages").GetBoolean());
        Assert.Empty(registry.RootElement.GetProperty("dropFolderPackages").EnumerateArray());
    }

    private static HttpClient AuthorizedClient(WebApplication app)
    {
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add(ManagementApiKeyAuthentication.HeaderName, ApiKey);
        return client;
    }

    private async Task<WebApplication> StartAsync(string configuration)
    {
        await File.WriteAllTextAsync(SettingsPath, configuration);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = _contentRoot.FullName });
        builder.WebHost.UseTestServer();
        builder.Configuration[ManagementApiKeyAuthentication.ConfigurationKey] = ApiKey;
        builder.Services.AddSingleton<IRuntimeFeatureCatalog>(new FakeRuntimeFeatureCatalog());
        builder.Services.AddSingleton<INuplaneAdminOperations>(new FakeNuplaneAdminOperations());
        builder.Services.AddElsaModuleManagement(options => options.HostOwner = HostOwner);

        var app = builder.Build();
        try
        {
            app.MapElsaModuleManagement();
            await app.StartAsync();
            return app;
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }
}

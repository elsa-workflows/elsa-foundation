using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
using Nuplane.Store.Cleanup;
using Xunit;

namespace Elsa.Modularity.Tests;

public sealed class ModuleManagementApiTests
{
    private const string ApiKey = "custom-host-management-key";
    private const string HostOwner = "CustomElsaHost";

    [Fact]
    public async Task Saved_retention_policy_survives_configuration_read_and_drives_the_next_prune()
    {
        var contentRoot = Directory.CreateTempSubdirectory("elsa-retention-contract-").FullName;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(contentRoot, "appsettings.json"), "{\"Nuplane\":{}}");
            var admin = new FakeNuplaneAdminOperations();
            foreach (var (version, days) in new[] { ("1.0.0", 30), ("2.0.0", 20), ("3.0.0", 10) })
            {
                var directory = Directory.CreateDirectory(Path.Combine(contentRoot, "packages", "sample", version));
                directory.LastWriteTimeUtc = DateTime.UtcNow.AddDays(-days);
                if (version == "3.0.0") admin.Packages.Add(new("sample", version, "local", "drop", directory.FullName,
                    DateTimeOffset.UtcNow, "corr", "graph", "gen", default, [], [], true));
            }
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = contentRoot });
            builder.WebHost.UseTestServer();
            builder.Configuration[ManagementApiKeyAuthentication.ConfigurationKey] = ApiKey;
            builder.Services.AddSingleton<IRuntimeFeatureCatalog>(new FakeRuntimeFeatureCatalog());
            builder.Services.AddSingleton<INuplaneAdminOperations>(admin);
            builder.Services.Configure<CleanupPolicyOptions>(options =>
            { options.RetainLastNVersions = 99; options.RetainYoungerThanDays = 365; options.ProtectLastKnownGood = true; });
            builder.Services.AddElsaModuleManagement(options => options.HostOwner = HostOwner);
            await using var app = builder.Build();
            app.MapElsaModuleManagement();
            await app.StartAsync();
            using var client = app.GetTestClient();
            client.DefaultRequestHeaders.Add(ManagementApiKeyAuthentication.HeaderName, ApiKey);
            using var update = await client.PutAsJsonAsync("/_elsa/module-management/retention-policy", new
            { retainLastNVersions = 1, retainYoungerThanDays = 1, mode = "Automatic", protectLastKnownGood = false });
            update.EnsureSuccessStatusCode();
            using var saved = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(contentRoot, "appsettings.json")));
            Assert.Equal(1, saved.RootElement.GetProperty("Nuplane").GetProperty("CleanupPolicy").GetProperty("RetainLastNVersions").GetInt32());
            using var registry = JsonDocument.Parse(await client.GetStringAsync("/_elsa/module-management/registry"));
            var policy = registry.RootElement.GetProperty("retentionPolicy");
            Assert.Equal(1, policy.GetProperty("retainLastNVersions").GetInt32());
            Assert.Equal(1, policy.GetProperty("retainYoungerThanDays").GetInt32());
            Assert.False(policy.GetProperty("protectLastKnownGood").GetBoolean());
            using var prune = await client.PostAsJsonAsync("/_elsa/module-management/prune", new { dryRun = true });
            prune.EnsureSuccessStatusCode();
            using var result = JsonDocument.Parse(await prune.Content.ReadAsStringAsync());
            var decisions = result.RootElement.GetProperty("decisions").EnumerateArray().ToArray();
            Assert.Equal(2, decisions.Count(row => row.GetProperty("action").GetString() == "would-delete"));
            Assert.All(decisions, row => Assert.True(Directory.Exists(row.GetProperty("path").GetString())));
        }
        finally { Directory.Delete(contentRoot, recursive: true); }
    }

    [Fact]
    public async Task Custom_host_can_read_registry_and_routes_use_configured_owner()
    {
        // Host settings files commonly carry comments (Foundation.Host's does); the registry must read them as ASP.NET Core does.
        var contentRoot = Directory.CreateTempSubdirectory("elsa-module-management-").FullName;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(contentRoot, "appsettings.json"), """
                {
                  // A commented host settings file.
                  "Nuplane": {
                    "Setup": {
                      "Feeds": [],
                    },
                  },
                }
                """);
            await AssertCustomHostAsync(contentRoot);
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    private static async Task AssertCustomHostAsync(string contentRoot)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = contentRoot });
        builder.WebHost.UseTestServer();
        builder.Configuration[ManagementApiKeyAuthentication.ConfigurationKey] = ApiKey;
        builder.Services.AddSingleton<IRuntimeFeatureCatalog>(new FakeRuntimeFeatureCatalog());
        builder.Services.AddSingleton<INuplaneAdminOperations>(new FakeNuplaneAdminOperations());
        builder.Services.AddElsaModuleManagement(options => options.HostOwner = HostOwner);

        await using var app = builder.Build();
        app.MapElsaModuleManagement();
        await app.StartAsync();

        var registryEndpoint = new EndpointManifestBuilder(app.Services.GetServices<EndpointDataSource>())
            .Build()
            .Entries
            .Single(entry => entry.Route.Value == "/_elsa/module-management/registry");
        Assert.Equal(HostOwner, registryEndpoint.Owner);

        using var client = app.GetTestClient();
        using var authorizedRequest = new HttpRequestMessage(HttpMethod.Get, "/_elsa/module-management/registry");
        authorizedRequest.Headers.Add(ManagementApiKeyAuthentication.HeaderName, ApiKey);
        using var authorizedResponse = await client.SendAsync(authorizedRequest);
        Assert.Equal(HttpStatusCode.OK, authorizedResponse.StatusCode);
        using var body = JsonDocument.Parse(await authorizedResponse.Content.ReadAsStringAsync());
        Assert.Equal(HostOwner, body.RootElement.GetProperty("host").GetProperty("runtime").GetString());

        using var rejectedRequest = new HttpRequestMessage(HttpMethod.Get, "/_elsa/module-management/registry");
        rejectedRequest.Headers.Add(ManagementApiKeyAuthentication.HeaderName, "wrong-key");
        using var rejectedResponse = await client.SendAsync(rejectedRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, rejectedResponse.StatusCode);
    }
}

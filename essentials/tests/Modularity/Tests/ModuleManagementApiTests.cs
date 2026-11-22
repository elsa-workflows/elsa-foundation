using System.Net;
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
using Xunit;

namespace Elsa.Modularity.Tests;

public sealed class ModuleManagementApiTests
{
    private const string ApiKey = "custom-host-management-key";
    private const string HostOwner = "CustomElsaHost";

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
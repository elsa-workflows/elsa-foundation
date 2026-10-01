using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Elsa.ExtensionBuilder.Api.Extensions;
using Elsa.Modularity.Api.Authorization;
using Elsa.Modularity.Core.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nuplane.Admin;
using Xunit;

namespace Elsa.ExtensionBuilder.Api.Tests;

/// <summary>
/// The HTTP surface as the Studio relay sees it (studio ADR 0037): the routes under <c>/_elsa/extension-builder</c>, the
/// management-key filter in front of every one of them, and the trusted-caller filter in front of every one but
/// <c>/capabilities</c>. The surface is pinned because the relay's allowlist names these routes, and a mapper edit that
/// dropped or renamed one would otherwise surface only as a Studio page that stopped working.
/// </summary>
public sealed class ExtensionBuilderApiTests : IAsyncDisposable
{
    private const string ManagementKey = "extension-builder-api-tests-key";
    private const string Prefix = "/_elsa/extension-builder";

    /// <summary>Every route, as <c>METHOD template</c> under <see cref="Prefix"/>: the 42 that #1635 removed and #2294 restored.</summary>
    private static readonly string[] Routes =
    [
        "GET /capabilities",
        "GET /templates",
        "GET /repositories",
        "POST /repositories/server-local",
        "POST /repositories/clone",
        "GET /workspaces",
        "POST /workspaces",
        "GET /workspaces/{workspaceId}",
        "DELETE /workspaces/{workspaceId}",
        "GET /workspaces/{workspaceId}/working-copies",
        "POST /workspaces/{workspaceId}/working-copies/select",
        "GET /workspaces/{workspaceId}/repository-tree",
        "GET /workspaces/{workspaceId}/files/{*path}",
        "PUT /workspaces/{workspaceId}/files/{*path}",
        "DELETE /workspaces/{workspaceId}/files/{*path}",
        "POST /workspaces/{workspaceId}/files/move",
        "POST /workspaces/{workspaceId}/templates/apply",
        "GET /workspaces/{workspaceId}/source-control/status",
        "GET /workspaces/{workspaceId}/source-control/diff/{*path}",
        "POST /workspaces/{workspaceId}/source-control/stage",
        "POST /workspaces/{workspaceId}/source-control/unstage",
        "POST /workspaces/{workspaceId}/source-control/stage-all",
        "POST /workspaces/{workspaceId}/source-control/commit",
        "POST /workspaces/{workspaceId}/source-control/push",
        "POST /workspaces/{workspaceId}/source-control/pull",
        "POST /workspaces/{workspaceId}/builds",
        "POST /workspaces/{workspaceId}/projects",
        "GET /projects/{projectId}",
        "DELETE /projects/{projectId}",
        "GET /projects/{projectId}/files",
        "GET /projects/{projectId}/files/{*path}",
        "PUT /projects/{projectId}/files/{*path}",
        "DELETE /projects/{projectId}/files/{*path}",
        "POST /projects/{projectId}/builds",
        "GET /projects/{projectId}/runtime-status",
        "POST /projects/{projectId}/rollback",
        "POST /projects/{projectId}/retry-reconcile",
        "GET /builds/{buildId}",
        "GET /builds/{buildId}/log",
        "GET /builds/{buildId}/artifact",
        "POST /builds/{buildId}/promote",
        "POST /builds/{buildId}/artifacts/{artifactId}/promote"
    ];

    private readonly string _directory = Directory.CreateTempSubdirectory("elsa-extension-builder-api-").FullName;
    private readonly List<WebApplication> _apps = [];

    [Fact]
    public async Task Maps_exactly_the_routes_the_studio_relay_names()
    {
        var app = await StartAsync(ManagementKey);

        var routes = app.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => $"{string.Join('|', endpoint.Metadata.GetRequiredMetadata<HttpMethodMetadata>().HttpMethods)} {endpoint.RoutePattern.RawText![Prefix.Length..]}");

        Assert.Equal(Routes.Order(StringComparer.Ordinal), routes.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(null, "anything", HttpStatusCode.NotFound)]
    [InlineData(ManagementKey, null, HttpStatusCode.Unauthorized)]
    [InlineData(ManagementKey, "wrong-key", HttpStatusCode.Unauthorized)]
    public async Task Every_route_answers_only_a_matching_management_key(string? configuredKey, string? providedKey, HttpStatusCode expected)
    {
        var client = Client(await StartAsync(configuredKey), providedKey);

        using var capabilities = await client.GetAsync($"{Prefix}/capabilities");
        using var templates = await client.GetAsync($"{Prefix}/templates");

        Assert.Equal(expected, capabilities.StatusCode);
        Assert.Equal(expected, templates.StatusCode);
    }

    [Fact]
    public async Task The_management_key_is_a_trusted_caller_with_every_capability()
    {
        var client = Client(await StartAsync(ManagementKey), ManagementKey);

        var capabilities = await client.GetFromJsonAsync<JsonElement>($"{Prefix}/capabilities");
        using var templates = await client.GetAsync($"{Prefix}/templates");

        Assert.Equal(
            ["canBuild", "canCreateWorkspace", "canEditFiles", "canPromote", "canRollback"],
            capabilities.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.All(capabilities.EnumerateObject(), property => Assert.True(property.Value.GetBoolean()));
        Assert.Equal(HttpStatusCode.OK, templates.StatusCode);
    }

    /// <summary>
    /// The key alone opens <c>/capabilities</c>, which then says what the caller may do; everything else needs the trust the
    /// first configured role lends the key's principal, so a host with no trusted role refuses the rest.
    /// </summary>
    [Fact]
    public async Task Without_a_trusted_role_the_management_key_reads_capabilities_and_nothing_else()
    {
        var client = Client(await StartAsync(ManagementKey, trustedRoles: []), ManagementKey);

        var capabilities = await client.GetFromJsonAsync<JsonElement>($"{Prefix}/capabilities");
        using var templates = await client.GetAsync($"{Prefix}/templates");

        Assert.All(capabilities.EnumerateObject(), property => Assert.False(property.Value.GetBoolean()));
        Assert.Equal(HttpStatusCode.Forbidden, templates.StatusCode);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var app in _apps)
            await app.DisposeAsync();
        Directory.Delete(_directory, recursive: true);
    }

    private async Task<WebApplication> StartAsync(string? managementKey, string[]? trustedRoles = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = _directory, EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [ManagementApiKeyAuthentication.ConfigurationKey] = managementKey,
            ["Elsa:ExtensionBuilder:StoragePath"] = Path.Combine(_directory, "state")
        });
        builder.Services.AddSingleton<INuplaneAdminOperations>(new FakeNuplaneAdmin());
        builder.Services.AddSingleton<IFeatureManagementService>(new FakeFeatureManagement());
        builder.Services.AddElsaExtensionBuilder(builder.Configuration);
        if (trustedRoles is not null)
            builder.Services.PostConfigure<ExtensionBuilderOptions>(options => options.TrustedRoles = trustedRoles);

        var app = builder.Build();
        _apps.Add(app);
        app.MapElsaExtensionBuilderApi();
        await app.StartAsync();
        return app;
    }

    private static HttpClient Client(WebApplication app, string? managementKey)
    {
        var client = app.GetTestClient();
        if (managementKey is not null)
            client.DefaultRequestHeaders.Add(ManagementApiKeyAuthentication.HeaderName, managementKey);
        return client;
    }
}

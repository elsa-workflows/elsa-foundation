using Elsa.Activities.Http;
using Elsa.Api.Capabilities.Contracts;
using Elsa.Api.Capabilities.Extensions;
using Elsa.Api.Capabilities.Models;
using Elsa.Activities.Http.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Http.Tests;

public sealed class HttpWorkflowEndpointsCapabilitySourceTests
{
    [Fact]
    public async Task ActivitiesHttpFeature_advertises_its_default_workflow_endpoint_base_path()
    {
        var document = await GetDocumentAsync();

        var capability = Assert.Single(document.Capabilities);
        Assert.Equal("elsa.http.workflow-endpoints", capability.Id);
        Assert.Equal("1", capability.ContractVersion);
        var link = Assert.Single(capability.Links);
        Assert.Equal("workflow-http-base", link.Rel);
        Assert.Equal("workflows/http", link.Href);
    }

    [Theory]
    [InlineData("/tenant/workflows/", "tenant/workflows")]
    [InlineData("/werkstromen/één/", "werkstromen/één")]
    public async Task ActivitiesHttpFeature_advertises_a_custom_base_path_without_outer_slashes(string basePath, string expectedHref)
    {
        var document = await GetDocumentAsync(featureBasePath: basePath);

        Assert.Equal(expectedHref, Assert.Single(Assert.Single(document.Capabilities).Links).Href);
    }

    [Fact]
    public async Task Capability_uses_the_effective_options_after_host_overrides()
    {
        var document = await GetDocumentAsync(hostBasePathOverride: "/gateway/hooks/");

        Assert.Equal("gateway/hooks", Assert.Single(Assert.Single(document.Capabilities).Links).Href);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("////")]
    public async Task Disabled_base_path_does_not_advertise_a_capability(string basePath)
    {
        var document = await GetDocumentAsync(hostBasePathOverride: basePath);

        Assert.Empty(document.Capabilities);
    }

    [Fact]
    public async Task Capability_source_is_only_registered_when_ActivitiesHttp_is_selected()
    {
        var document = await GetDocumentAsync(includeFeature: false);

        Assert.Empty(document.Capabilities);
    }

    [Theory]
    [InlineData("https://example.test/workflows")]
    [InlineData("//example.test/workflows")]
    [InlineData("/https:example.test/workflows")]
    [InlineData("/workflows/../runtime")]
    [InlineData("/workflows/%2e%2e/runtime")]
    [InlineData("/workflows/%252e%252e/runtime")]
    [InlineData("/workflows/api%2fadmin")]
    [InlineData("/workflows/%ZZ")]
    public async Task Invalid_origin_or_traversal_base_path_is_rejected(string basePath)
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await GetDocumentAsync(hostBasePathOverride: basePath));

        Assert.Contains("HttpEndpointOptions.BasePath", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/%61pi", "/api")]
    [InlineData("/workflow%73/http", "/workflows/http")]
    public async Task Encoded_mount_cannot_advertise_an_alias_of_a_different_pipeline_path(string basePath, string decodedPath)
    {
        // ASP.NET decodes the request path before the middleware compares it with the configured mount.
        Assert.Equal(decodedPath, PathString.FromUriComponent(basePath).Value);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await GetDocumentAsync(hostBasePathOverride: basePath));
    }

    private static async Task<ApiCapabilitiesDocument> GetDocumentAsync(
        string featureBasePath = "/workflows/http",
        string? hostBasePathOverride = null,
        bool includeFeature = true)
    {
        var services = new ServiceCollection();
        services.AddApiCapabilities();
        if (includeFeature)
            new ActivitiesHttpFeature { BasePath = featureBasePath }.ConfigureServices(services);
        if (hostBasePathOverride is not null)
            services.Configure<HttpEndpointOptions>(options => options.BasePath = hostBasePathOverride);

        await using var provider = services.BuildServiceProvider();
        return await provider.GetRequiredService<IApiCapabilityCatalog>().GetAsync();
    }
}

using Elsa.Activities.Http;
using Elsa.Api.Capabilities.Contracts;
using Elsa.Api.Capabilities.Extensions;
using Elsa.Api.Capabilities.Models;
using Elsa.Activities.Http.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

        var href = Assert.Single(Assert.Single(document.Capabilities).Links).Href;
        Assert.Equal(expectedHref, href);
        Assert.Equal($"/{basePath.Trim('/')}", PathString.FromUriComponent($"/{href}").Value);
    }

    [Theory]
    [InlineData("/tenant/workflow endpoints/", "tenant/workflow%20endpoints")]
    [InlineData("/api:v1/workflows", "api%3Av1/workflows")]
    [InlineData("/https:example.test/workflows", "https%3Aexample.test/workflows")]
    public async Task ActivitiesHttpFeature_escapes_literal_segments_that_would_not_resolve_as_relative_links(
        string basePath,
        string expectedHref)
    {
        var document = await GetDocumentAsync(featureBasePath: basePath);

        var href = Assert.Single(Assert.Single(document.Capabilities).Links).Href;
        Assert.Equal(expectedHref, href);
        Assert.Equal($"/{basePath.Trim('/')}", PathString.FromUriComponent($"/{href}").Value);
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
    [InlineData("/workflows?x=1")]
    [InlineData("/workflows#fragment")]
    [InlineData("/workflows\\admin")]
    [InlineData("/api//v2")]
    [InlineData("/workflows/../runtime")]
    [InlineData("/workflows/%2e%2e/runtime")]
    [InlineData("/workflows/%252e%252e/runtime")]
    [InlineData("/workflows/api%2fadmin")]
    [InlineData("/workflows/%ZZ")]
    public async Task Invalid_origin_or_traversal_base_path_omits_the_capability(string basePath)
    {
        var document = await GetDocumentAsync(hostBasePathOverride: basePath);

        Assert.DoesNotContain(document.Capabilities, capability => capability.Id == "elsa.http.workflow-endpoints");
    }

    [Fact]
    public async Task Invalid_base_path_is_omitted_with_a_warning_without_blocking_other_capabilities()
    {
        var logs = new CapturingLoggerProvider();
        var services = new ServiceCollection();
        services.AddApiCapabilities();
        services.AddLogging(builder => builder.AddProvider(logs));
        services.AddApiCapability(new ApiCapabilityDeclaration(
            "elsa.test.other",
            1,
            [new ApiCapabilityLink("other", "other")],
            "OtherFeature"));
        new ActivitiesHttpFeature().ConfigureServices(services);
        services.Configure<HttpEndpointOptions>(options => options.BasePath = "/workflows/../runtime");

        await using var provider = services.BuildServiceProvider();
        var document = await provider.GetRequiredService<IApiCapabilityCatalog>().GetAsync();

        Assert.Contains(document.Capabilities, capability => capability.Id == "elsa.test.other");
        Assert.DoesNotContain(document.Capabilities, capability => capability.Id == "elsa.http.workflow-endpoints");
        Assert.Single(logs.Warnings);
    }

    [Theory]
    [InlineData("/%61pi", "/api")]
    [InlineData("/workflow%73/http", "/workflows/http")]
    public async Task Encoded_mount_cannot_advertise_an_alias_of_a_different_pipeline_path(string basePath, string decodedPath)
    {
        // ASP.NET decodes the request path before the middleware compares it with the configured mount.
        Assert.Equal(decodedPath, PathString.FromUriComponent(basePath).Value);

        var document = await GetDocumentAsync(hostBasePathOverride: basePath);

        Assert.DoesNotContain(document.Capabilities, capability => capability.Id == "elsa.http.workflow-endpoints");
    }

    private static async Task<ApiCapabilitiesDocument> GetDocumentAsync(
        string featureBasePath = "/workflows/http",
        string? hostBasePathOverride = null,
        bool includeFeature = true)
    {
        var services = new ServiceCollection();
        services.AddApiCapabilities();
        services.AddLogging();
        if (includeFeature)
            new ActivitiesHttpFeature { BasePath = featureBasePath }.ConfigureServices(services);
        if (hostBasePathOverride is not null)
            services.Configure<HttpEndpointOptions>(options => options.BasePath = hostBasePathOverride);

        await using var provider = services.BuildServiceProvider();
        return await provider.GetRequiredService<IApiCapabilityCatalog>().GetAsync();
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<LogLevel> Warnings { get; } = [];

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(CapturingLoggerProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel == LogLevel.Warning;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (logLevel == LogLevel.Warning)
                    owner.Warnings.Add(logLevel);
            }
        }
    }
}

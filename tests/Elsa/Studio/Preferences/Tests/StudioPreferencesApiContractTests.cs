using Elsa.Api.AspNetCore;
using Elsa.Api.Compatibility.Testing.Baselines;
using Elsa.Api.Compatibility.Testing.OpenApi;
using Elsa.Api.Compatibility.Testing.Serialization;
using CShells.AspNetCore.Features;
using Elsa.Studio.Preferences.Api;
using Elsa.Studio.Preferences.Tests.Support;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Studio.Preferences.Tests;

public sealed class StudioPreferencesApiContractTests
{
    private static readonly string HttpBaselinePath = Path.Join(
        AppContext.BaseDirectory, "Baselines", "studio-preferences-http-fastendpoints.json");
    private static readonly string OpenApiBaselinePath = Path.Join(
        AppContext.BaseDirectory, "Baselines", "studio-preferences-openapi-fastendpoints.json");

    [Fact]
    public void Committed_fastendpoints_http_before_baseline_is_complete_and_stable()
    {
        var expected = BaselineFile.Load<Elsa.Api.Compatibility.Testing.Http.HttpCompatibilityObservation[]>(HttpBaselinePath);

        Assert.Equal(StudioPreferencesCompatibilityCases.All.Count, expected.Length);
        Assert.Equal(
            StudioPreferencesCompatibilityCases.All.Select(testCase => testCase.Endpoint + "|" + testCase.Case).Order(),
            expected.Select(observation => observation.Endpoint + "|" + observation.Case).Order());
        Assert.All(expected, observation =>
        {
            Assert.NotEmpty(observation.Binding);
            Assert.InRange(observation.StatusCode, 200, 599);
            Assert.Equal(observation.StatusCode.ToString(), observation.Status);
        });
    }

    [Fact]
    public void Committed_consumed_openapi_before_baseline_has_exactly_the_two_legacy_operations()
    {
        var expected = BaselineFile.Load<OpenApiEvidenceDocument>(OpenApiBaselinePath);

        Assert.Equal(2, expected.Operations.Count);
        Assert.Equal(
            ["GET /_elsa/studio/preferences/{param}", "PUT /_elsa/studio/preferences/{param}"],
            expected.Operations.Select(operation => operation.Endpoint.ToString()).Order(StringComparer.Ordinal));
        Assert.All(expected.Operations, operation =>
        {
            Assert.NotEmpty(operation.RequestBody);
            Assert.NotEmpty(operation.Responses);
            Assert.NotEmpty(operation.Schemas);
        });
    }

    [Fact]
    public void Committed_legacy_manifest_cases_pin_the_two_routes_and_methods()
    {
        var expected = BaselineFile.Load<Elsa.Api.Compatibility.Testing.Http.HttpCompatibilityObservation[]>(HttpBaselinePath);
        var endpoints = expected.Select(observation => observation.Endpoint).Distinct()
            .OrderBy(endpoint => endpoint.ToString(), StringComparer.Ordinal).ToArray();

        Assert.Equal(
            ["GET /_elsa/studio/preferences/{param}", "PUT /_elsa/studio/preferences/{param}"],
            endpoints.Select(endpoint => endpoint.ToString()).Order(StringComparer.Ordinal));
        Assert.Contains(expected, observation => observation.Case == "exact-read" && observation.StatusCode == 200);
        Assert.Contains(expected, observation => observation.Case == "exact-write" && observation.StatusCode == 200);
        Assert.Contains(expected, observation => observation.Case == "denied" && observation.StatusCode == 403);
    }

    [Fact]
    public void Ten_legacy_baseline_reads_are_byte_identical()
    {
        var captures = new List<string>();
        for (var index = 0; index < 10; index++)
        {
            captures.Add(CompatibilityJson.Serialize(
                BaselineFile.Load<Elsa.Api.Compatibility.Testing.Http.HttpCompatibilityObservation[]>(HttpBaselinePath)));
        }

        Assert.NotEmpty(captures);
        Assert.All(captures, capture => Assert.Equal(captures[0], capture));
    }

    [Fact]
    public void Target_feature_publishes_exactly_one_minimal_get_and_put_through_the_standard_shell_seam()
    {
        Assert.True(typeof(IWebShellFeature).IsAssignableFrom(typeof(StudioPreferencesApiFeature)));

        using var services = new ServiceCollection()
            .AddRouting()
            .BuildServiceProvider();
        var routes = new TestEndpointRouteBuilder(services);
        var feature = new StudioPreferencesApiFeature();
        var mapEndpoints = typeof(StudioPreferencesApiFeature).GetMethod(
            nameof(IWebShellFeature.MapEndpoints),
            [typeof(IEndpointRouteBuilder), typeof(Microsoft.Extensions.Hosting.IHostEnvironment)]);

        Assert.NotNull(mapEndpoints);
        mapEndpoints.Invoke(feature, [routes, null]);

        var endpoints = routes.DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText == "/_elsa/studio/preferences/{namespace}")
            .OrderBy(GetMethod, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["GET", "PUT"], endpoints.Select(GetMethod).ToArray());
        Assert.All(endpoints, endpoint =>
        {
            var owner = Assert.IsType<EndpointOwnershipMetadata>(endpoint.Metadata.GetMetadata<EndpointOwnershipMetadata>());
            Assert.Equal(EndpointOwnerKind.Module, owner.Kind);
            Assert.Equal(typeof(StudioPreferencesApiFeature).Assembly.GetName().Name, owner.OwnerId);
            Assert.Equal(
                EndpointAuthoringModels.MinimalApi,
                endpoint.Metadata.GetMetadata<EndpointAuthoringMetadata>()?.Model);
        });
    }

    private sealed class TestEndpointRouteBuilder(IServiceProvider serviceProvider) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider { get; } = serviceProvider;
        public ICollection<EndpointDataSource> DataSources { get; } = [];
        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);
    }

    private static string GetMethod(RouteEndpoint endpoint) =>
        endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Single()
        ?? throw new InvalidOperationException($"Endpoint '{endpoint.DisplayName}' has no HTTP method metadata.");

}

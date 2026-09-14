using Elsa.Api.AspNetCore;
using CShells.AspNetCore.Features;
using Elsa.Studio.Preferences.Api;
using Elsa.Studio.Preferences.Tests.Support;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Elsa.Testing;

namespace Elsa.Studio.Preferences.Tests;

public sealed class StudioPreferencesApiContractTests
{
    [Fact]
    public void Target_feature_publishes_exactly_one_minimal_get_and_put_through_the_standard_shell_seam()
    {
        Assert.True(typeof(IWebShellFeature).IsAssignableFrom(typeof(StudioPreferencesApiFeature)));

        using var services = new ServiceCollection()
            .AddRouting()
            .AddElsaEndpoints().BuildServiceProvider();
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

    private static string GetMethod(RouteEndpoint endpoint) =>
        endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Single()
        ?? throw new InvalidOperationException($"Endpoint '{endpoint.DisplayName}' has no HTTP method metadata.");

}

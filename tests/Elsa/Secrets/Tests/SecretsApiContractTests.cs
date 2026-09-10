using CShells.AspNetCore.Features;
using Elsa.Api.AspNetCore;
using Elsa.Api.Compatibility.Testing.Manifests;
using Elsa.Secrets.Api.Features;
using Elsa.Secrets.Core.Permissions;
using Elsa.Secrets.Tests.Support;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Secrets.Tests;

public sealed class SecretsApiContractTests
{
    private static readonly IReadOnlyDictionary<string, string> ExpectedPermissions =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GET /secrets"] = SecretsPermissions.Read,
            ["POST /secrets"] = SecretsPermissions.Write,
            ["GET /secrets/descriptors"] = SecretsPermissions.Read,
            ["POST /secrets/picker"] = SecretsPermissions.Read,
            ["DELETE /secrets/{param}"] = SecretsPermissions.Delete,
            ["GET /secrets/{param}"] = SecretsPermissions.Read,
            ["PUT /secrets/{param}"] = SecretsPermissions.Write,
            ["POST /secrets/{param}/revoke"] = SecretsPermissions.Delete,
            ["POST /secrets/{param}/rotate"] = SecretsPermissions.UpdateValue,
            ["POST /secrets/{param}/test"] = SecretsPermissions.Test
        };

    [Fact]
    public void Target_feature_exposes_one_explicit_ten_route_minimal_api_mapper()
    {
        Assert.True(typeof(IWebShellFeature).IsAssignableFrom(typeof(SecretsApiFeature)));

        using var services = new ServiceCollection().AddRouting().AddElsaEndpoints().BuildServiceProvider();
        var routes = new TestEndpointRouteBuilder(services);
        var feature = new SecretsApiFeature();
        var mapEndpoints = typeof(SecretsApiFeature).GetMethod(
            nameof(IWebShellFeature.MapEndpoints),
            [typeof(IEndpointRouteBuilder), typeof(Microsoft.Extensions.Hosting.IHostEnvironment)]);

        Assert.NotNull(mapEndpoints);
        mapEndpoints.Invoke(feature, [routes, null]);
        var endpoints = routes.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/secrets", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.Equal(10, endpoints.Length);
        Assert.Equal(ExpectedPermissions.Keys.Order(StringComparer.Ordinal),
            endpoints.Select(endpoint =>
            {
                var method = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Single();
                var route = new Elsa.Api.Compatibility.Testing.Manifests.NormalizedRoute(endpoint.RoutePattern.RawText!);
                return $"{method} {route.Value}";
            }).Order(StringComparer.Ordinal));
        Assert.All(endpoints, endpoint =>
        {
            Assert.Equal("Elsa.Secrets.Api", endpoint.Metadata.GetMetadata<EndpointOwnershipMetadata>()?.OwnerId);
            Assert.Equal(EndpointAuthoringModels.MinimalApi,
                endpoint.Metadata.GetMetadata<EndpointAuthoringMetadata>()?.Model);
        });
    }

    private sealed class TestEndpointRouteBuilder(IServiceProvider serviceProvider) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider { get; } = serviceProvider;
        public ICollection<EndpointDataSource> DataSources { get; } = [];
        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);
    }
}

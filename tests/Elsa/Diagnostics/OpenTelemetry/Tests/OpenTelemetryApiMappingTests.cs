using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Api.AspNetCore;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Endpoints;
using Elsa.Diagnostics.OpenTelemetry.Extensions;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elsa.Diagnostics.OpenTelemetry.Tests;

public sealed class OpenTelemetryApiMappingTests
{
    [Fact]
    public void Maps_eleven_query_stream_and_otlp_routes_with_owner_metadata()
    {
        var services = new ServiceCollection();
        services.AddRouting();
        services.AddFoundationIdentityAbstractions();
        new OpenTelemetryFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        var builder = new TestRouteBuilder(provider);

        new OpenTelemetryFeature().MapEndpoints(builder, null);

        var routes = builder.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToArray();
        Assert.Equal(11, routes.Length);
        Assert.Equal(11, routes.Select(route => route.RoutePattern.RawText).Distinct(StringComparer.Ordinal).Count());
        Assert.All(routes, route => Assert.Equal("Elsa.Diagnostics.OpenTelemetry", route.Metadata.GetMetadata<EndpointOwnershipMetadata>()?.Owner));
        Assert.All(routes, route => Assert.Equal(EndpointAuthoringModels.MinimalApi, route.Metadata.GetMetadata<EndpointAuthoringMetadata>()?.Model));
        Assert.All(routes, route => Assert.NotNull(route.Metadata.GetMetadata<EndpointNameMetadata>()));
        Assert.All(routes.Where(route => !route.RoutePattern.RawText!.StartsWith("/elsa/otlp/v1/", StringComparison.Ordinal)), route =>
        {
            var policy = Assert.Single(route.Metadata.GetOrderedMetadata<AuthorizeAttribute>());
            var parsed = new PermissionPolicyCodec().Parse(policy.Policy!);
            Assert.Equal(["DIAGNOSTICS:OPENTELEMETRY.READ"], parsed.Descriptor!.Permissions);
        });
        Assert.Equal(3, routes.Count(route => route.RoutePattern.RawText!.StartsWith("/elsa/otlp/v1/", StringComparison.Ordinal)));
        Assert.All(routes.Where(route => route.RoutePattern.RawText!.StartsWith("/elsa/otlp/v1/", StringComparison.Ordinal)), route =>
        {
            var security = route.Metadata.GetMetadata<Elsa.Api.AspNetCore.EndpointSecurityDispositionMetadata>();
            Assert.NotNull(security);
            Assert.Equal(Elsa.Api.AspNetCore.EndpointSecurityDispositionKind.HostCredential, security.Kind);
            Assert.Equal("Elsa.Diagnostics.OpenTelemetry", security.Owner);
            Assert.Null(route.Metadata.GetMetadata<Elsa.Api.AspNetCore.EndpointSecurityDispositionMetadata>()?.Category);
        });
    }

    [Fact]
    public void Maps_otlp_routes_with_http_and_openapi_metadata()
    {
        var services = new ServiceCollection();
        services.AddRouting();
        services.AddFoundationIdentityAbstractions();
        new OpenTelemetryFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        var builder = new TestRouteBuilder(provider);
        builder.MapOpenTelemetryOtlpReceiver();
        var routes = builder.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToArray();
        Assert.Equal(3, routes.Length);
        Assert.All(routes, route => Assert.Contains(HttpMethods.Post, route.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []));
        Assert.All(routes, route => Assert.NotNull(route.Metadata.GetMetadata<IEndpointNameMetadata>()));
    }

    private sealed class TestRouteBuilder(IServiceProvider services) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider { get; } = services;
        public ICollection<EndpointDataSource> DataSources { get; } = [];
        public Microsoft.AspNetCore.Builder.IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);
    }
}

using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Api;
using Elsa.Foundation.Identity.Api.Models;
using Elsa.Foundation.Identity.AspNetCoreIdentity;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Endpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Foundation.Identity.Tests.Api;

public sealed class MinimalIdentityEndpointMetadataTests
{
    [Fact]
    public async Task The_two_identity_owners_expose_exactly_nine_minimal_api_endpoints()
    {
        using var host = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services => services.AddRouting().AddElsaEndpoints());
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        FoundationIdentityApi.MapFoundationIdentityApi(endpoints);
                        AspNetCoreIdentityApi.MapAspNetCoreIdentityApi(endpoints);
                    });
                });
            })
            .Build();

        await host.StartAsync();

        var endpoints = host.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<EndpointAuthoringMetadata>()?.Model == EndpointAuthoringModels.MinimalApi)
            .ToArray();

        Assert.Equal(9, endpoints.Length);

        Assert.Equal(
            new[]
            {
                "AspNetCoreIdentityLogin",
                "AspNetCoreIdentityLoginPage",
                "FoundationIdentityBootstrap",
                "FoundationIdentityCapabilities",
                "FoundationIdentityChallenge",
                "FoundationIdentityLogout",
                "FoundationIdentityRefresh",
                "FoundationIdentitySession",
                "FoundationIdentityToken"
            },
            endpoints.Select(endpoint => endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName)
                .OrderBy(name => name, StringComparer.Ordinal));
        Assert.All(endpoints, endpoint => Assert.Equal(["Identity"], endpoint.Metadata.GetMetadata<ITagsMetadata>()?.Tags));

        var routes = endpoints
            .Select(endpoint => (Route: endpoint.RoutePattern.RawText ?? string.Empty, Methods: endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Single() ?? string.Empty))
            .OrderBy(endpoint => endpoint.Route, StringComparer.Ordinal)
            .ThenBy(endpoint => endpoint.Methods, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                ("/_elsa/identity/bootstrap", "GET"),
                ("/_elsa/identity/capabilities", "GET"),
                ("/_elsa/identity/challenge/{provider}", "GET"),
                ("/_elsa/identity/login", "GET"),
                ("/_elsa/identity/login", "POST"),
                ("/_elsa/identity/logout/{provider}", "POST"),
                ("/_elsa/identity/refresh", "POST"),
                ("/_elsa/identity/session", "GET"),
                ("/_elsa/identity/token", "GET")
            },
            routes);

        var publicEndpoints = endpoints
            .Where(endpoint => endpoint.Metadata.GetMetadata<EndpointSecurityDispositionMetadata>()?.Kind == EndpointSecurityDispositionKind.Public)
            .ToArray();
        Assert.Equal(8, publicEndpoints.Length);
        Assert.All(publicEndpoints, endpoint => Assert.NotNull(endpoint.Metadata.GetMetadata<AllowAnonymousAttribute>()));

        var capabilities = Assert.Single(endpoints, endpoint => endpoint.RoutePattern.RawText == "/_elsa/identity/capabilities");
        var permission = Assert.Single(capabilities.Metadata.OfType<EndpointSecurityDispositionMetadata>());
        Assert.Equal(EndpointSecurityDispositionKind.Permission, permission.Kind);
        var policy = new PermissionPolicyCodec().Parse(permission.Value!);
        Assert.Equal(PermissionPolicyParseStatus.Valid, policy.Status);
        Assert.Equal(
            [PermissionKey.Normalize(DefaultIdentityPermissionKeys.IdentityProvidersRead)],
            policy.Descriptor!.Permissions);
        Assert.NotNull(capabilities.Metadata.GetMetadata<AuthorizeAttribute>());

        var owners = endpoints
            .GroupBy(endpoint => endpoint.Metadata.GetMetadata<EndpointOwnershipMetadata>()?.OwnerId ?? string.Empty, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var foundationOwner = typeof(FoundationIdentityApiFeature).Assembly.GetName().Name
            ?? throw new InvalidOperationException("The Foundation Identity API assembly has no name.");
        var aspNetCoreOwner = typeof(AspNetCoreIdentityFeature).Assembly.GetName().Name
            ?? throw new InvalidOperationException("The ASP.NET Core Identity assembly has no name.");
        Assert.Equal(7, owners[foundationOwner]);
        Assert.Equal(2, owners[aspNetCoreOwner]);

        var refresh = Assert.Single(endpoints, endpoint => endpoint.RoutePattern.RawText == "/_elsa/identity/refresh");
        var refreshRequest = Assert.Single(refresh.Metadata.OfType<IAcceptsMetadata>());
        Assert.Equal(typeof(RefreshTokenRequest), refreshRequest.RequestType);
        Assert.Equal(["application/json"], refreshRequest.ContentTypes);

        var login = Assert.Single(endpoints, endpoint =>
            endpoint.RoutePattern.RawText == "/_elsa/identity/login" &&
            endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Single() == "POST");
        var loginRequest = Assert.Single(login.Metadata.OfType<IAcceptsMetadata>());
        Assert.Equal(typeof(LoginRequest), loginRequest.RequestType);
        Assert.Equal(["application/json", "application/x-www-form-urlencoded"], loginRequest.ContentTypes);
    }
}

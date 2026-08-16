using System.Reflection;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Studio.Preferences.Api;
using Elsa.Studio.Preferences.Tests.Support;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Studio.Preferences.Tests;

public sealed class StudioPreferencesApiCoexistenceTests
{
    [Fact]
    public void Studio_preferences_is_an_explicit_web_feature_alongside_fastendpoints_features()
    {
        var webShellFeature = LoadType(
            "CShells.AspNetCore.Features.IWebShellFeature",
            "CShells.AspNetCore.Abstractions");
        var studioFeature = typeof(StudioPreferencesApiFeature);

        Assert.True(
            webShellFeature.IsAssignableFrom(studioFeature),
            $"{studioFeature.FullName} must implement {webShellFeature.FullName} so it maps through the shell-owned Minimal API seam.");

        var mapEndpoints = studioFeature.GetMethod(
            "MapEndpoints",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            [
                LoadType("Microsoft.AspNetCore.Routing.IEndpointRouteBuilder", "Microsoft.AspNetCore.Routing"),
                LoadType("Microsoft.Extensions.Hosting.IHostEnvironment", "Microsoft.Extensions.Hosting.Abstractions")
            ],
            modifiers: null);

        Assert.NotNull(mapEndpoints);

        var fastEndpointsFeature = LoadType(
            "CShells.FastEndpoints.Features.FastEndpointsFeature",
            "CShells.FastEndpoints");
        Assert.True(
            webShellFeature.IsAssignableFrom(fastEndpointsFeature),
            "The mixed host must continue to support an unrelated FastEndpoints web feature.");
    }

    [Fact]
    public void Studio_preferences_exposes_one_module_owned_mapper_for_the_two_routes()
    {
        var mapper = typeof(StudioPreferencesApiFeature).Assembly.GetType(
            "Elsa.Studio.Preferences.Api.StudioPreferencesApi");

        Assert.NotNull(mapper);

        var mappingMethods = mapper!.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.Name == "MapStudioPreferencesApi")
            .ToArray();

        var mappingMethod = Assert.Single(mappingMethods);
        Assert.Single(mappingMethod.GetParameters());
        Assert.Equal(
            "Microsoft.AspNetCore.Routing.IEndpointRouteBuilder",
            mappingMethod.GetParameters()[0].ParameterType.FullName);
    }

    [Theory]
    [InlineData(null, 401)]
    [InlineData("denied", 403)]
    [InlineData("read", 200)]
    [InlineData("wildcard", 200)]
    [InlineData("untrusted", 401)]
    [InlineData("resource-denied", 403)]
    public async Task Minimal_api_and_fastendpoints_routes_share_one_foundation_permission_evaluator(
        string? identity,
        int expectedStatus)
    {
        await using var host = await StudioPreferencesCanaryHost.StartMigratedAsync();
        using var studioRequest = CreateRequest("/_elsa/studio/preferences/dashboard", identity, includeHost: true);
        using var fastEndpointsRequest = CreateRequest(UnrelatedFastEndpointsEndpoint.Route, identity, includeHost: false);

        using var studioResponse = await host.Client.SendAsync(studioRequest);
        using var fastEndpointsResponse = await host.Client.SendAsync(fastEndpointsRequest);

        Assert.Equal(expectedStatus, (int)studioResponse.StatusCode);
        Assert.Equal(expectedStatus, (int)fastEndpointsResponse.StatusCode);
    }

    [Fact]
    public async Task Mixed_host_manifest_identifies_both_authoring_models_with_the_same_encoded_read_policy()
    {
        await using var host = await StudioPreferencesCanaryHost.StartMigratedAsync();
        var routes = host.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToArray();
        var minimal = Assert.Single(routes, endpoint =>
            endpoint.RoutePattern.RawText == "/_elsa/studio/preferences/{namespace}" && GetMethod(endpoint) == "GET");
        var fastEndpoints = Assert.Single(routes, endpoint =>
            endpoint.RoutePattern.RawText == UnrelatedFastEndpointsEndpoint.Route && GetMethod(endpoint) == "GET");

        Assert.Equal(EndpointAuthoringModels.MinimalApi,
            minimal.Metadata.GetMetadata<EndpointAuthoringMetadata>()?.Model);
        Assert.Equal(EndpointAuthoringModels.FastEndpoints,
            fastEndpoints.Metadata.GetMetadata<EndpointAuthoringMetadata>()?.Model);
        Assert.Equal(GetPermissionPolicy(minimal), GetPermissionPolicy(fastEndpoints));
        Assert.Equal(
            minimal.Metadata.GetMetadata<EndpointSecurityDispositionMetadata>()?.Value,
            fastEndpoints.Metadata.GetMetadata<EndpointSecurityDispositionMetadata>()?.Value);
    }

    private static string GetPermissionPolicy(RouteEndpoint endpoint) => endpoint.Metadata
        .OfType<AuthorizeAttribute>()
        .Select(attribute => attribute.Policy)
        .Single(policy => policy?.StartsWith(PermissionPolicyCodec.Prefix, StringComparison.Ordinal) == true)!;

    private static string GetMethod(RouteEndpoint endpoint) =>
        endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Single()
        ?? throw new InvalidOperationException($"Endpoint '{endpoint.DisplayName}' has no HTTP method metadata.");

    private static HttpRequestMessage CreateRequest(string route, string? identity, bool includeHost)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, route);
        if (identity is not null)
            request.Headers.TryAddWithoutValidation(StudioPreferencesCanaryHost.IdentityHeader, identity);
        if (includeHost)
            request.Headers.TryAddWithoutValidation("X-Elsa-Studio-Host-Id", StudioPreferencesCanaryHost.HostId);
        return request;
    }

    private static Type LoadType(string fullName, string assemblyName)
    {
        var assembly = Assembly.Load(new AssemblyName(assemblyName));
        return assembly.GetType(fullName, throwOnError: true)!;
    }
}

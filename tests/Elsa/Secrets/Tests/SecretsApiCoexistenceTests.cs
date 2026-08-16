using System.Net;
using System.Net.Http.Json;
using Elsa.Api.AspNetCore;
using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Core.Permissions;
using Elsa.Secrets.Api.Requests;
using Elsa.Secrets.Tests.Support;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Secrets.Tests;

public sealed class SecretsApiCoexistenceTests
{
    [Fact]
    public async Task Migrated_minimal_api_and_real_fastendpoints_route_coexist_in_one_host()
    {
        SecretsCanaryHost.ResetPermissionEvaluatorObservations();
        await using var host = await SecretsCanaryHost.StartMigratedWithFastEndpointsAsync();

        var anonymousMinimal = await host.Client.GetAsync("/secrets/descriptors");
        var anonymousFastEndpoints = await host.Client.GetAsync(UnrelatedFastEndpointsEndpoint.Route);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousMinimal.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousFastEndpoints.StatusCode);

        using var permittedMinimal = new HttpRequestMessage(HttpMethod.Get, "/secrets/descriptors");
        permittedMinimal.Headers.Add(SecretsCanaryHost.IdentityHeader, "read|tenant-alpha");
        using var permittedFastEndpoints = new HttpRequestMessage(HttpMethod.Get, UnrelatedFastEndpointsEndpoint.Route);
        permittedFastEndpoints.Headers.Add(SecretsCanaryHost.IdentityHeader, "read|tenant-alpha");

        var permittedMinimalResponse = await host.Client.SendAsync(permittedMinimal);
        var permittedFastEndpointsResponse = await host.Client.SendAsync(permittedFastEndpoints);
        Assert.Equal(HttpStatusCode.OK, permittedMinimalResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, permittedFastEndpointsResponse.StatusCode);
        Assert.Equal(
            "unrelated-fastendpoints",
            (await permittedFastEndpointsResponse.Content.ReadAsStringAsync()).Trim('"'));

        using var deniedMinimal = new HttpRequestMessage(HttpMethod.Get, "/secrets/descriptors");
        deniedMinimal.Headers.Add(SecretsCanaryHost.IdentityHeader, "test|tenant-alpha");
        using var deniedFastEndpoints = new HttpRequestMessage(HttpMethod.Get, UnrelatedFastEndpointsEndpoint.Route);
        deniedFastEndpoints.Headers.Add(SecretsCanaryHost.IdentityHeader, "test|tenant-alpha");

        var deniedMinimalResponse = await host.Client.SendAsync(deniedMinimal);
        var deniedFastEndpointsResponse = await host.Client.SendAsync(deniedFastEndpoints);
        Assert.Equal(HttpStatusCode.Forbidden, deniedMinimalResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, deniedFastEndpointsResponse.StatusCode);
        Assert.True(SecretsCanaryHost.PermissionEvaluatorCallsFor("/secrets/descriptors") >= 2);
        Assert.True(SecretsCanaryHost.PermissionEvaluatorCallsFor(UnrelatedFastEndpointsEndpoint.Route) >= 2);
    }

    [Theory]
    [InlineData("status=not-a-status")]
    [InlineData("activeOnly=not-a-boolean")]
    [InlineData("page=not-an-integer")]
    [InlineData("pageSize=not-an-integer")]
    [InlineData("status=active&status=not-a-status")]
    [InlineData("activeOnly=true&activeOnly=not-a-boolean")]
    [InlineData("page=1&page=not-an-integer")]
    [InlineData("pageSize=2&pageSize=not-an-integer")]
    public async Task Migrated_query_binding_matches_the_real_fastendpoints_binder_for_invalid_values(string query)
    {
        await using var host = await SecretsCanaryHost.StartMigratedWithFastEndpointsAsync();
        using var minimal = new HttpRequestMessage(HttpMethod.Get, $"/secrets?{query}");
        minimal.Headers.Add(SecretsCanaryHost.IdentityHeader, "read|tenant-alpha");
        using var fastEndpoints = new HttpRequestMessage(HttpMethod.Get, $"{SecretQueryBindingFastEndpointsCanary.Route}?{query}");
        fastEndpoints.Headers.Add(SecretsCanaryHost.IdentityHeader, "read|tenant-alpha");

        using var minimalResponse = await host.Client.SendAsync(minimal);
        using var fastEndpointsResponse = await host.Client.SendAsync(fastEndpoints);

        Assert.Equal(fastEndpointsResponse.StatusCode, minimalResponse.StatusCode);
        Assert.Equal(fastEndpointsResponse.Content.Headers.ContentType?.MediaType, minimalResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            await fastEndpointsResponse.Content.ReadAsStringAsync(),
            await minimalResponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Fastendpoints_before_binding_confirms_the_route_name_is_authoritative()
    {
        await using var host = await SecretsCanaryHost.StartMigratedWithFastEndpointsAsync();
        using var request = new HttpRequestMessage(HttpMethod.Put, "/_canary/secrets-route-binding/route-name")
        {
            Content = JsonContent.Create(new { name = "body-name", displayName = "Updated" })
        };
        request.Headers.Add(SecretsCanaryHost.IdentityHeader, "write|tenant-alpha");

        using var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("route-name", (await response.Content.ReadAsStringAsync()).Trim('"'));
    }

    [Fact]
    public void Migrated_and_unmigrated_routes_use_the_same_foundation_permission_conventions()
    {
        var services = new ServiceCollection();
        services.AddFoundationIdentityAbstractions(options =>
            options.NormalizedAuthenticationTypes = new HashSet<string>(StringComparer.Ordinal) { "test" });
        using var provider = services.BuildServiceProvider();
        var builder = new ConventionRouteBuilder(provider);

        builder.MapGet("/legacy-secured", () => Results.Ok())
            .WithOwner("legacy-canary")
            .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
            .RequireAnyPermission(PermissionKey.Wildcard, "legacy.read");

        var endpoint = Assert.Single(builder.DataSources.SelectMany(dataSource => dataSource.Endpoints));
        var disposition = endpoint.Metadata.GetMetadata<EndpointSecurityDispositionMetadata>();
        Assert.NotNull(disposition);
        Assert.Equal(EndpointAuthoringModels.MinimalApi, endpoint.Metadata.GetMetadata<EndpointAuthoringMetadata>()?.Model);
    }

    private sealed class ConventionRouteBuilder(IServiceProvider serviceProvider) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider { get; } = serviceProvider;
        public ICollection<EndpointDataSource> DataSources { get; } = new List<EndpointDataSource>();
        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);
    }
}

/// <summary>
/// A real FastEndpoints endpoint intentionally left unmigrated to prove that a module's Minimal API
/// routes can share one host and the Foundation permission evaluator with an existing feature route.
/// </summary>
internal sealed class UnrelatedFastEndpointsEndpoint : ElsaEndpointWithoutRequest<string>
{
    public const string Route = "/_canary/unrelated-fastendpoints";

    public override void Configure()
    {
        Get(Route);
        ConfigurePermissions(SecretsPermissions.Read);
    }

    public override Task HandleAsync(CancellationToken cancellationToken) =>
        Send.OkAsync("unrelated-fastendpoints", cancellationToken);
}

internal sealed class SecretQueryBindingFastEndpointsCanary : ElsaEndpoint<SecretQuery, SecretQuery>
{
    public const string Route = "/_canary/secrets-query-binding";

    public override void Configure()
    {
        Get(Route);
        ConfigurePermissions(SecretsPermissions.Read);
    }

    public override Task HandleAsync(SecretQuery request, CancellationToken cancellationToken) =>
        Send.OkAsync(request, cancellationToken);
}

internal sealed class SecretRouteBindingFastEndpointsCanary : ElsaEndpoint<UpdateSecretApiRequest, string>
{
    public override void Configure()
    {
        Put("/_canary/secrets-route-binding/{name}");
        ConfigurePermissions(SecretsPermissions.Write);
    }

    public override Task HandleAsync(UpdateSecretApiRequest request, CancellationToken cancellationToken) =>
        Send.OkAsync(request.Name, cancellationToken);
}

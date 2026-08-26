using System.Security.Claims;
using Elsa.Api.AspNetCore;
using Elsa.Attention.Core;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Attention.Api.Tests;

public sealed class GetAttentionItemsEndpointTests
{
    [Fact]
    public void Endpoint_is_a_secured_minimal_api_owned_by_the_attention_module()
    {
        var endpoint = GetEndpoint();

        var owner = Assert.IsType<EndpointOwnershipMetadata>(endpoint.Metadata.GetMetadata<EndpointOwnershipMetadata>());
        Assert.Equal("Elsa.Attention.Api", owner.OwnerId);
        Assert.Equal(EndpointAuthoringModels.MinimalApi, endpoint.Metadata.GetMetadata<EndpointAuthoringMetadata>()?.Model);
        var security = Assert.IsType<EndpointSecurityDispositionMetadata>(
            endpoint.Metadata.GetMetadata<EndpointSecurityDispositionMetadata>());
        Assert.Equal(EndpointSecurityDispositionKind.Permission, security.Kind);
        var policy = new PermissionPolicyCodec().Parse(security.Value!);
        Assert.Contains(PermissionKey.Normalize(AttentionPermissions.Read), policy.Descriptor!.Permissions);
        Assert.DoesNotContain(endpoint.Metadata, item => item is Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute);
    }

    [Fact]
    public async Task Repeated_filters_and_tenant_context_are_forwarded_to_aggregation()
    {
        var service = new StubAggregationService();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "user-1"),
            new Claim(IdentityClaimTypes.TenantId, "tenant-1")
        ], "test"));
        var context = CreateContext(service, principal, "?contributorId=workflows.runtime&contributorId=secrets");

        await GetEndpoint().RequestDelegate!(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal(["workflows.runtime", "secrets"], service.Query!.ContributorIds);
        Assert.Same(principal, service.Query.Context.Principal);
        Assert.Equal("tenant-1", service.Query.Context.TenantId);
    }

    [Fact]
    public async Task Malformed_filter_returns_request_level_bad_request()
    {
        var context = CreateContext(
            new StubAggregationService(new AttentionQueryException("Contributor IDs cannot be empty.")),
            queryString: "?contributorId=");

        await GetEndpoint().RequestDelegate!(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Fact]
    public async Task Unexpected_aggregation_failure_is_not_converted_to_partial_success()
    {
        // The module endpoint pipeline contains unexpected failures as a sanitized problem: the
        // caller never sees a partial success, and the failure's own message never leaks.
        var context = CreateContext(new StubAggregationService(new InvalidOperationException("aggregation unavailable")));
        context.Response.Body = new MemoryStream();

        await GetEndpoint().RequestDelegate!(context);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.DoesNotContain("aggregation unavailable", body);
        Assert.DoesNotContain("items", body);
    }

    private static RouteEndpoint GetEndpoint()
    {
        using var services = new ServiceCollection().AddRouting().BuildServiceProvider();
        var routes = new TestEndpointRouteBuilder(services);
        AttentionApi.MapAttentionApi(routes);
        return Assert.Single(routes.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>());
    }

    private static DefaultHttpContext CreateContext(
        IAttentionAggregationService service,
        ClaimsPrincipal? principal = null,
        string? queryString = null)
    {
        var services = new ServiceCollection().AddLogging().AddSingleton(service).BuildServiceProvider();
        var context = new DefaultHttpContext
        {
            RequestServices = services,
            User = principal ?? new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "test"))
        };
        context.Response.Body = new MemoryStream();
        if (queryString is not null)
            context.Request.QueryString = new QueryString(queryString);
        return context;
    }

    private sealed class TestEndpointRouteBuilder(IServiceProvider serviceProvider) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider { get; } = serviceProvider;
        public ICollection<EndpointDataSource> DataSources { get; } = [];
        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);
    }

    private sealed class StubAggregationService(Exception? exception = null) : IAttentionAggregationService
    {
        public AttentionQuery? Query { get; private set; }

        public Task<AttentionAggregationResult> AggregateAsync(AttentionQuery query, CancellationToken cancellationToken = default)
        {
            Query = query;
            if (exception is not null)
                return Task.FromException<AttentionAggregationResult>(exception);

            return Task.FromResult(new AttentionAggregationResult(
                DateTimeOffset.UtcNow,
                [
                    new AttentionContributorResult(
                        "workflows.runtime", "Workflow runtime", AttentionContributorStatus.Ready,
                        DateTimeOffset.UtcNow, 0, false, []),
                    new AttentionContributorResult(
                        "secrets", "Secrets", AttentionContributorStatus.TimedOut,
                        DateTimeOffset.UtcNow, 0, false, [], "CONTRIBUTOR_TIMEOUT")
                ]));
        }
    }
}

using System.Security.Claims;
using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Attention.Api;
using Elsa.Attention.Core;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace Elsa.Attention.Api.Tests;

public sealed class GetAttentionItemsEndpointTests
{
    [Fact]
    public void Endpoint_requires_authentication_and_never_allows_anonymous_access()
    {
        var endpoint = CreateEndpoint(new StubAggregationService());

        endpoint.Configure();

        Assert.NotEmpty(endpoint.Definition.PreBuiltUserPolicies!);
        Assert.All(
            endpoint.Definition.PreBuiltUserPolicies!,
            policy => Assert.Equal(ElsaEndpointPermissions.ComposePolicy([]), policy));
        Assert.Null(endpoint.Definition.AnonymousVerbs);
        Assert.Contains(AttentionRoutes.Items, endpoint.Definition.Routes);
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
        var endpoint = CreateEndpoint(
            service,
            principal,
            "?contributorId=workflows.runtime&contributorId=secrets");

        await HandleAsync(endpoint);

        Assert.Equal(StatusCodes.Status200OK, endpoint.HttpContext.Response.StatusCode);
        Assert.Equal(["workflows.runtime", "secrets"], service.Query!.ContributorIds);
        Assert.Same(principal, service.Query.Context.Principal);
        Assert.Equal("tenant-1", service.Query.Context.TenantId);
    }

    [Fact]
    public async Task Malformed_filter_returns_request_level_bad_request()
    {
        var endpoint = CreateEndpoint(
            new StubAggregationService(new AttentionQueryException("Contributor IDs cannot be empty.")),
            queryString: "?contributorId=");

        await HandleAsync(endpoint);

        Assert.Equal(StatusCodes.Status400BadRequest, endpoint.HttpContext.Response.StatusCode);
    }

    [Fact]
    public async Task Unexpected_aggregation_failure_is_not_converted_to_partial_success()
    {
        var endpoint = CreateEndpoint(
            new StubAggregationService(new InvalidOperationException("aggregation unavailable")));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => HandleAsync(endpoint));

        Assert.Equal("aggregation unavailable", exception.Message);
    }

    private static BaseEndpoint CreateEndpoint(
        IAttentionAggregationService service,
        ClaimsPrincipal? principal = null,
        string? queryString = null)
    {
        var endpointType = typeof(AttentionApiFeature).Assembly
            .GetType("Elsa.Attention.Api.Endpoints.GetAttentionItems", throwOnError: true)!;
        var create = typeof(Factory).GetMethods()
            .Single(method => method.Name == nameof(Factory.Create)
                              && method.IsGenericMethodDefinition
                              && method.GetParameters() is [var first, var rest]
                              && first.ParameterType == typeof(Action<DefaultHttpContext>)
                              && rest.ParameterType == typeof(object[]))
            .MakeGenericMethod(endpointType);
        Action<DefaultHttpContext> configure = context =>
        {
            context.User = principal ?? new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "test"));
            context.Response.Body = new MemoryStream();
            if (queryString is not null)
                context.Request.QueryString = new QueryString(queryString);
        };
        return (BaseEndpoint)create.Invoke(null, [configure, new object[] { service }])!;
    }

    private static Task HandleAsync(BaseEndpoint endpoint) =>
        (Task)endpoint.GetType()
            .GetMethod("HandleAsync", [typeof(CancellationToken)])!
            .Invoke(endpoint, [CancellationToken.None])!;

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
                        "workflows.runtime",
                        "Workflow runtime",
                        AttentionContributorStatus.Ready,
                        DateTimeOffset.UtcNow,
                        0,
                        false,
                        []),
                    new AttentionContributorResult(
                        "secrets",
                        "Secrets",
                        AttentionContributorStatus.TimedOut,
                        DateTimeOffset.UtcNow,
                        0,
                        false,
                        [],
                        "CONTRIBUTOR_TIMEOUT")
                ]));
        }
    }
}

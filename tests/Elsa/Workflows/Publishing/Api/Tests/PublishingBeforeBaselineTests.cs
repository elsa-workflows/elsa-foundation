using Elsa.Workflows.Publishing.Api.Tests.Support;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>Structural gates for the immutable FastEndpoints-before route inventory.</summary>
public sealed class PublishingBeforeBaselineTests
{
    [Fact]
    public void Reviewed_manifest_contains_exactly_23_one_to_one_registrations()
    {
        var routes = PublishingCompatibilityCases.Manifest;

        Assert.Equal(23, routes.Count);
        Assert.Equal(23, routes.Select(route => route.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(23, routes.Select(route => route.Endpoint).Distinct().Count());
        Assert.Equal(23, routes.Select(route => route.Endpoint.ToString()).Distinct(StringComparer.Ordinal).Count());
        Assert.All(routes, route => Assert.False(string.IsNullOrWhiteSpace(route.Request)));
        Assert.All(routes, route => Assert.False(string.IsNullOrWhiteSpace(route.Response)));
        Assert.Equal(12, routes.Count(route => route.Action == "read"));
        Assert.Equal(11, routes.Count(route => route.Action == "manage"));
        Assert.Equal(23, PublishingCompatibilityCases.Anonymous.Count);
        Assert.Equal(23, PublishingCompatibilityCases.Authenticated.Count);
        Assert.Equal(7, PublishingCompatibilityCases.Binding.Count);
        Assert.Equal(5, PublishingCompatibilityCases.Domain.Count);
        Assert.Single(PublishingCompatibilityCases.Cancellation);
        Assert.Equal(59, PublishingCompatibilityCases.All.Count);
    }

    [Fact]
    public void Manifest_preserves_reserved_drafts_and_mixed_owner_prefixes()
    {
        var routes = PublishingCompatibilityCases.Manifest;
        var versioned = routes.Where(route => route.Endpoint.Route.Value.Contains("regex", StringComparison.Ordinal)).ToArray();

        Assert.Equal(3, versioned.Length);
        Assert.All(versioned, route => Assert.Contains("drafts", route.Endpoint.Route.Value, StringComparison.Ordinal));
        Assert.Equal(20, routes.Count(route => route.Endpoint.Route.Value.StartsWith("/publishing/", StringComparison.Ordinal)));
        Assert.Equal(3, routes.Count(route => route.Endpoint.Route.Value.StartsWith("/design/activities/", StringComparison.Ordinal)));
        Assert.Equal(4, routes.Count(route => route.Endpoint.Route.Value.StartsWith("/publishing/activity-", StringComparison.Ordinal)));
    }

    [Fact]
    public void Every_route_has_anonymous_and_authenticated_capture_cases()
    {
        var anonymous = PublishingCompatibilityCases.Anonymous.Select(testCase => testCase.Case.Split('|')[0]).ToHashSet(StringComparer.Ordinal);
        var authenticated = PublishingCompatibilityCases.Authenticated.Select(testCase => testCase.Case.Split('|')[0]).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(PublishingCompatibilityCases.Manifest.Select(route => route.Id).ToHashSet(StringComparer.Ordinal), anonymous);
        Assert.Equal(anonymous, authenticated);
        Assert.All(PublishingCompatibilityCases.Authenticated, testCase =>
            Assert.Contains("trusted-success", testCase.Case, StringComparison.Ordinal));
    }
}

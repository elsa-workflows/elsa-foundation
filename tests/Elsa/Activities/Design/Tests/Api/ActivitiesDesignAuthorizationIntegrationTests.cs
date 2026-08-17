using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Tests.Api.Support;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text.Json;
using Xunit;

namespace Elsa.Activities.Design.Tests.Api;

[Collection(ActivitiesDesignAuthorizationHostCollection.Name)]
public sealed class ActivitiesDesignAuthorizationIntegrationTests
{
    public static IEnumerable<object[]> PermissionMatrix() =>
    [
        ["anonymous", null!, HttpStatusCode.Unauthorized],
        ["authenticated-untrusted", "untrusted", HttpStatusCode.Unauthorized],
        ["ambiguous", "ambiguous", HttpStatusCode.Unauthorized],
        ["trusted-denied", "denied", HttpStatusCode.Forbidden],
        ["exact", "exact", HttpStatusCode.OK],
        ["implied", "implied", HttpStatusCode.OK],
        ["evaluator-wildcard", "wildcard", HttpStatusCode.OK],
        ["normalized", "normalized", HttpStatusCode.OK],
        ["normalized-external", "external", HttpStatusCode.OK],
        ["malformed-normalized-marker", "invalid-normalization", HttpStatusCode.Unauthorized]
    ];

    [Theory]
    [MemberData(nameof(PermissionMatrix))]
    public async Task Minimal_activity_design_route_uses_fail_closed_shared_permission_matrix(
        string _,
        string? identity,
        HttpStatusCode expected)
    {
        await using var host = await ActivitiesDesignAuthorizationHost.StartAsync();
        host.Observations.Reset();

        using var response = await host.Client.SendAsync(Request("/design/activities/catalog", identity));

        Assert.Equal(expected, response.StatusCode);
        if (expected != HttpStatusCode.OK)
            Assert.Equal(0, host.Observations.RequestSenderCalls);
    }

    [Fact]
    public async Task Replacement_evaluator_is_used_by_the_real_Minimal_host()
    {
        await using var host = await ActivitiesDesignAuthorizationHost.StartAsync();
        var evaluator = host.Host.Services.GetRequiredService<IPermissionEvaluator>();

        Assert.IsType<RecordingPermissionEvaluator>(evaluator);
        using var response = await host.Client.SendAsync(Request("/design/activities/catalog", "exact"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEmpty(host.Observations.EvaluatedPermissions);
        Assert.All(host.Observations.EvaluatorImplementations, implementation =>
            Assert.Equal(typeof(RecordingPermissionEvaluator).FullName, implementation));
    }

    [Fact]
    public async Task External_provider_claims_are_mapped_by_the_real_claims_normalizer()
    {
        await using var host = await ActivitiesDesignAuthorizationHost.StartAsync();
        host.Observations.Reset();

        using var response = await host.Client.SendAsync(Request("/design/activities/catalog", "external"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, host.Observations.NormalizerCalls);
        Assert.Contains(ActivityDesignPermissions.Read, host.Observations.EvaluatedPermissions, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Minimal_and_retained_FastEndpoints_routes_share_policy_provider_normalizer_and_evaluator()
    {
        await using var host = await ActivitiesDesignAuthorizationHost.StartAsync();

        foreach (var (identity, expected) in new[]
                 {
                     ("exact", HttpStatusCode.OK),
                     ("implied", HttpStatusCode.OK),
                     ("wildcard", HttpStatusCode.OK),
                     ("denied", HttpStatusCode.Forbidden),
                     ("untrusted", HttpStatusCode.Unauthorized),
                     ("ambiguous", HttpStatusCode.Unauthorized)
                 })
        {
            host.Observations.Reset();
            using var minimal = await host.Client.SendAsync(Request("/design/activities/catalog", identity));
            var minimalPermissions = host.Observations.EvaluatedPermissions.ToArray();
            var minimalResourceCalls = host.Observations.ResourceCalls;

            host.Observations.Reset();
            using var fastEndpoints = await host.Client.SendAsync(Request("/test/activity-fast", identity));
            var fastPermissions = host.Observations.EvaluatedPermissions.ToArray();
            var fastEndpointsResourceCalls = host.Observations.ResourceCalls;

            Assert.Equal(expected, minimal.StatusCode);
            Assert.Equal(expected, fastEndpoints.StatusCode);
            if (expected != HttpStatusCode.Unauthorized)
            {
                Assert.NotEmpty(host.Observations.EvaluatorImplementations);
                Assert.All(host.Observations.EvaluatorImplementations, implementation =>
                    Assert.Equal(typeof(RecordingPermissionEvaluator).FullName, implementation));
            }
            if (expected == HttpStatusCode.OK)
            {
                Assert.True(minimalResourceCalls > 0, "Minimal API authorization did not invoke the shared resource handler.");
                Assert.True(fastEndpointsResourceCalls > 0, "FastEndpoints authorization did not invoke the shared resource handler.");
                Assert.Contains(PermissionKey.Normalize("activity-design.read"), minimalPermissions.Select(PermissionKey.Normalize));
                Assert.True(
                    fastPermissions.Select(PermissionKey.Normalize).Contains(PermissionKey.Normalize("activity-design.read"), StringComparer.Ordinal) ||
                    fastPermissions.Contains(PermissionKey.Wildcard),
                    $"The retained FastEndpoints canary did not evaluate read or wildcard: {string.Join(",", fastPermissions)}");
            }
        }
    }

    [Fact]
    public async Task Both_authoring_models_publish_standard_permission_metadata_with_one_dynamic_provider()
    {
        await using var host = await ActivitiesDesignAuthorizationHost.StartAsync();
        var minimalCandidates = host.Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.Contains("catalog", StringComparison.Ordinal) == true)
            .ToArray();
        Assert.True(minimalCandidates.Length > 0,
            string.Join(Environment.NewLine, host.Endpoints.OfType<RouteEndpoint>().Select(endpoint =>
                $"raw='{endpoint.RoutePattern.RawText}', display='{endpoint.DisplayName}'")));
        var minimal = Assert.Single(minimalCandidates);
        var fastEndpoints = Assert.Single(host.Endpoints.OfType<RouteEndpoint>(), endpoint =>
            endpoint.Metadata.GetMetadata<EndpointAuthoringMetadata>()?.Model == EndpointAuthoringModels.FastEndpoints);

        var minimalPolicy = Assert.IsType<AuthorizeAttribute>(minimal.Metadata.GetMetadata<AuthorizeAttribute>()).Policy;
        Assert.IsType<AuthorizeAttribute>(fastEndpoints.Metadata.GetMetadata<AuthorizeAttribute>());
        Assert.StartsWith(PermissionPolicyCodec.Prefix, minimalPolicy, StringComparison.Ordinal);
        var fastSecurity = Assert.IsType<EndpointSecurityDispositionMetadata>(
            fastEndpoints.Metadata.GetMetadata<EndpointSecurityDispositionMetadata>());
        var fastPermissionPolicy = new PermissionPolicyCodec().Parse(fastSecurity.Value!);
        Assert.Equal(PermissionRequirementMode.Any, fastPermissionPolicy.Descriptor!.Mode);
        Assert.Contains(PermissionKey.Normalize("activity-design.read"), fastPermissionPolicy.Descriptor.Permissions);
        Assert.IsType<RequirePermissionPolicyProvider>(host.Host.Services.GetRequiredService<IAuthorizationPolicyProvider>());
        Assert.Equal(
            PermissionKey.Normalize("activity-design.read"),
            new PermissionPolicyCodec().Parse(minimalPolicy!).Descriptor!.Permissions.Single());
    }

    [Theory]
    [InlineData("/design/activities/catalog")]
    [InlineData("/test/activity-fast")]
    public async Task Evaluator_cancellation_is_observed_by_both_authoring_models(string path)
    {
        await using var host = await ActivitiesDesignAuthorizationHost.StartAsync();
        using var request = Request(path, "cancel-evaluator");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.Client.SendAsync(request));

        Assert.True(host.Observations.EvaluatedPermissions.Count > 0);
        Assert.True(host.Observations.LastEvaluatorCancellation.CanBeCanceled);
        Assert.Equal(0, host.Observations.RequestSenderCalls);
    }

    internal static HttpRequestMessage Request(string path, string? identity)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (identity is not null)
            request.Headers.TryAddWithoutValidation(ActivitiesDesignAuthorizationHost.IdentityHeader, identity);
        if (identity is not null && !identity.EndsWith("no-tenant", StringComparison.Ordinal))
            request.Headers.TryAddWithoutValidation(ActivitiesDesignAuthorizationHost.TenantHeader, identity == "tenant-b" ? "tenant-b" : "tenant-a");
        return request;
    }
}

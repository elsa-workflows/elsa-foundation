using Elsa.Activities.Design.Tests.Api.Support;
using System.Net;
using System.Text.Json;
using Xunit;

namespace Elsa.Activities.Design.Tests.Api;

[Collection(ActivitiesDesignAuthorizationHostCollection.Name)]
public sealed class ActivitiesDesignAuthorizationBoundaryTests
{
    [Theory]
    [InlineData("/design/activities/catalog", "no-tenant")]
    [InlineData("/design/activities/catalog", "implied-no-tenant")]
    [InlineData("/design/activities/catalog", "wildcard-no-tenant")]
    [InlineData("/test/activity-fast", "no-tenant")]
    [InlineData("/test/activity-fast", "implied-no-tenant")]
    [InlineData("/test/activity-fast", "wildcard-no-tenant")]
    public async Task Missing_tenant_is_forbidden_before_the_activity_operation_runs(string path, string identity)
    {
        await using var host = await ActivitiesDesignAuthorizationHost.StartAsync();
        host.Observations.Reset();

        using var response = await host.Client.SendAsync(
            ActivitiesDesignAuthorizationIntegrationTests.Request(path, identity));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, host.Observations.RequestSenderCalls);
        Assert.Equal(0, host.Observations.CommandSenderCalls);
    }

    [Theory]
    [InlineData("exact")]
    [InlineData("implied")]
    [InlineData("wildcard")]
    public async Task Route_resource_tenant_mismatch_is_denied_for_every_grant_shape(string identity)
    {
        await using var host = await ActivitiesDesignAuthorizationHost.StartAsync();
        host.Observations.Reset();
        using var request = ActivitiesDesignAuthorizationIntegrationTests.Request(
            "/test/activity-resource/tenant-b",
            identity);

        using var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, host.Observations.RequestSenderCalls);
        Assert.Equal(0, host.Observations.CommandSenderCalls);
    }

    [Theory]
    [InlineData("manage", HttpStatusCode.Forbidden)]
    [InlineData("provider-author", HttpStatusCode.OK)]
    [InlineData("provider-all", HttpStatusCode.OK)]
    public async Task Provider_authoring_is_a_distinct_inner_resource_decision(
        string identity,
        HttpStatusCode expected)
    {
        await using var host = await ActivitiesDesignAuthorizationHost.StartAsync();
        using var request = ActivitiesDesignAuthorizationIntegrationTests.Request(
            "/test/activity-provider-authoring",
            identity);
        request.Headers.TryAddWithoutValidation(ActivitiesDesignAuthorizationHost.ProviderHeader, "allowed-provider");

        using var response = await host.Client.SendAsync(request);

        Assert.Equal(expected, response.StatusCode);
        Assert.Contains("activities.design.author", host.Observations.EvaluatedPermissions);
    }

    [Fact]
    public async Task Provider_resource_denial_wins_over_an_authoring_permission_grant()
    {
        await using var host = await ActivitiesDesignAuthorizationHost.StartAsync();
        using var request = ActivitiesDesignAuthorizationIntegrationTests.Request(
            "/test/activity-provider-authoring",
            "provider-all");
        request.Headers.TryAddWithoutValidation(ActivitiesDesignAuthorizationHost.ProviderHeader, "denied-provider");

        using var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("manage", null)]
    [InlineData("provider-payload", "provider-payload")]
    [InlineData("provider-all", "provider-payload")]
    public async Task Provider_payload_is_present_only_for_the_inner_payload_permission(
        string identity,
        string? expectedPayload)
    {
        await using var host = await ActivitiesDesignAuthorizationHost.StartAsync();
        using var request = ActivitiesDesignAuthorizationIntegrationTests.Request(
            "/test/activity-provider-payload",
            identity);
        request.Headers.TryAddWithoutValidation(ActivitiesDesignAuthorizationHost.ProviderHeader, "allowed-provider");

        using var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var payload = document.RootElement.GetProperty("payload");
        if (expectedPayload is null)
            Assert.Equal(JsonValueKind.Null, payload.ValueKind);
        else
            Assert.Equal(expectedPayload, payload.GetString());
    }

    [Fact]
    public async Task Permission_denial_happens_before_store_sender_and_provider_work()
    {
        await using var host = await ActivitiesDesignAuthorizationHost.StartAsync();
        host.Observations.Reset();

        using var request = ActivitiesDesignAuthorizationIntegrationTests.Request(
            "/test/activity-denial-boundary",
            "denied");
        using var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, host.Observations.RequestSenderCalls);
        Assert.Equal(0, host.Observations.CommandSenderCalls);
        Assert.Equal(0, host.Observations.ProviderCalls);
        Assert.Equal(0, host.Observations.StoreCalls);
        Assert.DoesNotContain("activities.design.author", host.Observations.EvaluatedPermissions);
        Assert.DoesNotContain("activities.design.provider-payload.read", host.Observations.EvaluatedPermissions);
    }

    [Fact]
    public async Task Granted_boundary_probe_invokes_provider_store_and_sender_once()
    {
        await using var host = await ActivitiesDesignAuthorizationHost.StartAsync();
        host.Observations.Reset();

        using var response = await host.Client.SendAsync(
            ActivitiesDesignAuthorizationIntegrationTests.Request("/test/activity-denial-boundary", "manage"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, host.Observations.RequestSenderCalls);
        Assert.Equal(1, host.Observations.ProviderCalls);
        Assert.Equal(1, host.Observations.StoreCalls);
    }
}

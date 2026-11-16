using Elsa.Workflows.Publishing.Api.Authorization;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text.Json;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

[Collection(PublishingAuthorizationHostCollection.Name)]
public sealed class PublishingAuthorizationBoundaryTests
{
    [Theory]
    [InlineData("no-tenant")]
    [InlineData("implied-no-tenant")]
    [InlineData("wildcard-no-tenant")]
    public async Task Missing_tenant_is_forbidden_before_a_publishing_resource_operation_runs(string identity)
    {
        await using var host = await PublishingAuthorizationHost.StartAsync();
        host.Observations.Reset();

        using var response = await host.Client.SendAsync(PublishingAuthorizationRequests.Create(
            "/test/publishing/resource/tenant-a", identity));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, host.Observations.RequestSenderCalls);
        Assert.Equal(0, host.Observations.CompilerCalls);
    }

    [Theory]
    [InlineData("exact")]
    [InlineData("implied")]
    [InlineData("wildcard")]
    public async Task Route_resource_mismatch_is_denied_for_every_grant_shape(string identity)
    {
        await using var host = await PublishingAuthorizationHost.StartAsync();
        host.Observations.Reset();

        using var response = await host.Client.SendAsync(PublishingAuthorizationRequests.Create(
            "/test/publishing/resource/tenant-b", identity));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, host.Observations.RequestSenderCalls);
        Assert.Equal(0, host.Observations.CompilerCalls);
    }

    [Fact]
    public async Task Mismatched_request_tenant_is_denied_before_the_resource_operation_runs()
    {
        await using var host = await PublishingAuthorizationHost.StartAsync();
        host.Observations.Reset();

        using var response = await host.Client.SendAsync(PublishingAuthorizationRequests.Create(
            "/test/publishing/resource/tenant-a", "exact", tenant: "tenant-b"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, host.Observations.RequestSenderCalls);
        Assert.Equal(0, host.Observations.CompilerCalls);
    }

    [Fact]
    public async Task Explicit_resource_denial_vetoes_an_exact_grant_before_the_operation_runs()
    {
        await using var host = await PublishingAuthorizationHost.StartAsync();
        host.Observations.Reset();
        using var request = PublishingAuthorizationRequests.Create(
            "/test/publishing/resource/tenant-a", "exact-resource-denied");

        using var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, host.Observations.RequestSenderCalls);
        Assert.Equal(0, host.Observations.CompilerCalls);
    }

    [Fact]
    public async Task Activity_publication_inner_authorizer_denies_a_foreign_tenant_without_disclosing_payload()
    {
        await using var host = await PublishingAuthorizationHost.StartAsync();
        host.Observations.Reset();
        using var request = PublishingAuthorizationRequests.Create(
            "/test/publishing/activity-authorizer", "manage");
        request.Headers.TryAddWithoutValidation(PublishingAuthorizationHost.ActivityTenantHeader, "tenant-b");

        using var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(1, host.Host.Services.GetRequiredService<PublishingAuthorizationBoundaryProbe>()
            .ActivityAuthorizerCalls);
        Assert.Equal(0, host.Observations.RequestSenderCalls);
    }

    [Theory]
    [InlineData("tenant-a", "provider-payload")]
    [InlineData("tenant-b", null)]
    public async Task Activity_publication_payload_is_redacted_when_the_inner_tenant_authorizer_denies(
        string activityTenant,
        string? expectedPayload)
    {
        await using var host = await PublishingAuthorizationHost.StartAsync();
        using var request = PublishingAuthorizationRequests.Create(
            "/test/publishing/activity-payload", "exact");
        request.Headers.TryAddWithoutValidation(PublishingAuthorizationHost.ActivityTenantHeader, activityTenant);

        using var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var payload = document.RootElement.GetProperty("payload");
        if (expectedPayload is null)
        {
            Assert.Equal(JsonValueKind.Null, payload.ValueKind);
            Assert.DoesNotContain("provider-payload", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
        else
            Assert.Equal(expectedPayload, payload.GetString());
    }

    [Fact]
    public async Task Permission_denial_happens_before_sender_store_compiler_publisher_and_test_run()
    {
        await using var host = await PublishingAuthorizationHost.StartAsync();
        var probe = host.Host.Services.GetRequiredService<PublishingAuthorizationBoundaryProbe>();

        using var response = await host.Client.SendAsync(PublishingAuthorizationRequests.Create(
            "/test/publishing/denial-boundary", "denied"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, probe.SenderCalls);
        Assert.Equal(0, probe.StoreCalls);
        Assert.Equal(0, probe.CompilerCalls);
        Assert.Equal(0, probe.PublisherCalls);
        Assert.Equal(0, probe.TestRunCalls);
        Assert.Equal(0, host.Observations.RequestSenderCalls);
        Assert.Equal(0, host.Observations.CompilerCalls);
    }

    [Fact]
    public async Task A_granted_boundary_probe_runs_after_permission_authorization()
    {
        await using var host = await PublishingAuthorizationHost.StartAsync();
        var probe = host.Host.Services.GetRequiredService<PublishingAuthorizationBoundaryProbe>();

        using var response = await host.Client.SendAsync(PublishingAuthorizationRequests.Create(
            "/test/publishing/denial-boundary", "manage"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, probe.SenderCalls);
        Assert.Equal(1, probe.StoreCalls);
        Assert.Equal(1, probe.CompilerCalls);
        Assert.Equal(1, probe.PublisherCalls);
        Assert.Equal(1, probe.TestRunCalls);
    }
}

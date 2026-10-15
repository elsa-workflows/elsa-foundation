using Elsa.Integrations.Sdk.OAuth;
using System.Text.Json;

namespace Elsa.Integrations.Connectors.OAuth2.Tests;

public sealed class HttpConnectionTests
{
    [Fact]
    public async Task SendHttpRequest_uses_the_http_connection_and_injects_the_token()
    {
        var preview = new MockOAuthServer();
        var (harness, server) = await OAuth2TestSupport.StartAsync(OAuth2TestSupport.ClientCredentialsScheme(preview));
        await using var _ = harness;

        var completion = await OAuth2TestSupport.SendAsync(harness, server.ResourceEndpoint);

        Assert.Equal("Done", completion.Outcome);
        Assert.Equal(200, completion.Result.StatusCode);
        Assert.Contains("\"ok\":true", completion.Result.ResponseBody, StringComparison.Ordinal);
        Assert.Contains(server.IssuedAccessTokens, token => token.StartsWith(MockOAuthServer.CanaryAccessToken, StringComparison.Ordinal));
        Assert.DoesNotContain(MockOAuthServer.CanaryAccessToken, JsonSerializer.Serialize(completion.Result), StringComparison.Ordinal);
        Assert.False(completion.Result.ResponseHeaders?.ContainsKey("Authorization") ?? false);
    }

    [Fact]
    public async Task Authorization_code_with_pkce_works_through_the_callback()
    {
        var preview = new MockOAuthServer();
        var (harness, server) = await OAuth2TestSupport.StartAsync(OAuth2TestSupport.AuthorizationCodeScheme(preview));
        await using var _ = harness;

        var started = await harness.Tokens.BeginAuthorizationCodeAsync(OAuth2TestSupport.BoundConnection(harness), harness.Callback);
        Assert.Contains("code_challenge=", started.AuthorizationUrl, StringComparison.Ordinal);
        Assert.Contains("code_challenge_method=S256", started.AuthorizationUrl, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString(harness.Callback.CallbackUri.ToString()), started.AuthorizationUrl, StringComparison.Ordinal);

        var code = server.ApproveAuthorization(new Uri(started.AuthorizationUrl));
        var result = await harness.CallbackHandler.HandleAsync(OAuth2TestSupport.BoundConnection(harness), code, started.State);
        Assert.True(result.Ok, result.Error);

        var completion = await OAuth2TestSupport.SendAsync(harness, server.ResourceEndpoint);
        Assert.Equal(200, completion.Result.StatusCode);
    }

    [Theory]
    [InlineData(OAuth2ClientSources.Valence)]
    [InlineData(OAuth2ClientSources.BringYourOwn)]
    public async Task Valence_and_byo_clients_both_work(string clientSource)
    {
        var preview = new MockOAuthServer();
        var scheme = OAuth2TestSupport.ClientCredentialsScheme(preview) with
        {
            ClientSource = clientSource,
            ClientId = clientSource == OAuth2ClientSources.BringYourOwn ? preview.ExpectedClientId : null
        };
        var (harness, server) = await OAuth2TestSupport.StartAsync(scheme, clientSource);
        await using var _ = harness;

        var completion = await OAuth2TestSupport.SendAsync(harness, server.ResourceEndpoint);
        Assert.Equal(200, completion.Result.StatusCode);
    }
}

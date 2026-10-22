using System.Text.Json;

namespace Elsa.Integrations.Connectors.OAuth2.Tests;

public sealed class HttpConnectionTests
{
    [Fact]
    public async Task SendHttpRequest_uses_the_http_connection_and_injects_the_token()
    {
        var preview = new MockOAuthServer();
        await using var harness = await OAuth2TestSupport.StartAsync(OAuth2TestSupport.ClientCredentialsScheme(preview));
        var server = harness.Server;

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
        await using var harness = await OAuth2TestSupport.StartAsync(OAuth2TestSupport.AuthorizationCodeScheme(preview));
        var server = harness.Server;

        await OAuth2TestSupport.AuthorizeAsync(harness, server);

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
        await using var harness = await OAuth2TestSupport.StartAsync(scheme, clientSource);
        var server = harness.Server;

        var completion = await OAuth2TestSupport.SendAsync(harness, server.ResourceEndpoint);
        Assert.Equal(200, completion.Result.StatusCode);
    }
}

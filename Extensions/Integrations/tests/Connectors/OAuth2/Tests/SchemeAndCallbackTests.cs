using Elsa.Integrations.Connectors.OAuth2;
using Elsa.Integrations.Sdk;
using Elsa.Integrations.Sdk.OAuth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;

namespace Elsa.Integrations.Connectors.OAuth2.Tests;

public sealed class SchemeAndCallbackTests
{
    [Fact]
    public void OAuth2_is_one_authentication_scheme()
    {
        Assert.Equal("oauth2", OAuth2AuthenticationScheme.Name);
        Assert.True(OAuth2AuthenticationScheme.RequiresWritableStore);
        Assert.True(IntegrationsSdkStub.IsStub);
    }

    [Fact]
    public void Callback_uri_is_public_base_url_plus_foundation_path()
    {
        var options = new OAuthCallbackOptions { PublicBaseUrl = "https://elsa.example" };
        Assert.Equal("/_elsa/connections/oauth/callback", OAuthCallbackOptions.Path);
        Assert.Equal("https://elsa.example/_elsa/connections/oauth/callback", options.CallbackUri.ToString());
    }

    [Fact]
    public async Task Backend_serves_the_callback_path()
    {
        var scheme = OAuth2TestSupport.AuthorizationCodeScheme(new MockOAuthServer());
        var (harness, server) = await OAuth2TestSupport.StartAsync(scheme);
        await using var _ = harness;

        var started = await harness.Tokens.BeginAuthorizationCodeAsync(
            OAuth2TestSupport.BoundConnection(harness),
            harness.Callback);
        var code = server.ApproveAuthorization(new Uri(started.AuthorizationUrl));

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();
        app.MapMethods(OAuthCallbackOptions.Path, ["GET", "POST"], async (string code, string state) =>
        {
            var result = await harness.CallbackHandler.HandleAsync(OAuth2TestSupport.BoundConnection(harness), code, state);
            return result.Ok ? Results.Ok() : Results.BadRequest(result.Error);
        });
        await app.StartAsync();

        var client = app.GetTestClient();
        var response = await client.GetAsync($"{OAuthCallbackOptions.Path}?code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(started.State)}");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

        var grant = await harness.Tokens.ReadGrantAsync(OAuth2TestSupport.BoundConnection(harness), harness.Mask, OAuth2TestSupport.ActivityExecutionId, CancellationToken.None);
        Assert.NotNull(grant);
        Assert.True(grant.HasAccessToken);
    }
}

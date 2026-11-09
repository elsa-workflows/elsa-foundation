using Elsa.Connections.Api;
using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Integrations.Connectors.OAuth2.Tests;

public sealed class SchemeAndCallbackTests
{
    [Fact]
    public void OAuth2_is_one_authentication_scheme()
    {
        Assert.Equal(HttpAuthSchemes.OAuth2, OAuth2AuthenticationScheme.Name);
        Assert.True(OAuth2AuthenticationScheme.RequiresWritableStore);
    }

    [Fact]
    public void Callback_uri_is_public_base_url_plus_foundation_path()
    {
        Assert.Equal("/_elsa/connections/oauth/callback", ConnectionsApi.OAuthCallbackPath);
        Assert.Equal(
            "https://elsa.example/_elsa/connections/oauth/callback",
            "https://elsa.example".TrimEnd('/') + ConnectionsApi.OAuthCallbackPath);
    }

    [Fact]
    public async Task Backend_serves_the_callback_path()
    {
        var preview = new MockOAuthServer();
        await using var harness = await OAuth2TestSupport.StartAsync(OAuth2TestSupport.AuthorizationCodeScheme(preview));
        var tokens = harness.Services.GetRequiredService<OAuth2TokenService>();
        var resolver = harness.Services.GetRequiredService<IConnectionResolver>();
        await using var connection = await resolver.ResolveAsync(new ConnectionResolveRequest
        {
            TenantId = OAuth2TestSupport.TenantId,
            Name = OAuth2TestSupport.ConnectionName,
            RequiredType = ConnectionTypeNames.Http,
            Owner = ConnectionOwners.Tenant
        });
        var started = await tokens.BeginAuthorizationCodeAsync(connection);
        var code = harness.Server.ApproveAuthorization(new Uri(started.AuthorizationUrl));

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(harness.Services.GetRequiredService<IEnumerable<IOAuthCallbackHandler>>());
        await using var app = builder.Build();
        app.MapGet(ConnectionsApi.OAuthCallbackPath, async (HttpContext context) =>
        {
            var request = new OAuthCallbackRequest(
                context.Request.Query["code"],
                context.Request.Query["state"],
                context.Request.Query["error"],
                context.Request.Query["error_description"]);
            foreach (var handler in context.RequestServices.GetRequiredService<IEnumerable<IOAuthCallbackHandler>>())
            {
                var handled = await handler.TryHandleAsync(request);
                if (handled is not null)
                {
                    context.Response.StatusCode = handled.StatusCode;
                    return;
                }
            }

            context.Response.StatusCode = 400;
        });
        await app.StartAsync();

        var client = app.GetTestClient();
        var response = await client.GetAsync($"{ConnectionsApi.OAuthCallbackPath}?code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(started.State)}");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }
}

using System.Net;
using System.Net.Http.Headers;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.OpenIddict;
using Elsa.Foundation.Identity.OpenIddict.EntityFrameworkCore;
using Elsa.Foundation.Identity.OpenIddict.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Foundation.Identity.Tests.OpenIddict;

/// <summary>
/// B2/B4 integration: with only the OpenIddict feature registered (no Elsa.Server wiring), the validation
/// handler — reached through the default selector scheme — authenticates <c>Authorization: Bearer</c> calls
/// to a protected endpoint with freshly issued tokens and rejects missing/tampered/expired/revoked ones.
/// </summary>
public sealed class OpenIddictBearerAuthenticationTests
{
    [Fact]
    public async Task Protected_Endpoint_Accepts_A_Freshly_Issued_Token_And_Sees_Its_Claims()
    {
        using var host = await StartHostAsync();
        var issued = await IssueAsync(host);

        var response = await CallProtectedAsync(host, issued.AccessToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("tenant-a", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Protected_Endpoint_Rejects_Anonymous_Calls_With_401()
    {
        using var host = await StartHostAsync();

        var response = await CallProtectedAsync(host, accessToken: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Protected_Endpoint_Rejects_A_Tampered_Token()
    {
        using var host = await StartHostAsync();
        var issued = await IssueAsync(host);

        var response = await CallProtectedAsync(host, OpenIddictTokenServiceTests.Tamper(issued.AccessToken));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Protected_Endpoint_Rejects_An_Expired_Token()
    {
        using var host = await StartHostAsync(options => options.AccessTokenLifetime = TimeSpan.FromMinutes(-15));
        var issued = await IssueAsync(host);

        var response = await CallProtectedAsync(host, issued.AccessToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Protected_Endpoint_Rejects_A_Revoked_Token()
    {
        using var host = await StartHostAsync();
        var issued = await IssueAsync(host);
        Assert.Equal(HttpStatusCode.OK, (await CallProtectedAsync(host, issued.AccessToken)).StatusCode);

        await using (var scope = host.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<ITokenService>().RevokeAsync(new TokenRevocationRequest(issued.AccessToken));

        Assert.Equal(HttpStatusCode.Unauthorized, (await CallProtectedAsync(host, issued.AccessToken)).StatusCode);
    }

    private static async Task<IHost> StartHostAsync(Action<OpenIddictIdentityOptions>? configure = null)
    {
        var databaseName = $"openiddict-http-{Guid.NewGuid():n}";

        var host = await new HostBuilder()
            // The dev/demo OpenIddict feature registers a DevelopmentOrDemoGuard that hard-fails startup
            // outside Development (HostBuilder defaults to Production); this host runs legitimately in dev/demo.
            .UseEnvironment(Environments.Development)
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddRouting();
                    services.AddAuthorization();
                    services.AddFoundationIdentityOpenIddict(
                        options =>
                        {
                            options.IsDevelopmentOrDemo = true;
                            configure?.Invoke(options);
                        },
                        configureDbContext: builder => builder.UseInMemoryDatabase(databaseName));
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints
                        .MapGet("/protected", context =>
                            context.Response.WriteAsync(context.User.FindFirst(IdentityClaimTypes.TenantId)?.Value ?? string.Empty))
                        .RequireAuthorization());
                });
            })
            .StartAsync();

        await using var scope = host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<OpenIddictIdentityDbContext>().Database.EnsureCreatedAsync();

        return host;
    }

    private static async ValueTask<TokenIssueResult> IssueAsync(IHost host)
    {
        // Resolve the (scoped) token service from a scope — the Development environment enables scope validation.
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ITokenService>()
            .IssueAsync(new TokenIssueRequest("user-1", "tenant-a", ["identity.users.read"]));
    }

    private static async Task<HttpResponseMessage> CallProtectedAsync(IHost host, string? accessToken)
    {
        var client = host.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/protected");

        if (accessToken is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return await client.SendAsync(request);
    }
}

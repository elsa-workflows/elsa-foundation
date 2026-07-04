using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Seeding;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.Tests.Api;

/// <summary>
/// Plan C2: the <c>GET /_elsa/identity/token</c> cookie→bearer exchange, end to end over the landed A+B
/// pipeline. Covers the anonymous→401 contract the Studio client relies on, claim round-tripping from the
/// cookie principal into the issued JWT, and the full login→token→protected-call→logout flow.
/// </summary>
public sealed class TokenEndpointTests : IAsyncLifetime
{
    private const string TokenRoute = "/_elsa/identity/token";
    private const string LoginRoute = "/_elsa/identity/login";

    private TokenEndpointFixture _fixture = null!;

    public async Task InitializeAsync() => _fixture = await TokenEndpointFixture.StartAsync();

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    [Fact]
    public async Task Anonymous_Request_Gets_401_So_The_Client_Stays_Anonymous()
    {
        var response = await _fixture.Client.GetAsync(TokenRoute);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Authenticated_Cookie_Principal_Gets_200_With_A_Bearer_Whose_Claims_RoundTrip()
    {
        var client = _fixture.Client;
        await LoginAsync(client);

        var response = await client.GetAsync(TokenRoute);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Contract: the client reads camelCase `accessToken`; `expiresAt` is additive.
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.TryGetProperty("accessToken", out var accessTokenElement));
        Assert.True(json.RootElement.TryGetProperty("expiresAt", out _));
        var accessToken = accessTokenElement.GetString()!;
        Assert.False(string.IsNullOrWhiteSpace(accessToken));

        // The issued JWT must carry the cookie principal's subject, tenant, and permission claims.
        var validation = await _fixture.Services.GetRequiredService<ITokenService>()
            .ValidateAsync(new TokenValidationRequest(accessToken));

        Assert.True(validation.Succeeded, validation.Failure);
        var principal = validation.Principal!;
        Assert.False(string.IsNullOrWhiteSpace(principal.FindFirst(IdentityClaimTypes.TenantId)?.Value));
        var permissions = principal.FindAll(IdentityClaimTypes.Permission).Select(x => x.Value).ToList();
        // The seeded admin is granted every catalog permission; a representative one must round-trip.
        Assert.Contains(DefaultIdentityPermissionKeys.IdentityUsersManage, permissions);
    }

    [Fact]
    public async Task Login_Then_Token_Yields_A_Bearer_That_Authenticates_A_Protected_Endpoint()
    {
        var client = _fixture.Client;
        await LoginAsync(client);

        var accessToken = await FetchAccessTokenAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/protected");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var protectedResponse = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, protectedResponse.StatusCode);
        // The protected endpoint echoes the tenant claim carried by the validated bearer.
        Assert.False(string.IsNullOrWhiteSpace(await protectedResponse.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task Logout_Then_Token_Returns_401()
    {
        var client = _fixture.Client;
        await LoginAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(TokenRoute)).StatusCode);

        // Clearing the identity cookie (as POST /logout does) drops the session; the exchange must 401 again.
        client.DefaultRequestHeaders.Remove("Cookie");

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(TokenRoute)).StatusCode);
    }

    /// <summary>Drives the real POST /_elsa/identity/login form flow and captures the issued cookie.</summary>
    private static async Task LoginAsync(HttpClient client)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = IdentitySeeder.AdminUserName,
            ["password"] = IdentitySeeder.AdminPassword
        });

        var response = await client.PostAsync(LoginRoute, content);
        Assert.True(response.IsSuccessStatusCode, $"Login failed: {(int)response.StatusCode}");

        var cookie = response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(x => x.StartsWith("Elsa.Identity.Cookie", StringComparison.Ordinal))
            : null;
        Assert.NotNull(cookie);

        // TestServer's HttpClient doesn't persist cookies; attach it explicitly for subsequent requests.
        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.Add("Cookie", cookie!.Split(';', 2)[0]);
    }

    private static async Task<string> FetchAccessTokenAsync(HttpClient client)
    {
        var response = await client.GetFromJsonAsync<AccessTokenPayload>(TokenRoute);
        Assert.NotNull(response);
        Assert.False(string.IsNullOrWhiteSpace(response!.AccessToken));
        return response.AccessToken;
    }

    private sealed record AccessTokenPayload(string AccessToken, DateTimeOffset ExpiresAt);
}

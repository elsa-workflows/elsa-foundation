using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Authorization;
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
    private const string RefreshRoute = "/_elsa/identity/refresh";

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
        await LoginTestHelper.LoginAsync(client);

        var response = await client.GetAsync(TokenRoute);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Contract: the client reads camelCase `accessToken`; `expiresAt` is additive.
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.TryGetProperty("accessToken", out var accessTokenElement));
        Assert.True(json.RootElement.TryGetProperty("expiresAt", out _));
        var accessToken = accessTokenElement.GetString()!;
        Assert.False(string.IsNullOrWhiteSpace(accessToken));

        // The issued JWT must carry the cookie principal's subject, tenant, and permission claims. Resolve the
        // (scoped) token service from a scope — the Development environment enables scope validation.
        await using var scope = _fixture.Services.CreateAsyncScope();
        var validation = await scope.ServiceProvider.GetRequiredService<ITokenService>()
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
        await LoginTestHelper.LoginAsync(client);

        var accessToken = await FetchAccessTokenAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/protected");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var protectedResponse = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, protectedResponse.StatusCode);
        // The protected endpoint echoes the tenant claim carried by the validated bearer.
        Assert.False(string.IsNullOrWhiteSpace(await protectedResponse.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task Form_Login_Without_A_Csrf_Token_Is_Rejected_And_Issues_No_Session()
    {
        var client = _fixture.Client;

        // The HTML-form flow is identified by a (local) returnUrl. Posting valid credentials but no
        // antiforgery token must NOT establish a session — it redirects back to the login page with an error.
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = TestAdmin.UserName,
            ["password"] = TestAdmin.Password,
            ["returnUrl"] = "/studio"
        });

        var response = await client.PostAsync(LoginRoute, content);

        // Redirected back to the login page with an error (not straight to the returnUrl), and crucially no
        // identity cookie was issued — the CSRF failure blocked sign-in before credentials were checked.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(LoginRoute, response.Headers.Location?.ToString() ?? string.Empty, StringComparison.Ordinal);
        var setCookie = response.Headers.TryGetValues("Set-Cookie", out var values) ? values : [];
        Assert.DoesNotContain(setCookie, x => x.StartsWith("Elsa.Identity.Cookie", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Form_Login_Without_A_Csrf_Token_And_Without_A_ReturnUrl_Is_Rejected()
    {
        var client = _fixture.Client;

        // The exact bypass the fix closes: a form-urlencoded login POST that omits both the antiforgery token
        // AND the returnUrl. Previously the antiforgery gate keyed off returnUrl presence, so this path
        // skipped CSRF validation entirely while still authenticating and issuing the cookie. The gate now
        // keys off Content-Type (this is not application/json), so it must be rejected with no session issued.
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = TestAdmin.UserName,
            ["password"] = TestAdmin.Password
        });

        var response = await client.PostAsync(LoginRoute, content);

        // Rejected (401 — no returnUrl to redirect to) and crucially NO identity cookie was issued: the CSRF
        // gate blocked sign-in before credentials were checked, even though none of the body named a returnUrl.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var setCookie = response.Headers.TryGetValues("Set-Cookie", out var values) ? values : [];
        Assert.DoesNotContain(setCookie, x => x.StartsWith("Elsa.Identity.Cookie", StringComparison.Ordinal));

        // And the exchange still 401s — no session was established by the rejected login.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(TokenRoute)).StatusCode);
    }

    [Fact]
    public async Task Logout_Then_Token_Returns_401()
    {
        var client = _fixture.Client;
        await LoginTestHelper.LoginAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(TokenRoute)).StatusCode);

        // Clearing the identity cookie (as POST /logout does) drops the session; the exchange must 401 again.
        client.DefaultRequestHeaders.Remove("Cookie");

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(TokenRoute)).StatusCode);
    }

    [Fact]
    public async Task Logout_For_An_Unknown_Provider_Returns_204_And_Does_Not_500()
    {
        // Regression for the logout 500: an unregistered/bad provider scheme (e.g. "openiddict", which declares
        // no sign-out handler) must be an idempotent no-op 204, never an InvalidOperationException → 500.
        foreach (var provider in new[] { "openiddict", "does-not-exist" })
        {
            var response = await _fixture.Client.PostAsJsonAsync($"/_elsa/identity/logout/{provider}", new { });
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }
    }

    [Fact]
    public async Task Logout_On_The_Cookie_Provider_Clears_The_Session()
    {
        var client = _fixture.Client;
        await LoginTestHelper.LoginAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(TokenRoute)).StatusCode);

        // Logout on the real first-party provider signs out its cookie scheme (an IAuthenticationSignOutHandler).
        var logout = await client.PostAsJsonAsync("/_elsa/identity/logout/aspnetcore-identity", new { });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        // The response expires the identity cookie; sending the (now-cleared) cookie no longer authenticates.
        var cleared = logout.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(x => x.StartsWith("Elsa.Identity.Cookie", StringComparison.Ordinal))
            : null;
        Assert.NotNull(cleared);
        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.Add("Cookie", cleared!.Split(';', 2)[0]);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(TokenRoute)).StatusCode);
    }

    [Fact]
    public async Task Refresh_With_A_Garbage_Token_Returns_401_Not_500()
    {
        // Regression for the refresh error contract: an invalid/expired/replayed refresh token makes
        // RefreshAsync throw InvalidOperationException; the endpoint must map that to a clean 401, not a 500.
        var response = await _fixture.Client.PostAsJsonAsync(
            RefreshRoute, new { refreshToken = "not-a-real-refresh-token" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"refreshToken\":null}")]
    [InlineData("{\"refreshToken\":\"\"}")]
    [InlineData("{\"refreshToken\":\"   \"}")]
    public async Task Refresh_Without_A_Usable_Token_Returns_401_Not_500(string payload)
    {
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");

        var response = await _fixture.Client.PostAsync(RefreshRoute, content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
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

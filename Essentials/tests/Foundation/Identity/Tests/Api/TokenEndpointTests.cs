using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.OpenIddict;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Core;
using OpenIddict.EntityFrameworkCore.Models;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace Elsa.Foundation.Identity.Tests.Api;

/// <summary>
/// Plan C2: the <c>GET /_elsa/identity/token</c> cookie→bearer exchange, end to end over the landed A+B
/// pipeline. Covers the anonymous→401 contract the Studio client relies on, claim round-tripping from the
/// cookie principal into the issued JWT, and the full login→token→protected-call→logout flow.
/// </summary>
[Collection(FastEndpointsHostCollection.Name)]
public sealed class TokenEndpointTests : IAsyncLifetime
{
    private const string TokenRoute = "/_elsa/identity/token";
    private const string LoginRoute = "/_elsa/identity/login";
    private const string RefreshRoute = "/_elsa/identity/refresh";
    private const string LogoutRoute = "/_elsa/identity/logout/aspnetcore-identity";

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
        Assert.Equal(principal.FindFirst(ClaimTypes.NameIdentifier)!.Value, Assert.Single(principal.FindAll(IdentityClaimTypes.UserId)).Value);
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
    public async Task FirstParty_Bearer_Cannot_Be_Exchanged_For_A_Second_Token()
    {
        var client = _fixture.Client;
        await LoginTestHelper.LoginAsync(client);
        var accessToken = await FetchAccessTokenAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Get, TokenRoute);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Unnormalized_External_Principal_Cannot_Exchange_Forged_Internal_Permissions()
    {
        using var exchangeRequest = new HttpRequestMessage(HttpMethod.Get, TokenRoute);
        exchangeRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", ForgedExternalAuthenticationHandler.Token);

        var exchangeResponse = await _fixture.Client.SendAsync(exchangeRequest);

        Assert.Equal(HttpStatusCode.Unauthorized, exchangeResponse.StatusCode);

        using var protectedRequest = new HttpRequestMessage(HttpMethod.Get, "/permission-protected");
        protectedRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", ForgedExternalAuthenticationHandler.Token);

        var protectedResponse = await _fixture.Client.SendAsync(protectedRequest);

        // Untrusted identities are intentionally challenged rather than treated as normalized callers that
        // merely lack a grant, so this is 401 (trusted-but-underprivileged callers receive 403).
        Assert.Equal(HttpStatusCode.Unauthorized, protectedResponse.StatusCode);
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
    public async Task Login_Page_Drops_Remote_Return_Urls()
    {
        var response = await _fixture.Client.GetAsync($"{LoginRoute}?returnUrl=https%3A%2F%2Fevil.example%2Flanding");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("name=\"returnUrl\" value=\"/\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("evil.example", html, StringComparison.Ordinal);
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
    public async Task Logout_Revokes_The_Sessions_Tokens_So_An_Old_Refresh_Token_Gets_401()
    {
        var client = _fixture.Client;
        await LoginTestHelper.LoginAsync(client);
        var accessToken = await FetchAccessTokenAsync(client);

        // The cookie exchange stores a refresh token but does not hand it out, so issue the pair a
        // refresh-capable client would hold for the same subject. The control proves refresh works pre-logout.
        var control = await IssueRefreshTokenForAsync(accessToken);
        var refreshToken = await IssueRefreshTokenForAsync(accessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(RefreshRoute, new { refreshToken = control })).StatusCode);

        var logout = await client.PostAsJsonAsync(LogoutRoute, new { });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        // A stale tab can neither refresh nor keep using the bearer it already had.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(RefreshRoute, new { refreshToken })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await CallProtectedAsync(client, accessToken));
    }

    [Fact]
    public async Task Logout_Revokes_Only_The_Signed_Out_Subjects_Tokens()
    {
        var client = _fixture.Client;
        await LoginTestHelper.LoginAsync(client);
        var accessToken = await FetchAccessTokenAsync(client);
        var other = await IssueForAsync(_fixture, accessToken, subject: "other-user");

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync(LogoutRoute, new { })).StatusCode);

        // The signed-out subject's bearer is dead; another user's refresh token and bearer are untouched.
        Assert.Equal(HttpStatusCode.Unauthorized, await CallProtectedAsync(client, accessToken));
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(RefreshRoute, new { refreshToken = other.RefreshToken })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, await CallProtectedAsync(client, other.AccessToken));
    }

    [Fact]
    public async Task Logout_That_Cannot_Revoke_Returns_503_Keeps_The_Session_And_A_Retry_Revokes()
    {
        var fault = new RevocationFault { Armed = true };
        await using var faulting = await TokenEndpointFixture.StartAsync(services =>
        {
            services.AddScoped<OpenIddictTokenService>();
            services.AddScoped<ITokenService>(provider => new RevocationFaultTokenService(provider.GetRequiredService<OpenIddictTokenService>(), fault));
        });

        await AssertFailedLogoutKeepsTheSessionAndARetryRevokesAsync(faulting, fault);
    }

    [Fact]
    public async Task Logout_Whose_Store_Declines_A_Revocation_Returns_503_Keeps_The_Session_And_A_Retry_Revokes()
    {
        // OpenIddict's TryRevokeAsync answers false (no throw) on a non-fatal update failure such as a concurrency
        // conflict. One entry declining must still fail the sign-out rather than leave that token valid.
        var fault = new RevocationFault { Armed = true };
        await using var faulting = await TokenEndpointFixture.StartAsync(services =>
        {
            services.AddSingleton(fault);
            services.AddOpenIddict().AddCore(core =>
                core.ReplaceTokenManager<OpenIddictEntityFrameworkCoreToken, DecliningTokenManager>());
        });

        await AssertFailedLogoutKeepsTheSessionAndARetryRevokesAsync(faulting, fault);
    }

    [Fact]
    public async Task Logout_With_Another_Subjects_Bearer_Ends_The_Cookie_Session_And_Leaves_The_Bearer_Subject_Alone()
    {
        var client = _fixture.Client;
        await LoginTestHelper.LoginAsync(client);
        var sessionToken = await FetchAccessTokenAsync(client);
        var sessionRefresh = await IssueRefreshTokenForAsync(sessionToken);
        var other = await IssueForAsync(_fixture, sessionToken, subject: "other-user");

        // The selector would pick the bearer, but logout acts on the provider's cookie session.
        Assert.Equal(HttpStatusCode.NoContent, await LogoutWithBearerAsync(client, other.AccessToken));

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(TokenRoute)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(RefreshRoute, new { refreshToken = sessionRefresh })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await CallProtectedAsync(client, sessionToken));
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(RefreshRoute, new { refreshToken = other.RefreshToken })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, await CallProtectedAsync(client, other.AccessToken));
    }

    [Fact]
    public async Task Bearer_Only_Logout_Signs_Out_The_Bearers_Own_Subject_And_No_Other()
    {
        var client = _fixture.Client;
        await LoginTestHelper.LoginAsync(client);
        var accessToken = await FetchAccessTokenAsync(client);
        var refreshToken = await IssueRefreshTokenForAsync(accessToken);
        var other = await IssueForAsync(_fixture, accessToken, subject: "other-user");
        var cookie = WithoutCookie(client);

        Assert.Equal(HttpStatusCode.NoContent, await LogoutWithBearerAsync(client, accessToken));

        Assert.Equal(HttpStatusCode.Unauthorized, await CallProtectedAsync(client, accessToken));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(RefreshRoute, new { refreshToken })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(RefreshRoute, new { refreshToken = other.RefreshToken })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, await CallProtectedAsync(client, other.AccessToken));

        // The subject is an Identity user, so its security stamp rotated: the cookie it also held is dead too.
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(TokenRoute)).StatusCode);
    }

    [Fact]
    public async Task Bearer_Only_Logout_Of_A_Subject_That_Is_No_Identity_User_Revokes_And_Returns_204()
    {
        var client = _fixture.Client;
        await LoginTestHelper.LoginAsync(client);
        var other = await IssueForAsync(_fixture, await FetchAccessTokenAsync(client), subject: "other-user");
        var cookie = WithoutCookie(client);

        // No Identity user resolves, so there is no stamp to rotate: revoke, then 204, never a 500.
        Assert.Equal(HttpStatusCode.NoContent, await LogoutWithBearerAsync(client, other.AccessToken));

        Assert.Equal(HttpStatusCode.Unauthorized, await CallProtectedAsync(client, other.AccessToken));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(RefreshRoute, new { refreshToken = other.RefreshToken })).StatusCode);
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(TokenRoute)).StatusCode);
    }

    [Fact]
    public async Task Refresh_For_A_Disabled_User_Returns_401_Issues_Nothing_And_Revokes_Their_Other_Refresh_Tokens()
    {
        var client = _fixture.Client;
        await LoginTestHelper.LoginAsync(client);
        var accessToken = await FetchAccessTokenAsync(client);
        var first = await IssueRefreshTokenForAsync(accessToken);
        var second = await IssueRefreshTokenForAsync(accessToken);
        var (tenantId, userId) = await UserOfAsync(accessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(RefreshRoute, new { refreshToken = await IssueRefreshTokenForAsync(accessToken) })).StatusCode);

        await SetUserStatusAsync(tenantId, userId, UserStatus.Disabled);

        // invalid_grant on the wire is a bare 401 with no token in the body.
        var refused = await client.PostAsJsonAsync(RefreshRoute, new { refreshToken = first });
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.DoesNotContain("accessToken", await refused.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        // The refusal revoked the user's other outstanding refresh token too, and re-enabling does not revive it.
        await SetUserStatusAsync(tenantId, userId, UserStatus.Active);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(RefreshRoute, new { refreshToken = second })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await CallProtectedAsync(client, accessToken));
    }

    [Fact]
    public async Task Refresh_For_A_Deleted_User_Returns_401()
    {
        var client = _fixture.Client;
        await LoginTestHelper.LoginAsync(client);
        var accessToken = await FetchAccessTokenAsync(client);
        var refreshToken = await IssueRefreshTokenForAsync(accessToken);
        var (tenantId, userId) = await UserOfAsync(accessToken);

        await using (var scope = _fixture.Services.CreateAsyncScope())
            Assert.True(await scope.ServiceProvider.GetRequiredService<IUserStore>().DeleteAsync(tenantId, userId));

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(RefreshRoute, new { refreshToken })).StatusCode);
    }

    [Fact]
    public async Task Logout_Leaves_A_Provider_Subject_Equal_To_The_Users_Id_Alone()
    {
        var client = _fixture.Client;
        await LoginTestHelper.LoginAsync(client);
        var accessToken = await FetchAccessTokenAsync(client);
        var (_, userId) = await UserOfAsync(accessToken);
        // Another provider's sub happens to equal the Elsa user id; it is a different subject.
        var lookalike = await IssueForAsync(_fixture, accessToken, subject: userId, provider: "other-idp");

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync(LogoutRoute, new { })).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, await CallProtectedAsync(client, accessToken));
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(RefreshRoute, new { refreshToken = lookalike.RefreshToken })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, await CallProtectedAsync(client, lookalike.AccessToken));
    }

    [Fact]
    public async Task External_Bearers_Of_Two_Providers_Sharing_A_Sub_Exchange_And_Sign_Out_Apart()
    {
        // Two normalized external bearers, two providers, the same sub. Each exchanges for its own first-party token.
        var client = _fixture.Client;
        var first = await ExchangeExternalAsync(client, NormalizedExternalAuthenticationHandler.Token("idp-1", "shared-sub"));
        var second = await ExchangeExternalAsync(client, NormalizedExternalAuthenticationHandler.Token("idp-2", "shared-sub"));
        var firstRefresh = await IssueRefreshTokenForExternalAsync(first, "idp-1");
        var secondRefresh = await IssueRefreshTokenForExternalAsync(second, "idp-2");
        Assert.Equal("shared-sub", await SubjectOfAsync(first));
        Assert.Equal("shared-sub", await SubjectOfAsync(second));

        // The first provider's subject signs out with its bearer.
        Assert.Equal(HttpStatusCode.NoContent, await LogoutWithBearerAsync(client, first));

        Assert.Equal(HttpStatusCode.Unauthorized, await CallProtectedAsync(client, first));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(RefreshRoute, new { refreshToken = firstRefresh })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, await CallProtectedAsync(client, second));
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(RefreshRoute, new { refreshToken = secondRefresh })).StatusCode);
    }

    [Fact]
    public async Task An_External_Bearer_Whose_Sub_Equals_An_Elsa_User_Id_Does_Not_Share_The_Users_Tokens()
    {
        var client = _fixture.Client;
        await LoginTestHelper.LoginAsync(client);
        var userToken = await FetchAccessTokenAsync(client);
        var (_, userId) = await UserOfAsync(userToken);
        var userRefresh = await IssueRefreshTokenForAsync(userToken);
        // The external client holds no cookie; its bearer alone is exchanged.
        WithoutCookie(client);
        var external = await ExchangeExternalAsync(client, NormalizedExternalAuthenticationHandler.Token("idp-1", userId));
        var externalRefresh = await IssueRefreshTokenForExternalAsync(external, "idp-1");
        Assert.Equal(userId, await SubjectOfAsync(external));

        // The external subject signs out: the Elsa user's tokens are untouched.
        Assert.Equal(HttpStatusCode.NoContent, await LogoutWithBearerAsync(client, external));
        Assert.Equal(HttpStatusCode.Unauthorized, await CallProtectedAsync(client, external));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(RefreshRoute, new { refreshToken = externalRefresh })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, await CallProtectedAsync(client, userToken));
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(RefreshRoute, new { refreshToken = userRefresh })).StatusCode);
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

    [Fact]
    public async Task Refresh_With_Unsupported_Content_Type_Returns_415()
    {
        using var content = new StringContent("refresh-token", Encoding.UTF8, "text/plain");

        var response = await _fixture.Client.PostAsync(RefreshRoute, content);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{}")]
    [InlineData("{\"refreshToken\":null}")]
    [InlineData("{\"refreshToken\":\"\"}")]
    [InlineData("{\"refreshToken\":\"   \"}")]
    public async Task Refresh_Invalid_Or_Empty_Input_Uses_Stable_4xx_Contracts(string payload)
    {
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");

        var response = await _fixture.Client.PostAsync(RefreshRoute, content);

        // Malformed JSON is a binding failure (400); a well-formed but unusable credential is anonymous (401).
        Assert.Equal(payload == "{" ? HttpStatusCode.BadRequest : HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<string> FetchAccessTokenAsync(HttpClient client)
    {
        var response = await client.GetFromJsonAsync<AccessTokenPayload>(TokenRoute);
        Assert.NotNull(response);
        Assert.False(string.IsNullOrWhiteSpace(response!.AccessToken));
        return response.AccessToken;
    }

    /// <summary>With the fault armed logout fails and keeps the session; once it clears, a retry revokes.</summary>
    private static async Task AssertFailedLogoutKeepsTheSessionAndARetryRevokesAsync(TokenEndpointFixture fixture, RevocationFault fault)
    {
        var client = fixture.Client;
        await LoginTestHelper.LoginAsync(client);
        var accessToken = await FetchAccessTokenAsync(client);
        var refreshToken = (await IssueForAsync(fixture, accessToken)).RefreshToken;

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync(LogoutRoute, new { })).StatusCode);

        // The security stamp did not rotate: the same cookie still exchanges, so the retry runs as the subject.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(TokenRoute)).StatusCode);

        fault.Armed = false;
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync(LogoutRoute, new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(RefreshRoute, new { refreshToken })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await CallProtectedAsync(client, accessToken));
    }

    /// <summary>Posts logout carrying the bearer (plus whatever cookie the client holds).</summary>
    private static async Task<HttpStatusCode> LogoutWithBearerAsync(HttpClient client, string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, LogoutRoute) { Content = JsonContent.Create(new { }) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return (await client.SendAsync(request)).StatusCode;
    }

    /// <summary>Drops the client's cookie so the next request carries no provider session; returns it.</summary>
    private static string WithoutCookie(HttpClient client)
    {
        var cookie = client.DefaultRequestHeaders.GetValues("Cookie").Single();
        client.DefaultRequestHeaders.Remove("Cookie");
        return cookie;
    }

    private async Task<string> IssueRefreshTokenForAsync(string accessToken) => (await IssueForAsync(_fixture, accessToken)).RefreshToken!;

    /// <summary>
    /// Issues a pair in the access token's tenant, for its subject unless another is named. For its own subject the
    /// pair names the Elsa user, as the exchange does; a named other subject is no Elsa user and is issued under
    /// <paramref name="provider"/>.
    /// </summary>
    private static async Task<TokenIssueResult> IssueForAsync(TokenEndpointFixture fixture, string accessToken, string? subject = null, string provider = "test-provider")
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var principal = (await tokens.ValidateAsync(new TokenValidationRequest(accessToken))).Principal!;
        return await tokens.IssueAsync(new TokenIssueRequest(
            subject ?? principal.FindFirst(ClaimTypes.NameIdentifier)!.Value, principal.FindFirst(IdentityClaimTypes.TenantId)!.Value, [])
        {
            CanonicalUserId = subject is null ? principal.FindFirst(IdentityClaimTypes.UserId)?.Value : null,
            Provider = subject is null ? null : provider
        });
    }

    /// <summary>Exchanges a normalized external bearer for a first-party access token.</summary>
    private static async Task<string> ExchangeExternalAsync(HttpClient client, string externalToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, TokenRoute);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", externalToken);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<AccessTokenPayload>();
        return payload!.AccessToken;
    }

    /// <summary>Issues the refresh token a refresh-capable external client would hold: the bearer's own subject under its provider.</summary>
    private async Task<string> IssueRefreshTokenForExternalAsync(string accessToken, string provider)
    {
        await using var scope = _fixture.Services.CreateAsyncScope();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var principal = (await tokens.ValidateAsync(new TokenValidationRequest(accessToken))).Principal!;
        Assert.Empty(principal.FindAll(IdentityClaimTypes.UserId));
        return (await tokens.IssueAsync(new TokenIssueRequest(
            principal.FindFirst(ClaimTypes.NameIdentifier)!.Value, principal.FindFirst(IdentityClaimTypes.TenantId)!.Value, []) { Provider = provider })).RefreshToken!;
    }

    private async Task<string> SubjectOfAsync(string accessToken)
    {
        await using var scope = _fixture.Services.CreateAsyncScope();
        var principal = (await scope.ServiceProvider.GetRequiredService<ITokenService>().ValidateAsync(new TokenValidationRequest(accessToken))).Principal!;
        return principal.FindFirst(ClaimTypes.NameIdentifier)!.Value;
    }

    /// <summary>The tenant and Elsa user the access token was issued to.</summary>
    private async Task<(string TenantId, string UserId)> UserOfAsync(string accessToken)
    {
        await using var scope = _fixture.Services.CreateAsyncScope();
        var principal = (await scope.ServiceProvider.GetRequiredService<ITokenService>().ValidateAsync(new TokenValidationRequest(accessToken))).Principal!;
        return (principal.FindFirst(IdentityClaimTypes.TenantId)!.Value, Assert.Single(principal.FindAll(IdentityClaimTypes.UserId)).Value);
    }

    private async Task SetUserStatusAsync(string tenantId, string userId, UserStatus status)
    {
        await using var scope = _fixture.Services.CreateAsyncScope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<IUserStore>().SetStatusAsync(tenantId, userId, status));
    }

    private static async Task<HttpStatusCode> CallProtectedAsync(HttpClient client, string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/protected");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return (await client.SendAsync(request)).StatusCode;
    }

    private sealed record AccessTokenPayload(string AccessToken, DateTimeOffset ExpiresAt);

    private sealed class RevocationFault
    {
        public bool Armed { get; set; }

        /// <summary>The one entry a declining store refuses while armed: the first it is asked to revoke.</summary>
        public string? DeclinedEntryId { get; set; }
    }

    /// <summary>The real token manager, except that while armed it declines (returns false for) one entry.</summary>
    private sealed class DecliningTokenManager(
        IOpenIddictTokenCache<OpenIddictEntityFrameworkCoreToken> cache,
        ILogger<OpenIddictTokenManager<OpenIddictEntityFrameworkCoreToken>> logger,
        IOptionsMonitor<OpenIddictCoreOptions> options,
        IOpenIddictTokenStore<OpenIddictEntityFrameworkCoreToken> store,
        RevocationFault fault) : OpenIddictTokenManager<OpenIddictEntityFrameworkCoreToken>(cache, logger, options, store)
    {
        public override ValueTask<bool> TryRevokeAsync(OpenIddictEntityFrameworkCoreToken token, CancellationToken cancellationToken = default) =>
            fault.Armed && (fault.DeclinedEntryId ??= token.Id) == token.Id
                ? ValueTask.FromResult(false)
                : base.TryRevokeAsync(token, cancellationToken);
    }

    /// <summary>The real token service, except that subject revocation throws while the fault is armed.</summary>
    private sealed class RevocationFaultTokenService(ITokenService inner, RevocationFault fault) : ITokenService
    {
        public ValueTask<TokenIssueResult> IssueAsync(TokenIssueRequest request, CancellationToken cancellationToken = default) => inner.IssueAsync(request, cancellationToken);
        public ValueTask<TokenRefreshResult> RefreshAsync(TokenRefreshRequest request, CancellationToken cancellationToken = default) => inner.RefreshAsync(request, cancellationToken);
        public ValueTask<TokenValidationResult> ValidateAsync(TokenValidationRequest request, CancellationToken cancellationToken = default) => inner.ValidateAsync(request, cancellationToken);
        public ValueTask RevokeAsync(TokenRevocationRequest request, CancellationToken cancellationToken = default) => inner.RevokeAsync(request, cancellationToken);

        public ValueTask RevokeSubjectAsync(TokenSubject subject, CancellationToken cancellationToken = default) =>
            fault.Armed ? throw new InvalidOperationException("The token store is unavailable.") : inner.RevokeSubjectAsync(subject, cancellationToken);
    }
}

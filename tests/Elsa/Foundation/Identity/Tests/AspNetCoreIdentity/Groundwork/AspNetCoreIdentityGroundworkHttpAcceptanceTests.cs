using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Stores;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.Fixtures;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Foundation.Identity.Tests;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests;

[Collection(FastEndpointsHostCollection.Name)]
public sealed class AspNetCoreIdentityGroundworkHttpAcceptanceTests
{
    private const string LoginRoute = "/_elsa/identity/login";
    private const string TokenRoute = "/_elsa/identity/token";
    private const string RefreshRoute = "/_elsa/identity/refresh";
    private const string LogoutRoute = "/_elsa/identity/logout/aspnetcore-identity";

    [Fact]
    public async Task Http_host_uses_Groundwork_for_framework_identity_and_EF_only_for_OpenIddict()
    {
        await using var fixture = await AspNetCoreIdentityGroundworkHttpFixture.StartAsync();
        await using var scope = fixture.Services.CreateAsyncScope();

        Assert.IsType<GroundworkIdentityUserStore>(
            scope.ServiceProvider.GetRequiredService<IUserStore<AspNetCoreIdentityUser>>());
        Assert.IsType<GroundworkIdentityRoleStore>(
            scope.ServiceProvider.GetRequiredService<IRoleStore<IdentityRole>>());
        Assert.DoesNotContain(
            fixture.ServiceDescriptors,
            descriptor => IsApplicationIdentityDbContext(descriptor.ServiceType) ||
                          IsApplicationIdentityDbContext(descriptor.ImplementationType));
    }

    [Fact]
    public async Task Username_and_unique_email_login_issue_cookie_with_authority_claims_and_permission_access()
    {
        await using var fixture = await AspNetCoreIdentityGroundworkHttpFixture.StartAsync();
        var byName = await FormLoginAsync(
            fixture.CreateClient(),
            AspNetCoreIdentityGroundworkHttpFixture.UserName,
            AspNetCoreIdentityGroundworkHttpFixture.Password,
            AspNetCoreIdentityGroundworkHttpFixture.PrimaryTenant);
        var byEmail = await FormLoginAsync(
            fixture.CreateClient(),
            AspNetCoreIdentityGroundworkHttpFixture.Email,
            AspNetCoreIdentityGroundworkHttpFixture.Password,
            AspNetCoreIdentityGroundworkHttpFixture.PrimaryTenant);

        Assert.Equal(HttpStatusCode.OK, byName.StatusCode);
        Assert.Equal(HttpStatusCode.OK, byEmail.StatusCode);
        var identityCookie = ExtractIdentityCookie(byName);
        Assert.False(string.IsNullOrWhiteSpace(ExtractIdentityCookie(byEmail)));

        var claimsResponse = await SendWithCookieAsync(
            fixture.CreateClient(),
            HttpMethod.Get,
            AspNetCoreIdentityGroundworkHttpFixture.ClaimsRoute,
            identityCookie);
        Assert.Equal(HttpStatusCode.OK, claimsResponse.StatusCode);
        using var claims = JsonDocument.Parse(await claimsResponse.Content.ReadAsStringAsync());
        Assert.Equal(AspNetCoreIdentityGroundworkHttpFixture.PrimaryUserId, claims.RootElement.GetProperty("subject").GetString());
        Assert.Equal(AspNetCoreIdentityGroundworkHttpFixture.PrimaryTenant, claims.RootElement.GetProperty("tenantId").GetString());
        Assert.Contains(
            claims.RootElement.GetProperty("roles").EnumerateArray().Select(value => value.GetString()),
            value => value == AspNetCoreIdentityGroundworkHttpFixture.RoleId);
        Assert.Contains(
            claims.RootElement.GetProperty("permissions").EnumerateArray().Select(value => value.GetString()),
            value => value == AspNetCoreIdentityGroundworkHttpFixture.DirectPermission);
        Assert.Contains(
            claims.RootElement.GetProperty("permissions").EnumerateArray().Select(value => value.GetString()),
            value => value == AspNetCoreIdentityGroundworkHttpFixture.RolePermission);

        var permissionResponse = await SendWithCookieAsync(
            fixture.CreateClient(),
            HttpMethod.Get,
            AspNetCoreIdentityGroundworkHttpFixture.PermissionRoute,
            identityCookie);
        Assert.Equal(HttpStatusCode.OK, permissionResponse.StatusCode);

        await using var scope = fixture.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AspNetCoreIdentityUser>>();
        var duplicateEmail = new AspNetCoreIdentityUser
        {
            Id = "duplicate-email-user",
            TenantId = AspNetCoreIdentityGroundworkHttpFixture.PrimaryTenant,
            UserName = "different-user",
            Email = AspNetCoreIdentityGroundworkHttpFixture.Email,
            SecurityStamp = "duplicate-security",
            ConcurrencyStamp = "duplicate-revision"
        };
        var duplicateResult = await users.CreateAsync(duplicateEmail, AspNetCoreIdentityGroundworkHttpFixture.Password);
        Assert.False(duplicateResult.Succeeded);
        Assert.Contains(duplicateResult.Errors, error => error.Code == nameof(IdentityErrorDescriber.DuplicateEmail));
    }

    [Fact]
    public async Task Json_login_is_accepted_as_the_documented_non_browser_flow()
    {
        await using var fixture = await AspNetCoreIdentityGroundworkHttpFixture.StartAsync();

        var response = await fixture.CreateClient().PostAsJsonAsync(LoginRoute, new
        {
            username = AspNetCoreIdentityGroundworkHttpFixture.UserName,
            password = AspNetCoreIdentityGroundworkHttpFixture.Password,
            tenantId = AspNetCoreIdentityGroundworkHttpFixture.PrimaryTenant
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(ExtractIdentityCookie(response)));
    }

    [Fact]
    public async Task Bad_unknown_and_wrong_tenant_credentials_fail_indistinguishably_and_lockout_is_enforced()
    {
        await using var fixture = await AspNetCoreIdentityGroundworkHttpFixture.StartAsync();
        var failures = new[]
        {
            await FormLoginAsync(
                fixture.CreateClient(),
                AspNetCoreIdentityGroundworkHttpFixture.UserName,
                "wrong-password",
                AspNetCoreIdentityGroundworkHttpFixture.PrimaryTenant),
            await FormLoginAsync(
                fixture.CreateClient(),
                "unknown-user",
                "wrong-password",
                AspNetCoreIdentityGroundworkHttpFixture.PrimaryTenant),
            await FormLoginAsync(
                fixture.CreateClient(),
                AspNetCoreIdentityGroundworkHttpFixture.UserName,
                AspNetCoreIdentityGroundworkHttpFixture.Password,
                AspNetCoreIdentityGroundworkHttpFixture.SecondaryTenant)
        };

        Assert.All(failures, response =>
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.DoesNotContain(
                response.Headers.TryGetValues("Set-Cookie", out var values) ? values : [],
                value => value.StartsWith(AspNetCoreIdentityDefaults.CookieScheme, StringComparison.Ordinal));
        });

        for (var attempt = 1; attempt < 5; attempt++)
        {
            var failure = await FormLoginAsync(
                fixture.CreateClient(),
                AspNetCoreIdentityGroundworkHttpFixture.UserName,
                "wrong-password",
                AspNetCoreIdentityGroundworkHttpFixture.PrimaryTenant);
            Assert.Equal(HttpStatusCode.Unauthorized, failure.StatusCode);
        }

        var lockedOut = await FormLoginAsync(
            fixture.CreateClient(),
            AspNetCoreIdentityGroundworkHttpFixture.UserName,
            AspNetCoreIdentityGroundworkHttpFixture.Password,
            AspNetCoreIdentityGroundworkHttpFixture.PrimaryTenant);
        Assert.Equal(HttpStatusCode.Unauthorized, lockedOut.StatusCode);
    }

    [Fact]
    public async Task Ambiguous_email_login_fails_with_the_same_generic_unauthorized_response()
    {
        await using var fixture = await AspNetCoreIdentityGroundworkHttpFixture.StartAsync();
        await fixture.InjectAmbiguousEmailUserAsync();

        var response = await FormLoginAsync(
            fixture.CreateClient(),
            AspNetCoreIdentityGroundworkHttpFixture.Email,
            AspNetCoreIdentityGroundworkHttpFixture.Password,
            AspNetCoreIdentityGroundworkHttpFixture.PrimaryTenant);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Cookie_exchanges_for_bearer_that_carries_claims_and_authorizes_identity_capabilities()
    {
        await using var fixture = await AspNetCoreIdentityGroundworkHttpFixture.StartAsync();
        var login = await FormLoginAsync(
            fixture.CreateClient(),
            AspNetCoreIdentityGroundworkHttpFixture.UserName,
            AspNetCoreIdentityGroundworkHttpFixture.Password,
            AspNetCoreIdentityGroundworkHttpFixture.PrimaryTenant);
        var cookie = ExtractIdentityCookie(login);

        var tokenResponse = await SendWithCookieAsync(fixture.CreateClient(), HttpMethod.Get, TokenRoute, cookie);
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
        using var tokenJson = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
        var accessToken = tokenJson.RootElement.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(accessToken));

        using var bearerRequest = new HttpRequestMessage(HttpMethod.Get, AspNetCoreIdentityGroundworkHttpFixture.BearerRoute);
        bearerRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var bearerResponse = await fixture.CreateClient().SendAsync(bearerRequest);
        Assert.Equal(HttpStatusCode.OK, bearerResponse.StatusCode);
        Assert.Equal(AspNetCoreIdentityGroundworkHttpFixture.PrimaryTenant, await bearerResponse.Content.ReadAsStringAsync());

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var validation = await scope.ServiceProvider.GetRequiredService<ITokenService>()
                .ValidateAsync(new TokenValidationRequest(accessToken!));
            Assert.True(validation.Succeeded, validation.Failure);
            Assert.Contains(
                AspNetCoreIdentityGroundworkHttpFixture.RolePermission,
                validation.Principal!.FindAll(IdentityClaimTypes.Permission).Select(claim => claim.Value));
        }

        using var permissionRequest = new HttpRequestMessage(HttpMethod.Get, AspNetCoreIdentityGroundworkHttpFixture.PermissionRoute);
        permissionRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        Assert.Equal(HttpStatusCode.OK, (await fixture.CreateClient().SendAsync(permissionRequest)).StatusCode);

        using var capabilitiesRequest = new HttpRequestMessage(HttpMethod.Get, "/_elsa/identity/capabilities");
        capabilitiesRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var capabilitiesResponse = await fixture.CreateClient().SendAsync(capabilitiesRequest);
        Assert.Equal(HttpStatusCode.OK, capabilitiesResponse.StatusCode);
        var capabilities = await capabilitiesResponse.Content.ReadAsStringAsync();
        Assert.Contains(AspNetCoreIdentityGroundworkHttpFixture.RolePermission, capabilities, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refresh_rotates_tokens_and_rejects_replay_through_the_HTTP_endpoint()
    {
        await using var fixture = await AspNetCoreIdentityGroundworkHttpFixture.StartAsync();
        string refreshToken;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var issued = await scope.ServiceProvider.GetRequiredService<ITokenService>().IssueAsync(new TokenIssueRequest(
                AspNetCoreIdentityGroundworkHttpFixture.PrimaryUserId,
                AspNetCoreIdentityGroundworkHttpFixture.PrimaryTenant,
                [AspNetCoreIdentityGroundworkHttpFixture.DirectPermission, AspNetCoreIdentityGroundworkHttpFixture.RolePermission]));
            Assert.False(string.IsNullOrWhiteSpace(issued.RefreshToken));
            refreshToken = issued.RefreshToken!;
        }

        var rotated = await fixture.CreateClient().PostAsJsonAsync(RefreshRoute, new { refreshToken });
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        using var rotatedJson = JsonDocument.Parse(await rotated.Content.ReadAsStringAsync());
        var rotatedAccessToken = rotatedJson.RootElement.GetProperty("accessToken").GetString();
        var rotatedRefreshToken = rotatedJson.RootElement.GetProperty("refreshToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(rotatedAccessToken));
        Assert.False(string.IsNullOrWhiteSpace(rotatedRefreshToken));
        Assert.NotEqual(refreshToken, rotatedRefreshToken);

        using var bearerRequest = new HttpRequestMessage(HttpMethod.Get, AspNetCoreIdentityGroundworkHttpFixture.BearerRoute);
        bearerRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rotatedAccessToken);
        Assert.Equal(HttpStatusCode.OK, (await fixture.CreateClient().SendAsync(bearerRequest)).StatusCode);

        var replay = await fixture.CreateClient().PostAsJsonAsync(RefreshRoute, new { refreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
    }

    [Fact]
    public async Task Logout_invalidates_the_issued_session_cookie_instead_of_only_clearing_the_client_copy()
    {
        await using var fixture = await AspNetCoreIdentityGroundworkHttpFixture.StartAsync();
        var login = await FormLoginAsync(
            fixture.CreateClient(),
            AspNetCoreIdentityGroundworkHttpFixture.UserName,
            AspNetCoreIdentityGroundworkHttpFixture.Password,
            AspNetCoreIdentityGroundworkHttpFixture.PrimaryTenant);
        var originalCookie = ExtractIdentityCookie(login);
        Assert.Equal(
            HttpStatusCode.OK,
            (await SendWithCookieAsync(fixture.CreateClient(), HttpMethod.Get, TokenRoute, originalCookie)).StatusCode);

        var logout = await SendJsonWithCookieAsync(fixture.CreateClient(), LogoutRoute, originalCookie, new { });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(ExtractIdentityCookie(logout)));

        var replay = await SendWithCookieAsync(fixture.CreateClient(), HttpMethod.Get, TokenRoute, originalCookie);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
    }

    [Fact]
    public async Task Normal_session_cookie_remains_valid_across_immediate_stamp_checks()
    {
        await using var fixture = await AspNetCoreIdentityGroundworkHttpFixture.StartAsync();
        var login = await FormLoginAsync(
            fixture.CreateClient(),
            AspNetCoreIdentityGroundworkHttpFixture.UserName,
            AspNetCoreIdentityGroundworkHttpFixture.Password,
            AspNetCoreIdentityGroundworkHttpFixture.PrimaryTenant);
        var cookie = ExtractIdentityCookie(login);

        for (var request = 0; request < 2; request++)
        {
            var response = await SendWithCookieAsync(fixture.CreateClient(), HttpMethod.Get, TokenRoute, cookie);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task Cookie_validation_binds_the_tenant_claim_before_loading_the_user()
    {
        await using var fixture = await AspNetCoreIdentityGroundworkHttpFixture.StartAsync();
        await fixture.SeedSecondaryTenantUserAsync();
        var login = await FormLoginAsync(
            fixture.CreateClient(),
            AspNetCoreIdentityGroundworkHttpFixture.SecondaryUserName,
            AspNetCoreIdentityGroundworkHttpFixture.SecondaryPassword,
            AspNetCoreIdentityGroundworkHttpFixture.SecondaryTenant);
        var cookie = ExtractIdentityCookie(login);

        var response = await SendWithCookieAsync(
            fixture.CreateClient(),
            HttpMethod.Get,
            AspNetCoreIdentityGroundworkHttpFixture.ClaimsRoute,
            cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var claims = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(AspNetCoreIdentityGroundworkHttpFixture.SecondaryUserId, claims.RootElement.GetProperty("subject").GetString());
        Assert.Equal(AspNetCoreIdentityGroundworkHttpFixture.SecondaryTenant, claims.RootElement.GetProperty("tenantId").GetString());
    }

    [Fact]
    public async Task Anonymous_and_non_session_providers_remain_idempotent_logout_no_ops()
    {
        await using var fixture = await AspNetCoreIdentityGroundworkHttpFixture.StartAsync();

        foreach (var provider in new[] { AspNetCoreIdentityDefaults.ProviderId, "openiddict", "unknown-provider" })
        {
            var response = await fixture.CreateClient().PostAsJsonAsync($"/_elsa/identity/logout/{provider}", new { });
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }
    }

    [Fact]
    public async Task Invalidation_failure_is_truthful_and_does_not_clear_a_still_valid_cookie()
    {
        await using var fixture = await AspNetCoreIdentityGroundworkHttpFixture.StartAsync(services =>
        {
            services.RemoveAll<IAuthenticationSessionInvalidator>();
            services.AddScoped<IAuthenticationSessionInvalidator, ThrowingSessionInvalidator>();
        });
        var login = await FormLoginAsync(
            fixture.CreateClient(),
            AspNetCoreIdentityGroundworkHttpFixture.UserName,
            AspNetCoreIdentityGroundworkHttpFixture.Password,
            AspNetCoreIdentityGroundworkHttpFixture.PrimaryTenant);
        var cookie = ExtractIdentityCookie(login);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await SendJsonWithCookieAsync(fixture.CreateClient(), LogoutRoute, cookie, new { }));
        Assert.Contains("invalidation failed", exception.Message, StringComparison.Ordinal);

        var replay = await SendWithCookieAsync(fixture.CreateClient(), HttpMethod.Get, TokenRoute, cookie);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
    }

    private static async Task<HttpResponseMessage> FormLoginAsync(
        HttpClient client,
        string identifier,
        string password,
        string tenantId)
    {
        var pageResponse = await client.GetAsync(LoginRoute);
        pageResponse.EnsureSuccessStatusCode();
        var html = await pageResponse.Content.ReadAsStringAsync();
        var match = Regex.Match(
            html,
            $"name=\"{Regex.Escape(AspNetCoreIdentityDefaults.AntiforgeryFieldName)}\"\\s+value=\"([^\"]+)\"");
        Assert.True(match.Success, "The login page must embed an antiforgery token.");
        var antiforgeryCookie = ExtractSetCookie(pageResponse, ".AspNetCore.Antiforgery.");
        using var request = new HttpRequestMessage(HttpMethod.Post, LoginRoute)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["username"] = identifier,
                ["password"] = password,
                ["tenantId"] = tenantId,
                [AspNetCoreIdentityDefaults.AntiforgeryFieldName] = WebUtility.HtmlDecode(match.Groups[1].Value)
            })
        };
        request.Headers.Add("Cookie", antiforgeryCookie);
        return await client.SendAsync(request);
    }

    private static string ExtractIdentityCookie(HttpResponseMessage response)
    {
        return ExtractSetCookie(response, AspNetCoreIdentityDefaults.CookieScheme);
    }

    private static string ExtractSetCookie(HttpResponseMessage response, string prefix)
    {
        var cookie = response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(value => value.StartsWith(prefix, StringComparison.Ordinal))
            : null;
        Assert.False(string.IsNullOrWhiteSpace(cookie));
        return cookie!.Split(';', 2)[0];
    }

    private static async Task<HttpResponseMessage> SendWithCookieAsync(
        HttpClient client,
        HttpMethod method,
        string route,
        string cookie)
    {
        using var request = new HttpRequestMessage(method, route);
        request.Headers.Add("Cookie", cookie);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendJsonWithCookieAsync(
        HttpClient client,
        string route,
        string cookie,
        object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, route)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("Cookie", cookie);
        return await client.SendAsync(request);
    }

    private static bool IsApplicationIdentityDbContext(Type? type) =>
        type?.FullName == "Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.ApplicationIdentityDbContext";

    private sealed class ThrowingSessionInvalidator : IAuthenticationSessionInvalidator
    {
        public string ProviderId => AspNetCoreIdentityDefaults.ProviderId;

        public ValueTask InvalidateAsync(
            AuthenticationSessionInvalidationContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException(new InvalidOperationException("Test session invalidation failed."));
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Api;
using Elsa.Foundation.Identity.AspNetCoreIdentity;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.Fixtures;
using Elsa.Foundation.Identity.Tests.Api;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Elsa.Foundation.Identity.Tests.AspNetCoreIdentity.Differential;

/// <summary>
/// App-level parity between an EF-composed identity host and a Groundwork-composed one.
/// </summary>
/// <remarks>
/// <para>
/// The store differential compares persistence <em>semantics</em>. This compares what an application
/// actually does: boot a real HTTP host on each stack, drive the shared identity surface
/// (<c>POST /_elsa/identity/login</c> → <c>GET /_elsa/identity/token</c>), and compare the observable
/// outcome. It closes the composition/integration gap the store probes cannot reach — DI wiring, cookie
/// and antiforgery handling, token issuance, and claim projection.
/// </para>
/// <para>
/// Apples to apples means comparing <em>shape</em>, not values: each host seeds its own user, tenant and
/// role, so the assertions are on status codes, payload shape, and which claim kinds survive — never on a
/// literal username or tenant id.
/// </para>
/// <para>
/// Scope: identity is the only lane where two composable stacks exist. The workflow runtime, design,
/// publishing, secrets and diagnostics lanes are Groundwork-only in every shipping shell, so there is no
/// second composition of them to compare.
/// </para>
/// </remarks>
public sealed class IdentityAppParityTests(ITestOutputHelper output)
{
    [Fact]
    public async Task An_identity_app_behaves_the_same_on_either_persistence_stack()
    {
        await using var ef = await IdentityAppComparand.EfCoreAsync();
        await using var groundwork = await IdentityAppComparand.GroundworkAsync();

        var efObserved = await DriveAsync(ef);
        var groundworkObserved = await DriveAsync(groundwork);

        output.WriteLine($"efcore      : {Canonical(efObserved)}");
        output.WriteLine($"groundwork  : {Canonical(groundworkObserved)}");

        var divergent = efObserved.Keys.Union(groundworkObserved.Keys, StringComparer.Ordinal)
            .Where(key => !string.Equals(Get(efObserved, key), Get(groundworkObserved, key), StringComparison.Ordinal))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            divergent.Length == 0,
            $"""
             An identity app behaves differently depending on its persistence stack: {string.Join(", ", divergent)}.
             This is an application-visible difference, not a storage detail, so it needs a disposition
             recorded in specs/094-harden-groundwork-stores/divergence-ledger.md before EF is removed.
               efcore     : {Canonical(efObserved)}
               groundwork : {Canonical(groundworkObserved)}
             """);

        static string Get(IReadOnlyDictionary<string, string> facts, string key) =>
            facts.TryGetValue(key, out var value) ? value : "<absent>";
    }

    /// <summary>
    /// One identical journey through the shared identity HTTP surface, reduced to normalized facts.
    /// </summary>
    private static async Task<IReadOnlyDictionary<string, string>> DriveAsync(IdentityAppComparand app)
    {
        const string tokenRoute = "/_elsa/identity/token";
        var client = app.CreateClient();

        var anonymous = await client.GetAsync(tokenRoute);

        var wrongPassword = await FormLoginAsync(client, app.UserName, "definitely-not-the-password", app.TenantId);

        var login = await FormLoginAsync(client, app.UserName, app.Password, app.TenantId);
        var identityCookie = TryExtractSetCookie(login, AspNetCoreIdentityDefaults.CookieScheme);
        if (identityCookie is not null)
        {
            client.DefaultRequestHeaders.Remove("Cookie");
            client.DefaultRequestHeaders.Add("Cookie", identityCookie);
        }

        var authenticated = await client.GetAsync(tokenRoute);
        var facts = new Dictionary<string, string>
        {
            ["token-before-login"] = Status(anonymous),
            ["login-with-wrong-password"] = Status(wrongPassword),
            ["login-with-correct-password"] = Status(login),
            ["identity-cookie-issued"] = identityCookie is null ? "false" : "true",
            ["token-after-login"] = Status(authenticated)
        };

        if (authenticated.StatusCode is not HttpStatusCode.OK)
            return facts;

        using var payload = JsonDocument.Parse(await authenticated.Content.ReadAsStringAsync());
        var hasAccessToken = payload.RootElement.TryGetProperty("accessToken", out var accessTokenElement);
        facts["token-payload-has-accessToken"] = hasAccessToken ? "true" : "false";
        facts["token-payload-has-expiresAt"] = payload.RootElement.TryGetProperty("expiresAt", out _) ? "true" : "false";

        var accessToken = hasAccessToken ? accessTokenElement.GetString() : null;
        facts["access-token-non-empty"] = string.IsNullOrWhiteSpace(accessToken) ? "false" : "true";

        if (string.IsNullOrWhiteSpace(accessToken))
            return facts;

        await using var scope = app.Services.CreateAsyncScope();
        var validation = await scope.ServiceProvider.GetRequiredService<ITokenService>()
            .ValidateAsync(new TokenValidationRequest(accessToken));

        facts["access-token-validates"] = validation.Succeeded ? "true" : "false";

        var principal = validation.Principal;
        facts["token-carries-tenant-claim"] =
            string.IsNullOrWhiteSpace(principal?.FindFirst(IdentityClaimTypes.TenantId)?.Value) ? "false" : "true";
        facts["token-carries-identity-name"] =
            string.IsNullOrWhiteSpace(principal?.Identity?.Name) ? "false" : "true";
        facts["token-carries-permission-claims"] =
            principal?.FindAll(IdentityClaimTypes.Permission).Any() == true ? "true" : "false";

        // A bearer issued by one stack must be rejected once the cookie is dropped only if it is invalid;
        // here it must still authenticate, which is the property an app depends on.
        client.DefaultRequestHeaders.Remove("Cookie");
        using var bearerRequest = new HttpRequestMessage(HttpMethod.Get, tokenRoute);
        bearerRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        facts["bearer-authenticates-without-cookie"] = Status(await client.SendAsync(bearerRequest));

        return facts;
    }

    private static string Status(HttpResponseMessage response) => ((int)response.StatusCode).ToString();

    private static string Canonical(IReadOnlyDictionary<string, string> facts) =>
        string.Join("; ", facts.OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => $"{f.Key}={f.Value}"));

    /// <summary>
    /// Drives the real CSRF-protected HTML form login the way a browser does. Parameterized rather than
    /// reusing <c>LoginTestHelper</c>, which is pinned to the EF host's seeded admin.
    /// </summary>
    private static async Task<HttpResponseMessage> FormLoginAsync(HttpClient client, string userName, string password, string? tenantId)
    {
        var page = await client.GetAsync("/_elsa/identity/login");
        page.EnsureSuccessStatusCode();

        var html = await page.Content.ReadAsStringAsync();
        var match = Regex.Match(html, $"name=\"{Regex.Escape(AspNetCoreIdentityDefaults.AntiforgeryFieldName)}\"\\s+value=\"([^\"]+)\"");
        Assert.True(match.Success, "The login page must embed an antiforgery token.");

        var form = new Dictionary<string, string>
        {
            ["username"] = userName,
            ["password"] = password,
            [AspNetCoreIdentityDefaults.AntiforgeryFieldName] = WebUtility.HtmlDecode(match.Groups[1].Value)
        };

        if (tenantId is not null)
            form["tenantId"] = tenantId;

        using var request = new HttpRequestMessage(HttpMethod.Post, "/_elsa/identity/login") { Content = new FormUrlEncodedContent(form) };
        var antiforgeryCookie = TryExtractSetCookie(page, ".AspNetCore.Antiforgery.");
        if (antiforgeryCookie is not null)
            request.Headers.Add("Cookie", antiforgeryCookie);

        return await client.SendAsync(request);
    }

    private static string? TryExtractSetCookie(HttpResponseMessage response, string prefix) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(x => x.StartsWith(prefix, StringComparison.Ordinal))?.Split(';', 2)[0]
            : null;
}

/// <summary>A running identity application composed over one persistence stack.</summary>
internal abstract class IdentityAppComparand : IAsyncDisposable
{
    public abstract string UserName { get; }
    public abstract string Password { get; }
    public abstract string? TenantId { get; }
    public abstract IServiceProvider Services { get; }
    public abstract HttpClient CreateClient();
    public abstract ValueTask DisposeAsync();

    public static async Task<IdentityAppComparand> EfCoreAsync() => new EfCoreApp(await TokenEndpointFixture.StartAsync());

    public static async Task<IdentityAppComparand> GroundworkAsync() => new GroundworkApp(await AspNetCoreIdentityGroundworkHttpFixture.StartAsync());

    private sealed class EfCoreApp(TokenEndpointFixture fixture) : IdentityAppComparand
    {
        public override string UserName => TestAdmin.UserName;
        public override string Password => TestAdmin.Password;

        // The EF host's login form carries no tenant field; its seeder binds the configured default tenant.
        public override string? TenantId => null;

        public override IServiceProvider Services => fixture.Services;
        public override HttpClient CreateClient() => fixture.Client;
        public override ValueTask DisposeAsync() => fixture.DisposeAsync();
    }

    private sealed class GroundworkApp(AspNetCoreIdentityGroundworkHttpFixture fixture) : IdentityAppComparand
    {
        public override string UserName => AspNetCoreIdentityGroundworkHttpFixture.UserName;
        public override string Password => AspNetCoreIdentityGroundworkHttpFixture.Password;
        public override string? TenantId => AspNetCoreIdentityGroundworkHttpFixture.PrimaryTenant;
        public override IServiceProvider Services => fixture.Services;
        public override HttpClient CreateClient() => fixture.CreateClient();
        public override ValueTask DisposeAsync() => fixture.DisposeAsync();
    }
}

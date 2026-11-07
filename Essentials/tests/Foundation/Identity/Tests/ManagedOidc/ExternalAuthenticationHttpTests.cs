using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Api;
using Elsa.Foundation.Identity.Api.Models;
using Elsa.Foundation.Identity.Api.Extensions;
using Elsa.Foundation.Identity.AspNetCoreIdentity;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.DependencyInjection;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Core.Ownership;
using Elsa.Foundation.Identity.ExternalAuthentication;
using Elsa.Foundation.Identity.ExternalAuthentication.Api;
using Elsa.Foundation.Identity.ExternalAuthentication.Core;
using Elsa.Foundation.Identity.ExternalAuthentication.Oidc;
using Elsa.Foundation.Identity.ExternalAuthentication.Persistence.EntityFrameworkCore;
using Elsa.Foundation.Identity.OpenIddict;
using Elsa.Foundation.Identity.OpenIddict.EntityFrameworkCore;
using Elsa.Foundation.Identity.OpenIddict.Extensions;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.Tests.ManagedOidc;

public sealed partial class ExternalAuthenticationHttpTests
{
    [Fact]
    public async Task Managed_protocol_endpoints_declare_their_owner_and_preview_management_permission()
    {
        await using var host = await HttpHost.Start();
        var owner = typeof(ExternalAuthenticationApiFeature).Assembly.GetName().Name!;
        var endpoints = host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<EndpointOwnershipMetadata>()?.OwnerId == owner &&
                endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName.StartsWith("ExternalAuthentication", StringComparison.Ordinal) == true).ToArray();
        Assert.Equal(7, endpoints.Length);
        var managed = endpoints.Where(endpoint => endpoint.Metadata.GetMetadata<EndpointSecurityDispositionMetadata>()?.Kind == EndpointSecurityDispositionKind.Permission).ToArray();
        Assert.Equal(2, managed.Length);
        foreach (var endpoint in managed)
        {
            var metadata = endpoint.Metadata.GetMetadata<EndpointSecurityDispositionMetadata>()!;
            var parsed = new PermissionPolicyCodec().Parse(metadata.Value!);
            Assert.Equal(PermissionPolicyParseStatus.Valid, parsed.Status);
            Assert.Equal([PermissionKey.Normalize(DefaultIdentityPermissionKeys.IdentityProvidersManage)], parsed.Descriptor!.Permissions);
        }
        Assert.Equal(5, endpoints.Count(endpoint => endpoint.Metadata.GetMetadata<EndpointSecurityDispositionMetadata>()?.Kind == EndpointSecurityDispositionKind.Public));
    }
    [Theory]
    [InlineData("external:company")]
    [InlineData("openiddict")]
    public async Task Two_real_provider_sign_ins_use_the_normal_cookie_token_refresh_and_scoped_logout_pipeline(string logoutProvider)
    {
        await using var host = await HttpHost.Start();
        using var company = host.Browser(); using var partner = host.Browser();
        var companyCallback = await host.StartAndAuthorize(company);
        using (var callback = await company.GetAsync(companyCallback)) Assert.Equal(HttpStatusCode.Found, callback.StatusCode);
        var companySession = await Session(company);
        Assert.Equal("authenticated", companySession.Status);
        Assert.Equal("external:company", companySession.Provider);
        Assert.Equal(["reader"], companySession.Roles);
        Assert.Equal([DefaultIdentityPermissionKeys.IdentityUsersRead], companySession.Permissions);
        Assert.NotEqual("opaque-Subject", companySession.Subject);
        var firstPair = await Pair(company);
        Assert.False(string.IsNullOrWhiteSpace(firstPair.RefreshToken));
        using (var resource = await Bearer(company, firstPair.AccessToken)) Assert.Equal(HttpStatusCode.OK, resource.StatusCode);
        using (var admin = await Bearer(company, firstPair.AccessToken, "admin-resource")) Assert.Equal(HttpStatusCode.Forbidden, admin.StatusCode);

        var partnerCallback = await host.StartAndAuthorize(partner, "partner");
        using (var callback = await partner.GetAsync(partnerCallback)) Assert.Equal(HttpStatusCode.Found, callback.StatusCode);
        var partnerSession = await Session(partner);
        Assert.NotEqual(companySession.Subject, partnerSession.Subject); // Same opaque subject, different validated issuer/provider.
        var secondPair = await Pair(partner);
        var copiedCookie = company.Cookies.GetCookies(host.BaseUri)[AspNetCoreIdentityDefaults.CookieScheme]!.Value;
        // A disabled connection disappears from discovery, but its issued cookie can still sign out.
        host.Options.Connections[0] = host.Options.Connections[0] with { Enabled = false };
        using var signOut = new HttpRequestMessage(HttpMethod.Post, "_elsa/identity/logout/" + logoutProvider);
        if (logoutProvider == "openiddict") signOut.Headers.Authorization = new AuthenticationHeaderValue("Bearer", firstPair.AccessToken);
        using (var logout = await company.SendAsync(signOut)) Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using (var dead = await Bearer(company, firstPair.AccessToken)) Assert.Equal(HttpStatusCode.Unauthorized, dead.StatusCode);
        using (var refresh = await company.PostAsJsonAsync("_elsa/identity/refresh", new { refreshToken = firstPair.RefreshToken })) Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        using (var alive = await Bearer(partner, secondPair.AccessToken)) Assert.Equal(HttpStatusCode.OK, alive.StatusCode);
        using (var refresh = await partner.PostAsJsonAsync("_elsa/identity/refresh", new { refreshToken = secondPair.RefreshToken }))
        {
            Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
            Assert.True(refresh.Headers.CacheControl!.NoStore);
        }
        using var copied = host.Browser();
        copied.Cookies.Add(host.BaseUri, new Cookie(AspNetCoreIdentityDefaults.CookieScheme, copiedCookie, "/"));
        Assert.Equal("anonymous", (await Session(copied)).Status);
        Assert.Equal("authenticated", (await Session(partner)).Status);
        Assert.Equal(1, host.Company.TokenRequests);
        Assert.Equal(1, host.Partner.TokenRequests);
    }

    [Fact]
    public async Task Themed_chooser_keeps_local_and_live_provider_choices_and_never_bypasses_a_preferred_method()
    {
        await using var host = await HttpHost.Start();
        using var browser = host.Browser();
        using (var response = await browser.GetAsync("_elsa/identity/login?returnUrl=%2Fbackend%2Fresource"))
        {
            var page = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("field-guide", page, StringComparison.Ordinal);
            Assert.Contains("name=\"username\"", page, StringComparison.Ordinal);
            Assert.Contains(host.BaseUri.AbsoluteUri.TrimEnd('/') + "/identity/external/signin/company", page, StringComparison.Ordinal);
            Assert.Contains("Continue with Company", page, StringComparison.Ordinal);
            Assert.Contains("Continue with Partner", page, StringComparison.Ordinal);
            Assert.Contains("data-preferred=\"true\"", page, StringComparison.Ordinal);
            Assert.DoesNotContain("http-equiv=\"refresh\"", page, StringComparison.OrdinalIgnoreCase);
        }
        using (var challenge = await browser.GetAsync("_elsa/identity/challenge/external:company"))
        {
            Assert.Equal(HttpStatusCode.Found, challenge.StatusCode);
            Assert.EndsWith("/identity/external/signin/company?returnUrl=%2F", challenge.Headers.Location!.AbsoluteUri, StringComparison.Ordinal);
            using var chooser = await browser.GetAsync(challenge.Headers.Location);
            Assert.Contains("/_elsa/identity/login", chooser.Headers.Location!.AbsolutePath, StringComparison.Ordinal);
        }
        Assert.Equal(0, host.Company.TokenRequests);
        host.Options.Connections[0] = host.Options.Connections[0] with { DisplayName = "Company <script>bad()</script>" };
        var encoded = await browser.GetStringAsync("_elsa/identity/login");
        Assert.Contains("Company &lt;script&gt;bad()&lt;/script&gt;", encoded, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>bad()", encoded, StringComparison.Ordinal);
        host.Options.Connections[1] = host.Options.Connections[1] with { Enabled = false };
        using var bootstrap = await browser.GetAsync("_elsa/identity/bootstrap");
        var data = await bootstrap.Content.ReadAsStringAsync();
        Assert.Contains("external:company", data, StringComparison.Ordinal);
        Assert.DoesNotContain("external:partner", data, StringComparison.Ordinal);
        host.Services.GetRequiredService<IOptions<AspNetCoreIdentityOptions>>().Value.Enabled = false;
        Assert.DoesNotContain("name=\"username\"", await browser.GetStringAsync("_elsa/identity/login"), StringComparison.Ordinal);
        using var local = await browser.PostAsJsonAsync("_elsa/identity/login", new { username = TestAdmin.UserName, password = TestAdmin.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, local.StatusCode);
        var externalCallback = await host.StartAndAuthorize(browser);
        using var external = await browser.GetAsync(externalCallback);
        Assert.Equal(HttpStatusCode.Found, external.StatusCode);
        Assert.Equal("external:company", (await Session(browser)).Provider);
    }

    [Fact]
    public async Task Browser_binding_antiforgery_purpose_and_replay_are_enforced_before_sign_in()
    {
        await using var host = await HttpHost.Start();
        using var browser = host.Browser(); using var other = host.Browser();
        using (var missingCsrf = await browser.PostAsync("identity/external/signin/company", new FormUrlEncodedContent(new Dictionary<string, string>())))
            Assert.Equal(HttpStatusCode.BadRequest, missingCsrf.StatusCode);
        var callback = await host.StartAndAuthorize(browser);
        using (var wrongBrowser = await other.GetAsync(callback)) Assert.Equal(HttpStatusCode.BadRequest, wrongBrowser.StatusCode);
        var previewCallback = new Uri(callback.AbsoluteUri.Replace("/signin/", "/preview/", StringComparison.Ordinal));
        using (var wrongPurpose = await browser.GetAsync(previewCallback)) Assert.Equal(HttpStatusCode.BadRequest, wrongPurpose.StatusCode);
        Assert.Equal(0, host.Company.TokenRequests);
        using (var completed = await browser.GetAsync(callback))
        {
            Assert.Equal(HttpStatusCode.Found, completed.StatusCode);
            Assert.True(completed.Headers.CacheControl!.NoStore);
            Assert.Equal("no-referrer", Assert.Single(completed.Headers.GetValues("Referrer-Policy")));
        }
        using (var replay = await browser.GetAsync(callback)) Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        Assert.Equal(1, host.Company.TokenRequests);
        Assert.Equal("authenticated", (await Session(browser)).Status);
    }

    [Fact]
    public async Task Concurrent_tabs_have_independent_correlations_and_duplicate_callback_parameters_do_not_consume_state()
    {
        await using var host = await HttpHost.Start();
        using var browser = host.Browser();
        var first = await host.StartAndAuthorize(browser);
        var second = await host.StartAndAuthorize(browser);
        using (var ambiguous = await browser.GetAsync(first.AbsoluteUri + "&state=another")) Assert.Equal(HttpStatusCode.BadRequest, ambiguous.StatusCode);
        using (var firstCompleted = await browser.GetAsync(first)) Assert.Equal(HttpStatusCode.Found, firstCompleted.StatusCode);
        var owner = (await Session(browser)).Subject;
        using (var secondCompleted = await browser.GetAsync(second)) Assert.Equal(HttpStatusCode.Found, secondCompleted.StatusCode);
        Assert.Equal(owner, (await Session(browser)).Subject);
        Assert.Equal(2, host.Company.TokenRequests);
        Assert.Equal(2, (await host.Counts()).Users); // Seed administrator plus one admitted user.
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_preview_preserves_admin_cookie_tokens_and_IAM_rows_for_disabled_draft_or_enabled_linked_user(bool existingIdentity)
    {
        await using var host = await HttpHost.Start();
        if (existingIdentity)
        {
            using var previous = host.Browser();
            var admitted = await host.StartAndAuthorize(previous);
            using var completed = await previous.GetAsync(admitted);
            Assert.Equal(HttpStatusCode.Found, completed.StatusCode);
        }
        using var browser = host.Browser();
        await host.LoginAdministrator(browser);
        var original = await Session(browser);
        var originalCookie = browser.Cookies.GetCookies(host.BaseUri)[AspNetCoreIdentityDefaults.CookieScheme]!.Value;
        var existingToken = await Pair(browser);
        if (!existingIdentity)
        {
            await using var scope = host.Scope();
            var catalog = scope.ServiceProvider.GetRequiredService<IConnectionCatalogStore>();
            Assert.True((await catalog.SaveAsync(new([host.Options.Connections[0] with { Id = "draft", Enabled = false }]))).Saved);
        }
        var before = await host.Counts();
        var callback = await host.StartAndAuthorize(browser, purpose: "preview", record: existingIdentity ? "deployment:company" : "draft");
        using var returned = await browser.GetAsync(callback);
        Assert.Equal(HttpStatusCode.Found, returned.StatusCode);
        Assert.DoesNotContain(returned.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [], cookie => cookie.StartsWith(AspNetCoreIdentityDefaults.CookieScheme + "=", StringComparison.Ordinal));
        var location = new Uri(host.BaseUri, returned.Headers.Location!);
        var handle = QueryHelpers.ParseQuery(location.Query)["externalPreviewHandle"].ToString();
        Assert.False(string.IsNullOrWhiteSpace(handle));
        using (var result = await browser.GetAsync("identity/external/preview/company/results/" + handle))
        {
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            var preview = await result.Content.ReadFromJsonAsync<ExternalAuthenticationPreview>();
            Assert.Equal(existingIdentity ? ExternalAdmissionKind.ExistingUser : ExternalAdmissionKind.NewUser, preview!.Admission);
            Assert.Equal(["reader"], preview.RoleIds);
            Assert.Equal([DefaultIdentityPermissionKeys.IdentityUsersRead], preview.Permissions);
            Assert.False(preview.Stale);
        }
        using (var replay = await browser.GetAsync("identity/external/preview/company/results/" + handle)) Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        Assert.Equal(original.Subject, (await Session(browser)).Subject);
        Assert.Equal(originalCookie, browser.Cookies.GetCookies(host.BaseUri)[AspNetCoreIdentityDefaults.CookieScheme]!.Value);
        Assert.Equal(before, await host.Counts());
        using var stillValid = await Bearer(browser, existingToken.AccessToken, "admin-resource");
        Assert.Equal(HttpStatusCode.OK, stillValid.StatusCode);
    }

    [Theory]
    [InlineData("provider", "ProviderFailed")]
    [InlineData("stale", "StaleConnection")]
    [InlineData("credential", "ProviderFailed")]
    [InlineData("unbound", null)]
    [InlineData("callback", null)]
    [InlineData("return-policy", null)]
    public async Task Preview_failures_return_only_to_the_protected_currently_allowed_destination(string scenario, string? failure)
    {
        await using var host = await HttpHost.Start();
        using var browser = host.Browser();
        await host.LoginAdministrator(browser);
        var original = await Session(browser);
        var originalCookie = browser.Cookies.GetCookies(host.BaseUri)[AspNetCoreIdentityDefaults.CookieScheme]!.Value;
        host.Options.AllowedReturnUrlOrigins = ["https://studio.example"];
        var before = await host.Counts();
        var callback = await host.StartAndAuthorize(browser, purpose: "preview", returnUrl: "https://studio.example/security/external-sign-in?backend=selected&externalPreviewNonce=local-binding");
        if (scenario == "provider")
        {
            var query = QueryHelpers.ParseQuery(callback.Query);
            callback = new Uri(QueryHelpers.AddQueryString(callback.GetLeftPart(UriPartial.Path), new Dictionary<string, string?>
                { ["state"] = query["state"].ToString(), ["error"] = "access_denied", ["error_description"] = "private-provider-detail" }));
        }
        if (scenario == "stale") host.Options.Connections[0] = host.Options.Connections[0] with { Preferred = !host.Options.Connections[0].Preferred,
            Admission = new() { Type = "reject" } };
        if (scenario == "credential") host.Secrets.Version++;
        if (scenario == "unbound") callback = new Uri(QueryHelpers.AddQueryString(callback.GetLeftPart(UriPartial.Path),
            new Dictionary<string, string?> { ["state"] = new string('a', 43), ["error"] = "access_denied" }));
        if (scenario == "callback") host.Options.PublicBaseUrl = "https://elsewhere.example/backend";
        if (scenario == "return-policy") host.Options.AllowedReturnUrlOrigins = [];
        using var returned = await browser.GetAsync(callback);
        if (failure is null)
        {
            Assert.Equal(HttpStatusCode.BadRequest, returned.StatusCode);
            Assert.Null(returned.Headers.Location);
        }
        else
        {
            Assert.Equal(HttpStatusCode.Found, returned.StatusCode);
            var destination = returned.Headers.Location!;
            Assert.Equal("https://studio.example/security/external-sign-in", destination.GetLeftPart(UriPartial.Path));
            var query = QueryHelpers.ParseQuery(destination.Query);
            Assert.Equal(failure, query["externalPreviewFailure"]);
            Assert.Equal("company", query["externalPreviewConnection"]);
            Assert.Equal("selected", query["backend"]);
            Assert.Equal("local-binding", query["externalPreviewNonce"]);
            Assert.False(query.ContainsKey("externalPreviewHandle"));
            Assert.DoesNotContain("private-provider-detail", destination.AbsoluteUri, StringComparison.Ordinal);
            using var replay = await browser.GetAsync(callback);
            Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
            Assert.Null(replay.Headers.Location);
        }
        Assert.Equal(before, await host.Counts());
        Assert.Equal(original.Subject, (await Session(browser)).Subject);
        Assert.Equal(originalCookie, browser.Cookies.GetCookies(host.BaseUri)[AspNetCoreIdentityDefaults.CookieScheme]!.Value);
    }

    [Theory]
    [InlineData("reject")]
    [InlineData("disabled")]
    [InlineData("secret")]
    [InlineData("bad-return")]
    public async Task Refused_or_invalidated_admission_never_sets_a_normal_cookie(string scenario)
    {
        await using var host = await HttpHost.Start();
        using var browser = host.Browser();
        if (scenario == "reject") host.Options.Connections[0] = host.Options.Connections[0] with { Admission = new() };
        if (scenario == "bad-return")
        {
            using var denied = await host.Begin(browser, "company", returnUrl: "https://attacker.example");
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        }
        else
        {
            var callback = await host.StartAndAuthorize(browser);
            if (scenario == "disabled") host.Options.Connections[0] = host.Options.Connections[0] with { Enabled = false };
            if (scenario == "secret") host.Secrets.Version++;
            using var denied = await browser.GetAsync(callback);
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
            Assert.DoesNotContain(ControlledOidcServer.ClientSecret, await denied.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
        Assert.Equal("anonymous", (await Session(browser)).Status);
        Assert.Null(browser.Cookies.GetCookies(host.BaseUri)[AspNetCoreIdentityDefaults.CookieScheme]);
        Assert.Equal(1, (await host.Counts()).Users);
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("deleted")]
    [InlineData("membership")]
    public async Task An_unavailable_admitted_user_or_membership_cannot_reuse_the_cookie(string change)
    {
        await using var host = await HttpHost.Start();
        using var browser = host.Browser();
        var callback = await host.StartAndAuthorize(browser);
        using (var signedIn = await browser.GetAsync(callback)) Assert.Equal(HttpStatusCode.Found, signedIn.StatusCode);
        var session = await Session(browser);
        await using (var scope = host.Scope())
        {
            var users = scope.ServiceProvider.GetRequiredService<IUserStore>();
            if (change == "disabled") Assert.True(await users.SetStatusAsync("default", session.Subject!, UserStatus.Disabled));
            if (change == "deleted") Assert.True(await users.DeleteAsync("default", session.Subject!));
            if (change == "membership") await scope.ServiceProvider.GetRequiredService<ITenantMembershipStore>()
                .SaveAsync(new("default", session.Subject!, TenantMembershipStatus.Suspended, new HashSet<string>(), new HashSet<string>()));
        }
        Assert.Equal("anonymous", (await Session(browser)).Status);
    }

    [Fact]
    public async Task A_catalog_outage_preserves_the_local_recovery_form_login_and_logout()
    {
        await using var host = await HttpHost.Start();
        using var browser = host.Browser();
        await using (var scope = host.Scope())
            await scope.ServiceProvider.GetRequiredService<IdentityProviderConfigurationDbContext>().Database.ExecuteSqlRawAsync("DROP TABLE identity_global_provider_configurations");
        var page = await browser.GetStringAsync("_elsa/identity/login");
        Assert.Contains("Some sign-in methods are currently unavailable", page, StringComparison.Ordinal);
        Assert.Contains("name=\"username\"", page, StringComparison.Ordinal);
        await host.LoginAdministrator(browser);
        using (var logout = await browser.PostAsync("_elsa/identity/logout/aspnetcore-identity", null)) Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal("anonymous", (await Session(browser)).Status);
    }

    [Fact]
    public async Task Bearer_only_administration_cannot_start_an_interactive_preview_without_the_administrators_cookie()
    {
        await using var host = await HttpHost.Start();
        using var admin = host.Browser(); using var bearerOnly = host.Browser();
        await host.LoginAdministrator(admin);
        var pair = await Pair(admin);
        bearerOnly.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pair.AccessToken);
        var before = await host.Counts();
        using var denied = await host.Begin(bearerOnly, "company", "preview");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.Equal(before, await host.Counts());
        Assert.Equal(0, host.Company.TokenRequests);
    }

    [Fact]
    public async Task Oversized_and_non_form_starts_are_refused_before_protocol_or_admission()
    {
        await using var host = await HttpHost.Start();
        using var browser = host.Browser();
        using (var oversized = await browser.PostAsync("identity/external/signin/company", new StringContent("returnUrl=" + new string('x', 16385), System.Text.Encoding.UTF8, "application/x-www-form-urlencoded")))
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
        using (var json = await browser.PostAsJsonAsync("identity/external/signin/company", new { returnUrl = "/" }))
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, json.StatusCode);
        Assert.Equal(0, host.Company.TokenRequests);
        Assert.Equal(1, (await host.Counts()).Users);
    }

    [Theory]
    [InlineData(OwnershipMode.FoundationOwned, true, true)]
    [InlineData(OwnershipMode.ExternalOwned, false, false)]
    [InlineData(OwnershipMode.Hybrid, false, true)]
    public async Task External_authentication_preserves_Elsa_authority_and_current_ownership_capabilities(OwnershipMode mode, bool users, bool roles)
    {
        await using var host = await HttpHost.Start();
        using var browser = host.Browser();
        var admitted = await host.StartAndAuthorize(browser);
        using (var first = await browser.GetAsync(admitted)) Assert.Equal(HttpStatusCode.Found, first.StatusCode);
        host.Services.GetRequiredService<IOptions<FoundationIdentityOptions>>().Value.OwnershipMode = mode;
        await using (var scope = host.Scope())
        {
            var descriptors = await scope.ServiceProvider.GetRequiredService<IAuthenticationProviderResolver>().ListAsync();
            var external = Assert.Single(descriptors, descriptor => descriptor.Id == "external:company");
            var effective = scope.ServiceProvider.GetRequiredService<IEffectiveCapabilitiesResolver>()
                .Resolve(OwnershipConfiguration.FromMode(mode), external.Capabilities);
            Assert.Equal(users, effective.CanManageUsers);
            Assert.Equal(roles, effective.CanManageRoles);
            Assert.False(effective.CanMapGroups); // The owner chose provisioning-only saved Elsa roles.
        }
        var known = await host.StartAndAuthorize(browser);
        using (var returned = await browser.GetAsync(known)) Assert.Equal(HttpStatusCode.Found, returned.StatusCode);
        Assert.Equal([DefaultIdentityPermissionKeys.IdentityUsersRead], (await Session(browser)).Permissions);
        using var unknownBrowser = host.Browser();
        var unknown = await host.StartAndAuthorize(unknownBrowser, "partner");
        using var result = await unknownBrowser.GetAsync(unknown);
        Assert.Equal(users ? HttpStatusCode.Found : HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(users ? 3 : 2, (await host.Counts()).Users);
    }

    [Fact]
    public async Task External_sign_in_cannot_bypass_an_existing_accounts_local_second_factor()
    {
        await using var host = await HttpHost.Start();
        using var original = host.Browser();
        var admitted = await host.StartAndAuthorize(original);
        using (var first = await original.GetAsync(admitted)) Assert.Equal(HttpStatusCode.Found, first.StatusCode);
        var owner = (await Session(original)).Subject!;
        await using (var scope = host.Scope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AspNetCoreIdentityUser>>();
            var user = await users.FindByIdAsync(owner);
            Assert.True((await users.SetTwoFactorEnabledAsync(user!, true)).Succeeded);
        }
        using var fresh = host.Browser();
        var callback = await host.StartAndAuthorize(fresh);
        using var denied = await fresh.GetAsync(callback);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.Null(fresh.Cookies.GetCookies(host.BaseUri)[AspNetCoreIdentityDefaults.CookieScheme]);
        Assert.Equal(2, (await host.Counts()).Users);
    }

    private sealed record SessionResponse(string Status, string? Subject, string? Provider, string[] Roles, string[] Permissions);
    private static async Task<SessionResponse> Session(HttpClient client) => (await client.GetFromJsonAsync<SessionResponse>("_elsa/identity/session"))!;
    private static async Task<AccessTokenResponse> Pair(HttpClient client)
    {
        using var response = await client.GetAsync("_elsa/identity/token");
        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.CacheControl!.NoStore);
        return (await response.Content.ReadFromJsonAsync<AccessTokenResponse>())!;
    }
    private static Task<HttpResponseMessage> Bearer(HttpClient client, string token, string path = "resource")
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.SendAsync(request);
    }

    private sealed class HttpBrowser(HttpClientHandler handler) : HttpClient(handler)
    {
        public CookieContainer Cookies => handler.CookieContainer;
        public static HttpBrowser Create(Uri address)
        {
            var browser = new HttpBrowser(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = address };
            return browser;
        }
    }

    private sealed class HttpHost : IAsyncDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), $"elsa-3579-http-{Guid.NewGuid():n}");
        private WebApplication application = null!;
        public ControlledOidcServer Company { get; private set; } = null!;
        public ControlledOidcServer Partner { get; private set; } = null!;
        public SecretResolver Secrets { get; } = new();
        public RecoveryClock Clock { get; } = new();
        public Uri BaseUri { get; private set; } = null!;
        public IServiceProvider Services => application.Services;
        public ExternalAuthenticationOptions Options => Services.GetRequiredService<IOptions<ExternalAuthenticationOptions>>().Value;

        public static async Task<HttpHost> Start(Action<IServiceCollection>? configure = null, bool databaseManagement = true)
        {
            var host = new HttpHost { Company = await ControlledOidcServer.Start(), Partner = await ControlledOidcServer.Start() };
            Directory.CreateDirectory(host.directory);
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
            builder.Logging.ClearProviders();
            builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
            var services = builder.Services;
            services.AddSingleton<TimeProvider>(host.Clock);
            services.AddAuthorization();
            services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(host.directory, "keys"))).SetApplicationName("owned-http-fixture");
            var iam = $"Data Source={Path.Combine(host.directory, "iam.db")};Pooling=False";
            services.AddFoundationAspNetCoreIdentityEntityFrameworkCore(new() { Provider = "Sqlite", ConnectionString = iam }, TestAdmin.SeedOptions(), isDevelopmentOrDemo: true);
            if (databaseManagement) services.AddIdentityProviderConfigurationEntityFrameworkCore(new() { Provider = "Sqlite", ConnectionString = iam });
            services.AddExternalAuthenticationEntityFrameworkCore(new() { Provider = "Sqlite", ConnectionString = iam });
            services.AddSingleton<ISecretValueResolver>(host.Secrets);
            services.AddFoundationExternalOidc(options => options.AllowLoopbackHttpForDevelopment = true);
            services.AddFoundationExternalAuthentication(options =>
            {
                options.AllowDatabaseManagement = databaseManagement;
                options.Connections = [Connection("company", host.Company, true), Connection("partner", host.Partner, false)];
            });
            services.AddFoundationExternalAuthenticationApi();
            services.AddFoundationIdentityApi();
            services.Configure<Elsa.Foundation.Identity.AspNetCoreIdentity.Endpoints.LoginPageOptions>(options => { options.Layout = "field-guide"; options.Palette = "field-guide"; });
            services.AddOpenIddictVendorForTests(options => options.UseSqlite($"Data Source={Path.Combine(host.directory, "tokens.db")};Pooling=False"));
            using var signing = RSA.Create(2048);
            var signingKey = Convert.ToBase64String(signing.ExportPkcs8PrivateKey());
            services.AddFoundationIdentityOpenIddict(options => { options.IsDevelopmentOrDemo = true; options.SigningKey = signingKey; });
            services.AddAuthentication(options =>
            {
                options.DefaultScheme = OpenIddictIdentityDefaults.SelectorScheme;
                options.DefaultAuthenticateScheme = OpenIddictIdentityDefaults.SelectorScheme;
                options.DefaultChallengeScheme = OpenIddictIdentityDefaults.SelectorScheme;
            });
            configure?.Invoke(services);
            var app = host.application = builder.Build();
            app.UsePathBase("/backend");
            app.UseRouting(); app.UseAuthentication(); app.UseAuthorization();
            FoundationIdentityApi.MapFoundationIdentityApi(app);
            AspNetCoreIdentityApi.MapAspNetCoreIdentityApi(app);
            ExternalAuthenticationApi.MapExternalAuthenticationApi(app);
            app.MapGet("/resource", () => Results.Ok()).RequirePermission(DefaultIdentityPermissionKeys.IdentityUsersRead);
            app.MapGet("/admin-resource", () => Results.Ok()).RequirePermission(DefaultIdentityPermissionKeys.IdentityProvidersManage);
            await using (var scope = host.Scope())
            {
                await scope.ServiceProvider.GetRequiredService<IdentityIamDbContext>().Database.MigrateAsync();
                if (databaseManagement) await scope.ServiceProvider.GetRequiredService<IdentityProviderConfigurationDbContext>().Database.MigrateAsync();
                await scope.ServiceProvider.GetRequiredService<ExternalAuthenticationDbContext>().Database.MigrateAsync();
                await scope.ServiceProvider.GetRequiredService<OpenIddictIdentityDbContext>().Database.EnsureCreatedAsync();
            }
            await app.StartAsync();
            host.BaseUri = new(app.Urls.Single().TrimEnd('/') + "/backend/");
            host.Options.PublicBaseUrl = host.BaseUri.AbsoluteUri.TrimEnd('/');
            await using (var scope = host.Scope())
            {
                await scope.ServiceProvider.GetRequiredService<IRoleStore>().SaveAsync(new("reader", "default", "Reader", null,
                    new HashSet<string> { DefaultIdentityPermissionKeys.IdentityUsersRead }, false));
                var flows = scope.ServiceProvider.GetRequiredService<IExternalAuthenticationFlowService>();
                foreach (var (key, provider) in new[] { ("company", host.Company), ("partner", host.Partner) })
                {
                    provider.RegisteredCallbacks.Add(flows.GetCallbackUri(key, AuthenticationArtifactPurpose.SignIn)!);
                    provider.RegisteredCallbacks.Add(flows.GetCallbackUri(key, AuthenticationArtifactPurpose.Preview)!);
                }
            }
            return host;
        }

        public HttpBrowser Browser() => HttpBrowser.Create(BaseUri);
        public ServiceProvider RuntimeNode(string applicationName = "owned-http-fixture", IConnectionCatalogStore? catalog = null)
        {
            var services = new ServiceCollection();
            services.AddLogging(); services.AddSingleton(application.Environment);
            services.AddSingleton<Microsoft.Extensions.Hosting.IHostEnvironment>(application.Environment);
            services.AddSingleton<TimeProvider>(Clock);
            if (catalog is not null) services.AddSingleton(catalog);
            services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(directory, "keys"))).SetApplicationName(applicationName);
            var iam = $"Data Source={Path.Combine(directory, "iam.db")};Pooling=False";
            services.AddFoundationAspNetCoreIdentityEntityFrameworkCore(new() { Provider = "Sqlite", ConnectionString = iam }, isDevelopmentOrDemo: true);
            services.Configure<AspNetCoreIdentityOptions>(configured => configured.Enabled = Services.GetRequiredService<IOptions<AspNetCoreIdentityOptions>>().Value.Enabled);
            if (Options.AllowDatabaseManagement) services.AddIdentityProviderConfigurationEntityFrameworkCore(new() { Provider = "Sqlite", ConnectionString = iam });
            services.AddExternalAuthenticationEntityFrameworkCore(new() { Provider = "Sqlite", ConnectionString = iam });
            services.AddSingleton<ISecretValueResolver>(Secrets);
            services.AddFoundationExternalOidc(configured => configured.AllowLoopbackHttpForDevelopment = true);
            services.AddFoundationExternalAuthentication(configured =>
            {
                configured.PublicBaseUrl = Options.PublicBaseUrl;
                configured.AllowDatabaseManagement = Options.AllowDatabaseManagement;
                configured.AllowDeploymentOverrides = Options.AllowDeploymentOverrides;
                configured.RecoveryEvidenceLifetime = Options.RecoveryEvidenceLifetime;
                configured.Connections = Options.Connections.ToList();
            });
            services.AddFoundationExternalAuthenticationApi();
            return services.BuildServiceProvider();
        }
        public AsyncServiceScope Scope()
        {
            var scope = application.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(PersistenceAccessContext.Scoped(new PersistenceScope("default")));
            return scope;
        }
        public async Task<(int Users, int Links, int Tokens)> Counts()
        {
            await using var scope = Scope();
            var iam = scope.ServiceProvider.GetRequiredService<IdentityIamDbContext>();
            var tokens = scope.ServiceProvider.GetRequiredService<OpenIddictIdentityDbContext>();
            return (await iam.Users.CountAsync(), await iam.ExternalIdentities.CountAsync(), await tokens.Set<global::OpenIddict.EntityFrameworkCore.Models.OpenIddictEntityFrameworkCoreToken>().CountAsync());
        }
        public async Task<string> Csrf(HttpClient browser)
        {
            using var response = await browser.GetAsync("identity/external/antiforgery");
            response.EnsureSuccessStatusCode();
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            return json.RootElement.GetProperty("requestToken").GetString()!;
        }
        public async Task<HttpResponseMessage> Begin(HttpClient browser, string key, string purpose = "signin", string? record = null, string returnUrl = "/backend/resource") =>
            await browser.PostAsync($"identity/external/{purpose}/{key}", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                [AspNetCoreIdentityDefaults.AntiforgeryFieldName] = await Csrf(browser), ["returnUrl"] = returnUrl,
                ["connectionRecordId"] = record ?? "deployment:" + key
            }));
        public async Task<Uri> StartAndAuthorize(HttpClient browser, string key = "company", string purpose = "signin", string? record = null, string returnUrl = "/backend/resource")
        {
            using var start = await Begin(browser, key, purpose, record, returnUrl);
            Assert.Equal(HttpStatusCode.Found, start.StatusCode);
            Assert.Contains(start.Headers.GetValues("Set-Cookie"), cookie => cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase) && cookie.Contains("samesite=lax", StringComparison.OrdinalIgnoreCase));
            using var upstream = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
            using var authorized = await upstream.GetAsync(start.Headers.Location);
            Assert.Equal(HttpStatusCode.Found, authorized.StatusCode);
            return authorized.Headers.Location!;
        }
        public async Task LoginAdministrator(HttpClient browser)
        {
            using var response = await browser.PostAsync("_elsa/identity/login", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                [AspNetCoreIdentityDefaults.AntiforgeryFieldName] = await Csrf(browser), ["username"] = TestAdmin.UserName,
                ["password"] = TestAdmin.Password, ["returnUrl"] = "/backend/resource"
            }));
            Assert.Equal(HttpStatusCode.Found, response.StatusCode);
            Assert.Equal("authenticated", (await Session(browser)).Status);
        }
        private static ConnectionRecord Connection(string key, ControlledOidcServer server, bool preferred) => new()
        {
            Key = key, DisplayName = key == "company" ? "Company" : "Partner", Enabled = true, Preferred = preferred,
            Settings = new Dictionary<string, string> { ["authority"] = server.Issuer, ["clientId"] = ControlledOidcServer.ClientId },
            Secrets = new Dictionary<string, SecretReference> { ["clientSecret"] = new("client-" + key, SecretTypeNames.Text, "identity/external") },
            Admission = new() { Type = "jit", DefaultRoleIds = ["reader"] }
        };
        public async ValueTask DisposeAsync()
        {
            await application.DisposeAsync(); await Company.DisposeAsync(); await Partner.DisposeAsync(); Directory.Delete(directory, true);
        }
    }
    private sealed class SecretResolver : ISecretValueResolver
    {
        public int Version = 1;
        public bool Available = true;
        public ValueTask<ResolvedSecret> ResolveAsync(string tenantId, SecretReference reference, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Available ? ResolvedSecret.Success(ControlledOidcServer.ClientSecret, new()
                { Id = reference.Name, TenantId = tenantId, Name = reference.Name, Scope = reference.Scope, Status = SecretStatus.Active, CurrentVersion = Version })
                : ResolvedSecret.Failure(SecretResolutionFailureCode.NotFound, "not available"));
    }
    private sealed class RecoveryClock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Elsa.Foundation.Identity.Api.Models;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Foundation.Identity.AspNetCoreIdentity;
using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.ExternalAuthentication.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Elsa.Foundation.Identity.Tests.ManagedOidc;

public sealed partial class ExternalAuthenticationHttpTests
{
    [Fact]
    public async Task Removing_and_recreating_one_qualified_link_cannot_revive_its_credentials_and_preserves_another_link_for_the_same_user()
    {
        await using var host = await HttpHost.Start();
        using var company = host.Browser(); using var partner = host.Browser(); using var fresh = host.Browser();
        var callback = await host.StartAndAuthorize(company);
        using (var completed = await company.GetAsync(callback)) Assert.Equal(HttpStatusCode.Found, completed.StatusCode);
        var user = (await Session(company)).Subject!; var pair = await Pair(company);
        await LinkPartnerToSameUser(host, user);
        var otherCallback = await host.StartAndAuthorize(partner, "partner");
        using (var completed = await partner.GetAsync(otherCallback)) Assert.Equal(HttpStatusCode.Found, completed.StatusCode);
        var otherPair = await Pair(partner);
        Assert.Equal(user, (await Session(partner)).Subject);
        await RemoveAndRecreateCompanyLink(host, user);
        // Keep the old cookie untouched until recreation, so rejection during removal cannot hide revival.
        Assert.Equal("anonymous", (await Session(company)).Status);
        using (var denied = await Bearer(company, pair.AccessToken)) Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using (var denied = await company.PostAsJsonAsync("_elsa/identity/refresh", new { refreshToken = pair.RefreshToken })) Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.Equal(user, (await Session(partner)).Subject);
        using (var live = await Bearer(partner, otherPair.AccessToken)) Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        using (var live = await partner.PostAsJsonAsync("_elsa/identity/refresh", new { refreshToken = otherPair.RefreshToken })) Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        var freshCallback = await host.StartAndAuthorize(fresh);
        using (var completed = await fresh.GetAsync(freshCallback)) Assert.Equal(HttpStatusCode.Found, completed.StatusCode);
        Assert.Equal(user, (await Session(fresh)).Subject); Assert.Equal(["reader"], (await Session(fresh)).Roles);
    }

    private static async Task LinkPartnerToSameUser(HttpHost host, string user)
    {
        await using var scope = host.Scope();
        await scope.ServiceProvider.GetRequiredService<IExternalIdentityStore>().SaveAsync(new("default", "partner", "opaque-Subject", user,
            host.Clock.GetUtcNow(), host.Clock.GetUtcNow(), ExternalIdentityLinkPolicy.Admin) { Issuer = host.Partner.Issuer });
    }

    private static async Task RemoveAndRecreateCompanyLink(HttpHost host, string user)
    {
        await using var scope = host.Scope(); var services = scope.ServiceProvider;
        var key = new ExternalIdentityKey("default", "company", host.Company.Issuer, "opaque-Subject");
        var store = services.GetRequiredService<IRevisionAwareExternalIdentityStore>();
        var link = (await store.FindByKeyWithRevisionAsync(key))!;
        var guard = (await services.GetRequiredService<IRevisionAwareUserStore>().FindRevisionGuardAsync("default", user))!;
        Assert.Equal(ExternalIdentityRemovalStatus.Removed, (await store.RemoveWithRevisionAsync(key, guard, link.Revision)).Status);
        await services.GetRequiredService<IExternalIdentityStore>().SaveAsync(link.Record);
        var recreated = (await store.FindByKeyWithRevisionAsync(key))!;
        Assert.NotEqual(link.Revision, recreated.Revision);
        Assert.Equal(link.Record.LinkedAt, recreated.Record.LinkedAt);
    }

    [Fact]
    public void Canonical_sign_in_session_bindings_cannot_be_serialized_or_bound_from_client_JSON()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var session = new AuthSession("authenticated", "user", null, "default", new HashSet<string>(), new HashSet<string>(), "fresh", "external:company")
        { AuthenticationSessionId = "server-binding-private-canary" };
        var provenance = new ExternalAuthenticationProvenance("company", "source", "https://issuer.example", "subject")
        { AuthenticationSessionId = "server-binding-private-canary" };
        foreach (var json in new[] { JsonSerializer.Serialize(session, options), JsonSerializer.Serialize(provenance, options) })
        {
            Assert.DoesNotContain("authenticationSessionId", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("server-binding-private-canary", json, StringComparison.Ordinal);
        }
        const string forged = "{\"status\":\"authenticated\",\"authenticationSessionId\":\"client-binding-private-canary\"}";
        Assert.Null(JsonSerializer.Deserialize<AuthSession>(forged, options)!.AuthenticationSessionId);
        Assert.Null(JsonSerializer.Deserialize<ExternalAuthenticationProvenance>(forged, options)!.AuthenticationSessionId);
    }

    [Fact]
    public async Task Rebinding_a_qualified_identity_away_and_back_cannot_resurrect_previous_cookies_access_or_refresh()
    {
        await using var host = await HttpHost.Start();
        using var original = host.Browser(); using var unrelated = host.Browser(); using var fresh = host.Browser();
        var callback = await host.StartAndAuthorize(original);
        using (var completed = await original.GetAsync(callback)) Assert.Equal(HttpStatusCode.Found, completed.StatusCode);
        var unrelatedCallback = await host.StartAndAuthorize(unrelated, "partner");
        using (var completed = await unrelated.GetAsync(unrelatedCallback)) Assert.Equal(HttpStatusCode.Found, completed.StatusCode);
        var user = (await Session(original)).Subject!; var pair = await Pair(original); var otherPair = await Pair(unrelated);
        await AddLinkTarget(host, "temporary-owner");
        await using (var scope = host.Scope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IRevisionAwareExternalIdentityStore>();
            var key = new ExternalIdentityKey("default", "company", host.Company.Issuer, "opaque-Subject");
            var initial = (await store.FindByKeyWithRevisionAsync(key))!;
            Assert.Equal(user, initial.Record.UserId);
            Assert.Equal(IamRevisionSaveStatus.Saved, (await store.SaveWithRevisionAsync(initial.Record with { UserId = "temporary-owner" }, initial.Revision)).Status);
            var rebound = (await store.FindByKeyWithRevisionAsync(key))!;
            Assert.Equal(IamRevisionSaveStatus.Saved, (await store.SaveWithRevisionAsync(rebound.Record with { UserId = user }, rebound.Revision)).Status);
        }
        // Keep the original cookie untouched through both ownership changes: rejection during the
        // first change must not hide resurrection after the link returns to its former owner.
        Assert.Equal("anonymous", (await Session(original)).Status);
        using (var access = await Bearer(original, pair.AccessToken)) Assert.Equal(HttpStatusCode.Unauthorized, access.StatusCode);
        using (var refresh = await original.PostAsJsonAsync("_elsa/identity/refresh", new { refreshToken = pair.RefreshToken }))
            Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        Assert.Equal("authenticated", (await Session(unrelated)).Status);
        using (var access = await Bearer(unrelated, otherPair.AccessToken)) Assert.Equal(HttpStatusCode.OK, access.StatusCode);
        using (var refresh = await unrelated.PostAsJsonAsync("_elsa/identity/refresh", new { refreshToken = otherPair.RefreshToken }))
            Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var newCallback = await host.StartAndAuthorize(fresh);
        using (var completed = await fresh.GetAsync(newCallback)) Assert.Equal(HttpStatusCode.Found, completed.StatusCode);
        Assert.Equal(user, (await Session(fresh)).Subject);
        Assert.Equal(["reader"], (await Session(fresh)).Roles);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_existing_user_created_through_the_Elsa_store_can_match_and_receive_a_durable_session_without_a_local_password(bool legacyWithoutEpoch)
    {
        await using var host = await HttpHost.Start(); using var browser = host.Browser();
        await using (var scope = host.Scope())
        {
            await scope.ServiceProvider.GetRequiredService<IUserStore>().SaveAsync(new("existing-user", "default", "existing", "ada@example.test", "Existing user",
                UserStatus.Active, ResourceOwnership.Foundation, new HashSet<string> { "reader" }, new HashSet<string>()));
            if (legacyWithoutEpoch)
            {
                await scope.ServiceProvider.GetRequiredService<IdentityIamDbContext>().Users.Where(row => row.UserId == "existing-user")
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.SecurityStamp, (string?)null));
                Assert.True(await scope.ServiceProvider.GetRequiredService<ICanonicalUserSignInService>().CanSignInAsync(new("default", "existing-user")));
                var legacyManager = scope.ServiceProvider.GetRequiredService<UserManager<AspNetCoreIdentityUser>>();
                Assert.Null((await legacyManager.FindByIdAsync("existing-user"))!.SecurityStamp); // Eligibility stays read-only.
            }
        }
        host.Options.Connections[0] = host.Options.Connections[0] with { Admission = new() { Type = "match", Matcher = "verified-email", DefaultRoleIds = ["reader"] } };
        var callback = await host.StartAndAuthorize(browser);
        using (var complete = await browser.GetAsync(callback)) Assert.Equal(HttpStatusCode.Found, complete.StatusCode);
        Assert.Equal("existing-user", (await Session(browser)).Subject);
        var pair = await Pair(browser);
        using var access = await Bearer(browser, pair.AccessToken); Assert.Equal(HttpStatusCode.OK, access.StatusCode);
        await using var verify = host.Scope();
        var manager = verify.ServiceProvider.GetRequiredService<UserManager<AspNetCoreIdentityUser>>();
        Assert.False(await manager.HasPasswordAsync((await manager.FindByIdAsync("existing-user"))!));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("user")]
    [InlineData("tenant")]
    [InlineData("external")]
    public async Task First_party_issuance_cannot_attach_a_session_to_another_subject_or_omit_its_binding(string mismatch)
    {
        await using var host = await HttpHost.Start(); using var browser = host.Browser();
        var callback = await host.StartAndAuthorize(browser);
        using (var complete = await browser.GetAsync(callback)) Assert.Equal(HttpStatusCode.Found, complete.StatusCode);
        var pair = await Pair(browser); var userId = (await Session(browser)).Subject!;
        var sessionId = new JsonWebToken(pair.AccessToken).GetClaim(IdentityClaimTypes.SessionId).Value;
        var before = await host.Counts();
        var subject = mismatch switch
        {
            "user" => new TokenSubject("default", "another-user"),
            "tenant" => new TokenSubject("another-tenant", userId),
            "external" => new TokenSubject("default", userId, "company", host.Company.Issuer),
            _ => new TokenSubject("default", userId)
        };
        await using var scope = host.Scope();
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<ITokenService>()
            .IssueAsync(new(subject, ["*"]) { SessionId = mismatch == "missing" ? null : sessionId }).AsTask());
        Assert.Equal(before, await host.Counts());
        using var stillValid = await Bearer(browser, pair.AccessToken);
        Assert.Equal(HttpStatusCode.OK, stillValid.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    public async Task An_invalid_session_lifetime_refuses_managed_authentication_before_provider_or_admission_work(int days)
    {
        await using var host = await HttpHost.Start();
        host.Services.GetRequiredService<IOptions<AspNetCoreIdentityOptions>>().Value.SessionLifetime = TimeSpan.FromDays(days);
        using var browser = host.Browser(); var before = await host.Counts();
        using var start = await host.Begin(browser, "company");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, start.StatusCode);
        Assert.Equal(before, await host.Counts()); Assert.Equal(0, host.Company.TokenRequests);
        await using var scope = host.Scope();
        Assert.False(scope.ServiceProvider.GetRequiredService<ICanonicalUserSignInService>().SupportsDurableSessions);
    }

    [Fact]
    public async Task Revoking_one_durable_session_kills_its_cookie_access_and_refresh_while_other_sessions_of_the_same_user_survive()
    {
        await using var host = await HttpHost.Start();
        using var first = host.Browser(); using var second = host.Browser(); using var partner = host.Browser();
        foreach (var browser in new[] { first, second })
        { var callback = await host.StartAndAuthorize(browser); using var completed = await browser.GetAsync(callback); Assert.Equal(HttpStatusCode.Found, completed.StatusCode); }
        var partnerCallback = await host.StartAndAuthorize(partner, "partner");
        using (var completed = await partner.GetAsync(partnerCallback)) Assert.Equal(HttpStatusCode.Found, completed.StatusCode);
        var firstPair = await Pair(first); var secondPair = await Pair(second); var partnerPair = await Pair(partner);
        Assert.Equal((await Session(first)).Subject, (await Session(second)).Subject);
        var firstId = new JsonWebToken(firstPair.AccessToken).GetClaim(IdentityClaimTypes.SessionId).Value;
        var secondId = new JsonWebToken(secondPair.AccessToken).GetClaim(IdentityClaimTypes.SessionId).Value;
        Assert.NotEqual(firstId, secondId);
        using var rotated = await first.PostAsJsonAsync("_elsa/identity/refresh", new { refreshToken = firstPair.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        var rotation = (await rotated.Content.ReadFromJsonAsync<AccessTokenResponse>())!;
        Assert.Equal(firstId, new JsonWebToken(rotation.AccessToken).GetClaim(IdentityClaimTypes.SessionId).Value);
        await using (var remote = host.RuntimeNode())
        await using (var scope = remote.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<Elsa.Workflows.Runtime.Core.Contracts.IPersistenceAccessContextBinder>()
                .Bind(Elsa.Workflows.Runtime.Core.Models.PersistenceAccessContext.Scoped(new("default")));
            var store = scope.ServiceProvider.GetRequiredService<IAuthenticationSessionStore>();
            var session = (await store.FindAsync("default", firstId))!;
            Assert.Equal("company", session.ConnectionKey); Assert.Equal(host.Company.Issuer, session.Issuer);
            Assert.Equal("opaque-Subject", session.ExternalSubject);
            Assert.Equal(AuthenticationSessionRevocationStatus.Revoked, await store.RevokeAsync("default", firstId, session.Revision!, DateTimeOffset.UtcNow));
        }
        using (var access = await Bearer(first, firstPair.AccessToken)) Assert.Equal(HttpStatusCode.Unauthorized, access.StatusCode);
        using (var access = await Bearer(first, rotation.AccessToken)) Assert.Equal(HttpStatusCode.Unauthorized, access.StatusCode);
        using (var refresh = await first.PostAsJsonAsync("_elsa/identity/refresh", new { refreshToken = rotation.RefreshToken })) Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        Assert.Equal("anonymous", (await Session(first)).Status);
        Assert.Equal("authenticated", (await Session(second)).Status);
        using (var access = await Bearer(second, secondPair.AccessToken)) Assert.Equal(HttpStatusCode.OK, access.StatusCode);
        using (var refresh = await second.PostAsJsonAsync("_elsa/identity/refresh", new { refreshToken = secondPair.RefreshToken })) Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        using (var access = await Bearer(partner, partnerPair.AccessToken)) Assert.Equal(HttpStatusCode.OK, access.StatusCode);
    }

    [Theory]
    [InlineData("disable")]
    [InlineData("delete")]
    [InlineData("membership")]
    [InlineData("stamp")]
    [InlineData("lockout")]
    [InlineData("second-factor")]
    [InlineData("unlink")]
    public async Task Live_account_or_link_invalidation_refuses_issued_access_refresh_and_cookie_without_affecting_an_unrelated_user(string change)
    {
        await using var host = await HttpHost.Start();
        using var target = host.Browser(); using var control = host.Browser();
        var callback = await host.StartAndAuthorize(target);
        using (var complete = await target.GetAsync(callback)) Assert.Equal(HttpStatusCode.Found, complete.StatusCode);
        var other = await host.StartAndAuthorize(control, "partner");
        using (var complete = await control.GetAsync(other)) Assert.Equal(HttpStatusCode.Found, complete.StatusCode);
        var pair = await Pair(target); var controlPair = await Pair(control); var subject = (await Session(target)).Subject!;
        await using (var scope = host.Scope())
        {
            var users = scope.ServiceProvider.GetRequiredService<IUserStore>();
            if (change == "disable") Assert.True(await users.SetStatusAsync("default", subject, UserStatus.Disabled));
            else if (change == "delete") Assert.True(await users.DeleteAsync("default", subject));
            else if (change == "membership") await scope.ServiceProvider.GetRequiredService<ITenantMembershipStore>().SaveAsync(new("default", subject,
                TenantMembershipStatus.Suspended, new HashSet<string>(), new HashSet<string>()));
            else if (change == "unlink") Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<IdentityIamDbContext>().ExternalIdentities.Where(row => row.UserId == subject).ExecuteDeleteAsync());
            else
            {
                var manager = scope.ServiceProvider.GetRequiredService<UserManager<AspNetCoreIdentityUser>>();
                var user = (await manager.FindByIdAsync(subject))!;
                if (change == "lockout") Assert.True((await manager.SetLockoutEnabledAsync(user, true)).Succeeded);
                var result = change switch
                {
                    "stamp" => await manager.UpdateSecurityStampAsync(user),
                    "lockout" => await manager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddHours(1)),
                    _ => await manager.SetTwoFactorEnabledAsync(user, true)
                };
                Assert.True(result.Succeeded);
            }
        }
        using (var access = await Bearer(target, pair.AccessToken)) Assert.Equal(HttpStatusCode.Unauthorized, access.StatusCode);
        using (var refresh = await target.PostAsJsonAsync("_elsa/identity/refresh", new { refreshToken = pair.RefreshToken })) Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        Assert.Equal("anonymous", (await Session(target)).Status);
        using (var exchange = await target.GetAsync("_elsa/identity/token")) Assert.Equal(HttpStatusCode.Unauthorized, exchange.StatusCode);
        using (var access = await Bearer(control, controlPair.AccessToken)) Assert.Equal(HttpStatusCode.OK, access.StatusCode);
        using (var refresh = await control.PostAsJsonAsync("_elsa/identity/refresh", new { refreshToken = controlPair.RefreshToken })) Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        Assert.Equal("authenticated", (await Session(control)).Status);
    }

    [Fact]
    public async Task Absolute_session_expiry_caps_tokens_and_refresh_rotation_cannot_extend_it()
    {
        await using var host = await HttpHost.Start();
        host.Services.GetRequiredService<IOptions<AspNetCoreIdentityOptions>>().Value.SessionLifetime = TimeSpan.FromMinutes(1);
        using var browser = host.Browser();
        var callback = await host.StartAndAuthorize(browser);
        using (var complete = await browser.GetAsync(callback)) Assert.Equal(HttpStatusCode.Found, complete.StatusCode);
        var pair = await Pair(browser);
        var id = new JsonWebToken(pair.AccessToken).GetClaim(IdentityClaimTypes.SessionId).Value;
        AuthenticationSessionRecord original;
        await using (var scope = host.Scope()) original = (await scope.ServiceProvider.GetRequiredService<IAuthenticationSessionStore>().FindAsync("default", id))!;
        Assert.Equal(original.ExpiresAt, pair.ExpiresAt);
        host.Clock.Now += TimeSpan.FromSeconds(30);
        using var refresh = await browser.PostAsJsonAsync("_elsa/identity/refresh", new { refreshToken = pair.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var rotated = (await refresh.Content.ReadFromJsonAsync<AccessTokenResponse>())!;
        Assert.Equal(original.ExpiresAt, rotated.ExpiresAt);
        await using (var scope = host.Scope()) Assert.Equal(original, await scope.ServiceProvider.GetRequiredService<IAuthenticationSessionStore>().FindAsync("default", id));
        host.Clock.Now += TimeSpan.FromSeconds(31);
        using (var access = await Bearer(browser, rotated.AccessToken)) Assert.Equal(HttpStatusCode.Unauthorized, access.StatusCode);
        using (var retry = await browser.PostAsJsonAsync("_elsa/identity/refresh", new { refreshToken = rotated.RefreshToken })) Assert.Equal(HttpStatusCode.Unauthorized, retry.StatusCode);
        Assert.Equal("anonymous", (await Session(browser)).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reenabling_a_user_does_not_resurrect_sessions_issued_before_disable(bool saveWholeRecord)
    {
        await using var host = await HttpHost.Start(); using var browser = host.Browser();
        var callback = await host.StartAndAuthorize(browser);
        using (var complete = await browser.GetAsync(callback)) Assert.Equal(HttpStatusCode.Found, complete.StatusCode);
        var pair = await Pair(browser); var id = (await Session(browser)).Subject!;
        await using (var scope = host.Scope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IUserStore>();
            if (saveWholeRecord)
            {
                var user = (await store.FindAsync("default", id))!;
                await store.SaveAsync(user with { Status = UserStatus.Disabled });
                await store.SaveAsync(user);
            }
            else
            { Assert.True(await store.SetStatusAsync("default", id, UserStatus.Disabled)); Assert.True(await store.SetStatusAsync("default", id, UserStatus.Active)); }
        }
        // No authentication request was made while disabled; the epoch changed in the actual status transaction.
        using (var access = await Bearer(browser, pair.AccessToken)) Assert.Equal(HttpStatusCode.Unauthorized, access.StatusCode);
        using (var refresh = await browser.PostAsJsonAsync("_elsa/identity/refresh", new { refreshToken = pair.RefreshToken })) Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        Assert.Equal("anonymous", (await Session(browser)).Status);
        var newCallback = await host.StartAndAuthorize(browser);
        using (var complete = await browser.GetAsync(newCallback)) Assert.Equal(HttpStatusCode.Found, complete.StatusCode);
        Assert.Equal(id, (await Session(browser)).Subject);
        using var current = await Bearer(browser, (await Pair(browser)).AccessToken);
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
    }
}

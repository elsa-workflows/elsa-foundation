using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Elsa.Foundation.Identity.AspNetCoreIdentity;
using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Core.Security;
using Elsa.Foundation.Identity.ExternalAuthentication.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.Tests.ManagedOidc;

public sealed partial class ExternalAuthenticationHttpTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task HTTP_unlink_preserves_a_proved_sibling_credential_and_invalidates_only_the_removed_link(bool managed, bool endedObservation)
    {
        var events = new CapturedSecurityEvents();
        await using var host = await HttpHost.Start(services => services.AddSingleton<IAuthEventSink>(events), databaseManagement: managed);
        using var administrator = host.Browser(); using var company = host.Browser(); using var partner = host.Browser();
        await host.LoginAdministrator(administrator);
        var user = await SignInCompanyAndPartner(host, company, partner);
        var old = await Pair(company); var other = await Pair(partner);
        if (endedObservation)
        {
            var sessions = await AccessDocument(administrator, $"/users/{user}/sessions");
            var selected = sessions.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("connectionKey").GetString() == "partner");
            using var revoked = await ManagementPost(host, administrator, $"/users/{user}/sessions/{selected.GetProperty("id").GetString()}/revoke",
                new { expectedRevision = selected.GetProperty("revision").GetString() });
            Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
        }
        var state = await AccessDocument(administrator, $"/users/{user}/links");
        using (var removed = await ManagementPost(host, administrator, $"/users/{user}/links/remove", RemoveRequest(state, "company")))
        { Assert.Equal(HttpStatusCode.OK, removed.StatusCode); Assert.True((await removed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("removed").GetBoolean()); }
        Assert.Equal("anonymous", (await Session(company)).Status);
        using (var denied = await Bearer(company, old.AccessToken)) Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using (var denied = await company.PostAsJsonAsync("_elsa/identity/refresh", new { refreshToken = old.RefreshToken })) Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.Equal(endedObservation ? "anonymous" : "authenticated", (await Session(partner)).Status);
        using (var response = await Bearer(partner, other.AccessToken)) Assert.Equal(endedObservation ? HttpStatusCode.Unauthorized : HttpStatusCode.OK, response.StatusCode);
        using (var response = await partner.PostAsJsonAsync("_elsa/identity/refresh", new { refreshToken = other.RefreshToken }))
            Assert.Equal(endedObservation ? HttpStatusCode.Unauthorized : HttpStatusCode.OK, response.StatusCode);
        var callback = await host.StartAndAuthorize(partner, "partner");
        using (var returned = await partner.GetAsync(callback)) Assert.Equal(HttpStatusCode.Found, returned.StatusCode);
        Assert.Equal(user, (await Session(partner)).Subject);
        var after = await AccessDocument(administrator, $"/users/{user}/links");
        Assert.Equal("partner", Assert.Single(after.GetProperty("items").EnumerateArray()).GetProperty("provider").GetString());
        Assert.Equal(managed, after.GetProperty("catalogRevision").GetString() != state.GetProperty("catalogRevision").GetString());
        await using var current = host.Scope();
        Assert.True((await current.ServiceProvider.GetRequiredService<IUserStore>().FindAsync("default", user))!.RoleIds.SetEquals(new[] { "reader" }));
        Assert.Contains(events.Items, item => item.Detail["action"] == "link.remove" && item.Outcome == AuthEventOutcome.Success);
        Assert.DoesNotContain("opaque-Subject", JsonSerializer.Serialize(events.Items), StringComparison.Ordinal);
        Assert.DoesNotContain("credentialBinding", after.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task HTTP_unlink_refuses_the_users_final_path_including_provider_key_aliases(bool managed, bool alias)
    {
        await using var host = await HttpHost.Start(databaseManagement: managed);
        using var administrator = host.Browser(); using var userBrowser = host.Browser();
        await host.LoginAdministrator(administrator);
        var callback = await host.StartAndAuthorize(userBrowser);
        using (var returned = await userBrowser.GetAsync(callback)) Assert.Equal(HttpStatusCode.Found, returned.StatusCode);
        var user = (await Session(userBrowser)).Subject!;
        var state = await AccessDocument(administrator, $"/users/{user}/links");
        var request = RemoveRequest(state, "company"); if (alias) request["provider"] = "COMPANY";
        using var denied = await ManagementPost(host, administrator, $"/users/{user}/links/remove", request);
        Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
        Assert.Equal("final-login-path", (await denied.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
        Assert.Equal(user, (await Session(userBrowser)).Subject);
        var after = await AccessDocument(administrator, $"/users/{user}/links");
        Assert.Single(after.GetProperty("items").EnumerateArray());
        Assert.Equal(state.GetProperty("catalogRevision").GetString(), after.GetProperty("catalogRevision").GetString());
    }

    [Fact]
    public async Task HTTP_unlink_does_not_count_a_configured_link_without_normal_sign_in_evidence()
    {
        await using var host = await HttpHost.Start(); using var administrator = host.Browser(); using var company = host.Browser();
        await host.LoginAdministrator(administrator);
        var callback = await host.StartAndAuthorize(company);
        using (var returned = await company.GetAsync(callback)) Assert.Equal(HttpStatusCode.Found, returned.StatusCode);
        var user = (await Session(company)).Subject!; await LinkPartnerToSameUser(host, user);
        var state = await AccessDocument(administrator, $"/users/{user}/links");
        using var denied = await ManagementPost(host, administrator, $"/users/{user}/links/remove", RemoveRequest(state, "company"));
        Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
        Assert.Equal("final-login-path", (await denied.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
    }

    [Theory]
    [InlineData("secret")]
    [InlineData("source")]
    [InlineData("callback")]
    [InlineData("expired")]
    [InlineData("account-instance")]
    [InlineData("link-instance")]
    public async Task HTTP_unlink_refuses_stale_remaining_credential_evidence(string stale)
    {
        await using var host = await HttpHost.Start();
        using var administrator = host.Browser(); using var company = host.Browser(); using var partner = host.Browser();
        await host.LoginAdministrator(administrator); var user = await SignInCompanyAndPartner(host, company, partner);
        await using (var scope = host.Scope())
        {
            switch (stale)
            {
                case "secret": host.Secrets.Version++; break;
                case "source": host.Options.Connections[1] = host.Options.Connections[1] with { DisplayOrder = 7, Admission = new() { Type = "reject" } }; break;
                case "callback": host.Options.PublicBaseUrl += "/changed"; break;
                case "expired": host.Options.RecoveryEvidenceLifetime = TimeSpan.FromMinutes(1); host.Clock.Now += TimeSpan.FromMinutes(2); break;
                case "account-instance":
                    var users = scope.ServiceProvider.GetRequiredService<IUserStore>();
                    Assert.True(await users.SetStatusAsync("default", user, UserStatus.Disabled));
                    Assert.True(await users.SetStatusAsync("default", user, UserStatus.Active)); break;
                case "link-instance":
                    var links = scope.ServiceProvider.GetRequiredService<IRevisionAwareExternalIdentityStore>();
                    var key = new ExternalIdentityKey("default", "partner", host.Partner.Issuer, "opaque-Subject");
                    var original = (await links.FindByKeyWithRevisionAsync(key))!;
                    var owner = (await scope.ServiceProvider.GetRequiredService<IRevisionAwareUserStore>().FindRevisionGuardAsync("default", user))!;
                    Assert.Equal(ExternalIdentityRemovalStatus.Removed, (await links.RemoveWithRevisionAsync(key, owner, original.Revision)).Status);
                    await scope.ServiceProvider.GetRequiredService<IExternalIdentityStore>().SaveAsync(original.Record); break;
            }
        }
        var state = await AccessDocument(administrator, $"/users/{user}/links");
        using var denied = await ManagementPost(host, administrator, $"/users/{user}/links/remove", RemoveRequest(state, "company"));
        Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
        Assert.Equal("final-login-path", (await denied.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
        Assert.Equal(2, (await AccessDocument(administrator, $"/users/{user}/links")).GetProperty("items").GetArrayLength());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HTTP_unlink_requires_global_administrative_recovery_as_well_as_the_users_other_path(bool managed)
    {
        await using var host = await HttpHost.Start(databaseManagement: managed);
        using var administrator = host.Browser(); using var company = host.Browser(); using var partner = host.Browser();
        await host.LoginAdministrator(administrator); var user = await SignInCompanyAndPartner(host, company, partner);
        host.Services.GetRequiredService<IOptions<AspNetCoreIdentityOptions>>().Value.Enabled = false;
        var state = await AccessDocument(administrator, $"/users/{user}/links");
        using (var denied = await ManagementPost(host, administrator, $"/users/{user}/links/remove", RemoveRequest(state, "company")))
        { Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode); Assert.Equal("final-login-path", (await denied.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString()); }
        await using (var scope = host.Scope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<IRoleStore>(); var reader = (await roles.FindAsync("default", "reader"))!;
            await roles.SaveAsync(reader with { Permissions = reader.Permissions.Append(DefaultIdentityPermissionKeys.IdentityProvidersManage).ToHashSet() });
        }
        state = await AccessDocument(administrator, $"/users/{user}/links");
        using var removed = await ManagementPost(host, administrator, $"/users/{user}/links/remove", RemoveRequest(state, "company"));
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode); Assert.Equal(user, (await Session(partner)).Subject);
    }

    private static async Task<string> SignInCompanyAndPartner(HttpHost host, HttpClient company, HttpClient partner)
    {
        var callback = await host.StartAndAuthorize(company);
        using (var returned = await company.GetAsync(callback)) Assert.Equal(HttpStatusCode.Found, returned.StatusCode);
        var user = (await Session(company)).Subject!; await LinkPartnerToSameUser(host, user);
        callback = await host.StartAndAuthorize(partner, "partner");
        using (var returned = await partner.GetAsync(callback)) Assert.Equal(HttpStatusCode.Found, returned.StatusCode);
        Assert.Equal(user, (await Session(partner)).Subject); return user;
    }

    private static Dictionary<string, object?> RemoveRequest(JsonElement state, string provider)
    {
        var link = state.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("provider").GetString() == provider);
        return new() { ["provider"] = provider, ["issuer"] = link.GetProperty("issuer").GetString(), ["subject"] = link.GetProperty("subject").GetString(),
            ["expectedLinkRevision"] = link.GetProperty("revision").GetString(), ["expectedUserRevision"] = state.GetProperty("userRevision").GetString(),
            ["expectedCatalogRevision"] = state.GetProperty("catalogRevision").GetString() };
    }
}

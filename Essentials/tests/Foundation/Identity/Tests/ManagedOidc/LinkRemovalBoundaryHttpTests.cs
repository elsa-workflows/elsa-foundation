using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.ExternalAuthentication.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.Tests.ManagedOidc;

public sealed partial class ExternalAuthenticationHttpTests
{
    [Theory]
    [InlineData("user-revision", HttpStatusCode.Conflict)]
    [InlineData("link-revision", HttpStatusCode.Conflict)]
    [InlineData("catalog-revision", HttpStatusCode.Conflict)]
    [InlineData("issuer", HttpStatusCode.NotFound)]
    [InlineData("owner", HttpStatusCode.NotFound)]
    [InlineData("subject", HttpStatusCode.NotFound)]
    [InlineData("legacy", HttpStatusCode.BadRequest)]
    [InlineData("unknown", HttpStatusCode.BadRequest)]
    [InlineData("csrf", HttpStatusCode.BadRequest)]
    [InlineData("permission", HttpStatusCode.Forbidden)]
    public async Task HTTP_unlink_enforces_revisions_exact_identity_owner_permissions_and_request_boundaries(string invalid, HttpStatusCode expected)
    {
        await using var host = await HttpHost.Start();
        using var administrator = host.Browser(); using var company = host.Browser(); using var partner = host.Browser();
        await host.LoginAdministrator(administrator); var user = await SignInCompanyAndPartner(host, company, partner);
        await AddLinkTarget(host, "other-owner");
        var state = await AccessDocument(administrator, $"/users/{user}/links");
        Assert.All(state.GetProperty("items").EnumerateArray(), item => Assert.True(item.GetProperty("canRemove").GetBoolean()));
        var request = RemoveRequest(state, "company");
        if (invalid == "user-revision") request["expectedUserRevision"] = "stale";
        if (invalid == "link-revision") request["expectedLinkRevision"] = "stale";
        if (invalid == "catalog-revision") request["expectedCatalogRevision"] = "stale";
        if (invalid == "issuer") request["issuer"] = host.Company.Issuer + "/different";
        if (invalid == "subject") request["subject"] = "different-subject";
        if (invalid == "legacy") request["issuer"] = null;
        if (invalid == "unknown") request["roleMappings"] = Array.Empty<object>();
        if (invalid == "owner")
        {
            user = "other-owner";
            request["expectedUserRevision"] = (await AccessDocument(administrator, $"/users/{user}/links")).GetProperty("userRevision").GetString();
        }
        if (invalid == "permission")
        {
            await GrantAdministrator(host, DefaultIdentityPermissionKeys.IdentityUsersRead, DefaultIdentityPermissionKeys.IdentityLinksRead);
            Assert.All((await AccessDocument(administrator, $"/users/{user}/links")).GetProperty("items").EnumerateArray(), item => Assert.False(item.GetProperty("canRemove").GetBoolean()));
        }
        using var denied = invalid == "csrf"
            ? await administrator.PostAsJsonAsync(Management + $"/users/{user}/links/remove", request)
            : await ManagementPost(host, administrator, $"/users/{user}/links/remove", request);
        Assert.Equal(expected, denied.StatusCode);
        await using var scope = host.Scope();
        Assert.NotNull(await scope.ServiceProvider.GetRequiredService<IExternalIdentityStore>().FindByKeyAsync(new("default", "company", host.Company.Issuer, "opaque-Subject")));
        Assert.Equal(state.GetProperty("catalogRevision").GetString(), (await scope.ServiceProvider.GetRequiredService<Elsa.Foundation.Identity.ExternalAuthentication.Core.IConnectionCatalogStore>().ReadAsync()).Revision);
    }

    [Fact]
    public async Task Old_sessions_without_credential_provenance_cannot_prove_an_unlink_alternative_until_fresh_sign_in()
    {
        await using var host = await HttpHost.Start();
        using var administrator = host.Browser(); using var company = host.Browser(); using var partner = host.Browser();
        await host.LoginAdministrator(administrator); var user = await SignInCompanyAndPartner(host, company, partner);
        await using (var scope = host.Scope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IAuthenticationSessionStore>();
            var original = (await store.ListAsync("default", user, new(0, 100))).Items.Single(item => item.ConnectionKey == "partner");
            Assert.True(await store.TryCreateAsync(original with { Id = new string('e', 64), Revision = null,
                ConnectionMaterialRevision = null, ConnectionCredentialBinding = null, ConnectionRedirectUri = null }));
            await scope.ServiceProvider.GetRequiredService<ExternalAuthenticationDbContext>().Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM identity_authentication_sessions WHERE Id = {original.Id}");
        }
        var state = await AccessDocument(administrator, $"/users/{user}/links");
        using (var denied = await ManagementPost(host, administrator, $"/users/{user}/links/remove", RemoveRequest(state, "company")))
        { Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode); Assert.Equal("final-login-path", (await denied.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString()); }
        var callback = await host.StartAndAuthorize(partner, "partner");
        using (var returned = await partner.GetAsync(callback)) Assert.Equal(HttpStatusCode.Found, returned.StatusCode);
        state = await AccessDocument(administrator, $"/users/{user}/links");
        using var removed = await ManagementPost(host, administrator, $"/users/{user}/links/remove", RemoveRequest(state, "company"));
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
    }
}

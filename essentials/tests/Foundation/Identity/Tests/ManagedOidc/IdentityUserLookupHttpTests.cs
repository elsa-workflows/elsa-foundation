using System.Net;
using System.Text.Json;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.Tests.ManagedOidc;

public sealed partial class ExternalAuthenticationHttpTests
{
    [Fact]
    public async Task Access_user_lookup_uses_current_user_read_grants_without_provider_management_and_projects_safe_capabilities()
    {
        await using var host = await HttpHost.Start(); using var administrator = host.Browser();
        await host.LoginAdministrator(administrator); await AddLinkTarget(host, "target");
        await GrantAdministrator(host, DefaultIdentityPermissionKeys.IdentityUsersRead, DefaultIdentityPermissionKeys.IdentityLinksRead);
        using var response = await administrator.GetAsync(Management + "/users?skip=0&take=1&search=target");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync(); using var json = JsonDocument.Parse(text); var root = json.RootElement;
        Assert.Equal("target", Assert.Single(root.GetProperty("items").EnumerateArray()).GetProperty("id").GetString());
        Assert.Equal(1, root.GetProperty("totalCount").GetInt64()); Assert.True(root.GetProperty("canReadLinks").GetBoolean());
        Assert.False(root.GetProperty("canReadSessions").GetBoolean()); Assert.Equal("/identity/external", root.GetProperty("protocolPath").GetString());
        Assert.Equal("FoundationOwned", root.GetProperty("ownershipMode").GetString());
        foreach (var forbidden in new[] { "password", "securityStamp", "directPermissions", "roleIds", "credentialBinding" }) Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
        using (var denied = await administrator.GetAsync(Management)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        await GrantAdministrator(host, DefaultIdentityPermissionKeys.IdentityLinksRead);
        using var stale = await administrator.GetAsync(Management + "/users"); Assert.Equal(HttpStatusCode.Forbidden, stale.StatusCode);
    }

    [Theory]
    [InlineData("take=101")]
    [InlineData("skip=-1")]
    [InlineData("take=1&take=2")]
    [InlineData("search=a&search=b")]
    public async Task Access_user_lookup_refuses_unbounded_or_ambiguous_queries(string query)
    {
        await using var host = await HttpHost.Start(); using var administrator = host.Browser(); await host.LoginAdministrator(administrator);
        using var denied = await administrator.GetAsync(Management + "/users?" + query); Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
    }
}
